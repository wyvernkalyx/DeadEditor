namespace DeadEditor.Models
{
    /// <summary>
    /// Outcome of <see cref="Helpers.ManualMatchApply.Apply"/>: the track's prior song name (the
    /// alias candidate for the host's alias-persistence hook) and the flattened setlist position now
    /// claimed (for the host's panel re-dim + status hooks). A pure data DTO — no WPF, no services —
    /// mirroring the immutable <see cref="SetlistEntryVm"/> shape.
    /// </summary>
    public class ManualMatchResult
    {
        /// <summary>Song name on the track before the canonical overwrite (the alias candidate).</summary>
        public string PreviousSongName { get; }

        /// <summary>Canonical title written to the track.</summary>
        public string Canonical { get; }

        /// <summary>Flattened setlist position now added to the claimed set.</summary>
        public int ClaimedPosition { get; }

        public ManualMatchResult(string previousSongName, string canonical, int claimedPosition)
        {
            PreviousSongName = previousSongName;
            Canonical = canonical;
            ClaimedPosition = claimedPosition;
        }
    }
}
