using System.Collections.Generic;

namespace DeadEditor.Models
{
    /// <summary>
    /// A box-set curation artifact: the authoritative description of a commercial
    /// release and the tracks it contains. Exists independently of any
    /// audio — built in the wizard from external sources, persisted as one JSON file
    /// per box set under %APPDATA%/DeadEditor/box-sets/. See
    /// documentation/box-set-design-memo.md.
    /// </summary>
    public class BoxSetDefinition
    {
        public int Version { get; set; } = 1;
        public string Name { get; set; } = "";
        public string ReleaseDate { get; set; } = "";       // yyyy-MM-dd
        public string Label { get; set; } = "";
        public string CatalogNumber { get; set; } = "";
        public string Notes { get; set; } = "";
        public bool Verified { get; set; }
        public List<BoxSetTrack> Tracks { get; set; } = new();
    }
}
