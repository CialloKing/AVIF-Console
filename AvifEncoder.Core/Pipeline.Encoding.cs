using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;   // 如果使用 System.Text.Json
using System.Text.RegularExpressions;



namespace AvifEncoder
{
    partial class AvifPipeline
    {
        /// <summary>
        /// 生成用于编码缓存的一致键，确保所有缓存访问使用相同格式。
        /// </summary>
        /// <summary>
        /// 生成用于编码缓存的一致键，确保所有缓存访问使用相同格式。
        /// </summary>
        private static string GetEncodeCacheKey(
            string normalizedPath, int crf, string pixFmt,
            string tilePart, int actualCpu, bool isTrueLossless,
            string aomParams, bool jpeg, int bitDepth,
            int width = 0, int height = 0, string rowMt = "")   // ★ 新增 rowMt 参数
        {
            string res = (width > 0 && height > 0) ? $"|res={width}x{height}" : "";
            return $"{normalizedPath}|crf={crf}|pix={pixFmt}" +
                   $"|tile={tilePart}|cpu={actualCpu}|lossless={isTrueLossless}" +
                   $"|aom={aomParams}|jpeg={jpeg}|depth={bitDepth}{res}|rowmt={rowMt}";
        }







        // ── 编码方法（带错误详情，并自动降级像素格式） ──
        // ========== 修复后的 EncodeToFileExAsync（信号量超时） ==========
        // ========== EncodeToFileExAsync 主体 ==========
        private async Task<(bool ok, TimeSpan t, int retries, string error, bool fromCache,
                    string? actualAomParams, string? commandLine)>
EncodeToFileExAsync(string input, string output, int crf, int tileCols, int cpuUsed, PresetConfig cfg,
                    bool jpeg, string pixFmt, bool isTrueLossless, int timeoutMinutes,
                    bool allowParamDegrade = true)
        {
            string[] pixFmtsToTry = GetPixelFormatFallbackList(pixFmt, isTrueLossless);
            string lastError = "所有像素格式尝试均失败";
            string fileName = Path.GetFileName(input);
            string normalizedKey = GetNormalizedPathForCache(input);
            var fatalSet = _fatalFmts.GetOrAdd(normalizedKey, _ => new ConcurrentDictionary<string, byte>());
            foreach (var currentPixFmt in pixFmtsToTry)
            {
                // 若该格式之前已被标记为“无法生成任何输出”，直接跳过
                if (fatalSet.ContainsKey(currentPixFmt))
                {
                    _logger.LogInfo($"致命格式 {currentPixFmt} 已禁用，跳过 [{fileName}]");
                    continue;
                }

                var result = await TryEncodeWithPixelFormatFallback(
                        input, output, crf, tileCols, cpuUsed, cfg, jpeg, currentPixFmt, isTrueLossless,
                        timeoutMinutes, allowParamDegrade, fileName);

                if (result.ok)
                    return result;

                lastError = result.error ?? "未知错误";

                // 在 EncodeToFileExAsync 的循环内，替换原有的致命标记逻辑：
                // 在编码结果处理中：
                if (result.error?.StartsWith("FATAL_NOTHING:", StringComparison.OrdinalIgnoreCase) == true)
                {
                    // 只有所有参数集都 Nothing 才标记
                    fatalSet.TryAdd(currentPixFmt, 0);
                    _logger.LogInfo($"致命格式 {currentPixFmt} 已记录 [{fileName}]，将不再重试");
                }
                // 原有的降级日志保留

                // 仅当还有后续格式时才输出降级日志
                if (currentPixFmt != pixFmtsToTry.Last())
                {
                    string nextFmt = pixFmtsToTry[Array.IndexOf(pixFmtsToTry, currentPixFmt) + 1];
                    if (!fatalSet.ContainsKey(nextFmt))
                        _logger.LogInfo($"像素格式 {currentPixFmt} 编码失败，降级尝试 {nextFmt} ...");
                }
            }

            string chainDesc = string.Join(" → ", pixFmtsToTry);
            _logger.LogInfo($"编码失败 [CRF={crf}] [{fileName}] 尝试序列: {chainDesc}。最后错误: {lastError}");
            return (false, TimeSpan.Zero, _maxRetries, $"编码失败 [序列: {chainDesc}] {lastError}", false, null, null);
        }

