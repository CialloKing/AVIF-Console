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
        private async Task<IEnumerable<EncodeResult>> ProcessFilesAsync(
    List<(string filePath, int index)> files, PresetConfig config, bool isRetry)
        {
            if (config.SweepMode)
            {
                int crfCount = config.MaxCRF - config.MinCRF + 1;
                if (crfCount <= 0) crfCount = 1;
                int totalTasks = files.Count * crfCount;
                _progress.SetTotalFiles(totalTasks);   // 遍历模式下总进度 = 图片数 × CRF 范围
                return await ProcessFilesSweepAsync(files, config);
            }
            var results = new ConcurrentDictionary<int, EncodeResult>();
            var semaphore = new SemaphoreSlim(config.MaxJobs);
            var tasks = files.Select(async file =>
                {
                    try
                    {
                        bool acquired = await semaphore.WaitAsync(TimeSpan.FromMinutes(720), _globalCts?.Token ?? default);
                        if (!acquired)
                        {
                            _logger.LogInfo($"任务信号量获取超时，跳过文件: {Path.GetFileName(file.filePath)}");
                            // 信号量超时
                            var failResult = new EncodeResult
                            {
                                Index = file.index,
                                FileName = GetOutputFileName(file.filePath, file.index),
                                OriginalFileName = Path.GetFileName(file.filePath),
                                InputPath = file.filePath,                     // ★ 新增
                                Success = false,
                                Skipped = false,
                                ErrorMessage = "任务信号量获取超时",
                                TotalTime = TimeSpan.Zero
                            };
                            results[file.index] = failResult;
                            MarkProcessed(failResult);
                            return;
                        }
                        try
                        {
                            var r = await ProcessSingleFileAsync(file.filePath, file.index, config, isRetry);
                            if (r != null) results[r.Index] = r;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogInfo($"文件处理异常: {file.filePath} - {ex.Message}");
                            // 异常处理中的 failResult
                            var failResult = new EncodeResult
                            {
                                Index = file.index,
                                FileName = GetOutputFileName(file.filePath, file.index),
                                OriginalFileName = Path.GetFileName(file.filePath),
                                InputPath = file.filePath,
                                Success = false,
                                Skipped = false,
                                ErrorMessage = $"异常: {ex.Message}",
                                TotalTime = TimeSpan.Zero
                            };
                            results[file.index] = failResult;
                            MarkProcessed(failResult);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // ★ 修复 Bug3：全局取消时优雅退出，记录取消结果
                        _logger.LogInfo($"操作取消，跳过文件: {Path.GetFileName(file.filePath)}");
                        // 取消操作
                        var cancelResult = new EncodeResult
                        {
                            Index = file.index,
                            FileName = GetOutputFileName(file.filePath, file.index),
                            OriginalFileName = Path.GetFileName(file.filePath),
                            InputPath = file.filePath,
                            Success = false,
                            Skipped = false,
                            ErrorMessage = "用户取消操作",
                            TotalTime = TimeSpan.Zero
                        };
                        results[file.index] = cancelResult;
                        MarkProcessed(cancelResult);
                    }
                });
            await Task.WhenAll(tasks);
            return results.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value);
        }


        /// <summary> 遍历模式：对每个输入文件在 MinCRF～MaxCRF 范围内生成多个 AVIF 并保存完整指标 </summary>
        /// <summary> 遍历模式：对每个输入文件在 MinCRF～MaxCRF 范围内生成多个 AVIF 并保存完整指标（文件按顺序串行） </summary>
        private async Task<IEnumerable<EncodeResult>> ProcessFilesSweepAsync(
    List<(string filePath, int index)> files, PresetConfig config)
        {
            var results = new ConcurrentBag<EncodeResult>();

            // 文件级串行：依次处理每个文件
            foreach (var file in files)
            {
                string inputPath = file.filePath;
                string name = Path.GetFileName(inputPath);

                // 1. 准备编码基础信息（复用缓存）
                var encInfo = await PrepareEncodingInfoAsync(inputPath, config);
                if (encInfo == null)
                {
                    _logger.LogInfo($"跳过 {name}：无法获取编码信息");
                    continue;
                }

                // 2. 预缩放（如果需要）
                var scaling = await HandlePreScalingAsync(inputPath, config, name);
                string workingInput = scaling.WorkingPath;

                // 3. 为当前文件创建所有 CRF 任务，用信号量控制文件内并发
                var semaphore = new SemaphoreSlim(config.MaxJobs);
                var crfTasks = new List<Task>();

                for (int crf = config.MinCRF; crf <= config.MaxCRF; crf++)
                {
                    int capturedCrf = crf;
                    var task = Task.Run(async () =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            var startTime = DateTime.Now;

                            // 生成输出路径
                            string baseOutput = GetOutputPath(inputPath, file.index);
                            string dir = Path.GetDirectoryName(baseOutput)!;
                            string baseName = Path.GetFileNameWithoutExtension(baseOutput);
                            string outputPath = Path.Combine(dir, $"{baseName}_CRF{capturedCrf}.avif");

                            // 编码（内部会等待 _ffmpegSlots）
                            (bool ok, TimeSpan t, int retries, string error, bool fromCache,
                             string? actualAom, string? cmd) =
                                await EncodeToFileExAsync(workingInput, outputPath, capturedCrf,
                                    encInfo.TileCols, config.FinalCpuUsed, config,
                                    IsJpeg(workingInput), encInfo.ActualPixFmt, encInfo.IsTrulyLossless,
                                    config.EncodeTimeoutMinutes > 0 ? config.EncodeTimeoutMinutes : 30,
                                    allowParamDegrade: true);

                            if (!ok)
                            {
                                var failResult = new EncodeResult
                                {
                                    Index = file.index * 1000 + capturedCrf,
                                    FileName = Path.GetFileName(outputPath),
                                    OriginalFileName = name,
                                    InputPath = inputPath,
                                    UsedCRF = capturedCrf,
                                    Success = false,
                                    ErrorMessage = error,
                                    TotalTime = DateTime.Now - startTime,
                                    PixelFormat = encInfo.ActualPixFmt,
                                };
                                MarkProcessed(failResult);
                                results.Add(failResult);
                                return;
                            }

                            // 质量指标计算
                            (double ssim, QualityMetrics? metrics, string sweepCacheKey) = await EvaluateFinalQualityAsync(
                                workingInput, outputPath,
                                new FinalEncodeResult
                                {
                                    Success = true,
                                    Crf = capturedCrf,
                                    ActualPixFmt = encInfo.ActualPixFmt,
                                    EncodeTime = t,
                                    Retries = retries,
                                    FromCache = fromCache,
                                    FinalCommand = cmd,
                                    UseSafeMode = false,
                                    ActualAom = actualAom ?? config.GetEffectiveAomParams()
                                },
                                encInfo,
                                new CRFSearchResult
                                {
                                    Crf = capturedCrf,
                                    ActualPixFmt = encInfo.ActualPixFmt,
                                    SearchBasedCRF = false,
                                    UseSafeModeFinalEncode = false,
                                    SearchEvalCount = 0
                                },
                                config
                            );

                            var result = new EncodeResult
                            {
                                Index = file.index * 1000 + capturedCrf,
                                FileName = Path.GetFileName(outputPath),
                                OriginalFileName = name,
                                InputPath = inputPath,
                                OriginalSize = _fs.FileExists(inputPath) ? _fs.GetFileLength(inputPath) : 0,
                                OutputSize = _fs.FileExists(outputPath) ? _fs.GetFileLength(outputPath) : 0,
                                UsedCRF = capturedCrf,
                                FinalSSIM = ssim,
                                EncodeTime = t,
                                SearchTime = TimeSpan.Zero,
                                TotalTime = DateTime.Now - startTime,
                                Retries = retries,
                                Success = true,
                                PixelFormat = encInfo.ActualPixFmt,
                                SourcePixelFormat = encInfo.SourcePixFmt,
                                Mode = config.AutoSource ? "自适应" : "手动",
                                IsSafeMode = false,
                                CacheReused = fromCache,
                                CommandLine = cmd,
                                FinalVMAF = metrics?.VMAF,
                                FinalPSNR_Y = metrics?.PSNR_Y,
                                FinalMSSSIM = metrics?.MS_SSIM,
                                FinalMixScore = metrics != null ? ComputeMixScore(metrics) : null,
                                FinalXPSNR_Y = metrics?.XPSNR_Y,
                                FinalXPSNR_U = metrics?.XPSNR_U,
                                FinalXPSNR_V = metrics?.XPSNR_V,
                                FinalWXPSNR = metrics?.W_XPSNR,
                                FinalSSIMULACRA2 = metrics?.SSIMULACRA2,
                                FinalButteraugli_Raw = metrics?.Butteraugli_Raw,
                                FinalButteraugli_3norm = metrics?.Butteraugli_3norm,
                                FinalGMSD = metrics?.GMSD,
                                AomParamsUsed = actualAom ?? config.GetEffectiveAomParams(),
                                AdvancedMetricsCacheKey = sweepCacheKey,
                                SearchEvaluations = 0
                            };

                            // 标注 AOM 参数降级
                            string expectedAom = config.GetEffectiveAomParams();
                            if (expectedAom.Length > 0 && (actualAom ?? "") != expectedAom)
                            {
                                result.ErrorMessage = "AOM参数已降级（编码器未使用完整参数）";
                            }

                            MarkProcessed(result);
                            results.Add(result);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, _globalCts?.Token ?? CancellationToken.None);

                    crfTasks.Add(task);
                }

                // 4. 等待当前文件的所有 CRF 任务完成后，清理临时缩放文件，再处理下一个文件
                await Task.WhenAll(crfTasks);
                semaphore.Dispose();
                if (scaling.TempFilePath != null)
                    try { _fs.DeleteFile(scaling.TempFilePath); } catch { }
            }

            return results.OrderBy(r => r.Index).ToList();
        }














        private static bool IsJpeg(string path) =>
                Path.GetExtension(path).ToLower() is ".jpg" or ".jpeg";


        public static void ApplyBitDepth(PresetConfig cfg)
        {
            if (cfg.PixelFormat == null) return;
            if (cfg.BitDepth == 8)
            {
                if (cfg.PixelFormat.EndsWith("10le"))
                    cfg.PixelFormat = cfg.PixelFormat.Substring(0, cfg.PixelFormat.Length - 4);
            }
            else // 10
            {
                // ★ 移除无意义的 rgb24 判断，仅根据后缀决定是否添加 10le
                if (!cfg.PixelFormat.EndsWith("10le"))
                    cfg.PixelFormat += "10le";
            }
        }


        // ==================== 主调度方法 ====================
        // ==================== 主调度方法 ====================
        private async Task<EncodeResult?> ProcessSingleFileAsync(string inputPath, int index, PresetConfig config, bool isRetry)
        {
            string name = Path.GetFileName(inputPath);
            string outputPath = GetOutputPath(inputPath, index);
            var fileStartTime = DateTime.Now;

            // ---- 无损模式强约束：禁止缩放、固定 tile=0 ----
            if (config.Lossless)
            {
                config.MaxResolution = 0;
                _logger.LogInfo($"无损模式：已强制禁用预缩放 ({name})");
            }

            // ---- 预缩放 ----
            var scaling = await HandlePreScalingAsync(inputPath, config, name);
            try
            {
                string workingInputPath = scaling.WorkingPath;
                if (scaling.WasScaled)
                    _logger.LogInfo($"预缩放: {name} {scaling.OriginalWidth}x{scaling.OriginalHeight} -> {scaling.ScaledWidth}x{scaling.ScaledHeight}");

                // 跳过已存在
                var skipResult = await TrySkipExistingOutputAsync(inputPath, index, config, isRetry);
                if (skipResult != null) return skipResult;

                _logger.LogInfo($"开始: {name}");

                // 准备编码信息
                var encInfo = await PrepareEncodingInfoAsync(workingInputPath, config);
                if (encInfo == null)
                    return FailResult(index, Path.GetFileName(outputPath), name,
                                      inputPath, "无法获取分辨率", fileStartTime);

                SafeWriteLine($"[START] {name} [{encInfo.PixInfo}]");

                // 搜索 + 最终编码
                var searchResult = await RunCRFSearchAsync(workingInputPath, config, encInfo, name);
                string finalEncodeInput = (scaling.WasScaled && !config.ApplyScalingToOutput) ? inputPath : workingInputPath;
                var encodeResult = await PerformFinalEncodeAsync(finalEncodeInput, outputPath, config, encInfo, searchResult);

                // 无损验证：全量逐像素比对，失败即视为编码失败并生成诊断报告
                if (config.Lossless && encodeResult.Success)
                {
                    var swVerify = System.Diagnostics.Stopwatch.StartNew();
                    var verReport = await VerifyLosslessAsync(
                        workingInputPath, outputPath, name, encInfo.Width);
                    swVerify.Stop();

                    if (verReport != null)
                    {
                        // 填充完整诊断信息
                        verReport.SourceFile = inputPath;
                        verReport.PixelFormat = encInfo.ActualPixFmt;
                        verReport.BitDepth = config.BitDepth;
                        verReport.Width = encInfo.Width;
                        verReport.Height = encInfo.Height;
                        verReport.Encoder = config.Encoder;
                        verReport.EncodeCommand = encodeResult.FinalCommand ?? "";
                        verReport.Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                        // JSON 扩展字段
                        verReport.SourcePixelFormat = encInfo.SourcePixFmt;
                        verReport.VerificationTimeSec =
                            swVerify.Elapsed.TotalSeconds;
                        verReport.EncodeTimeSec =
                            encodeResult.EncodeTime.TotalSeconds;

                        // 获取编码器版本
                        var (ffVer, encVers) = await GetEncoderVersionsAsync(_ffmpegPath);
                        if (encVers.TryGetValue(config.Encoder, out var encVer))
                        {
                            verReport.EncoderVersion = encVer;
                        }
                        else
                        {
                            verReport.EncoderVersion = ffVer;
                        }

                        // 移动失败输出到隔离目录
                        string failedOutputName = Path.GetFileName(outputPath);
                        string failedDest = Path.Combine(_failedVerificationDir, failedOutputName);
                        verReport.FailedOutput = failedDest;

                        // 输出文件大小
                        verReport.OutputFileSize = _fs.FileExists(outputPath)
                            ? _fs.GetFileLength(outputPath) : 0;

                        if (_fs.FileExists(outputPath))
                        {
                            try
                            {
                                _fs.CopyFile(outputPath, failedDest, true);
                            }
                            catch { }
                            try { _fs.DeleteFile(outputPath); } catch { }
                        }

                        _logger.LogInfo(
                            $"✘ 无损验证失败：{verReport.ToSummary()} ({name})");
                        SafeWriteLine(
                            $" [FAIL] [{name}] 无损验证失败，" +
                            $"{verReport.FailureType}，" +
                            $"已保存到 {_failedVerificationDir}");

                        // 写入 CSV 和 JSON
                        AppendFailedVerificationCsv(verReport);
                        await WriteVerificationReportJsonAsync(verReport);

                        // 差异热力图不再自动生成，用户可从 JSON 的 MismatchSamples 查看差异

                        // 计算质量指标（编码成功但验证失败，指标依然有效）
                        (double failSsim, QualityMetrics? failMetrics, string failCacheKey) =
                            await EvaluateFinalQualityAsync(
                             workingInputPath, _fs.FileExists(failedDest) ? failedDest : outputPath,
                             encodeResult, encInfo, searchResult, config);

                        // 用 BuildResult 保留完整指标，仅标记为失败
                        var result = BuildResult(index,
                            Path.GetFileName(outputPath), name,
                            inputPath,
                            _fs.FileExists(failedDest) ? failedDest : outputPath,
                            encodeResult, searchResult, encInfo, failSsim, failMetrics,
                            fileStartTime, failCacheKey);
                        result.Success = false;
                        result.ErrorMessage = $"无损验证失败：{verReport.FailureType}";
                        return result;
                    }
                }

                // 计算最终质量
                (double ssim, QualityMetrics? metrics, string advancedCacheKey) = await EvaluateFinalQualityAsync(
                 workingInputPath, outputPath, encodeResult, encInfo, searchResult, config);

                // 组装结果（传递 cacheKey 以便最终回填高级指标）
                return BuildResult(index, Path.GetFileName(outputPath), name,
                                       inputPath, outputPath,
                                       encodeResult, searchResult, encInfo, ssim, metrics, fileStartTime, advancedCacheKey);
            }
            finally
            {
                if (scaling.TempFilePath != null)
                    try { _fs.DeleteFile(scaling.TempFilePath); } catch { }
            }
        }

        private record PreScalingResult(
    string WorkingPath, bool WasScaled, string? TempFilePath,
    int OriginalWidth, int OriginalHeight, int ScaledWidth, int ScaledHeight);

        private async Task<PreScalingResult> HandlePreScalingAsync(string inputPath, PresetConfig config, string name)
        {
            if (config.MaxResolution <= 0)
                return new PreScalingResult(inputPath, false, null, 0, 0, 0, 0);

            var (srcW, srcH) = await GetResolutionAsync(inputPath);
            int longSide = Math.Max(srcW, srcH);
            if (longSide <= config.MaxResolution)
                return new PreScalingResult(inputPath, false, null, srcW, srcH, srcW, srcH);

            string scaledDir = Path.Combine(_outputDir, "_scaled");
            _fs.CreateDirectory(scaledDir);
            string scaledFile = Path.Combine(scaledDir, $"_scaled_{Guid.NewGuid():N}.png");
            await ScaleImageAsync(inputPath, scaledFile, config.MaxResolution);
            var (sw, sh) = await GetResolutionAsync(scaledFile);
            return new PreScalingResult(scaledFile, true, scaledFile, srcW, srcH, sw, sh);
        }


        /// <summary>
        /// 评估最终编码质量：先从缓存取，若无则计算 VMAF/XPSNR/高级指标，
        /// 并自动清洗被尾部污染的 PNG 源文件以保证 SSIMULACRA2/Butteraugli 正常。
        /// </summary>
        /// <summary>
        /// 评估最终编码质量：先从缓存取，若无则计算 VMAF/XPSNR/高级指标，
        /// 并自动清洗被尾部污染的 PNG 源文件以保证 SSIMULACRA2/Butteraugli 正常。
        /// </summary>
        private async Task<(double ssim, QualityMetrics? metrics, string cacheKey)> EvaluateFinalQualityAsync(
            string workingInputPath, string outputPath, FinalEncodeResult encodeResult,
            EncodingInfo encInfo, CRFSearchResult searchResult, PresetConfig config)
        {
            if (!encodeResult.Success)
                return (0, null, "");

            string normalizedInput = GetNormalizedPathForCache(workingInputPath);
            string cleanPixFmt = encodeResult.ActualPixFmt?.Replace("a", "") ?? "";
            int actualDepth = encodeResult.ActualPixFmt?.Contains("10le") == true ? 10 : 8;
            string aomParams = config.GetEffectiveAomParams();
            bool jpeg = IsJpeg(workingInputPath);
            int tileCols = encInfo.TileCols;
            int cpuUsed = searchResult.UseSafeModeFinalEncode ? 0 : config.FinalCpuUsed;
            var (keyW, keyH) = await GetResolutionAsync(workingInputPath);
            string rowMtArg = EncodeHelpers.GetRowMtArg(config);
            string cacheKey = GetSsimCacheKey(normalizedInput, encodeResult.Crf, cleanPixFmt, tileCols,
                                              cpuUsed, jpeg, aomParams, actualDepth, keyW, keyH, rowMtArg);

            // ---------- 缓存命中 ----------
            // ---------- 缓存命中 ----------
            // ---------- 缓存命中 ----------
            if (_cache.TryGetMetrics(cacheKey, out QualityMetrics? cachedMetrics))
            {
                _logger.LogSearch($"最终指标复用缓存: CRF={encodeResult.Crf} VMAF={cachedMetrics!.VMAF:F4}");

                bool needUpdate = false;

                // PSNR-Y 接近 libvmaf 60dB 上限时用独立滤镜重算
                if (cachedMetrics.PSNR_Y >= 59.5)
                {
                    _logger.LogInfo($"PSNR={cachedMetrics.PSNR_Y} 触上限，用独立滤镜重算...");
                    try
                    {
                        var uncapped = await ComputePsnrUncappedAsync(
                            workingInputPath, outputPath);
                        if (uncapped.HasValue)
                        {
                            cachedMetrics.PSNR_Y = uncapped.Value;
                            needUpdate = true;
                            _logger.LogInfo(
                                $"PSNR 独立重算完成: {uncapped.Value}");
                        }
                        else
                        {
                            _logger.LogInfo("PSNR 独立重算返回 null");
                        }
                    }
                    catch (Exception ex) { _logger.LogInfo($"PSNR 上限重算异常: {ex.Message}"); }
                }

                // XPSNR 后台异步计算（不阻塞主流程）
                if (!cachedMetrics.XPSNR_Y.HasValue || !cachedMetrics.XPSNR_U.HasValue ||
                    !cachedMetrics.XPSNR_V.HasValue || !cachedMetrics.W_XPSNR.HasValue)
                {
                    var xKey = cacheKey;
                    var xTask = Task.Run(async () =>
                    {
                        try
                        {
                            var (y, u, v, weighted) = await ComputeXPSNRAsync(
                                workingInputPath, outputPath, "yuv444p");
                            _cache.UpdateMetrics(xKey, m =>
                            {
                                m.XPSNR_Y = y; m.XPSNR_U = u;
                                m.XPSNR_V = v; m.W_XPSNR = weighted;
                            });
                            _logger.LogInfo(
                                $"XPSNR 后台完成: Y={y?.ToString("F4")}, " +
                                $"U={u?.ToString("F4")}, V={v?.ToString("F4")}, " +
                                $"W={weighted?.ToString("F4")}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogInfo($"XPSNR 后台异常: {ex.Message}");
                        }
                    });
                    _xpsnrTasks.Enqueue(xTask);
                    needUpdate = true;
                }

                // 补算缺失的高级指标（异步后台执行）
                bool advancedUpdated = false;
                {
                    bool needSsimu2 = !cachedMetrics.SSIMULACRA2.HasValue;
                    bool needButter = !cachedMetrics.Butteraugli_3norm.HasValue || !cachedMetrics.Butteraugli_Raw.HasValue;
                    bool needGmsd = !cachedMetrics.GMSD.HasValue;

                    if (needSsimu2 || needButter || needGmsd)
                    {
                        var bgTask = ComputeAdvancedMetricsInBackgroundAsync(
                            workingInputPath, outputPath, _outputDir, cacheKey,
                            needSsimu2, needButter, needGmsd,
                            _globalCts?.Token ?? CancellationToken.None);
                        _advancedMetricTasks.Enqueue(bgTask);
                        advancedUpdated = true;
                    }
                }

                if (needUpdate || advancedUpdated)
                {
                    _cache.SetMetrics(cacheKey, cachedMetrics);
                    _logger.LogInfo(
                        $"缓存指标补充: " +
                        $"SSIMULACRA2={cachedMetrics.SSIMULACRA2?.ToString("F4")}, " +
                        $"Butteraugli={cachedMetrics.Butteraugli_Raw?.ToString("F4")}/{cachedMetrics.Butteraugli_3norm?.ToString("F4")}, " +
                        $"GMSD={cachedMetrics.GMSD?.ToString("F4")}, " +
                        $"XPSNR Y={cachedMetrics.XPSNR_Y?.ToString("F4")}, W={cachedMetrics.W_XPSNR?.ToString("F4")}");
                }

                return (cachedMetrics.SSIM, cachedMetrics, cacheKey);

            }

            // ---------- 全新计算 ----------
            QualityMetrics? metrics = null;
            try
            {
                metrics = await ComputeAllMetricsAsync(workingInputPath, outputPath);
            }
            catch (Exception ex) { _logger.LogError($"多指标计算异常: {ex.Message}"); }

            if (metrics != null)
            {
                // XPSNR
                // XPSNR 后台异步计算
                var xKey2 = cacheKey;
                _xpsnrTasks.Enqueue(Task.Run(async () =>
                {
                    try
                    {
                        var (y, u, v, weighted) = await ComputeXPSNRAsync(
                            workingInputPath, outputPath, "yuv444p");
                        _cache.UpdateMetrics(xKey2, m =>
                        {
                            m.XPSNR_Y = y; m.XPSNR_U = u;
                            m.XPSNR_V = v; m.W_XPSNR = weighted;
                        });
                        _logger.LogInfo(
                            $"XPSNR 后台完成: Y={y?.ToString("F4")}, " +
                            $"U={u?.ToString("F4")}, V={v?.ToString("F4")}, " +
                            $"W={weighted?.ToString("F4")}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogInfo($"XPSNR 后台异常: {ex.Message}");
                    }
                }));

                // 高级指标（改为异步后台执行）
                bool advancedUpdated = false;
                {
                    bool needSsimu2 = !metrics.SSIMULACRA2.HasValue;
                    bool needButter = !metrics.Butteraugli_3norm.HasValue || !metrics.Butteraugli_Raw.HasValue;
                    bool needGmsd = !metrics.GMSD.HasValue;

                    if (needSsimu2 || needButter || needGmsd)
                    {
                        var bgTask = ComputeAdvancedMetricsInBackgroundAsync(
                            workingInputPath, outputPath, _outputDir, cacheKey,
                            needSsimu2, needButter, needGmsd,
                            _globalCts?.Token ?? CancellationToken.None);
                        _advancedMetricTasks.Enqueue(bgTask);
                    }
                }

                _cache.SetMetrics(cacheKey, metrics);
                if (advancedUpdated)
                    _logger.LogInfo($"高级指标补充: SSIMULACRA2={metrics.SSIMULACRA2?.ToString("F4")}, Butteraugli={metrics.Butteraugli_Raw?.ToString("F4")}/{metrics.Butteraugli_3norm?.ToString("F4")}, GMSD={metrics.GMSD?.ToString("F4")}");

                return (metrics.SSIM, metrics, cacheKey);
            }

            // 回退 SSIM 单一缓存
            if (_cache.TryGetSSIM(cacheKey, out double cachedSsim) && cachedSsim >= 0)
                return (cachedSsim, null, cacheKey);

            double ssim = await CalcSSIMAsync(workingInputPath, outputPath, encodeResult.ActualPixFmt);
            if (ssim >= 0) _cache.SetSSIM(cacheKey, ssim);

            return (ssim, null, cacheKey);
        }


        /// <summary>
        /// 无损验证：将 AVIF 解码为 raw rgba，与原图全量逐字节比对。
        /// 返回 null 表示完全一致（bit-exact），否则返回详细失败报告。
        /// </summary>
        private async Task<FailedVerificationInfo?> VerifyLosslessAsync(
            string refPath, string avifPath, string name, int width)
        {
            try
            {
                string refArgs =
                    $"-v error -i \"{refPath}\" -f rawvideo " +
                    $"-pix_fmt rgba -";
                var (refOk, refRaw) = await RunFfmpegToMemoryAsync(
                    refArgs, TimeSpan.FromMinutes(2));
                if (!refOk || refRaw.Length == 0)
                {
                    _logger.LogInfo($"无损验证：无法解码原图 ({name})");
                    return new FailedVerificationInfo
                    {
                        FailureType = VerificationFailureType.SizeMismatch
                    };
                }

                string avifArgs =
                    $"-v error -i \"{avifPath}\" -f rawvideo " +
                    $"-pix_fmt rgba -";
                var (avifOk, avifRaw) = await RunFfmpegToMemoryAsync(
                    avifArgs, TimeSpan.FromMinutes(2));
                if (!avifOk || avifRaw.Length == 0)
                {
                    _logger.LogInfo($"无损验证：无法解码 AVIF ({name})");
                    return new FailedVerificationInfo
                    {
                        FailureType = VerificationFailureType.SizeMismatch
                    };
                }

                if (refRaw.Length != avifRaw.Length)
                {
                    _logger.LogInfo(
                        $"无损验证：像素数据长度不同 " +
                        $"ref={refRaw.Length} avif={avifRaw.Length} ({name})");
                    return new FailedVerificationInfo
                    {
                        FailureType = VerificationFailureType.SizeMismatch,
                        MismatchCount = Math.Abs(refRaw.Length - avifRaw.Length)
                    };
                }

                // 全量扫描：统计 mismatch 数量、最大偏差、通道分别计数、采集差异采样
                int mismatchCount = 0;
                int maxDelta = 0;
                int rCount = 0, gCount = 0, bCount = 0, aCount = 0;
                int firstMismatchX = -1, firstMismatchY = -1;
                int firstRefVal = 0, firstOutVal = 0;
                int firstChannelIdx = 0;
                bool firstFound = false;
                var samples = new List<MismatchSample>();
                const int maxSamples = 500;

                for (int i = 0; i < refRaw.Length; i++)
                {
                    byte refB = refRaw[i];
                    byte avifB = avifRaw[i];
                    if (refB != avifB)
                    {
                        mismatchCount++;
                        int delta = Math.Abs(refB - avifB);
                        if (delta > maxDelta)
                        {
                            maxDelta = delta;
                        }

                        int channelIdx = i % 4;
                        switch (channelIdx)
                        {
                            case 0: rCount++; break;
                            case 1: gCount++; break;
                            case 2: bCount++; break;
                            case 3: aCount++; break;
                        }

                        // 采集差异采样（均匀间隔，最多 maxSamples 条）
                        if (samples.Count < maxSamples || mismatchCount % (mismatchCount / maxSamples + 1) == 0)
                        {
                            if (samples.Count < maxSamples)
                            {
                                int pixel = i / 4;
                                samples.Add(new MismatchSample
                                {
                                    X = width > 0 ? pixel % width : pixel,
                                    Y = width > 0 ? pixel / width : 0,
                                    Channel = channelIdx switch { 0 => "R", 1 => "G", 2 => "B", _ => "A" },
                                    RefValue = refB,
                                    OutValue = avifB,
                                    Delta = delta
                                });
                            }
                        }

                        if (!firstFound)
                        {
                            firstFound = true;
                            int pixel = i / 4;
                            firstMismatchX = width > 0 ? pixel % width : pixel;
                            firstMismatchY = width > 0 ? pixel / width : 0;
                            firstRefVal = refB;
                            firstOutVal = avifB;
                            firstChannelIdx = channelIdx;
                        }
                    }
                }

                if (mismatchCount == 0)
                {
                    return null;   // 通过
                }

                // 失败分类
                VerificationFailureType failureType;
                int totalPixels = refRaw.Length / 4;
                if (rCount + gCount + bCount == 0 && aCount > 0)
                {
                    failureType = VerificationFailureType.AlphaMismatch;
                }
                else if (rCount + gCount + bCount > 0 && aCount == 0)
                {
                    failureType = VerificationFailureType.ChromaMismatch;
                }
                else if (mismatchCount > totalPixels / 2)
                {
                    failureType = VerificationFailureType.MassiveMismatch;
                }
                else
                {
                    failureType = VerificationFailureType.PixelMismatch;
                }

                string firstChannel = firstChannelIdx switch
                {
                    0 => "R", 1 => "G", 2 => "B", _ => "A"
                };

                _logger.LogInfo(
                    $"✘ 无损验证失败 ({name})：" +
                    $"FailureType={failureType} " +
                    $"Mismatches={mismatchCount} MaxDelta={maxDelta} " +
                    $"FirstAt=({firstMismatchX},{firstMismatchY}) " +
                    $"Channel={firstChannel} " +
                    $"ref=0x{firstRefVal:X2} out=0x{firstOutVal:X2} " +
                    $"Breakdown=[R:{rCount} G:{gCount} B:{bCount} A:{aCount}]");

                double totalPx = totalPixels;
                return new FailedVerificationInfo
                {
                    FailureType = failureType,
                    MismatchCount = mismatchCount,
                    MaxDelta = maxDelta,
                    FirstMismatchX = firstMismatchX,
                    FirstMismatchY = firstMismatchY,
                    FirstMismatchChannel = firstChannel,
                    RefValue = firstRefVal,
                    OutValue = firstOutVal,
                    RMismatches = rCount,
                    GMismatches = gCount,
                    BMismatches = bCount,
                    AMismatches = aCount,
                    // JSON 扩展字段
                    MismatchRatio = totalPx > 0 ? mismatchCount / totalPx : 0,
                    RPct = totalPx > 0 ? rCount * 100.0 / mismatchCount : 0,
                    GPct = totalPx > 0 ? gCount * 100.0 / mismatchCount : 0,
                    BPct = totalPx > 0 ? bCount * 100.0 / mismatchCount : 0,
                    APct = totalPx > 0 ? aCount * 100.0 / mismatchCount : 0,
                    MismatchSamples = samples,
                };
            }
            catch (Exception ex)
            {
                _logger.LogInfo($"无损验证异常: {ex.Message} ({name})");
                return new FailedVerificationInfo
                {
                    FailureType = VerificationFailureType.MassiveMismatch
                };
            }
        }

        /// <summary>
        /// 运行 ffmpeg 并将 stdout 输出读入内存字节数组。
        /// </summary>
        private async Task<(bool ok, byte[] data)> RunFfmpegToMemoryAsync(
            string args, TimeSpan timeout)
        {
            try
            {
                using var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _ffmpegPath,
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                process.Start();

                using var ms = new System.IO.MemoryStream();
                var copyTask = process.StandardOutput.BaseStream
                    .CopyToAsync(ms);
                var stderrTask = process.StandardError
                    .ReadToEndAsync();

                using var cts = new CancellationTokenSource(timeout);
                await Task.WhenAny(
                    Task.WhenAll(copyTask,
                        process.WaitForExitAsync(cts.Token)),
                    Task.Delay(timeout));

                if (!process.HasExited)
                {
                    try { process.Kill(); } catch { }
                    return (false, Array.Empty<byte>());
                }

                await copyTask;
                return (process.ExitCode == 0, ms.ToArray());
            }
            catch
            {
                return (false, Array.Empty<byte>());
            }
        }

        /// <summary> 生成差异热力图：解码原图与 AVIF，计算 abs(diff)，输出为增强可见性的 PNG </summary>
        private async Task<string?> GenerateDiffHeatmapPngAsync(
            string refPath, string avifPath, string diffOutputPath)
        {
            try
            {
                // 解码原图为 raw RGBA
                string refArgs =
                    $"-v error -i \"{refPath}\" -f rawvideo " +
                    $"-pix_fmt rgba -";
                var (refOk, refRaw) = await RunFfmpegToMemoryAsync(
                    refArgs, TimeSpan.FromMinutes(2));
                if (!refOk || refRaw.Length == 0)
                {
                    return null;
                }

                // 解码 AVIF 为 raw RGBA
                string avifArgs =
                    $"-v error -i \"{avifPath}\" -f rawvideo " +
                    $"-pix_fmt rgba -";
                var (avifOk, avifRaw) = await RunFfmpegToMemoryAsync(
                    avifArgs, TimeSpan.FromMinutes(2));
                if (!avifOk || avifRaw.Length == 0)
                {
                    return null;
                }

                int minLen = Math.Min(refRaw.Length, avifRaw.Length);
                int pixelCount = minLen / 4;

                // 创建差异缓冲区：每像素 RGBA，差异值放大 4 倍以增强可见性
                byte[] diffRgba = new byte[pixelCount * 4];
                for (int i = 0; i < minLen; i++)
                {
                    int delta = Math.Abs(refRaw[i] - avifRaw[i]);
                    // 放大 4 倍使微小差异可见，上限 255
                    byte enhanced = (byte)Math.Min(delta * 4, 255);
                    diffRgba[i] = enhanced;
                }

                // 获取图像宽高用于 ffmpeg raw 编码
                var (w, h) = await GetResolutionAsync(refPath);
                if (w <= 0 || h <= 0)
                {
                    return null;
                }

                // 写临时 raw 文件
                string tempRaw = Path.Combine(
                    Path.GetDirectoryName(diffOutputPath) ?? ".",
                    $"_diff_raw_{Guid.NewGuid():N}.rgba");
                await _fs.WriteAllBytesAsync(tempRaw, diffRgba);

                try
                {
                    string args =
                        $"-y -loglevel error " +
                        $"-f rawvideo -pix_fmt rgba -s {w}x{h} " +
                        $"-i \"{tempRaw}\" " +
                        $"-frames:v 1 \"{diffOutputPath}\"";
                    var (ok, _) = await RunFfmpegExAsync(
                        _ffmpegPath, args, TimeSpan.FromMinutes(1));
                    return ok ? diffOutputPath : null;
                }
                finally
                {
                    if (_fs.FileExists(tempRaw))
                    {
                        try { _fs.DeleteFile(tempRaw); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogInfo($"Diff heatmap 生成异常: {ex.Message}");
                return null;
            }
        }

        private EncodeResult FailResult(int index, string outputFileName, string name,
                                    string inputPath, string error, DateTime fileStartTime)
        {
            var result = new EncodeResult
            {
                Index = index,
                FileName = outputFileName,
                OriginalFileName = name,
                InputPath = inputPath,                  // ★ 记录原始输入路径
                Success = false,
                ErrorMessage = error,
                TotalTime = DateTime.Now - fileStartTime
            };
            MarkProcessed(result);
            return result;
        }

        private EncodeResult BuildResult(int index, string outputFileName, string name,
string inputPath, string outputPath,
FinalEncodeResult encodeResult, CRFSearchResult searchResult,
EncodingInfo encInfo, double ssim, QualityMetrics? metrics, DateTime fileStartTime, string? advancedCacheKey = null)
        {
            // ★ 检测是否是“搜索已失败且最终被迫使用 CRF=0（MinCRF=0）”的情景
            bool crf0Unreachable = encodeResult.Success
                                   && !searchResult.SearchBasedCRF
                                   && searchResult.Crf == 0
                                   && _config.MinCRF == 0;

            var result = new EncodeResult
            {
                Index = index,
                FileName = outputFileName,
                OriginalFileName = name,
                InputPath = inputPath,
                OriginalSize = encodeResult.Success ? _fs.GetFileLength(inputPath) : 0,
                OutputSize = encodeResult.Success ? _fs.GetFileLength(outputPath) : 0,
                UsedCRF = encodeResult.Success ? encodeResult.Crf : -1,
                FinalSSIM = ssim,
                EncodeTime = encodeResult.EncodeTime,
                SearchTime = searchResult.SearchTime,
                TotalTime = DateTime.Now - fileStartTime,
                Retries = encodeResult.Retries,
                // 若 CRF=0 仍不达标，即便编码过程成功了也标记为失败
                Success = encodeResult.Success && !crf0Unreachable,
                ErrorMessage = crf0Unreachable
                    ? "CRF=0 仍无法达到质量目标，编码已用最佳质量但未达标"
                    : encodeResult.FailReason,
                PixelFormat = encodeResult.Success ? encodeResult.ActualPixFmt : "",
                SourcePixelFormat = encInfo.SourcePixFmt,
                Mode = _config.AutoSource ? "自适应" : "手动",
                IsSafeMode = encodeResult.UseSafeMode,
                AomParamsUsed = encodeResult.ActualAom ?? "",
                CacheReused = encodeResult.FromCache,
                CommandLine = encodeResult.FinalCommand ?? "",
                FinalVMAF = metrics?.VMAF,
                FinalPSNR_Y = metrics?.PSNR_Y,
                FinalMSSSIM = metrics?.MS_SSIM,
                FinalMixScore = metrics == null ? null : ComputeMixScore(metrics),

                // ---- 新增：XPSNR 分数 ----
                FinalXPSNR_Y = metrics?.XPSNR_Y,
                FinalXPSNR_U = metrics?.XPSNR_U,
                FinalXPSNR_V = metrics?.XPSNR_V,
                FinalWXPSNR = metrics?.W_XPSNR,

                FinalSSIMULACRA2 = metrics?.SSIMULACRA2,
                FinalButteraugli_Raw = metrics?.Butteraugli_Raw,
                FinalButteraugli_3norm = metrics?.Butteraugli_3norm,
                FinalGMSD = metrics?.GMSD,

                SearchEvaluations = searchResult.SearchEvalCount
            };
            result.AdvancedMetricsCacheKey = advancedCacheKey;

            // 标注 AOM 参数降级
            string expectedAom = _config.GetEffectiveAomParams();
            string actualAomUsed = encodeResult.ActualAom ?? "";
            if (result.Success && expectedAom.Length > 0 && actualAomUsed != expectedAom)
            {
                result.ErrorMessage = string.IsNullOrEmpty(result.ErrorMessage)
                    ? "AOM参数已降级（编码器未使用完整参数）"
                    : result.ErrorMessage + " | AOM参数已降级";
            }

            MarkProcessed(result);
            return result;
        }

        // ==================== 辅助方法 ====================

        // 1. 跳过已存在文件
        private async Task<EncodeResult?> TrySkipExistingOutputAsync(string inputPath, int index, PresetConfig config, bool isRetry)
        {
            if (isRetry) return null;

            string outputPath = GetOutputPath(inputPath, index);
            if (_fs.FileExists(outputPath))
            {
                // 覆盖模式：不跳过，继续编码（覆盖旧文件）
                if (config.FileConflictStrategy == PresetConfig.ConflictStrategy.Overwrite)
                    return null;

                // 跳过模式：直接返回已存在的文件信息
                string name = Path.GetFileName(inputPath);
                SafeWriteLine($"[SKIP] {name} (已存在，跳过)");
                _logger.LogInfo($"跳过: {name}");
                var skipResult = new EncodeResult
                {
                    Index = index,
                    FileName = Path.GetFileName(outputPath),
                    OriginalFileName = name,
                    InputPath = inputPath,
                    OriginalSize = _fs.GetFileLength(inputPath),
                    OutputSize = _fs.GetFileLength(outputPath),
                    Skipped = true
                };
                MarkProcessed(skipResult);
                return skipResult;
            }
            return null;
        }

        // 2. 准备编码基础信息
        // 2. 准备编码基础信息
        private async Task<EncodingInfo?> PrepareEncodingInfoAsync(string inputPath, PresetConfig config)
        {
            string name = Path.GetFileName(inputPath);
            bool isLosslessMode = config.Lossless;
            bool isTrulyLossless = isLosslessMode;   // ★ 已修改
            string srcFmt = await GetSourcePixelFormat(inputPath);
            bool hasAlpha = await SourceHasAlpha(inputPath);
            string actualPixFmt = await GetPixelFormatForFileAsync(inputPath, isLosslessMode, hasAlpha);
            // ===== 补全缺失的 pixInfo、w、h =====
            string pixInfo;
            if (config.AutoSource && !isLosslessMode)
                pixInfo = $"源: {srcFmt} -> 输出: {actualPixFmt}";
            else
                pixInfo = actualPixFmt;
            var (w, h) = await GetResolutionAsync(inputPath);
            if (w == 0 || h == 0) return null;

            // 硬件编码器 Alpha 警告等保持原有逻辑
            bool alphaDropped = false;
            if (hasAlpha && !config.Encoder.StartsWith("lib", StringComparison.OrdinalIgnoreCase))
            {
                hasAlpha = false;
                alphaDropped = true;
                actualPixFmt = actualPixFmt.Replace("a", "");
                SafeWriteLine($" [WARN] [{name}] 硬件编码器不支持 Alpha 通道，透明度将被丢弃");
                _logger.LogInfo($"Alpha 通道丢弃: {name}，编码器 {config.Encoder} 不支持 yuva 格式");
            }

            // 硬件编码器色度采样警告（原有逻辑）
            if (!config.Encoder.StartsWith("lib", StringComparison.OrdinalIgnoreCase))
            {
                bool is420 = actualPixFmt.Contains("420");
                if (!is420)
                {
                    SafeWriteLine($" [WARN] [{name}] 硬件编码器通常只支持 4:2:0，程序将自动尝试降级。");
                }
            }

            // ─── 计算合法的 tileCols ───
            // 在 PrepareEncodingInfoAsync 中，替换 tileCols 计算部分：
            int tileCols = 0;
            if (!isTrulyLossless)
            {
                // 基础性能推荐值（单核时设为 0，避免强制分块）
                tileCols = Environment.ProcessorCount > 1
                           ? Math.Clamp((int)Math.Log2(Environment.ProcessorCount), 1, 4)
                           : 0;
                // 小图保护（任何一边小于256）
                if (tileCols > 0 && (w < 256 || h < 256))
                    tileCols = 0;
                // 合法性强制约束：tile 宽度不能超过 4096
                int minLegalCols = GetMinLegalTileCols(w);
                // ★ 合法性强制约束：tile 宽度不能小于 256（libaom 实现限制）
                int maxLegalCols = GetMaxLegalTileCols(w);

                if (minLegalCols > maxLegalCols)   // 图像太小，无法满足任何 tile 要求
                    tileCols = 0;
                else
                    tileCols = Math.Clamp(tileCols, minLegalCols, maxLegalCols);
            }
            // ★ 新增：强制关闭分块并行
            //对于大分辨率图片无法转换，已修改
            // 极限压缩模式：只保留 AV1 规范允许的必要瓦片分割，关闭额外并行
            if (config.SerialEncode)
            {
                // 宽度 ≤ 4096 时无需瓦片，tileCols = 0；
                // 宽度 > 4096 时取最小合法列数，确保每个 tile 宽度 ≤ 4096。
                tileCols = GetMinLegalTileCols(w);
            }

            int crf = config.BaseCRF;
            if (isLosslessMode && !isTrulyLossless) crf = 0;

            return new EncodingInfo
            {
                SourcePixFmt = srcFmt,
                ActualPixFmt = actualPixFmt,
                PixInfo = pixInfo + (alphaDropped ? " (Alpha 已丢弃)" : ""),
                Width = w,
                Height = h,
                IsTrulyLossless = isTrulyLossless,
                IsLosslessMode = isLosslessMode,
                TileCols = tileCols,
                BaseCrf = crf,
                HasAlpha = hasAlpha
            };
        }
    }
}
