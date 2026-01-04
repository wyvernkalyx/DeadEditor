using System.IO;
using Newtonsoft.Json;

namespace DeadEditor.Models
{
    public class LibrarySettings
    {
        public string LibraryRootPath { get; set; } = "";

        private static readonly string SettingsPath = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "DeadEditor",
            "settings.json");

        public static LibrarySettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonConvert.DeserializeObject<LibrarySettings>(json) ?? new LibrarySettings();
                }
            }
            catch
            {
                // If loading fails, return default settings
            }

            return new LibrarySettings();
        }

        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsPath);
                if (directory != null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // Silently fail if we can't save settings
            }
        }
    }
}
