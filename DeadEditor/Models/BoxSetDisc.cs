using System.Collections.Generic;

namespace DeadEditor.Models
{
    /// <summary>
    /// A physical disc within a box set. Tracks are numbered with the disc-prefixed
    /// convention (101, 102, ..., 201, ...) carried on each <see cref="BoxSetDiscTrack"/>.
    /// </summary>
    public class BoxSetDisc
    {
        public int DiscNumber { get; set; }
        public string? DiscName { get; set; }               // optional disc title
        public List<BoxSetDiscTrack> Tracks { get; set; } = new();
    }
}
