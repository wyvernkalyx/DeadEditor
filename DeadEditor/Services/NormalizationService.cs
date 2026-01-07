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

            // Clean the title first - remove tape markers and trim
            var cleaned = title
                .Replace("//", "")  // Remove tape change/splice markers
                .Trim();

            // Direct lookup
            if (_aliasLookup.TryGetValue(cleaned, out var official))
            {
                return official;
            }

            // Try normalizing different dash characters (hyphen, en-dash, em-dash, box-drawing)
            var normalizedDashes = cleaned
                .Replace("–", "-")  // en-dash to hyphen
                .Replace("—", "-")  // em-dash to hyphen
                .Replace("−", "-")  // minus sign to hyphen
                .Replace("─", "-"); // box-drawing horizontal to hyphen

            if (_aliasLookup.TryGetValue(normalizedDashes, out official))
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

            // Try normalized dashes without suffixes
            var normalizedWithoutSuffix = normalizedDashes
                .Replace(" (1)", "")
                .Replace(" (2)", "")
                .Replace(" Reprise", "")
                .Replace(" reprise", "")
                .Trim();

            if (_aliasLookup.TryGetValue(normalizedWithoutSuffix, out official))
            {
                return official;
            }

            // Try fuzzy matching as last resort (for typos)
            var fuzzyMatch = FindFuzzyMatch(cleaned);
            if (fuzzyMatch != null)
            {
                return fuzzyMatch;
            }

            return null; // No match found
        }

        /// <summary>
        /// Finds a fuzzy match using Levenshtein distance
        /// Only matches if distance is small relative to string length
        /// </summary>
        private string? FindFuzzyMatch(string input)
        {
            if (string.IsNullOrEmpty(input) || _database?.Songs == null) return null;

            int bestDistance = int.MaxValue;
            string? bestMatch = null;

            // Check against all official titles and aliases
            foreach (var kvp in _aliasLookup)
            {
                var distance = LevenshteinDistance(input, kvp.Key);

                // Only consider it a match if:
                // 1. Distance is less than 3 (max 2 typos)
                // 2. Distance is less than 20% of the string length
                var maxAllowedDistance = Math.Min(2, (int)(kvp.Key.Length * 0.2));

                if (distance <= maxAllowedDistance && distance < bestDistance)
                {
                    bestDistance = distance;
                    bestMatch = kvp.Value;
                }
            }

            return bestMatch;
        }

        /// <summary>
        /// Calculates Levenshtein distance between two strings (case-insensitive)
        /// </summary>
        private int LevenshteinDistance(string s1, string s2)
        {
            s1 = s1.ToLowerInvariant();
            s2 = s2.ToLowerInvariant();

            int n = s1.Length;
            int m = s2.Length;
            int[,] d = new int[n + 1, m + 1];

            if (n == 0) return m;
            if (m == 0) return n;

            for (int i = 0; i <= n; i++)
                d[i, 0] = i;
            for (int j = 0; j <= m; j++)
                d[0, j] = j;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (s2[j - 1] == s1[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[n, m];
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
