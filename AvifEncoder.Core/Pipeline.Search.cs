using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static AvifEncoder.PresetConfig;

namespace AvifEncoder
{
    partial class AvifPipeline
    {









        /// <summary>
        /// 使用极快的编码参数进行代理评估，返回 0?1 分数（与 getScore 一致）。
        /// 失败返回 -1。
        /// </summary>
        /// <summary>
        /// 使用极快的编码参数进行代理评估，返回 0?1 分数。
        /// 失败返回 -1。
        /// </summary>
        private async Task<double> ProxyEvaluateAsync(string input, int crf,
        int tileCols, PresetConfig cfg, bool jpeg, string pixFmt)
        {
            // Proxy 始终使用 yuv420p + cpu-used 6 + 最小稳定参数
            var proxyCfg = new PresetConfig
            {
                Encoder = cfg.Encoder,
                BaseCRF = crf,
                FinalCpuUsed = 6,
                SearchCpuUsed = 6,
                PixelFormat = "yuv420p",
                Lossless = false,
                AomParams = "aq-mode=0:enable-cdef=0",
                MaxJobs = cfg.MaxJobs,
                BitDepth = cfg.BitDepth,
                SerialEncode = cfg.SerialEncode   // ← 新属性名（传递极限压缩设置）
            };

            string tmpOutput = Path.Combine(_outputDir, $"_proxy_{Guid.NewGuid():N}.avif");
            try
            {
                var encResult = await EncodeToFileExAsync(input, tmpOutput, crf,
                    tileCols, proxyCfg.FinalCpuUsed, proxyCfg, jpeg, "yuv420p",
                    isTrueLossless: false, timeoutMinutes: cfg.SearchEncodeTimeoutMinutes,
                    allowParamDegrade: false);

                if (!encResult.ok || !_fs.FileExists(tmpOutput) || _fs.GetFileLength(tmpOutput) < 100)
                    return -1;

                QualityMetrics? m = await ComputeAllMetricsAsync(input, tmpOutput);
                if (m == null) return -1;

                return GetSearchScore(m, cfg.MetricMode ?? "vmaf");
            }
            finally
            {
                if (_fs.FileExists(tmpOutput)) try { _fs.DeleteFile(tmpOutput); } catch { }
            }
        }



        /// <summary>
        /// 数据驱动混合搜索：中位数初始化 + 保守 Proxy 验证 + 安全二分
        /// 始终在用户指定范围（或全范围）内搜索，保证全局最优。
        /// </summary>
        /// <summary>
        /// 混合搜索：默认仅中位数初始化 + 标准二分；可选开启保守 Proxy 验证。
        /// 不再执行 MaxCRF 早停，评估次数精确统计。
        /// </summary>
        /// <summary>
        /// 混合搜索：默认基于先验表直接划定搜索区间，使用标准二分，无需 Proxy。
        /// 若启用 --proxy，则保留保守 Proxy 验证（沿用原有 PerformConservativeProxyPhaseAsync）。
        /// </summary>
        /// <summary>
        /// 数据驱动混合搜索（默认模式）：
        /// 1. 根据先验表获取中位数 CRF，执行一次真实评估。
        /// 2. 若中位数达标 → 向右二分 [median, userMax]（已知下界，不重复测 median）。
        /// 3. 若中位数不达标 → 向左二分 [userMin, median-1]（验证下界 userMin）。
        /// 4. 若仍未找到可行解，回退到安全模式全扫描（兜底离群值）。
        /// 若启用 --proxy，则保留保守 Proxy 验证流程。
        /// </summary>
        /// <summary>
        /// 按目标 VMAF 动态返回最优哨兵偏移量（基于 400 张图片统计）
        /// </summary>
        private static int GetOptimalSentinelDelta(int targetVmafInt)
        {
            return targetVmafInt switch
            {
                90 => 4,                                   // 中位数 38 时最优
                >= 91 and <= 95 => 2,                     // 分布最集中，极小偏移最优
                96 => 3,                                   // 高离散度目标
                _ => 3                                     // 安全默认
            };
        }

