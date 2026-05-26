using System;
using System.Drawing;
using System.IO;
using System.Text.Json;

namespace AvifEncoder
{
    public class AppConfig
    {
        // ===== 字体 =====
        public string FontFamily { get; set; } = "Segoe UI";
        public float FontSize { get; set; } = 9f;

        // ===== 窗口状态 =====
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public int WindowLeft { get; set; }
        public int WindowTop { get; set; }
        public bool Maximized { get; set; }

        // ===== 路径 =====
        public string? EncodeInput { get; set; }
        public string? EncodeOutput { get; set; }

        // ===== 编码设置 =====

        // ===== 编码设置 =====
        public string? EncodePreset { get; set; }
        public string? EncodeEncoder { get; set; }
        public int EncodeJobs { get; set; }
        public int EncodeSearchCpuUsed { get; set; }
        public int EncodeFinalCpuUsed { get; set; }
        public string? EncodeTemplate { get; set; }
        public bool EncodeSearch { get; set; }
        public bool EncodeCrfRangeMode { get; set; }
        public int EncodeCrfFix { get; set; }
        public int EncodeCrfMin { get; set; }
        public int EncodeCrfMax { get; set; }
        public string? EncodeMetric { get; set; }
        public string? EncodeQualityMode { get; set; }
        public double EncodeQualityValue { get; set; }
        public string? EncodeChroma { get; set; }
        public string? EncodeBitDepth { get; set; }
        public bool EncodeLossless { get; set; }
        public bool EncodeRecursive { get; set; }
        public int EncodeMaxRes { get; set; }
        public bool EncodeOutputFullRes { get; set; }
        public int EncodeConflict { get; set; }
        public bool EncodeSerialEncode { get; set; }
        public bool EncodePriorSearch { get; set; }
        public bool EncodeProxy { get; set; }
        public bool EncodeSweep { get; set; }
    }

    public static class AppConfigHelper
    {
        public static AppConfig? LoadFromFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppConfig>(json);
            }
            catch
            {
                return null;
            }
        }

        public static void SaveToFile(AppConfig config, string path)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            string json = JsonSerializer.Serialize(
                config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
    }
}