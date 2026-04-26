using DeadEditor.Models;
using DeadEditor.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using System.Windows.Controls;
using System.Windows.Input;

namespace DeadEditor
{
    public partial class EditSetlistView : System.Windows.Controls.UserControl
    {
        private readonly ShellWindow _shell;
        private readonly ConcertReference _concert;
        private readonly NormalizationService _normalizationService;
        private List<EditableTrack> _tracks = new();
        private bool _hasUnsavedChanges;

        // Structural labels for set assignment UI — these are display values, not song data
        private static readonly string[] SetChoices =
            { "Set 1", "Set 2", "Set 3", "Encore", "Encore 2" };

        public string VenueName => _concert.Venue;
        public bool HasUnsavedChanges => _hasUnsavedChanges;

        /// <summary>Fired when save completes so the shell can refresh the detail view.</summary>
        public event EventHandler? SaveCompleted;

        public EditSetlistView(ShellWindow shell, ConcertReference concert)
        {
            InitializeComponent();
            _shell = shell;
            _concert = concert;
            _normalizationService = new NormalizationService();
        }

        private void EditSetlistView_Loaded(object sender, RoutedEventArgs e)
        {
            LoadTrackData();

            // Set up the Set column's combo box items
            var setColumn = TracksDataGrid.Columns[3] as DataGridComboBoxColumn;
            if (setColumn != null)
            {
                setColumn.ItemsSource = SetChoices;
            }
        }

        private void LoadTrackData()
        {
            // Populate metadata fields
            VenueTextBox.Text = _concert.Venue;
            var location = _concert.FormattedLocation;
            CityStateTextBox.Text = !string.IsNullOrEmpty(_concert.State) && _concert.Country == "US"
                ? $"{_concert.City}, {_concert.State}"
                : !string.IsNullOrEmpty(_concert.Country)
                    ? $"{_concert.City}, {_concert.Country}"
                    : _concert.City;
            DateTextBox.Text = _concert.Date;

            // Build editable track list from the concert's sets
            _tracks.Clear();
            int position = 0;

            foreach (var set in _concert.Sets)
            {
                // Normalize set name: strip trailing colon
                var setName = set.Name.TrimEnd(':', ' ');

                foreach (var song in set.Songs)
                {
                    position++;
                    var track = new EditableTrack
                    {
                        Position = position,
                        SongName = song.Name,
                        Date = !string.IsNullOrEmpty(song.Date) ? song.Date : _concert.Date,
                        Segue = song.Segue,
                        Set = setName,
                        Info = song.Info
                    };
                    track.PropertyChanged += Track_PropertyChanged;
                    _tracks.Add(track);
                }
            }

            TracksDataGrid.ItemsSource = _tracks;
            BottomStatusText.Text = $"{_tracks.Count} tracks";
        }

        private void Track_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            _hasUnsavedChanges = true;
        }

        // ===== ADD / REMOVE SONGS =====

        private void AddSongButton_Click(object sender, RoutedEventArgs e)
        {
            var lastSet = _tracks.LastOrDefault()?.Set ?? "Set 1";
            var newTrack = new EditableTrack
            {
                Position = _tracks.Count + 1,
                SongName = "",
                Date = DateTextBox.Text.Trim(),
                Segue = false,
                Set = lastSet
            };
            newTrack.PropertyChanged += Track_PropertyChanged;
            _tracks.Add(newTrack);
            TracksDataGrid.ItemsSource = null;
            TracksDataGrid.ItemsSource = _tracks;
            _hasUnsavedChanges = true;

            BottomStatusText.Text = $"{_tracks.Count} tracks";

            // Focus the new row's song name cell
            TracksDataGrid.UpdateLayout();
            TracksDataGrid.SelectedItem = newTrack;
            TracksDataGrid.ScrollIntoView(newTrack);
        }

