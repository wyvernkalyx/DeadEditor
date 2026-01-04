using System.Collections.Generic;

namespace DeadEditor.Models
{
    public class SongEntry
    {
        public string OfficialTitle { get; set; }      // The correct, normalized title
        public List<string> Aliases { get; set; }      // Alternative spellings/abbreviations
    }

    public class SongDatabase
    {
        public List<SongEntry> Songs { get; set; }
    }
}
