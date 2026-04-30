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
                _database = new SongDatabase { Artists = new List<ArtistEntry>() };
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

        }

        /// <summary>
        /// Attempts to normalize a single song title to a canonical OfficialTitle.
        /// <para>
        /// As of commit Y2K-2d, the legacy 13-stage strip stack (S1-S13) is replaced by a
        /// call to <see cref="TitleStructureParser.Parse"/>. The parser handles cosmetic
        /// prep (tape markers, apostrophes), artist-suffix stripping, paren/bracket
        /// classification, segue stripping, dash normalization, and whitespace collapse.
        /// The L1-L9 alias-lookup stack runs on the parser's <c>SongName</c> output. See
        /// <c>documentation/title-structure-parser-spec.md</c> and
        /// <c>documentation/12-normalization-service.md</c>.
        /// </para>
        /// </summary>
        /// <param name="title">Raw track title from ID3 tag, file name, or external source.</param>
        /// <param name="albumDate">Optional album date (yyyy-MM-dd or yyyy form). Forwarded to the parser
        /// for two-digit-year resolution. See <see cref="TwoDigitYearResolver"/>.</param>
        public string? Normalize(string title, string? albumDate = null)
        {
            if (string.IsNullOrEmpty(title)) return null;

            // Y2K-2d: replace S1-S13 strip stack with parser delegation. Cosmetic prep,
            // metadata-paren stripping, segue handling, dash normalization, and whitespace
            // collapse all happen inside Parse(). The parser preserves canonical parens
            // (e.g. "Ain't It Crazy (The Rub)") that the position-based strip stack could
            // not distinguish from metadata.
            var parsed = TitleStructureParser.Parse(title, albumDate);
            var cleaned = parsed.SongName;
            if (string.IsNullOrEmpty(cleaned)) return null;

            // L1: direct alias-table lookup.
            if (_aliasLookup.TryGetValue(cleaned, out var official))
            {
                return official;
            }

            // L2: REDUNDANT — superseded by TitleStructureParser. Slated for deletion in next cleanup commit.
            //     See documentation/title-structure-parser-spec.md.
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

            // L3: REDUNDANT — superseded by TitleStructureParser. Slated for deletion in next cleanup commit.
            //     See documentation/title-structure-parser-spec.md.
            // Try stripping [Live in...] or [Live at...] patterns for matching
            var withoutLiveInfo = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"\s*\[Live (?:at|in) [^\]]+\]\s*$",
                "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

            if (withoutLiveInfo != cleaned && _aliasLookup.TryGetValue(withoutLiveInfo, out official))
            {
                return official;
            }

            // L4: REDUNDANT — superseded by TitleStructureParser. Slated for deletion in next cleanup commit.
            //     See documentation/title-structure-parser-spec.md.
            // Try stripping (yyyy-MM-dd - Location) pattern AND segue markers for matching
            var withoutDateLocation = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"\s*\(\d{4}-\d{2}-\d{2}\s*-\s*[^)]+\)\s*$",
                "").Trim();
            withoutDateLocation = System.Text.RegularExpressions.Regex.Replace(
                withoutDateLocation,
                @"\s*(\[?>?\]?|[-–]?\s*>\s*)\s*$",
                "").Trim();

            if (withoutDateLocation != cleaned && _aliasLookup.TryGetValue(withoutDateLocation, out official))
            {
                return official;
            }

            // L5: REDUNDANT — superseded by TitleStructureParser. Slated for deletion in next cleanup commit.
            //     See documentation/title-structure-parser-spec.md.
            // Try stripping (yyyy-MM-dd) pattern AND segue markers for matching
            var withoutDateOnly = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"\s*\(\d{4}-\d{2}-\d{2}\)\s*$",
                "").Trim();
            withoutDateOnly = System.Text.RegularExpressions.Regex.Replace(
                withoutDateOnly,
                @"\s*(\[?>?\]?|[-–]?\s*>\s*)\s*$",
                "").Trim();

            if (withoutDateOnly != cleaned && _aliasLookup.TryGetValue(withoutDateOnly, out official))
            {
                return official;
            }

            // L6: dash normalization re-lookup. The parser handles en-dash, em-dash, and
            // box-drawing horizontal in step 6, but does NOT normalize U+2212 MINUS SIGN.
            // Kept for that gap.
            var normalizedDashes = cleaned
                .Replace("–", "-")  // en-dash to hyphen
                .Replace("—", "-")  // em-dash to hyphen
                .Replace("−", "-")  // minus sign to hyphen
                .Replace("─", "-"); // box-drawing horizontal to hyphen

            if (_aliasLookup.TryGetValue(normalizedDashes, out official))
            {
                return official;
            }

            // L7: parser preserves canonical parens, so " (1)"/" (2)" survive as content
            // parens. Strip them here. Also handles bare " Reprise" suffix that has no
            // paren/bracket boundary for the parser to detect.
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

            // L8: REDUNDANT — superseded by TitleStructureParser. Slated for deletion in next cleanup commit.
            //     See documentation/title-structure-parser-spec.md.
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

            // L9: fuzzy match (Levenshtein) as last resort for typos.
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
            if (string.IsNullOrEmpty(input) || _aliasLookup == null || _aliasLookup.Count == 0) return null;

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
                // Safety net: If ParseTitleAndDate missed the date during ReadFolder,
                // try to extract it from the raw title before normalization strips it.
                // Handles bracket dates [Venue M/D/YY] and parenthetical dates (M/D/YY Venue)
                // that may not have been recognized during initial file reading.
                if (string.IsNullOrEmpty(track.TrackDate))
                {
                    var rawTitle = !string.IsNullOrEmpty(track.RawTitle) ? track.RawTitle : track.SongName;
                    var extractedDate = ExtractDateFromRawTitle(rawTitle, track.AlbumDate);
                    if (extractedDate != null)
                    {
                        track.TrackDate = extractedDate;
                    }
                }

                // Use SongName for matching — it's already been cleaned by ParseTitleAndDate
                // during ReadFolder (date/tour suffixes stripped, segue markers removed).
                // Fall back to Title (RawTitle) only if SongName is empty.
                var titleToNormalize = !string.IsNullOrEmpty(track.SongName) ? track.SongName : track.Title;

                // REDUNDANT — superseded by TitleStructureParser. Slated for deletion in next cleanup commit.
                //     See documentation/title-structure-parser-spec.md.
                // Normalize() now forwards albumDate to the parser, which extracts and strips
                // slash-formatted dates internally. The pre-pass below is no-op-equivalent for
                // every input the parser handles.
                // var dateNormalizedTitle = NormalizeDateInTitle(titleToNormalize, track.AlbumDate);
                // if (dateNormalizedTitle != titleToNormalize)
                // {
                //     titleToNormalize = dateNormalizedTitle;
                // }

                // Then normalize the song name for matching
                var normalized = Normalize(titleToNormalize, track.AlbumDate);
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
        /// Looks up the canonical OfficialTitle for a song name (exact alias match only, no fuzzy matching).
        /// Returns null if the input doesn't match any OfficialTitle or alias.
        /// </summary>
        public string? GetOfficialTitle(string input)
        {
            if (string.IsNullOrEmpty(input)) return null;
            return _aliasLookup.TryGetValue(input, out var official) ? official : null;
        }

        /// <summary>
        /// Gets all known song titles for autocomplete
        /// </summary>
        public List<string> GetAllTitles()
        {
            var titles = new List<string>();

            if (_database?.Artists != null)
            {
                foreach (var artist in _database.Artists)
                {
                    titles.AddRange(artist.Songs?.Select(s => s.OfficialTitle) ?? new List<string>());
                }
            }

            return titles.OrderBy(t => t).ToList();
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
        /// Normalize slash-formatted dates in parenthetical suffixes to yyyy-MM-dd format.
        /// </summary>
        /// <param name="title">Title that may contain a trailing slash-formatted date.</param>
        /// <param name="albumDate">Optional album date (yyyy-MM-dd or yyyy form). When provided,
        /// two-digit years are resolved against the album's century where possible.
        /// See <see cref="TwoDigitYearResolver"/>.</param>
        public string NormalizeDateInTitle(string title, string? albumDate = null)
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

                    // Resolve two-digit years using album-date context (when available)
                    // and a current-year-relative pivot. See TwoDigitYearResolver.
                    year = TwoDigitYearResolver.ResolveTwoDigitYear(year, albumDate);
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

        /// <summary>
        /// Extracts a date from a raw title string as a fallback when ParseTitleAndDate didn't find one.
        /// Handles bracket dates [Venue M/D/YY], parenthetical dates (M/D/YY Venue),
        /// (Live at/in Venue M/D/YYYY), and yyyy-MM-dd formats.
        /// </summary>
        /// <param name="title">Raw title to scan.</param>
        /// <param name="albumDate">Optional album date (yyyy-MM-dd or yyyy form) used to disambiguate two-digit years.</param>
        private string? ExtractDateFromRawTitle(string title, string? albumDate = null)
        {
            if (string.IsNullOrEmpty(title)) return null;

            // Pattern A: Date inside square brackets [... M/D/YY ...] or [... M/D/YYYY ...]
            var match = System.Text.RegularExpressions.Regex.Match(title, @"\[.*?(\d{1,2})/(\d{1,2})/(\d{2,4}).*?\]");
            if (match.Success)
            {
                return ParseSlashDate(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value, albumDate);
            }

            // Pattern B: Slash date inside parentheses (M/D/YY ...) or (... M/D/YYYY)
            match = System.Text.RegularExpressions.Regex.Match(title, @"\(.*?(\d{1,2})/(\d{1,2})/(\d{2,4}).*?\)");
            if (match.Success)
            {
                return ParseSlashDate(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value, albumDate);
            }

            // Pattern C: yyyy-MM-dd in parentheses (already handled by ParseTitleAndDate, but just in case)
            match = System.Text.RegularExpressions.Regex.Match(title, @"\((\d{4}-\d{2}-\d{2})");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return null;
        }

        /// <summary>
        /// Parses M/D/YY or M/D/YYYY slash-formatted date parts into yyyy-MM-dd string.
        /// Returns null if date is invalid.
        /// </summary>
        /// <param name="albumDate">Optional album date used to resolve two-digit years.</param>
        private string? ParseSlashDate(string monthStr, string dayStr, string yearStr, string? albumDate = null)
        {
            if (!int.TryParse(monthStr, out var month) || !int.TryParse(dayStr, out var day) || !int.TryParse(yearStr, out var year))
                return null;

            year = TwoDigitYearResolver.ResolveTwoDigitYear(year, albumDate);
            if (month < 1 || month > 12 || day < 1 || day > 31 || year < 1900 || year > 2100)
                return null;

            return $"{year:D4}-{month:D2}-{day:D2}";
        }

        /// <summary>
        /// Adds an alias for an existing song's OfficialTitle. If the alias already exists, does nothing.
        /// Reloads the alias lookup after saving so the new alias is immediately available.
        /// </summary>
        public bool AddAlias(string officialTitle, string alias)
        {
            if (string.IsNullOrEmpty(officialTitle) || string.IsNullOrEmpty(alias))
                return false;

            // Don't add if alias is same as official title (case-insensitive)
            if (string.Equals(officialTitle, alias, StringComparison.OrdinalIgnoreCase))
                return false;

            // Re-read from disk to avoid overwriting concurrent changes
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "songs.json");
            if (!File.Exists(path)) return false;

            var json = File.ReadAllText(path);
            var db = JsonConvert.DeserializeObject<SongDatabase>(json);
            if (db == null) return false;

            // Find the song entry by OfficialTitle
            SongEntry? targetSong = null;
            if (db.Artists != null)
            {
                foreach (var artist in db.Artists)
                {
                    targetSong = artist.Songs?.FirstOrDefault(s =>
                        string.Equals(s.OfficialTitle, officialTitle, StringComparison.OrdinalIgnoreCase));
                    if (targetSong != null) break;
                }
            }

            if (targetSong == null) return false;

            // Check if alias already exists
            if (targetSong.Aliases == null)
                targetSong.Aliases = new List<string>();

            if (targetSong.Aliases.Any(a => string.Equals(a, alias, StringComparison.OrdinalIgnoreCase)))
                return false; // Already exists

            targetSong.Aliases.Add(alias);

            // Atomic write: temp file + rename
            var tempPath = path + ".tmp";
            var newJson = JsonConvert.SerializeObject(db, Formatting.Indented);
            File.WriteAllText(tempPath, newJson);
            File.Delete(path);
            File.Move(tempPath, path);

            // Reload in-memory state
            LoadDatabase();

            return true;
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
