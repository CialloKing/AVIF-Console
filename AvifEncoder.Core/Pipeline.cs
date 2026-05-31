using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;   // ���ʹ�� System.Text.Json
using System.Text.RegularExpressions;


namespace AvifEncoder
{





    public enum CliPreset { Fast, Balanced, Best, Extreme }




    class ProbeInfo
    {
        public string PixFmt { get; set; } = "yuv420p";
        public bool HasAlpha { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        // ����ɫ��Ԫ�����ֶΣ�����Ϊ null/unknown��
        public string? ColorPrimaries { get; set; }
        public string? ColorTransfer { get; set; }
        public string? ColorSpace { get; set; }
        public string? ColorRange { get; set; }
    }




    /// <summary>����������ӿ�</summary>
    public interface ICacheManager
    {
        bool TryGetEncode(string key, out (string file, TimeSpan encodeTime, string commandLine) cached);
        void SetEncode(string key, string cacheFile, TimeSpan encodeTime, string commandLine);
        bool TryGetMetrics(string key, out QualityMetrics? metrics);
        void SetMetrics(string key, QualityMetrics metrics);
        /// <summary>ԭ�Ӹ��»����е� QualityMetrics��ȷ���̰߳�ȫ</summary>
        void UpdateMetrics(string key, Action<QualityMetrics> updateAction);
        bool TryGetSSIM(string key, out double ssim);
        void SetSSIM(string key, double ssim);
    }



    public class CacheManager : ICacheManager
    {
        private readonly ConcurrentDictionary<string, (string file, TimeSpan encodeTime, string commandLine)> _encodeCache = new();
        private readonly ConcurrentDictionary<string, QualityMetrics> _metricsCache = new();
        private readonly ConcurrentDictionary<string, double> _ssimCache = new();

        public bool TryGetEncode(string key, out (string file, TimeSpan encodeTime, string commandLine) cached)
            => _encodeCache.TryGetValue(key, out cached);

        public void SetEncode(string key, string cacheFile, TimeSpan encodeTime, string commandLine)
            => _encodeCache[key] = (cacheFile, encodeTime, commandLine);

        public bool TryGetMetrics(string key, out QualityMetrics? metrics)   // ��Ϊ QualityMetrics?
            => _metricsCache.TryGetValue(key, out metrics);

        public void SetMetrics(string key, QualityMetrics metrics)
            => _metricsCache[key] = metrics;

        /// <summary>
        /// �̰߳�ȫ�ظ��»����е� QualityMetrics ����
        /// �� key �������򴴽��¶����ִ�� updateAction��
        /// </summary>
        public void UpdateMetrics(string key, Action<QualityMetrics> updateAction)
        {
            _metricsCache.AddOrUpdate(key,
                _ =>
                {
                    var metrics = new QualityMetrics();
                    updateAction(metrics);
                    return metrics;
                },
                (_, existing) =>
                {
                    updateAction(existing);
                    return existing;
                });
        }

        public bool TryGetSSIM(string key, out double ssim)
            => _ssimCache.TryGetValue(key, out ssim);

        public void SetSSIM(string key, double ssim)
            => _ssimCache[key] = ssim;
    }






    public partial class AvifPipeline : IDisposable
    {
        #region �ֶ��빹��

        private readonly string _inputDir;
        private readonly string _outputDir;
        private readonly PresetConfig _config;
        private readonly int _maxRetries = 2;
        private readonly string _ffmpegPath;
        private readonly string _ffprobePath;


        private const double SSIMMargin = 0.0002;

        private readonly ProgressTracker _progress = new();

        private readonly IProgress<int>? _guiProgress;   // �� �����ֶΣ����� _progress ��ͻ

        private readonly ConcurrentDictionary<string, Task<double>> _ssimTasks = new();

        private readonly ICacheManager _cache;


        private readonly SemaphoreSlim _ssimConcurrency;
        private readonly SemaphoreSlim _ffmpegSlots;
        private readonly string _instanceId = Guid.NewGuid().ToString("N");
        private ConsoleCancelEventHandler? _cancelKeyHandler;

        private static readonly object _consoleLock = new();
        private CancellationTokenSource? _globalCts;

        private readonly ConcurrentDictionary<string, Task<QualityMetrics?>> _metricsTasks = new();

        private static void SafeWriteLine(string msg) { lock (_consoleLock) Console.WriteLine(msg); }

        private readonly ConcurrentDictionary<string, bool> _srcAlphaCache = new();

        private readonly int _maxFfmpegConcurrency;

        private int _disposed;

        private FileStream? _lockStream;

        private readonly IProcessRunner _processRunner;

        private readonly ILogger _logger;



        private readonly PresetConfig.IFileSystem _fs;   // ��Ϊ�����޶���

        // �ļ���ʧ�ܸ���������ǰδʹ�ã������Թ�������չ��
        private readonly ConcurrentDictionary<string, FileScopedFailTracker> _failTrackers = new();


        // ��¼ĳ�ļ���ĳ���ظ�ʽ�Ƿ��ѷ�������ȫ�޷�д�롱��������������������������
        // ��¼ĳ�ļ���ĳ���ظ�ʽ�Ƿ��ѷ�������ȫ�޷�д�롱��������������������������
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _fatalFmts = new();
        private readonly ConcurrentDictionary<string, byte> _allocatedOutputs = new();
        private readonly ConcurrentBag<System.Diagnostics.Process> _spawnedProcesses = new();


        private readonly ConcurrentQueue<Task> _advancedMetricTasks = new();
        private readonly ConcurrentQueue<Task> _xpsnrTasks = new();
        private readonly SemaphoreSlim _advancedMetricSemaphore;

        // ������֤�������
        private readonly object _failedCsvLock = new();
        private string _failedCsvPath = "";
        private string _failedVerificationDir = "";

        // CSV ����д��
        private readonly object _csvLock = new();
        private string _csvPath = "";
        private bool _csvHeaderWritten;

        // Journal �ϵ�����
        private string _journalPath = "";
        private string _snapshotPath = "";
        private StreamWriter? _journalWriter;
        private readonly object _journalLock = new();
        private int _journalCountSinceSnapshot;
        private DateTime _lastSnapshotTime;








        // ===== ���ߣ�������ͼƬתΪ PNG��SSIMULACRA2/Butteraugli ��Ҫ�� =====
        private async Task<string?> ConvertToPngAsync(string inputPath, string tempDir)
        {
            string tempPng = Path.Combine(tempDir, $"_tool_{Guid.NewGuid():N}.png");
            string cleanInput = NormalizePathForExternalTool(inputPath);
            string cleanOutput = NormalizePathForExternalTool(tempPng);
            // �� ʹ������֤�ɹ��������-y -loglevel error -i "����" -pix_fmt rgb24 -frames:v 1 "���"
            string args = $"-y -loglevel error -i \"{cleanInput}\" -pix_fmt rgb24 -frames:v 1 \"{cleanOutput}\"";
            var (ok, _) = await RunFfmpegExAsync(_ffmpegPath, args, TimeSpan.FromMinutes(1));
            return ok && _fs.FileExists(tempPng) ? tempPng : null;
        }