        private async Task<(bool ok, TimeSpan t, int retries, string error, bool fromCache,
    string? actualAomParams, string? commandLine)>
TryEncodeWithPixelFormatFallback(string input, string output, int crf, int tileCols, int cpuUsed,
    PresetConfig cfg, bool jpeg, string currentPixFmt, bool isTrueLossless, int timeoutMinutes,
    bool allowParamDegrade, string fileName)
        {
            // 获取图片宽度（只取宽度，高度不需要）
            var (w, _) = await GetResolutionAsync(input);
            var paramSets = BuildParamSets(cfg, currentPixFmt, isTrueLossless, tileCols, cpuUsed,
                                           allowParamDegrade, w);   // 传入宽度

            string lastError = "";
            bool allNothingWritten = true;

            foreach (var param in paramSets)
            {
                var result = await TryEncodeWithParamSet(input, output, crf, currentPixFmt, param, cfg,
                                                         isTrueLossless, timeoutMinutes, fileName);
                if (result.ok)
                    return result;

                lastError = result.error ?? "未知错误";
                if (!lastError.Contains("Nothing was written", StringComparison.OrdinalIgnoreCase))
                    allNothingWritten = false;
            }

            if (allNothingWritten)
                return (false, TimeSpan.Zero, _maxRetries, $"FATAL_NOTHING:{lastError}", false, null, null);

            return (false, TimeSpan.Zero, _maxRetries, $"像素格式 {currentPixFmt} 所有参数均失败", false, null, null);
        }

        // ---------- 辅助函数 ----------

        /// <summary> 获取像素格式降级顺序列表 </summary>
        private static string[] GetPixelFormatFallbackList(string pixFmt, bool isTrueLossless)
        {
            // 提取 Alpha 标记和位深后缀
            bool hasAlpha = pixFmt.Contains('a');
            string depthSuffix = pixFmt.EndsWith("10le") ? "10le" : "";

            // 去掉后缀，得到纯净的基础格式（如 yuv444p 或 yuva444p）
            string baseFmt = depthSuffix.Length > 0 ? pixFmt.Substring(0, pixFmt.Length - 4) : pixFmt;

            // 分离出色彩采样部分
            if (baseFmt.Contains("444") && !isTrueLossless)
            {
                return hasAlpha
                    ? new[] { $"yuva444p{depthSuffix}", $"yuva422p{depthSuffix}", $"yuva420p{depthSuffix}" }
                    : new[] { $"yuv444p{depthSuffix}", $"yuv422p{depthSuffix}", $"yuv420p{depthSuffix}" };
            }
            if (baseFmt.Contains("422") && !isTrueLossless)
            {
                return hasAlpha
                    ? new[] { $"yuva422p{depthSuffix}", $"yuva420p{depthSuffix}" }
                    : new[] { $"yuv422p{depthSuffix}", $"yuv420p{depthSuffix}" };
            }
            return new[] { pixFmt };
        }

