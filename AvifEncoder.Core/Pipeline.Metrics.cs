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



        // ── 核心 SSIM 计算（已整合色彩空间归一化 + 时间轴同步） ──
        private async Task<double> SSIMDirect(string a, string b, string? targetPixFmt = null)
        {
            if (!EnsureFilesValid(a, b)) return -1;

            string alignFmt = PrepareAlignFormat(targetPixFmt);

            try
            {
                var (w1, h1) = await GetResolutionAsync(a).WaitAsync(TimeSpan.FromSeconds(30));
                var (w2, h2) = await GetResolutionAsync(b).WaitAsync(TimeSpan.FromSeconds(30));

                // ★ 修复：任意一边分辨率无效则立即返回 -1
                if (w1 <= 0 || h1 <= 0 || w2 <= 0 || h2 <= 0)
                {
                    _logger.LogInfo($"SSIM 分辨率无效: a={Path.GetFileName(a)} ({w1}x{h1}), b={Path.GetFileName(b)} ({w2}x{h2})");
                    return -1;
                }

                string args = BuildSsimArgs(a, b, alignFmt, w1, h1, w2, h2);
                string output = await RunSsimProcess(args);
                return ParseSsimOutput(output);
            }
            catch (Exception ex)
            {
                _logger.LogInfo($"SSIM 异常: {Path.GetFileName(a)} vs {Path.GetFileName(b)} - {ex.Message}");
                SafeWriteLine($" [FAIL] SSIM 异常: {ex.Message}");
                return -1;
            }
        }

        private bool EnsureFilesValid(string a, string b)
        {
            if (!_fs.FileExists(a) || !_fs.FileExists(b))
            {
                _logger.LogInfo($"SSIM 文件缺失: a={Path.GetFileName(a)}, b={Path.GetFileName(b)}");
                return false;
            }

            long sizeA = _fs.GetFileLength(a);
            long sizeB = _fs.GetFileLength(b);
            if (sizeA < 100 || sizeB < 100)
            {
                _logger.LogInfo($"SSIM 文件太小 ({sizeA} / {sizeB} 字节)");
                return false;
            }
            return true;
        }


        private static string PrepareAlignFormat(string? targetPixFmt)
        {
            string alignFmt = targetPixFmt ?? "yuv420p";
            return alignFmt.Replace("a", ""); // 移除 Alpha 标记
        }


        private static string BuildSsimArgs(string a, string b, string alignFmt,
                                    int w1, int h1, int w2, int h2)
        {
            if (w1 > 0 && h1 > 0 && w2 > 0 && h2 > 0 && (w1 != w2 || h1 != h2))
            {
                int w = Math.Min(w1, w2);
                int h = Math.Min(h1, h2);
                string scaleFilter = $"[0:v]scale={w}:{h}[ref];[1:v]scale={w}:{h}[dist];[ref][dist]ssim";
                return $"-loglevel info -hide_banner -i \"{a}\" -i \"{b}\" " +
                       $"-filter_complex \"{scaleFilter}\" -frames:v 1 -f null -";
            }
            else
            {
                return $"-loglevel info -hide_banner " +
                       $"-i \"{a}\" -i \"{b}\" " +
                       $"-filter_complex \"[0:v]format={alignFmt}[ref];[1:v]format={alignFmt}[dist];[ref][dist]ssim\" " +
                       $"-frames:v 1 -f null -";
            }
        }




        private async Task<string> RunSsimProcess(string args)
        {
            var (exitCode, stdout, stderr) = await _processRunner.RunAsync(
                _ffmpegPath, args, TimeSpan.FromMinutes(_config.SsimTimeoutMinutes), _globalCts?.Token ?? default);

            // 与之前行为一致，将 stdout 和 stderr 合并返回
            return stdout + stderr;
        }

        private double ParseSsimOutput(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                LogSsimParseFailure("输出为空");
                return -1;
            }

            _logger.LogInfo($"SSIM output:\n{output}");

            // 匹配 "All:0.xxxx" （容错空格）
            var m = Regex.Match(output, @"All:\s*([0-9.]+)");
            if (m.Success &&
                double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double ssim))
            {
                return ssim;
            }

            // 备选：某些版本输出 "SSIM All:"
            m = Regex.Match(output, @"SSIM\s+All:\s*([0-9.]+)");
            if (m.Success &&
                double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out ssim))
            {
                return ssim;
            }

            LogSsimParseFailure(output.Length > 500 ? output[^500..] : output);
            return -1;
        }

        private void LogSsimParseFailure(string tail)
        {
            SafeWriteLine($" [WARN] SSIM 解析失败");
            SafeWriteLine($"  ffmpeg 尾部: {tail}");
            _logger.LogInfo($"SSIM 解析失败: tail:\n{tail}");
        }


        // CSV 列名常量，修改这里即可同步表头和数据行
        private static readonly string[] CsvColumnNames =
