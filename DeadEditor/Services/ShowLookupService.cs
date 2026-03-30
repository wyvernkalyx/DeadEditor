using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DeadEditor.Services
{
    public class ShowInfo
    {
        [JsonProperty("venue")]
        public string Venue { get; set; } = "";

        [JsonProperty("city")]
        public string City { get; set; } = "";

        [JsonProperty("state")]
        public string State { get; set; } = "";

        [JsonProperty("country")]
        public string Country { get; set; } = "";

        [JsonProperty("multiShow")]
        public bool MultiShow { get; set; }

        /// <summary>
        /// Formats location as "City, ST" (US) or "City, Country" (non-US).
        /// </summary>
        public string FormattedLocation
        {
            get
            {
                if (!string.IsNullOrEmpty(State) && Country == "US")
                    return $"{City}, {State}";
                return $"{City}, {Country}";
            }
        }

        /// <summary>
        /// Formats as "Venue, City, ST" (US) or "Venue, City, Country" (non-US).
        /// </summary>
        public string FormattedVenueLocation
        {
            get
            {
                if (string.IsNullOrEmpty(Venue))
                    return FormattedLocation;
                return $"{Venue}, {FormattedLocation}";
            }
        }
    }

    /// <summary>
    /// Provides O(1) lookup of concert venue/location by date from Data/shows.json.
    /// Thread-safe, lazily loaded on first access. Supports updates and persistence.
    /// </summary>
    public class ShowLookupService
    {
        private static readonly Lazy<ShowLookupService> _instance = new(() => new ShowLookupService());
        public static ShowLookupService Instance => _instance.Value;

        private readonly Dictionary<string, ShowInfo> _shows;
        private readonly string _filePath;

        private ShowLookupService()
        {
            _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "shows.json");
            _shows = LoadShows(_filePath);
        }

        private static Dictionary<string, ShowInfo> LoadShows(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"[ShowLookupService] Warning: shows.json not found at {path}");
                return new Dictionary<string, ShowInfo>();
            }

            try
            {
                var json = File.ReadAllText(path);
                var shows = JsonConvert.DeserializeObject<Dictionary<string, ShowInfo>>(json);
                return shows ?? new Dictionary<string, ShowInfo>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ShowLookupService] Error loading shows.json: {ex.Message}");
                return new Dictionary<string, ShowInfo>();
            }
        }

        /// <summary>
        /// Looks up a show by yyyy-MM-dd date string.
        /// </summary>
        public ShowInfo? GetShowByDate(string dateYyyyMmDd)
        {
            if (string.IsNullOrEmpty(dateYyyyMmDd))
                return null;

            _shows.TryGetValue(dateYyyyMmDd, out var info);
            return info;
        }

        /// <summary>
        /// Returns true if a show exists for the given date.
        /// </summary>
        public bool HasShow(string dateYyyyMmDd)
        {
            return !string.IsNullOrEmpty(dateYyyyMmDd) && _shows.ContainsKey(dateYyyyMmDd);
        }

        /// <summary>
        /// Updates or creates a show entry for the given date.
        /// </summary>
        public void UpdateShow(string date, string venue, string city, string state, string country)
        {
            if (string.IsNullOrEmpty(date)) return;

            if (_shows.TryGetValue(date, out var existing))
            {
                existing.Venue = venue;
                existing.City = city;
                existing.State = state;
                if (!string.IsNullOrEmpty(country))
                    existing.Country = country;
            }
            else
            {
                _shows[date] = new ShowInfo
                {
                    Venue = venue,
                    City = city,
                    State = state,
                    Country = !string.IsNullOrEmpty(country) ? country : "US",
                    MultiShow = false
                };
            }
        }

        /// <summary>
        /// Persists the in-memory dictionary to Data/shows.json.
        /// Writes atomically via temp file + rename.
        /// </summary>
        public void SaveToFile()
        {
            try
            {
                // Sort by date key for consistent output
                var sorted = new SortedDictionary<string, ShowInfo>(_shows);
                var json = JsonConvert.SerializeObject(sorted, Formatting.Indented);

                var tempPath = _filePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _filePath, overwrite: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ShowLookupService] Error saving shows.json: {ex.Message}");
                throw;
            }
        }
    }
}
