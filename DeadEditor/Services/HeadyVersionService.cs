using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DeadEditor.Services
{
    public class HeadyInfo
    {
        [JsonProperty("song")]
        public string Song { get; set; } = "";

        [JsonProperty("date")]
        public string Date { get; set; } = "";

        [JsonProperty("venue")]
        public string Venue { get; set; } = "";

        [JsonProperty("city")]
        public string City { get; set; } = "";

        [JsonProperty("state")]
        public string State { get; set; } = "";

        [JsonProperty("rank")]
        public int Rank { get; set; }

        [JsonProperty("votes")]
        public int Votes { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; } = "";
    }

    public class HeadyData
    {
        [JsonProperty("versions")]
        public List<HeadyInfo> Versions { get; set; } = new();
    }

    /// <summary>
    /// Provides lookup of community-rated "heady" (top-voted) versions of songs
    /// from a static JSON data file. Thread-safe, lazily loaded singleton.
    /// </summary>
    public class HeadyVersionService
    {
        private static readonly Lazy<HeadyVersionService> _instance = new(() => new HeadyVersionService());
        public static HeadyVersionService Instance => _instance.Value;

        private readonly List<HeadyInfo> _versions;

        // Keyed by date for fast date-based lookups
        private readonly Dictionary<string, List<HeadyInfo>> _byDate;

        // Keyed by normalized song name (lowercase) for song-based lookups
        private readonly Dictionary<string, List<HeadyInfo>> _bySong;

        private HeadyVersionService()
        {
            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "heady.json");
            _versions = LoadVersions(filePath);

            // Build date index
            _byDate = new Dictionary<string, List<HeadyInfo>>();
            foreach (var v in _versions)
            {
                if (!_byDate.TryGetValue(v.Date, out var list))
                {
                    list = new List<HeadyInfo>();
                    _byDate[v.Date] = list;
                }
                list.Add(v);
            }

            // Build song index (normalized lowercase)
            _bySong = new Dictionary<string, List<HeadyInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in _versions)
            {
                var key = v.Song.ToLowerInvariant();
                if (!_bySong.TryGetValue(key, out var list))
                {
                    list = new List<HeadyInfo>();
                    _bySong[key] = list;
                }
                list.Add(v);
            }
        }

        private static List<HeadyInfo> LoadVersions(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"[HeadyVersionService] Warning: heady.json not found at {path}");
                return new List<HeadyInfo>();
            }

            try
            {
                var json = File.ReadAllText(path);
                var data = JsonConvert.DeserializeObject<HeadyData>(json);
                return data?.Versions ?? new List<HeadyInfo>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HeadyVersionService] Error loading heady.json: {ex.Message}");
                return new List<HeadyInfo>();
            }
        }

        /// <summary>
        /// Check if a specific song+date combo is a heady version.
        /// Song matching is fuzzy: strips segue markers and date suffixes before comparing.
        /// </summary>
        public HeadyInfo? GetHeadyVersion(string songName, string date)
        {
            if (string.IsNullOrEmpty(songName) || string.IsNullOrEmpty(date))
                return null;

            if (!_byDate.TryGetValue(date, out var dateVersions))
                return null;

            var baseName = ExtractBaseSongName(songName);
            return dateVersions.FirstOrDefault(v =>
                string.Equals(v.Song, baseName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Check if ANY track on a given date is a heady version.
        /// </summary>
        public bool HasHeadyVersionsOnDate(string date)
        {
            return !string.IsNullOrEmpty(date) && _byDate.ContainsKey(date);
        }

        /// <summary>
        /// Get all heady versions for a given date.
        /// </summary>
        public List<HeadyInfo> GetHeadyVersionsForDate(string date)
        {
            if (string.IsNullOrEmpty(date) || !_byDate.TryGetValue(date, out var list))
                return new List<HeadyInfo>();
            return list;
        }

        /// <summary>
        /// Get all heady versions for a song (across all dates).
        /// </summary>
        public List<HeadyInfo> GetHeadyVersionsForSong(string songName)
        {
            var baseName = ExtractBaseSongName(songName);
            if (_bySong.TryGetValue(baseName, out var list))
                return list;
            return new List<HeadyInfo>();
        }

        /// <summary>
        /// Returns all dates that have at least one heady version.
        /// </summary>
        public IReadOnlyCollection<string> GetAllHeadyDates() => _byDate.Keys;

        /// <summary>
        /// Extracts the base song name by stripping segue markers, date suffixes,
        /// and other metadata artifacts.
        /// "Scarlet Begonias > Fire on the Mountain (1977-05-08)" → "Scarlet Begonias"
        /// "Dark Star >" → "Dark Star"
        /// </summary>
        private static string ExtractBaseSongName(string songName)
        {
            // Strip date suffix like "(1977-05-08)"
            var name = Regex.Replace(songName, @"\s*\(\d{4}-\d{2}-\d{2}\)\s*$", "");

            // Strip segue marker and everything after " >"
            var segueIndex = name.IndexOf(" >", StringComparison.Ordinal);
            if (segueIndex >= 0)
                name = name.Substring(0, segueIndex);

            // Also strip trailing ">" without space
            name = name.TrimEnd('>', ' ');

            return name.Trim();
        }
    }
}