[
    "文件名", "原始文件名", "原始大小", "输出大小", "压缩率",
    "CRF", "SSIM", "VMAF", "PSNR-Y", "MS-SSIM", "MixScore",
    "XPSNR-Y", "XPSNR-U", "XPSNR-V", "W-XPSNR",
    "SSIMULACRA2", "Butteraugli_Raw", "Butteraugli_3norm", "GMSD",
    // "CAMBI", "ADM",  // 暂不可用
    "编码耗时(秒)", "搜索耗时(秒)", "总耗时(秒)", "重试次数",
    "像素格式", "源像素格式", "模式", "安全模式",
    "AOM参数", "完整命令行",
    "缓存复用", "状态", "失败原因",
    "搜索评估次数"
];
        /// <summary>
        /// 生成用于 SSIM 缓存的一致键，确保所有缓存访问使用相同格式。
        /// </summary>
        private static string GetSsimCacheKey(
    string normalizedPath, int crf, string pixFmt,
    int tileCols, int cpuUsed, bool isJpeg,
    string effectiveAomParams, int bitDepth,
    int width = 0, int height = 0,
    string rowMt = "")                         // ← 新增
        {
            string res = (width > 0 && height > 0) ? $"|res={width}x{height}" : "";
            return $"{normalizedPath}|crf={crf}|pix={pixFmt}|tile={tileCols}|cpu={cpuUsed}" +
                   $"|jpeg={isJpeg}|aom={effectiveAomParams}|depth={bitDepth}{res}|rowmt={rowMt}";
        }






        /// <summary> 计算原始图像与编码后 AVIF 的 SSIM </summary>
        /// <param name="orig">原始图片路径</param>
        /// <param name="enc">编码后的 AVIF 路径</param>
        /// <param name="pixFmt">像素格式（可能含 Alpha），将被清洗后用于 SSIM 计算</param>
        private async Task<double> CalcSSIMAsync(string orig, string enc, string? pixFmt = null)
        {
            // 移除 Alpha 标记，确保 SSIM 只比较颜色通道
            string? cleanFmt = pixFmt?.Replace("a", "");
            return await SSIMDirect(orig, enc, cleanFmt);
        }






        /// <summary>
        /// 获取或计算给定编码参数下的多指标。
        /// 使用与 SSIM 缓存相同的键，以便未来统一。
        /// </summary>
        private async Task<QualityMetrics?> GetOrComputeMetrics(
    string input, int crf, int tileCols, int cpuUsed, PresetConfig cfg, bool jpeg, string pixFmt)
        {
            if (cfg.Lossless)
                return new QualityMetrics { SSIM = 1.0, PSNR_Y = 100.0, MS_SSIM = 1.0, VMAF = 100.0 };

            int actualDepth = pixFmt.Contains("10le") ? 10 : 8;
            string normalizedInput = GetNormalizedPathForCache(input);
            string effectiveAom = cfg.GetEffectiveAomParams();

            string metricMode = cfg.MetricMode ?? "vmaf";

            var (metricsW, metricsH) = await GetResolutionAsync(input);
            string rowMtArg = EncodeHelpers.GetRowMtArg(cfg);
            string key = GetSsimCacheKey(normalizedInput, crf, pixFmt, tileCols, cpuUsed, jpeg,
                                         effectiveAom, actualDepth, metricsW, metricsH, rowMtArg)
                         + $"|metric={metricMode}";

            if (_cache.TryGetMetrics(key, out QualityMetrics? cached))
            {
                _logger.LogSearch($"指标缓存命中: CRF={crf} [{Path.GetFileName(input)}] VMAF={cached!.VMAF:F4}");
                return cached!;
            }

            var newTask = new TaskCompletionSource<QualityMetrics?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = _metricsTasks.GetOrAdd(key, newTask.Task);
            bool isOwner = task == newTask.Task;
            if (!isOwner)
            {
                try { return await task.WaitAsync(TimeSpan.FromMinutes(30)); }
                catch { return null; }
            }

            try
            {
                if (!await _ssimConcurrency.WaitAsync(TimeSpan.FromSeconds(300), _globalCts?.Token ?? default))
                {
                    _logger.LogError($"GetOrComputeMetrics 信号量等待超时 (300s)，可能资源耗尽。文件: {Path.GetFileName(input)}, CRF={crf}");
                    newTask.SetResult(null);
                    return null;
                }

                try
                {
                    string tmp = Path.Combine(_outputDir, $"_p_{Guid.NewGuid():N}.avif");
                    try
                    {
                        int searchCpu = Math.Min(cpuUsed + 2, 8);
                        var encResult = await EncodeToFileExAsync(input, tmp, crf, tileCols, searchCpu, cfg, jpeg, pixFmt,
                            isTrueLossless: false, cfg.SearchEncodeTimeoutMinutes, allowParamDegrade: true);

                        if (!encResult.ok || !_fs.FileExists(tmp) || _fs.GetFileLength(tmp) < 100)
                        {
                            _logger.LogSearch($"临时编码失败: CRF={crf} [{Path.GetFileName(input)}]");
                            newTask.SetResult(null);
                            return null;
                        }

                        // ★ 按需计算：搜索阶段只算目标指标，跳过无关项
                        QualityMetrics? metrics = null;

                        if (metricMode == "ssim")
                        {
                            double s = await SSIMDirect(input, tmp);
                            if (s >= 0)
                                metrics = new QualityMetrics { SSIM = s };
                        }
                        else if (metricMode == "psnr")
                        {
                            var psnr = await ComputePsnrUncappedAsync(input, tmp);
                            if (psnr.HasValue)
                                metrics = new QualityMetrics { PSNR_Y = psnr.Value, SSIM = -1 };
                        }
                        else if (metricMode == "vmaf" || metricMode == "msssim" || metricMode == "mix")
                        {
                            metrics = await ComputeAllMetricsAsync(input, tmp);
                        }
                        else
                        {
                            // XPSNR / 高级指标 等 → 先用 libvmaf 获取基础指标
                            metrics = await ComputeAllMetricsAsync(input, tmp);
                        }

                        if (metrics != null)
                        {
                            // XPSNR 补算
                            if (cfg.MetricMode?.StartsWith("xpsnr", StringComparison.OrdinalIgnoreCase) == true)
                            {
                                try
                                {
                                    var (y, u, v, w) = await ComputeXPSNRAsync(input, tmp, pixFmt);
                                    metrics.XPSNR_Y = y;
                                    metrics.XPSNR_U = u;
                                    metrics.XPSNR_V = v;
                                    metrics.W_XPSNR = w;
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogInfo($"搜索 XPSNR 计算异常，将留空: {ex.Message}");
                                }
                            }

                            // ★ 搜索模式需要高级指标 → 补算（使用随机目录）
                            // ★ 搜索模式需要高级指标 → 补算（各自独立）
                            string? needAdvanced = cfg.MetricMode;
                            if (PresetConfig.IsAdvancedMetricMode(needAdvanced))
                            {
                                string advDir = Path.Combine(_outputDir, $"_search_advanced_{Guid.NewGuid():N}");
                                try
                                {
                                    _fs.CreateDirectory(advDir);
                                    // 根据实际需要的指标，有选择地进行 png 转换
                                    string? refPng = null;
                                    string? distPng = null;
                                    bool needSsimu2 = (needAdvanced == "ssimu2" && !metrics.SSIMULACRA2.HasValue);
                                    bool needButter = (needAdvanced == "butter3" && !metrics.Butteraugli_3norm.HasValue);
                                    bool needGmsd = (needAdvanced == "gmsd" && !metrics.GMSD.HasValue);

                                    if (needSsimu2 || needButter)
                                    {
                                        if (Path.GetExtension(input)?.ToLower() != ".png")
                                        {
                                            try { refPng = await ConvertToPngAsync(input, advDir); } catch { refPng = null; }
                                        }
                                        else refPng = input;
                                        if (refPng == null) refPng = input; // 仍可尝试原始格式

                                        try { distPng = await ConvertToPngAsync(tmp, advDir); } catch { distPng = null; }
                                    }

                                    // SSIMULACRA2
                                    if (needSsimu2 && refPng != null && distPng != null)
                                    {
                                        try
                                        {
                                            var s = await ComputeSSIMULACRA2Async(refPng, distPng);
                                            if (s.HasValue) metrics.SSIMULACRA2 = s;
                                        }
                                        catch (Exception ex) { _logger.LogInfo($"搜索 SSIMULACRA2 补算异常: {ex.Message}"); }
                                    }
                                    // Butteraugli
                                    if (needButter && refPng != null && distPng != null)
                                    {
                                        try
                                        {
                                            var (_, p3) = await ComputeButteraugliAsync(refPng, distPng, advDir);
                                            if (p3.HasValue) metrics.Butteraugli_3norm = p3;
                                        }
                                        catch (Exception ex) { _logger.LogInfo($"搜索 Butteraugli 补算异常: {ex.Message}"); }
                                    }
                                    // GMSD （无需 png 转换）
                                    if (needGmsd)
                                    {
                                        try
                                        {
                                            var g = await ComputeGMSDAsync(input, tmp);
                                            if (g.HasValue) metrics.GMSD = g;
                                        }
                                        catch (Exception ex) { _logger.LogInfo($"搜索 GMSD 补算异常: {ex.Message}"); }
                                    }
                                }
                                catch (Exception ex) { _logger.LogInfo($"搜索高级指标补算整体异常: {ex.Message}"); }
                                finally { if (_fs.DirectoryExists(advDir)) try { _fs.DeleteDirectory(advDir, true); } catch { } }
                            }

                            _cache.SetMetrics(key, metrics);
                            _logger.LogSearch($"新指标: CRF={crf} [{Path.GetFileName(input)}] " +
                                             $"mode={metricMode} score={MetricRegistry.GetScore(metrics, metricMode):F4}");
                        }
                        else
                        {
                            _logger.LogSearch($"指标计算失败: CRF={crf} [{Path.GetFileName(input)}]");
                        }

                        newTask.SetResult(metrics);
                        return metrics;
                    }
                    finally
                    {
                        if (_fs.FileExists(tmp)) try { _fs.DeleteFile(tmp); } catch { }
                    }
                }
                finally
                {
                    _ssimConcurrency.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"GetOrComputeMetrics 意外异常: [{Path.GetFileName(input)}] CRF={crf} - {ex.Message}");
                newTask.TrySetException(ex);
                return null;
            }
            finally
            {
                if (isOwner)
                    _metricsTasks.TryRemove(key, out _);
            }
        }















        /// <summary>
        /// 根据当前配置的度量模式从 QualityMetrics 中提取一个 0?1 的分数。
        /// </summary>
        /// <summary>
        /// 从 QualityMetrics 提取原生指标值（不做归一化）。
        /// 失败返回 -1。
        /// </summary>
        internal static double GetSearchScore(QualityMetrics m, string metricMode)
        {
            return MetricRegistry.GetScore(m, metricMode);
        }

        /// <summary>
        /// 计算 XPSNR 的三个通道分 (Y/U/V) 并返回加权 W?XPSNR (6:1:1)。
        /// 失败时各字段为 null。
        /// </summary>
        /// <summary>
        /// 计算 XPSNR 各通道分（Y/U/V）及加权 W?XPSNR。
        /// 默认使用 yuv444p 色彩空间，可通过 pixFmt 覆盖。
        /// </summary>
        /// <param name="pixFmt">像素格式，如 yuv444p / yuv420p</param>
        /// <summary>
        /// 计算 XPSNR 各通道分（Y/U/V）及加权 W?XPSNR。
        /// 默认使用 yuv444p 色彩空间，可通过 pixFmt 覆盖。
        /// </summary>
        private async Task<(double? y, double? u, double? v, double? weighted)> ComputeXPSNRAsync(
                string refPath, string distPath, string pixFmt = "yuv444p")
        {
            if (!_fs.FileExists(refPath) || !_fs.FileExists(distPath))
                return (null, null, null, null);

            string CleanPath(string p)
            {
                if (p.StartsWith(@"\\?\")) p = p.Substring(4);
                return Path.GetFullPath(p);
            }
            string safeRef = CleanPath(refPath);
            string safeDist = CleanPath(distPath);

            int bitDepth = 8;
            try
            {
                var infoRef = await GetProbeInfoAsync(refPath);
                if (infoRef != null && infoRef.PixFmt?.Contains("10le") == true)
                    bitDepth = 10;
                var infoDist = await GetProbeInfoAsync(distPath);
                if (infoDist != null && infoDist.PixFmt?.Contains("10le") == true)
                    bitDepth = Math.Max(bitDepth, 10);
            }
            catch { }
            double maxVal = bitDepth == 10 ? 1023.0 : 255.0;


            // 根据实际位深选择正确的像素格式（覆盖调用者传入的 pixFmt）
            string actualPixFmt = bitDepth == 10 ? "yuv444p10le" : "yuv444p";

            string args = $"-i \"{safeDist}\" -i \"{safeRef}\" " +
                $"-lavfi \"" +
                $"[0:v]settb=AVTB,setpts=PTS-STARTPTS," +
                $"scale=in_range=pc:out_range=pc," +
                $"format={actualPixFmt}," +
                $"pad=iw:ceil(ih/2)*2:0:0:color=black[dist];" +
                $"[1:v]settb=AVTB,setpts=PTS-STARTPTS," +
                $"scale=in_range=pc:out_range=pc," +
                $"format={actualPixFmt}," +
                $"pad=iw:ceil(ih/2)*2:0:0:color=black[ref];" +
                $"[dist][ref]xpsnr\" -f null -";

            var (exitCode, stdout, stderr) = await _processRunner.RunAsync(
                _ffmpegPath, args, TimeSpan.FromMinutes(_config.SsimTimeoutMinutes),
                _globalCts?.Token ?? default);

            string combinedOutput = stdout + stderr;

            if (exitCode != 0 || string.IsNullOrWhiteSpace(combinedOutput))
            {
                _logger.LogInfo($"XPSNR ffmpeg 失败 (exit={exitCode}) 或输出为空。stdout/stderr 尾部: {combinedOutput.TrimEnd().Split('\n').LastOrDefault()}");
                return (null, null, null, null);
            }

            // 先尝试匹配一行中同时包含 y、u、v 的输出（如 "XPSNR y: 48.5 u: 48.0 v: 47.9"）
            var combinedMatch = Regex.Match(combinedOutput,
                @"XPSNR\s+y:\s*(-?inf|[0-9.]+)\s+u:\s*(-?inf|[0-9.]+)\s+v:\s*(-?inf|[0-9.]+)",
                RegexOptions.IgnoreCase);
            double? y, u, v;
            if (combinedMatch.Success)
            {
                y = ParseSingleValue(combinedMatch.Groups[1].Value);
                u = ParseSingleValue(combinedMatch.Groups[2].Value);
                v = ParseSingleValue(combinedMatch.Groups[3].Value);
            }
            else
            {
                // 如果一行没有，再分别独立提取每个通道（某些 ffmpeg 版本可能分多行输出）
                var yMatch = Regex.Match(combinedOutput, @"XPSNR\s+y:\s*(-?inf|[0-9.]+)", RegexOptions.IgnoreCase);
                var uMatch = Regex.Match(combinedOutput, @"XPSNR\s+u:\s*(-?inf|[0-9.]+)", RegexOptions.IgnoreCase);
                var vMatch = Regex.Match(combinedOutput, @"XPSNR\s+v:\s*(-?inf|[0-9.]+)", RegexOptions.IgnoreCase);
                y = yMatch.Success ? ParseSingleValue(yMatch.Groups[1].Value) : null;
                u = uMatch.Success ? ParseSingleValue(uMatch.Groups[1].Value) : null;
                v = vMatch.Success ? ParseSingleValue(vMatch.Groups[1].Value) : null;
            }

            // 值解析辅助方法（局部函数）
            static double? ParseSingleValue(string val)
            {
                if (val.Equals("inf", StringComparison.OrdinalIgnoreCase))
                    return double.PositiveInfinity;   // 完全一致 → 正无穷
                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
                    return result;
                return null;
            }

            // 计算加权 W?XPSNR
            double? weighted = null;
            if (y.HasValue && u.HasValue && v.HasValue)
            {
                weighted = ComputeWXPSNR(y.Value, u.Value, v.Value, maxVal);
            }
            return (y, u, v, weighted);
        }

        /// <summary>计算加权 XPSNR，权重 Y:U:V = 6:1:1</summary>
        /// <param name="maxVal">像素最大值，8?bit=255, 10?bit=1023</param>
        private static double ComputeWXPSNR(double y, double u, double v, double maxVal = 255.0)
        {
            double mseY = maxVal * maxVal * Math.Pow(10, -y / 10);
            double mseU = maxVal * maxVal * Math.Pow(10, -u / 10);
            double mseV = maxVal * maxVal * Math.Pow(10, -v / 10);
            double weightedMSE = (6.0 * mseY + 1.0 * mseU + 1.0 * mseV) / 8.0;
            return 10.0 * Math.Log10(maxVal * maxVal / weightedMSE);
        }
    }
}
