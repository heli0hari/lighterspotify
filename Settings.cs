using System;
using System.IO;
using System.Text.Json;

namespace LighterSpotify
{
    public class Settings
    {
        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LighterSpotify"); }
        }

        static string FilePath
        {
            get { return Path.Combine(Folder, "settings.json"); }
        }

        public string ClientId { get; set; } = "";
        public string LibrespotPath { get; set; } = "";
        public string DeviceName { get; set; } = "LighterSpotify";
        public int Bitrate { get; set; } = 160;
        public bool UseMediaKeys { get; set; } = true;
        public bool AlwaysOnTop { get; set; } = false;
        public bool HasWindowPosition { get; set; } = false;
        public double WindowLeft { get; set; }
        public double WindowTop { get; set; }

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    Settings loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
                    if (loaded != null) return loaded;
                }
            }
            catch { }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
