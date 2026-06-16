using System.Collections.Concurrent;
using System.Text.Json;

namespace AvifEncoder
{
    public partial class AvifPipeline
    {
        private readonly record struct ResumeEncodedInfo(
            string InputPath,
            string OutputPath,
            int Crf,
            string PixFmt,
            string? ActualAom,
            string? CommandLine);

        private readonly ConcurrentDictionary<string, ResumeEncodedInfo> _resumeEncodedFiles =
            new(StringComparer.OrdinalIgnoreCase);

        private async Task WaitForBackgroundMetricTasksAsync(
            TimeSpan? timeout,
            string context,
            bool requeueUnfinished)
        {
            var pending = new List<(Task Task, bool IsXpsnr)>();
            while (_advancedMetricTasks.TryDequeue(out var advTask))
                pending.Add((advTask, false));
            while (_xpsnrTasks.TryDequeue(out var xpsnrTask))
                pending.Add((xpsnrTask, true));

            if (pending.Count == 0)
                return;

            try
            {
                var allTasks = Task.WhenAll(pending.Select(p => p.Task));
                if (timeout.HasValue)
                    await allTasks.WaitAsync(timeout.Value);
                else
                    await allTasks;
            }
            catch (Exception ex)
            {
                _logger.LogInfo($"[{context}] 后台指标等待异常: {ex.Message}");
                if (requeueUnfinished)
                {
                    foreach (var item in pending.Where(p => !p.Task.IsCompleted))
                    {
                        if (item.IsXpsnr)
                            _xpsnrTasks.Enqueue(item.Task);
                        else
                            _advancedMetricTasks.Enqueue(item.Task);
                    }
                }
            }
        }

        private string GetBaseOutputPathNoReserve(string inputFilePath, int index)
        {
            string safeInputDir = NormalizePathForExternalTool(_inputDir);
            string safeInputPath = NormalizePathForExternalTool(inputFilePath);
            string relPath = Path.GetRelativePath(safeInputDir, safeInputPath);
            string? relDir = Path.GetDirectoryName(relPath);
            string targetDir = string.IsNullOrEmpty(relDir)
                ? _outputDir
                : Path.Combine(_outputDir, relDir);
            return Path.Combine(targetDir, GetOutputFileName(inputFilePath, index));
        }

        private static ResumeEncodedInfo CreateResumeEncodedInfo(string inputPath, JsonElement root)
        {
            static string? GetString(JsonElement element, string name)
                => element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
                    ? value.GetString()
                    : null;

            int crf = -1;
            if (root.TryGetProperty("crf", out var crfEl) && crfEl.ValueKind == JsonValueKind.Number)
                crfEl.TryGetInt32(out crf);

            return new ResumeEncodedInfo(
                inputPath,
                GetString(root, "outputpath") ?? "",
                crf,
                GetString(root, "pixfmt") ?? "",
                GetString(root, "actualaom"),
                GetString(root, "commandline"));
        }

        private void SaveJournalBackedSnapshot(string context)
        {
            // ★ CAS 守卫：与 AppendJournal 自动快照互斥，防止并发写损坏 snapshot.json
            if (Interlocked.CompareExchange(ref _snapshotInProgress, 1, 0) != 0) return;
            try
            {
                var (completed, metrics, _, _) = ReplayJournalWithMetrics(0);
                _logger.LogInfo(
                    $"[{context}] 保存 journal 快照: completed={completed.Count} metrics={metrics.Count}");
                SaveSnapshot(completed, metrics);
            }
            finally
            {
                Interlocked.Exchange(ref _snapshotInProgress, 0);
            }
        }
    }
}
