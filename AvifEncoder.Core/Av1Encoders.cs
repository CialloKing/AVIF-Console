namespace AvifEncoder
{
    public interface IAv1Encoder
    {
        string Name { get; }
        bool SupportsLossless { get; }
        bool SupportsStillPicture { get; }
        bool SupportsTiles { get; }
        bool SupportsRowMt { get; }
        int MinSpeed { get; }
        int MaxSpeed { get; }
        string BuildSpeedArg(int cpuUsed);
        string BuildLosslessArg();
        string BuildTuneArg(string? metricMode);
    }

    internal sealed class LibAomEncoder : IAv1Encoder
    {
        public string Name => "libaom-av1";
        public bool SupportsLossless => true;
        public bool SupportsStillPicture => true;
        public bool SupportsTiles => true;
        public bool SupportsRowMt => true;
        public int MinSpeed => 0;
        public int MaxSpeed => 8;

        public string BuildSpeedArg(int cpuUsed)
        {
            return $"-cpu-used {cpuUsed}";
        }

        public string BuildLosslessArg()
        {
            return "-lossless 1";
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
    }

    internal sealed class SvtAv1Encoder : IAv1Encoder
    {
        public string Name => "libsvtav1";
        public bool SupportsLossless => true;
        public bool SupportsStillPicture => false;
        public bool SupportsTiles => true;
        public bool SupportsRowMt => false;
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

        public string BuildTuneArg(string? metricMode)
        {
            string tune = metricMode switch
            {
                "ssim" or "msssim" => "2",
                "psnr" => "1",
                "vmaf" or "xpsnr" or "ssimu2" or "butter3" or "gmsd" => "3",
                _ => "3"
            };
            return tune;
        }
    }

    internal sealed class Rav1eEncoder : IAv1Encoder
    {
        public string Name => "librav1e";
        public bool SupportsLossless => true;
        public bool SupportsStillPicture => false;
        public bool SupportsTiles => false;
        public bool SupportsRowMt => false;
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

        public string BuildTuneArg(string? metricMode)
        {
            return metricMode switch
            {
                "ssim" or "vmaf" or "butter3" => "psychovisual",
                _ => ""
            };
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
        public HardwareAv1Encoder(string name) => _name = name;
        public string Name => _name;
        public bool SupportsLossless => false;
        public bool SupportsStillPicture => false;
        public bool SupportsTiles => false;
        public bool SupportsRowMt => false;
        public int MinSpeed => 0;
        public int MaxSpeed => 0;

        public string BuildSpeedArg(int cpuUsed)
        {
            return "";
        }

        public string BuildLosslessArg()
        {
            return "";
        }

        public string BuildTuneArg(string? metricMode)
        {
            return "";
        }
    }
}
