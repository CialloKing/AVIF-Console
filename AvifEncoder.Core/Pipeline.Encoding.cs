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
        /// <summary>
        /// 从 tilePart 字符串（如 "-tile-columns 3 -tile-rows 0"）中提取 tileCols 数值。
        /// 解析失败返回 0。
        /// </summary>
        private static int ParseTileCols(string tilePart)
        {
            if (string.IsNullOrEmpty(tilePart)) return 0;
            var m = System.Text.RegularExpressions.Regex.Match(tilePart, @"tile-columns\s+(\d+)");
            return m.Success && int.TryParse(m.Groups[1].Value, out int v) ? v : 0;
        }

        private static string GetEncodeCacheKey(
            string normalizedPath, int crf, string pixFmt,
            string tilePart, int actualCpu, bool isTrueLossless,
            string aomParams, bool jpeg, int bitDepth,
            int width, int height, string rowMt,
            string encoder, string? encoderCustomParams,
            int denoise, bool arnrUseMaxFrames, string? rgbMode)
        {
            return EncodingFingerprint.ForEncode(
                normalizedPath, crf, pixFmt, ParseTileCols(tilePart),
                actualCpu, isTrueLossless, aomParams, jpeg, bitDepth,
                width, height, rowMt,
                encoder, encoderCustomParams, denoise, arnrUseMaxFrames, rgbMode).ToCacheKey();
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
            string normalizedKey = EncodeHelpers.GetNormalizedPathForCache(input);
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
            // ★ Alpha 检测必须精确匹配子串，不能用 pixFmt.Contains('a')：
            //    "gray"/"gray10le"/"gray12le" 含字母 'a' 但无 Alpha 通道，
            //    误判会导致构建无效格式名传入 ffmpeg。
            bool hasAlpha = pixFmt.Contains("yuva") || pixFmt.Contains("rgba") || pixFmt.Contains("bgra") || pixFmt.Contains("gbra");
            string depthSuffix = pixFmt.EndsWith("12le") ? "12le" : pixFmt.EndsWith("10le") ? "10le" : "";

            // 去掉后缀，得到纯净的基础格式（如 yuv444p 或 yuva444p）
            string baseFmt = (depthSuffix.Length > 0 && pixFmt.Length > 4)
                ? pixFmt.Substring(0, pixFmt.Length - 4) : pixFmt;

            // 分离出色彩采样部分
            if (baseFmt.Contains("444") && !isTrueLossless)
            {
                return hasAlpha
                    ? [$"yuva444p{depthSuffix}", $"yuva422p{depthSuffix}", $"yuva420p{depthSuffix}"]
                    : [$"yuv444p{depthSuffix}", $"yuv422p{depthSuffix}", $"yuv420p{depthSuffix}"];
            }
            if (baseFmt.Contains("422") && !isTrueLossless)
            {
                return hasAlpha
                    ? [$"yuva422p{depthSuffix}", $"yuva420p{depthSuffix}"]
                    : [$"yuv422p{depthSuffix}", $"yuv420p{depthSuffix}"];
            }
            return [pixFmt];
        }

        /// <summary> 构建参数集尝试列表 </summary>
        /// <summary> 构建参数集尝试列表 </summary>
        /// <summary> 构建参数集尝试列表（已优化降级顺序，优先保留 AOM 参数） </summary>
        /// <summary> 构建参数集尝试列表（已优化降级顺序，优先保留 AOM 参数） </summary>
        internal static List<(string aomParams, string tilePart, int actualCpu, string rowMt)> BuildParamSets(
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

                    // 安全 tile
                    int safeTileCols;
                    if (imageWidth > 0 && imageWidth >= 256 && minLegal <= maxLegal)
                        safeTileCols = minLegal;
                    else
                        safeTileCols = 0;

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
            string normalizedInput = EncodeHelpers.GetNormalizedPathForCache(input);
            var (encW, encH) = await GetResolutionAsync(input);

            string cacheKey = GetEncodeCacheKey(normalizedInput, crf, currentPixFmt, param.tilePart,
                                                param.actualCpu, isTrueLossless, param.aomParams,
                                                IsJpeg(input), currentPixFmt.Contains("12le") ? 12 : currentPixFmt.Contains("10le") ? 10 : 8,
                                                encW, encH, param.rowMt,
                                                cfg.Encoder, cfg.EncoderCustomParams,
                                                cfg.Denoise, cfg.ArNrUseMaxFrames, cfg.RgbMode);

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
            _logger.LogSearch($"  [ENCODE] [{fileName}] 等待编码资源 (CRF={crf})...");
            bool slotTaken = false;
            try
            {
                if (!await _ffmpegSlots.WaitAsync(TimeSpan.FromSeconds(300), _globalCts?.Token ?? default))
                {
                    _logger.LogSearch($"[ENCODE] 编码信号量获取超时: {input} CRF={crf}");
                    return (false, TimeSpan.Zero, 0, "编码信号量获取超时", false, null, null);
                }
                slotTaken = true;

                // ★ 随机错开启动时间，避免任务同时开始/同时结束造成 CPU 波峰波谷
                int jitterMs = Random.Shared.Next(0, 2000);          // 0 ~ 2000 毫秒随机抖动
                if (jitterMs > 0)
                    await Task.Delay(jitterMs, _globalCts?.Token ?? default);

                _logger.LogSearch($"  [ENCODE] [{fileName}] 开始编码 (CRF={crf}, pix={currentPixFmt})");

                // ★ 原子重命名：先写到临时文件，成功后再 rename 到最终路径
                string outputDir = Path.GetDirectoryName(output) ?? ".";
                string tmpOutput = Path.Combine(outputDir, $"_tmp_{Guid.NewGuid():N}.avif");

                for (int attempt = 0; attempt <= _maxRetries; attempt++)
                {
                    string ffArgs = await BuildFfmpegArgsAsync(input, tmpOutput, crf, currentPixFmt, param, cfg, isTrueLossless);
                    var sw = Stopwatch.StartNew();
                    (bool success, string stderrLastLine) = await RunFfmpegExAsync(_ffmpegPath, ffArgs,
                        TimeSpan.FromMinutes(timeoutMinutes));
                    sw.Stop();

                    if (success)
                    {
                        if (_fs.GetFileLength(tmpOutput) < 100)
                        {
                            _logger.LogSearch($"编码输出文件过小 ({_fs.GetFileLength(tmpOutput)} 字节)，丢弃并重试");
                            if (_fs.FileExists(tmpOutput)) _fs.DeleteFile(tmpOutput);
                            if (attempt < _maxRetries) { await Task.Delay(1000); continue; }
                            return (false, TimeSpan.Zero, _maxRetries, "编码输出文件过小", false, null, null);
                        }

                        // ★ 原子重命名：File.Move 的第三个参数 overwrite:true 是原子操作，无 Delete+Move 崩溃窗口
                        File.Move(tmpOutput, output, true);

                        // 成功，保存缓存（也用原子写入）
                        _fs.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
                        string tmpCache = cacheFile + ".tmp";
                        _fs.CopyFile(output, tmpCache, true);
                        // ★ 用 overwrite:true 直接原子替换，避免 Delete→Move 之间崩溃导致缓存永久丢失
                        File.Move(tmpCache, cacheFile, true);
                        _cache.SetEncode(cacheKey, cacheFile, sw.Elapsed, ffArgs);
                        _logger.LogSearch($"[OK] 编码成功: {input} CRF={crf} 耗时={sw.Elapsed.TotalSeconds:F4}s");
                        return (true, sw.Elapsed, attempt, "", false, param.aomParams, ffArgs);
                    }

                    string error = $"CRF={crf}, {stderrLastLine}";
                    _logger.LogSearch($"[FAIL] 编码失败: {input} 尝试{attempt + 1}/{_maxRetries + 1} - {error}");

                    // 清理失败输出
                    if (_fs.FileExists(tmpOutput)) _fs.DeleteFile(tmpOutput);

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
            catch (OperationCanceledException)
            {
                throw;  // 用户取消：直接向上传播，不要吞掉
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

            // ★ 仅需 primaries + transfer 即可继承 HDR 色彩，space 缺失时从 primaries 推导
            if (probe.ColorPrimaries == null || probe.ColorTransfer == null)
                return null;
            string space = probe.ColorSpace ?? (probe.ColorPrimaries == "bt2020" ? "bt2020nc" : "bt709");

            return (probe.ColorPrimaries, probe.ColorTransfer, space, probe.ColorRange);
        }



        /// <summary> 构建 ffmpeg 参数字符串 </summary>
        /// <summary> 构建 ffmpeg 参数字符串 </summary>
        private async Task<string> BuildFfmpegArgsAsync(string input, string output, int crf, string pixFmt,
                   (string aomParams, string tilePart, int actualCpu, string rowMt) param,
                   PresetConfig cfg, bool isTrueLossless)
        {
            string logLevel = "-loglevel info -hide_banner";

            var encoder = Av1EncoderFactory.Get(cfg.Encoder);

            // NVENC 硬件编码器 10-bit 映射: yuv*x*p10le → p010le
            string actualPixFmt = pixFmt;
            if (cfg.Encoder.Contains("nvenc", StringComparison.OrdinalIgnoreCase))
            {
                if (actualPixFmt.Contains("12le") || actualPixFmt.Contains("10le") || actualPixFmt.Contains("p010"))
                    actualPixFmt = "p010le";
                else if (actualPixFmt.Contains("444"))
                {
                    actualPixFmt = "yuv420p";  // NVENC 不支持 4:4:4，降为 4:2:0
                    _logger.LogInfo($"[NVENC] 像素格式 4:4:4 已降级为 4:2:0（NVENC 硬件限制）");
                }
            }

            string crfPart = isTrueLossless
                ? encoder.BuildLosslessArg()
                : encoder.BuildQualityArg(crf);

            // -b:v 0 防止 ffmpeg 在质量参数不被识别时静默回退到 bitrate 模式
            string bitrateGuard = "-b:v 0";

            string stillPic = encoder.SupportsStillPicture
                ? "-still-picture 1"
                : "";
            // still-picture 单帧模式下 enable-keyframe-filtering 和 lag-in-frames 无意义，
            // 且 lag-in-frames 在 libaom 3.14+ 中会被拒绝导致编码失败
            // ★ 仅 libaom 存在 -aom-params 重复问题：encoderSpecific 中含 "tune=ssim"，
            //    命令行末尾又有 "-aom-params aq-mode=3:..."，两个 -aom-params 后者覆盖前者。
            //    解法：从 encoderSpecific 剥离 tune 纯值，合并到 aomCombined 冒号列表末尾。
            //    SVT-AV1/Rav1e/Hardware 的 BuildFullTuneArg 返回完整 CLI 段（各自有独立前缀），
            //    不能剥离也不能塞入 -aom-params，必须保持原样。
            string encoderSpecific = EncodeHelpers.BuildEncoderSpecificArgs(cfg, param.actualCpu, param.tilePart, param.rowMt);
            bool isLibAom = cfg.Encoder == "libaom-av1" || cfg.Encoder == "libaom";
            string tuneVal = (cfg.Lossless || !isLibAom) ? "" : Av1EncoderFactory.Get(cfg.Encoder).BuildFullTuneArg(cfg.MetricMode);
            if (!string.IsNullOrEmpty(tuneVal))
                encoderSpecific = encoderSpecific.Replace(tuneVal, "").Replace("  ", " ").Trim();
            string aomCombined;
            if (string.IsNullOrEmpty(param.aomParams))
                aomCombined = tuneVal.Length > 0 ? $"-aom-params {tuneVal}" : "";
            else
                aomCombined = tuneVal.Length > 0
                    ? $"-aom-params {param.aomParams}:{tuneVal}"  // tune 追加到已有参数末尾
                    : $"-aom-params {param.aomParams}";
            string threadsArg = cfg.SerialEncode ? "-threads 1" : "";

            // ---------- 默认 SDR sRGB（全范围），根据像素格式选择矩阵 ----------
            string primaries = "bt709";
            string trc = "iec61966-2-1";
            // RGB planar 输出使用 rgb 矩阵，YUV 输出使用 bt709
            bool isRgbOutput = pixFmt.StartsWith("gbr", StringComparison.OrdinalIgnoreCase);
            string space = isRgbOutput ? "rgb" : "bt709";
            string rangeVal = "pc";

            // ---------- 探测源文件色彩元数据 ----------
            // 源文件有完整色彩元数据时，全部忠实继承；
            // 仅 bt2020 primaries 时强制使用 bt2020nc 矩阵（HDR10 标准）。
            var srcColor = await GetSourceColorInfoAsync(input);
            if (srcColor != null)
            {
                var p = srcColor.Value.primaries;
                var t = srcColor.Value.trc;
                var s = srcColor.Value.space;

                primaries = p;
                trc = t;
                // bt2020 色域统一使用 bt2020nc 矩阵
                if (p == "bt2020")
                {
                    space = "bt2020nc";
                }
                else
                {
                    // ★ 归一化：ffprobe 输出 "gbr" 但 ffmpeg -colorspace 只接受 "rgb"
                    //    YUV 输出时源文件 RGB 矩阵无意义，使用输出格式对应的默认矩阵
                    space = s switch
                    {
                        "gbr" => isRgbOutput ? "rgb" : "bt709",
                        _ => s
                    };
                }

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

            // ── 用户自定义编码器参数（含完整 CLI 前缀） ──
            string customPart = cfg.EncoderCustomParams ?? "";

            // ── 动图模板：用户自定义完整命令 ──
            if (_isAnimatedFile.Value && !string.IsNullOrEmpty(cfg.AnimatedCommand))
            {
                // filter_complex 已提取 Alpha 到独立流，颜色流去掉 'a' 避免 libaom 拒绝
                string templatePixFmt = actualPixFmt.Replace("a", "");
                string cmd = cfg.AnimatedCommand
                    .Replace("{PIXFMT}", templatePixFmt)
                    .Replace("{CRF}", crf.ToString())
                    .Replace("{COLORMETA}", colorMeta)
                    .Replace("INPUT.gif", EncodeHelpers.EscapeArg(input))
                    .Replace("INPUT.GIF", EncodeHelpers.EscapeArg(input))
                    .Replace("OUTPUT.avif", EncodeHelpers.EscapeArg(output))
                    .Replace("OUTPUT.AVIF", EncodeHelpers.EscapeArg(output));
                // ★ 模板以 "ffmpeg" 开头（用于预览展示），编码时去掉（args 不含二进制路径）
                if (cmd.StartsWith("ffmpeg ", StringComparison.OrdinalIgnoreCase))
                    cmd = cmd.Substring(7);
                _logger.LogInfo($"[ANIM] 使用动图模板命令: {cmd}");
                return cmd;
            }

            // ── 动图 + Alpha：filter_complex 双流映射 ──
            bool hasAlpha = actualPixFmt.Contains("yuva") || actualPixFmt.Contains("rgba") || actualPixFmt.Contains("bgra") || actualPixFmt.StartsWith("gbra", StringComparison.OrdinalIgnoreCase);
            if (_isAnimatedFile.Value && hasAlpha && !encoder.SupportsStillPicture)
            {
                _logger.LogInfo($"[WARN] 编码器 {cfg.Encoder} 不支持动图+Alpha 双流映射，Alpha 通道将被忽略");
            }
            if (_isAnimatedFile.Value && hasAlpha && encoder.SupportsStillPicture)
            {
                // ★ 动态位深：从格式名末尾提取（yuva444p10le → 10），避免匹配到色度数字
                var bdMatch = System.Text.RegularExpressions.Regex.Match(actualPixFmt, @"p(\d+)le$");
                int animBitDepth = bdMatch.Success && int.TryParse(bdMatch.Groups[1].Value, out int bd) ? Math.Min(bd, 12) : 10;
                string yuvaFmt = $"yuva444p{animBitDepth}le";
                string yuvCleanFmt = $"yuv444p{animBitDepth}le";
                string grayFmt = $"gray{animBitDepth}le";

                // ★ [c] 流显式剥离 Alpha → [clean]，避免编码器接收含 Alpha 的帧
                string filterComplex = $"-filter_complex \"[0:v]format={yuvaFmt},split=2[c][a];[c]format={yuvCleanFmt}[clean];[a]alphaextract[alpha]\"";
                string colorMap = $"-map \"[clean]\" -c:v:0 {cfg.Encoder} -pix_fmt {yuvCleanFmt}";
                string alphaMap = $"-map \"[alpha]\" -c:v:1 {cfg.Encoder} -pix_fmt {grayFmt}";
                return $"{logLevel} -i \"{EncodeHelpers.EscapeArg(input)}\" " +
                       $"{filterComplex} " +
                       $"{colorMap} {rangeArg} {colorMeta} " +
                       $"{crfPart} {bitrateGuard} {encoderSpecific} " +
                       $"{alphaMap} " +
                       $"-vsync vfr {aomCombined} {customPart} {threadsArg} -y \"{EncodeHelpers.EscapeArg(output)}\"";
            }

            // ── 动图：去掉单帧限制 ──
            string framesPart = "-frames:v 1";
            string animPart = "";
            if (_isAnimatedFile.Value)
            {
                stillPic = "";           // 动图不用 still-picture
                framesPart = "";         // 动图不限制帧数
                animPart = "-vsync vfr"; // 保留原始帧率
                _logger.LogInfo($"[ANIM] 硬编码动图命令 (isAnimated={_isAnimatedFile.Value})");
            }

            return $"{logLevel} -i \"{EncodeHelpers.EscapeArg(input)}\" " +
                   $"-c:v {cfg.Encoder} -pix_fmt {actualPixFmt} {rangeArg} {colorMeta} " +
                   $"{crfPart} {bitrateGuard} {encoderSpecific} " +
                   $"{stillPic} {framesPart} {animPart} {aomCombined} {customPart} {threadsArg} -y \"{EncodeHelpers.EscapeArg(output)}\"";
        }

        private static string CsvEscape(string field) => EncodeHelpers.CsvEscape(field);

        private async Task<(bool success, string stderrLastLine)> RunFfmpegExAsync(string file, string args, TimeSpan timeout)
        {
            var (exitCode, stdout, stderr) = await _processRunner.RunAsync(
                file, args, timeout, _globalCts?.Token ?? default);

            // 记录完整 stderr
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                _logger.LogInfo($"ffmpeg stderr:\n{stderr.Trim()}");
            }

            // 防呆：检测磁盘空间不足，取消所有后续任务（英文+中文 locale）
            if (stderr.Contains("No space left on device") ||
                stderr.Contains("Disk full") || stderr.Contains("disk full") ||
                stderr.Contains("磁盘空间不足") || stderr.Contains("not enough space"))
            {
                SafeWriteLine("[FATAL] 磁盘空间不足，正在取消所有待处理任务...");
                _logger.LogError("磁盘空间不足，终止编码");
                _globalCts?.Cancel();
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
