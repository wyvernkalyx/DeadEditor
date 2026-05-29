namespace DeadEditor.Models
{
    /// <summary>
    /// One track in a box set: a song performed on a date, with an opaque track
    /// number and a segue-out flag. The date lives on the track because a box set
    /// is multi-date by nature (see documentation/box-set-design-memo.md, the
    /// 2026-05-28 decision).
    /// </summary>
    public class BoxSetTrack
    {
        public int TrackNumber { get; set; }    // disc-prefixed (101, 1207) or continuous; opaque to the model
        public string SongName { get; set; } = "";
        public string Date { get; set; } = "";  // yyyy-MM-dd; validated in the wizard (commit G)
        public bool SegueOut { get; set; }
    }
}
