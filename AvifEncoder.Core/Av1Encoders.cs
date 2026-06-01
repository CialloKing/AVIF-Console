namespace AvifEncoder
{
    public interface IAv1Encoder
    {
        string Name { get; }
        bool SupportsLossless { get; }
        bool SupportsStillPicture { get; }
        bool SupportsTiles { get; }
        bool SupportsRowMt { get; }
        bool SupportsAomParams { get; }
        int MinSpeed { get; }
        int MaxSpeed { get; }
        string BuildSpeedArg(int cpuUsed);
        string BuildLosslessArg();
        string BuildQualityArg(int crf);
        string BuildTuneArg(string? metricMode);
        string BuildFullTuneArg(string? metricMode);
    }

    internal sealed class LibAomEncoder : IAv1Encoder
    {
        public string Name => "libaom-av1";
        public bool SupportsLossless => true;
        public bool SupportsStillPicture => true;
        public bool SupportsTiles => true;
        public bool SupportsRowMt => true;
        public bool SupportsAomParams => true;
        public int MinSpeed => 0;
        public int MaxSpeed => 8;

        public string BuildSpeedArg(int cpuUsed)
        {
            return $"-cpu-used {cpuUsed}";
        }

        public string BuildLosslessArg()
        {
            // ffmpeg -lossless 1 在某些构建中不被 libaom 编码器识别（被当作解码器选项忽略），
            // -crf 0 在 libaom 中等价于数学无损，兼容性更好
            return "-crf 0";
        }

        public string BuildQualityArg(int crf)
        {
            return $"-crf {crf}";
        }

        public string BuildTuneArg(string? metricMode)
        {
            return metricMode switch
            {
                "ssim" => "tune=ssim",
                "psnr" => "tune=psnr",
                _ => ""
            };
        }

        public string BuildFullTuneArg(string? metricMode)
        {
            string tune = BuildTuneArg(metricMode);
            return tune.Length > 0 ? $"-aom-params {tune}" : "";
        }
    }

    internal sealed class SvtAv1Encoder : IAv1Encoder
    {
        public string Name => "libsvtav1";
        public bool SupportsLossless => true;
        public bool SupportsStillPicture => false;
        public bool SupportsTiles => true;
        public bool SupportsRowMt => false;
        public bool SupportsAomParams => false;
        public int MinSpeed => 0;
        public int MaxSpeed => 13;

        public string BuildSpeedArg(int cpuUsed)
        {
            int svtPreset = Math.Clamp(MaxSpeed - cpuUsed, 0, MaxSpeed);
            return $"-preset {svtPreset}";
        }

        public string BuildLosslessArg()
        {
            return "-svtav1-params lossless=1";
        }

        public string BuildQualityArg(int crf)
        {
            return $"-crf {crf}";
        }

        public string BuildTuneArg(string? metricMode)
        {
            return metricMode switch
            {
                "ssim" or "msssim" => "2",
                "psnr" => "1",
                "vmaf" or "xpsnr" or "ssimu2" or "butter3" or "gmsd" => "3",
                _ => "3"
            };
        }

        public string BuildFullTuneArg(string? metricMode)
        {
            string tune = BuildTuneArg(metricMode);
            return $"-svtav1-params \"tune={tune}:keyint=1:avif=1:film-grain=0:enable-qm=1:qm-min=0:qm-max=8\"";
        }
    }

    internal sealed class Rav1eEncoder : IAv1Encoder
    {
        public string Name => "librav1e";
        public bool SupportsLossless => true;
        public bool SupportsStillPicture => false;
        public bool SupportsTiles => false;
        public bool SupportsRowMt => false;
        public bool SupportsAomParams => false;
        public int MinSpeed => 0;
        public int MaxSpeed => 10;

        public string BuildSpeedArg(int cpuUsed)
        {
            return $"-speed {cpuUsed}";
        }

        public string BuildLosslessArg()
        {
            return "-rav1e-params lossless=1";
        }

        public string BuildQualityArg(int crf)
        {
            return $"-crf {crf}";
        }

        public string BuildTuneArg(string? metricMode)
        {
            return metricMode switch
            {
                "ssim" or "vmaf" or "butter3" => "psychovisual",
                _ => ""
            };
        }

        public string BuildFullTuneArg(string? metricMode)
        {
            string tune = BuildTuneArg(metricMode);
            // ffmpeg 中 librav1e 使用 -rav1e-params 传递参数，而非独立 CLI 的 --tune
            return tune.Length > 0 ? $"-rav1e-params tune={tune}" : "";
        }
    }

    public static class Av1EncoderFactory
    {
        private static readonly Dictionary<string, IAv1Encoder> Cache = new()
        {
            ["libaom-av1"] = new LibAomEncoder(),
            ["libsvtav1"] = new SvtAv1Encoder(),
            ["librav1e"] = new Rav1eEncoder(),
        };

        public static IAv1Encoder Get(string encoderName)
        {
            if (Cache.TryGetValue(encoderName, out var encoder))
            {
                return encoder;
            }
            return new HardwareAv1Encoder(encoderName);
        }
    }

    internal sealed class HardwareAv1Encoder : IAv1Encoder
    {
        private readonly string _name;
        private readonly bool _isNvenc;

        public HardwareAv1Encoder(string name)
        {
            _name = name;
            _isNvenc = name.Contains("nvenc", StringComparison.OrdinalIgnoreCase);
        }

        public string Name => _name;
        public bool SupportsLossless => false;
        public bool SupportsStillPicture => _isNvenc;
        public bool SupportsTiles => false;
        public bool SupportsRowMt => false;
        public bool SupportsAomParams => false;

        // NVENC preset: p1(最快) ~ p7(最慢/最高质量)
        // cpu-used: 0(慢/高质量) ~ 7(快)，需要反转映射
        public int MinSpeed => _isNvenc ? 0 : 0;
        public int MaxSpeed => _isNvenc ? 7 : 0;

        public string BuildSpeedArg(int cpuUsed)
        {
            if (!_isNvenc) return "";
            int preset = Math.Max(1, 7 - Math.Clamp(cpuUsed, 0, 7));  // cpu=0→p7, cpu=7→p1
            return $"-preset p{preset}";
        }

        public string BuildLosslessArg()
        {
            return "";
        }

        public string BuildQualityArg(int crf)
        {
            string lower = _name.ToLower();
            if (lower.Contains("nvenc"))
            {
                return $"-cq {crf}";
            }
            if (lower.Contains("qsv") || lower.Contains("vaapi"))
            {
                return $"-global_quality {crf}";
            }
            if (lower.Contains("amf"))
            {
                return $"-qp_i {crf} -qp_p {crf}";
            }
            // 兜底：仍用 -crf
            return $"-crf {crf}";
        }

        public string BuildTuneArg(string? metricMode)
        {
            if (!_isNvenc) return "";
            return metricMode switch
            {
                "vmaf" or "psnr" or "ssim" or "msssim" => "hq",
                _ => ""
            };
        }

        public string BuildFullTuneArg(string? metricMode)
        {
            if (!_isNvenc) return "";
            string tune = BuildTuneArg(metricMode);
            return tune.Length > 0 ? $"-tune {tune}" : "";
        }
    }
}
