using DeadEditor.Helpers;
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
    ///   Select Folder → Read → Normalize → Renumber → Write / Import to Library
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
        /// <summary>The date used for the last Match Setlist run (the applied-date value; distinct
        /// from the run-completed signal <see cref="_matchSetlistHasRun"/>). Still consumed as a
        /// value by GetSetlist/GetDiscTrack/ShouldPersistCombines and the date-equality re-dim guard.</summary>
        private string? _lastMatchDate;
        /// <summary>
        /// Unified "an applied Match Setlist run has completed this session" signal (spec §7.6a),
        /// the same mechanism Edit uses (<c>EditMetadataView._matchSetlistHasRun</c>). Separates the
        /// run-completed flag from <see cref="_lastMatchDate"/>'s value role, so both surfaces reason
        /// about run-state identically. Reset by <see cref="ClearView"/> (spec §7.6b).
        /// </summary>
        private bool _matchSetlistHasRun;
        /// <summary>
        /// The setlist projection currently shown in the reference side-panel
        /// (reference-side-panel-spec.md §5). Stored so a manual/panel re-dim can reuse it without
        /// rebuilding, and so the match run and date-entry population share one source.
        /// </summary>
        private IReadOnlyList<SetlistEntryVm>? _lastSetlistProjection;
        /// <summary>
        /// Confirmed combines (Count&gt;1 covered runs) captured from the last Match Setlist
        /// confirm, mapped to the persistence model. Carried to the import-commit point so the
        /// alias is persisted only when the album irrevocably commits to the library
        /// (alias-setlists-spec.md §4, Option B) — not at Match-confirm, so an abandoned preview
        /// writes no canonical reference data. Keyed against <see cref="_lastMatchDate"/> via
        /// <see cref="ShouldPersistCombines"/> so a stale match cannot persist for a different album.
        /// </summary>
        private List<AliasEntry>? _lastConfirmedCombines;

        /// <summary>Pre-populated concert metadata for click-to-import from Concerts view.</summary>
        private (string Date, string Venue, string CityState)? _prePopulatedConcert;

        // ===== WORKFLOW STEPPER STATE =====

        /// <summary>
        /// Cached "any source file carried an MBID at folder-load time." Drives the
        /// Enrich stage. Updated on folder-load (one-shot file scan). Reset by ClearView.
        /// </summary>
        private bool _mbidPresentInSource;

        /// <summary>
        /// Backing collection for the workflow stepper. Recomputed by
        /// <see cref="RefreshStepper"/> at folder-load,
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
            _libraryImportService = new LibraryImportService(_metadataService);

            TracksDataGrid.ItemsSource = _tracks;
            WorkflowStepperControl.Stages = _stages;
            RefreshStepper();

            // Reference-panel "Edit setlist ↗" deep-link — the host owns navigation (spec §9).
            SetlistPanel.EditSetlistRequested += OnEditSetlistRequested;
            // Reference-panel double-click assign — the host owns the write (spec §7.5).
            SetlistPanel.AssignRequested += SetlistPanel_AssignRequested;
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

                // Cold-gate the lazy ~2,300-file concert load behind the status overlay before
                // RefreshUI -> UpdateMatchSetlistButton -> GetSetlist makes its first synchronous
                // touch; no-op once warm. Covers every read entry point (Read button, drag-drop,
                // path load) since they all funnel through here. See Helpers/ConcertDataGate.
                await ConcertDataGate.EnsureLoadedAsync();

                // Refresh all bound UI fields
                RefreshUI();

                // Read fills the date programmatically (no LostFocus fires), so drive the reference
                // side-panel from here too (reference-side-panel-spec.md §5.2 addendum). The concert
                // cache was already warmed above, so this is instant.
                await RefreshSetlistPanelAsync();

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
            UpdateAlbumFieldHint();
            UpdateAllTrackDisplayTitles();

            _isUpdating = false;
        }

        // Tooltip for the ALBUM / RELEASE field. Text + type logic live in the shared
        // Helpers.AlbumFieldHint so ImportView and EditMetadataView stay in lockstep.
        private void UpdateAlbumFieldHint()
        {
            if (AlbumNameTextBox == null) return;

            AlbumNameTextBox.ToolTip = Helpers.AlbumFieldHint.ForType(_albumInfo?.Type);
        }

        private void ClearView()
        {
            _isUpdating = true;

            _tracks.Clear();
            _albumInfo = null;
            _currentFolderPath = null;
            _mbidPresentInSource = false;
            // Drop the stashed import-commit combines so a cleared/next import cannot persist them.
            _lastConfirmedCombines = null;
            // Reset the Match Setlist run state so it cannot leak onto a newly loaded album
            // (reference-side-panel-spec.md §7.6b): a stale claimed set would otherwise mis-dim the
            // panel and a stale run signal would offer the right-click Match-to-Song menu on album B
            // before B has its own run.
            _lastSetlistSongs = null;
            _lastClaimedPositions = null;
            _lastMatchDate = null;
            _matchSetlistHasRun = false;
            // Clear the reference side-panel and its stashed projection on view reset.
            _lastSetlistProjection = null;
            SetlistPanel?.SetSetlist(System.Array.Empty<SetlistEntryVm>(), null);
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

        private async void AlbumDateTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var date = AlbumDateTextBox.Text?.Trim();
            if (!string.IsNullOrEmpty(date) && date.Length == 10
                && System.Text.RegularExpressions.Regex.IsMatch(date, @"^\d{4}-\d{2}-\d{2}$"))
            {
                var showInfo = ShowLookupService.Instance.GetShowByDate(date);
                if (showInfo != null)
                {
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
            }

            // Populate/clear the reference side-panel independent of the venue lookup, so a date
            // with a setlist but no shows.json venue entry still shows it (reference-side-panel-spec.md §5.2).
            await RefreshSetlistPanelAsync();
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
            UpdateAlbumFieldHint();
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

            // "Match to Song..." — for unmatched tracks after Match Setlist has run. Gated on the
            // unified run-completed signal (spec §7.6a), equivalent to the prior _lastMatchDate != null
            // check (both are set at apply and reset together in ClearView) but now identical to Edit.
            if (_lastSetlistSongs != null && _lastClaimedPositions != null &&
                _matchSetlistHasRun && vm.Track.IsMatched != true)
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

            // Model B: decorate only via the shared manual-match core — set canonical SongName,
            // segue, matched/modified flags, and claim the position. Do NOT touch DiscNumber/
            // TrackNumber; the audio's position in the recording stays where it was
            // (reference-side-panel-spec.md §7.1).
            var matchResult = ManualMatchApply.Apply(
                vm.Track, _lastClaimedPositions, selectedIndex, selectedSong.Canonical, selectedSong.Segue);
            // Back-reference stamp so a later panel re-assign of this track frees this position (§7.5.1).
            vm.ClaimedSetlistPosition = matchResult.ClaimedPosition;

            // Re-dim the reference panel so the newly claimed entry greys out
            // (reference-side-panel-spec.md §6, §7.1 host hook). Projection is unchanged; only the
            // claimed set grew.
            if (_lastSetlistProjection != null)
                SetlistPanel.SetSetlist(_lastSetlistProjection, _lastClaimedPositions);

            // Auto-add alias to songs.json so future imports of the same variant
            // match automatically.
            var aliasCandidate = matchResult.PreviousSongName.Trim();
            if (!string.IsNullOrEmpty(aliasCandidate) && !string.IsNullOrEmpty(matchResult.Canonical))
            {
                _normalizationService.AddAlias(matchResult.Canonical, aliasCandidate);
            }

            int matchCount = _lastClaimedPositions.Count;
            int unmatchedCount = _tracks.Count - matchCount;
            int segueCount = _tracks.Count(t => t.Track.Segue);
            var segueMsg = segueCount > 0 ? $", {segueCount} segues" : "";
            var unmatchedMsg = unmatchedCount > 0
                ? $". {unmatchedCount} unmatched — assign from the Setlist panel."
                : "";
            StatusTextBlock.Text = $"Matched {matchCount} of {_tracks.Count} tracks to setlist{segueMsg}{unmatchedMsg}";

            // This row just became matched — drop it out of the amber unplaced tint (parity §7.6d).
            RecomputeUnmatchedHighlights();
            TracksDataGrid.Items.Refresh();
        }

        /// <summary>
        /// Recomputes the amber unplaced-row tint for every track (Import parity with Edit, spec
        /// §7.6d). A row goes amber only when Match Setlist has run this session AND the track is
        /// unplaced (<c>IsMatched != true</c>) — never on fresh load. Distinct from the always-on gold
        /// title-unresolved text color (which means "title is not a known canonical", run-independent).
        /// Called after a match run, a right-click Match-to-Song, and a panel assign.
        /// </summary>
        private void RecomputeUnmatchedHighlights()
        {
            foreach (var vm in _tracks)
                vm.ShowUnmatchedWarning = _matchSetlistHasRun && vm.Track.IsMatched != true;
        }

        /// <summary>
        /// Stamps each track VM's <see cref="TrackInfoViewModel.ClaimedSetlistPosition"/> from a Match
        /// Setlist run so a later panel re-assign can free the prior position (§7.5.1). Resets all
        /// stamps first, then applies the run's per-track claims — so a track left unplaced this run
        /// carries no stale stamp.
        /// </summary>
        private void StampRunClaims(SetlistMatcher.MatchResult result)
        {
            foreach (var vm in _tracks)
                vm.ClaimedSetlistPosition = null;
            foreach (var claim in result.ClaimsByTrack)
            {
                foreach (var vm in _tracks)
                {
                    if (ReferenceEquals(vm.Track, claim.Track))
                    {
                        vm.ClaimedSetlistPosition = claim.Position;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Panel double-click assign (reference-side-panel-spec.md §7.5): assign the double-clicked
        /// setlist entry to the grid's selected track. Available whenever the panel is populated,
        /// independent of any match run. Re-assign onto an already-resolved track is allowed and frees
        /// the track's prior position (§7.5.1); claiming an entry another track already holds is refused.
        /// </summary>
        private void SetlistPanel_AssignRequested(int position)
        {
            if (_lastSetlistProjection == null || position < 0 || position >= _lastSetlistProjection.Count)
                return;

            if (TracksDataGrid.SelectedItem is not TrackInfoViewModel vm)
            {
                StatusTextBlock.Text = "Select a track first, then double-click a setlist entry to assign it.";
                return;
            }

            _lastClaimedPositions ??= new HashSet<int>();

            // Refuse stealing an entry a different track already claims (no un-place gesture in v1).
            if (_lastClaimedPositions.Contains(position) && vm.ClaimedSetlistPosition != position)
            {
                StatusTextBlock.Text = "That setlist entry is already placed to another track.";
                return;
            }

            var entry = _lastSetlistProjection[position];
            var result = ManualMatchApply.Reassign(
                vm.Track, _lastClaimedPositions, vm.ClaimedSetlistPosition, position, entry.Canonical, entry.Segue);
            vm.ClaimedSetlistPosition = position;

            SetlistPanel.SetSetlist(_lastSetlistProjection, _lastClaimedPositions);

            var aliasCandidate = result.PreviousSongName.Trim();
            if (!string.IsNullOrEmpty(aliasCandidate) && !string.IsNullOrEmpty(result.Canonical)
                && !string.Equals(aliasCandidate, result.Canonical, StringComparison.OrdinalIgnoreCase))
            {
                _normalizationService.AddAlias(result.Canonical, aliasCandidate);
            }

            RecomputeUnmatchedHighlights();
            TracksDataGrid.Items.Refresh();
            StatusTextBlock.Text = $"Assigned '{entry.Name}' to the selected track.";
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

        // ===== REFERENCE SIDE-PANEL (Setlist) =====

        // The 24px toggle strip and expand/collapse now live inside SetlistReferencePanel itself
        // (spec §3, strip extraction) so Import and Edit share one mechanism — no host-side toggle.

        /// <summary>
        /// Populate the reference side-panel's Setlist tab from the current album date
        /// (reference-side-panel-spec.md §5.2). Called on date LostFocus and post-Read. A malformed
        /// or empty date, or a date with no setlist, clears the tab. Post-match dimming is applied
        /// only when the current date is the one the last match ran against; otherwise the panel is
        /// pure reference (§6). The cold-load guard (§5.3) keeps the UI responsive on a fresh-Import
        /// pre-Read cold hit — instant when warm, the shared status overlay when cold.
        /// </summary>
        private async Task RefreshSetlistPanelAsync()
        {
            if (SetlistPanel == null) return; // defensive: before InitializeComponent completes

            var date = AlbumDateTextBox.Text?.Trim();
            bool wellFormed = !string.IsNullOrEmpty(date) && date.Length == 10
                && System.Text.RegularExpressions.Regex.IsMatch(date, @"^\d{4}-\d{2}-\d{2}$");

            if (!wellFormed)
            {
                _lastSetlistProjection = null;
                SetlistPanel.SetSetlist(System.Array.Empty<SetlistEntryVm>(), null);
                return;
            }

            // Cold-load guard: no-op when warm; on a cold cache surfaces the shared status overlay
            // off the UI thread (scoped reversal of follow-ups.md:300 for panel population, §5.3).
            await ConcertDataGate.EnsureLoadedAsync();

            var setlist = ShowLookupService.Instance.GetSetlist(date!);
            if (setlist == null)
            {
                _lastSetlistProjection = null;
                SetlistPanel.SetSetlist(System.Array.Empty<SetlistEntryVm>(), null);
                return;
            }

            var projection = SetlistProjection.Build(setlist, _normalizationService.GetOfficialTitle);
            _lastSetlistProjection = projection;

            // Dim only when this date is the matched date; a stale claimed set must not dim a
            // different date's setlist (§6 — no invented pre-match claim state).
            var claimed = (_lastMatchDate == date && _lastClaimedPositions != null)
                ? _lastClaimedPositions
                : null;
            SetlistPanel.SetSetlist(projection, claimed);
        }

        /// <summary>
        /// Handle the reference panel's "Edit setlist ↗" deep-link (reference-side-panel-spec.md §9).
        /// Resolves the concert for the current album date and navigates to its detail view, from
        /// which the user enters the existing Edit Setlist flow. A malformed date or a date with no
        /// concert file shows a status message and does NOT navigate. No refresh-on-return hook —
        /// the user re-Reads and Read re-populates the panel (spec §9, decision 8).
        /// </summary>
        private void OnEditSetlistRequested()
        {
            var date = AlbumDateTextBox.Text?.Trim();
            bool wellFormed = !string.IsNullOrEmpty(date) && date.Length == 10
                && System.Text.RegularExpressions.Regex.IsMatch(date, @"^\d{4}-\d{2}-\d{2}$");
            if (!wellFormed)
            {
                StatusTextBlock.Text = "Enter a valid date (yyyy-MM-dd) to edit its setlist.";
                return;
            }

            var concert = ConcertLookupService.Instance.GetConcertByDate(date!);
            if (concert == null)
            {
                StatusTextBlock.Text = $"No concert record for {date} to edit.";
                return;
            }

            // Second arg (libraryShows) is null: Import has no per-date library-shows index like the
            // Concerts list (ConcertDatabaseView._libraryShowsByDate). ConcertDetailView degrades
            // gracefully when null — its library section offers the Import affordance
            // (ConcertDetailView.xaml.cs:215). No refresh-on-return hook is wired (spec §9, dec 8).
            if (Window.GetWindow(this) is ShellWindow shell)
            {
                shell.NavigateToConcertDetail(concert, null);
            }
        }

        /// <summary>
        /// Safety gate for Option B import-commit alias persistence (alias-setlists-spec.md §4).
        /// Returns true iff there are stashed confirmed combines AND the album being committed is the
        /// one that was matched, at the date it was matched against — so a stale match (match album A,
        /// then import a different album B without re-matching) cannot persist A's coverage for B.
        /// Pure + static so the safety-critical logic is unit-testable without WPF.
        /// </summary>
        internal static bool ShouldPersistCombines(
            string? lastMatchDate,
            string? currentAlbumDate,
            IReadOnlyList<AliasEntry>? stash)
        {
            if (stash == null || stash.Count == 0) return false;
            if (string.IsNullOrEmpty(lastMatchDate)) return false;
            return string.Equals(lastMatchDate, currentAlbumDate, StringComparison.Ordinal);
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

            // Flatten setlist into the shared projection (one 0-based global position across all
            // sets; canonical = GetOfficialTitle(name) ?? name so aliases collapse before
            // comparison). The pure SetlistProjection.Build is the single source the reference
            // side-panel also consumes (reference-side-panel-spec.md §5.1); _lastSetlistSongs and
            // matcherSetlist are thin adapters off it, preserving their existing tuple/entry shapes.
            var projection = SetlistProjection.Build(setlist, _normalizationService.GetOfficialTitle);
            var setlistSongs = projection
                .Select(e => (e.Name, e.Canonical, e.Position, e.Segue))
                .ToList();
            var matcherSetlist = projection
                .Select(e => new SetlistMatcher.SetlistEntry
                {
                    Name = e.Name,
                    Canonical = e.Canonical,
                    Position = e.Position,
                    Segue = e.Segue,
                })
                .ToList();

            // Model B: matched tracks get SongName/Segue/IsMatched decoration;
            // unmatched tracks are left entirely untouched (DiscNumber and
            // TrackNumber preserved). The audio's position in the recording is
            // the archival truth.
            var trackList = _tracks.Select(vm => vm.Track).ToList();

            // B2b: route through the preview-before-apply review surface instead
            // of a blind MatchAndDecorate (mirrors Edit). ComputeProposals ->
            // review dialog -> Apply(edited subset). A null result means the user
            // cancelled — the surface protects against the blind segue overwrite.
            var result = MatchReviewRunner.RunReview(
                trackList,
                matcherSetlist,
                // Direct-match resolver: may echo the input on a miss (pass-1 only equality-compares).
                trackName =>
                {
                    var normalized = _normalizationService.Normalize(trackName) ?? trackName;
                    return _normalizationService.GetOfficialTitle(normalized) ?? normalized;
                },
                // Decomposition resolver: same Normalize, but null on unknown (no echo) so the
                // decomposer's atomicity guard + component validation work (slice-3 gate fix).
                trackName =>
                {
                    var normalized = _normalizationService.Normalize(trackName) ?? trackName;
                    return _normalizationService.GetOfficialTitle(normalized);
                },
                Window.GetWindow(this));

            if (result == null)
            {
                // Cancelled: apply nothing. Leave _lastSetlistSongs,
                // _lastClaimedPositions, _lastMatchDate, the grid, the stepper,
                // and prior status untouched — the Import flow stays on its
                // current step with no partial state.
                StatusTextBlock.Text = "Match Setlist cancelled.";
                return;
            }

            // Store state for the Match to Song manual-match dialog (right-click
            // on unmatched tracks).
            _lastSetlistSongs = setlistSongs;
            _lastClaimedPositions = result.ClaimedPositions;
            _lastMatchDate = date;
            _matchSetlistHasRun = true;

            // Feed the reference side-panel and dim the entries the match claimed
            // (reference-side-panel-spec.md §6). The projection is the same one _lastSetlistSongs
            // was adapted from, so panel and match state stay in lockstep.
            _lastSetlistProjection = projection;
            SetlistPanel.SetSetlist(projection, _lastClaimedPositions);

            // Stamp per-track claimed positions for the panel-reassign back-reference (§7.5.1) and
            // paint unplaced tracks amber (Import parity, §7.6d).
            StampRunClaims(result);
            RecomputeUnmatchedHighlights();

            // Stash the confirmed combines (Count>1 covered runs) mapped to the persistence model.
            // These are NOT persisted here — Option B persists at the irrevocable library-commit
            // point (ImportButton_Click), so an abandoned import preview writes no canonical
            // reference data (alias-setlists-spec.md §4).
            _lastConfirmedCombines = result.ConfirmedCombines
                .Select(p => new AliasEntry { CoveredOfficialIndices = p.CoveredEntryIndices })
                .ToList();

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
                    ? $". {unmatchedCount} unmatched — assign from the Setlist panel."
                    : "";
                StatusTextBlock.Text = $"Matched {matchCount} of {_tracks.Count} tracks to setlist{segueMsg}{unmatchedMsg}";
            }

            RefreshStepper();
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

                // ===== Option B: import-commit alias persistence (alias-setlists-spec.md §4) =====
                // The library write above is the irrevocable-commit point; we are past every abandon
                // path. Persist the confirmed combines stashed at Match-confirm as canonical aliases,
                // but ONLY when the album just committed is the one that was matched (and at the date
                // it was matched against) — ShouldPersistCombines guards a stale match from persisting
                // for a different album. A failed/abandoned import never reaches here, so it writes
                // nothing. PersistAliasSetlist is sync + does file I/O, so the loop runs off the UI
                // thread, mirroring the Edit seam.
                if (ShouldPersistCombines(_lastMatchDate, _albumInfo.Date?.Trim(), _lastConfirmedCombines))
                {
                    var combinesToPersist = _lastConfirmedCombines!;
                    var persistDate = _lastMatchDate!;
                    int persistedCount = 0;
                    try
                    {
                        await Task.Run(() =>
                        {
                            foreach (var entry in combinesToPersist)
                            {
                                var outcome = ConcertLookupService.Instance.PersistAliasSetlist(persistDate, entry);
                                switch (outcome)
                                {
                                    case AliasPersistResult.Persisted:
                                        persistedCount++;
                                        break;
                                    case AliasPersistResult.DuplicateNoOp:
                                        break; // idempotent re-confirm — silent, nothing written
                                    case AliasPersistResult.ConcertNotFound:
                                        Debug.WriteLine($"[ALIAS] No concert cached for {persistDate}; combine not persisted.");
                                        break;
                                }
                            }
                        });

                        if (persistedCount > 0)
                            StatusTextBlock.Text += $" ({persistedCount} combine{(persistedCount == 1 ? "" : "s")} recorded)";
                    }
                    catch (Exception ex)
                    {
                        // The service rethrows on a disk-write failure (the in-memory append is rolled
                        // back). Surface it via the import notification convention; do not let it
                        // escape the async void handler or undo the successful library import.
                        Debug.WriteLine($"[ALIAS] Combine persistence failed for {persistDate}: {ex.Message}");
                        StatusTextBlock.Text += " (combine aliases not saved)";
                        await ShowNotificationAsync("Alias Save Failed",
                            $"The import succeeded, but the combined-track aliases could not be saved:\n\n{ex.Message}");
                    }
                }

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

            // Open the info .txt in the OS default editor (reference-side-panel-spec.md §8),
            // replacing the in-window ReadPanelHost scrim so the file can sit in its own window
            // beside the setlist editor. Shell-open + try/catch-to-status mirrors
            // OpenFolderButton_Click. The button is enabled only when an info file exists
            // (ImportView.xaml.cs UpdateUiState → ViewInfoButton.IsEnabled), so the path is present.
            try
            {
                var path = Path.Combine(_albumInfo.FolderPath, _albumInfo.InfoFileName ?? "");
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Could not open info file: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[IMPORT] Open info file failed: {ex.Message}");
            }
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
