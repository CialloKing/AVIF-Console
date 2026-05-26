using System;
using System.Drawing;
using System.IO;
using System.Text.Json;

namespace AvifEncoder
{
    public class AppConfig
    {
        public string FontFamily { get; set; } = "Segoe UI";
        public float FontSize { get; set; } = 9f;
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public int WindowLeft { get; set; }
        public int WindowTop { get; set; }
        public bool Maximized { get; set; }
    }

    public static class AppConfigHelper
    {
        public static AppConfig? LoadFromFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
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
                Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
    }
}