        /// <summary>
        /// 混合搜索：先验中位数 + 动态哨兵探测 + 标准二分
        /// </summary>
        /// <summary>
        /// 混合搜索：先验中位数 + 动态哨兵探测 + 标准二分
        /// </summary>
        /// <summary>
        /// 混合搜索：先验中位数 + 动态哨兵探测 + 标准二分（可通过 --prior-search 启用）
        /// </summary>
        private async Task<(int crf, bool searchFailed, bool qualityInsufficient, int evalCount)> HybridSearchCRFAsync(
    string input, int tileCols, PresetConfig cfg, string pixFmt, bool jpeg, string? displayName = null)
        {
            string name = displayName ?? Path.GetFileName(input);
            string metricMode = cfg.MetricMode ?? "vmaf";
            double target;
            double margin;
            bool lowerIsBetter = PresetConfig.IsMetricLowerBetter(metricMode);

            // 获取统一原生目标值
            double effectiveTarget = cfg.GetEffectiveTarget();

            // 选择 margin（原生值尺度）
            if (cfg.XpsnrTargetValue.HasValue) margin = 0.01;
            else if (cfg.Ssimu2TargetValue.HasValue) margin = 0.2;
            else if (cfg.Butteraugli3TargetValue.HasValue) margin = 0.01;
            else if (cfg.GmsdTargetValue.HasValue) margin = 0.001;
            else margin = metricMode switch
            {
                "vmaf" => 0.05,
                "psnr" => 0.01,
                _ => SSIMMargin
            };

            // 计算搜索用的判定阈值（原生值）
            if (cfg.XpsnrTargetValue.HasValue)
                target = effectiveTarget - margin;
            else if (cfg.Butteraugli3TargetValue.HasValue || cfg.GmsdTargetValue.HasValue)
                target = effectiveTarget + margin;
            else
                target = effectiveTarget + margin;

            // 控制台显示
            string targetDisplay = FormatScore(effectiveTarget, metricMode);
            SafeWriteLine($"  [{name}] [SEARCH] 混合搜索开始 (目标={targetDisplay})");

            using var searchCts = new CancellationTokenSource(TimeSpan.FromMinutes(cfg.SearchTimeoutMinutes));
            var token = CancellationTokenSource.CreateLinkedTokenSource(searchCts.Token, _globalCts?.Token ?? default).Token;

            Func<int, Task<double>> getScore = BuildGetScoreFunc(input, tileCols, cfg, pixFmt, jpeg, name, token);

            // ★ 对于越小越好的指标，将分数取反，使 “>= target” 仍然有效
            if (lowerIsBetter)
            {
                var originalGetScore = getScore;
                getScore = async crf =>
                {
                    double s = await originalGetScore(crf);
                    return s >= 0 ? -s : s;      // 负值表示失败，不反转
                };
                target = -target;
            }

            // 辅助函数：将内部反转后的分数恢复为原始值用于控制台显示
            Func<double, double> displayScore = s => lowerIsBetter && s != -1 ? -s : s;

            int totalEvalCount = 0;
            int userMin = cfg.MinCRF;
            int userMax = cfg.MaxCRF;

            // ────────── 先验搜索未启用：直接全范围二分 ──────────
            if (!cfg.UsePriorSearch)
            {
                SafeWriteLine($"  [{name}] [INFO] 先验搜索已关闭，使用标准二分区间 [{userMin}, {userMax}]");
                var (directBestCrf, directEval) = await StandardBinarySearch(
                    input, tileCols, cfg, pixFmt, jpeg, name, target, getScore, token,
                    userMin, userMax, knownLoScore: null, lowerIsBetter: lowerIsBetter);
                totalEvalCount = directEval;

                if (directBestCrf >= 0)
                {
                    SafeWriteLine($"  [{name}] [DONE] 搜索完成，最优 CRF={directBestCrf}，总评估 {totalEvalCount} 次");
                    return (directBestCrf, false, false, totalEvalCount);
                }

                if (userMin == 0)
                {
                    SafeWriteLine($"  [{name}] [FAIL] 所有搜索均失败且 MinCRF=0，跳过安全扫描，将使用 CRF=0 最终编码");
                    return (cfg.BaseCRF, true, false, totalEvalCount);
                }

                SafeWriteLine($"  [{name}] [FALLBACK] 标准二分无解，启动安全模式全扫描 (范围=[{userMin},{userMax}])");
                var (safeOk, safeCrf, _, _) = await RunSafeModeScan(input, cfg, name, userMin, userMax);
                if (safeOk)
                {
                    SafeWriteLine($"  [{name}] [FALLBACK] 安全扫描成功，CRF={safeCrf}");
                    return (safeCrf, false, false, totalEvalCount);
                }

                SafeWriteLine($"  [{name}] [FAIL] 所有搜索均失败，回退到 BaseCRF={cfg.BaseCRF}");
                return (cfg.BaseCRF, true, false, totalEvalCount);
            }

            // ────────── 先验搜索启用 ──────────
            int priorMedian = (userMin + userMax) / 2;
            if (metricMode == "vmaf")
            {
                double targetVmaf = target * 100.0;
                var (median, _, _) = VmafPriorHelper.GetPriorFromVmaf(targetVmaf);
                priorMedian = Math.Clamp(median, userMin, userMax);
            }

            int searchLo, searchHi;
            double? knownLoScore = null;

            // ── Proxy 模式 ──
            if (cfg.UseProxySearch)
            {
                SafeWriteLine($"  [{name}] [PRIOR] 先验中位数={priorMedian}");

                var (safeLo, safeHi) = await PerformConservativeProxyPhaseAsync(
                    input, tileCols, cfg, pixFmt, jpeg, name, target, metricMode, token,
                    priorMedian, userMin, userMax);

                searchLo = (safeLo >= 0 && safeHi >= safeLo) ? Math.Max(userMin, safeLo) : userMin;
                searchHi = (safeLo >= 0 && safeHi >= safeLo) ? Math.Min(userMax, safeHi) : userMax;

                SafeWriteLine($"  [{name}] [INFO] 二分区间: [{searchLo}, {searchHi}]");

                var (proxyCrf, proxyEval) = await StandardBinarySearch(
                    input, tileCols, cfg, pixFmt, jpeg, name, target, getScore, token,
                    searchLo, searchHi, knownLoScore: null, lowerIsBetter: lowerIsBetter);
                totalEvalCount += proxyEval;

                if (proxyCrf >= 0)
                {
                    SafeWriteLine($"  [{name}] [DONE] 搜索完成，最优 CRF={proxyCrf}，总评估 {totalEvalCount} 次");
                    return (proxyCrf, false, false, totalEvalCount);
                }

                if (userMin == 0)
                {
                    SafeWriteLine($"  [{name}] [FAIL] 所有搜索均失败且 MinCRF=0，跳过安全扫描，将使用 CRF=0 最终编码");
                    return (cfg.BaseCRF, true, false, totalEvalCount);
                }

                SafeWriteLine($"  [{name}] [FALLBACK] Proxy 区间无解，启动安全模式全扫描 (范围=[{userMin},{userMax}])");
                var (safeOk, safeCrf, _, _) = await RunSafeModeScan(input, cfg, name, userMin, userMax);
                if (safeOk)
                {
                    SafeWriteLine($"  [{name}] [FALLBACK] 安全扫描成功，CRF={safeCrf}");
                    return (safeCrf, false, false, totalEvalCount);
                }

                SafeWriteLine($"  [{name}] [FAIL] 所有搜索均失败，回退到 BaseCRF={cfg.BaseCRF}");
                return (cfg.BaseCRF, true, false, totalEvalCount);
            }

            // ── 默认先验模式：中位数 + 哨兵 + 二分 ──
            SafeWriteLine($"  [{name}] [PRIOR] 先验中位数 CRF={priorMedian} ...");
            double medianScore = await getScore(priorMedian);
            totalEvalCount++;
            string medianDisplay = metricMode == "vmaf" ? $"VMAF={displayScore(medianScore):F4}" : $"分数={displayScore(medianScore):F4}";
            SafeWriteLine($"  [{name}] [PRIOR] CRF={priorMedian} → {medianDisplay}");

            if (metricMode == "vmaf" && medianScore >= 0)
            {
                int delta = GetOptimalSentinelDelta((int)Math.Round(target * 100.0));
                if (delta > 0)
                {
                    if (medianScore >= target)
                    {
                        int probe = Math.Min(priorMedian + delta, userMax);
                        if (probe > priorMedian)
                        {
                            SafeWriteLine($"  [{name}] [SENTINEL] 哨兵探测 CRF={probe} ...");
                            double probeScore = await getScore(probe);
                            totalEvalCount++;
                            string probeDisplay = metricMode == "vmaf" ? $"VMAF={displayScore(probeScore):F4}" : $"分数={displayScore(probeScore):F4}";
                            SafeWriteLine($"  [{name}] [SENTINEL] CRF={probe} → {probeDisplay}");

                            if (probeScore >= target)
                            {
                                searchLo = probe;
                                searchHi = userMax;
                                knownLoScore = probeScore;
                            }
                            else
                            {
                                searchLo = priorMedian;
                                searchHi = probe - 1;
                                knownLoScore = medianScore;
                            }
                        }
                        else
                        {
                            searchLo = priorMedian;
                            searchHi = userMax;
                            knownLoScore = medianScore;
                        }
                    }
                    else
                    {
                        int probe = Math.Max(priorMedian - delta, userMin);
                        if (probe < priorMedian)
                        {
                            SafeWriteLine($"  [{name}] [SENTINEL] 哨兵探测 CRF={probe} ...");
                            double probeScore = await getScore(probe);
                            totalEvalCount++;
                            string probeDisplay = metricMode == "vmaf" ? $"VMAF={displayScore(probeScore):F4}" : $"分数={displayScore(probeScore):F4}";
                            SafeWriteLine($"  [{name}] [SENTINEL] CRF={probe} → {probeDisplay}");

                            if (probeScore >= target)
                            {
                                searchLo = probe;
                                searchHi = priorMedian - 1;
                                knownLoScore = probeScore;
                            }
                            else
                            {
                                searchLo = userMin;
                                searchHi = probe - 1;
                                knownLoScore = null;
                            }
                        }
                        else
                        {
                            searchLo = userMin;
                            searchHi = priorMedian - 1;
                            knownLoScore = null;
                        }
                    }
                }
                else
                {
                    searchLo = medianScore >= target ? priorMedian : userMin;
                    searchHi = medianScore >= target ? userMax : priorMedian - 1;
                    knownLoScore = medianScore >= target ? medianScore : (double?)null;
                }
            }
            else
            {
                searchLo = medianScore >= target ? priorMedian : userMin;
                searchHi = medianScore >= target ? userMax : priorMedian - 1;
                knownLoScore = medianScore >= target ? medianScore : (double?)null;
            }

            SafeWriteLine($"  [{name}] [INFO] 二分区间: [{searchLo}, {searchHi}] {(knownLoScore.HasValue ? "(下界已知可行)" : "(需验证下界)")}");
            if (knownLoScore.HasValue)
                SafeWriteLine($"  [{name}] [CORE] 下界已知可行 CRF={searchLo} ({FormatScore(displayScore(knownLoScore.Value), metricMode)})");

            var (bestCrf, binEval) = await StandardBinarySearch(
                input, tileCols, cfg, pixFmt, jpeg, name, target, getScore, token,
                searchLo, searchHi, knownLoScore: knownLoScore, lowerIsBetter: lowerIsBetter);
            totalEvalCount += binEval;

            if (bestCrf >= 0)
            {
                SafeWriteLine($"  [{name}] [DONE] 搜索完成，最优 CRF={bestCrf}，总评估 {totalEvalCount} 次");
                return (bestCrf, false, false, totalEvalCount);
            }

            if (userMin == 0)
            {
                SafeWriteLine($"  [{name}] [FAIL] 所有搜索均失败且 MinCRF=0，跳过安全扫描，将使用 CRF=0 最终编码");
                return (cfg.BaseCRF, true, false, totalEvalCount);
            }

            SafeWriteLine($"  [{name}] [FALLBACK] 二分未找到可行解，启动安全模式全扫描 (范围=[{userMin},{userMax}])");
            var (safeOk2, safeCrf2, _, _) = await RunSafeModeScan(input, cfg, name, userMin, userMax);
            if (safeOk2)
            {
                SafeWriteLine($"  [{name}] [FALLBACK] 安全扫描成功，CRF={safeCrf2}");
                return (safeCrf2, false, false, totalEvalCount);
            }

            SafeWriteLine($"  [{name}] [FAIL] 所有搜索均失败，回退到 BaseCRF={cfg.BaseCRF}");
            return (cfg.BaseCRF, true, false, totalEvalCount);
        }

