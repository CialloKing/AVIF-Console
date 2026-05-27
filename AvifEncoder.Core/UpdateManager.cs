using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AvifEncoder
{
    /// <summary>更新进度事件参数</summary>
    public class UpdateProgressEventArgs : EventArgs
    {
        public long BytesDownloaded { get; set; }
        public long TotalBytes { get; set; }
        public double SpeedBytesPerSec { get; set; }
        public int Percent
        {
            get
            {
                return TotalBytes > 0
                    ? (int)(BytesDownloaded * 100 / TotalBytes)
                    : 0;
            }
        }
    }

    /// <summary>GitHub Release 信息</summary>
    public class ReleaseInfo
    {
        public bool Success { get; set; }
        public string Error { get; set; } = "";
        public string TagName { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public long FileSize { get; set; }
    }

    /// <summary>更新管理器：检查 → 下载 → 安装</summary>
    public class UpdateManager
    {
        private static readonly HttpClient _http = new(new HttpClientHandler
        {
            AllowAutoRedirect = true
        })
        {
            DefaultRequestHeaders =
            {
                UserAgent =
                {
                    ProductInfoHeaderValue.Parse("AvifEncoder-Update/1.0")
                }
            },
            Timeout = TimeSpan.FromMinutes(10)
        };

        private const string RepoOwner = "CialloKing";
        private const string RepoName = "AVIF-Console";
        private const string ApiUrl =
            $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

        /// <summary>获取当前版本号</summary>
        public static Version CurrentVersion
        {
            get
            {
                return Assembly.GetEntryAssembly()?.GetName().Version
                    ?? Assembly.GetExecutingAssembly().GetName().Version
                    ?? new Version(1, 0, 0);
            }
        }

        /// <summary>
        /// 检查是否有新版本。
        /// </summary>
        public async Task<ReleaseInfo?> CheckForUpdateAsync(
            CancellationToken ct = default)
        {
            try
            {
                string json = await _http.GetStringAsync(ApiUrl, ct);

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string tagName =
                    root.GetProperty("tag_name").GetString() ?? "";
                // 去掉 v 前缀：v1.2.0 → 1.2.0
                if (tagName.StartsWith("v",
                    StringComparison.OrdinalIgnoreCase))
                {
                    tagName = tagName.Substring(1);
                }

                if (CompareVersions(CurrentVersion, tagName) >= 0)
                {
                    return null;
                }

                // 找到与当前进程同名的 .exe 资源
                string currentName =
                    Path.GetFileName(Environment.ProcessPath) ?? "";
                string downloadUrl = "";
                long fileSize = 0;
                var assets = root.GetProperty("assets");
                foreach (var asset in assets.EnumerateArray())
                {
                    string name =
                        asset.GetProperty("name").GetString() ?? "";
                    if (!name.EndsWith(".exe",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (string.Equals(name, currentName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl =
                            asset.GetProperty(
                                "browser_download_url").GetString() ?? "";
                        fileSize = asset.GetProperty("size").GetInt64();
                        break;
                    }

                    // 兜底：记录第一个遇到的 .exe
                    if (downloadUrl.Length == 0)
                    {
                        downloadUrl =
                            asset.GetProperty(
                                "browser_download_url").GetString() ?? "";
                        fileSize = asset.GetProperty("size").GetInt64();
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    return new ReleaseInfo
                    {
                        Success = false,
                        Error = "未在 Release 中找到 .exe 资源"
                    };
                }

                return new ReleaseInfo
                {
                    Success = true,
                    TagName = tagName,
                    DownloadUrl = downloadUrl,
                    FileSize = fileSize
                };
            }
            catch (Exception ex)
            {
                return new ReleaseInfo
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        /// <summary>
        /// 下载新版本到临时文件。
        /// 返回 .new 文件路径。
        /// </summary>
        public async Task<string> DownloadAsync(
    ReleaseInfo release,
    IProgress<UpdateProgressEventArgs>? progress = null,
    CancellationToken ct = default)
        {
            string exePath = Environment.ProcessPath
                ?? AppContext.BaseDirectory;
            string newPath = exePath + ".new";

            try
            {
                using var response = await _http.GetAsync(
                    release.DownloadUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct);
                response.EnsureSuccessStatusCode();

                long total = response.Content.Headers.ContentLength
                    ?? release.FileSize;

                using var stream =
                    await response.Content.ReadAsStreamAsync(ct);
                using var file = new FileStream(
                    newPath, FileMode.Create, FileAccess.Write,
                    FileShare.None, 8192, useAsync: true);

                var buffer = new byte[8192];
                long downloaded = 0;
                var watch =
                    System.Diagnostics.Stopwatch.StartNew();
                int lastReport = -1;

                int read;
                while ((read =
                    await stream.ReadAsync(buffer, ct)) > 0)
                {
                    await file.WriteAsync(
                        buffer.AsMemory(0, read), ct);
                    downloaded += read;

                    if (progress != null && total > 0)
                    {
                        int pct =
                            (int)(downloaded * 100 / total);
                        if (pct != lastReport)
                        {
                            lastReport = pct;
                            double speed =
                                watch.Elapsed.TotalSeconds > 0
                                ? downloaded
                                    / watch.Elapsed.TotalSeconds
                                : 0;
                            progress.Report(
                                new UpdateProgressEventArgs
                                {
                                    BytesDownloaded = downloaded,
                                    TotalBytes = total,
                                    SpeedBytesPerSec = speed
                                });
                        }
                    }
                }

                return newPath;
            }
            catch
            {
                try { File.Delete(newPath); }
                catch { }
                throw;
            }
        }

        /// <summary>
        /// 生成 _update.bat 并启动它，然后退出应用。
        /// </summary>
        public void InstallAndRestart(string newPath)
        {
            string exePath = Environment.ProcessPath
                            ?? AppContext.BaseDirectory;
            string exeDir = Path.GetDirectoryName(exePath)
                ?? AppContext.BaseDirectory;
            string exeName = Path.GetFileName(exePath);
            string batPath = Path.Combine(exeDir, "_update.bat");

            File.WriteAllText(batPath, $"""
                @echo off
                cd /d "{exeDir}"
                timeout /t 2 /nobreak > nul
                del "{exeName}"
                move /y "{exeName}.new" "{exeName}"
                start "" "{exeName}"
                del "%~f0"
                """);

            // 启动 bat（不等待）
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = batPath,
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WorkingDirectory = exeDir
                });

            // 退出应用
            Environment.Exit(0);
        }

        private static int CompareVersions(Version current, string tag)
        {
            if (Version.TryParse(tag, out var latest))
            {
                return current.CompareTo(latest);
            }
            return -1; // 解析失败（tag 不规范），保守假设有新版本
        }
    }
}