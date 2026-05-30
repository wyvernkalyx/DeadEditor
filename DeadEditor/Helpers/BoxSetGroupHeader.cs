namespace DeadEditor.Helpers
{
    /// <summary>
    /// Formats the collapsible group-header text for the box-set wizard's date-grouped
    /// track grid (commit H): "date — venue (N tracks)". Pure string formatting with no
    /// WPF or IO dependency — the caller (the WPF GroupHeaderConverter) resolves the venue
    /// via ShowLookupService and passes it in, so this stays unit-testable.
    /// </summary>
    public static class BoxSetGroupHeader
    {
        /// <summary>
        /// Builds a date-group header.
        /// <list type="bullet">
        /// <item>date + venue → "1987-12-27 — Long Beach Arena, Long Beach, CA (21 tracks)"</item>
        /// <item>date, no venue → "1987-12-27 (21 tracks)"</item>
        /// <item>blank/unknown date → "(no date) (21 tracks)"</item>
        /// </list>
        /// The count is singularized ("1 track").
        /// </summary>
        public static string FormatGroupHeader(string? date, string? venue, int trackCount)
        {
            string count = trackCount == 1 ? "1 track" : $"{trackCount} tracks";

            if (string.IsNullOrWhiteSpace(date))
                return $"(no date) ({count})";

            if (string.IsNullOrWhiteSpace(venue))
                return $"{date} ({count})";

            return $"{date} — {venue} ({count})";
        }
    }
}
