using System.Collections.Generic;
using Newtonsoft.Json;

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

        /// <summary>
        /// User attestation that this concert's data is correct. Bare bool, no provenance —
        /// matches BoxSetDefinition.Verified and AlbumManifest.Verified. Absent from legacy
        /// fetcher-sourced files, which deserialize to false (the correct default). Serializes
        /// as camelCase "verified" via CanonicalJson. See documentation/concert-verification-spec.md.
        /// </summary>
        public bool Verified { get; set; }

        public List<ConcertSet> Sets { get; set; } = new();
        public List<ConcertTrack> Tracks { get; set; } = new();

        /// <summary>
        /// Formats location as "City, ST" (US) or "City, Country" (non-US).
        /// Computed view-only — never persisted (would otherwise leak a junk
        /// camelCase key into written concert files).
        /// </summary>
        [JsonIgnore]
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

        /// <summary>Number of songs in the setlist. Computed view-only — never persisted.</summary>
        [JsonIgnore]
        public int SongCount => Tracks.Count;

        /// <summary>
        /// Bridges the bare <see cref="Verified"/> bool to the shared <see cref="Models.VerificationState"/>
        /// enum so the ConcertDatabaseView leading glyph column can reuse the Library grid's glyph/brush
        /// converters (single source of truth for the ✓ glyph and verified-green). Binary only — a concert
        /// is single-record, so Partial never applies (concert-verification-spec.md decision 9). Computed
        /// view-only — never persisted.
        /// </summary>
        [JsonIgnore]
        public VerificationState VerificationState =>
            Verified ? VerificationState.Verified : VerificationState.Unverified;
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
