using System.Collections.Generic;

namespace DeadEditor.Models
{
    /// <summary>
    /// One set within a concert's setlist (e.g. "1", "2", "encore"). <see cref="Set"/>
    /// is a free string by design — the wizard's UI enforces a recommended vocabulary,
    /// but the data type accepts anything.
    /// </summary>
    public class BoxSetSet
    {
        public string Set { get; set; } = "";               // "1", "2", "3", "encore", or freeform
        public List<BoxSetSetSong> Songs { get; set; } = new();
    }

    /// <summary>
    /// A single song within a set's setlist, with a segue-out flag linking it to the
    /// following song.
    /// </summary>
    public class BoxSetSetSong
    {
        public string Name { get; set; } = "";
        public bool SegueOut { get; set; }
    }
}