        private void TracksDataGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Delete && !TracksDataGrid.IsEditing())
            {
                RemoveSelectedTracks();
                e.Handled = true;
            }
        }

        private void RemoveSelectedTracks()
        {
            var selected = TracksDataGrid.SelectedItems.OfType<EditableTrack>().ToList();
            if (selected.Count == 0) return;

            foreach (var track in selected)
            {
                track.PropertyChanged -= Track_PropertyChanged;
                _tracks.Remove(track);
            }

            // Renumber
            for (int i = 0; i < _tracks.Count; i++)
                _tracks[i].Position = i + 1;

            TracksDataGrid.ItemsSource = null;
            TracksDataGrid.ItemsSource = _tracks;
            _hasUnsavedChanges = true;
            BottomStatusText.Text = $"{_tracks.Count} tracks";
        }

        // ===== NORMALIZE =====

        private void NormalizeButton_Click(object sender, RoutedEventArgs e)
        {
            int normalized = 0;
            foreach (var track in _tracks)
            {
                if (string.IsNullOrWhiteSpace(track.SongName)) continue;

                var result = _normalizationService.Normalize(track.SongName);
                if (result != null && result != track.SongName)
                {
                    track.SongName = result;
                    normalized++;
                }
            }

            TracksDataGrid.ItemsSource = null;
            TracksDataGrid.ItemsSource = _tracks;

            StatusText.Text = normalized > 0
                ? $"Normalized {normalized} song{(normalized == 1 ? "" : "s")}"
                : "All songs already normalized";
            _hasUnsavedChanges = _hasUnsavedChanges || normalized > 0;
        }

        private void TracksDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            _hasUnsavedChanges = true;
        }

        // ===== SAVE =====

        public async System.Threading.Tasks.Task SaveChangesAsync()
        {
            // Validate
            var date = DateTextBox.Text.Trim();
            if (!Regex.IsMatch(date, @"^\d{4}-\d{2}-\d{2}$"))
            {
                MessageBox.Show("Date must be in yyyy-MM-dd format.", "Invalid Date",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_tracks.Count == 0)
            {
                MessageBox.Show("Setlist must have at least one song.", "Empty Setlist",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Update the concert reference object
            _concert.Date = date;
            _concert.Venue = VenueTextBox.Text.Trim();

            // Parse city/state from the text box
            var cityState = CityStateTextBox.Text.Trim();
            var parts = cityState.Split(',', 2);
            _concert.City = parts.Length > 0 ? parts[0].Trim() : "";
            _concert.State = parts.Length > 1 ? parts[1].Trim() : "";

            // Rebuild sets and tracks from the editable track list
            var setGroups = _tracks
                .GroupBy(t => t.Set)
                .OrderBy(g => GetSetOrder(g.Key));

            _concert.Sets.Clear();
            _concert.Tracks.Clear();
            int position = 0;

            foreach (var group in setGroups)
            {
                var concertSet = new ConcertSet
                {
                    Name = group.Key,
                    Songs = new List<ConcertSong>()
                };

                foreach (var track in group)
                {
                    position++;
                    var trackDate = !string.IsNullOrEmpty(track.Date) ? track.Date : date;

                    concertSet.Songs.Add(new ConcertSong
                    {
                        Name = track.SongName,
                        Date = trackDate,
                        Segue = track.Segue,
                        Info = track.Info
                    });

                    _concert.Tracks.Add(new ConcertTrack
                    {
                        Position = position,
                        SongName = track.SongName,
                        Date = trackDate,
                        Segue = track.Segue,
                        Set = group.Key
                    });
                }

                _concert.Sets.Add(concertSet);
            }

            _concert.HasSetlist = _concert.Tracks.Count > 0;
            _concert.LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

            // Write to AppData concerts directory (atomic: temp file + rename)
            try
            {
                var concertsDir = ConcertLookupService.AppDataConcertsPath;
                Directory.CreateDirectory(concertsDir);

                var targetPath = Path.Combine(concertsDir, $"{date}.json");
                var tempPath = targetPath + ".tmp";

                var json = JsonConvert.SerializeObject(_concert, Formatting.Indented);

                await System.Threading.Tasks.Task.Run(() =>
                {
                    File.WriteAllText(tempPath, json);
                    if (File.Exists(targetPath))
                        File.Delete(targetPath);
                    File.Move(tempPath, targetPath);
                });

                _hasUnsavedChanges = false;
                StatusText.Text = "Setlist saved";
                Debug.WriteLine($"[CONCERT EDIT] Saved {date} to {targetPath}");

                SaveCompleted?.Invoke(this, EventArgs.Empty);

                // Navigate back to detail view
                _shell.Navigation.GoBack();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving setlist:\n\n{ex.Message}", "Save Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Debug.WriteLine($"[CONCERT EDIT] Save error: {ex}");
            }
        }

        public void CancelEdit()
        {
            if (_hasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Discard them?",
                    "Discard Changes?",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes) return;
            }

            _shell.Navigation.GoBack();
        }

        public void NavigateBack()
        {
            CancelEdit();
        }

        private static int GetSetOrder(string setName)
        {
            return setName switch
            {
                "Set 1" => 0,
                "Set 2" => 1,
                "Set 3" => 2,
                "Encore" => 3,
                "Encore 2" => 4,
                _ => 5
            };
        }
    }

    /// <summary>
    /// Editable track for the setlist editor DataGrid.
    /// </summary>
    public class EditableTrack : INotifyPropertyChanged
    {
        private int _position;
        private string _songName = "";
        private string _date = "";
        private bool _segue;
        private string _set = "Set 1";
        private string _info = "";

        public int Position
        {
            get => _position;
            set { if (_position != value) { _position = value; OnPropertyChanged(nameof(Position)); } }
        }

        public string SongName
        {
            get => _songName;
            set { if (_songName != value) { _songName = value; OnPropertyChanged(nameof(SongName)); OnPropertyChanged(nameof(SegueMarker)); OnPropertyChanged(nameof(DateDisplay)); } }
        }

        public string Date
        {
            get => _date;
            set { if (_date != value) { _date = value; OnPropertyChanged(nameof(Date)); OnPropertyChanged(nameof(DateDisplay)); } }
        }

        public bool Segue
        {
            get => _segue;
            set { if (_segue != value) { _segue = value; OnPropertyChanged(nameof(Segue)); OnPropertyChanged(nameof(SegueMarker)); } }
        }

        public string SegueMarker => Segue ? " >" : "";
        public string DateDisplay => !string.IsNullOrEmpty(Date) ? $" ({Date})" : "";

        public string Set
        {
            get => _set;
            set { if (_set != value) { _set = value; OnPropertyChanged(nameof(Set)); } }
        }

        public string Info
        {
            get => _info;
            set { if (_info != value) { _info = value; OnPropertyChanged(nameof(Info)); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Extension method to check if DataGrid is in editing mode.
    /// </summary>
    public static class DataGridExtensions
    {
        public static bool IsEditing(this DataGrid dataGrid)
        {
            return dataGrid.CommitEdit(DataGridEditingUnit.Row, true) == false;
        }
    }
}
