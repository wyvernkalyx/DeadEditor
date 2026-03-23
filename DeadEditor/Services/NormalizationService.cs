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
                _database = new SongDatabase { Songs = new List<SongEntry>(), Artists = new List<ArtistEntry>() };
            }

            // Build lookup dictionary
            _aliasLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Load from new artist-based structure first
            if (_database?.Artists != null)
            {
                foreach (var artist in _database.Artists)
                {
                    foreach (var song in artist.Songs ?? new List<SongEntry>())
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

            // Load from legacy Songs structure (backward compatibility)
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

            // Normalize apostrophes (convert curly/typographic apostrophes to regular ones)
            cleaned = cleaned
                .Replace("'", "'")  // Curly apostrophe to straight
                .Replace("'", "'")  // Another variant
                .Replace("`", "'"); // Backtick to apostrophe

            // Strip common metadata patterns before matching
            // This ensures we clean the title thoroughly before any lookups

            // Remove (Filler: yyyy-MM-dd - Venue, City, State) pattern
            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"\s*\(Filler:\s*\d{4}-\d{2}-\d{2}\s*-\s*[^)]+\)\s*$",
                "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

            // Normalize slash-formatted dates: (M/d/yy Venue) → (yyyy-MM-dd)
            cleaned = NormalizeDateInTitle(cleaned);

            // Remove venue suffix from dash-formatted dates: (yyyy-MM-dd - Location) → (yyyy-MM-dd)
            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"\s*\((\d{4}-\d{2}-\d{2})\s*-\s*[^)]+\)\s*$",
                " ($1)").Trim();

            // Remove (YYYY Remaster), (YYYY Remastered), (Remaster), (Remastered) patterns
            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"\s*\((?:\d{4}\s+)?Remastere?d?\)\s*$",
                "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

            // Remove [YYYY Remaster], [Remaster] patterns
            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"\s*\[(?:\d{4}\s+)?Remastere?d?\]\s*$",
                "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

            // Remove (Reprise) or [Reprise] editorial suffixes
            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"\s*[\(\[]Reprise[\)\]]\s*$",
                "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

            // Remove (Live) or [Live] standalone editorial markers (NOT "Live at/in" patterns)
            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"\s*[\(\[]Live[\)\]]\s*$",
                "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

            // Remove [M/D/YY, Venue pattern
            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"\s*\[\d{1,2}/\d{1,2}/\d{2,4}[,\s].*$",
                "").Trim();

            // Remove artist suffix (e.g., " - Grateful Dead__", " - Artist Name")
            // MUST come BEFORE "(Live at...)" removal because MusicBrainz titles have format:
            // "Song (Live at Venue) - Artist__" where artist suffix prevents (Live...) regex from matching
            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"\s*-\s*[^-]+_{0,2}\s*$",
                "").Trim();

            // Remove [Live in...] or [Live at...] patterns (square brackets)
            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"\s*\[Live (?:at|in) [^\]]+\]\s*$",
                "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

            // Remove (Live at...) or (Live in...) patterns (parentheses)
            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"\s*\(Live (?:at|in) [^)]+\)\s*$",
                "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

            // Remove [Venue, Location M/D/YY] or [Kiel Opera House, St. Louis, MO 10/24/70] patterns
            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"\s*\[[^\]]*\d{1,2}/\d{1,2}/\d{2,4}\]\s*$",
                "").Trim();

            // Remove segue markers at the end: >, ->, →, [>]
            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"\s*(\[?>?\]?|[-–]?\s*>\s*)\s*$",
                "").Trim();

            // Direct lookup after all cleaning
            if (_aliasLookup.TryGetValue(cleaned, out var official))
            {
                return official;
            }

            // Try stripping date/venue info for matching (but don't change the actual title)
            // Pattern: "Song [M/D/YY, Venue" or "[MM/DD/YYYY, Venue" (for bonus tracks from different dates)
            var withoutDateVenue = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"\s*\[\d{1,2}/\d{1,2}/\d{2,4}[,\s].*$",
                "").Trim();

            if (withoutDateVenue != cleaned && _aliasLookup.TryGetValue(withoutDateVenue, out official))
            {
                return official;
            }

            // Try stripping [Live in...] or [Live at...] patterns for matching
            var withoutLiveInfo = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"\s*\[Live (?:at|in) [^\]]+\]\s*$",
                "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

            if (withoutLiveInfo != cleaned && _aliasLookup.TryGetValue(withoutLiveInfo, out official))
            {
                return official;
            }

            // Try stripping (yyyy-MM-dd - Location) pattern AND segue markers for matching
            var withoutDateLocation = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"\s*\(\d{4}-\d{2}-\d{2}\s*-\s*[^)]+\)\s*$",
                "").Trim();
            // Also remove segue marker after stripping date
            withoutDateLocation = System.Text.RegularExpressions.Regex.Replace(
                withoutDateLocation,
                @"\s*(\[?>?\]?|[-–]?\s*>\s*)\s*$",
                "").Trim();

            if (withoutDateLocation != cleaned && _aliasLookup.TryGetValue(withoutDateLocation, out official))
            {
                return official;
            }

            // Try stripping (yyyy-MM-dd) pattern AND segue markers for matching
            var withoutDateOnly = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"\s*\(\d{4}-\d{2}-\d{2}\)\s*$",
                "").Trim();
            // Also remove segue marker after stripping date
            withoutDateOnly = System.Text.RegularExpressions.Regex.Replace(
                withoutDateOnly,
                @"\s*(\[?>?\]?|[-–]?\s*>\s*)\s*$",
                "").Trim();

            if (withoutDateOnly != cleaned && _aliasLookup.TryGetValue(withoutDateOnly, out official))
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
                // First, normalize any slash-formatted dates in the title
                // This ensures dates like "(1971/07/02 Filmore West)" become "(1971-07-02)"
                var dateNormalizedTitle = NormalizeDateInTitle(track.Title);
                if (dateNormalizedTitle != track.Title)
                {
                    track.Title = dateNormalizedTitle;  // Update the title with normalized date
                }

                // Then normalize the song name for matching
                var normalized = Normalize(track.Title);
                if (normalized != null)
                {
                    track.SongName = normalized;
                    track.IsMatched = true;  // Mark as matched for UI highlighting
                    matched++;
                }
                else
                {
                    track.IsMatched = false;  // Mark as unmatched for UI highlighting (gold color)
                }
            }
            return matched;
        }

        /// <summary>
        /// Gets all known song titles for autocomplete
        /// </summary>
        public List<string> GetAllTitles()
        {
            var titles = new List<string>();

            // Get titles from new artist-based structure
            if (_database?.Artists != null)
            {
                foreach (var artist in _database.Artists)
                {
                    titles.AddRange(artist.Songs?.Select(s => s.OfficialTitle) ?? new List<string>());
                }
            }

            // Get titles from legacy structure
            if (_database?.Songs != null)
            {
                titles.AddRange(_database.Songs.Select(s => s.OfficialTitle));
            }

            return titles.Distinct().OrderBy(t => t).ToList();
        }

        /// <summary>
        /// Gets all artist names
        /// </summary>
        public List<string> GetAllArtists()
        {
            return _database?.Artists?.Select(a => a.Name).OrderBy(n => n).ToList() ?? new List<string>();
        }

        /// <summary>
        /// Adds a new song to the database (legacy method - uses first artist or creates one)
        /// </summary>
        public void AddSong(string officialTitle, List<string>? aliases = null)
        {
            AddSong(officialTitle, aliases, "Grateful Dead");
        }

        /// <summary>
        /// Adds a new song to the database for a specific artist
        /// </summary>
        public void AddSong(string officialTitle, List<string>? aliases, string artistName)
        {
            if (_database == null) return;

            // Ensure Artists list exists
            if (_database.Artists == null)
            {
                _database.Artists = new List<ArtistEntry>();
            }

            // Find or create artist
            var artist = _database.Artists.FirstOrDefault(a => a.Name.Equals(artistName, StringComparison.OrdinalIgnoreCase));
            if (artist == null)
            {
                artist = new ArtistEntry
                {
                    Name = artistName,
                    Songs = new List<SongEntry>()
                };
                _database.Artists.Add(artist);
            }

            // Check if song already exists for this artist
            if (artist.Songs.Any(s => s.OfficialTitle.Equals(officialTitle, StringComparison.OrdinalIgnoreCase)))
                return;

            // Add song
            artist.Songs.Add(new SongEntry
            {
                OfficialTitle = officialTitle,
                Aliases = aliases ?? new List<string>()
            });

            SaveDatabase();
            LoadDatabase(); // Reload to update lookup
        }

        /// <summary>
        /// Normalize slash-formatted dates in parenthetical suffixes to yyyy-MM-dd format
        /// </summary>
        public string NormalizeDateInTitle(string title)
        {
            // Match pattern: (M/d/yy Venue) or (yyyy/MM/dd Venue) or (MM/DD/YYYY Venue)
            var match = System.Text.RegularExpressions.Regex.Match(
                title,
                @"\s*\((\d{1,2}/\d{1,2}/\d{2,4})(?:\s+[^)]+)?\)\s*$");

            if (!match.Success)
                return title;  // No slash date found, return unchanged

            try
            {
                // Extract the date string (without venue)
                var dateString = match.Groups[1].Value;
                var parts = dateString.Split('/');

                if (parts.Length != 3)
                    return title;  // Invalid format

                int year, month, day;

                // Detect format: yyyy/MM/dd vs M/d/yy
                if (parts[0].Length == 4)
                {
                    // yyyy/MM/dd format
                    year = int.Parse(parts[0]);
                    month = int.Parse(parts[1]);
                    day = int.Parse(parts[2]);
                }
                else
                {
                    // M/d/yy or MM/DD/YYYY format
                    month = int.Parse(parts[0]);
                    day = int.Parse(parts[1]);
                    year = int.Parse(parts[2]);

                    // Handle 2-digit years (70-99 → 1970-1999, 00-69 → 2000-2069)
                    if (year < 100)
                    {
                        year += (year >= 70) ? 1900 : 2000;
                    }
                }

                // Validate date components
                if (month < 1 || month > 12 || day < 1 || day > 31 || year < 1900 || year > 2100)
                    return title;  // Invalid date, return unchanged

                // Extract base title (everything before the date parentheses)
                var baseTitle = title.Substring(0, match.Index).Trim();

                // Reconstruct with normalized date
                return $"{baseTitle} ({year:D4}-{month:D2}-{day:D2})";
            }
            catch
            {
                // Any parsing error, return original
                return title;
            }
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