        // ===== PNG β����ϴ =====
        /// <summary>
        /// �� PNG �ļ� IEND ���ж����ֽڣ��򴴽���ϴ�����ʱ�ļ���������·����
        /// ���򷵻�ԭ·�������޸�ԭ�ļ�����
        /// </summary>
        private async Task<string> SanitizePngIfNeededAsync(string originalPath, string tempDir)
        {
            // ������ .png �ļ�
            if (!originalPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                return originalPath;

            byte[] bytes = await _fs.ReadAllBytesAsync(originalPath);
            int iendEnd = FindIendEndOffset(bytes);
            if (iendEnd < 0 || iendEnd == bytes.Length)
            {
                // û�ҵ� IEND ��ɾ��ļ���ֱ�ӷ���
                return originalPath;
            }

            // ��β��������������ϴ�汾
            string cleanFileName = $"_clean_{Guid.NewGuid():N}.png";
            string cleanPath = Path.Combine(tempDir, cleanFileName);
            byte[] cleanBytes = new byte[iendEnd];
            Array.Copy(bytes, cleanBytes, iendEnd);
            await _fs.WriteAllBytesAsync(cleanPath, cleanBytes);
            _logger.LogInfo($"PNG β����ϴ: {Path.GetFileName(originalPath)} �Ƴ� {bytes.Length - iendEnd} �ֽ� -> {cleanFileName}");
            return cleanPath;
        }

        /// <summary>
        /// ���� PNG �ļ��б�׼ IEND �������ƫ����������һ�������� PNG ���ֽ�λ�ã���
        /// ʧ�ܷ��� -1���ɾ��ļ������ļ��ܳ��ȡ�
        /// </summary>
        private static int FindIendEndOffset(byte[] bytes)
        {
            // ��׼ IEND chunk: ���� 0 (4 bytes) + "IEND" (4 bytes) + CRC (4 bytes)
            int limit = bytes.Length - 12; // ������Ҫ 8 �ֽڵĿ� + �����ܵ� CRC

            for (int i = 0; i <= limit; i++)
            {
                if (bytes[i] == 0x49 && bytes[i + 1] == 0x45 && bytes[i + 2] == 0x4E && bytes[i + 3] == 0x44)
                {
                    // �ҵ� "IEND"�����ǰ 4 �ֽ��Ƿ�Ϊ 0���鳤�ȱ���Ϊ 0��
                    if (i >= 4 && bytes[i - 4] == 0 && bytes[i - 3] == 0 && bytes[i - 2] == 0 && bytes[i - 1] == 0)
                    {
                        // IEND ����� = ������ʼ + 8������ + CRC��
                        return i + 8;
                    }
                }
            }

            // δ�ҵ��κ���Ч IEND ��
            return -1;
        }


        private async Task ComputeAdvancedMetricsInBackgroundAsync(
        string refPath, string distPath, string outputDir, string cacheKey,
        bool needSsimu2, bool needButter, bool needGmsd,
        CancellationToken cancellationToken)
        {
            await _advancedMetricSemaphore.WaitAsync(cancellationToken);
            try
            {
                string advancedTempDir = Path.Combine(outputDir, $"_advanced_metrics_{Guid.NewGuid():N}");
                try
                {
                    _fs.CreateDirectory(advancedTempDir);
                    string? cleanRef = await SanitizePngIfNeededAsync(refPath, advancedTempDir);
                    bool ownClean = cleanRef != refPath;

                    string? refPng = cleanRef;
                    if (Path.GetExtension(cleanRef).ToLower() != ".png")
                    {
                        try { refPng = await ConvertToPngAsync(cleanRef, advancedTempDir); }
                        catch { refPng = null; }
                    }

                    string? distPng = null;
                    if (needSsimu2 || needButter)
                    {
                        try { distPng = await ConvertToPngAsync(distPath, advancedTempDir); }
                        catch { distPng = null; }
                    }

                    if (needSsimu2 && refPng != null && distPng != null)
                    {
                        try
                        {
                            var s = await ComputeSSIMULACRA2Async(refPng, distPng);
                            if (s.HasValue) UpdateCachedMetrics(cacheKey, m => m.SSIMULACRA2 = s);
                        }
                        catch (Exception ex) { _logger.LogInfo($"SSIMULACRA2 ��̨�쳣: {ex.Message}"); }
                    }

                    if (needButter && refPng != null && distPng != null)
                    {
                        try
                        {
                            var (raw, p3) = await ComputeButteraugliAsync(refPng, distPng, advancedTempDir);
                            if (raw.HasValue) UpdateCachedMetrics(cacheKey, m => m.Butteraugli_Raw = raw);
                            if (p3.HasValue) UpdateCachedMetrics(cacheKey, m => m.Butteraugli_3norm = p3);
                        }
                        catch (Exception ex) { _logger.LogInfo($"Butteraugli ��̨�쳣: {ex.Message}"); }
                    }

                    if (needGmsd)
                    {
                        try
                        {
                            var g = await ComputeGMSDAsync(cleanRef, distPath);
                            if (g.HasValue) UpdateCachedMetrics(cacheKey, m => m.GMSD = g);
                        }
                        catch (Exception ex) { _logger.LogInfo($"GMSD ��̨�쳣: {ex.Message}"); }
                    }

                    if (ownClean && _fs.FileExists(cleanRef))
                        try { _fs.DeleteFile(cleanRef); } catch { }
                }
                finally
                {
                    if (_fs.DirectoryExists(advancedTempDir))
                        try { _fs.DeleteDirectory(advancedTempDir, true); } catch { }
                }
            }
            finally
            {
                _advancedMetricSemaphore.Release();
            }
        }

        /// <summary> �̰߳�ȫ�ظ��»����е� QualityMetrics ���� </summary>
        /// <summary> �̰߳�ȫ�ظ��»����е� QualityMetrics ����ʹ��ԭ�� AddOrUpdate�� </summary>
        private void UpdateCachedMetrics(string cacheKey, Action<QualityMetrics> updateAction)
        {
            _cache.UpdateMetrics(cacheKey, updateAction);
        }



        // ===== SSIMULACRA2 =====
        private async Task<double?> ComputeSSIMULACRA2Async(string refPath, string distPath)
        {
            string exe = EncoderUtils.FindExecutable("ssimulacra2") ?? "ssimulacra2";
            string cleanRef = NormalizePathForExternalTool(refPath);
            string cleanDist = NormalizePathForExternalTool(distPath);
            string args = $"\"{cleanRef}\" \"{cleanDist}\"";
            _logger.LogInfo($"?? SSIMULACRA2 ����: {exe} {args}");   // �� ����
            var (exitCode, stdout, stderr) = await _processRunner.RunAsync(
                exe, args, TimeSpan.FromMinutes(2), _globalCts?.Token ?? default);
            _logger.LogInfo($"?? SSIMULACRA2 ����: exit={exitCode}, stdout={stdout.Trim()}, stderr={stderr.Trim()}"); // �� ����
            if (exitCode != 0) return null;
            string output = (stdout + stderr).Trim();
            if (double.TryParse(output, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                return val;
            return null;
        }

        // ===== Butteraugli =====
        private async Task<(double? raw, double? p3)> ComputeButteraugliAsync(string refPath, string distPath, string tempDir)
        {
            string exe = EncoderUtils.FindExecutable("butteraugli_main") ?? "butteraugli_main";
            string diffPng = Path.Combine(tempDir, $"_butteraugli_diff_{Guid.NewGuid():N}.png");
            string cleanRef = NormalizePathForExternalTool(refPath);
            string cleanDist = NormalizePathForExternalTool(distPath);
            string cleanDiff = NormalizePathForExternalTool(diffPng);
            string args = $"\"{cleanRef}\" \"{cleanDist}\" --distmap \"{cleanDiff}\"";
            _logger.LogInfo($"?? Butteraugli ����: {exe} {args}");   // �� ����
            var (exitCode, stdout, stderr) = await _processRunner.RunAsync(
                exe, args, TimeSpan.FromMinutes(2), _globalCts?.Token ?? default);
            _logger.LogInfo($"?? Butteraugli ����: exit={exitCode}, stdout={stdout.Trim()}, stderr={stderr.Trim()}"); // �� ����

            if (_fs.FileExists(diffPng)) try { _fs.DeleteFile(diffPng); } catch { }

            if (exitCode != 0) return (null, null);
            string output = stdout + stderr;

            var rawMatch = Regex.Match(output, @"^\s*(\d+\.?\d*)", RegexOptions.Multiline);
            double? raw = null;
            if (rawMatch.Success && double.TryParse(rawMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double r))
                raw = r;

            var p3Match = Regex.Match(output, @"3-norm:\s*(\d+\.?\d*)");
            double? p3 = null;
            if (p3Match.Success && double.TryParse(p3Match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double p))
                p3 = p;

            return (raw, p3);
        }

        // ===== GMSD���Զ���ʵ�֣��� C++ �汾��ʹ�� ffmpeg ����Ҷ�ͼ���㣩 =====
        private async Task<double?> ComputeGMSDAsync(string refPath, string distPath)
        {
            // 1. ��������ͼ�� 8 λ�Ҷ�ԭʼ����
            var refGray = await DecodeGrayRawAsync(refPath);
            if (refGray == null) return null;
            var distGray = await DecodeGrayRawAsync(distPath);
            if (distGray == null) return null;

            // 2. �ߴ����һ��
            if (refGray.Value.w != distGray.Value.w || refGray.Value.h != distGray.Value.h)
                return null;

            // 3. ���� GMSD
            double score = ComputeGMSD_C(refGray.Value.data, refGray.Value.w, refGray.Value.h,
                                          distGray.Value.data);
            return score >= 0 ? score : null;
        }

        /// <summary> �� ffmpeg ������ͼƬ����Ϊ 8 λ�Ҷ�ԭʼ�ֽ����飬�����ؿ���ߡ�ʧ�ܷ��� null�� </summary>
        private async Task<(int w, int h, byte[] data)?> DecodeGrayRawAsync(string imagePath)
        {
            string cleanPath = NormalizePathForExternalTool(imagePath);
            string args = $"-loglevel error -hide_banner -i \"{cleanPath}\" -vf format=gray -f rawvideo -pix_fmt gray pipe:1";
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo(_ffmpegPath, args)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                process.Start();

                using var ms = new MemoryStream();
                var copyTask = process.StandardOutput.BaseStream.CopyToAsync(ms);
                var stderrTask = process.StandardError.ReadToEndAsync();

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    _globalCts?.Token ?? CancellationToken.None, timeoutCts.Token);
                await Task.WhenAll(copyTask, stderrTask, process.WaitForExitAsync(linkedCts.Token));

                if (process.ExitCode != 0) return null;

                byte[] rawData = ms.ToArray();

                // ��ȡͼ��ֱ���
                var (w, h) = await GetResolutionAsync(imagePath);
                if (w <= 0 || h <= 0) return null;
                int expectedSize = w * h;
                if (rawData.Length != expectedSize) return null;

                return (w, h, rawData);
            }
            catch (Exception ex)
            {
                _logger.LogInfo($"DecodeGrayRawAsync ʧ��: {ex.Message}");
                return null;
            }
        }

        /// <summary> ���� GMSD���ݶȷ�ֵ���ƶ�ƫ���C = 0.0026�����Ϊ��׼�ʧ�ܷ��� -1�� </summary>
        private static double ComputeGMSD_C(byte[] refData, int w, int h, byte[] distData)
        {
            if (refData.Length != distData.Length || w < 3 || h < 3)
                return -1;

            const double C = 0.0026;
            double sum = 0.0;
            double sumSq = 0.0;
            int count = 0;

            int GetPix(byte[] data, int x, int y) => data[y * w + x];

            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    double grx = GetPix(refData, x + 1, y) - GetPix(refData, x - 1, y);
                    double gry = GetPix(refData, x, y + 1) - GetPix(refData, x, y - 1);
                    double gdx = GetPix(distData, x + 1, y) - GetPix(distData, x - 1, y);
                    double gdy = GetPix(distData, x, y + 1) - GetPix(distData, x, y - 1);

                    double gmR = Math.Sqrt(grx * grx + gry * gry);
                    double gmD = Math.Sqrt(gdx * gdx + gdy * gdy);

                    double gms = (2.0 * gmR * gmD + C) / (gmR * gmR + gmD * gmD + C);
                    sum += gms;
                    sumSq += gms * gms;
                    count++;
                }
            }

            if (count == 0) return -1;
            double mean = sum / count;
            double variance = (sumSq / count) - (mean * mean);
            return Math.Sqrt(Math.Max(0, variance));   // ��׼��
        }







        /// <summary>
        /// ����ͼ���Ⱥ���С tile ������ƣ��������Ϸ��� tile-columns ֵ��log2 ��������
        /// ���磺��� �� 255 �� 0��256~511 �� 0��512~1023 �� 1��1024~2047 �� 2���Դ����ơ�
        /// </summary>
        private static int GetMaxLegalTileCols(int imageWidth, int minTileWidth = 256)
        {
            if (imageWidth < minTileWidth)
                return 0;
            int maxTiles = imageWidth / minTileWidth;
            if (maxTiles < 1)
                return 0;
            return (int)Math.Floor(Math.Log2(maxTiles));
        }











        private static string TilePart(int tileCols, bool isTrueLossless)
    => isTrueLossless
        ? "-tile-columns 0 -tile-rows 0"
        : $"-tile-columns {tileCols} -tile-rows 0";

        /// <summary>
        /// ���������ļ�·�������������������·������������Ŀ¼�ṹ��
        /// </summary>
        /// <summary>
        /// ���������ļ�·�������������������·������������Ŀ¼�ṹ��
        /// �� ����ͬ����⣺���ļ����Ѵ��ڣ��Զ�׷�� _1��_2 �� �Ա��⸲�ǡ�
        /// </summary>
        private string GetOutputPath(string inputFilePath, int index)
        {
            // �� ͬ��ȥ�����ܵĳ�·��ǰ׺����֤ Path.GetRelativePath ��ȷ����
            string safeInputDir = NormalizePathForExternalTool(_inputDir);
            string safeInputPath = NormalizePathForExternalTool(inputFilePath);
            string relPath = Path.GetRelativePath(safeInputDir, safeInputPath);
            string? relDir = Path.GetDirectoryName(relPath);
            string fileName = GetOutputFileName(inputFilePath, index);
            string targetDir = string.IsNullOrEmpty(relDir)
                ? _outputDir
                : Path.Combine(_outputDir, relDir);
            _fs.CreateDirectory(targetDir);
            string candidate = Path.Combine(targetDir, fileName);
            switch (_config.FileConflictStrategy)
            {
                case PresetConfig.ConflictStrategy.Overwrite:
                case PresetConfig.ConflictStrategy.Skip:
                    _allocatedOutputs.TryAdd(
                        NormalizePathForExternalTool(candidate).ToLowerInvariant(), 0);
                    return candidate;
                default: // Rename
                    // �Զ�׷������Ա���ͬ����ͻ���ڴ�+����˫�ؼ�⣩
                    string allocatedKey = NormalizePathForExternalTool(candidate).ToLowerInvariant();
                    if (_allocatedOutputs.ContainsKey(allocatedKey) || _fs.FileExists(candidate))
                    {
                        string nameNoExt = Path.GetFileNameWithoutExtension(fileName);
                        string ext = Path.GetExtension(fileName);
                        int counter = 1;
                        do
                        {
                            fileName = $"{nameNoExt}_{counter}{ext}";
                            candidate = Path.Combine(targetDir, fileName);
                            counter++;
                        } while (_fs.FileExists(candidate) ||
                                 _allocatedOutputs.ContainsKey(
                                     NormalizePathForExternalTool(Path.Combine(targetDir, fileName)).ToLowerInvariant()));
                    }
                    // ����ѷ��䣬��ֹͬ����ͬ������
                    _allocatedOutputs.TryAdd(
                        NormalizePathForExternalTool(candidate).ToLowerInvariant(), 0);
                    return candidate;
            }
        }

