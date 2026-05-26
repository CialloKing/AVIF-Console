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

        // 3. CRF 搜索
        // ---------- 主调度 ----------
        // 修改签名，增加 originalFileName 参数
        private async Task<CRFSearchResult> RunCRFSearchAsync(string inputPath, PresetConfig config, EncodingInfo encInfo, string originalFileName)
        {
            // 使用 originalFileName 作为所有显示的基准名称
            string displayName = originalFileName;

            int crf = encInfo.BaseCrf;
            string actualPixFmt = encInfo.ActualPixFmt;
            var searchTime = TimeSpan.Zero;
            bool searchBasedCRF = false, useSafeModeFinalEncode = false;
            int totalEvaluations = 0;
            string name = displayName;   // 用于日志和输出

            if (!encInfo.IsLosslessMode && config.UseCRFSearch)
            {
                string metricModeLabel = (config.MetricMode ?? "vmaf").ToUpper();
                string targetDisplay = GetTargetDisplayString(config.TargetSSIM, metricModeLabel, config);
                SafeWriteLine($"  [SEARCH] [{name}] 开始 CRF 搜索 (目标 {metricModeLabel}={targetDisplay})，请耐心等待...");

                try
                {
                    var swSearch = Stopwatch.StartNew();
                    bool searchOk;
                    int finalCrf;
                    string usedPixFmt;

                    // ★ 传递 displayName 给搜索核心
                    (searchOk, finalCrf, usedPixFmt, totalEvaluations) = await TrySearchWithFormatAttempts(
                        inputPath, config, encInfo, actualPixFmt, name);   // 传入 name

                    if (!searchOk)
                    {
                        // 当 MinCRF=0 时，CRF=0 已是质量上限，跳过耗时的安全扫描
                        if (config.MinCRF == 0)
                        {
                            SafeWriteLine($"  [WARN] [{name}] 搜索未达目标，MinCRF=0，跳过安全扫描，将直接使用 CRF=0 最终编码");
                            // searchOk 保持 false，后续 else 分支会处理
                        }
                        else
                        {
                            SafeWriteLine($" [RETRY] [{name}] 普通搜索失败，开始安全模式全扫描 (yuv420p, cpu?used 0)...");
                            (searchOk, finalCrf, usedPixFmt, useSafeModeFinalEncode) = await RunSafeModeScan(
                                inputPath, config, name, config.MinCRF, config.MaxCRF);
                        }
                    }

                    swSearch.Stop();

                    searchTime = swSearch.Elapsed;

                    if (searchOk)
                    {
                        crf = Math.Clamp(finalCrf, config.MinCRF, config.MaxCRF);
                        searchBasedCRF = true;
                        if (!useSafeModeFinalEncode) actualPixFmt = usedPixFmt;
                    }
                    else
                    {
                        // ★ 新逻辑：若搜索无解且 MinCRF=0，则直接使用 CRF=0 进行最终编码
                        if (config.MinCRF == 0)
                        {
                            crf = 0;
                            SafeWriteLine($"  [WARN] [{name}] 搜索未达目标，CRF=0 也无法满足，将使用 CRF=0 进行最终编码");
                            _logger.LogInfo($"搜索失败但 MinCRF=0，强制使用 CRF=0 编码: {name}");
                        }
                        else
                        {
                            crf = config.BaseCRF;
                            SafeWriteLine($"  [WARN] [{name}] 所有搜索失败，使用 BaseCRF ({crf}) 直接编码");
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 若最小CRF为0，异常时也尝试使用0编码（目标无法达成，但0是质量上限）
                    if (config.MinCRF == 0)
                    {
                        crf = 0;
                        _logger.LogInfo($"搜索异常但 MinCRF=0，强制使用 CRF=0 编码: {name} - {ex.Message}");
                        SafeWriteLine($" [WARN] [{name}] 搜索异常，使用 CRF=0 进行最终编码");
                    }
                    else
                    {
                        crf = config.BaseCRF;
                        _logger.LogInfo($"搜索异常，回退直接编码: {name} - {ex.Message}");
                        SafeWriteLine($" [WARN] [{name}] CRF搜索异常，使用 BaseCRF ({crf}) 直接编码");
                    }
                }
            }

            return new CRFSearchResult
            {
                Crf = crf,
                ActualPixFmt = actualPixFmt,
                SearchTime = searchTime,
                SearchBasedCRF = searchBasedCRF,
                UseSafeModeFinalEncode = useSafeModeFinalEncode,
                SearchEvalCount = totalEvaluations
            };
        }

        // ---------- 尝试目标格式列表搜索 ----------
        // ---------- 尝试目标格式列表搜索 ----------
        // 修改签名，增加 displayName 参数
        private async Task<(bool ok, int crf, string pixFmt, int totalEvalCount)> TrySearchWithFormatAttempts(
            string inputPath, PresetConfig config, EncodingInfo encInfo,
            string actualPixFmt, string displayName)
        {
            var attempts = BuildPixFmtAttempts(config, actualPixFmt, encInfo.HasAlpha);
            int totalEvalCount = 0;

            foreach (var fmt in attempts)
            {
                if (fmt != actualPixFmt && !config.AutoSource)
                {
                    string desc = fmt.Contains("422") ? "422" :
                                  (fmt.Contains("420") && !actualPixFmt.Contains("420") ? "420" : "");
                    if (!string.IsNullOrEmpty(desc))
                        SafeWriteLine($"  [RETRY] [{displayName}] 尝试 {desc} {fmt} ...");
                }

                // ★ 将 displayName 传给混合搜索
                (int crfResult, bool failed, bool qualityInsufficient, int evalCount) =
                    await HybridSearchCRFAsync(inputPath, encInfo.TileCols, config, fmt, IsJpeg(inputPath), displayName);
                totalEvalCount += evalCount;

                if (qualityInsufficient)
                    break;

                if (!failed)
                    return (true, crfResult, fmt, totalEvalCount);
            }
            return (false, config.BaseCRF, actualPixFmt, totalEvalCount);
        }

        // ---------- 安全模式全扫描 ----------
        private async Task<(bool ok, int crf, string pixFmt, bool safeMode)>
RunSafeModeScan(string inputPath, PresetConfig config, string name, int scanLow, int scanHigh)
        {
            using var safeCts = new CancellationTokenSource(TimeSpan.FromMinutes(config.SafeTimeoutMinutes));
            var safeToken = CancellationTokenSource.CreateLinkedTokenSource(
                safeCts.Token, _globalCts?.Token ?? default).Token;

            double target = config.TargetSSIM + SSIMMargin;
            int bestSafeCRF = -1;
            // ★ 使用传入的区间，而非全局 MinCRF/MaxCRF
            int start = scanHigh;
            int end = scanLow;
            int totalSteps = start - end + 1;
            int step = 0;
            int consecutiveFailures = 0;

            for (int testCrf = start; testCrf >= end; testCrf--)
            {
                step++;
                if (safeToken.IsCancellationRequested) break;

                if (step == 1 || step == totalSteps || step % 5 == 0)
                    SafeWriteLine($"  [{name}] 安全扫描 {step}/{totalSteps} (CRF={testCrf})...");

                double curSSIM = await SafeModeSSIM(inputPath, config, testCrf, safeToken);
                if (curSSIM >= target)
                {
                    bestSafeCRF = testCrf;
                    break;
                }

                if (curSSIM < 0)
                {
                    consecutiveFailures++;
                    if (consecutiveFailures >= 5)
                    {
                        SafeWriteLine($"  [{name}] 安全扫描连续失败 {consecutiveFailures} 次，终止扫描");
                        break;
                    }
                }
                else
                {
                    consecutiveFailures = 0;
                }
            }

            if (bestSafeCRF > 0)
            {
                SafeWriteLine($"  -> [{name}] 安全模式扫描成功，最佳 CRF = {bestSafeCRF}");
                return (true, bestSafeCRF, "yuv420p", true);
            }
            return (false, config.BaseCRF, "yuv420p", false);
        }

        // ---------- 安全模式单次 SSIM ----------
        private async Task<double> SafeModeSSIM(string inputPath, PresetConfig config, int testCrf,
                                        CancellationToken token)
        {
            if (token.IsCancellationRequested) return -1;

            bool acquired = false;
            try
            {
                await _ssimConcurrency.WaitAsync(token);
                acquired = true;

                string tmpAvif = Path.Combine(_outputDir, $"_safe_{Guid.NewGuid():N}.avif");
                string effectiveAom = config.GetEffectiveAomParams();
                string aomPart = string.IsNullOrEmpty(effectiveAom) ? "" : $"-aom-params {effectiveAom}";
                try
                {
                    // 提前获取分辨率，用于合法 tile 计算
                    var (w, h) = await GetResolutionAsync(inputPath);

                    string args = BuildSafeModeArgs(inputPath, tmpAvif, config, testCrf, aomPart, w);
                    (bool ok, string _) = await RunFfmpegExAsync(_ffmpegPath, args,
                        TimeSpan.FromMinutes(config.SafeEncodeTimeoutMinutes));
                    if (!ok || !_fs.FileExists(tmpAvif) || _fs.GetFileLength(tmpAvif) < 100) return -1;

                    QualityMetrics? metrics = await ComputeAllMetricsAsync(inputPath, tmpAvif);
                    if (metrics != null)
                    {
                        string normalizedInput = GetNormalizedPathForCache(inputPath);
                        string rowMtSafe = GetRowMtArg(config);
                        string cacheKey = GetSsimCacheKey(
                            normalizedInput, testCrf, "yuv420p", 0, 0,
                            IsJpeg(inputPath), effectiveAom, 8, w, h, rowMtSafe);
                        _cache.SetMetrics(cacheKey, metrics);

                        return GetSearchScore(metrics, config.MetricMode ?? "ssim");
                    }

                    // 回退传统 SSIM
                    _logger.LogInfo($"安全模式回退传统 SSIM: [{Path.GetFileName(inputPath)}] CRF={testCrf}");
                    return await SSIMDirect(inputPath, tmpAvif, "yuv420p");
                }
                finally
                {
                    if (_fs.FileExists(tmpAvif)) try { _fs.DeleteFile(tmpAvif); } catch { }
                }
            }
            finally
            {
                if (acquired) _ssimConcurrency.Release();
            }
        }

        // 4. 构建像素格式尝试列表（从原搜索代码中提取）
        private static List<string> BuildPixFmtAttempts(PresetConfig config, string actualPixFmt, bool hasAlpha)
        {
            // 提取位深后缀和基础格式
            string depthSuffix = actualPixFmt.EndsWith("10le") ? "10le" : "";
            string baseFmt = depthSuffix.Length > 0 ? actualPixFmt[..^4] : actualPixFmt;

            bool effectiveAlpha = hasAlpha;
            string cleanBase = effectiveAlpha ? baseFmt.Replace("a", "") : baseFmt;

            if (config.AutoSource)
                return new List<string> { actualPixFmt };

            var attempts = new List<string>();
            int startChroma = cleanBase.Contains("444") ? 0 :
                              (cleanBase.Contains("422") ? 1 : 2);

            for (int level = startChroma; level <= 2; level++)
            {
                string chroma = level switch { 0 => "444", 1 => "422", _ => "420" };

                // ★ 修复：正确生成 yuva / yuv 格式
                string fmt8 = effectiveAlpha ? $"yuva{chroma}p" : $"yuv{chroma}p";
                string fmt10 = $"{fmt8}10le";

                if (level == startChroma)
                {
                    if (depthSuffix == "10le")
                    {
                        if (config.BitDepth >= 10) attempts.Add(fmt10);
                        if (config.BitDepth <= 10) attempts.Add(fmt8);
                    }
                    else
                    {
                        if (config.BitDepth <= 10) attempts.Add(fmt8);
                        if (config.BitDepth >= 10) attempts.Add(fmt10);
                    }
                }
                else
                {
                    if (config.BitDepth >= 10) attempts.Add(fmt10);
                    if (config.BitDepth <= 10) attempts.Add(fmt8);
                }
            }

            return attempts.Distinct().ToList();
        }

        // 5. 执行最终编码
        private async Task<FinalEncodeResult> PerformFinalEncodeAsync(
    string inputPath, string outputPath, PresetConfig config,
    EncodingInfo encInfo, CRFSearchResult searchResult)
        {
            int timeoutMinutes = config.EncodeTimeoutMinutes > 0
                ? config.EncodeTimeoutMinutes
                : Math.Clamp((int)(((double)encInfo.Width * encInfo.Height) / (1920.0 * 1080.0) * 10), 5, 180);

            string effectiveAom = config.GetEffectiveAomParams();
            string aomPart = string.IsNullOrEmpty(effectiveAom) ? "" : $"-aom-params {effectiveAom}";
            string name = Path.GetFileName(inputPath);

            if (searchResult.UseSafeModeFinalEncode)
            {
                // 安全模式最终编码：传入图像宽度，确保 tile 合法
                return await EncodeSafeMode(inputPath, outputPath, config, searchResult,
                                            timeoutMinutes, aomPart, encInfo.Width);
            }
            else
            {
                var result = await EncodeWithFallback(inputPath, outputPath, config, encInfo, searchResult,
                                                      timeoutMinutes, aomPart, name, effectiveAom);
                if (!result.Success && encInfo.IsTrulyLossless)
                {
                    result = await TryLosslessFallback(inputPath, outputPath, config, encInfo, searchResult,
                                                       timeoutMinutes, aomPart, effectiveAom);
                }

                if (!result.Success && string.IsNullOrEmpty(result.FailReason))
                    result.FailReason = $"编码失败（多次重试后）: {result.FailReason}";

                return result;
            }
        }

        /// <summary>
        /// 构造安全模式（yuv420p + 单tile + 全色域）的 ffmpeg 参数字符串
        /// </summary>
        /// <summary>
        /// 构造安全模式（yuv420p + 单 tile + 全色域）的 ffmpeg 参数字符串。
        /// 仅对 libaom?av1 启用 tile 与 row?mt 参数，其他编码器忽略，避免无效参数造成失败。
        /// </summary>
        /// <summary>
        /// 构造安全模式（yuv420p + 单 tile + 全色域）的 ffmpeg 参数字符串。
        /// 仅对 libaom?av1 启用 tile 与 row?mt 参数，其他编码器忽略，避免无效参数造成失败。
        /// 若启用了 DisableTileParallel，则强制 tile=0 且关闭 row?mt。
        /// </summary>
        /// <summary>
        /// 构造安全模式（yuv420p + 单 tile + 全色域）的 ffmpeg 参数字符串。
        /// 若启用了 SerialEncode，则强制 tile=0 且关闭 row?mt。
        /// </summary>
        private static string BuildSafeModeArgs(string inputPath, string outputPath, PresetConfig config,
                                 int crf, string aomPart, int imageWidth)
        {
            bool useStillPic = Av1EncoderFactory.Get(config.Encoder).SupportsStillPicture;
            string stillPic = useStillPic ? "-still-picture 1" : "";

            int minCols = GetMinLegalTileCols(imageWidth);
            int maxCols = GetMaxLegalTileCols(imageWidth);
            int safeTileCols;
            if (imageWidth < 256 || minCols > maxCols)
                safeTileCols = 0;
            else
                safeTileCols = minCols;   // minCols 已确保 tile 宽度 ≤4096，无需额外强制 ≥2

            string safeRowMt;
            var enc = Av1EncoderFactory.Get(config.Encoder);

            if (config.SerialEncode)
            {
                safeTileCols = GetMinLegalTileCols(imageWidth);
                safeRowMt = enc.SupportsRowMt ? "-row-mt 0" : "";
            }
            else
            {
                safeRowMt = enc.SupportsRowMt ? "-row-mt 1" : "";
            }

            string safeTile = enc.SupportsTiles
                ? $"-tile-columns {safeTileCols} -tile-rows 0" : "";
            string encArgs = BuildEncoderSpecificArgs(config, 0, safeTile, safeRowMt);
            string threadsArg = config.SerialEncode ? "-threads 1" : "";  // 新属性名

            return $"-loglevel error -hide_banner -i \"{inputPath}\" " +
                   $"-c:v {config.Encoder} -pix_fmt yuv420p " +
                   $"-crf {crf} {encArgs} " +
                   $"-color_range pc {stillPic} -frames:v 1 {aomPart} {threadsArg} -y \"{outputPath}\"";
        }


        private async Task<FinalEncodeResult> EncodeWithFallback(
    string inputPath, string outputPath, PresetConfig config,
    EncodingInfo encInfo, CRFSearchResult searchResult,
    int timeoutMinutes, string aomPart, string name, string effectiveAom)
        {
            var startTime = DateTime.Now;
            int crf = searchResult.Crf;
            string actualPixFmt = searchResult.ActualPixFmt;
            bool success;
            TimeSpan encodeTime;
            int retries;
            string failReason;
            bool fromCache;
            string? actualAom;
            string? finalCommand;
            bool usedSafeModeFallback = false;

            // 1. 尝试常规编码
            (success, encodeTime, retries, failReason, fromCache, actualAom, finalCommand) =
                await EncodeToFileExAsync(inputPath, outputPath, crf, encInfo.TileCols, config.FinalCpuUsed,
                    config, IsJpeg(inputPath), actualPixFmt, encInfo.IsTrulyLossless, timeoutMinutes);

            // 2. CRF 递增重试
            if (!success && searchResult.SearchBasedCRF && crf < config.MaxCRF)
            {
                for (int attemptCRF = crf + 1; attemptCRF <= config.MaxCRF; attemptCRF++)
                {
                    SafeWriteLine($" [WARN] [{name}] CRF={crf} 编码失败，尝试 CRF={attemptCRF}...");
                    (success, encodeTime, retries, failReason, fromCache, actualAom, finalCommand) =
                        await EncodeToFileExAsync(inputPath, outputPath, attemptCRF, encInfo.TileCols, config.FinalCpuUsed,
                            config, IsJpeg(inputPath), actualPixFmt, encInfo.IsTrulyLossless, timeoutMinutes);
                    if (success) { crf = attemptCRF; break; }
                }
            }

            // 3. 最终安全模式回退（使用合法 tile 尺寸）
            if (!success)
            {
                SafeWriteLine($" [WARN] [{name}] 常规/降级均失败，尝试最终安全模式（yuv420p）...");
                // 构建安全模式参数，传入图像宽度以满足 tile 合法性
                string safeArgs = BuildSafeModeArgs(inputPath, outputPath, config, crf, aomPart, encInfo.Width);
                var swSafe = Stopwatch.StartNew();
                (bool safeOk, string safeErr) = await RunFfmpegExAsync(_ffmpegPath, safeArgs, TimeSpan.FromMinutes(timeoutMinutes * 2));
                swSafe.Stop();

                if (safeOk)
                {
                    success = true;
                    encodeTime = swSafe.Elapsed;
                    retries = 0;
                    failReason = "";
                    actualPixFmt = "yuv420p";
                    fromCache = false;
                    usedSafeModeFallback = true;

                    string encoderDesc = config.Encoder.StartsWith("libsvtav1") ? "preset 0" :
                                         config.Encoder.StartsWith("libaom-av1") ? "cpu-used 0" :
                                         config.Encoder.StartsWith("librav1e") ? "speed 0" : "hardware";
                    bool useStillPic = Av1EncoderFactory.Get(config.Encoder).SupportsStillPicture;
                    string stillDesc = useStillPic ? ", still-picture" : "";
                    actualAom = $"safe (yuv420p, {encoderDesc}{stillDesc}, full-range)";
                    finalCommand = safeArgs;
                }
                else
                {
                    failReason = safeErr;
                }
            }

            return new FinalEncodeResult
            {
                Success = success,
                Crf = crf,
                ActualPixFmt = actualPixFmt,
                EncodeTime = encodeTime,
                Retries = retries,
                FailReason = failReason,
                FromCache = fromCache,
                ActualAom = actualAom,
                FinalCommand = finalCommand,
                UseSafeMode = searchResult.UseSafeModeFinalEncode || usedSafeModeFallback,
                StartTime = startTime
            };
        }


        private async Task<FinalEncodeResult> EncodeSafeMode(
    string inputPath, string outputPath, PresetConfig config,
    CRFSearchResult searchResult, int timeoutMinutes, string aomPart, int imageWidth)
        {
            var startTime = DateTime.Now;
            int crf = searchResult.Crf;

            string safeArgs = BuildSafeModeArgs(inputPath, outputPath, config, crf, aomPart, imageWidth);

            var swSafe = Stopwatch.StartNew();
            (bool success, string failReason) = await RunFfmpegExAsync(_ffmpegPath, safeArgs, TimeSpan.FromMinutes(timeoutMinutes));
            swSafe.Stop();

            string encoderDesc = config.Encoder.StartsWith("libsvtav1", StringComparison.OrdinalIgnoreCase) ? "preset 0" :
                                 config.Encoder.StartsWith("libaom-av1", StringComparison.OrdinalIgnoreCase) ? "cpu-used 0" :
                                 config.Encoder.StartsWith("librav1e", StringComparison.OrdinalIgnoreCase) ? "speed 0" :
                                 "hardware";
            bool useStillPic = Av1EncoderFactory.Get(config.Encoder).SupportsStillPicture;
            string stillDesc = useStillPic ? ", still-picture" : "";
            string safeDesc = $"safe (yuv420p, {encoderDesc}{stillDesc}, full-range)";

            return new FinalEncodeResult
            {
                Success = success,
                Crf = crf,
                ActualPixFmt = "yuv420p",
                EncodeTime = swSafe.Elapsed,
                Retries = 0,
                FailReason = success ? "" : $"安全模式编码失败: {failReason}",
                FromCache = false,
                ActualAom = safeDesc,
                FinalCommand = success ? safeArgs : null,
                UseSafeMode = true,
                StartTime = startTime
            };
        }




        private async Task<FinalEncodeResult> TryLosslessFallback(
    string inputPath, string outputPath, PresetConfig config,
    EncodingInfo encInfo, CRFSearchResult searchResult,
    int timeoutMinutes, string aomPart, string effectiveAom)
        {
            SafeWriteLine($" [WARN] [{Path.GetFileName(inputPath)}] 真无损失败，尝试 -crf 0 ...");

            var fallbackConfig = new PresetConfig
            {
                BaseCRF = 0,
                Lossless = false,                           // 使用 -crf 0 而非真无损模式
                PixelFormat = searchResult.ActualPixFmt,
                AomParams = effectiveAom,
                FinalCpuUsed = 0,
                SearchCpuUsed = 0,
                UseCRFSearch = false,
                MaxJobs = config.MaxJobs,
                BitDepth = config.BitDepth,
                Encoder = config.Encoder
            };

            (bool success, TimeSpan encodeTime, int retries, string failReason, bool fromCache,
                string? actualAom, string? finalCommand) =
                await EncodeToFileExAsync(inputPath, outputPath, 0, 0, 0, fallbackConfig,
                    IsJpeg(inputPath), searchResult.ActualPixFmt, false, timeoutMinutes * 2);

            return new FinalEncodeResult
            {
                Success = success,
                Crf = 0,
                ActualPixFmt = searchResult.ActualPixFmt,
                EncodeTime = encodeTime,
                Retries = retries,
                FailReason = success ? "" : "真无损回退仍然失败",
                FromCache = fromCache,
                ActualAom = actualAom,
                FinalCommand = finalCommand,
                UseSafeMode = false,
                StartTime = DateTime.Now
            };
        }





        private void MarkProcessed(EncodeResult? r)
        {
            _progress.MarkFileProcessed();
            PrintProgress(r);

            // ★ 向 GUI 报告进度（0 ~ 100）
            if (_progress.TotalFiles > 0)
            {
                int pct = _progress.ProcessedCount * 100 / _progress.TotalFiles;
                _guiProgress?.Report(Math.Min(pct, 100));
            }
        }
    }
}
