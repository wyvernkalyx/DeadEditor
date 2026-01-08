using System.Collections.Generic;

namespace DeadEditor.Models
{
    public class SongEntry
    {
        public string OfficialTitle { get; set; }      // The correct, normalized title
        public List<string> Aliases { get; set; }      // Alternative spellings/abbreviations
    }

    public class ArtistEntry
    {
        public string Name { get; set; }               // Artist name (e.g., "Grateful Dead", "NRPS")
        public List<SongEntry> Songs { get; set; }     // Songs by this artist
    }

    public class SongDatabase
    {
        // New artist-based structure
        public List<ArtistEntry> Artists { get; set; }

        // Legacy structure for backward compatibility
        public List<SongEntry> Songs { get; set; }
    }
}
