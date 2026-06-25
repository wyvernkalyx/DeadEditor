using System.IO;
using Newtonsoft.Json;

namespace DeadEditor.Models
{
    public class LibrarySettings
    {
        public string LibraryRootPath { get; set; } = "";              // Single library root — all albums stored under {Artist}/{AlbumFolder}/
        public string FpcalcPath { get; set; } = "";                   // Path to fpcalc.exe (Chromaprint) for audio fingerprinting
        public string PrimaryArtistName { get; set; } = "Grateful Dead"; // Default artist for imports and new songs when none is otherwise set
        public string? LastBoxSetName { get; set; }                    // Remember last box set name for faster imports
        public bool DismissedFpcalcWarning { get; set; } = false;      // User has dismissed the fpcalc.exe startup warning
        public int VolumePercent { get; set; } = 75;                     // Volume slider 0-100, persisted between sessions

        // Playlist persistence
        public List<string> SavedPlaylistPaths { get; set; } = new();  // File paths of tracks in saved playlist

        // Window positions
        public double? MainWindowLeft { get; set; }
        public double? MainWindowTop { get; set; }
        public double? MainWindowWidth { get; set; }
        public double? MainWindowHeight { get; set; }

        public double? LibraryWindowLeft { get; set; }
        public double? LibraryWindowTop { get; set; }
        public double? LibraryWindowWidth { get; set; }
        public double? LibraryWindowHeight { get; set; }

        // PlayerWindow position (free-floating, no docking)
        public double? PlayerWindowLeft { get; set; }
        public double? PlayerWindowTop { get; set; }

        // PlaylistWindow position (follows PlayerWindow by default, can detach)
        public double? PlaylistWindowLeft { get; set; }
        public double? PlaylistWindowTop { get; set; }

        // VisWindow position (follows PlaylistWindow by default, can detach)
        public double? VisWindowLeft { get; set; }
        public double? VisWindowTop { get; set; }

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
