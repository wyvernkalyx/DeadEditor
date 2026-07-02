namespace DeadEditor.Models
{
    /// <summary>
    /// One flattened setlist entry, projected for the reference side-panel and shared with the
    /// Match Setlist flatten (reference-side-panel-spec.md §4). A pure display/data DTO — no WPF,
    /// no services, immutable — produced by <see cref="Helpers.SetlistProjection.Build"/>.
    /// </summary>
    public class SetlistEntryVm
    {
        /// <summary>Setlist display name as stored (pre-canonicalization).</summary>
        public string Name { get; }

        /// <summary>Canonical song title: the setlist name run through the caller's resolver.</summary>
        public string Canonical { get; }

        /// <summary>Flattened, 0-based position across all sets — the claim axis.</summary>
        public int Position { get; }

        /// <summary>Segue-out marker for this entry.</summary>
        public bool Segue { get; }

        /// <summary>Human set label, e.g. "Set 2, #3".</summary>
        public string SetLabel { get; }

        public SetlistEntryVm(string name, string canonical, int position, bool segue, string setLabel)
        {
            Name = name;
            Canonical = canonical;
            Position = position;
            Segue = segue;
            SetLabel = setLabel;
        }
    }
}
