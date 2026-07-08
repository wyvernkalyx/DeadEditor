namespace DeadEditor.Models
{
    /// <summary>
    /// The D1 typed-entry vocabulary (setlist-extras-writeback-spec.md §3.1) and the shared
    /// song/extra predicate. Centralizes the vocabulary strings and the one rule both the matcher's
    /// auto-match extra-exclusion (D2) and the completeness computation (D4) depend on: a
    /// null / empty / "song" type IS a song (an absent field reads as song, §3.1), everything else
    /// is an extra. The vocabulary is deliberately extensible (prefer <see cref="OtherExtra"/> over
    /// coining a new label, D1); the predicate is intentionally open — any unrecognized non-"song"
    /// string counts as an extra, so a future type is excluded from auto-match by default.
    /// </summary>
    public static class SetlistEntryType
    {
        public const string Song = "song";
        public const string Tuning = "tuning";
        public const string FalseStart = "false-start";
        public const string Banter = "banter";
        public const string OtherExtra = "other-extra";

        /// <summary>True when <paramref name="type"/> is the song default (or absent/empty).</summary>
        public static bool IsSong(string? type) =>
            string.IsNullOrEmpty(type) || string.Equals(type, Song, System.StringComparison.Ordinal);

        /// <summary>True when <paramref name="type"/> is any non-song extra kind.</summary>
        public static bool IsExtra(string? type) => !IsSong(type);

        /// <summary>
        /// Display label for an extra's type chip, e.g. "false-start" → "FALSE START"
        /// (setlist-extras-writeback-spec.md §7). Shared by the reference panel and the setlist editor
        /// so both render the D1 vocabulary identically.
        /// </summary>
        public static string DisplayLabel(string? type) =>
            (type ?? "").Replace('-', ' ').ToUpperInvariant();
    }
}
