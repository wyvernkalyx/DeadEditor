using System.Collections.Generic;

namespace DeadEditor.Models
{
    /// <summary>
    /// Reference data for a single concert, loaded from Data/concerts/{date}.json.
    /// Read-only — sourced from setlist.fm via the SetlistFetcher tool.
    /// </summary>
    public class ConcertReference
    {
        public string Date { get; set; } = "";
        public string Venue { get; set; } = "";
        public string City { get; set; } = "";
        public string State { get; set; } = "";
        public string Country { get; set; } = "";
        public string SetlistFmId { get; set; } = "";
        public string SetlistFmUrl { get; set; } = "";
        public string LastUpdated { get; set; } = "";
        public bool HasSetlist { get; set; }
        public bool MultiShow { get; set; }
        public List<ConcertSet> Sets { get; set; } = new();
        public List<ConcertTrack> Tracks { get; set; } = new();

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

        /// <summary>Number of songs in the setlist.</summary>
        public int SongCount => Tracks.Count;
    }

    public class ConcertSet
    {
        public string Name { get; set; } = "";
        public List<ConcertSong> Songs { get; set; } = new();
    }

    public class ConcertSong
    {
        public string Name { get; set; } = "";
        public string Date { get; set; } = "";
        public bool Segue { get; set; }
        public string Info { get; set; } = "";
    }

    public class ConcertTrack
    {
        public int Position { get; set; }
        public string SongName { get; set; } = "";
        public string Date { get; set; } = "";
        public bool Segue { get; set; }
        public string Set { get; set; } = "";
    }
}
