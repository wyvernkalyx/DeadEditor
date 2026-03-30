using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DeadEditor.Models
{
    /// <summary>
    /// DateRow - Display model for "By Date" view mode in the Library Grid.
    /// Each row represents a single concert date, possibly exploded from a multi-date album.
    /// Venue and CityState are editable and persist to shows.json.
    /// </summary>
    public class DateRow : INotifyPropertyChanged
    {
        private string _venue = "";
        private string _cityState = "";

        public string Date { get; set; } = "";

        public string Venue
        {
            get => _venue;
            set
            {
                if (_venue != value)
                {
                    _venue = value;
                    OnPropertyChanged();
                }
            }
        }

        public string CityState
        {
            get => _cityState;
            set
            {
                if (_cityState != value)
                {
                    _cityState = value;
                    OnPropertyChanged();
                }
            }
        }

        public string FromAlbum { get; set; } = "";
        public int TrackCount { get; set; }

        // Back-reference to source album for double-click navigation
        public LibraryShow SourceShow { get; set; } = null!;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
