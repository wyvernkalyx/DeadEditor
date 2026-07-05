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
        /// Registered alias setlists for this concert — stored combined-track shapes that let a
        /// media import match the official setlist OR a confirmed combine (e.g. the official
        /// "Help On The Way/Slipknot!" pressing). Reference-based: each entry stores only the
        /// covered official entry indices, never flattened names (see alias-setlists-spec.md §3.2).
        /// Additive and backward-compatible: absent on legacy fetcher-sourced files, which leave
        /// this initialized-empty list untouched. Empty-omit on write via
        /// <see cref="ShouldSerializeAliasSetlists"/> so otherwise-untouched concert files stay
        /// pristine (no "aliasSetlists": [] noise). NOT copied by ConcertSnapshot.Project — an
        /// alias write is additive provenance and must not enter the concert diff/unverify
        /// baseline (spec §8.1).
        /// </summary>
        public List<AliasSetlist> AliasSetlists { get; set; } = new();

        /// <summary>Omit the alias list entirely when empty, keeping legacy files byte-clean.</summary>
        public bool ShouldSerializeAliasSetlists() => AliasSetlists.Count > 0;

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

        /// <summary>
        /// Typed-entry kind (setlist-extras-writeback-spec.md D1/§3.1): one of
        /// song / tuning / false-start / banter / other-extra (extensible, prefer other-extra).
        /// Default "song"; a missing key deserializes to "song" so every legacy fetcher-sourced
        /// file is valid unchanged. Written to BOTH sets[] and tracks[] in lockstep (D8a) — see the
        /// twin on <see cref="ConcertTrack"/>. Empty-omitted when "song" via
        /// <see cref="ShouldSerializeType"/> (mirrors <see cref="ShouldSerializeAliasSetlists"/>) so
        /// untyped files stay byte-clean.
        /// </summary>
        public string Type { get; set; } = "song";

        /// <summary>Omit the type key when it is the "song" default (or empty/null), keeping legacy files byte-clean.</summary>
        public bool ShouldSerializeType() =>
            !string.IsNullOrEmpty(Type) && !string.Equals(Type, "song", System.StringComparison.Ordinal);
    }

    public class ConcertTrack
    {
        public int Position { get; set; }
        public string SongName { get; set; } = "";
        public string Date { get; set; } = "";
        public bool Segue { get; set; }
        public string Set { get; set; } = "";

        /// <summary>
        /// Typed-entry kind, the flattened-shape twin of <see cref="ConcertSong.Type"/> kept in
        /// lockstep by <c>ConcertSnapshot.Project</c> (D8a). Same vocabulary, default, and empty-omit
        /// behavior (setlist-extras-writeback-spec.md D1/§3.1/§3.2).
        /// </summary>
        public string Type { get; set; } = "song";

        /// <summary>Omit the type key when it is the "song" default (or empty/null), keeping legacy files byte-clean.</summary>
        public bool ShouldSerializeType() =>
            !string.IsNullOrEmpty(Type) && !string.Equals(Type, "song", System.StringComparison.Ordinal);
    }

    /// <summary>
    /// One registered alias setlist for a concert — a stored combined-track shape. Reference-based:
    /// its <see cref="Entries"/> carry covered official indices only (alias-setlists-spec.md §3.2).
    /// </summary>
    public class AliasSetlist
    {
        /// <summary>Stable id, optional — provenance/debug only.</summary>
        public string Id { get; set; } = "";

        /// <summary>Optional source note, e.g. "One From The Vault".</summary>
        public string Label { get; set; } = "";

        /// <summary>The combined runs this alias collapses; singleton runs are implicit, never stored.</summary>
        public List<AliasEntry> Entries { get; set; } = new();
    }

    /// <summary>
    /// One combined run within an <see cref="AliasSetlist"/>: a contiguous span of official entries
    /// (over flattened official positions) collapsed into a single combined media track.
    /// </summary>
    public class AliasEntry
    {
        /// <summary>Contiguous run of covered official entry indices, in order.</summary>
        public List<int> CoveredOfficialIndices { get; set; } = new();
    }
}