        // 注意：FormatScore 方法保持不变（已在其他地方定义）

        // 辅助格式化方法（可放在同一类中）
        /// <summary>
        /// 将内部 TargetSSIM (0-1) 反算为各指标的原生目标值。
        /// </summary>
        private static string FormatScore(double score, string metricMode)
        {
            if (metricMode.StartsWith("xpsnr", StringComparison.OrdinalIgnoreCase))
                return $"XPSNR={score:F4} dB";
            return metricMode == "vmaf" ? $"VMAF={score:F4}" : $"分数={score:F4}";
        }





        /// <summary>
        /// 在 [lo, hi] 区间内执行标准右边界二分，并附带右侧单调扫描。
        /// 返回 (最优CRF, 本阶段真实评估次数)。若下界不可行或全部失败返回 (-1, 评估次数)。
        /// 每一步均通过控制台和日志输出。
        /// </summary>
        /// <summary>
        /// 在 [lo, hi] 区间内使用标准右边界二分查找满足目标的最大 CRF。
        /// 区间内的每一个测试点都通过 getScore 获取真实分数，评估次数精确统计。
        /// 若没有任何点达标，返回 (-1, 评估次数)。
        /// 每一步均输出到控制台和日志。
        /// </summary>
        /// <summary>
        /// 标准右边界二分：在 [lo, hi] 区间内找到满足目标的最大 CRF。
        /// 若提供 knownLoScore（且 >= target），则跳过 lo 的评估，直接从 lo+1 开始搜索。
        /// 每一步均输出到控制台与日志。
        /// 返回 (最优CRF, 本阶段评估次数)。若无任何可行点，返回 (-1, evalCount)。
        /// </summary>
        internal static async Task<(int bestCrf, int evalCount)> StandardBinarySearch(
    string input, int tileCols, PresetConfig cfg, string pixFmt, bool jpeg,
    string name, double target, Func<int, Task<double>> getScore,
    CancellationToken token, int lo, int hi, double? knownLoScore = null,
    bool lowerIsBetter = false)
        {
            int evalCount = 0;
            int bestCrf = -1;

            // 已知下界可行：直接记录，不评估
            if (knownLoScore.HasValue && knownLoScore.Value >= target)
            {
                bestCrf = lo;
                double displayKnown = lowerIsBetter && knownLoScore.Value != -1 ? -knownLoScore.Value : knownLoScore.Value;
                string loDisplay = cfg.MetricMode == "vmaf" ? $"VMAF={displayKnown:F4}" : $"分数={displayKnown:F4}";
                SafeWriteLine($"  [{name}] [CORE] 下界已知可行 CRF={lo} ({loDisplay})");
            }

            int l = bestCrf >= 0 ? bestCrf + 1 : lo;
            int r = hi;

            while (l <= r)
            {
                token.ThrowIfCancellationRequested();
                int mid = (l + r) / 2;
                SafeWriteLine($"  [{name}] [BIN] 测试 CRF={mid} (区间 {l}-{r})...");
                double score = await getScore(mid);
                evalCount++;

                double displayMid = lowerIsBetter && score != -1 ? -score : score;
                string midDisplay = cfg.MetricMode == "vmaf" ? $"VMAF={displayMid:F4}" : $"分数={displayMid:F4}";
                SafeWriteLine($"  [{name}] [BIN] CRF={mid} → {midDisplay}");

                if (score >= target)
                {
                    bestCrf = mid;
                    l = mid + 1;
                }
                else
                {
                    r = mid - 1;
                }
            }

            if (bestCrf >= 0)
                SafeWriteLine($"  [{name}] [CORE] 二分结束，最优 CRF={bestCrf}，本阶段评估 {evalCount} 次");
            else
                SafeWriteLine($"  [{name}] [CORE] 二分结束，区间内无可行点，评估 {evalCount} 次");

            return (bestCrf, evalCount);
        }



