using DeadEditor.Models;
using DeadEditor.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DeadEditor
{
    /// <summary>
    /// Wrapper for TrackInfo that adds computed properties for playlist display.
    /// </summary>
    public class PlaylistTrackViewModel : INotifyPropertyChanged
    {
        private readonly TrackInfo _track;
        private readonly AudioPlayerService _player;

        public PlaylistTrackViewModel(TrackInfo track, AudioPlayerService player)
        {
            _track = track;
            _player = player;
        }

        public TrackInfo Track => _track;

        public string TrackNumber => _track.TrackNumber.ToString();

        // Computed property for disc-aware sorting
        // With disc numbers: 101, 102... 201, 202... (DiscNumber * 100 + TrackNumber)
        // No disc numbers: 1, 2, 3... (raw TrackNumber)
        public int SortKey => (_track.DiscNumber > 0 ? _track.DiscNumber * 100 : 0) + _track.TrackNumber;

        public string DisplayTitle
        {
            get
            {
                // Use TrackInfo's DisplayTitle which includes date suffix: "Song > (yyyy-MM-dd)"
                return _track.DisplayTitle ?? "";
            }
        }

        public string ShortDate
        {
            get
            {
                if (string.IsNullOrEmpty(_track.TrackDate))
                    return "";

                // Return full yyyy-MM-dd format
                return _track.TrackDate;
            }
        }

        public string Duration => _track.Duration ?? "";

        public bool IsCurrentTrack => _player.CurrentTrack == _track;

        public void RefreshIsCurrentTrack()
        {
            OnPropertyChanged(nameof(IsCurrentTrack));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
