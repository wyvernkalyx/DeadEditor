using System.ComponentModel;

namespace DeadEditor.Models
{
    /// <summary>
    /// One track in a box set: a song performed on a date, with an opaque track
    /// number and a segue-out flag. The date lives on the track because a box set
    /// is multi-date by nature (see documentation/box-set-design-memo.md, the
    /// 2026-05-28 decision).
    ///
    /// Implements <see cref="INotifyPropertyChanged"/> so the wizard's step-2 grid
    /// (commit G2) can bind two-way directly to <c>BoxSetDefinition.Tracks</c>.
    /// Serialization is unchanged: the four public properties still emit the same
    /// camelCase keys; the event and private backing fields are not serialized.
    /// </summary>
    public class BoxSetTrack : INotifyPropertyChanged
    {
        private int _trackNumber;       // disc-prefixed (101, 1207) or continuous; opaque to the model
        private string _songName = "";
        private string _date = "";      // yyyy-MM-dd; validated in the wizard (commit G2)
        private bool _segueOut;

        public int TrackNumber { get => _trackNumber; set { if (_trackNumber != value) { _trackNumber = value; OnPropertyChanged(nameof(TrackNumber)); } } }
        public string SongName { get => _songName; set { if (_songName != value) { _songName = value; OnPropertyChanged(nameof(SongName)); } } }
        public string Date { get => _date; set { if (_date != value) { _date = value; OnPropertyChanged(nameof(Date)); } } }
        public bool SegueOut { get => _segueOut; set { if (_segueOut != value) { _segueOut = value; OnPropertyChanged(nameof(SegueOut)); } } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
