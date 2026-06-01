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

        // ===== 选项页 =====
        public string? EncodeExtensions { get; set; }
        public int EncodeTimeoutEncode { get; set; }
        public int EncodeTimeoutSearch { get; set; }
        public int EncodeTimeoutSafe { get; set; }
        public int EncodeTimeoutSsim { get; set; }
        public bool EncodeDryRun { get; set; }
        public bool EncodeVerbose { get; set; }
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
            // 先写入临时文件再原子替换，防止断电/崩溃导致配置文件损坏
            string tmpPath = path + ".tmp";
            File.WriteAllText(tmpPath, json);
            try
            {
                File.Move(tmpPath, path, overwrite: true);
            }
            catch (IOException)
            {
                // 跨卷移动失败时退化为拷贝+删除
                File.Copy(tmpPath, path, overwrite: true);
                File.Delete(tmpPath);
            }
        }
    }
}