        /// <summary> 构建参数集尝试列表 </summary>
        /// <summary> 构建参数集尝试列表 </summary>
        /// <summary> 构建参数集尝试列表（已优化降级顺序，优先保留 AOM 参数） </summary>
        /// <summary> 构建参数集尝试列表（已优化降级顺序，优先保留 AOM 参数） </summary>
        private static List<(string aomParams, string tilePart, int actualCpu, string rowMt)> BuildParamSets(
    PresetConfig cfg, string currentPixFmt, bool isTrueLossless, int tileCols, int cpuUsed,
    bool allowParamDegrade, int imageWidth)
        {
            string effectiveAom = cfg.GetEffectiveAomParams();
            var sets = new List<(string, string, int, string)>();
            bool isHighChroma = currentPixFmt.Contains("444") || currentPixFmt.Contains("422");
            string rowMt;

            var enc = Av1EncoderFactory.Get(cfg.Encoder);

            // ===== 极限压缩：强制关闭所有并行 =====
            if (cfg.SerialEncode)
            {
                tileCols = GetMinLegalTileCols(imageWidth);
                rowMt = enc.SupportsRowMt ? "-row-mt 0" : "";
            }
            else
            {
                rowMt = enc.SupportsRowMt ? "-row-mt 1" : "";
            }
            // =====================================

            // 合法性约束（图像宽度限制）
            int minLegal = GetMinLegalTileCols(imageWidth);
            int maxLegal = GetMaxLegalTileCols(imageWidth);
            int legalTileCols = Math.Clamp(tileCols, minLegal, maxLegal);

            if (!isTrueLossless && isHighChroma)
            {
                sets.Add((effectiveAom, TilePart(legalTileCols, false), cpuUsed, rowMt));
                if (allowParamDegrade)
                {
                    sets.Add((effectiveAom, TilePart(legalTileCols, false), 0, rowMt));

                    string tilePart = enc.SupportsTiles ? $"-tile-columns {legalTileCols} -tile-rows 0" : "";
                    sets.Add(("", tilePart, 0, rowMt));

                    // 安全 tile（强制单线程时同样归零）
                    // 安全 tile（强制单线程时同样归零）
                    int safeTileCols;
                    if (imageWidth > 0 && imageWidth >= 256 && minLegal <= maxLegal)
                        safeTileCols = minLegal;   // minLegal 已确保 tile 宽度 ≤ 4096，无需额外强制 ≥2
                    else
                        safeTileCols = 0;
                    if (cfg.SerialEncode) safeTileCols = 0;   // ← 新属性名

                    string safeTilePart = safeTileCols > 0
                            ? $"-tile-columns {safeTileCols} -tile-rows 0"
                            : "-tile-columns 0 -tile-rows 0";
                    sets.Add(("", safeTilePart, 0, rowMt));
                }
            }
            else
            {
                sets.Add((effectiveAom, TilePart(legalTileCols, isTrueLossless), cpuUsed, rowMt));
            }

            return sets;
        }

        private async Task<(bool ok, TimeSpan t, int retries, string error, bool fromCache,
    string? actualAomParams, string? commandLine)>
TryEncodeWithParamSet(string input, string output, int crf, string currentPixFmt,
                      (string aomParams, string tilePart, int actualCpu, string rowMt) param,
                      PresetConfig cfg, bool isTrueLossless, int timeoutMinutes, string fileName)
        {
            string normalizedInput = GetNormalizedPathForCache(input);
            var (encW, encH) = await GetResolutionAsync(input);

            string cacheKey = GetEncodeCacheKey(normalizedInput, crf, currentPixFmt, param.tilePart,
                                                param.actualCpu, isTrueLossless, param.aomParams,
                                                IsJpeg(input), currentPixFmt.Contains("10le") ? 10 : 8,
                                                encW, encH, param.rowMt);   // ★ 新增 param.rowMt

            string cacheFile = Path.Combine(_outputDir, "_enc_cache", $"{EncodeHelpers.Sha256(cacheKey)}.avif");

            // 缓存命中
            if (_cache.TryGetEncode(cacheKey, out var cached) && File.Exists(cached.file))
            {
                _fs.CreateDirectory(Path.GetDirectoryName(output)!);
                _fs.CopyFile(cached.file!, output, true);
                _logger.LogInfo($"复用编码缓存: {input} CRF={crf} pix={currentPixFmt} 原耗时={cached.encodeTime.TotalSeconds:F4}s");
                return (true, cached.encodeTime, 0, "", true, param.aomParams, cached.commandLine);
            }

            // 执行编码重试
            return await ExecuteEncodingWithRetries(input, output, crf, currentPixFmt, param, cfg,
                                                    isTrueLossless, timeoutMinutes, fileName, cacheKey, cacheFile);
        }


