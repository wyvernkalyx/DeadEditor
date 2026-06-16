using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfDragDropEffects = System.Windows.DragDropEffects;

namespace DeadEditor
{
    /// <summary>
    /// ImportView - Concert Import UserControl for the Shell
    ///
    /// Provides the full import pipeline:
    ///   Select Folder → Read → MusicBrainz → Normalize → Renumber → Write / Import to Library
    ///
    /// State is preserved when the user switches away to Library / Settings and returns.
    /// Fires ImportCompleted when a concert is successfully imported so ShellWindow can
    /// trigger a library refresh.
    /// </summary>
    public partial class ImportView : WpfUserControl
    {
        // ===== EVENTS =====

        /// <summary>Fired after a successful Import to Library. Carries the destination path.</summary>
        public event EventHandler<string>? ImportCompleted;

        // ===== SERVICES =====

        private readonly MetadataService _metadataService;
        private readonly NormalizationService _normalizationService;
        private readonly LibraryImportService _libraryImportService;
        private readonly MusicBrainzService _musicBrainzService;
        private LibrarySettings _librarySettings;

        // ===== DATA =====

        private ObservableCollection<TrackInfoViewModel> _tracks = new();
        private AlbumInfo? _albumInfo;
        private string? _currentFolderPath;
        private bool _isUpdating = false;
        private TaskCompletionSource<bool>? _notificationResult;

        // ===== MATCH SETLIST STATE (for Match to Song dialog) =====

        /// <summary>Flattened setlist songs from the last Match Setlist run.</summary>
        private List<(string Name, string Canonical, int Position, bool Segue)>? _lastSetlistSongs;
        /// <summary>Indices into _lastSetlistSongs that have been claimed by matched tracks.</summary>
        private HashSet<int>? _lastClaimedPositions;
        /// <summary>The date used for the last Match Setlist run.</summary>
        private string? _lastMatchDate;

        /// <summary>Pre-populated concert metadata for click-to-import from Concerts view.</summary>
        private (string Date, string Venue, string CityState)? _prePopulatedConcert;

        // ===== WORKFLOW STEPPER STATE =====

        /// <summary>
        /// Cached "any source file carried an MBID at folder-load time, OR a
        /// MusicBrainz lookup has been applied this session." Drives the Enrich
        /// stage. Updated on folder-load (one-shot file scan) and flipped to
        /// true after a successful MusicBrainz apply. Reset by ClearView.
        /// </summary>
        private bool _mbidPresentInSource;

        /// <summary>
        /// Backing collection for the workflow stepper. Recomputed by
        /// <see cref="RefreshStepper"/> at folder-load, post-MusicBrainz,
        /// post-Normalize, post-Match-Setlist, post-Import, and on ClearView.
        /// Per-cell edits do not refresh — stages are too coarse-grained for
        /// keystroke updates to matter.
        /// </summary>
        private readonly ObservableCollection<DeadEditor.Models.WorkflowStage> _stages = new();

        // ===== PLAYBACK =====

        // ===== CONSTRUCTOR =====

        public ImportView()
        {
            InitializeComponent();

            _metadataService = new MetadataService();
            _normalizationService = new NormalizationService();
            _librarySettings = LibrarySettings.Load();
            _musicBrainzService = new MusicBrainzService("asa4wLQhwJ", _librarySettings);
            _libraryImportService = new LibraryImportService(_metadataService, _musicBrainzService);

            TracksDataGrid.ItemsSource = _tracks;
            WorkflowStepperControl.Stages = _stages;
            RefreshStepper();
        }

        // ===== PUBLIC API =====

        /// <summary>
        /// Called by ShellWindow's HeaderBar folder picker so the folder path
        /// can also appear in the header bar. Loads the folder.
        /// </summary>
        public void LoadFolderFromPath(string folderPath)
        {
            _currentFolderPath = folderPath;
            _ = LoadFolderAsync(folderPath);
        }

        /// <summary>Called by the Header Bar "Select Folder…" button.</summary>
        public void BrowseForFolder()
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select folder containing audio files (FLAC/MP3)"
            };

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _currentFolderPath = dlg.SelectedPath;
                _ = LoadFolderAsync(dlg.SelectedPath);

