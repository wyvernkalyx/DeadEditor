using System.IO;
using Newtonsoft.Json;

namespace DeadEditor.Models
{
    public class LibrarySettings
    {
        public string LibraryRootPath { get; set; } = "";              // Path to audience recordings (also used for box sets)
        public string OfficialReleasesPath { get; set; } = "";         // Path to official releases
        public string PrimaryArtistName { get; set; } = "Grateful Dead"; // Primary artist for MusicBrainz filtering
        public string? LastBoxSetName { get; set; }                    // Remember last box set name for faster imports

        // Window positions
        public double? MainWindowLeft { get; set; }
        public double? MainWindowTop { get; set; }
        public double? MainWindowWidth { get; set; }
        public double? MainWindowHeight { get; set; }

        public double? LibraryWindowLeft { get; set; }
        public double? LibraryWindowTop { get; set; }
        public double? LibraryWindowWidth { get; set; }
        public double? LibraryWindowHeight { get; set; }

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