        /// <summary>
        /// 保守 Proxy 阶段：评估中位数附近 3 个点（median-2, median, median+2），
        /// 仅当分数 > target + 0.02 时才视为“明确通过”。
        /// 返回 (safeLo, safeHi) 均钳制在 [globalMin, globalMax] 内。
        /// 若 Proxy 全部失败或无法判断，返回 (-1, -1)。
        /// </summary>
        private async Task<(int safeLo, int safeHi)> PerformConservativeProxyPhaseAsync(
            string input, int tileCols, PresetConfig cfg, string pixFmt, bool jpeg,
            string name, double target, string metricMode, CancellationToken token,
            int priorMedian, int globalMin, int globalMax)
        {
            int median = Math.Clamp(priorMedian, globalMin, globalMax);
            var testCrfs = new[] { median - 2, median, median + 2 }
                .Where(c => c >= globalMin && c <= globalMax)
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            if (testCrfs.Count == 0)
                return (globalMin, globalMax);

            bool anyPass = false;
            int lastPass = -1;
            double passMargin = 0.02;  // 保守余量

            foreach (int crf in testCrfs)
            {
                token.ThrowIfCancellationRequested();
                SafeWriteLine($"  [{name}] [PROXY] 快速验证 CRF={crf} ...");
                double proxyScore = await ProxyEvaluateAsync(input, crf, tileCols, cfg, jpeg, pixFmt);
                if (proxyScore < 0)
                {
                    SafeWriteLine($"  [{name}] [PROXY] CRF={crf} 评估失败，跳过");
                    continue;
                }

                bool pass = proxyScore >= target + passMargin;
                string status = pass ? "明确通过" : "保守失败";
                string display = metricMode == "vmaf" ? $"VMAF={proxyScore:F4}" : $"分数={proxyScore:F4}";
                SafeWriteLine($"  [{name}] [PROXY] CRF={crf} → {display} ({status})");

                if (pass)
                {
                    anyPass = true;
                    if (crf > lastPass) lastPass = crf;
                }
            }

            if (anyPass)
            {
                // 至少一个明确通过 → 下界设为最后一个通过点，上界向右扩展 6 个 CRF
                int safeLo = lastPass;
                int safeHi = Math.Min(globalMax, lastPass + 6);
                return (safeLo, safeHi);
            }
            else
            {
                // 全部未明确通过 → 最优解可能在左侧，向左扩展 6
                int safeLo = Math.Max(globalMin, median - 6);
                int safeHi = median - 1;
                if (safeHi < safeLo) safeHi = safeLo;
                return (safeLo, safeHi);
            }
        }
        private Func<int, Task<double>> BuildGetScoreFunc(string input, int tileCols, PresetConfig cfg, string pixFmt, bool jpeg, string name, CancellationToken token)
        {
            int consecutiveFailures = 0;
            const int failThreshold = 2;
            string normalizedKey = GetNormalizedPathForCache(input);

            return async crf =>
            {
                // 提前致命短路：若该文件的当前 pixFmt 已被标记为致命，直接失败
                // 提前致命短路：若该文件的当前 pixFmt 已被标记为致命，直接失败
                if (_fatalFmts.TryGetValue(normalizedKey, out var fatalSet) && fatalSet.ContainsKey(pixFmt))
                {
                    _logger.LogInfo($"?? 致命格式 {pixFmt} 已禁用，跳过 CRF={crf} [{name}]");
                    return -1;
                }

                for (int i = 0; i < 3; i++)
                {
                    token.ThrowIfCancellationRequested();

                    QualityMetrics? m = null;

                    if (consecutiveFailures < failThreshold)
                    {
                        m = await GetOrComputeMetrics(input, crf, tileCols, cfg.SearchCpuUsed, cfg, jpeg, pixFmt);
                        if (m != null) { consecutiveFailures = 0; return GetSearchScore(m, cfg.MetricMode ?? "ssim"); }

                        m = await GetOrComputeMetrics(input, crf, tileCols, Math.Max(0, cfg.SearchCpuUsed - 1), cfg, jpeg, pixFmt);
                        if (m != null) { consecutiveFailures = 0; return GetSearchScore(m, cfg.MetricMode ?? "ssim"); }
                    }

                    // 仅在 yuv420p 未被标记致命时才降级尝试
                    if (!pixFmt.StartsWith("yuv420p") && (!_fatalFmts.TryGetValue(normalizedKey, out var fs) || !fs.ContainsKey("yuv420p")))
                    {
                        m = await GetOrComputeMetrics(input, crf, tileCols, cfg.SearchCpuUsed, cfg, jpeg, "yuv420p");
                        if (m != null) { consecutiveFailures = 0; return GetSearchScore(m, cfg.MetricMode ?? "ssim"); }
                    }
                    else
                    {
                        // 当前格式就是 yuv420p 或已被致命标记，尝试降速
                        m = await GetOrComputeMetrics(input, crf, tileCols, 0, cfg, jpeg, pixFmt);
                        if (m != null) { consecutiveFailures = 0; return GetSearchScore(m, cfg.MetricMode ?? "ssim"); }
                    }

                    if (i < 2)
                        _logger.LogInfo($"真实指标重试 ({i + 1}/2): {name} CRF={crf}");
                }

                consecutiveFailures++;
                if (consecutiveFailures >= failThreshold)
                    _logger.LogInfo($"连续失败达到阈值，后续 CRF 点将优先使用降级参数 [{name}]");

                return -1;
            };
        }















