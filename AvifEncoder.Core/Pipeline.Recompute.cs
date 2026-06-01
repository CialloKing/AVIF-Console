using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AvifEncoder
{
    partial class AvifPipeline
    {
        private struct CsvEntry
        {
            public int RowIndex;
            public string OutputFile;
            public string SourceFile;
            public string[] Fields;
        }

        /// <summary> 解析单行 CSV（支持引号转义） </summary>
        private static string[] ParseCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            int start = 0;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    string field = line.Substring(start, i - start).Trim();
                    if (field.StartsWith('"') && field.EndsWith('"'))
                    {
                        field = field.Substring(1, field.Length - 2)
                            .Replace("\"\"", "\"");
                    }
                    result.Add(field);
                    start = i + 1;
                }
            }
            string last = line.Substring(start).Trim();
            if (last.StartsWith('"') && last.EndsWith('"'))
            {
                last = last.Substring(1, last.Length - 2).Replace("\"\"", "\"");
            }
            result.Add(last);
            return [.. result];
        }

        /// <summary>
        /// 重算模式：读取已有 avif_stats.csv，对缺失 SSIMULACRA2/Butteraugli/GMSD
        /// 的行补算高级指标，不重新编码。完成后写回 CSV。
        /// </summary>
        public async Task RunRecomputeMetricsAsync()
        {
            try
            {
                _globalCts = new CancellationTokenSource();
                _cancelKeyHandler = (s, e) =>
                {
                    e.Cancel = true;
                    SafeWriteLine("\n[WARN] 正在安全停止...");
                    _globalCts?.Cancel();
                };
                Console.CancelKeyPress += _cancelKeyHandler;

                string csvPath = Path.Combine(_outputDir, "avif_stats.csv");
                if (!_fs.FileExists(csvPath))
                {
                    SafeWriteLine($"[ERROR] 未找到 CSV: {csvPath}");
                    return;
                }

                SafeWriteLine("===== 高级指标重算模式 =====");
                SafeWriteLine($"输出文件夹: {_outputDir}");
                SafeWriteLine($"CSV 文件: {csvPath}");

                string csvContent = await _fs.ReadAllTextAsync(csvPath);
                var lines = csvContent.Split('\n',
                    StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length < 2)
                {
                    SafeWriteLine("[ERROR] CSV 为空或只有表头");
                    return;
                }

                string[] headers = ParseCsvLine(lines[0]);
                int idxFile = Array.IndexOf(headers, "文件名");
                int idxSrc = Array.IndexOf(headers, "原始文件名");
                int idxSsimu2 = Array.IndexOf(headers, "SSIMULACRA2");
                int idxButterR = Array.IndexOf(headers, "Butteraugli_Raw");
                int idxButter3 = Array.IndexOf(headers, "Butteraugli_3norm");
                int idxGmsd = Array.IndexOf(headers, "GMSD");
                int idxStatus = Array.IndexOf(headers, "状态");

                if (idxFile < 0 || idxSrc < 0)
                {
                    SafeWriteLine("[ERROR] CSV 缺少必要列（文件名/原始文件名）");
                    return;
                }

                var entries = new List<CsvEntry>();
                for (int row = 1; row < lines.Length; row++)
                {
                    string[] fields = ParseCsvLine(lines[row]);
                    if (fields.Length <= Math.Max(idxFile, idxSrc))
                    {
                        continue;
                    }

                    string status = idxStatus >= 0 &&
                        idxStatus < fields.Length ? fields[idxStatus] : "";
                    if (status == "失败" || status == "跳过")
                    {
                        continue;
                    }

                    bool hasMetrics = idxSsimu2 >= 0 &&
                        idxSsimu2 < fields.Length &&
                        !string.IsNullOrWhiteSpace(fields[idxSsimu2]);
                    if (hasMetrics)
                    {
                        continue;
                    }

                    entries.Add(new CsvEntry
                    {
                        RowIndex = row,
                        OutputFile = fields[idxFile],
                        SourceFile = fields[idxSrc],
                        Fields = fields
                    });
                }

                if (entries.Count == 0)
                {
                    SafeWriteLine("所有行均已有高级指标，无需重算。");
                    return;
                }

                SafeWriteLine($"\n找到 {entries.Count} 行需要补算高级指标\n");

                var srcIndex = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                var inputFiles = _fs.EnumerateFiles(_inputDir, "*.*",
                    SearchOption.AllDirectories);
                foreach (var f in inputFiles)
                {
                    string name = Path.GetFileName(f);
                    if (!srcIndex.ContainsKey(name))
                    {
                        srcIndex[name] = f;
                    }
                }

                int completed = 0;
                int failed = 0;
                var updatedFields = new ConcurrentDictionary<int,
                    (double? ssimu2, double? butterRaw,
                     double? butter3, double? gmsd)>();

                using var semaphore = new SemaphoreSlim(
                    Math.Max(1, Environment.ProcessorCount / 2));

                var tasks = entries.Select(async entry =>
                {
                    await semaphore.WaitAsync(_globalCts.Token);
                    try
                    {
                        if (!srcIndex.TryGetValue(entry.SourceFile,
                            out string? srcPath))
                        {
                            srcPath = Path.Combine(_inputDir, entry.SourceFile);
                            if (!_fs.FileExists(srcPath))
                            {
                                SafeWriteLine($"  [SKIP] 未找到源文件: {entry.SourceFile}");
                                Interlocked.Increment(ref failed);
                                return;
                            }
                        }

                        string avifPath = Path.Combine(_outputDir, entry.OutputFile);
                        if (!_fs.FileExists(avifPath))
                        {
                            SafeWriteLine($"  [SKIP] 未找到 AVIF: {entry.OutputFile}");
                            Interlocked.Increment(ref failed);
                            return;
                        }

                        string name = entry.SourceFile;
                        int current = Interlocked.Increment(ref completed);

                        string tempDir = Path.Combine(_outputDir,
                            $"_recompute_{Guid.NewGuid():N}");
                        _fs.CreateDirectory(tempDir);

                        try
                        {
                            double? ssimu2 = null;
                            double? butterRaw = null;
                            double? butter3 = null;
                            double? gmsd = null;

                            string? refPng = await ConvertToPngAsync(srcPath, tempDir);
                            string refClean = await SanitizePngIfNeededAsync(
                                srcPath, tempDir);

                            if (EncoderUtils.FindExecutable("ssimulacra2") != null
                                && refPng != null)
                            {
                                try
                                {
                                    string? distPng = await ConvertToPngAsync(
                                        avifPath, tempDir);
                                    if (distPng != null)
                                    {
                                        ssimu2 = await ComputeSSIMULACRA2Async(
                                            refPng, distPng);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogInfo($"SSIMULACRA2: {ex.Message}");
                                }
                            }

                            if (EncoderUtils.FindExecutable("butteraugli_main") != null
                                && refPng != null)
                            {
                                try
                                {
                                    string? distPng = await ConvertToPngAsync(
                                        avifPath, tempDir);
                                    if (distPng != null)
                                    {
                                        var (raw, p3) = await ComputeButteraugliAsync(
                                            refPng, distPng, tempDir);
                                        butterRaw = raw;
                                        butter3 = p3;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogInfo($"Butteraugli: {ex.Message}");
                                }
                            }

                            try
                            {
                                gmsd = await ComputeGMSDAsync(refClean, avifPath);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogInfo($"GMSD: {ex.Message}");
                            }

                            updatedFields[entry.RowIndex] = (
                                ssimu2, butterRaw, butter3, gmsd);

                            string statusStr = ssimu2.HasValue ||
                                butterRaw.HasValue || gmsd.HasValue ? "OK" : "全部缺失";
                            SafeWriteLine(
                                $"  [{current}/{entries.Count}] {name} " +
                                $"SSIMU2={ssimu2?.ToString("F4") ?? "-"} " +
                                $"Butter={butterRaw?.ToString("F4") ?? "-"}/" +
                                $"{butter3?.ToString("F4") ?? "-"} " +
                                $"GMSD={gmsd?.ToString("F4") ?? "-"} [{statusStr}]");
                        }
                        finally
                        {
                            try
                            {
                                if (_fs.DirectoryExists(tempDir))
                                {
                                    _fs.DeleteDirectory(tempDir, true);
                                }
                            }
                            catch { }
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        SafeWriteLine($"  [ERROR] {entry.SourceFile}: {ex.Message}");
                        Interlocked.Increment(ref failed);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);

                if (updatedFields.Count > 0)
                {
                    SafeWriteLine($"\n写回 CSV ({updatedFields.Count} 行)...");

                    var newLines = new List<string>();
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (updatedFields.TryGetValue(i, out var metrics))
                        {
                            string[] fields = ParseCsvLine(lines[i]);
                            if (idxSsimu2 >= 0 && idxSsimu2 < fields.Length)
                            {
                                fields[idxSsimu2] = metrics.ssimu2?.ToString("F4",
                                    CultureInfo.InvariantCulture) ?? "";
                            }
                            if (idxButterR >= 0 && idxButterR < fields.Length)
                            {
                                fields[idxButterR] = metrics.butterRaw?.ToString("F4",
                                    CultureInfo.InvariantCulture) ?? "";
                            }
                            if (idxButter3 >= 0 && idxButter3 < fields.Length)
                            {
                                fields[idxButter3] = metrics.butter3?.ToString("F4",
                                    CultureInfo.InvariantCulture) ?? "";
                            }
                            if (idxGmsd >= 0 && idxGmsd < fields.Length)
                            {
                                fields[idxGmsd] = metrics.gmsd?.ToString("F4",
                                    CultureInfo.InvariantCulture) ?? "";
                            }
                            newLines.Add(string.Join(",",
                                fields.Select(f =>
                                    f.Contains(',') || f.Contains('"')
                                        ? $"\"{f.Replace("\"", "\"\"")}\""
                                        : f)));
                        }
                        else
                        {
                            newLines.Add(lines[i]);
                        }
                    }

                    _fs.WriteAllText(csvPath, string.Join("\n", newLines) + "\n",
                        System.Text.Encoding.UTF8);
                    SafeWriteLine("CSV 已更新。");
                }

                SafeWriteLine(
                    $"\n完成: {completed} 成功, {failed} 失败, " +
                    $"{entries.Count - completed - failed} 跳过");
            }
            finally
            {
                _globalCts?.Dispose();
                _globalCts = null;
            }
        }
    }
}