        private async Task<(bool ok, TimeSpan t, int retries, string error, bool fromCache,
    string? actualAomParams, string? commandLine)>
    ExecuteEncodingWithRetries(string input, string output, int crf, string currentPixFmt,
                           (string aomParams, string tilePart, int actualCpu, string rowMt) param,
                           PresetConfig cfg, bool isTrueLossless, int timeoutMinutes, string fileName,
                           string cacheKey, string cacheFile)
        {
            _logger.LogSearch($"  ? [{fileName}] 等待编码资源 (CRF={crf})...");
            bool slotTaken = false;
            try
            {
                if (!await _ffmpegSlots.WaitAsync(TimeSpan.FromSeconds(300), _globalCts?.Token ?? default))
                {
                    _logger.LogSearch($"? 编码信号量获取超时: {input} CRF={crf}");
                    return (false, TimeSpan.Zero, 0, "编码信号量获取超时", false, null, null);
                }
                slotTaken = true;

                // ★ 随机错开启动时间，避免任务同时开始/同时结束造成 CPU 波峰波谷
                int jitterMs = Random.Shared.Next(0, 2000);          // 0 ~ 2000 毫秒随机抖动
                if (jitterMs > 0)
                    await Task.Delay(jitterMs, _globalCts?.Token ?? default);

                _logger.LogSearch($"  ? [{fileName}] 开始编码 (CRF={crf}, pix={currentPixFmt})");

                for (int attempt = 0; attempt <= _maxRetries; attempt++)
                {
                    string ffArgs = await BuildFfmpegArgsAsync(input, output, crf, currentPixFmt, param, cfg, isTrueLossless);
                    // ... 后续编码逻辑保持不变 ...
                    var sw = Stopwatch.StartNew();
                    (bool success, string stderrLastLine) = await RunFfmpegExAsync(_ffmpegPath, ffArgs,
                        TimeSpan.FromMinutes(timeoutMinutes));
                    sw.Stop();

                    if (success)
                    {
                        if (_fs.GetFileLength(output) < 100)
                        {
                            _logger.LogSearch($"编码输出文件过小 ({_fs.GetFileLength(output)} 字节)，丢弃并重试");
                            if (_fs.FileExists(output)) _fs.DeleteFile(output);
                            if (attempt < _maxRetries) { await Task.Delay(1000); continue; }
                            return (false, TimeSpan.Zero, _maxRetries, "编码输出文件过小", false, null, null);
                        }

                        // 成功，保存缓存
                        _fs.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
                        _fs.CopyFile(output, cacheFile, true);
                        _cache.SetEncode(cacheKey, cacheFile, sw.Elapsed, ffArgs);
                        _logger.LogSearch($"? 编码成功: {input} CRF={crf} 耗时={sw.Elapsed.TotalSeconds:F4}s");
                        return (true, sw.Elapsed, attempt, "", false, param.aomParams, ffArgs);
                    }

                    string error = $"CRF={crf}, {stderrLastLine}";
                    _logger.LogSearch($"? 编码失败: {input} 尝试{attempt + 1}/{_maxRetries + 1} - {error}");

                    // 清理失败输出
                    if (_fs.FileExists(output)) _fs.DeleteFile(output);

                    // 致命错误：立即停止重试
                    if (stderrLastLine.Contains("Nothing was written", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogSearch($"检测到致命错误，放弃重试: {input} CRF={crf}");
                        return (false, TimeSpan.Zero, attempt, error, false, null, null);
                    }

                    if (attempt < _maxRetries) await Task.Delay(1000);
                }

                return (false, TimeSpan.Zero, _maxRetries, $"CRF={crf}, 重试耗尽", false, null, null);
            }
            catch (Exception ex)
            {
                _logger.LogError($"编码异常: {input} - {ex.Message}");
                return (false, TimeSpan.Zero, _maxRetries, $"异常: {ex.Message}", false, null, null);
            }
            finally
            {
                if (slotTaken) _ffmpegSlots.Release();
            }
        }



        /// <summary>
        /// 通过 ffprobe (JSON 模式) 探测输入文件的色彩元数据。
        /// 如果任何核心字段缺失、为 unknown/reserved，则返回 null，避免半继承。
        /// </summary>
        private async Task<(string primaries, string trc, string space, string? range)?>
        GetSourceColorInfoAsync(string inputPath)
        {
            var probe = await GetProbeInfoAsync(inputPath);
            if (probe == null) return null;

            // 任何核心色彩字段缺失则返回 null
            if (probe.ColorPrimaries == null || probe.ColorTransfer == null || probe.ColorSpace == null)
                return null;

            return (probe.ColorPrimaries, probe.ColorTransfer, probe.ColorSpace, probe.ColorRange);
        }



        /// <summary> 构建 ffmpeg 参数字符串 </summary>
        /// <summary> 构建 ffmpeg 参数字符串 </summary>
        private async Task<string> BuildFfmpegArgsAsync(string input, string output, int crf, string pixFmt,
                   (string aomParams, string tilePart, int actualCpu, string rowMt) param,
                   PresetConfig cfg, bool isTrueLossless)
        {
            string logLevel = "-loglevel info -hide_banner";
            string aom = string.IsNullOrEmpty(param.aomParams) ? "" : $"-aom-params {param.aomParams}";

            var encoder = Av1EncoderFactory.Get(cfg.Encoder);

            string crfPart = isTrueLossless
                ? encoder.BuildLosslessArg()
                : $"-crf {crf}";

            string stillPic = encoder.SupportsStillPicture
                ? "-still-picture 1 -aom-params enable-keyframe-filtering=0:lag-in-frames=0"
                : "";
            string encoderSpecific = BuildEncoderSpecificArgs(cfg, param.actualCpu, param.tilePart, param.rowMt);
            string threadsArg = cfg.SerialEncode ? "-threads 1" : "";

            // ---------- 默认 SDR sRGB（全范围），根据像素格式选择矩阵 ----------
            string primaries = "bt709";
            string trc = "iec61966-2-1";
            // 仅当像素格式为 gbr*（planar RGB）时使用 identity matrix，
            // 其他 YUV 格式（yuv420p, yuv444p 等）使用 bt709。
            string space = pixFmt.StartsWith("gbr", StringComparison.OrdinalIgnoreCase)
                                   ? "gbr"
                                   : "bt709";
            string rangeVal = "pc";

            // ---------- 探测源文件色彩元数据 ----------
            var srcColor = await GetSourceColorInfoAsync(input);
            if (srcColor != null)
            {
                var p = srcColor.Value.primaries;
                var t = srcColor.Value.trc;

                bool isHdrPq = p == "bt2020" && t == "smpte2084";   // 仅允许 PQ HDR

                if (isHdrPq)
                {
                    primaries = "bt2020";
                    trc = "smpte2084";
                    space = "bt2020nc";          // HDR10 标准矩阵
                }
                // 其他组合保留默认 space（已按像素格式选择 bt709 或 gbr）

                // range 始终允许继承
                if (!string.IsNullOrWhiteSpace(srcColor.Value.range))
                    rangeVal = srcColor.Value.range;
            }

            // range 映射
            string rangeArg = rangeVal.ToLowerInvariant() switch
            {
                "tv" or "mpeg" => "-color_range tv",
                _ => "-color_range pc"
            };

            string colorMeta = $"-color_primaries {primaries} -color_trc {trc} -colorspace {space}";

            return $"{logLevel} -i \"{input}\" " +
                   $"-c:v {cfg.Encoder} -pix_fmt {pixFmt} {rangeArg} {colorMeta} " +
                   $"{crfPart} {encoderSpecific} " +
                   $"{stillPic} -frames:v 1 {aom} {threadsArg} -y \"{output}\"";
        }

        private static string CsvEscape(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
                return $"\"{field.Replace("\"", "\"\"")}\"";
            return field;
        }

        private async Task<(bool success, string stderrLastLine)> RunFfmpegExAsync(string file, string args, TimeSpan timeout)
        {
            var (exitCode, stdout, stderr) = await _processRunner.RunAsync(
                file, args, timeout, _globalCts?.Token ?? default);

            // 记录完整 stderr
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                _logger.LogInfo($"ffmpeg stderr:\n{stderr.Trim()}");
            }

            string lastLine = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "";

            if (exitCode != 0)
            {
                _logger.LogError($"ffmpeg 错误(退出码 {exitCode}): {lastLine}");
                return (false, lastLine);
            }
            return (true, "");
        }
    }
}