        /// <summary>
        /// 获取图像分辨率，优先从统一 Probe 缓存获取。
        /// </summary>
        private async Task<(int w, int h)> GetResolutionAsync(string path)
        {
            // 优先从统一 Probe 缓存获取
            var info = await GetProbeInfoAsync(path);
            if (info != null)
            {
                return (info.Width, info.Height);
            }

            // 兜底：单独探测
            string args = $"-v error -select_streams v:0 -show_entries stream=width,height -of csv=p=0 \"{path}\"";
            string o = await RunProbeAsync(_ffprobePath, args).WaitAsync(TimeSpan.FromSeconds(30));
            var parts = o.Trim().Split(',');
            if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
            {
                return (w, h);
            }
            return (0, 0);
        }

        private static string GetCsvRow(EncodeResult r)
        {
            string status = r.Skipped ? "跳过" : (r.Success ? "成功" : "失败");
            string errMsg = CsvEscape(r.ErrorMessage);
            string fmt = r.PixelFormat ?? "";
            string srcFmt = r.SourcePixelFormat ?? "";
            string mode = r.Mode ?? "";
            string safe = r.IsSafeMode ? "是" : "否";
            string command = CsvEscape(r.CommandLine ?? "");
            string aomParams = CsvEscape(r.AomParamsUsed ?? "");
            string cache = r.CacheReused ? "是" : "否";

            string vmaf = FormatMetric(r.FinalVMAF);
            string psnrY = FormatDbValue(r.FinalPSNR_Y);
            string msssim = FormatMetric(r.FinalMSSSIM);
            string mix = FormatMetric(r.FinalMixScore);

            var values = new[]
            {
        CsvEscape(r.FileName),
        CsvEscape(r.OriginalFileName),
        r.OriginalSize.ToString(CultureInfo.InvariantCulture),
        r.OutputSize.ToString(CultureInfo.InvariantCulture),
        FormatMetric(r.CompressionRatio),
        r.UsedCRF.ToString(CultureInfo.InvariantCulture),
        FormatMetric(r.FinalSSIM),
        vmaf,
        psnrY,
        msssim,
        mix,
        FormatDbValue(r.FinalXPSNR_Y),
        FormatDbValue(r.FinalXPSNR_U),
        FormatDbValue(r.FinalXPSNR_V),
        FormatDbValue(r.FinalWXPSNR),
        FormatMetric(r.FinalSSIMULACRA2),
        FormatMetric(r.FinalButteraugli_Raw),
        FormatMetric(r.FinalButteraugli_3norm),
        FormatMetric(r.FinalGMSD),

        FormatMetric(r.EncodeTime.TotalSeconds),
        FormatMetric(r.SearchTime.TotalSeconds),
        FormatMetric(r.TotalTime.TotalSeconds),
        r.Retries.ToString(CultureInfo.InvariantCulture),
        CsvEscape(fmt),
        CsvEscape(srcFmt),
        CsvEscape(mode),
        CsvEscape(safe),
        aomParams,
        command,
        CsvEscape(cache),
        CsvEscape(status),
        errMsg,
        r.SearchEvaluations.ToString(CultureInfo.InvariantCulture)   // ★ 新增
    };

            return string.Join(",", values);
        }

