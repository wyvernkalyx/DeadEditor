using DeadEditor.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DeadEditor
{
    /// <summary>
    /// ViewModel for TrackInfo that handles display title computation and property change notification.
    /// Used by ImportView for the editable track grid.
    /// </summary>
    public class TrackInfoViewModel : INotifyPropertyChanged
    {
        public TrackInfo Track { get; }
        private readonly AlbumInfo _albumInfo;
        private readonly bool _isEditMode;
        private string _displayTitle;
        private string _inheritedDate;

        public TrackInfoViewModel(TrackInfo track, AlbumInfo albumInfo, bool isEditMode = false)
        {
            Track = track;
            _albumInfo = albumInfo;
            _isEditMode = isEditMode;
            _displayTitle = "";
            _inheritedDate = "";
            UpdateDisplayTitle();
            UpdateInheritedDate();

            // Subscribe to TrackInfo property changes to update DisplayTitle when SongName changes
            Track.PropertyChanged += Track_PropertyChanged;
        }

        private void Track_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TrackInfo.SongName))
            {
                UpdateDisplayTitle();
            }
            else if (e.PropertyName == nameof(TrackInfo.TrackDate))
            {
                UpdateDisplayTitle();
                UpdateInheritedDate();
            }
        }

        public string DisplayTitle
        {
            get => _displayTitle;
            set
            {
                if (_displayTitle != value)
                {
                    _displayTitle = value;
                    OnPropertyChanged();
                }
            }
        }

        public string InheritedDate
        {
            get => _inheritedDate;
            set
            {
                if (_inheritedDate != value)
                {
                    _inheritedDate = value;
                    OnPropertyChanged();
                }
            }
        }

        // Proxy properties for DataGrid binding
        public int TrackNumber
        {
            get => Track.TrackNumber;
            set
            {
                Track.TrackNumber = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayTrackNumber));
            }
        }

        // Read-only property that displays disc-aware track numbers (101, 201, 301, etc.)
        public string DisplayTrackNumber => Track.DisplayTrackNumber;

        public int DiscNumber
        {
            get => Track.DiscNumber;
            set
            {
                Track.DiscNumber = value;
                Track.IsModified = true;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayTrackNumber));
            }
        }

        public string SongName
        {
            get => Track.SongName ?? "";
            set
            {
                Track.SongName = value;
                OnPropertyChanged();
                UpdateDisplayTitle();
            }
        }

        public bool Segue
        {
            get => Track.Segue;
            set
            {
                Track.Segue = value;
                OnPropertyChanged();
                UpdateDisplayTitle();
            }
        }

        public string TrackDate
        {
            get => Track.TrackDate ?? "";
            set
            {
                Track.TrackDate = string.IsNullOrWhiteSpace(value) ? "" : value;
                OnPropertyChanged();
                UpdateDisplayTitle();
            }
        }

        public string Duration => Track.Duration;

        public void UpdateDisplayTitle()
        {
            if (_isEditMode)
            {
                // EDIT MODE: Display the raw SongName as-is from FLAC tags
                DisplayTitle = Track.SongName ?? Track.Title ?? "";
            }
            else
            {
                // IMPORT MODE: Build display title with date appending
                var effectiveDate = string.IsNullOrEmpty(Track.TrackDate) ? _albumInfo?.AlbumDate : Track.TrackDate;
                var songName = Track.SongName ?? Track.Title ?? "";

                if (Track.Segue)
                {
                    songName += " >";
                }

                if (!string.IsNullOrEmpty(effectiveDate))
                {
                    DisplayTitle = $"{songName} ({effectiveDate})";
                }
                else
                {
                    DisplayTitle = songName;
                }
            }
        }

        public void UpdateInheritedDate()
        {
            if (_isEditMode)
            {
                InheritedDate = Track.TrackDate ?? "";
            }
            else
            {
                // Show track-specific date if available, otherwise fall back to album date
                InheritedDate = !string.IsNullOrEmpty(Track.TrackDate)
                    ? Track.TrackDate
                    : (_albumInfo?.AlbumDate ?? "");
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
