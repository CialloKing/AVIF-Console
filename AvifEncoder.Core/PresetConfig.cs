using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace AvifEncoder
{
    public class PresetConfig
    {
        public int BaseCRF { get; set; }

        /// <summary>
        /// 通用质量意图（0-1 尺度，不绑定具体指标）。
        /// 预设（CreateFromPreset）使用此字段存储 SSIM 尺度的质量意图。
        /// 当用户通过 -q 显式指定目标时，优先使用 NativeTargetValue。
        /// </summary>
        public double TargetSSIM { get; set; }

        /// <summary>
        /// 用户显式指定的原生质量目标值（各指标的原始尺度）。
        /// 不为 null 时优先于 TargetSSIM，搜索直接使用此原生值，无需归一化/反算。
        /// </summary>
        public double? NativeTargetValue { get; set; }
        public int FinalCpuUsed { get; set; } = 0;
        public int SearchCpuUsed { get; set; } = 2;
        public bool UseCRFSearch { get; set; }
        public int MaxJobs { get; set; }
        public string? PixelFormat { get; set; }

        // 在 PresetConfig 类中，将 AomParams 属性更新为：
        public string AomParams { get; set; } =
            "aq-mode=3:deltaq-mode=0:enable-chroma-deltaq=1:sharpness=0:" +
            "enable-qm=1:enable-restoration=1:enable-cdef=1:" +
            "enable-global-motion=1:enable-warped-motion=1:" +
            "enable-obmc=1:enable-ref-frame-mvs=1:" +
            "enable-tx64=1:enable-dist-wtd-comp=1:" +
            "enable-rect-tx=1:enable-1to4-partitions=1:" +
            "enable-ab-partitions=1:enable-rect-partitions=1";
        public bool Lossless { get; set; } = false;

        // ---------- XPSNR 原生目标 ----------
        /// <summary>若不为 null，表示使用原生 XPSNR 分贝值作为目标（忽略 TargetSSIM）</summary>
        public double? XpsnrTargetValue { get; set; }
        /// <summary>XPSNR 目标通道：y / u / v / w（null 时视为 w）</summary>
        public string? XpsnrTargetChannel { get; set; }
        public int BitDepth { get; set; } = 8;
        public bool AutoSource { get; set; } = true;
        public bool UserSetChroma { get; set; } = false;
        public bool UserSetBitDepth { get; set; } = false;
        public string OutputNameFormat { get; set; } = "covers-{index}.avif";

        /// <summary> 输出文件冲突时的处理策略 </summary>
        public enum ConflictStrategy
        {
            Rename,    // 自动追加 _1, _2 … 后缀（默认）
            Overwrite, // 直接覆盖已存在的文件
            Skip       // 存在时跳过该文件
        }
        /// <summary> 当前冲突处理策略 </summary>
        public ConflictStrategy FileConflictStrategy { get; set; } = ConflictStrategy.Rename;

        // 自定义 CRF 搜索范围
        public int MinCRF { get; set; } = 0;
        public int MaxCRF { get; set; } = 63;

        // 超时配置（分钟）
        public int EncodeTimeoutMinutes { get; set; } = -1;
        public int SearchTimeoutMinutes { get; set; } = 60;
        public int SafeTimeoutMinutes { get; set; } = 180;
        public int SafeEncodeTimeoutMinutes { get; set; } = 10;
        public int SearchEncodeTimeoutMinutes { get; set; } = 10;
        public int SsimTimeoutMinutes { get; set; } = 5;

        // 自定义编码器
        public string Encoder { get; set; } = "libaom-av1";
        public string MetricMode { get; set; } = "vmaf";

        // ★ 新增：高级指标的原生目标值（仅用于对应搜索模式）
        public double? Ssimu2TargetValue { get; set; }
        public double? Butteraugli3TargetValue { get; set; }
        public double? GmsdTargetValue { get; set; }

        /// <summary> 当前搜索指标的优劣方向：true 表示越小越好，否则越大越好 </summary>
        public bool? MetricLowerIsBetter { get; set; }

        /// <summary> 判断给定指标模式是否属于高级指标（SSIMU2/Butter3/GMSD） </summary>
        public static bool IsAdvancedMetricMode(string? metricMode) => MetricRegistry.IsAdvanced(metricMode);

        public static bool IsMetricLowerBetter(string? metricMode) => MetricRegistry.IsLowerBetter(metricMode);

        // 用户是否通过 -t 手动指定了 MaxJobs
        public bool UserSpecifiedMaxJobs { get; set; } = false;

        // 预缩放：长边最大像素数，0 或负数表示禁用
        public int MaxResolution { get; set; } = 0;

        // 是否将缩放应用于最终输出
        public bool ApplyScalingToOutput { get; set; } = true;

        // 是否递归遍历输入目录的子文件夹
        public bool RecurseSubdirectories { get; set; } = false;

        /// <summary>用户指定的输入文件扩展名（逗号分隔，如 ".jpg,.png"）。null 时使用全部默认 12 种格式。</summary>
        public string? InputExtensions { get; set; }

        /// <summary>默认支持的图片扩展名全集（12 种）</summary>
        public static readonly string[] DefaultInputExtensions =
            { ".jpg", ".jpeg", ".png", ".webp" };

        // 在 PresetConfig 类中添加
        public bool SerialEncode { get; set; } = false;

        // 在 PresetConfig 类中添加
        public bool UseProxySearch { get; set; } = false;   // 默认关闭

        /// <summary> 是否启用先验引导搜索（中位数初始化 + 动态哨兵探测） </summary>
        public bool UsePriorSearch { get; set; } = false;

        /// <summary> 是否开启遍历模式（对 MinCRF～MaxCRF 逐个编码并保存结果） </summary>
        public bool SweepMode { get; set; } = false;
        public bool DryRun { get; set; } = false;
        public bool Verbose { get; set; } = false;
        public bool Resume { get; set; } = false;



        /// <summary>
        /// 返回当前编码器实际有效的 AOM 参数字符串。
        /// 只有 libaom-av1 支持 aq-mode/deltaq-mode 等参数，其他编码器返回空字符串。
        /// </summary>
        public string GetEffectiveAomParams()
        {
            if (Av1EncoderFactory.Get(Encoder).SupportsAomParams)
                return AomParams;
            return "";
        }

        /// <summary>
        /// 根据当前 MetricMode 自动调整 TargetSSIM 的默认上限。
        /// 仅在用户未手动指定 -q 时调用。
        /// </summary>
        public void AdjustTargetForMetricMode()
        {
            if (Lossless || XpsnrTargetValue.HasValue || NativeTargetValue.HasValue) return;
            switch (MetricMode?.ToLower())
            {
                case "vmaf":
                    TargetSSIM = Math.Min(TargetSSIM, 0.98);
                    break;
                case "mix":
                    TargetSSIM = Math.Min(TargetSSIM, 0.95);
                    break;
            }
        }

        /// <summary>
        /// 根据当前的 MetricMode，将用户输入的原生质量值转换为内部 0‑1 目标。
        /// </summary>
        /// <summary>
        /// 根据当前的 MetricMode，将用户输入的原生质量值转换为内部 0‑1 目标。
        /// </summary>
        /// <summary>
        /// 根据当前的 MetricMode，将用户输入的原生质量值转换为内部 0‑1 目标。
        /// 对于高级指标（ssimu2/butter3/gmsd），直接存储原生值，不进行 clamp，以便任意输入。
        /// </summary>
        public void SetQualityTarget(double rawValue, string metricMode)
        {
            // 清除所有目标字段
            XpsnrTargetValue = null;
            XpsnrTargetChannel = null;
            Ssimu2TargetValue = null;
            Butteraugli3TargetValue = null;
            GmsdTargetValue = null;
            MetricLowerIsBetter = null;
            NativeTargetValue = null;
            TargetSSIM = 0;

            MetricMode = metricMode;

            // XPSNR 特殊处理
            if (metricMode?.StartsWith("xpsnr", StringComparison.OrdinalIgnoreCase) == true)
            {
                XpsnrTargetValue = rawValue;
                XpsnrTargetChannel = metricMode.Length > 5 ? metricMode.Substring(6).ToLower() : "w";
                return;
            }

            // 高级指标：使用独立原生字段
            if (MetricRegistry.IsAdvanced(metricMode))
            {
                MetricLowerIsBetter = IsMetricLowerBetter(metricMode);
                switch (metricMode)
                {
                    case "ssimu2":
                        Ssimu2TargetValue = rawValue;
                        break;
                    case "butter3":
                        Butteraugli3TargetValue = rawValue;
                        break;
                    case "gmsd":
                        GmsdTargetValue = rawValue;
                        break;
                }
                return;
            }

            // ★ 传统指标（VMAF/PSNR/SSIM/MSSSIM/mix）：直接存储原生值
            NativeTargetValue = rawValue;
        }

        /// <summary>
        /// 获取当前指标模式下的有效原生目标值。
        /// 优先返回 NativeTargetValue（用户显式指定），否则从 TargetSSIM 反算（预设路径）。
        /// </summary>
        public double GetEffectiveTarget()
        {
            string mode = MetricMode ?? "vmaf";

            if (NativeTargetValue.HasValue)
            {
                return NativeTargetValue.Value;
            }

            // 高级指标从独立字段取
            if (XpsnrTargetValue.HasValue)
            {
                return XpsnrTargetValue.Value;
            }
            if (Ssimu2TargetValue.HasValue)
            {
                return Ssimu2TargetValue.Value;
            }
            if (Butteraugli3TargetValue.HasValue)
            {
                return Butteraugli3TargetValue.Value;
            }
            if (GmsdTargetValue.HasValue)
            {
                return GmsdTargetValue.Value;
            }

            // 预设路径：从 TargetSSIM（0-1）反算原生值
            return TargetToRaw(TargetSSIM, mode);
        }

        /// <summary>
        /// 将内部 TargetSSIM (0-1) 反算为各指标的原生目标值。
        /// 仅用于预设路径（无 NativeTargetValue 时）。
        /// </summary>
        private static double TargetToRaw(double targetSSIM, string metricMode)
        {
            return metricMode?.ToLower() switch
            {
                "vmaf" => targetSSIM * 100.0,
                "psnr" => targetSSIM * 20.0 + 30,
                "ssim" or "msssim" or "mix" => targetSSIM,
                // 高级指标范围与 SSIM 0-1 尺度不兼容，必须通过 --target-{metric} 显式指定
                _ => throw new InvalidOperationException(
                    $"指标模式 '{metricMode}' 不支持从预设 TargetSSIM 自动换算。" +
                    $"请使用 --target-{metricMode?.ToLower()} <原生值> 显式指定目标。")
            };
        }


        /// <summary>验证配置参数是否合法，返回错误描述列表（空列表表示无错误）</summary>
        public System.Collections.Generic.List<string> Validate()
        {
            var errors = new System.Collections.Generic.List<string>();
            if (MinCRF > MaxCRF)
                errors.Add($"MinCRF ({MinCRF}) 不能大于 MaxCRF ({MaxCRF})");
            if (MinCRF < 0 || MinCRF > 63)
                errors.Add($"MinCRF ({MinCRF}) 超出有效范围 0-63");
            if (MaxCRF < 0 || MaxCRF > 63)
                errors.Add($"MaxCRF ({MaxCRF}) 超出有效范围 0-63");
            if (!UseCRFSearch && (BaseCRF < 0 || BaseCRF > 63))
                errors.Add($"BaseCRF ({BaseCRF}) 超出有效范围 0-63");
            if (BitDepth != 8 && BitDepth != 10)
                errors.Add($"BitDepth ({BitDepth}) 仅支持 8 或 10");
            if (string.IsNullOrWhiteSpace(Encoder))
                errors.Add("编码器名称不能为空");
            if (MaxJobs < 0)
                errors.Add($"MaxJobs ({MaxJobs}) 不能为负数");
            if (SearchCpuUsed < 0)
                errors.Add($"SearchCpuUsed ({SearchCpuUsed}) 不能为负数");
            if (FinalCpuUsed < 0)
                errors.Add($"FinalCpuUsed ({FinalCpuUsed}) 不能为负数");
            return errors;
        }

        // ========== 文件系统抽象（解决跨平台/长路径/可测试性）==========
        public interface IFileSystem
        {
            bool FileExists(string path);
            long GetFileLength(string path);
            void DeleteFile(string path);
            void CopyFile(string source, string dest, bool overwrite);
            void CreateDirectory(string path);
            void DeleteDirectory(string path, bool recursive);
            string[] GetFiles(string path, string searchPattern);
            DateTime GetCreationTime(string path);
            void AppendAllText(string path, string contents);
            void WriteAllText(string path, string contents, Encoding encoding);
            Task<string> ReadAllTextAsync(string path);
            IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption);
            bool DirectoryExists(string path);
            // ★ 新增：字节数组读写（用于 PNG 清洗）
            Task<byte[]> ReadAllBytesAsync(string path);
            Task WriteAllBytesAsync(string path, byte[] bytes);
            Task WriteAllTextAsync(string path, string contents);
        }

        public class RealFileSystem : IFileSystem
        {
            public bool FileExists(string path) => File.Exists(path);
            public long GetFileLength(string path) => new FileInfo(path).Length;
            public void DeleteFile(string path) => File.Delete(path);
            public void CopyFile(string source, string dest, bool overwrite) => File.Copy(source, dest, overwrite);
            public void CreateDirectory(string path) => Directory.CreateDirectory(path);
            public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);
            public string[] GetFiles(string path, string searchPattern) => Directory.GetFiles(path, searchPattern);
            public DateTime GetCreationTime(string path) => File.GetCreationTime(path);
            public void AppendAllText(string path, string contents) => File.AppendAllText(path, contents);
            public void WriteAllText(string path, string contents, Encoding encoding) => File.WriteAllText(path, contents, encoding);
            public async Task<string> ReadAllTextAsync(string path) => await File.ReadAllTextAsync(path);
            public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
                => Directory.EnumerateFiles(path, searchPattern, searchOption);
            public bool DirectoryExists(string path) => Directory.Exists(path);
            // ★ 新增字节数组读写实现
            public async Task<byte[]> ReadAllBytesAsync(string path) =>
                await File.ReadAllBytesAsync(path);

            public async Task WriteAllBytesAsync(string path, byte[] bytes) =>
                await File.WriteAllBytesAsync(path, bytes);
            public async Task WriteAllTextAsync(string path, string contents) =>
                await File.WriteAllTextAsync(path, contents, Encoding.UTF8);
        }

        public static PresetConfig CreateFromPreset(CliPreset preset)
        {
            return preset switch
            {
                CliPreset.Fast => new PresetConfig { BaseCRF = 38, TargetSSIM = 0.91, UseCRFSearch = false },
                CliPreset.Balanced => new PresetConfig { BaseCRF = 36, TargetSSIM = 0.97, UseCRFSearch = true },
                CliPreset.Best => new PresetConfig { BaseCRF = 34, TargetSSIM = 0.985, UseCRFSearch = true },
                CliPreset.Extreme => new PresetConfig { BaseCRF = 32, TargetSSIM = 0.99, UseCRFSearch = true },
                _ => throw new ArgumentOutOfRangeException(nameof(preset))
            };
        }
    }
}
