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

                QualityMetrics? m = await ComputeAllMetricsAsync(input, tmpOutput,
                    isAnimated: _isAnimatedFile.Value, hasAlpha: await SourceHasAlpha(input));
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
            // 保守策略：搜索条件比用户目标更严格，确保最终编码达标
            // - 越大越好（VMAF/SSIM/PSNR/XPSNR/SSIMU2）：target = effectiveTarget + margin → 要求更高分数
            // - 越小越好（Butteraugli/GMSD）：target = effectiveTarget - margin → 要求更低分数
            if (cfg.XpsnrTargetValue.HasValue)
                target = effectiveTarget + margin;
            else if (cfg.Butteraugli3TargetValue.HasValue || cfg.GmsdTargetValue.HasValue)
                target = effectiveTarget - margin;
            else
                target = effectiveTarget + margin;

            // 控制台显示
            string targetDisplay = FormatScore(effectiveTarget, metricMode);
            SafeWriteLine($"  [{name}] [SEARCH] 混合搜索开始 (目标={targetDisplay})");

            using var searchCts = new CancellationTokenSource(TimeSpan.FromMinutes(cfg.SearchTimeoutMinutes));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(searchCts.Token, _globalCts?.Token ?? default);
            var token = linkedCts.Token;

            Func<int, Task<double>> getScore = BuildGetScoreFunc(input, tileCols, cfg, pixFmt, jpeg, name, token);

            // ★ 对于越小越好的指标，将分数取反，使 “>= target” 仍然有效
            if (lowerIsBetter)
            {
                var originalGetScore = getScore;
                getScore = async crf =>
                {
                    double s = await originalGetScore(crf);
                    return NormalizeLowerIsBetterSearchScore(s);
                };
                target = -target;
            }

            // 辅助函数：将内部反转后的分数恢复为原始值用于控制台显示
            Func<double, double> displayScore = s => lowerIsBetter && !double.IsNaN(s) ? -s : s;
            Func<double, string> fmtScore = s =>
            {
                double native = lowerIsBetter && !double.IsNaN(s) ? -s : s;
                return FormatScore(native, metricMode);
            };

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
            if (VmafPriorHelper.HasTable(metricMode))
            {
                // 传入原生尺度目标值（越小越好指标用取反前的值）
                double nativeTarget = lowerIsBetter ? -target : target;
                var (median, _, _) = VmafPriorHelper.GetPrior(metricMode, nativeTarget);
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
            string medianDisplay = fmtScore(medianScore);
            SafeWriteLine($"  [{name}] [PRIOR] CRF={priorMedian} → {medianDisplay}");

            if (VmafPriorHelper.HasTable(metricMode) && medianScore >= 0)
            {
                double nativeTarget = lowerIsBetter ? -target : target;
                // ★ 中位数远偏离目标时哨兵探测价值低：|score-target| > 2*margin 说明
                //    中位数大概率在同侧所有点都达标/不达标，哨兵探测不会缩小区间。跳过省 1 次编码。
                double sentinelMargin = lowerIsBetter ? 0.2 : metricMode == "vmaf" ? 1.0 : 0.02;
                bool skipSentinel = Math.Abs(displayScore(medianScore) - effectiveTarget) > 2 * sentinelMargin;
                if (skipSentinel)
                {
                    SafeWriteLine($"  [{name}] [SENTINEL] 中位数偏离目标过大，跳过哨兵探测");
                    searchLo = medianScore >= target ? priorMedian : userMin;
                    searchHi = medianScore >= target ? userMax : priorMedian - 1;
                    knownLoScore = medianScore >= target ? medianScore : (double?)null;
                }
                else
                {
                int delta = VmafPriorHelper.GetSentinelDelta(metricMode, nativeTarget);
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
                            string probeDisplay = fmtScore(probeScore);
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
                            string probeDisplay = fmtScore(probeScore);
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
                }  // end sentinel skip else
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

        internal static double NormalizeLowerIsBetterSearchScore(double score)
            => score >= 0 ? -score : double.NaN;

        // 注意：FormatScore 方法保持不变（已在其他地方定义）

        // 辅助格式化方法（可放在同一类中）
        /// <summary>
        /// 将内部 TargetSSIM (0-1) 反算为各指标的原生目标值。
        /// </summary>
        private static string FormatScore(double score, string metricMode)
        {
            string mode = (metricMode ?? "vmaf").ToLowerInvariant();
            return mode switch
            {
                "vmaf" => $"VMAF={score:F4}",
                "ssim" => $"SSIM={score:F6}",
                "msssim" => $"MS-SSIM={score:F6}",
                "psnr" => $"PSNR={score:F4} dB",
                "ssimu2" => $"SSIMU2={score:F2}",
                "butter3" => $"Butter3={score:F4}",
                _ when mode.StartsWith("xpsnr") => $"XPSNR={score:F4} dB",
                _ => $"分数={score:F4}",
            };
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
                double displayKnown = lowerIsBetter && !double.IsNaN(knownLoScore.Value) ? -knownLoScore.Value : knownLoScore.Value;
                string loDisplay = FormatScore(displayKnown, cfg.MetricMode);
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

                double displayMid = lowerIsBetter && !double.IsNaN(score) ? -score : score;
                string midDisplay = FormatScore(displayMid, cfg.MetricMode);
                SafeWriteLine($"  [{name}] [BIN] CRF={mid} → {midDisplay}");

                // ★ NaN（评估失败）跳过该点并向两侧各尝试一步，避免 O(2^n) 递归
                if (double.IsNaN(score))
                {
                    l++;     // 跳过当前点，向右收缩左边界
                    continue;
                }
                else if (score >= target)
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
            double passMargin = metricMode switch
            {
                "gmsd"   => 0.0005,  // GMSD 原生值 0.001~0.01，0.02 远超其尺度
                "butter3"=> 0.1,     // Butteraugli 原生值 0.3~3.0
                _        => 0.02    // VMAF/SSIM/PSNR/XPSNR
            };

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

                // ★ ProxyEvaluateAsync 返回原生值（未取反），target 已在 HybridSearchCRFAsync 中取反
                //    对越小越好指标统一取反 proxyScore，与 target 在同一尺度上比较
                bool lowerIsBetter = PresetConfig.IsMetricLowerBetter(metricMode);
                bool pass = lowerIsBetter
                    ? proxyScore >= 0 && -proxyScore >= target + passMargin
                    : proxyScore >= target + passMargin;
                string status = pass ? "明确通过" : "保守失败";
                string display = FormatScore(proxyScore, metricMode);
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
        /// <summary>
        /// 单次 CRF 评估：给定参数，编码并计算质量分数。负责一次原子尝试，不包含降级/重试。
        /// 降级策略（重试、格式降级、cpu-used 降速）由调用方 BuildGetScoreFunc 控制。
        /// </summary>
        private async Task<CrfEvaluationResult> EvaluateSingleCrfAsync(
            string input, int crf, int tileCols, int cpuUsed, PresetConfig cfg,
            bool jpeg, string pixFmt)
        {
            QualityMetrics? m = await GetOrComputeMetrics(input, crf, tileCols, cpuUsed, cfg, jpeg, pixFmt);
            if (m != null)
            {
                return CrfEvaluationResult.Ok(
                    GetSearchScore(m, cfg.MetricMode ?? "ssim"),
                    pixFmt, cpuUsed, fromCache: false);
            }
            return CrfEvaluationResult.Failed;
        }

        private Func<int, Task<double>> BuildGetScoreFunc(string input, int tileCols, PresetConfig cfg, string pixFmt, bool jpeg, string name, CancellationToken token)
        {
            int consecutiveFailures = 0;
            const int failThreshold = 2;
            string normalizedKey = EncodeHelpers.GetNormalizedPathForCache(input);

            return async crf =>
            {
                // 提前致命短路：若该文件的当前 pixFmt 已被标记为致命，直接失败
                if (_fatalFmts.TryGetValue(normalizedKey, out var fatalSet) && fatalSet.ContainsKey(pixFmt))
                {
                    _logger.LogInfo($"致命格式 {pixFmt} 已禁用，跳过 CRF={crf} [{name}]");
                    return -1;
                }

                for (int i = 0; i < 3; i++)
                {
                    token.ThrowIfCancellationRequested();

                    CrfEvaluationResult eval;

                    if (consecutiveFailures < failThreshold)
                    {
                        eval = await EvaluateSingleCrfAsync(input, crf, tileCols, cfg.SearchCpuUsed, cfg, jpeg, pixFmt);
                        if (eval.Success) { consecutiveFailures = 0; return eval.Score; }

                        eval = await EvaluateSingleCrfAsync(input, crf, tileCols, Math.Max(0, cfg.SearchCpuUsed - 1), cfg, jpeg, pixFmt);
                        if (eval.Success) { consecutiveFailures = 0; return eval.Score; }
                    }

                    // 仅在 yuv420p 未被标记致命时才降级尝试
                    if (!pixFmt.StartsWith("yuv420p") && (!_fatalFmts.TryGetValue(normalizedKey, out var fs) || !fs.ContainsKey("yuv420p")))
                    {
                        eval = await EvaluateSingleCrfAsync(input, crf, tileCols, cfg.SearchCpuUsed, cfg, jpeg, "yuv420p");
                        if (eval.Success) { consecutiveFailures = 0; return eval.Score; }
                    }
                    else
                    {
                        // 当前格式就是 yuv420p 或已被致命标记，尝试降速
                        eval = await EvaluateSingleCrfAsync(input, crf, tileCols, 0, cfg, jpeg, pixFmt);
                        // ★ 降级成功→重置计数器，下一次 CRF 恢复正常参数尝试
                        if (eval.Success) { consecutiveFailures = 0; return eval.Score; }
                    }

                    if (i < 2)
                        _logger.LogInfo($"真实指标重试 ({i + 1}/2): {name} CRF={crf}");
                }

                consecutiveFailures++;
                if (consecutiveFailures >= failThreshold)
                {
                    _logger.LogInfo($"连续失败达到阈值，后续 CRF 点将优先使用降级参数 [{name}]");
                    SafeWriteLine($"  [{name}] [WARN] 搜索评分连续失败 ≥{failThreshold} 次，已启用降级策略（降速/降格式）");
                }

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
            string args = $"-v error -select_streams v:0 -show_entries stream=width,height -of csv=p=0 \"{EncodeHelpers.EscapeArg(path)}\"";
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
        FormatMetric(r.BPP),
        r.Width.ToString(CultureInfo.InvariantCulture),
        r.Height.ToString(CultureInfo.InvariantCulture),
        r.IsAnimated ? "是" : "否",
        r.FrameCount.ToString(CultureInfo.InvariantCulture),
        r.Fps > 0 ? r.Fps.ToString("F2", CultureInfo.InvariantCulture) : "",
        r.UsedCRF.ToString(CultureInfo.InvariantCulture),
        CsvEscape(r.Encoder),
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
        // FormatMetric(r.FinalCAMBI),   // 暂不可用
        // FormatMetric(r.FinalADM),     // 暂不可用

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

        /// <summary> 线程安全追加一行到 CSV。使用内存缓冲区批量写入，减少磁盘系统调用。 </summary>
        private readonly StringBuilder _csvBuffer = new();
        private int _csvBufferCount;

        private void AppendCsvRow(EncodeResult r)
        {
            lock (_csvLock)
            {
                _csvBuffer.AppendLine(GetCsvRow(r));
                _csvBufferCount++;
                // ★ 每 10 行批量刷盘（从 50 降低以减少崩溃丢失窗口），session 结束时 FlushCsvBuffer() 兜底
                if (_csvBufferCount >= 10)
                    FlushCsvBuffer();
            }
        }

        /// <summary> 将 CSV 缓冲区刷入磁盘 </summary>
        private void FlushCsvBuffer()
        {
            if (_csvBuffer.Length > 0)
            {
                _fs.AppendAllTextWithHeader(
                    _csvPath,
                    string.Join(",", CsvColumnNames),
                    _csvBuffer.ToString(),
                    new UTF8Encoding(true));
                _csvBuffer.Clear();
                _csvBufferCount = 0;
            }
        }

        private void ExportCsv(IEnumerable<EncodeResult> results,
            Dictionary<string, QualityMetrics>? resumeMetrics = null)
        {
            lock (_csvLock)
            {
            // ★ 合并导出：读取已有 CSV + 用当前 results 更新/追加 + 用 resumeMetrics 修补旧行指标
            try
            {
                var newList = results.ToList();
                var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // 1. 读取已有 CSV 行（保留前次运行的数据）
                if (_fs.FileExists(_csvPath))
                {
                    try
                    {
                        var oldLines = File.ReadAllLines(_csvPath);
                        for (int i = 1; i < oldLines.Length; i++)
                        {
                            var cols = SplitCsvLine(oldLines[i]);
                            if (cols.Length > 0 && !string.IsNullOrEmpty(cols[0]))
                                merged[cols[0]] = oldLines[i];
                        }
                    }
                    catch { }
                }

                // 2. 用当前 results 覆盖/追加
                foreach (var r in newList)
                {
                    merged[r.FileName] = GetCsvRow(r);
                }

                // 3. 用 resume 恢复的指标修补旧行（那些前次运行已完成但指标未写入 CSV 的文件）
                if (resumeMetrics != null && resumeMetrics.Count > 0)
                {
                    int patched = 0;
                    foreach (var kv in resumeMetrics)
                    {
                        string fileName = Path.GetFileName(kv.Key);
                        // 在 merged 中查找匹配的旧行（仅靠 key 精确匹配，避免子串误匹配）
                        foreach (var mk in merged.Keys.ToList())
                        {
                            if (mk == fileName)
                            {
                                var cols = SplitCsvLine(merged[mk]);
                                if (cols.Length >= CsvColumnNames.Length)
                                {
                                    var m = kv.Value;
                                    void SetIf(int ci, string? v) { if (ci >= 0 && ci < cols.Length && v != null) cols[ci] = v; }
                                    SetIf(_colXpsnrY,  FormatDbValue(m.XPSNR_Y));
                                    SetIf(_colXpsnrU,  FormatDbValue(m.XPSNR_U));
                                    SetIf(_colXpsnrV,  FormatDbValue(m.XPSNR_V));
                                    SetIf(_colWXpsnr,  FormatDbValue(m.W_XPSNR));
                                    SetIf(_colSsimu2,  FormatMetric(m.SSIMULACRA2));
                                    SetIf(_colButterR, FormatMetric(m.Butteraugli_Raw));
                                    SetIf(_colButter3, FormatMetric(m.Butteraugli_3norm));
                                    SetIf(_colGmsd,    FormatMetric(m.GMSD));
                                    merged[mk] = string.Join(",", cols.Select(f => CsvEscape(f)));
                                    patched++;
                                }
                                break;
                            }
                        }
                    }
                    _logger.LogInfo($"[CSV-PATCH] resume 指标修补 {patched} 行");
                }

                // 4. 按文件名自然排序后写回
                var sb = new StringBuilder();
                sb.AppendLine(string.Join(",", CsvColumnNames));
                foreach (var key in merged.Keys.OrderBy(k => k, new NaturalComparer()))
                    sb.AppendLine(merged[key]);

                _fs.WriteAllTextAtomic(_csvPath, sb.ToString(), new UTF8Encoding(true));
                _logger.LogInfo($"[CSV-EXPORT] 合并导出: 旧行={merged.Count - newList.Count} + 新行={newList.Count} = 总计 {merged.Count}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[CSV-EXPORT] 导出失败: {ex.Message}");
            }
            } // lock (_csvLock)
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
            if (double.IsPositiveInfinity(value)) return "";  // +Inf 输出空字符串，避免污染 CSV 统计
            return value.ToString("G", CultureInfo.InvariantCulture);
        }

        /// <summary>格式化 dB 值，+Inf 时输出空字符串避免污染 CSV 统计</summary>
        private static string FormatDbValue(double? value)
        {
            if (!value.HasValue) return "";
            if (double.IsPositiveInfinity(value.Value)) return "";  // +Inf 输出空字符串
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