        /// <summary> �ⲿ���ߣ�ffmpeg �ȣ������� \\?\ ��·������Ҫ���롣��ȷ���� UNC ·�� </summary>
        private static string NormalizePathForExternalTool(string path)
        {
            if (OperatingSystem.IsWindows() && path.StartsWith(@"\\?\"))
            {
                // \\?\UNC\server\share\path �� \\server\share\path
                if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                    return @"\" + path.Substring(7);
                return path.Substring(4);
            }
            return path;
        }

        /// <summary>
        /// ����ͼ���ȼ������� AV1 tile ��� �� 4096 ���Ƶ���С tile-columns ֵ��log2 ��������
        /// ���磺��� �� 4096 �� 0��4097~8192 �� 1��8193~16384 �� 2���Դ����ơ�
        /// </summary>
        private static int GetMinLegalTileCols(int imageWidth)
        {
            if (imageWidth <= 4096)
                return 0;

            int colsLog2 = 0;
            // ÿ����һ�У�tile ��ȼ��룬ֱ������ �� 4096
            while (Math.Ceiling((double)imageWidth / (1 << colsLog2)) > 4096)
                colsLog2++;
            return colsLog2;
        }

























        public AvifPipeline(string inputDir, string outputDir, PresetConfig config,
                    ILogger logger,
                    IProcessRunner? processRunner = null,
                    PresetConfig.IFileSystem? fileSystem = null,   // ��Ϊ�����޶���
                    ICacheManager? cacheManager = null,
                    IProgress<int>? progress = null)
        {
            _fs = fileSystem ?? new PresetConfig.RealFileSystem();

            // �� ���ó�·��֧�֣�Windows ���Զ���� \\?\ ǰ׺��
            _inputDir = EnsureLongPath(inputDir);
            _outputDir = EnsureLongPath(outputDir);

            // ��������Ŀ¼����������ֹ�������ͬʱдͬһĿ¼
            string lockFile = Path.Combine(_outputDir, ".avifencoder.lock");
            try
            {
                _lockStream = new FileStream(lockFile, FileMode.Create,
                    FileAccess.Write, FileShare.None, 4096,
                    FileOptions.DeleteOnClose);
            }
            catch (IOException)
            {
                throw new IOException(
                    $"���Ŀ¼ {outputDir} �ѱ���һ���������ռ�á�" +
                    "��ȴ�����ɻ�������Ŀ¼��");
            }

            // ������������ͬĿ¼ʱ������� .avif Դ�ļ����Զ����������Ŀ¼
            string normalizedInput = NormalizePathForExternalTool(_inputDir);
            string normalizedOutput = NormalizePathForExternalTool(_outputDir);
            if (string.Equals(normalizedInput, normalizedOutput,
                StringComparison.OrdinalIgnoreCase))
            {
                bool hasAvifInput = false;
                try
                {
                    hasAvifInput = _fs.EnumerateFiles(normalizedInput, "*.avif",
                        SearchOption.TopDirectoryOnly).Any();
                }
                catch { }

                if (hasAvifInput)
                {
                    string subDir = Path.Combine(_outputDir, "Avifoutput");
                    _logger?.LogInfo(
                        $"[INFO] �������ͬĿ¼�Ҵ��� .avif Դ�ļ���" +
                        $"����Զ��ض���: {subDir}");
                    SafeWriteLine(
                        $"[INFO] ��������Ŀ¼��ͬ��Ϊ���⸲��Դ .avif �ļ���" +
                        $"���Ŀ¼�Զ����Ϊ: {subDir}");
                    _outputDir = EnsureLongPath(subDir);
                }
            }

            _config = config;
            _ffmpegPath = EncoderUtils.FindExecutable("ffmpeg") ?? throw new Exception("ffmpeg δ�ҵ�");
            _ffprobePath = EncoderUtils.FindExecutable("ffprobe") ?? throw new Exception("ffprobe δ�ҵ�");
            _processRunner = processRunner ?? new RealProcessRunner();
            _logger = logger;
            _cache = cacheManager ?? new CacheManager();

            bool isHardwareEncoder = !Av1EncoderFactory.Get(config.Encoder).SupportsLossless;

            // �����Ӳ����������֧������ģʽ
            if (config.Lossless && !Av1EncoderFactory.Get(config.Encoder).SupportsLossless)
            {
                throw new ArgumentException(
                    $"������ {config.Encoder} ��֧������ģʽ��" +
                    "����� libaom-av1 / libsvtav1 / librav1e �������������");
            }

            // ���棺�� libaom ��������֧�� AOM �߼�����
            if (!Av1EncoderFactory.Get(config.Encoder).SupportsAomParams)
            {
                _logger.LogInfo(
                    $"[INFO] ������ {config.Encoder} ��֧�� -aom-params��" +
                    "aq-mode/deltaq-mode �Ȳ�����������");
            }

            // ��������ģ�岻�� {index} �� {name} �� ���ļ����ܻ��า��
            if (!config.OutputNameFormat.Contains("{index}") &&
                !config.OutputNameFormat.Contains("{name}"))
            {
                SafeWriteLine(
                    "[WARN] ���ģ�岻�� {index} �� {name}��" +
                    "�������ͼƬʱ���ܻ��า�ǡ�");
            }

            // �����CPU-used �������������� �� �Զ�ǯ��
            var cpuEnc = Av1EncoderFactory.Get(config.Encoder);
            if (config.FinalCpuUsed > cpuEnc.MaxSpeed)
            {
                SafeWriteLine(
                    $"[WARN] FinalCpuUsed={config.FinalCpuUsed} " +
                    $"���� {config.Encoder} ���� ({cpuEnc.MaxSpeed})��" +
                    $"��ǯ��Ϊ {cpuEnc.MaxSpeed}");
                config.FinalCpuUsed = cpuEnc.MaxSpeed;
                config.SearchCpuUsed = Math.Min(config.SearchCpuUsed, cpuEnc.MaxSpeed);
            }
            if (config.SearchCpuUsed > cpuEnc.MaxSpeed)
            {
                SafeWriteLine(
                    $"[WARN] SearchCpuUsed={config.SearchCpuUsed} " +
                    $"���� {config.Encoder} ���� ({cpuEnc.MaxSpeed})��" +
                    $"��ǯ��Ϊ {cpuEnc.MaxSpeed}");
                config.SearchCpuUsed = cpuEnc.MaxSpeed;
            }

            int cpuCount = Environment.ProcessorCount;

            // ���û�δͨ�� -j ָ�������������Զ�����
            // ���û�δͨ�� -j ָ�������������Զ�����
            if (!config.UserSpecifiedMaxJobs)
            {
                config.MaxJobs = isHardwareEncoder
                    ? Math.Max(2, cpuCount * 2)               // Ӳ�����������ʵ���߲���
                    : Math.Max(2, (int)Math.Sqrt(cpuCount));  // �����������������ƽ����
            }
            if (config.MaxJobs < 1) config.MaxJobs = 1;

            int ssimSlots = Math.Max(2, cpuCount);   // ���������Կ�ʹ��ȫ������

            _maxFfmpegConcurrency = config.MaxJobs;
            _ssimConcurrency = new SemaphoreSlim(ssimSlots);
            _ffmpegSlots = new SemaphoreSlim(config.MaxJobs);   // �����޸���ֱ��ʹ�� config.MaxJobs

            _guiProgress = progress;       // �� ��Ϊ _guiProgress

            _advancedMetricSemaphore = new SemaphoreSlim(Math.Max(1, Environment.ProcessorCount / 2));

            // ��ʼ��������֤ʧ�ܸ���Ŀ¼
            _failedVerificationDir = Path.Combine(_outputDir, "_failed_verification");
            if (!_fs.DirectoryExists(_failedVerificationDir))
            {
                _fs.CreateDirectory(_failedVerificationDir);
            }
            _failedCsvPath = Path.Combine(_failedVerificationDir, "failed_verification.csv");

            _csvPath = Path.Combine(_outputDir, "avif_stats.csv");

            // Journal �ϵ�����
            string sessionDir = Path.Combine(_outputDir, ".session");
            _fs.CreateDirectory(sessionDir);
            _journalPath = Path.Combine(sessionDir, "journal.ndjson");
            _snapshotPath = Path.Combine(sessionDir, "snapshot.json");

            // �� ��ƽ̨���ף������˳�ʱ��Ctrl+C�����ڹرա�Environment.Exit��ǿ�������ӽ���
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                foreach (var p in _spawnedProcesses)
                {
                    try
                    {
                        if (!p.HasExited)
                        {
                            p.Kill(entireProcessTree: true);
                        }
                    }
                    catch { }
                }
            };

        }

        /// <summary> �жϱ������Ƿ�֧�� -still-picture 1 ������AVIF ��֡��ֹͼ���־�� </summary>
        /// <summary>
        /// �ȱ�����ͼƬ��ʹ���߲����� maxDim�����Ϊ PNG ��ʱ�ļ���
        /// ���� Alpha ͨ�������Դ�ļ���͸����Ϣ����
        /// </summary>
        private async Task ScaleImageAsync(string input, string output, int maxDim)
        {
            var (w, h) = await GetResolutionAsync(input);
            if (w <= 0 || h <= 0)
                throw new Exception($"�޷���ȡ�ֱ���: {input}");

            int longSide = Math.Max(w, h);
            if (longSide <= maxDim)
            {
                _fs.CopyFile(input, output, true);   // �滻 File.Copy
                return;
            }

            double scale = (double)maxDim / longSide;
            int targetW = (int)Math.Round(w * scale) & ~1;
            int targetH = (int)Math.Round(h * scale) & ~1;
            if (targetW < 2) targetW = 2;
            if (targetH < 2) targetH = 2;

            bool hasAlpha = await SourceHasAlpha(input);
            string pixFmt = hasAlpha ? "rgba" : "rgb24";

            string filter = $"scale={targetW}:{targetH}:flags=lanczos";
            string args = $"-loglevel error -hide_banner -i \"{input}\" -vf \"{filter}\" -pix_fmt {pixFmt} \"{output}\"";

            (bool ok, string err) = await RunFfmpegExAsync(_ffmpegPath, args, TimeSpan.FromMinutes(2));
            if (!ok)
                throw new Exception($"����ʧ��: {err}");
        }
        private static double ComputeMixScore(QualityMetrics m)
        {
            return MetricRegistry.ComputeMixScore(m);
        }

        private async Task<string> RunProbeAsync(string file, string args)
        {
            // ��ʱ I/O �������ż����������� 2 ��
            for (int retry = 0; retry <= 2; retry++)
            {
                try
                {
                    var (exitCode, stdout, stderr) = await _processRunner.RunAsync(
                        file, args, TimeSpan.FromSeconds(30), _globalCts?.Token ?? default);
                    if (exitCode == 0 || retry >= 2)
                        return stdout;
                }
                catch when (retry < 2) { }
                await Task.Delay(1000 * (retry + 1));
            }
            return "";
        }



        #region Journal �ϵ�����

        private void InitJournal()
        {
            lock (_journalLock)
            {
                _journalWriter?.Dispose();
                _journalWriter = new StreamWriter(_journalPath, append: true, Encoding.UTF8)
                {
                    AutoFlush = false
                };
                _lastSnapshotTime = DateTime.UtcNow;
                _journalCountSinceSnapshot = 0;
            }
        }

        private void AppendJournal(string file, string evt, object? extra = null)
        {
            lock (_journalLock)
            {
                if (_journalWriter == null) return;
                var obj = new Dictionary<string, object>
                {
                    ["v"] = 1,
                    ["ts"] = DateTime.UtcNow.ToString("o"),
                    ["file"] = file,
                    ["evt"] = evt
                };
                if (extra != null)
                {
                    foreach (var prop in extra.GetType().GetProperties())
                        obj[prop.Name.ToLower()] = prop.GetValue(extra) ?? "";
                }
                string line = System.Text.Json.JsonSerializer.Serialize(obj);
                _journalWriter.WriteLine(line);
                _journalWriter.Flush();  // ����ˢ��
                _journalCountSinceSnapshot++;

                // �� �����Կ��գ��ϲ��ɿ�������б� + ��������
                if (_config.Resume && _journalCountSinceSnapshot >= 50)
                {
                    var (oldDone, _, _) = LoadSnapshot();
                    var newDone = ReplayJournal(null);
                    SaveSnapshot(oldDone.Union(newDone));
                }
            }
        }

        private HashSet<string> ReplayJournal(DateTime? since)
        {
            var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!_fs.FileExists(_journalPath)) return completed;

            try
            {
                var lines = File.ReadAllLines(_journalPath);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("ts", out var tsEl) &&
                            root.TryGetProperty("evt", out var evtEl) &&
                            root.TryGetProperty("file", out var fileEl))
                        {
                            if (since.HasValue &&
                                DateTime.TryParse(tsEl.GetString(), out var ts) &&
                                ts < since.Value)
                                continue;
                            if (evtEl.GetString() == "success")
                                completed.Add(fileEl.GetString() ?? "");
                        }
                    }
                    catch (JsonException)
                    {
                        // ���У��ضϲ��˳�
                        break;
                    }
                }
            }
            catch { }
            return completed;
        }

        private void SaveSnapshot(IEnumerable<string> completed)
        {
            if (string.IsNullOrEmpty(_snapshotPath)) return;
            try
            {
                var snapshot = new
                {
                    v = 3,
                    ts = DateTime.UtcNow.ToString("o"),
                    completed = completed.ToArray(),
                    inputDir = _inputDir,
                    config = new
                    {
                        _config.Encoder,
                        _config.Lossless,
                        _config.UseCRFSearch,
                        _config.BaseCRF,
                        _config.MinCRF,
                        _config.MaxCRF,
                        _config.MetricMode,
                        _config.PixelFormat,
                        _config.BitDepth,
                        _config.OutputNameFormat,
                        _config.RecurseSubdirectories,
                        _config.SerialEncode,
                        _config.UsePriorSearch,
                        _config.UseProxySearch,
                        _config.SearchCpuUsed,
                        _config.FinalCpuUsed,
                        _config.MaxResolution,
                        _config.MaxJobs,
                        FileConflictStrategy = _config.FileConflictStrategy.ToString(),
                        _config.InputExtensions,
                        _config.EncodeTimeoutMinutes,
                        _config.SearchTimeoutMinutes,
                        _config.SafeTimeoutMinutes,
                        _config.SsimTimeoutMinutes,
                        _config.SweepMode,
                        _config.DryRun,
                        _config.Verbose
                    }
                };
                string tmp = _snapshotPath + ".tmp";
                File.WriteAllText(tmp, System.Text.Json.JsonSerializer.Serialize(snapshot), Encoding.UTF8);
                if (_fs.FileExists(_snapshotPath))
                    _fs.DeleteFile(_snapshotPath);
                File.Move(tmp, _snapshotPath);
                _journalCountSinceSnapshot = 0;
                _lastSnapshotTime = DateTime.UtcNow;

                // �ض� Journal��ԭ���滻������ AppendJournal �ڴ����ڶ�ʧ��Ŀ
                lock (_journalLock)
                {
                    _journalWriter?.Flush();
                    _journalWriter?.Dispose();
                    try { if (_fs.FileExists(_journalPath)) _fs.DeleteFile(_journalPath); } catch { }
                    _journalWriter = new StreamWriter(_journalPath, append: false, Encoding.UTF8) { AutoFlush = false };
                }
            }
            catch { }
        }

        private (HashSet<string> completed, string? configJson, string? inputDir) LoadSnapshot()
        {
            if (!_fs.FileExists(_snapshotPath)) return (new HashSet<string>(), null, null);
            try
            {
                string json = File.ReadAllText(_snapshotPath, Encoding.UTF8);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string? cfgJson = null, inputDir = null;
                if (root.TryGetProperty("config", out var cfgEl))
                    cfgJson = cfgEl.GetRawText();
                if (root.TryGetProperty("inputDir", out var idEl))
                    inputDir = idEl.GetString();
                if (root.TryGetProperty("completed", out var arr))
                {
                    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var el in arr.EnumerateArray())
                        set.Add(el.GetString() ?? "");
                    return (set, cfgJson, inputDir);
                }
            }
            catch { }
            return (new HashSet<string>(), null, null);
        }

        private void CloseJournal()
        {
            lock (_journalLock)
            {
                _journalWriter?.Flush();
                _journalWriter?.Dispose();
                _journalWriter = null;
            }
        }

        /// <summary>�����ŷָ� CSV �У���ȷ����˫���Ű������ֶΣ������ڶ��Ų��ָ</summary>
        private static string[] SplitCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            int start = 0;
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (line[i] == ',' && !inQuotes)
                {
                    result.Add(Unquote(line[start..i]));
                    start = i + 1;
                }
            }
            result.Add(Unquote(line[start..]));
            return result.ToArray();
        }

        private static string Unquote(string s)
        {
            s = s.Trim();
            if (s.StartsWith('"') && s.EndsWith('"') && s.Length >= 2)
                return s[1..^1].Replace("\"\"", "\"");
            return s;
        }

        #endregion

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            CloseJournal();
            FinalCleanup();
            _globalCts?.Cancel();
            _globalCts?.Dispose();
            _globalCts = null;
            _ssimConcurrency?.Dispose();
            _ffmpegSlots?.Dispose();
            if (_cancelKeyHandler != null)
                Console.CancelKeyPress -= _cancelKeyHandler;
            _advancedMetricSemaphore?.Dispose();
            _lockStream?.Dispose();
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Probe ̽��

        private readonly ConcurrentDictionary<string, ProbeInfo> _probeCache = new();

        private async Task<ProbeInfo?> GetProbeInfoAsync(string filePath)
        {
            string key = GetNormalizedPathForCache(filePath);
            if (_probeCache.TryGetValue(key, out var cached)) return cached;

            // һ���� ffprobe ��ȡ������Ϣ
            string args = $"-v error -select_streams v:0 -show_entries stream=pix_fmt,width,height,is_lossless,color_primaries,color_transfer,color_space,color_range -of json \"{filePath}\"";
            string json = await RunProbeAsync(_ffprobePath, args);
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var streams = doc.RootElement.GetProperty("streams");
                if (streams.GetArrayLength() == 0) return null;

                var stream = streams[0];
                string fmt = stream.GetProperty("pix_fmt").GetString()?.ToLower() ?? "yuv420p";
                int w = stream.GetProperty("width").GetInt32();
                int h = stream.GetProperty("height").GetInt32();

                bool hasAlpha = fmt switch
                {
                    "rgba" or "bgra" or "argb" or "abgr" => true,
                    "rgba64le" or "bgra64le" => true,
                    _ => false
                };

                // ������ȡɫ���ֶΣ����� unknown/reserved
                static string? TryGetStringProperty(JsonElement element, string propertyName)
                {
                    if (element.TryGetProperty(propertyName, out var prop))
                    {
                        string val = prop.GetString()?.Trim().ToLowerInvariant() ?? "";
                        return !string.IsNullOrWhiteSpace(val) && val != "unknown" && val != "reserved" ? val : null;
                    }
                    return null;
                }

                string? colorPrimaries = TryGetStringProperty(stream, "color_primaries");
                string? colorTransfer = TryGetStringProperty(stream, "color_transfer");
                string? colorSpace = TryGetStringProperty(stream, "color_space");
                string? colorRange = TryGetStringProperty(stream, "color_range");

                var info = new ProbeInfo
                {
                    PixFmt = fmt,
                    HasAlpha = hasAlpha,
                    Width = w,
                    Height = h,
                    ColorPrimaries = colorPrimaries,
                    ColorTransfer = colorTransfer,
                    ColorSpace = colorSpace,
                    ColorRange = colorRange
                };
                _probeCache[key] = info;
                return info;
            }
            catch { return null; }
        }

        static string? TryGetStringProperty(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var prop))
            {
                string val = prop.GetString()?.Trim().ToLowerInvariant() ?? "";
                return !string.IsNullOrWhiteSpace(val) && val != "unknown" && val != "reserved" ? val : null;
            }
            return null;
        }






        /// <summary> ���ر������ض��Ĳ���Ƭ�Σ��Ѱ����������ٶȿ��ƺͷֿ鲿�� </summary>

        private static string GetNormalizedPathForCache(string input)
        {
            try
            {
                string full = Path.GetFullPath(input).Trim();
                // ���ó�·��֧�֣�ȷ�������һ��
                full = EnsureLongPath(full);
                return OperatingSystem.IsWindows() ? full.ToLowerInvariant() : full;
            }
            catch
            {
                return $"__fallback__{Path.GetFileName(input).ToLowerInvariant()}";
            }
        }


        /// <summary>
        /// ʹ�� libvmaf һ���Լ��� ref (ԭͼ) �� dist (�����) �� SSIM / PSNR?Y / MS?SSIM / VMAF��
        /// ���� QualityMetrics��ʧ�ܷ��� null�����Զ�����ֱ��ʲ�һ�µ��������������ͬ�ߴ磩��
        /// </summary>
        private async Task<QualityMetrics?> ComputeAllMetricsAsync(string refPath, string distPath)
        {
            if (!EnsureFilesValid(refPath, distPath)) return null;

            string workDir = Environment.CurrentDirectory;
            string metricsDir = Path.Combine(workDir, $"avif_metrics_tmp_{_instanceId}");
            Directory.CreateDirectory(metricsDir);

            string jsonName = $"_metrics_{Guid.NewGuid():N}.json";
            string jsonPath = Path.Combine(metricsDir, jsonName);
            string logPathSafe = jsonName;

            try
            {
                var (w1, h1) = await GetResolutionAsync(refPath).WaitAsync(TimeSpan.FromSeconds(30));
                var (w2, h2) = await GetResolutionAsync(distPath).WaitAsync(TimeSpan.FromSeconds(30));

                string filter;
                if (w1 > 0 && h1 > 0 && w2 > 0 && h2 > 0 && (w1 != w2 || h1 != h2))
                {
                    int w = Math.Min(w1, w2);
                    int h = Math.Min(h1, h2);
                    filter = $"[0:v]scale={w}:{h}[ref];[1:v]scale={w}:{h}[dist];[ref][dist]libvmaf=" +
                             $"feature=name=psnr|name=float_ssim|name=float_ms_ssim:" +
                             $"model='version=vmaf_float_v0.6.1':log_path={logPathSafe}:log_fmt=json:n_threads=4";
                }
                else
                {
                    filter = $"[0:v][1:v]libvmaf=feature=name=psnr|name=float_ssim|name=float_ms_ssim:" +
                             $"model='version=vmaf_float_v0.6.1':" +
                             $"log_path={logPathSafe}:log_fmt=json:n_threads=4";
                }

                string args = $"-loglevel error -hide_banner -i \"{refPath}\" -i \"{distPath}\" " +
                              $"-filter_complex \"{filter}\" -frames:v 1 -f null -";

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo(_ffmpegPath, args)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = metricsDir
                    }
                };

                process.Start();

                // �� �ڴ涵��׷�٣�Job Object ʧ��ʱ���ã�
                _spawnedProcesses.Add(process);

                // �� ���� Windows ƽ̨���ӽ��̼���ȫ�� Job Object
                if (OperatingSystem.IsWindows())
                {
                    JobObjectHelper.AssignProcess(process);
                }

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();

                var timeout = TimeSpan.FromMinutes(_config.SsimTimeoutMinutes);
                using var timeoutCts = new CancellationTokenSource(timeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    _globalCts?.Token ?? CancellationToken.None, timeoutCts.Token);

                try
                {
                    await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(linkedCts.Token));
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited)
                    {
                        try { process.Kill(entireProcessTree: true); } catch { }
                        try { await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
                    }
                }

                string stdout = await stdoutTask;
                string stderr = await stderrTask;
                int exitCode = process.ExitCode;

                if (!string.IsNullOrWhiteSpace(stderr))
                    _logger.LogInfo($"ComputeAllMetrics stderr [{Path.GetFileName(refPath)}]: {stderr.Trim()}");

                if (exitCode != 0)
                {
                    _logger.LogInfo($"ComputeAllMetrics ʧ�� (exit {exitCode}) [{Path.GetFileName(refPath)}]: {stderr.Trim()}");
                    return null;
                }

                if (!File.Exists(jsonPath))
                {
                    _logger.LogInfo($"ComputeAllMetrics: JSON �ļ�δ����: {jsonPath}");
                    return null;
                }

                string json = await File.ReadAllTextAsync(jsonPath);
                QualityMetrics? metrics = ParseVmafJson(json);
                if (metrics == null) return null;

                // �ϲ� stdout �� stderr��ͳһ��ȡ VMAF�����������λ�ò�ͬ��©��
                string combinedOutput = stdout + "\n" + stderr;
                double? vmafFromConsole = TryExtractVmaf(combinedOutput);

                if (vmafFromConsole.HasValue)
                {
                    // ����̨��ȡ�ɹ������� JSON ֵ�����ְ汾 JSON �� VMAF ȱʧ��Ϊ��ֵ��
                    metrics.VMAF = vmafFromConsole.Value;
                }
                else
                {
                    // ����̨Ҳδ��ȡ�� �� ��� JSON �Ƿ��Ѹ�����Ч VMAF
                    if (double.IsNaN(metrics.VMAF))
                    {
                        _logger.LogInfo($"δ��ȡ�� VMAF ���� [{Path.GetFileName(refPath)}]");
                    }
                }

                // PSNR-Y �ӽ� libvmaf ���� 60dB ʱ���ö��� PSNR �˾�����������ֵ
                if (metrics.PSNR_Y >= 59.5)
                {
                    var uncappedPsnr = await ComputePsnrUncappedAsync(
                        refPath, distPath);
                    if (uncappedPsnr.HasValue)
                    {
                        metrics.PSNR_Y = uncappedPsnr.Value;
                    }
                }

                return metrics;
            }
            catch (Exception ex)
            {
                _logger.LogInfo($"ComputeAllMetrics �쳣: {ex.Message}");
                return null;
            }
            finally
            {
                try
                {
                    if (File.Exists(jsonPath)) File.Delete(jsonPath);
                }
                catch { }
            }
        }

        /// <summary>
        /// ʹ�ö��� ffmpeg PSNR �˾����� Y ͨ�� PSNR���ƹ� libvmaf �� 60dB ���ޡ�
        /// ���� PSNR-Y ֵ����Ϊ inf �� double.PositiveInfinity����ʧ�ܷ��� null��
        /// </summary>
        private async Task<double?> ComputePsnrUncappedAsync(
            string refPath, string distPath)
        {
            try
            {
                string args =
                    $"-loglevel error -hide_banner " +
                    $"-i \"{refPath}\" -i \"{distPath}\" " +
                    $"-lavfi \"psnr=stats_file=-\" -f null -";

                var (exitCode, stdout, stderr) = await _processRunner.RunAsync(
                    _ffmpegPath, args, TimeSpan.FromMinutes(2),
                    _globalCts?.Token ?? default);

                if (exitCode != 0) return null;

                string output = stdout + stderr;
                // stats_file=- �����ʽ: "psnr_y:inf" �� "psnr_y:48.1234"
                var match = Regex.Match(output,
                    @"psnr_y:\s*(inf|[0-9.]+)",
                    RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string val = match.Groups[1].Value;
                    if (val.Equals("inf", StringComparison.OrdinalIgnoreCase))
                    {
                        return double.PositiveInfinity;
                    }
                    if (double.TryParse(val, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double psnr))
                    {
                        return psnr;
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogInfo($"ComputePsnrUncapped �쳣: {ex.Message}");
                return null;
            }
        }

        private static double TryGetPooledDouble(JsonElement pooled, string key, string subKey)
        {
            try
            {
                if (pooled.TryGetProperty(key, out var e) &&
                    e.TryGetProperty(subKey, out var v))
                    return v.GetDouble();
            }
            catch { }
            return double.NaN;
        }

        private static double? TryExtractVmaf(string combinedOutput)
        {
            // ���䲻ͬ FFmpeg �汾�������ʽ
            var patterns = new[]
            {
        @"VMAF score:\s*([0-9.]+)",
        @"vmaf\s*=\s*([0-9.]+)",
        @"aggregate_vmaf\s*:\s*([0-9.]+)"
    };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(combinedOutput, pattern, RegexOptions.IgnoreCase);
                if (match.Success &&
                    double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var score))
                {
                    return score;
                }
            }
            return null;
        }
        private QualityMetrics? ParseVmafJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var pooled = doc.RootElement.GetProperty("pooled_metrics");

                double ssim = pooled.TryGetProperty("float_ssim", out var e) ? e.GetProperty("mean").GetDouble() : 0;
                double ms_ssim = pooled.TryGetProperty("float_ms_ssim", out e) ? e.GetProperty("mean").GetDouble() : 0;
                // VMAF �ֶ�ȱʧʱ��Ϊ NaN������ -1 �� 0 ������Ϊ��Ч����
                double vmaf = pooled.TryGetProperty("vmaf", out e)
                                ? e.GetProperty("mean").GetDouble()
                                : double.NaN;
                double psnr_y = pooled.TryGetProperty("psnr_y", out e) ? e.GetProperty("mean").GetDouble() : 0;
                // CAMBI/ADM �ݲ����ã�����ָ�
                // double cambi = TryGetPooledDouble(pooled, "cambi", "cambi");
                // if (double.IsNaN(cambi)) cambi = TryGetPooledDouble(pooled, "cambi", "score");
                // double adm = TryGetPooledDouble(pooled, "adm", "adm");
                // if (double.IsNaN(adm)) adm = TryGetPooledDouble(pooled, "adm", "score");

                return new QualityMetrics
                {
                    SSIM = ssim,
                    PSNR_Y = psnr_y,
                    MS_SSIM = ms_ssim,
                    VMAF = vmaf,
                };
            }
            catch (Exception ex)
            {
                _logger.LogInfo($"���� VMAF JSON ʧ��: {ex.Message}");
                return null;
            }
        }








        /// <summary>
        /// ����ģ����������ļ���������·����
        /// </summary>
        /// <summary>
        /// ����ģ���Դ�ļ���Ϣ��������ļ���������Ŀ¼��
        /// </summary>
        private string GetOutputFileName(string inputFile, int index)
        {
            string template = _config.OutputNameFormat.Trim('"', '\'').Trim();
            string name = Path.GetFileNameWithoutExtension(inputFile);
            string ext = Path.GetExtension(inputFile);
            string dir = Path.GetFileName(Path.GetDirectoryName(inputFile)) ?? "";
            var now = DateTime.Now;

            // ����ռλ��
            string result = template
                .Replace("{name}", name)
                .Replace("{filename}", name)
                .Replace("{ext}", ext)
                .Replace("{dir}", dir);

            // �������ռλ��
            result = result
                .Replace("{encoder}", _config.Encoder)
                .Replace("{crf}", _config.BaseCRF.ToString())
                .Replace("{preset}", _config.MetricMode ?? "")
                .Replace("{speed}", _config.FinalCpuUsed.ToString())
                .Replace("{pixfmt}", _config.PixelFormat ?? "auto")
                .Replace("{bitdepth}", _config.BitDepth.ToString())
                .Replace("{lossless}", _config.Lossless ? "lossless" : "lossy");

            // ʱ��ռλ��
            result = result
                .Replace("{date}", now.ToString("yyyy-MM-dd"))
                .Replace("{time}", now.ToString("HH-mm-ss"))
                .Replace("{datetime}", now.ToString("yyyy-MM-dd_HH-mm-ss"));

            // {index} ֧���Զ�����: {index}��01, {index:000}��001
            result = Regex.Replace(result, @"\{index(?::(\d+))?\}",
                m => index.ToString("D" + (m.Groups[1].Success ? m.Groups[1].Value : "2")));

            // ȷ����չ��Ϊ .avif
            if (!result.EndsWith(".avif", StringComparison.OrdinalIgnoreCase))
                result += ".avif";

            // �滻�Ƿ��ļ����ַ�
            foreach (char c in Path.GetInvalidFileNameChars())
                result = result.Replace(c, '_');

            return result.Trim();
        }



        // ==================== ����� ====================
        public async Task RunAsync(CancellationToken externalToken = default)
        {
            try
            {
                _globalCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
                _cancelKeyHandler = (s, e) =>
                {
                    e.Cancel = true;
                    SafeWriteLine("\n[WARN] ���ڰ�ȫֹͣ�����Ժ�...");
                    _globalCts?.Cancel();
                };
                Console.CancelKeyPress += _cancelKeyHandler;

                Console.OutputEncoding = Encoding.UTF8;
                _progress.Start(DateTime.Now);

                // �����ϣ�Job Object ״̬
                if (OperatingSystem.IsWindows())
                {
                    if (JobObjectHelper.IsActive)
                        _logger.LogInfo("[Job] �ӽ��̱����Ѽ��� �� �������˳�ʱ�Զ���ֹ���� ffmpeg");
                    else
                        _logger.LogInfo("[Job] �ӽ��̱���δ���� �� ʹ���ڴ�����б������ֹ");
                }

                _logger.LogInfo($"Pipeline started: CRF={_config.BaseCRF} TargetSSIM={_config.TargetSSIM}");
                _logger.LogInfo(
                    $"Encoder={_config.Encoder} " +
                    $"Lossless={_config.Lossless} " +
                    $"PixelFmt={_config.PixelFormat ?? "auto"} " +
                    $"BitDepth={_config.BitDepth} " +
                    $"CRFSearch={_config.UseCRFSearch} " +
                    $"MaxJobs={_config.MaxJobs}");
                if (_config.Lossless)
                {
                    _logger.LogInfo("����ģʽ���������������֤��ʧ���ļ����뵽 _failed_verification/");
                }

                await PrintStartupInfoAsync();

                var files = await ScanAndPrepareFilesAsync();
                if (files == null || files.Count == 0) return;

                // �� �ϵ�����������ݸ� + �ط���־ + ���������
                if (_config.Resume)
                {
                    _logger.LogInfo("[RESUME] �ϵ�����ģʽ��������ʱ�ļ�...");
                    // �������ݸ�
                    foreach (var f in _fs.GetFiles(_outputDir, "_tmp_*.avif"))
                        try { _fs.DeleteFile(f); } catch { }
                    foreach (var f in _fs.GetFiles(_outputDir, "_p_*.avif"))
                        try { _fs.DeleteFile(f); } catch { }
                    // ����������ʱĿ¼���� Directory.GetDirectories ���� GetFiles��
                    try
                    {
                        foreach (var dir in Directory.GetDirectories(_outputDir, "_search_advanced_*"))
                            try { if (_fs.DirectoryExists(dir)) _fs.DeleteDirectory(dir, true); } catch { }
                        foreach (var dir in Directory.GetDirectories(_outputDir, "_advanced_metrics_*"))
                            try { if (_fs.DirectoryExists(dir)) _fs.DeleteDirectory(dir, true); } catch { }
                    }
                    catch { }

                    // ���ؿ��ղ��ط���־
                    // �� ���ز��ԣ���������Դȡ������ȫ��ȷ����ɲ�����ɣ�
                    var (snapshotDone, savedConfigJson, savedInputDir) = LoadSnapshot();

                    // �ӿ��ջָ��������ã�--resume ʱ��������ָ��������
                    if (savedConfigJson != null)
                    {
                        try
                        {
                            using var cfgDoc = JsonDocument.Parse(savedConfigJson);
                            var cfg = cfgDoc.RootElement;
                            if (cfg.TryGetProperty("Encoder", out var enc)) _config.Encoder = enc.GetString()!;
                            if (cfg.TryGetProperty("Lossless", out var ll)) _config.Lossless = ll.GetBoolean();
                            if (cfg.TryGetProperty("UseCRFSearch", out var sr)) _config.UseCRFSearch = sr.GetBoolean();
                            if (cfg.TryGetProperty("BaseCRF", out var bcrf)) _config.BaseCRF = bcrf.GetInt32();
                            if (cfg.TryGetProperty("MinCRF", out var mn)) _config.MinCRF = mn.GetInt32();
                            if (cfg.TryGetProperty("MaxCRF", out var mx)) _config.MaxCRF = mx.GetInt32();
                            if (cfg.TryGetProperty("MetricMode", out var mm)) _config.MetricMode = mm.GetString()!;
                            if (cfg.TryGetProperty("PixelFormat", out var pf)) _config.PixelFormat = pf.GetString();
                            if (cfg.TryGetProperty("BitDepth", out var bd)) _config.BitDepth = bd.GetInt32();
                            if (cfg.TryGetProperty("OutputNameFormat", out var ot)) _config.OutputNameFormat = ot.GetString()!;
                            if (cfg.TryGetProperty("RecurseSubdirectories", out var rc)) _config.RecurseSubdirectories = rc.GetBoolean();
                            if (cfg.TryGetProperty("SerialEncode", out var se)) _config.SerialEncode = se.GetBoolean();
                            if (cfg.TryGetProperty("UsePriorSearch", out var ps)) _config.UsePriorSearch = ps.GetBoolean();
                            if (cfg.TryGetProperty("UseProxySearch", out var px)) _config.UseProxySearch = px.GetBoolean();
                            if (cfg.TryGetProperty("SearchCpuUsed", out var sc)) _config.SearchCpuUsed = sc.GetInt32();
                            if (cfg.TryGetProperty("FinalCpuUsed", out var fc)) _config.FinalCpuUsed = fc.GetInt32();
                            if (cfg.TryGetProperty("MaxResolution", out var mr)) _config.MaxResolution = mr.GetInt32();
                            if (cfg.TryGetProperty("MaxJobs", out var mj)) _config.MaxJobs = mj.GetInt32();
                            if (cfg.TryGetProperty("InputExtensions", out var ie)) _config.InputExtensions = ie.GetString();
                            if (cfg.TryGetProperty("EncodeTimeoutMinutes", out var et)) _config.EncodeTimeoutMinutes = et.GetInt32();
                            if (cfg.TryGetProperty("SearchTimeoutMinutes", out var st)) _config.SearchTimeoutMinutes = st.GetInt32();
                            if (cfg.TryGetProperty("SafeTimeoutMinutes", out var sf)) _config.SafeTimeoutMinutes = sf.GetInt32();
                            if (cfg.TryGetProperty("SsimTimeoutMinutes", out var ss)) _config.SsimTimeoutMinutes = ss.GetInt32();
                            if (cfg.TryGetProperty("SweepMode", out var sw)) _config.SweepMode = sw.GetBoolean();
                            if (cfg.TryGetProperty("DryRun", out var dr)) _config.DryRun = dr.GetBoolean();
                            if (cfg.TryGetProperty("Verbose", out var vb)) _config.Verbose = vb.GetBoolean();
                            _logger.LogInfo($"[RESUME] �Ѵӿ��ջָ���������: Encoder={_config.Encoder} CRF={_config.BaseCRF}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogInfo($"[RESUME] ���ûָ�ʧ��: {ex.Message}��ʹ�õ�ǰ����");
                        }
                    }
                    var journalDone = ReplayJournal(null);  // �ط�ȫ����־������ʱ�����
                    var csvDone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    // CSV����ȡ "�ɹ�" �ж�Ӧ�������ļ�����ȷ���������ڶ��ţ�
                    if (_fs.FileExists(_csvPath))
                    {
                        try
                        {
                            var csvLines = File.ReadAllLines(_csvPath);
                            int statusIdx = -1, fileIdx = -1;
                            for (int i = 0; i < csvLines.Length; i++)
                            {
                                var cols = SplitCsvLine(csvLines[i]);
                                if (i == 0)
                                {
                                    for (int c = 0; c < cols.Length; c++)
                                    {
                                        if (cols[c] == "״̬") statusIdx = c;
                                        if (cols[c] == "�ļ���") fileIdx = c;
                                    }
                                    continue;
                                }
                                if (statusIdx >= 0 && fileIdx >= 0 &&
                                    statusIdx < cols.Length && fileIdx < cols.Length &&
                                    cols[statusIdx] == "�ɹ�")
                                {
                                    string csvFileName = cols[fileIdx];
                                    // ��ʵ�� index ����ӳ�䣨���� -1����������ģ���λ��
                                    foreach (var (path, idx) in files)
                                    {
                                        string outPath = GetOutputPath(path, idx);
                                        if (Path.GetFileName(outPath) == csvFileName)
                                        {
                                            csvDone.Add(path);
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    // ��������Դȫ��ȷ�� �� ����Ϊ���
                    var completed = new HashSet<string>(snapshotDone, StringComparer.OrdinalIgnoreCase);
                    completed.IntersectWith(journalDone);
                    if (csvDone.Count > 0) completed.IntersectWith(csvDone);  // CSV ���ڲŲ��뽻��

                    _logger.LogInfo(
                        $"[RESUME] ����:{snapshotDone.Count} ��־:{journalDone.Count} " +
                        $"CSV:{csvDone.Count} �� ����:{completed.Count}");

                    // �ļ�ϵͳ������֤������־ȱʧʱ��¼�����Զ������ɣ��������������У�
                    foreach (var (path, idx) in files)
                    {
                        if (completed.Contains(path)) continue;
                        string outPath = GetOutputPath(path, idx);
                        if (_fs.FileExists(outPath) && _fs.GetFileLength(outPath) >= 200)
                            _logger.LogInfo(
                                $"[RESUME] ����ļ����ڵ���־�޼�¼: {Path.GetFileName(outPath)}�������±���");
                    }

                    // ���������
                    var remaining = files.Where(f => !completed.Contains(f.path)).ToList();
                    int skipped = files.Count - remaining.Count;
                    _logger.LogInfo($"[RESUME] {skipped}/{files.Count} ����ɣ�ʣ�� {remaining.Count} ������");
                    if (remaining.Count == 0)
                    {
                        _logger.LogInfo("[RESUME] ȫ������ɣ����账��");
                        return;
                    }
                    files = remaining;
                    // ���ļ������䣨ScanAndPrepareFilesAsync ���裩��ֻ��������ɼ���
                    _progress.SetInitialProcessed(skipped);
                }

                // ��ʼ�� Journal���ǻָ�ģʽ������ɿ��ձ������
                if (!_config.Resume)
                {
                    try { if (_fs.FileExists(_snapshotPath)) _fs.DeleteFile(_snapshotPath); } catch { }
                    try { if (_fs.FileExists(_journalPath)) _fs.DeleteFile(_journalPath); } catch { }
                }
                InitJournal();

                var results = await ProcessInitialBatchAsync(files);
                results = await RetryFailuresAsync(results);

                // �˳�ǰ�ϲ�������� + ����� �� �������տ���
                if (_config.Resume)
                {
                    // �ϲ����������е�����б�
                    var (oldCompleted, _, _) = LoadSnapshot();
                    var newCompleted = results.Where(r => r != null && (r.Success || r.Skipped))
                        .Select(r => r!.InputPath);
                    SaveSnapshot(oldCompleted.Union(newCompleted));
                    int totalNonSkipped = results.Count(r => r != null && !r.Skipped);
                    if (newCompleted.Count() == totalNonSkipped)
                    {
                        CloseJournal();
                        try { if (_fs.FileExists(_journalPath)) _fs.DeleteFile(_journalPath); } catch { }
                    }
                }

                await PrintSummaryAndExport(results);
            }
            finally
            {
                FinalCleanup();   // ���۳ɹ���ʧ�ܡ��쳣����ִ��
            }
        }

        #endregion

        #region ��������

        /// <summary> ��ӡ�����Ϣ��������������� </summary>
        private async Task PrintStartupInfoAsync()
        {
            SafeWriteLine("===== AVIF ȫ�Զ�������ˮ�� =====");
            SafeWriteLine($"�����ļ���: {_inputDir}");
            SafeWriteLine($"����ļ���: {_outputDir}");

            string crfInfo;
            if (_config.UseCRFSearch)
                crfInfo = $"����CRF: {_config.BaseCRF}, ������Χ: {_config.MinCRF}-{_config.MaxCRF}";
            else
                crfInfo = $"CRF: {_config.BaseCRF}";

            // ���� MetricMode ��̬���ɱ�ǩ��ԭ����ֵ
            string metricMode = (_config.MetricMode ?? "vmaf").ToUpper();
            string targetDisplay = GetTargetDisplayString(_config);

            SafeWriteLine($"������: {_config.Encoder}");
            SafeWriteLine($"ͬʱ����ffmpeg������: {_maxFfmpegConcurrency}");
            SafeWriteLine($"{crfInfo}  {metricMode}Ŀ��: {targetDisplay}  ����: {_config.UseCRFSearch}  ���ظ�ʽ: {(_config.AutoSource ? "����Ӧ" : (_config.PixelFormat ?? "��̬"))}");
            SafeWriteLine($"�ļ���ģ��: {_config.OutputNameFormat}");
        }

        // ������������ȡ��ǰ���õ�Ŀ��ֵ��ʾ�ַ���������ԭ��ֵ��
        private static string GetTargetDisplayString(PresetConfig config)
        {
            string metricMode = config.MetricMode ?? "vmaf";
            double target = config.GetEffectiveTarget();

            if (metricMode.StartsWith("xpsnr", StringComparison.OrdinalIgnoreCase))
            {
                return $"{target:F1} dB ({(config.XpsnrTargetChannel ?? "W").ToUpper()})";
            }

            return metricMode.ToLower() switch
            {
                "vmaf" => target.ToString("F0"),
                "psnr" => target.ToString("F1") + " dB",
                "ssim" => target.ToString("F4"),
                "msssim" => target.ToString("F4"),
                "mix" => target.ToString("F4"),
                "ssimu2" => target.ToString("F4") + " (SSIMU2)",
                "butter3" => target.ToString("F4") + " (Butter3)",
                "gmsd" => target.ToString("F4") + " (GMSD)",
                _ => target.ToString("F4")
            };
        }

        /// <summary> ɨ������Ŀ¼�����ذ��ļ���С�������е��ļ��б� </summary>
        private async Task<List<(string path, int index)>?> ScanAndPrepareFilesAsync()
        {
            if (!_fs.DirectoryExists(_inputDir))
            {
                SafeWriteLine("�����ļ��в����ڡ�");
                return null;
            }
            _fs.CreateDirectory(_outputDir);

            // �������ù�����չ���б���û�δָ����ʹ�� 12 ��Ĭ��ȫ����ʽ
            string[] extensions;
            if (!string.IsNullOrWhiteSpace(_config.InputExtensions))
            {
                extensions = _config.InputExtensions
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => x.StartsWith('.') ? x.ToLower() : $".{x}".ToLower())
                    .ToArray();
            }
            else
            {
                extensions = PresetConfig.DefaultInputExtensions;
            }

            var searchOption = _config.RecurseSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            // �� �޸���ȥ�����ܵ� \\?\ ��·��ǰ׺������ Directory.EnumerateFiles �޷��ݹ���Ŀ¼
            string scanDir = NormalizePathForExternalTool(_inputDir);
            var sortedFiles = _fs.EnumerateFiles(scanDir, "*.*", searchOption)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()))
                .OrderBy(f => f, new NaturalComparer())
                .Select((path, idx) => (path, index: idx + 1))
                .ToList();

            if (sortedFiles.Count == 0)
            {
                SafeWriteLine("δ�ҵ�ͼƬ��");
                return null;
            }

            _progress.SetTotalFiles(sortedFiles.Count);
            SafeWriteLine($"������: {_progress.TotalFiles} ��\n");

            // �������ⳬ��ֱ���ͼƬ
            try
            {
                var probe = await GetProbeInfoAsync(sortedFiles[0].path);
                if (probe != null && Math.Max(probe.Width, probe.Height) > 3840)
                {
                    SafeWriteLine(
                        $"[INFO] ��⵽�߷ֱ���ͼƬ " +
                        $"({probe.Width}x{probe.Height})��" +
                        "AV1 ������ܽ���������ʹ�� --max-resolution ���Ʒֱ��ʡ�");
                }
            }
            catch { }

            var processingOrder = sortedFiles
                .OrderByDescending(t => _fs.GetFileLength(t.path))
                .ToList();
            return processingOrder;
        }

        /// <summary> �״��������������ļ� </summary>
        private async Task<List<EncodeResult?>> ProcessInitialBatchAsync(List<(string path, int index)> files)
        {
            var result = await ProcessFilesAsync(files, _config, isRetry: false);
            return [.. result.Select(r => (EncodeResult?)r)];
        }

        /// <summary> ����ʧ�ܵ��ļ��������غϲ���Ľ���б� </summary>
        /// <summary> ����ʧ�ܵ��ļ��������غϲ���Ľ���б� </summary>
        private async Task<List<EncodeResult?>> RetryFailuresAsync(List<EncodeResult?> results)
        {
            var failures = results.Where(r => r != null && !r.Success && !r.Skipped).ToList();
            if (failures.Count == 0) return results;

            SafeWriteLine($"\n[RETRY] ��ʼ���� {failures.Count} ��ʧ���ļ�...");

            // ��������������ȳ��� 100%
            _progress.SetTotalFiles(_progress.TotalFiles + failures.Count);

            // ʹ�� Result �б������������·��������ƴ��
            var retryFiles = failures.Select(f => (filePath: f!.InputPath, index: f.Index)).ToList();

            // ɾ�����е�����ļ����������
            foreach (var (filePath, index) in retryFiles)
            {
                string outPath = GetOutputPath(filePath, index);
                if (_fs.FileExists(outPath))
                    try { _fs.DeleteFile(outPath); } catch { }
            }

            var retryResults = await ProcessFilesAsync(retryFiles, _config, isRetry: true);
            var resultList = results.ToList();
            foreach (var r in retryResults)
            {
                if (r == null) continue;
                int idx = resultList.FindIndex(existing => existing != null && existing.Index == r.Index);
                if (idx >= 0)
                    resultList[idx] = r;
            }
            return resultList;
        }

        /// <summary> ͳ�Ʋ���ӡ�����ܽᣬ���� CSV </summary>
        /// <summary> ͳ�Ʋ���ӡ�����ܽᣬ���� CSV </summary>
        private async Task PrintSummaryAndExport(List<EncodeResult?> results)
        {
            // �� �ȴ����к�̨�߼�ָ��������
            if (!_advancedMetricTasks.IsEmpty)
            {
                SafeWriteLine("?? �ȴ���̨�߼�ָ��������...");
                try { await Task.WhenAll([.. _advancedMetricTasks]); }
                catch (Exception ex) { _logger.LogError($"��̨�߼������쳣: {ex.Message}"); }
            }

            // �� �ȴ����к�̨ XPSNR ������ɲ�����
            if (!_xpsnrTasks.IsEmpty)
            {
                try { await Task.WhenAll([.. _xpsnrTasks]); }
                catch (Exception ex) { _logger.LogInfo($"XPSNR ��̨�쳣: {ex.Message}"); }
            }

            var totalTime = DateTime.Now - _progress.StartTime;
            var allResults = results.Where(r => r != null).Cast<EncodeResult>().ToList();
            int successCount = allResults.Count(r => !r.Skipped && r.Success);
            int failCount = allResults.Count(r => !r.Skipped && !r.Success);
            int skipCount = allResults.Count(r => r.Skipped);

            long totalOriginal = allResults.Where(r => !r.Skipped && r.Success).Sum(r => r.OriginalSize);
            long totalOutput = allResults.Where(r => !r.Skipped && r.Success).Sum(r => r.OutputSize);
            double overallRatio = totalOriginal == 0 ? 0 : 1.0 - (double)totalOutput / totalOriginal;

            SafeWriteLine("\n================ ת����� ================");
            SafeWriteLine($"���ļ���: {_progress.TotalFiles}  �ɹ�: {successCount}  ʧ��: {failCount}  ����: {skipCount}");
            SafeWriteLine($"ԭʼ��С: {FormatSize(totalOriginal)}  �����С: {FormatSize(totalOutput)}");
            SafeWriteLine($"����ѹ����: {overallRatio:P1}  �ܺ�ʱ: {FormatTimeSpan(totalTime)}");
            // �Ƴ��ɵĻ�������������Ϊ ICacheManager δ��¶��������
            _logger.LogInfo(
                $"Finished. �ɹ�: {successCount}, ʧ��: {failCount}, " +
                $"����: {skipCount}, ��ʱ: {FormatTimeSpan(totalTime)}");
            if (successCount > 0)
            {
                double avgEncode = allResults
                    .Where(r => r.Success)
                    .Select(r => r.EncodeTime.TotalSeconds)
                    .DefaultIfEmpty(0).Average();
                _logger.LogInfo(
                    $"ƽ�������ʱ: {avgEncode:F1}s, " +
                    $"����ѹ����: {overallRatio:P1}, " +
                    $"�����: {FormatSize(totalOutput)}");
            }


            // �ӻ������߼�ָ��
            foreach (var r in allResults)
            {
                if (!string.IsNullOrEmpty(r.AdvancedMetricsCacheKey) && _cache.TryGetMetrics(r.AdvancedMetricsCacheKey, out var updated))
                {
                    r.FinalSSIMULACRA2 = updated?.SSIMULACRA2;
                    r.FinalButteraugli_Raw = updated?.Butteraugli_Raw;
                    r.FinalButteraugli_3norm = updated?.Butteraugli_3norm;
                    r.FinalGMSD = updated?.GMSD;
                    r.FinalXPSNR_Y = updated?.XPSNR_Y;
                    r.FinalXPSNR_U = updated?.XPSNR_U;
                    r.FinalXPSNR_V = updated?.XPSNR_V;
                    r.FinalWXPSNR = updated?.W_XPSNR;
                    // r.FinalCAMBI = updated?.CAMBI;   // �ݲ�����
                    // r.FinalADM = updated?.ADM;       // �ݲ�����
                }
            }

            // �� ȫ������������� �� ���� 100% �� GUI
            _guiProgress?.Report(100);

            // ��ע�ⲿ����ȱʧ���µĸ߼�ָ���ȱ
            bool hasSsimu2 = EncoderUtils.FindExecutable("ssimulacra2") != null;
            bool hasButter = EncoderUtils.FindExecutable("butteraugli_main") != null;
            if (!hasSsimu2 || !hasButter)
            {
                var missingTools = new List<string>();
                if (!hasSsimu2) missingTools.Add("SSIMULACRA2(ssimulacra2.exe)");
                if (!hasButter) missingTools.Add("Butteraugli(butteraugli_main.exe)");
                string note = $"�ⲿ����ȱʧ: {string.Join(", ", missingTools)}";

                foreach (var r in allResults)
                {
                    bool advancedEmpty = !r.FinalSSIMULACRA2.HasValue &&
                        !r.FinalButteraugli_Raw.HasValue &&
                        !r.FinalButteraugli_3norm.HasValue;
                    if (r.Success && advancedEmpty)
                    {
                        r.ErrorMessage = string.IsNullOrEmpty(r.ErrorMessage)
                            ? note
                            : r.ErrorMessage + " | " + note;
                    }
                }
                SafeWriteLine(
                    $"[INFO] �ⲿ����ȱʧ���߼�ָ�굥Ԫ�����: {string.Join(", ", missingTools)}");
            }

            ExportCsv(allResults);
        }

        /// <summary> ������뻺�漰��ʱ�ļ� </summary>
        private void FinalCleanup()
        {
            // �� ���ף�ǿ��ɱ������������� ffmpeg �ӽ��̣�Job Object ʧ��ʱ���ף�
            foreach (var p in _spawnedProcesses)
            {
                try
                {
                    if (!p.HasExited)
                    {
                        p.Kill(entireProcessTree: true);
                        _logger.LogInfo($"ǿ����ֹ������� PID={p.Id}");
                    }
                }
                catch { }
            }
            // �ͷ����� Process ����
            foreach (var p in _spawnedProcesses)
            {
                try { if (p.HasExited) p.Dispose(); } catch { }
            }
            _spawnedProcesses.Clear();

            // ������뻺��Ŀ¼
            CleanDirectory(Path.Combine(_outputDir, "_enc_cache"));

            // �������ź����ʱͼƬĿ¼
            string scaledDir = Path.Combine(_outputDir, "_scaled");
            if (_fs.DirectoryExists(scaledDir))
            {
                try { _fs.DeleteDirectory(scaledDir, true); } catch { }
            }

            // ����� _p_ ǰ׺����ʱ AVIF �ļ�
            foreach (var f in _fs.GetFiles(_outputDir, "_p_*.avif"))
                try { _fs.DeleteFile(f); } catch { }
            foreach (var f in _fs.GetFiles(_outputDir, "_tmp_*.avif"))
                try { _fs.DeleteFile(f); } catch { }

            // �� ��������ָ����ʱĿ¼
            try
            {
                foreach (var dir in Directory.GetDirectories(_outputDir, "_search_advanced_*"))
                    try { Directory.Delete(dir, true); } catch { }
                foreach (var dir in Directory.GetDirectories(_outputDir, "_advanced_metrics_*"))
                    try { Directory.Delete(dir, true); } catch { }
            }
            catch { }

            // �����ʵ�����ɵ� ComputeAllMetrics ��ʱ JSON Ŀ¼
            string metricsDir = Path.Combine(Environment.CurrentDirectory, $"avif_metrics_tmp_{_instanceId}");
            if (Directory.Exists(metricsDir))
            {
                try { Directory.Delete(metricsDir, true); } catch { }
            }

            // ���ݾɰ棺������ʵ����׺������Ŀ¼�������ں��Ƴ���
            string legacyMetricsDir = Path.Combine(Environment.CurrentDirectory, "avif_metrics_tmp");
            if (Directory.Exists(legacyMetricsDir))
            {
                try { Directory.Delete(legacyMetricsDir, true); } catch (Exception ex) { _logger?.LogError($"清理临时目录异常: "+ex.Message); }
            }
        }

        private void CleanDirectory(string dir)
        {
            if (_fs.DirectoryExists(dir))
            {
                try
                {
                    _fs.DeleteDirectory(dir, true);
                    _logger.LogInfo($"����������: {dir}");
                }
                catch (Exception ex) { _logger.LogInfo($"����ʧ��: {dir} - {ex.Message}"); }
            }
        }

        // ========== �޸���� PrintProgress������������ ==========
        private void PrintProgress(EncodeResult? r)
        {
            SafeWriteLine(_progress.GetProgressLine(r));
        }



        /// <summary>
        /// ȷ��·���� Windows ��ʹ�ó�·����ʽ����� \\?\ ǰ׺����
        /// �Ӷ�ͻ�� 260 �ַ��� MAX_PATH ���ơ�
        /// </summary>
        private static string EnsureLongPath(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                // ����ӹ���·��ǰ׺��ֱ�ӷ���
                if (path.StartsWith(@"\\?\"))
                    return path;

                string full = Path.GetFullPath(path);

                // ���� UNC ·����\\server\share\... �� \\?\UNC\server\share\...
                if (full.StartsWith(@"\\") && !full.StartsWith(@"\\?\"))
                {
                    // UNC ·����������ͷ�ķ�б�ܣ�����һ����б���滻Ϊ \\?\UNC
                    return @"\\?\UNC" + full.Substring(1);
                }
                else
                {
                    // ��ͨ�̷�·������ C:\...��
                    return @"\\?\" + full;
                }
            }
            // �� Windows ϵͳԭ�����أ�Linux/macOS ���账���
            return path;
        }

        /// <summary>
        /// ���Դ�ļ��Ƿ���� Alpha ͨ�������ȴ�ͳһ Probe �����ȡ��
        /// </summary>
        private async Task<bool> SourceHasAlpha(string filePath)
        {
            // �� ���ȴ�ͳһ Probe �����ȡ
            var info = await GetProbeInfoAsync(filePath);
            if (info != null)
            {
                // ͬ�����¾ɻ���
                string normalizedPath = GetNormalizedPathForCache(filePath);
                _srcAlphaCache[normalizedPath] = info.HasAlpha;
                return info.HasAlpha;
            }

            // ���ף�����̽��
            string args = $"-v error -select_streams v:0 -show_entries stream=pix_fmt -of csv=p=0 \"{filePath}\"";
            string raw = await RunProbeAsync(_ffprobePath, args);
            string fmt = raw.Trim().ToLower();
            bool hasAlpha = fmt switch
            {
                "rgba" or "bgra" or "argb" or "abgr" => true,
                "rgba64le" or "bgra64le" => true,
                _ => false
            };
            _srcAlphaCache[GetNormalizedPathForCache(filePath)] = hasAlpha;
            return hasAlpha;
        }



        private readonly ConcurrentDictionary<string, string> _srcPixFmtCache = new();

        /// <summary>
        /// ��ȡԴ�ļ��ı�׼�����ظ�ʽ������ yuv420p��yuv444p10le��
        /// </summary>
        /// <summary>
        /// ��ȡԴ�ļ��ı�׼�����ظ�ʽ������ yuv420p��yuv444p10le��
        /// </summary>
        /// <summary>
        /// ��ȡԴ�ļ��ı�׼�����ظ�ʽ����λ�� RGB �ᱣ���Ӧλ�10?bit�����Ҷ�ӳ��Ϊ yuv420p
        /// </summary>
        /// <summary>
        /// ��ȡԴ�ļ��ı�׼�����ظ�ʽ����λ�� RGB �ᱣ���Ӧλ�10?bit�����Ҷ�ӳ��Ϊ yuv420p
        /// </summary>
        /// <summary>
        /// ��ȡԴ�ļ��ı�׼�����ظ�ʽ������ʹ��ͳһ Probe ���棬�����ظ� ffprobe��
        /// ��λ�� RGB �ᱣ���Ӧλ�10?bit�����Ҷ�ӳ��Ϊ yuv420p��
        /// </summary>
        /// <summary>
        /// ��ȡԴ�ļ��ı�׼�����ظ�ʽ������ʹ��ͳһ Probe ���棬�����ظ� ffprobe��
        /// ��λ�� RGB �ᱣ���Ӧλ�10?bit�����Ҷ�ӳ��Ϊ yuv420p��
        /// </summary>
        private async Task<string> GetSourcePixelFormat(string filePath)
        {
            // �� ���ȴ�ͳһ Probe �����ȡ
            var info = await GetProbeInfoAsync(filePath);
            if (info != null)
            {
                string fmt = info.PixFmt; // �Ѿ���Сд���� rgba��gray16le ��

                // ���ɵ� Alpha ���棨���δ��䣩
                string normalizedPath = GetNormalizedPathForCache(filePath);
                if (!_srcAlphaCache.ContainsKey(normalizedPath))
                    _srcAlphaCache[normalizedPath] = info.HasAlpha;

                // ���ظ�ʽ��׼��������ԭ���߼���
                if (fmt == "gray" || fmt.StartsWith("gray"))
                {
                    bool is10bit = fmt.Contains("16") || fmt.Contains("10");
                    fmt = is10bit ? "yuv420p10le" : "yuv420p";
                }
                else if (fmt.Contains("yuvj"))
                {
                    fmt = fmt.Replace("yuvj", "yuv");
                }
                // �� �޸Ĵ�����չ RGB ��ʽǰ׺�жϣ����� argb��abgr��rgba��bgra ��
                else if (fmt.StartsWith("rgb") || fmt.StartsWith("bgr") || fmt.StartsWith("gbr") ||
                         fmt.StartsWith("argb") || fmt.StartsWith("abgr") || fmt.StartsWith("rgba") || fmt.StartsWith("bgra"))
                {
                    bool is4Comp = fmt.Contains('a') || fmt.Contains('0') || fmt.Contains('x') ||
                                   fmt == "argb" || fmt == "abgr";
                    if (fmt.Contains("64") && !is4Comp) is4Comp = true;

                    int components = is4Comp ? 4 : 3;
                    var match = Regex.Match(fmt, @"(\d+)");
                    int totalBits = 0;
                    if (match.Success && int.TryParse(match.Groups[1].Value, out totalBits))
                    {
                    }
                    if (totalBits == 0) totalBits = components * 8;
                    int perCompBits = totalBits / components;
                    int targetBitDepth = Math.Clamp(perCompBits, 8, 10);

                    string chromaFmt = targetBitDepth >= 10 ? "yuv444p10le" : "yuv444p";
                    if (info.HasAlpha)
                        chromaFmt = chromaFmt.Replace("yuv", "yuva");
                    fmt = chromaFmt;
                }

                if (string.IsNullOrEmpty(fmt)) fmt = "yuv420p";

                // ���¾ɵ����ظ�ʽ����
                _srcPixFmtCache[normalizedPath] = fmt;
                return fmt;
            }

            // ---- ���˵�ԭ�е���̽�⣨�����ϲ�Ӧ�������Ϊ���ף� ----
            string raw = await RunProbeAsync(_ffprobePath,
                $"-v error -select_streams v:0 -show_entries stream=pix_fmt -of csv=p=0 \"{filePath}\"");
            string fmtFallback = raw.Trim().ToLower();

            // �򵥱�׼������ȥ���Ӳ����Ա�֤���򲻱����������� probe �����ṩ��
            if (fmtFallback == "gray" || fmtFallback.StartsWith("gray"))
                fmtFallback = fmtFallback.Contains("16") || fmtFallback.Contains("10") ? "yuv420p10le" : "yuv420p";
            else if (fmtFallback.Contains("yuvj"))
                fmtFallback = fmtFallback.Replace("yuvj", "yuv");
            else if (fmtFallback.Contains("rgb") || fmtFallback.Contains("bgr"))
                fmtFallback = fmtFallback.Contains("64") ? "yuva444p10le" : "yuva444p"; // ���ؼ����� alpha

            if (string.IsNullOrEmpty(fmtFallback)) fmtFallback = "yuv420p";
            _srcPixFmtCache[GetNormalizedPathForCache(filePath)] = fmtFallback;
            return fmtFallback;
        }


        private async Task<string> GetPixelFormatForFileAsync(string filePath, bool isLosslessMode, bool hasAlpha)
        {
            if (isLosslessMode)
            {
                // ����ģʽʹ�� YUV444����ѧ���𣩣���Դ�ļ��� Alpha ͨ����Я�� Alpha
                string baseFmt = hasAlpha ? "yuva444p" : "yuv444p";
                return _config.BitDepth >= 10 ? baseFmt + "10le" : baseFmt;
            }

            if (_config.AutoSource)
            {
                string srcFmt = await GetSourcePixelFormat(filePath);
                bool srcIs10bit = srcFmt.EndsWith("10le");
                string baseFmt = srcIs10bit ? srcFmt.Substring(0, srcFmt.Length - 4) : srcFmt;

                // ��ȡɫ�Ȳ��� (444/422/420)
                string chroma = "420";
                if (baseFmt.Contains("444")) chroma = "444";
                else if (baseFmt.Contains("422")) chroma = "422";

                int targetBitDepth = _config.UserSetBitDepth ? _config.BitDepth : (srcIs10bit ? 10 : 8);

                // ��ȷ���� yuva / yuv ��ʽ
                string depthSuffix = targetBitDepth >= 10 ? "10le" : "";
                return hasAlpha ? $"yuva{chroma}p{depthSuffix}" : $"yuv{chroma}p{depthSuffix}";
            }
            else
            {
                // ������Ӧģʽ���ֶ�����
                string baseFmt = _config.PixelFormat ?? "yuv444p10le";
                string depthSuffix = "";
                if (baseFmt.EndsWith("10le"))
                {
                    depthSuffix = "10le";
                    baseFmt = baseFmt.Substring(0, baseFmt.Length - 4);
                }

                string cleanChroma = baseFmt.Replace("a", "");
                string chroma = "420";
                if (cleanChroma.Contains("444")) chroma = "444";
                else if (cleanChroma.Contains("422")) chroma = "422";

                if (_config.UserSetBitDepth)
                {
                    depthSuffix = _config.BitDepth >= 10 ? "10le" : "";
                }

                // ��ȷ���� yuva / yuv ��ʽ
                return hasAlpha ? $"yuva{chroma}p{depthSuffix}" : $"yuv{chroma}p{depthSuffix}";
            }
        }

        // ========== ������֤���� ==========

        /// <summary> ׷��һ��ʧ�ܼ�¼�� _failed_verification/failed_verification.csv���̰߳�ȫ�� </summary>
        private void AppendFailedVerificationCsv(FailedVerificationInfo info)
        {
            lock (_failedCsvLock)
            {
                bool writeHeader = !_fs.FileExists(_failedCsvPath);
                if (writeHeader)
                {
                    string header =
                        "SourceFile,FailedOutput,Encoder,EncoderVersion," +
                        "PixelFormat,BitDepth,Width,Height," +
                        "FailureType,MismatchCount,MaxDelta," +
                        "FirstMismatchX,FirstMismatchY,FirstMismatchChannel," +
                        "RefValue,OutValue," +
                        "RMismatches,GMismatches,BMismatches,AMismatches," +
                        "EncodeCommand,Timestamp";
                    _fs.WriteAllText(_failedCsvPath, header + "\n", System.Text.Encoding.UTF8);
                }

                string csvEscape(string? s) =>
                    "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

                string line = string.Join(",",
                    csvEscape(info.SourceFile),
                    csvEscape(info.FailedOutput),
                    csvEscape(info.Encoder),
                    csvEscape(info.EncoderVersion),
                    csvEscape(info.PixelFormat),
                    info.BitDepth,
                    info.Width,
                    info.Height,
                    info.FailureType,
                    info.MismatchCount,
                    info.MaxDelta,
                    info.FirstMismatchX,
                    info.FirstMismatchY,
                    info.FirstMismatchChannel,
                    info.RefValue,
                    info.OutValue,
                    info.RMismatches,
                    info.GMismatches,
                    info.BMismatches,
                    info.AMismatches,
                    csvEscape(info.EncodeCommand),
                    info.Timestamp
                );
                _fs.AppendAllText(_failedCsvPath, line + "\n");
            }
        }

        /// <summary> д�뵥�ļ� JSON ��֤���� </summary>
        private async Task WriteVerificationReportJsonAsync(FailedVerificationInfo info)
        {
            string jsonPath = Path.Combine(
                _failedVerificationDir,
                Path.GetFileNameWithoutExtension(info.FailedOutput) + ".report.json");
            string json = System.Text.Json.JsonSerializer.Serialize(
                info, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
            await _fs.WriteAllTextAsync(jsonPath, json);
        }

        /// <summary>
        /// ��� ffmpeg ����������汾��
        /// ���� (ffmpegVersion, encoderVersions) ���� encoderVersions �� key Ϊ����������
        /// </summary>
        private static async Task<(string ffmpegVersion, Dictionary<string, string> encoderVersions)>
    GetEncoderVersionsAsync(string ffmpegPath)
        {
            string ffmpegVersion = "";
            var encoderVersions = new Dictionary<string, string>();

            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo(ffmpegPath, "-version")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                process.Start();
                string stdout = await process.StandardOutput.ReadToEndAsync();
                string stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                string output = stdout + stderr;

                // ��ȡ ffmpeg �汾����һ�У�
                var ffmpegMatch = System.Text.RegularExpressions.Regex.Match(
                    output, @"^ffmpeg\s+version\s+([^\s]+)");
                if (ffmpegMatch.Success)
                {
                    ffmpegVersion = ffmpegMatch.Groups[1].Value;
                }

                // ��ȡ����������汾
                var libPatterns = new (string key, string pattern)[]
                {
                    ("libaom-av1", @"libaom-av1\s+([^\s]+)"),
                    ("libsvtav1",  @"libsvtav1\s+([^\s]+)"),
                    ("librav1e",   @"librav1e\s+([^\s]+)"),
                };

                foreach (var (key, pattern) in libPatterns)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(
                        output, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        encoderVersions[key] = m.Groups[1].Value;
                    }
                }
            }
            catch
            {
                // ��Ĭʧ�ܣ��汾��Ϣ�ǹؼ�·��
            }

            return (ffmpegVersion, encoderVersions);
        }





        #endregion
    }

}
