using DeadEditor.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DeadEditor.Services
{
    public class NormalizationService
    {
        private SongDatabase? _database;
        private Dictionary<string, string> _aliasLookup = new();

        public NormalizationService()
        {
            LoadDatabase();
        }

        private void LoadDatabase()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "songs.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                _database = JsonConvert.DeserializeObject<SongDatabase>(json);
            }
            else
            {
                _database = new SongDatabase { Songs = new List<SongEntry>() };
            }

            // Build lookup dictionary
            _aliasLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (_database?.Songs != null)
            {
                foreach (var song in _database.Songs)
                {
                    // Add official title as its own lookup
                    _aliasLookup[song.OfficialTitle] = song.OfficialTitle;

                    // Add all aliases
                    foreach (var alias in song.Aliases ?? new List<string>())
                    {
                        _aliasLookup[alias] = song.OfficialTitle;
                    }
                }
            }
        }

        /// <summary>
        /// Attempts to normalize a single song title
        /// </summary>
        public string? Normalize(string title)
        {
            if (string.IsNullOrEmpty(title)) return null;

            // Clean the title first
            var cleaned = title.Trim();

            // Direct lookup
            if (_aliasLookup.TryGetValue(cleaned, out var official))
            {
                return official;
            }

            // Try without common suffixes
            var withoutSuffix = cleaned
                .Replace(" (1)", "")
                .Replace(" (2)", "")
                .Replace(" Reprise", "")
                .Replace(" reprise", "")
                .Trim();

            if (_aliasLookup.TryGetValue(withoutSuffix, out official))
            {
                return official;
            }

            return null; // No match found
        }

        /// <summary>
        /// Normalizes all tracks, returns count of successful matches
        /// </summary>
        public int NormalizeAll(List<TrackInfo> tracks)
        {
            int matched = 0;
            foreach (var track in tracks)
            {
                var normalized = Normalize(track.Title);
                if (normalized != null)
                {
                    track.NormalizedTitle = normalized;
                    matched++;
                }
            }
            return matched;
        }

        /// <summary>
        /// Gets all known song titles for autocomplete
        /// </summary>
        public List<string> GetAllTitles()
        {
            return _database?.Songs?.Select(s => s.OfficialTitle).OrderBy(t => t).ToList() ?? new List<string>();
        }

        /// <summary>
        /// Adds a new song to the database
        /// </summary>
        public void AddSong(string officialTitle, List<string>? aliases = null)
        {
            if (_database?.Songs == null) return;

            if (_database.Songs.Any(s => s.OfficialTitle.Equals(officialTitle, StringComparison.OrdinalIgnoreCase)))
                return;

            _database.Songs.Add(new SongEntry
            {
                OfficialTitle = officialTitle,
                Aliases = aliases ?? new List<string>()
            });

            SaveDatabase();
            LoadDatabase(); // Reload to update lookup
        }

        private void SaveDatabase()
        {
            if (_database == null) return;

            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "songs.json");
            var json = JsonConvert.SerializeObject(_database, Formatting.Indented);
            File.WriteAllText(path, json);
        }
    }
}
