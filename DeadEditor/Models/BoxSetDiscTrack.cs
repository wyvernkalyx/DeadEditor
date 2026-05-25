namespace DeadEditor.Models
{
    /// <summary>
    /// A single track on a disc. Carries its own <see cref="SongName"/> and
    /// <see cref="SegueOut"/> (the values written to FLAC tags on import) plus a
    /// reference to the concert it belongs to. <see cref="Fingerprint"/> stays null
    /// until a successful import match persists it.
    /// </summary>
    public class BoxSetDiscTrack
    {
        public int TrackNumber { get; set; }                // disc-prefixed: 101, 102, ..., 201, etc.
        public string Title { get; set; } = "";
        public string Duration { get; set; } = "";          // "mm:ss" format, free string for MVP
        public string ConcertId { get; set; } = "";         // references BoxSetConcert.Id
        public string SongName { get; set; } = "";
        public bool SegueOut { get; set; }
        public string? Fingerprint { get; set; }            // null until first import match
    }
}
