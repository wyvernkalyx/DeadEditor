using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DeadEditor.Services
{
    public class SetlistSong
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("segue")]
        public bool Segue { get; set; }
    }

    public class SetInfo
    {
        [JsonProperty("label")]
        public string Label { get; set; } = "";      // "Set 1", "Set 2", "Encore"

        [JsonProperty("encore")]
        public bool Encore { get; set; }

        [JsonProperty("songs")]
        public List<SetlistSong> Songs { get; set; } = new();
    }

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

        [JsonProperty("sets")]
        public List<SetInfo>? Sets { get; set; }

        /// <summary>
        /// Returns true if this show has setlist data.
        /// </summary>
        public bool HasSetlist => Sets != null && Sets.Count > 0;

        /// <summary>
        /// Formats location as "City, ST" (US) or "City, Country" (non-US).
        /// </summary>
        public string FormattedLocation
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();

                if (!string.IsNullOrEmpty(City))
                    parts.Add(City);

                if (Country == "US")
                {
                    // US: show State (if present). Never show "US" itself.
                    if (!string.IsNullOrEmpty(State))
                        parts.Add(State);
                }
                else
                {
                    // Non-US: show Country (if present). State is ignored outside US.
                    if (!string.IsNullOrEmpty(Country))
                        parts.Add(Country);
                }

                return string.Join(", ", parts);
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
        /// Returns all date keys from shows.json (yyyy-MM-dd format).
        /// </summary>
        public IReadOnlyCollection<string> GetAllDates() => _shows.Keys;

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
        /// Returns the setlist for a show date, or null if no setlist data exists.
        /// </summary>
        public List<SetInfo>? GetSetlist(string dateYyyyMmDd)
        {
            var show = GetShowByDate(dateYyyyMmDd);
            return show?.HasSetlist == true ? show.Sets : null;
        }

        /// <summary>
        /// Returns which set number (1-based) a song at a given position belongs to,
        /// and the track number within that set. Used for disc/track numbering.
        /// songPosition is 0-based across the entire show (flattened across all sets).
        /// Returns null if no setlist data or position is out of range.
        /// </summary>
        public (int Disc, int Track)? GetDiscTrack(string dateYyyyMmDd, int songPosition)
        {
            var sets = GetSetlist(dateYyyyMmDd);
            if (sets == null || songPosition < 0)
                return null;

            int offset = 0;
            for (int i = 0; i < sets.Count; i++)
            {
                int setSize = sets[i].Songs.Count;
                if (songPosition < offset + setSize)
                    return (i + 1, songPosition - offset + 1);
                offset += setSize;
            }

            return null; // position out of range
        }

        /// <summary>
        /// Returns true if the song at the given position segued into the next song.
        /// songPosition is 0-based across the entire show (flattened across all sets).
        /// Returns null if no setlist data or position is out of range.
        /// </summary>
        public bool? GetSegue(string dateYyyyMmDd, int songPosition)
        {
            var sets = GetSetlist(dateYyyyMmDd);
            if (sets == null || songPosition < 0)
                return null;

            int offset = 0;
            foreach (var set in sets)
            {
                if (songPosition < offset + set.Songs.Count)
                    return set.Songs[songPosition - offset].Segue;
                offset += set.Songs.Count;
            }

            return null; // position out of range
        }

        /// <summary>
        /// Returns the total number of songs in the setlist for a given date.
        /// Returns null if no setlist data exists.
        /// </summary>
        public int? GetSetlistSongCount(string dateYyyyMmDd)
        {
            var sets = GetSetlist(dateYyyyMmDd);
            if (sets == null)
                return null;

            int count = 0;
            foreach (var set in sets)
                count += set.Songs.Count;
            return count;
        }

        /// <summary>
        /// Converts disc/track to the 100-format track number used in FLAC tags.
        /// Disc 1, Track 3 → 103. Disc 2, Track 1 → 201.
        /// </summary>
        public static int ToTrackNumber(int disc, int track) => disc * 100 + track;

        /// <summary>
        /// Given a show date and a 0-based song position in the full setlist,
        /// returns the 100-format track number (e.g., 101, 205, 301).
        /// Returns null if no setlist data or position out of range.
        /// </summary>
        public int? SuggestTrackNumber(string dateYyyyMmDd, int songPosition)
        {
            var discTrack = GetDiscTrack(dateYyyyMmDd, songPosition);
            if (discTrack == null)
                return null;
            return ToTrackNumber(discTrack.Value.Disc, discTrack.Value.Track);
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