        /// <summary> 线程安全追加一行到 CSV。首次写入时自动写表头。 </summary>
        private void AppendCsvRow(EncodeResult r)
        {
            lock (_csvLock)
            {
                if (!_csvHeaderWritten)
                {
                    _fs.WriteAllText(_csvPath,
                        string.Join(",", CsvColumnNames) + "\n",
                        new UTF8Encoding(true));
                    _csvHeaderWritten = true;
                }

                _fs.AppendAllText(_csvPath, GetCsvRow(r) + "\n");
            }
        }

        private void ExportCsv(IEnumerable<EncodeResult> results)
        {
            string p = Path.Combine(_outputDir, "avif_stats.csv");
            var sb = new StringBuilder();

            sb.AppendLine(string.Join(",", CsvColumnNames));

            foreach (var r in results)
            {
                sb.AppendLine(GetCsvRow(r));
            }

            lock (_csvLock)
            {
                _fs.WriteAllText(p, sb.ToString(), new UTF8Encoding(true));
                _csvHeaderWritten = true;
            }
            SafeWriteLine($"CSV 已保存: {p}");
        }

        private static string FormatSize(long b) => b switch
        {
            >= 1_048_576 => $"{b / 1_048_576.0:F2} MB",
            >= 1024 => $"{b / 1024.0:F2} KB",
            _ => $"{b} B"
        };

