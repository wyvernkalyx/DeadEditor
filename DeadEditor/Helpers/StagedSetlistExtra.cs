namespace DeadEditor.Helpers
{
    /// <summary>
    /// One proposed setlist extra handed to <c>EditSetlistView</c> as a pre-populated, UNSAVED row
    /// when the user accepts a write-back offer (setlist-extras-writeback-spec.md §6.1 D12, slice 7a).
    /// A pure DTO threaded through <c>ShellWindow.NavigateToSetlistEditor</c>; the editor renders each
    /// as an ordinary inserted row (no OriginIndex) that the user may retype, relabel, reposition, or
    /// delete, and that lands only through the editor's normal diff-at-save review — never auto-saved.
    /// </summary>
    public sealed class StagedSetlistExtra
    {
        /// <summary>Row label (the offer uses the track's canonical song name). Never empty at construction.</summary>
        public string Label { get; }

        /// <summary>Typed-entry kind (D1 vocabulary). The offer stages every row as <c>other-extra</c>.</summary>
        public string Type { get; }

        public StagedSetlistExtra(string label, string type)
        {
            Label = label ?? "";
            Type = string.IsNullOrWhiteSpace(type) ? Models.SetlistEntryType.OtherExtra : type;
        }
    }
}