                // Notify header bar about the new path
                FolderPathSelected?.Invoke(this, dlg.SelectedPath);
            }
        }

        /// <summary>
        /// Raised when the user picks a folder so the HeaderBar can display the path.
        /// </summary>
        public event EventHandler<string>? FolderPathSelected;

        /// <summary>True if tracks are loaded but not yet imported to library.</summary>
        public bool HasUnsavedWork => _tracks.Count > 0;

        /// <summary>
        /// Pre-populates import fields from a concert reference and loads a folder.
        /// Called from ShellWindow when user imports via Concerts view.
        /// Folder parsing overrides pre-populated values (folder parsing wins).
        /// </summary>
        public void ImportForConcert(string folderPath, string date, string venue, string cityState)
        {
            ClearView();
            _prePopulatedConcert = (date, venue, cityState);
            _currentFolderPath = folderPath;
            _ = LoadFolderAsync(folderPath);

            // Notify header bar about the new path
            FolderPathSelected?.Invoke(this, folderPath);
        }

        // ===== FOLDER LOADING =====

        private async Task LoadFolderAsync(string folderPath)
        {
            try
            {
                StatusTextBlock.Text = "Reading files...";

                var trackList = _metadataService.ReadFolder(folderPath, editMode: false);

                if (trackList.Count == 0)
                {
                    // Multi-disc guidance: if folder has subfolders, explain why it won't import
                    bool hasSubfolders = false;
                    try { hasSubfolders = Directory.GetDirectories(folderPath).Length > 0; } catch { }

                    if (hasSubfolders)
                    {
                        await ShowNotificationAsync("No Audio Files Found",
                            "No audio files found in this folder. This folder contains subfolders that may be individual discs.\n\n" +
                            "To import, either:\n" +
                            "• Select each disc folder separately and import as separate albums\n" +
                            "• Combine the disc contents into a single folder in File Explorer, then import the combined folder");
                    }
                    else
                    {
                        await ShowNotificationAsync("No Files",
                            "No audio files (FLAC/MP3) found in the selected folder.");
                    }
                    StatusTextBlock.Text = "No audio files found";
                    return;
                }

                // One-shot MBID scan of source files. Drives the Enrich stepper
                // stage. Bounded to once per folder load — stays in sync with
                // the existing read I/O cost of MetadataService.ReadFolder.
                _mbidPresentInSource = false;
                foreach (var t in trackList)
                {
                    try
                    {
                        var mbid = MbidModalHelper.ReadMbidFromFile(t.FilePath);
                        if (!string.IsNullOrWhiteSpace(mbid))
                        {
                            _mbidPresentInSource = true;
                            break;
                        }
                    }
                    catch { /* per-file failure is non-fatal for a heuristic */ }
                }

                // Read album info from folder / FLAC tags
                _albumInfo = _metadataService.ReadAlbumInfo(folderPath, trackList, out var typeFromTag);

                // Load-time Type fallback: if the source had no ALBUMTYPE tag,
                // run a single inference pass against the populated fields. After
                // this point, the combobox is the source of truth for Type — text
                // edits no longer re-assert it.
                if (!typeFromTag)
                {
                    _albumInfo.Type = InferAlbumType();
                }

                // Set album date on each track for display fallback (playlist, etc.)
                if (!string.IsNullOrEmpty(_albumInfo.AlbumDate))
                {
                    foreach (var track in trackList)
                    {
                        track.AlbumDate = _albumInfo.AlbumDate;
                    }
                }

                // Multi-night sort logic
                var distinctDates = trackList
                    .Where(t => !string.IsNullOrEmpty(t.TrackDate))
                    .Select(t => t.TrackDate)
                    .Distinct()
                    .Count();

                bool hasDiscNumbers = trackList.Any(t => t.DiscNumber > 1);

                if (distinctDates > 1)
                {
                    trackList = trackList
                        .OrderBy(t => string.IsNullOrEmpty(t.TrackDate) ? "9999-99-99" : t.TrackDate)
                        .ThenBy(t => t.DiscNumber)
                        .ThenBy(t => t.TrackNumber)
                        .ToList();
                }
                else if (hasDiscNumbers)
                {
                    trackList = trackList
                        .OrderBy(t => t.DiscNumber)
                        .ThenBy(t => t.TrackNumber)
                        .ToList();
                }
                else
                {
                    trackList = trackList.OrderBy(t => t.TrackNumber).ToList();
                }

                // Auto-fill venue/city/state from shows.json if fields are empty
                // Only for single-date albums (multi-date is ambiguous)
                if (distinctDates <= 1 && _albumInfo != null
                    && string.IsNullOrEmpty(_albumInfo.Venue) && string.IsNullOrEmpty(_albumInfo.CityState))
                {
                    var lookupDate = _albumInfo.AlbumDate;
                    if (string.IsNullOrEmpty(lookupDate) && distinctDates == 1)
                    {
                        lookupDate = trackList.First(t => !string.IsNullOrEmpty(t.TrackDate)).TrackDate;
                    }

                    var showInfo = ShowLookupService.Instance.GetShowByDate(lookupDate);
                    if (showInfo != null)
                    {
                        _albumInfo.Venue = showInfo.Venue;
                        _albumInfo.CityState = showInfo.FormattedLocation;
                    }
                }

                // Apply pre-populated concert data for any remaining empty fields
                // (folder parsing and ShowLookupService have already had their chance)
                if (_prePopulatedConcert.HasValue && _albumInfo != null)
                {
                    var pp = _prePopulatedConcert.Value;
                    if (string.IsNullOrEmpty(_albumInfo.AlbumDate))
                        _albumInfo.AlbumDate = pp.Date;
                    if (string.IsNullOrEmpty(_albumInfo.Venue))
                        _albumInfo.Venue = pp.Venue;
                    if (string.IsNullOrEmpty(_albumInfo.CityState))
                        _albumInfo.CityState = pp.CityState;
                    _prePopulatedConcert = null;
                }

                // Populate ViewModels
                _tracks.Clear();
                foreach (var track in trackList)
                {
                    _tracks.Add(new TrackInfoViewModel(track, _albumInfo, isEditMode: false));
                }

                // Refresh all bound UI fields
                RefreshUI();

                StatusTextBlock.Text = $"{_tracks.Count} tracks loaded";

                // Update folder path display
                UpdateFolderPathDisplay();
                RefreshStepper();
            }
            catch (Exception ex)
            {
                await ShowNotificationAsync("Error", $"Error loading folder: {ex.Message}");
                StatusTextBlock.Text = "Error loading folder";
            }
        }

        /// <summary>
        /// Recomputes the workflow stepper's stages from current Import view
        /// state and applies the result onto <see cref="_stages"/> in place
        /// (preserving the ObservableCollection so the stepper's INPC wiring
        /// keeps firing). Called from each canonical workflow event hook.
        /// </summary>
        private void RefreshStepper()
        {
            var titles = _tracks.Select(vm => vm.Track.SongName ?? "").ToList();

            var input = new ImportWorkflowInput
            {
                TrackTitles = titles,
                MbidPresent = _mbidPresentInSource,
                CurrentFolderPath = _currentFolderPath,
                LibraryRootPath = _librarySettings?.LibraryRootPath,
                IsCanonicalSongName = name =>
                    !string.IsNullOrEmpty(name) &&
                    _normalizationService.GetOfficialTitle(name) != null
            };

            var fresh = ImportWorkflowState.Compute(input);

            // Update in place. Pad/truncate the existing collection so the
            // stepper control's per-stage PropertyChanged subscriptions remain
            // attached to the same WorkflowStage instances.
            while (_stages.Count < fresh.Count) _stages.Add(new DeadEditor.Models.WorkflowStage());
            while (_stages.Count > fresh.Count) _stages.RemoveAt(_stages.Count - 1);

            for (int i = 0; i < fresh.Count; i++)
            {
                _stages[i].Name = fresh[i].Name;
                _stages[i].IsCompleted = fresh[i].IsCompleted;
                _stages[i].IsCurrent = fresh[i].IsCurrent;
                _stages[i].IsSkipped = fresh[i].IsSkipped;
            }
        }

        private void UpdateFolderPathDisplay()
        {
            if (!string.IsNullOrEmpty(_currentFolderPath))
            {
                FolderPathTextBox.Text = _currentFolderPath;
                FolderPathTextBox.Foreground = new SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#AAAAAA"));
                OpenFolderButton.IsEnabled = true;
                EmptyStatePanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                FolderPathTextBox.Text = "No folder loaded";
                FolderPathTextBox.Foreground = new SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#666666"));
                OpenFolderButton.IsEnabled = false;
                EmptyStatePanel.Visibility = Visibility.Visible;
            }
        }

        private void ReadButton_Click(object sender, RoutedEventArgs e)
        {
            BrowseForFolder();
        }

        // ===== REFRESH / CLEAR =====

        private void RefreshUI()
        {
            _isUpdating = true;

            if (_albumInfo != null)
            {
                ArtistTextBox.Text = _albumInfo.Artist ?? "";
                AlbumDateTextBox.Text = _albumInfo.AlbumDate ?? "";
                VenueTextBox.Text = _albumInfo.Venue ?? "";
                CityStateTextBox.Text = _albumInfo.CityState ?? "";
                AlbumNameTextBox.Text = _albumInfo.AlbumName ?? "";
                YearTextBox.Text = _albumInfo.Year ?? "";

                AlbumTypeComboBox.SelectedIndex = _albumInfo.Type switch
                {
                    AlbumType.AudienceRecording => 1,
                    AlbumType.OfficialRelease => 2,
                    _ => 0
                };

                UpdateArtworkDisplay();
                ViewInfoButton.IsEnabled = !string.IsNullOrEmpty(_albumInfo.InfoFileContent);
            }

            UpdateMatchSetlistButton();
            UpdateAlbumPreview();
            UpdateAllTrackDisplayTitles();

            _isUpdating = false;
        }

        private void ClearView()
        {
            _isUpdating = true;

            _tracks.Clear();
            _albumInfo = null;
            _currentFolderPath = null;
            _mbidPresentInSource = false;
            ArtistTextBox.Text = "";
            AlbumDateTextBox.Text = "";
            VenueTextBox.Text = "";
            CityStateTextBox.Text = "";
            AlbumNameTextBox.Text = "";
            YearTextBox.Text = "";
            AlbumTypeComboBox.SelectedIndex = 0;
            FolderPreviewTextBlock.Text = "";
            WritePathTextBlock.Text = "";
            ViewInfoButton.IsEnabled = false;
            MatchSetlistButton.IsEnabled = false;
            MatchSetlistButton.ToolTip = "No setlist data for this date";
            ImportButton.IsEnabled = true;
            ArtworkImage.Visibility = Visibility.Collapsed;
            NoArtworkText.Visibility = Visibility.Visible;
            StatusTextBlock.Text = "Ready — select a folder to begin";

            UpdateFolderPathDisplay();

            _isUpdating = false;
            RefreshStepper();
        }

        // ===== ALBUM INFO BAR =====

        private void AlbumInfo_Changed(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating || _albumInfo == null) return;

            _albumInfo.Artist = ArtistTextBox.Text;
            _albumInfo.AlbumDate = AlbumDateTextBox.Text;
            _albumInfo.Venue = VenueTextBox.Text;
            _albumInfo.CityState = CityStateTextBox.Text;
            _albumInfo.AlbumName = AlbumNameTextBox.Text;
            _albumInfo.Year = YearTextBox.Text;
            _albumInfo.IsModified = true;

            UpdateAlbumPreview();
            UpdateAllTrackDisplayTitles();
            UpdateAllTrackInheritedDates();

            // Show autocomplete suggestions when Album Name field changes
            if (sender == AlbumNameTextBox && !_isSelectingSuggestion)
            {
                UpdateAlbumNameSuggestions();
            }
        }

        private void AlbumDateTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var date = AlbumDateTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(date) || date.Length != 10) return;
            if (!System.Text.RegularExpressions.Regex.IsMatch(date, @"^\d{4}-\d{2}-\d{2}$")) return;

            var showInfo = ShowLookupService.Instance.GetShowByDate(date);
            if (showInfo == null) return;

            if (string.IsNullOrWhiteSpace(VenueTextBox.Text))
            {
                VenueTextBox.Text = showInfo.Venue;
            }
            if (string.IsNullOrWhiteSpace(CityStateTextBox.Text))
            {
                CityStateTextBox.Text = showInfo.FormattedLocation;
            }

            UpdateMatchSetlistButton();
        }

        // ===== ALBUM NAME AUTOCOMPLETE =====

        private bool _isSelectingSuggestion = false;

        private void UpdateAlbumNameSuggestions()
        {
            var text = AlbumNameTextBox.Text;
            var suggestions = ReleaseLookupService.Instance.GetSuggestions(text);

            if (suggestions.Count > 0)
            {
                AlbumNameSuggestions.ItemsSource = suggestions;
                AlbumNamePopup.IsOpen = true;
            }
            else
            {
                AlbumNamePopup.IsOpen = false;
            }
        }

        private void AlbumNameTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!AlbumNamePopup.IsOpen) return;

            if (e.Key == System.Windows.Input.Key.Down)
            {
                AlbumNameSuggestions.Focus();
                if (AlbumNameSuggestions.Items.Count > 0)
                    AlbumNameSuggestions.SelectedIndex = 0;
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                AlbumNamePopup.IsOpen = false;
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Enter && AlbumNameSuggestions.SelectedItem != null)
            {
                AcceptSuggestion(AlbumNameSuggestions.SelectedItem.ToString()!);
                e.Handled = true;
            }
        }

        private void AlbumNameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // Delay closing so click on suggestion can register
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!AlbumNameSuggestions.IsKeyboardFocusWithin)
                    AlbumNamePopup.IsOpen = false;
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void AlbumNameSuggestions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Don't act on programmatic selection changes
        }

        private void AlbumNameSuggestions_MouseClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (AlbumNameSuggestions.SelectedItem is string selected)
            {
                AcceptSuggestion(selected);
            }
        }

        private void AcceptSuggestion(string name)
        {
            _isSelectingSuggestion = true;
            AlbumNameTextBox.Text = name;
            AlbumNameTextBox.CaretIndex = name.Length;
            AlbumNamePopup.IsOpen = false;
            _isSelectingSuggestion = false;
            AlbumNameTextBox.Focus();
        }

        private void AlbumTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdating || _albumInfo == null) return;

            _albumInfo.Type = AlbumTypeComboBox.SelectedIndex switch
            {
                1 => AlbumType.AudienceRecording,
                2 => AlbumType.OfficialRelease,
                _ => InferAlbumType()
            };

            UpdateAlbumPreview();
        }

        private AlbumType InferAlbumType() => AlbumTypeInference.Infer(_albumInfo);

        private void UpdateAlbumPreview()
        {
            if (_albumInfo == null)
            {
                FolderPreviewTextBlock.Text = "";
                WritePathTextBlock.Text = "";
                return;
            }

            // Build folder name using the same logic as the import service
            var artistFolder = SanitizeFolderName(_albumInfo.Artist ?? "Unknown Artist");
            var albumFolder = _libraryImportService.BuildLibraryFolderName(_albumInfo);
            FolderPreviewTextBlock.Text = albumFolder;

            if (!string.IsNullOrEmpty(_librarySettings.LibraryRootPath))
            {
                WritePathTextBlock.Text = Path.Combine(_librarySettings.LibraryRootPath, artistFolder, albumFolder);
            }
            else
            {
                WritePathTextBlock.Text = "(Library path not set)";
            }
        }

        /// <summary>
        /// Sanitizes a string for use as a Windows folder name.
        /// Matches the logic in LibraryImportService.SanitizeFolderName.
        /// </summary>
        private static string SanitizeFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Unknown";

            name = name.Replace(": ", " - ").Replace(":", "-");

            var invalid = Path.GetInvalidFileNameChars();
            foreach (var c in invalid)
            {
                name = name.Replace(c, '_');
            }

            name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+", " ").Trim();
            name = name.Trim('.', ' ');

            return string.IsNullOrWhiteSpace(name) ? "Unknown" : name;
        }

        // ===== TRACK GRID =====

        private void TracksDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Playback controls are in the PlayerBar — no action needed here
        }

        private void TracksDataGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            // No special action needed
        }

        private void TracksDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                if (e.Row.Item is TrackInfoViewModel track)
                {
                    track.Track.IsModified = true;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        track.UpdateDisplayTitle();
                    }), DispatcherPriority.Background);
                }
            }
        }

        private void TracksDataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.Item is TrackInfoViewModel track && track.Track.IsMatched == false)
            {
                e.Row.Foreground = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xD7, 0xBA, 0x7D));
            }
            else
            {
                e.Row.Foreground = new SolidColorBrush(Colors.White);
            }

            // Attach drag-to-reorder handlers
            e.Row.PreviewMouseLeftButtonDown += Row_PreviewMouseLeftButtonDown;
            e.Row.MouseMove += Row_MouseMove;
            e.Row.Drop += Row_Drop;
            e.Row.DragOver += Row_DragOver;
            e.Row.AllowDrop = true;
        }

        private void TracksDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            if (e.Column.Header?.ToString() == "Disc")
            {
                e.Handled = true;

                ListSortDirection direction = e.Column.SortDirection != ListSortDirection.Ascending
                    ? ListSortDirection.Ascending
                    : ListSortDirection.Descending;

                ICollectionView view = CollectionViewSource.GetDefaultView(TracksDataGrid.ItemsSource);
                view.SortDescriptions.Clear();

                if (direction == ListSortDirection.Ascending)
                {
                    view.SortDescriptions.Add(new SortDescription("DiscNumber", ListSortDirection.Ascending));
                    view.SortDescriptions.Add(new SortDescription("TrackNumber", ListSortDirection.Ascending));
                }
                else
                {
                    view.SortDescriptions.Add(new SortDescription("DiscNumber", ListSortDirection.Descending));
                    view.SortDescriptions.Add(new SortDescription("TrackNumber", ListSortDirection.Ascending));
                }

                e.Column.SortDirection = direction;
            }
        }

        // ===== CONTEXT MENU =====

        private void TracksDataGrid_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Walk up the visual tree to find the DataGridRow under the cursor
            var hit = VisualTreeHelper.HitTest(TracksDataGrid, e.GetPosition(TracksDataGrid));
            if (hit == null) return;

            var element = hit.VisualHit as FrameworkElement;
            while (element != null && element is not DataGridRow)
            {
                element = VisualTreeHelper.GetParent(element) as FrameworkElement;
            }

            if (element is not DataGridRow row) return;
            if (row.Item is not TrackInfoViewModel vm) return;

            var clickedTrack = vm.Track;

            // Build dark-themed context menu (same style as AlbumDetailView)
            var menu = new ContextMenu
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x30)),
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xE0)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3E, 0x3E, 0x42)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2)
            };

            var menuItemStyle = new Style(typeof(MenuItem));
            menuItemStyle.Setters.Add(new Setter(MenuItem.ForegroundProperty,
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xE0))));
            menuItemStyle.Setters.Add(new Setter(MenuItem.PaddingProperty, new Thickness(8, 6, 20, 6)));
            menuItemStyle.Setters.Add(new Setter(MenuItem.FontSizeProperty, 14.0));

            var playNowItem = new MenuItem { Header = "▶  Play Now", Style = menuItemStyle };
            playNowItem.Click += (s, args) =>
            {
                if (!App.PlaybackService.Playlist.Contains(clickedTrack))
                    App.PlaybackService.Playlist.Add(clickedTrack);
                App.PlaybackService.Play(clickedTrack);
            };
            menu.Items.Add(playNowItem);

            var addItem = new MenuItem { Header = "＋  Add to Playlist", Style = menuItemStyle };
            addItem.Click += (s, args) =>
            {
                if (!App.PlaybackService.Playlist.Contains(clickedTrack))
                    App.PlaybackService.Playlist.Add(clickedTrack);
            };
            menu.Items.Add(addItem);

            // "Track Info" — always available for any track
            menu.Items.Add(new Separator
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3E, 0x3E, 0x42))
            });

            var trackInfoItem = new MenuItem { Header = "\U0001F4C4  Track Info", Style = menuItemStyle };
            trackInfoItem.Click += (s, args) =>
            {
                var dialog = new TrackInfoDialog(
                    clickedTrack,
                    artist: ArtistTextBox.Text,
                    album: AlbumNameTextBox?.Text);
                dialog.Owner = Window.GetWindow(this);
                dialog.ShowDialog();
            };
            menu.Items.Add(trackInfoItem);

            // "Match to Song..." — for unmatched tracks after Match Setlist has run
            if (_lastSetlistSongs != null && _lastClaimedPositions != null &&
                _lastMatchDate != null && vm.Track.IsMatched != true)
            {
                // Build list of unclaimed setlist songs
                var unmatchedSongs = BuildUnmatchedSetlistSongList();
                if (unmatchedSongs.Count > 0)
                {
                    menu.Items.Add(new Separator
                    {
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3E, 0x3E, 0x42))
                    });

                    var matchItem = new MenuItem { Header = "\U0001F3B5  Match to Song...", Style = menuItemStyle };
                    matchItem.Click += (s, args) => MatchToSong_Click(vm);
                    menu.Items.Add(matchItem);
                }
            }

            menu.IsOpen = true;
            e.Handled = true;
        }

        /// <summary>
        /// Builds a list of setlist songs that have not yet been matched to tracks.
        /// Each entry includes the setlist index and a display label like "Song Name (Set 2, #3)".
        /// </summary>
        private List<(int SetlistIndex, string DisplayLabel)> BuildUnmatchedSetlistSongList()
        {
            var result = new List<(int, string)>();
            if (_lastSetlistSongs == null || _lastClaimedPositions == null || _lastMatchDate == null)
                return result;

            var setlist = ShowLookupService.Instance.GetSetlist(_lastMatchDate);
            if (setlist == null) return result;

            for (int i = 0; i < _lastSetlistSongs.Count; i++)
            {
                if (_lastClaimedPositions.Contains(i)) continue;

                // Find which set and position within set this song belongs to
                var discTrack = ShowLookupService.Instance.GetDiscTrack(_lastMatchDate, _lastSetlistSongs[i].Position);
                string setLabel;
                if (discTrack != null && discTrack.Value.Disc <= setlist.Count)
                {
                    var setInfo = setlist[discTrack.Value.Disc - 1];
                    setLabel = $"{setInfo.Label}, #{discTrack.Value.Track}";
                }
                else
                {
                    setLabel = $"#{i + 1}";
                }

                result.Add((i, $"{_lastSetlistSongs[i].Name} ({setLabel})"));
            }

            return result;
        }

        /// <summary>
        /// Handles "Match to Song..." context menu click. Shows dialog, decorates
        /// the track with the canonical song title and segue flag from the
        /// chosen setlist song, and learns the track's prior title as an alias.
        /// Disc and track numbers are NOT touched — the audio's position in the
        /// recording is the archival truth.
        /// </summary>
        private void MatchToSong_Click(TrackInfoViewModel vm)
        {
            if (_lastSetlistSongs == null || _lastClaimedPositions == null || _lastMatchDate == null)
                return;

            var unmatchedSongs = BuildUnmatchedSetlistSongList();
            if (unmatchedSongs.Count == 0) return;

            // Get the cleaned track title (what normalization would have produced, or the raw name)
            var cleanedTitle = vm.Track.SongName ?? "";

            var dialog = new MatchToSongDialog(cleanedTitle, unmatchedSongs);
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() != true || dialog.SelectedSetlistIndex < 0)
                return;

            var selectedIndex = dialog.SelectedSetlistIndex;
            var selectedSong = _lastSetlistSongs[selectedIndex];

            // Model B: decorate only — set canonical SongName, segue, and matched
            // flag. Do NOT touch DiscNumber/TrackNumber. The audio's position in
            // the recording stays where it was.
            vm.Track.SongName = selectedSong.Canonical;
            vm.Track.IsMatched = true;
            vm.Track.IsModified = true;
            if (selectedSong.Segue)
                vm.Track.Segue = true;

            // Mark this position as claimed
            _lastClaimedPositions.Add(selectedIndex);

            // Auto-add alias to songs.json so future imports of the same variant
            // match automatically.
            var officialTitle = selectedSong.Canonical;
            var aliasCandidate = cleanedTitle.Trim();
            if (!string.IsNullOrEmpty(aliasCandidate) && !string.IsNullOrEmpty(officialTitle))
            {
                _normalizationService.AddAlias(officialTitle, aliasCandidate);
            }

            int matchCount = _lastClaimedPositions.Count;
            int unmatchedCount = _tracks.Count - matchCount;
            int segueCount = _tracks.Count(t => t.Track.Segue);
            var segueMsg = segueCount > 0 ? $", {segueCount} segues" : "";
            var unmatchedMsg = unmatchedCount > 0
                ? $". {unmatchedCount} unmatched — right-click to match manually."
                : "";
            StatusTextBlock.Text = $"Matched {matchCount} of {_tracks.Count} tracks to setlist{segueMsg}{unmatchedMsg}";

            TracksDataGrid.Items.Refresh();
        }

        // ===== DRAG-TO-REORDER =====

        private System.Windows.Point _dragStartPoint;
        private TrackInfoViewModel? _draggedItem;
        private bool _isDragging;

        private void Row_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGridRow row)
            {
                _dragStartPoint = e.GetPosition(null);
                _draggedItem = row.Item as TrackInfoViewModel;
                _isDragging = false;
            }
        }

        private void Row_MouseMove(object sender, WpfMouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedItem != null && !_isDragging)
            {
                System.Windows.Point current = e.GetPosition(null);
                System.Windows.Vector diff = _dragStartPoint - current;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDragging = true;
                    if (sender is DataGridRow row)
                        DragDrop.DoDragDrop(row, _draggedItem, WpfDragDropEffects.Move);
                    _isDragging = false;
                }
            }
        }

        private void Row_DragOver(object sender, WpfDragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(TrackInfoViewModel)))
            {
                e.Effects = WpfDragDropEffects.Move;

                if (sender is DataGridRow targetRow && _draggedItem != null)
                {
                    var targetItem = targetRow.Item as TrackInfoViewModel;
                    if (targetItem != null && targetItem != _draggedItem)
                        UpdateDropIndicator(targetRow);
                }
            }
            else
            {
                e.Effects = WpfDragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Row_Drop(object sender, WpfDragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(TrackInfoViewModel)) && sender is DataGridRow targetRow)
            {
                var targetItem = targetRow.Item as TrackInfoViewModel;
                if (targetItem != null && _draggedItem != null && targetItem != _draggedItem)
                {
                    int draggedIndex = _tracks.IndexOf(_draggedItem);
                    int targetIndex = _tracks.IndexOf(targetItem);

                    if (draggedIndex >= 0 && targetIndex >= 0)
                    {
                        _tracks.RemoveAt(draggedIndex);
                        _tracks.Insert(targetIndex, _draggedItem);
                        TracksDataGrid.SelectedItem = _draggedItem;
                    }
                }
            }

            HideDropIndicator();
            e.Handled = true;
        }

        private void UpdateDropIndicator(DataGridRow targetRow)
        {
            try
            {
                var position = targetRow.TranslatePoint(new System.Windows.Point(0, 0), TracksDataGrid);
                DropIndicator.Visibility = Visibility.Visible;
                DropIndicator.Margin = new Thickness(5, position.Y, 5, 0);
            }
            catch
            {
                HideDropIndicator();
            }
        }

        private void HideDropIndicator()
        {
            DropIndicator.Visibility = Visibility.Collapsed;
        }

        private void UpdateAllTrackDisplayTitles()
        {
            foreach (var t in _tracks) t.UpdateDisplayTitle();
        }

        private void UpdateAllTrackInheritedDates()
        {
            foreach (var t in _tracks) t.UpdateInheritedDate();
        }

        // ===== ACTION BUTTONS =====

        private async void NormalizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_tracks.Count == 0)
            {
                await ShowNotificationAsync("No Files", "No tracks loaded to normalize.");
                return;
            }

            try
            {
                StatusTextBlock.Text = "Normalizing...";

                var trackList = _tracks.Select(t => t.Track).ToList();
                int matched = _normalizationService.NormalizeAll(trackList);

                TracksDataGrid.Items.Refresh();

                StatusTextBlock.Text = $"Matched {matched} of {_tracks.Count} songs";

                int unmatched = _tracks.Count - matched;
                if (unmatched > 0)
                {
                    var unmatchedTracks = _tracks
                        .Where(t => t.Track.IsMatched == false)
                        .Select(t => t.Track)
                        .ToList();

                    // Find the parent window for dialog ownership
                    var parentWindow = Window.GetWindow(this);
                    var dialog = new UnmatchedSongsDialog(unmatchedTracks, _normalizationService)
                    {
                        Owner = parentWindow
                    };

                    bool applied = dialog.ShowDialog() == true;

                    if (applied && dialog.ChangesMade)
                    {
                        TracksDataGrid.Items.Refresh();
                        int nowMatched = _tracks.Count(t => t.Track.IsMatched == true);
                        StatusTextBlock.Text = $"Corrections applied. Matched {nowMatched} of {_tracks.Count} songs";
                    }
                    else
                    {
                        await ShowNotificationAsync("Normalization Complete",
                            $"{unmatched} songs not in database (will use original titles). Unmatched songs shown in gold.");
                    }

                    // Ruling 6 hand-off: the dialog notifies via the shell banner AFTER it closes
                    // (#13 Info / #14 Error). Both outcomes are handled here.
                    if (dialog.SongsAddedCount > 0)
                        App.Alerts.Notify(
                            $"Added {dialog.SongsAddedCount} new song{(dialog.SongsAddedCount == 1 ? "" : "s")} to the song database.",
                            AlertSeverity.Info, "Songs Added");
                    if (dialog.ApplyErrorMessage != null)
                        App.Alerts.Notify(
                            $"Error applying corrections:\n\n{dialog.ApplyErrorMessage}",
                            AlertSeverity.Error, "Error");
                }

                RefreshStepper();
            }
            catch (Exception ex)
            {
                await ShowNotificationAsync("Error", $"Error normalizing: {ex.Message}");
                StatusTextBlock.Text = "Normalization error";
            }
        }

        private void RenumberButton_Click(object sender, RoutedEventArgs e)
        {
            if (_tracks.Count == 0) return;

            var tracksByDisc = _tracks
                .GroupBy(t => t.Track.DiscNumber > 0 ? t.Track.DiscNumber : 1)
                .OrderBy(g => g.Key);

            foreach (var discGroup in tracksByDisc)
            {
                int discNumber = discGroup.Key;
                int trackIndex = 1;

                foreach (var tw in discGroup)
                {
                    tw.Track.TrackNumber = (discNumber * 100) + trackIndex;
                    tw.Track.IsModified = true;
                    trackIndex++;
                }
            }

            TracksDataGrid.Items.Refresh();
            StatusTextBlock.Text = "Tracks renumbered using disc-aware 101/201/301 convention";
        }

        // ===== MATCH SETLIST =====

        /// <summary>
        /// Updates the Match Setlist button enabled state and tooltip based on
        /// whether setlist data exists for the current album date.
        /// </summary>
        private void UpdateMatchSetlistButton()
        {
            var date = AlbumDateTextBox.Text?.Trim();
            if (!string.IsNullOrEmpty(date) && date.Length == 10
                && System.Text.RegularExpressions.Regex.IsMatch(date, @"^\d{4}-\d{2}-\d{2}$"))
            {
                var setlist = ShowLookupService.Instance.GetSetlist(date);
                if (setlist != null)
                {
                    MatchSetlistButton.IsEnabled = true;
                    int songCount = setlist.Sum(s => s.Songs.Count);
                    MatchSetlistButton.ToolTip = $"Match tracks to setlist ({songCount} songs in {setlist.Count} sets)";
                    return;
                }
            }

            MatchSetlistButton.IsEnabled = false;
            MatchSetlistButton.ToolTip = "No setlist data for this date";
        }

        private async void MatchSetlistButton_Click(object sender, RoutedEventArgs e)
        {
            if (_tracks.Count == 0 || _albumInfo == null)
            {
                await ShowNotificationAsync("No Tracks", "No tracks loaded to match.");
                return;
            }

            var date = AlbumDateTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(date)) return;

            var setlist = ShowLookupService.Instance.GetSetlist(date);
            if (setlist == null)
            {
                await ShowNotificationAsync("No Setlist", "No setlist data available for this date.");
                return;
            }

            // Flatten setlist into ordered list with global position. The
            // canonical name is what we compare against — setlist song names
            // are run through GetOfficialTitle so aliases collapse to their
            // canonical form before comparison.
            var setlistSongs = new List<(string Name, string Canonical, int Position, bool Segue)>();
            var matcherSetlist = new List<SetlistMatcher.SetlistEntry>();
            int pos = 0;
            foreach (var set in setlist)
            {
                foreach (var song in set.Songs)
                {
                    var canonical = _normalizationService.GetOfficialTitle(song.Name) ?? song.Name;
                    setlistSongs.Add((song.Name, canonical, pos, song.Segue));
                    matcherSetlist.Add(new SetlistMatcher.SetlistEntry
                    {
                        Name = song.Name,
                        Canonical = canonical,
                        Position = pos,
                        Segue = song.Segue,
                    });
                    pos++;
                }
            }

            // Model B: matched tracks get SongName/Segue/IsMatched decoration;
            // unmatched tracks are left entirely untouched (DiscNumber and
            // TrackNumber preserved). The audio's position in the recording is
            // the archival truth.
            var trackList = _tracks.Select(vm => vm.Track).ToList();
            var result = SetlistMatcher.MatchAndDecorate(
                trackList,
                matcherSetlist,
                trackName =>
                {
                    var normalized = _normalizationService.Normalize(trackName) ?? trackName;
                    return _normalizationService.GetOfficialTitle(normalized) ?? normalized;
                });

            // Store state for the Match to Song manual-match dialog (right-click
            // on unmatched tracks).
            _lastSetlistSongs = setlistSongs;
            _lastClaimedPositions = result.ClaimedPositions;
            _lastMatchDate = date;

            TracksDataGrid.Items.Refresh();

            int matchCount = result.MatchedCount;
            int unmatchedCount = _tracks.Count - matchCount;
            if (matchCount == 0)
            {
                StatusTextBlock.Text = $"No tracks matched the setlist ({setlistSongs.Count} songs)";
            }
            else
            {
                var segueMsg = result.SegueCount > 0 ? $", {result.SegueCount} segues" : "";
                var unmatchedMsg = unmatchedCount > 0
                    ? $". {unmatchedCount} unmatched — right-click to match manually."
                    : "";
                StatusTextBlock.Text = $"Matched {matchCount} of {_tracks.Count} tracks to setlist{segueMsg}{unmatchedMsg}";
            }

            RefreshStepper();
        }

        private async void MusicBrainzButton_Click(object sender, RoutedEventArgs e)
        {
            if (_tracks.Count == 0)
            {
                await ShowNotificationAsync("No Tracks", "No tracks loaded. Please select a folder first.");
                return;
            }

            // Decide between MBID-driven re-match and fingerprinting.
            // The "Re-fingerprint" checkbox forces fingerprinting for this session
            // even when source files already carry MBID tags.
            bool forceFingerprint = ReFingerprintCheckBox.IsChecked == true;

            if (!forceFingerprint)
            {
                var perTrackMbids = _tracks
                    .Select(vm =>
                    {
                        try { return MbidModalHelper.ReadMbidFromFile(vm.Track.FilePath); }
                        catch { return null; }
                    })
                    .ToList();

                var modal = MbidModalHelper.ComputeModal(perTrackMbids);
                if (modal.AnyPresent && !string.IsNullOrEmpty(modal.ModalMbid))
                {
                    await LookupByExistingMbidAsync(modal.ModalMbid, modal.AllAgree);
                    return;
                }
            }

            await LookupByFingerprintAsync();
        }

        private async Task LookupByFingerprintAsync()
        {
            try
            {
                MusicBrainzButton.IsEnabled = false;
                ProgressBar.Visibility = Visibility.Visible;
                ProgressBar.IsIndeterminate = true;
                StatusTextBlock.Text = "Looking up album on MusicBrainz...";

                var trackList = _tracks.Select(t => t.Track).ToList();
                var releases = await _musicBrainzService.LookupAllReleasesAsync(trackList, trackList.Count);

                ProgressBar.Visibility = Visibility.Collapsed;
                MusicBrainzButton.IsEnabled = true;

                if (releases == null || releases.Count == 0)
                {
                    await ShowNotificationAsync("Not Found",
                        "Could not identify this album using audio fingerprinting.\n\n" +
                        "Possible reasons:\n" +
                        "• fpcalc.exe not configured (check Settings)\n" +
                        "• Album not in MusicBrainz database\n" +
                        "• Audio fingerprints don't match\n\n" +
                        "Please enter the information manually.");
                    StatusTextBlock.Text = "Album lookup failed";
                    return;
                }

                var selector = new ReleaseSelectorDialog("Select MusicBrainz Release", releases);
                if (selector.ShowDialog() == true && selector.SelectedRelease != null)
                {
                    await ApplyMusicBrainzData(selector.SelectedRelease);
                }
                else
                {
                    StatusTextBlock.Text = "Album lookup cancelled";
                }
            }
            catch (Exception ex)
            {
                ProgressBar.Visibility = Visibility.Collapsed;
                MusicBrainzButton.IsEnabled = true;
                await ShowNotificationAsync("Error", $"Error looking up album: {ex.Message}");
                StatusTextBlock.Text = "Album lookup error";
            }
        }

        private async Task LookupByExistingMbidAsync(string mbid, bool allAgree)
        {
            try
            {
                MusicBrainzButton.IsEnabled = false;
                ProgressBar.Visibility = Visibility.Visible;
                ProgressBar.IsIndeterminate = true;

                var shortMbid = mbid.Length >= 8 ? mbid.Substring(0, 8) : mbid;
                StatusTextBlock.Text = allAgree
                    ? $"Looking up MBID {shortMbid}…"
                    : $"⚠ Tracks have inconsistent MBIDs; using {shortMbid}…";

                var tracks = await _musicBrainzService.GetReleaseTracksAsync(mbid);

                ProgressBar.Visibility = Visibility.Collapsed;
                MusicBrainzButton.IsEnabled = true;

                var stub = new ReleaseOption
                {
                    ReleaseId = mbid,
                    Title = _albumInfo?.AlbumName ?? "",
                    Artist = _albumInfo?.Artist ?? "",
                    Year = _albumInfo?.Year ?? "",
                    TotalTrackCount = tracks?.Count
                };

                var selector = new ReleaseSelectorDialog("Confirm MusicBrainz Release", new List<ReleaseOption> { stub });
                if (selector.ShowDialog() == true && selector.SelectedRelease != null)
                {
                    await ApplyMusicBrainzData(selector.SelectedRelease);
                }
                else
                {
                    StatusTextBlock.Text = "MBID lookup cancelled";
                }
            }
            catch (Exception ex)
            {
                ProgressBar.Visibility = Visibility.Collapsed;
                MusicBrainzButton.IsEnabled = true;
                await ShowNotificationAsync("Error", $"Error looking up MBID: {ex.Message}");
                StatusTextBlock.Text = "MBID lookup error";
            }
        }

        private async Task ApplyMusicBrainzData(ReleaseOption release)
        {
            try
            {
                StatusTextBlock.Text = "Fetching release tracks...";

                var tracks = await _musicBrainzService.GetReleaseTracksAsync(release.ReleaseId);

                _isUpdating = true;
                ArtistTextBox.Text = release.Artist;
                AlbumNameTextBox.Text = release.Title;
                YearTextBox.Text = release.Year.ToString();
                // A MusicBrainz hit IS an official release by definition.
                // Set Type explicitly here so the import flow doesn't fall
                // through to AudienceRecording validation (which would
                // demand a performance date the studio album doesn't have).
                AlbumTypeComboBox.SelectedIndex = 2;
                _isUpdating = false;

                if (_albumInfo != null)
                {
                    _albumInfo.Artist = release.Artist;
                    _albumInfo.AlbumName = release.Title;
                    _albumInfo.Year = release.Year.ToString();
                    _albumInfo.Type = AlbumType.OfficialRelease;
                    _albumInfo.MusicBrainzReleaseId = release.ReleaseId;
                }

                // Download artwork
                if (!string.IsNullOrEmpty(release.ArtworkUrl))
                {
                    try
                    {
                        using var httpClient = new System.Net.Http.HttpClient();
                        var artworkData = await httpClient.GetByteArrayAsync(release.ArtworkUrl);
                        if (artworkData != null && artworkData.Length > 0 && _albumInfo != null)
                        {
                            _albumInfo.ArtworkData = artworkData;
                            _albumInfo.ArtworkMimeType = "image/jpeg";
                            UpdateArtworkDisplay();
                        }
                    }
                    catch
                    {
                        // Artwork download failed — not critical
                    }
                }

                // Match track titles by disc + position
                if (tracks != null && tracks.Count > 0)
                {
                    int matchedCount = 0;

                    foreach (var localTrack in _tracks)
                    {
                        var mbTrack = tracks.FirstOrDefault(t =>
                            t.DiscNumber == localTrack.DiscNumber &&
                            t.Position == localTrack.TrackNumber);

                        if (mbTrack != null)
                        {
                            var (cleanName, mbSegue, _) = _metadataService.ParseTitleAndDate(mbTrack.Title, _albumInfo?.AlbumDate);
                            localTrack.Track.SongName = cleanName;
                            localTrack.Track.HasSegue = mbSegue;
                            localTrack.Track.IsMatched = true;
                            localTrack.Track.IsModified = true;
                            localTrack.UpdateDisplayTitle();
                            matchedCount++;
                        }
                    }

                    string confidence = (matchedCount == _tracks.Count && _tracks.Count == tracks.Count)
                        ? "High" : "Medium";
                    StatusTextBlock.Text =
                        $"MusicBrainz: Found {release.Title} ({release.Year}) — " +
                        $"{matchedCount}/{_tracks.Count} tracks matched (confidence: {confidence})";
                }
                else
                {
                    StatusTextBlock.Text =
                        $"MusicBrainz: Found {release.Title} ({release.Year}) — No track data";
                }

                TracksDataGrid.Items.Refresh();
                UpdateAlbumPreview();

                // We just applied an MBID-driven release. Mark the source as
                // enriched and refresh the stepper so Enrich flips to Completed.
                _mbidPresentInSource = true;
                RefreshStepper();
            }
            catch (Exception ex)
            {
                await ShowNotificationAsync("Error", $"Error applying MusicBrainz data: {ex.Message}");
                StatusTextBlock.Text = "Error applying MusicBrainz data";
            }
        }

        private async void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_tracks.Count == 0 || _albumInfo == null)
            {
                await ShowNotificationAsync("No Files", "No tracks loaded to import.");
                return;
            }

            // Reload settings in case the user updated the library path in Settings
            _librarySettings = LibrarySettings.Load();

            if (string.IsNullOrEmpty(_librarySettings.LibraryRootPath))
            {
                await ShowNotificationAsync("Library Not Set",
                    "Library path not set. Please configure it in Settings.");
                return;
            }

            // Validate required fields before importing
            if (_albumInfo.Type == AlbumType.AudienceRecording &&
                string.IsNullOrWhiteSpace(_albumInfo.Date))
            {
                await ShowNotificationAsync("Missing Date",
                    "Please enter a performance date for this audience recording.");
                return;
            }

            // Duplicate check
            bool exists = _libraryImportService.ShowExistsInLibrary(
                _librarySettings.LibraryRootPath,
                _albumInfo);

            if (exists)
            {
                bool overwrite = await ShowNotificationAsync("Show Already Exists",
                    $"This album ({_albumInfo.AlbumTitle}) already exists in your library.\n\nOverwrite?",
                    showYesNo: true);

                if (!overwrite) return;
            }

            string destination = WritePathTextBlock.Text;
            bool confirmed = await ShowNotificationAsync("Confirm Import",
                $"Import {_tracks.Count} tracks to library?\n\nDestination:\n{destination}",
                showYesNo: true);

            if (!confirmed) return;

            try
            {
                ImportButton.IsEnabled = false;
                StatusTextBlock.Text = "Importing to library...";
                ProgressBar.Visibility = Visibility.Visible;
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Value = 0;

                var progress = new Progress<(int current, int total, string status)>(report =>
                {
                    ProgressBar.Value = (report.current * 100.0) / report.total;
                    StatusTextBlock.Text = report.status;
                });

                var trackList = _tracks.Select(t => t.Track).ToList();

                // Capture source folder path and conflict callback for background thread.
                // Uses ManualResetEventSlim to avoid deadlock: Dispatcher.InvokeAsync shows the panel,
                // background thread blocks on the event, button clicks signal the event.
                var sourcePath = _currentFolderPath;
                ConflictPromptCallback conflictCallback = (fileName, sourceFilePath, context) =>
                {
                    ConflictResult? result = null;
                    var waitHandle = new ManualResetEventSlim(false);

                    Dispatcher.InvokeAsync(() =>
                    {
                        _conflictResult = new TaskCompletionSource<ConflictResult>();
                        ConflictMessage.Text = $"{context}:\n\n\"{fileName}\"\n\nSource: {sourceFilePath}";
                        ConflictApplyToAllCheckBox.IsChecked = false;
                        ConflictPanel.Visibility = Visibility.Visible;

                        _conflictResult.Task.ContinueWith(t =>
                        {
                            result = t.Result;
                            waitHandle.Set();
                        });
                    });

                    waitHandle.Wait();
                    return result!;
                };

                await Task.Run(() =>
                {
                    _libraryImportService.ImportToLibrary(
                        _librarySettings.LibraryRootPath,
                        _albumInfo,
                        trackList,
                        progress,
                        sourcePath,
                        conflictCallback);
                });

                ProgressBar.Visibility = Visibility.Collapsed;
                // Build final status combining audio tracks and any non-audio files
                var lastStatus = StatusTextBlock.Text;
                var audioMsg = $"Imported {_tracks.Count} tracks";
                if (lastStatus.Contains("additional file"))
                    StatusTextBlock.Text = $"{audioMsg}. {lastStatus}";
                else
                    StatusTextBlock.Text = $"Successfully {audioMsg.ToLower()}";

                // Remember box set name
                if (_albumInfo.Type == AlbumType.OfficialRelease &&
                    !string.IsNullOrEmpty(_albumInfo.AlbumName))
                {
                    _librarySettings.LastBoxSetName = _albumInfo.AlbumName;
                    _librarySettings.Save();
                }

                await ShowNotificationAsync("Import Complete",
                    $"Successfully imported {_tracks.Count} tracks to library.\n\nDestination:\n{destination}\n\nSwitch to Library to see the new album.");

                // Notify ShellWindow
                ImportCompleted?.Invoke(this, destination);

                // Reset UI for the next import
                ClearView();
            }
            catch (IOException ex)
            {
                ProgressBar.Visibility = Visibility.Collapsed;
                ImportButton.IsEnabled = true;
                System.Diagnostics.Debug.WriteLine($"[IMPORT ERROR] IOException: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[IMPORT ERROR] Source: {ex.Source}");
                System.Diagnostics.Debug.WriteLine($"[IMPORT ERROR] Stack: {ex.StackTrace}");
                await ShowNotificationAsync("Import Failed",
                    $"Import failed: {ex.Message}\n\n" +
                    "This usually means a file is locked by another process. " +
                    "Try closing any other apps that might have the files open (media players, file explorers, etc.).");
                StatusTextBlock.Text = $"Import failed: {ex.Message}";
            }
            catch (Exception ex)
            {
                ProgressBar.Visibility = Visibility.Collapsed;
                ImportButton.IsEnabled = true;
                System.Diagnostics.Debug.WriteLine($"[IMPORT ERROR] {ex.GetType().Name}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[IMPORT ERROR] Stack: {ex.StackTrace}");
                await ShowNotificationAsync("Import Failed",
                    $"Error importing to library:\n\n{ex.Message}");
                StatusTextBlock.Text = $"Import failed: {ex.Message}";
            }
        }

        private void ViewInfoButton_Click(object sender, RoutedEventArgs e)
        {
            if (_albumInfo == null || string.IsNullOrEmpty(_albumInfo.InfoFileContent))
                return;

            // Bucket-C read panel (alert-system-spec.md Ruling 3, #33): the whole info .txt in a
            // scrollable, selectable, monospace in-window panel — replacing the silent-destroying,
            // non-scrolling, proportional-font MessageBox.
            App.Alerts.ShowReadPanel(
                _albumInfo.InfoFileName ?? "Info File",
                _albumInfo.InfoFileContent);
        }

        // ===== ARTWORK =====

        private void ChangeArtworkButton_Click(object sender, RoutedEventArgs e)
        {
            if (_albumInfo == null) return;

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Album Artwork",
                Filter = "Image Files|*.jpg;*.jpeg;*.png|All Files|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var artworkData = File.ReadAllBytes(dlg.FileName);
                    string mimeType = Path.GetExtension(dlg.FileName).ToLower() switch
                    {
                        ".png" => "image/png",
                        _ => "image/jpeg"
                    };

                    _albumInfo.ArtworkData = artworkData;
                    _albumInfo.ArtworkMimeType = mimeType;
                    _albumInfo.IsModified = true;

                    UpdateArtworkDisplay();
                }
                catch (Exception ex)
                {
                    _ = ShowNotificationAsync("Error", $"Error loading artwork: {ex.Message}");
                }
            }
        }

        private void RemoveArtworkButton_Click(object sender, RoutedEventArgs e)
        {
            if (_albumInfo == null) return;

            _albumInfo.ArtworkData = null;
            _albumInfo.ArtworkMimeType = null;
            _albumInfo.IsModified = true;

            ArtworkImage.Visibility = Visibility.Collapsed;
            NoArtworkText.Visibility = Visibility.Visible;
        }

        private void UpdateArtworkDisplay()
        {
            if (_albumInfo?.ArtworkData != null && _albumInfo.ArtworkData.Length > 0)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = new MemoryStream(_albumInfo.ArtworkData);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();

                    ArtworkImage.Source = bitmap;
                    ArtworkImage.Visibility = Visibility.Visible;
                    NoArtworkText.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    ArtworkImage.Visibility = Visibility.Collapsed;
                    NoArtworkText.Visibility = Visibility.Visible;
                }
            }
            else
            {
                ArtworkImage.Visibility = Visibility.Collapsed;
                NoArtworkText.Visibility = Visibility.Visible;
            }
        }

        // ===== OPEN FOLDER =====

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentFolderPath))
                return;

            try
            {
                Process.Start(new ProcessStartInfo(_currentFolderPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Folder not found: {_currentFolderPath}";
                System.Diagnostics.Debug.WriteLine($"[IMPORT] Open folder failed: {ex.Message}");
            }
        }

        // ===== CONFLICT PANEL =====

        private TaskCompletionSource<ConflictResult>? _conflictResult;

        private void ResolveConflict(ConflictAction action)
        {
            ConflictPanel.Visibility = Visibility.Collapsed;
            _conflictResult?.SetResult(new ConflictResult
            {
                Action = action,
                ApplyToAll = ConflictApplyToAllCheckBox.IsChecked == true
            });
        }

        private void ConflictOverwrite_Click(object sender, RoutedEventArgs e) => ResolveConflict(ConflictAction.Overwrite);
        private void ConflictSkip_Click(object sender, RoutedEventArgs e) => ResolveConflict(ConflictAction.Skip);
        private void ConflictRename_Click(object sender, RoutedEventArgs e) => ResolveConflict(ConflictAction.Rename);
        private void ConflictCancel_Click(object sender, RoutedEventArgs e) => ResolveConflict(ConflictAction.CancelImport);

        // ===== NOTIFICATION PANEL =====

        private async Task<bool> ShowNotificationAsync(string title, string message, bool showYesNo = false)
        {
            _notificationResult = new TaskCompletionSource<bool>();

            NotificationTitle.Text = title;
            NotificationMessage.Text = message;

            if (showYesNo)
            {
                NotificationYesButton.Visibility = Visibility.Visible;
                NotificationNoButton.Visibility = Visibility.Visible;
                NotificationOkButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                NotificationYesButton.Visibility = Visibility.Collapsed;
                NotificationNoButton.Visibility = Visibility.Collapsed;
                NotificationOkButton.Visibility = Visibility.Visible;
            }

            NotificationPanel.Visibility = Visibility.Visible;
            return await _notificationResult.Task;
        }

        private void NotificationYes_Click(object sender, RoutedEventArgs e)
        {
            NotificationPanel.Visibility = Visibility.Collapsed;
            _notificationResult?.SetResult(true);
        }

        private void NotificationNo_Click(object sender, RoutedEventArgs e)
        {
            NotificationPanel.Visibility = Visibility.Collapsed;
            _notificationResult?.SetResult(false);
        }

        private void NotificationOk_Click(object sender, RoutedEventArgs e)
        {
            NotificationPanel.Visibility = Visibility.Collapsed;
            _notificationResult?.SetResult(true);
        }

        private void NotificationPanel_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == NotificationPanel)
            {
                NotificationPanel.Visibility = Visibility.Collapsed;
                _notificationResult?.SetResult(false);
            }
        }
    }
}