        /// <summary>格式化普通指标值，保留完整原生精度，不做四舍五入截断</summary>
        private static string FormatMetric(double? value)
        {
            if (!value.HasValue) return "";
            if (double.IsNaN(value.Value)) return "";
            if (double.IsPositiveInfinity(value.Value)) return int.MaxValue.ToString();
            return value.Value.ToString("G", CultureInfo.InvariantCulture);
        }

        /// <summary>格式化普通指标值</summary>
        private static string FormatMetric(double value)
        {
            if (double.IsNaN(value)) return "";
            if (double.IsPositiveInfinity(value)) return int.MaxValue.ToString();
            return value.ToString("G", CultureInfo.InvariantCulture);
        }

        /// <summary>格式化 dB 值，正无穷显示为 int.MaxValue 哨兵值，否则保留完整精度</summary>
        private static string FormatDbValue(double? value)
        {
            if (!value.HasValue) return "";
            if (double.IsPositiveInfinity(value.Value)) return int.MaxValue.ToString();
            if (double.IsNaN(value.Value)) return "";
            return value.Value.ToString("G", CultureInfo.InvariantCulture);
        }

        private static string FormatTimeSpan(TimeSpan t) => t switch
        {
            { TotalHours: >= 1 } => $"{(int)t.TotalHours}h {t.Minutes}m {t.Seconds}s",
            { TotalMinutes: >= 1 } => $"{(int)t.TotalMinutes}m {t.Seconds}s",
            _ => $"{t.TotalSeconds:F4}s"
        };
    }
}
