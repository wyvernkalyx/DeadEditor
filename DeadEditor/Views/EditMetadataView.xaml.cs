using DeadEditor.Helpers;
using DeadEditor.Models;
using DeadEditor.Services;
using DeadEditor.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfPoint = System.Windows.Point;
using WpfVector = System.Windows.Vector;

namespace DeadEditor
{
    public partial class EditMetadataView : System.Windows.Controls.UserControl
    {
        private readonly ShellWindow _shell;
        private readonly LibraryShow _show;
        private readonly MetadataService _metadataService;
        private readonly NormalizationService _normalizationService;
        private readonly ManifestService _manifestService;

        private AlbumInfo? _albumInfo;
        private ObservableCollection<TrackInfoViewModel> _tracks = new();
        private bool _isUpdating = false;
        private bool _hasUnsavedChanges = false;

        // Re-entrancy guard for the save path. Both entry points — the HeaderBar Save
        // button and VerifyButton_Click — funnel through SaveChangesAsync, so guarding
        // here makes a second (e.g. rapid-click or Verify-while-saving) invocation a
        // no-op. Backstops the app-wide WithFileReleased write gate against the UI ever
        // launching overlapping writes in the first place.
        private bool _isSaving = false;

        // Issues surfaced in the validation banner above the metadata grid.
        // Refreshed on load, cell edits, drag-to-reorder, renumber, and any batch
        // operation that mutates disc/track numbers or song titles.
        private readonly ObservableCollection<string> _validationIssues = new();
        private const int MaxBannerIssues = 5;

        // Artwork editing state
        private enum ArtworkState { Unchanged, Changed, Removed }
        private ArtworkState _artworkState = ArtworkState.Unchanged;
        private byte[]? _newArtworkData;
        private string? _newArtworkMimeType;

        // Match Setlist state
        private bool _matchSetlistHasRun = false;
        private List<(string Name, string Canonical, int Position, bool Segue)>? _lastSetlistSongs;
        private HashSet<int>? _lastClaimedPositions;

        // Drag-to-reorder state
        private WpfPoint _dragStartPoint;
        private TrackInfoViewModel? _draggedItem;
        private bool _isDragging;

        private bool _isVerified;

        // Snapshot of every tracked album-level field's value at load time (and
        // re-captured after a successful save). Drives the amber left-edge
        // marker via EditUnverifyRule.IsDirty(baseline, current). Strict diff
        // — reverting a typo back to the baseline clears the marker.
        // Keys: Artist, Date, Venue, CityState, AlbumName, Year, AlbumType, ArchivistNote.
        private readonly Dictionary<string, string> _baselineValues = new();

        /// <summary>
        /// The album name for the header bar back button text.
        /// </summary>
        public string AlbumName => _show.Type == AlbumType.OfficialRelease ? _show.AlbumName : _show.Venue;

        /// <summary>
        /// Whether there are unsaved changes (for confirmation dialog on cancel/back).
        /// </summary>
        public bool HasUnsavedChanges => _hasUnsavedChanges;

        /// <summary>
        /// Page-level verification flag, surfaced as the sidebar badge. Default false.
        /// </summary>
        public bool IsVerified
        {
            get => _isVerified;
            set
            {
                _isVerified = value;
                UpdateVerificationBadge();
            }
        }

        /// <summary>
        /// Free-text curator context, surfaced as the sidebar textbox. Default "".
        /// </summary>
        public string ArchivistNote
        {
            get => ArchivistNoteTextBox?.Text ?? "";
            set
            {
                if (ArchivistNoteTextBox != null)
                    ArchivistNoteTextBox.Text = value ?? "";
            }
        }

        /// <summary>
        /// Fired when save completes successfully, so the shell can refresh the album detail view.
        /// </summary>
        public event EventHandler? SaveCompleted;

        public EditMetadataView(ShellWindow shell, LibraryShow show)
        {
            InitializeComponent();
            _shell = shell;
            _show = show;
            _metadataService = new MetadataService();
            _normalizationService = new NormalizationService();
            _manifestService = new ManifestService();
            ValidationIssuesItemsControl.ItemsSource = _validationIssues;
        }

        private async void EditMetadataView_Loaded(object sender, RoutedEventArgs e)
        {
            // Cold-gate the lazy ~2,300-file concert load behind the status overlay before LoadData ->
            // RefreshUI -> UpdateMatchSetlistButton -> ShowLookupService.GetSetlist (which sources from
            // ConcertLookupService) makes its first synchronous touch; without it the UI thread would
            // freeze for seconds with no feedback. The scrim also blocks the click-into-not-ready-view
            // window. No-op once warm (silent fast path); load failures are swallowed and bannered so
            // LoadData still runs (GetSetlist degrades to a disabled Match Setlist button). The shared
            // helper owns this idiom verbatim — see Helpers/ConcertDataGate.
            await ConcertDataGate.EnsureLoadedAsync();

            // Warm now (cache is loaded): the TagLib read + UpdateMatchSetlistButton -> GetSetlist is
            // the fast (~sub-second) path. The 25-file read stays synchronous by design.
            LoadData();
        }

        // ===== DATA LOADING =====

        private void LoadData()
        {
            try
            {
                StatusTextBlock.Text = "Reading files...";

                // Read tracks from all folders in edit mode (as-is, no transforms)
                var folders = _show.FolderPaths.Any() ? _show.FolderPaths : new List<string> { _show.FolderPath };
                var rawTracks = new List<TrackInfo>();

                foreach (var folder in folders)
                {
                    if (!Directory.Exists(folder))
                        continue;

                    var folderTracks = _metadataService.ReadFolder(folder, editMode: true);
                    rawTracks.AddRange(folderTracks);
                }

                if (rawTracks.Count == 0)
                {
                    StatusTextBlock.Text = "No audio files found";
                    return;
                }

                // Build AlbumInfo from LibraryShow data (same as MainWindow edit mode)
                // NOTE: Do NOT set OfficialRelease here — it's an alias for AlbumName
                // in AlbumInfo (setter overwrites AlbumName), so setting both would clobber
                // AlbumName with the OfficialRelease value (often empty for audience recordings).
                _albumInfo = new AlbumInfo
                {
                    FolderPath = _show.FolderPath,
                    Type = _show.Type,
                    AlbumDate = _show.Date ?? "",
                    Venue = _show.Venue ?? "",
                    CityState = !string.IsNullOrEmpty(_show.Location) ? _show.Location :
                               (!string.IsNullOrEmpty(_show.City) && !string.IsNullOrEmpty(_show.State) ? $"{_show.City}, {_show.State}" : ""),
                    AlbumName = _show.AlbumName ?? "",
                    Year = _show.ReleaseYear?.ToString() ?? "",
                    Edition = _show.Edition ?? ""
                };

                // Read artist and artwork from first FLAC file
                var firstFile = rawTracks.FirstOrDefault()?.FilePath;
                if (firstFile != null)
                {
                    using (var file = TagLib.File.Create(firstFile))
                    {
                        _albumInfo.Artist = file.Tag.FirstPerformer ?? "";

                        var pictures = file.Tag.Pictures;
                        if (pictures.Length > 0)
                        {
                            _albumInfo.ArtworkData = pictures[0].Data.Data;
                            _albumInfo.ArtworkMimeType = pictures[0].MimeType;
                        }
                    }
                }

                // Fix double-date suffix: ReadFolder(editMode: true) leaves the full FLAC
                // title in SongName (e.g. "Dark Star > (1968-02-23)"). Since DisplayTitle
                // aggregates SongName + TrackDate, we must strip the date from SongName here
                // or it appears twice. See: documentation/double-date-bug-diagnostic-2026-04-25.md
                foreach (var track in rawTracks)
                {
                    var (cleanName, parsedSegue, date) = _metadataService.ParseTitleAndDate(track.SongName, _albumInfo.AlbumDate);
                    track.SongName = cleanName;
                    // Capture the parsed original song name once, before any
                    // Match Setlist / Normalize can change SongName. Set-once on
                    // TrackInfo, so a later manifest override does not move it.
                    track.OriginalTitle = track.SongName;
                    track.HasSegue = parsedSegue;
                    if (!string.IsNullOrEmpty(date) && string.IsNullOrEmpty(track.TrackDate))
                        track.TrackDate = date;
                }

                // Manifest read: if a sidecar manifest exists, its values override the
                // tag-derived defaults for the fields the manifest stores. This is the
                // curation overlay (memo § 7).
                ApplyManifestOverrides(rawTracks);

                // Sort raw tracks BEFORE wrapping in ViewModels (matching ImportView pattern)
                var distinctDates = rawTracks.Where(t => !string.IsNullOrEmpty(t.TrackDate))
                                            .Select(t => t.TrackDate)
                                            .Distinct()
                                            .Count();

                var hasDiscNumbers = rawTracks.Any(t => t.DiscNumber > 1);

                List<TrackInfo> sortedTracks;
                if (distinctDates > 1)
                {
                    sortedTracks = rawTracks.OrderBy(t => string.IsNullOrEmpty(t.TrackDate) ? "9999-99-99" : t.TrackDate)
                                            .ThenBy(t => t.DiscNumber)
                                            .ThenBy(t => t.TrackNumber)
                                            .ToList();
                }
                else if (hasDiscNumbers)
                {
                    sortedTracks = rawTracks.OrderBy(t => t.DiscNumber)
                                            .ThenBy(t => t.TrackNumber)
                                            .ToList();
                }
                else
                {
                    sortedTracks = rawTracks.OrderBy(t => t.TrackNumber).ToList();
                }

                // Wrap sorted tracks in ViewModels with isEditMode: false (aggregated display)
                _tracks.Clear();
                foreach (var track in sortedTracks)
                {
                    var vm = new TrackInfoViewModel(track, _albumInfo!, isEditMode: false);
                    vm.PropertyChanged += ViewModel_PropertyChanged;
                    _tracks.Add(vm);
                }

                // Bind tracks to DataGrid
                TracksDataGrid.ItemsSource = _tracks;

                // Populate album info fields
                RefreshUI();

                // Surface any pre-existing disc/track-number issues (e.g. mis-numbered
                // box sets) immediately on open so the user sees them without editing.
                RefreshValidation();

                StatusTextBlock.Text = $"{_tracks.Count} tracks loaded";
                _hasUnsavedChanges = false;

                // Baseline snapshot: capture current control values so subsequent
                // user edits can be diffed against the loaded state. Must run
                // after RefreshUI so the textboxes reflect _albumInfo +
                // ApplyManifestOverrides.
                CaptureBaseline();
                RecomputeAllMarkers();

                // Initialize the amber tint to all-clear (match has not run yet).
                RecomputeUnmatchedHighlights();

                // Display managed folder path
                var displayPath = _show.FolderPaths.Any() ? _show.FolderPaths.First() : _show.FolderPath;
                FolderPathTextBox.Text = !string.IsNullOrEmpty(displayPath) ? displayPath : "(unknown)";
                OpenFolderButton.IsEnabled = !string.IsNullOrEmpty(displayPath);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Error loading: {ex.Message}";
            }
        }

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

                // Album type dropdown
                AlbumTypeComboBox.SelectedIndex = _albumInfo.Type == AlbumType.OfficialRelease ? 1 : 0;

                // ALBUM / RELEASE field hint switches with the selected type (mirrors ImportView).
                UpdateAlbumFieldHint();

                // Artwork
                LoadArtwork();
            }

            // Fingerprint coverage summary - aggregate over the in-memory tracks
            // (populated at folder-load per fingerprint-persistence-spec \u00a7 2).
            UpdateFingerprintSummary();

            // Enable Match Setlist button if setlist data exists for this date
            UpdateMatchSetlistButton();

            TrackCountText.Text = _tracks.Count == 1 ? "1 track" : $"{_tracks.Count} tracks";

            _isUpdating = false;
        }

        /// <summary>
        /// Refreshes only the FINGERPRINTS sidebar summary. Extracted from RefreshUI so
        /// the Fingerprint button's progress callbacks can update the count live without
        /// triggering RefreshUI's heavier work (artwork decode, setlist lookup).
        /// </summary>
        private void UpdateFingerprintSummary()
        {
            var totalTracks = _tracks.Count;
            var fingerprinted = _tracks.Count(t => !string.IsNullOrWhiteSpace(t.Track.AcoustIdFingerprint));
            if (totalTracks == 0)
            {
                FingerprintSummaryText.Text = "—";
                FingerprintSummaryText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88));
            }
            else if (fingerprinted == 0)
            {
                FingerprintSummaryText.Text = "No fingerprints";
                FingerprintSummaryText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88));
            }
            else if (fingerprinted == totalTracks)
            {
                FingerprintSummaryText.Text = "All tracks fingerprinted";
                FingerprintSummaryText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC));
            }
            else
            {
                FingerprintSummaryText.Text = $"{fingerprinted} / {totalTracks} tracks fingerprinted";
                FingerprintSummaryText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC));
            }
        }

        /// <summary>
        /// Flips the verification badge's text and background to match _isVerified.
        /// Called from the IsVerified setter; safe to call before InitializeComponent
        /// finishes (no-ops if elements are null).
        /// </summary>
        private void UpdateVerificationBadge()
        {
            if (VerificationBadge == null || VerificationBadgeText == null)
                return;

            if (_isVerified)
            {
                VerificationBadge.Background = (SolidColorBrush)FindResource("BadgeVerifiedBg");
                VerificationBadgeText.Text = "✓ Verified";
                VerificationBadgeText.Foreground = (SolidColorBrush)FindResource("BadgeVerifiedFg");
            }
            else
            {
                VerificationBadge.Background = (SolidColorBrush)FindResource("BadgeUnverifiedBg");
                VerificationBadgeText.Text = "Unverified";
                VerificationBadgeText.Foreground = (SolidColorBrush)FindResource("BadgeUnverifiedFg");
            }

            if (VerifyButton != null)
            {
                VerifyButton.Content = _isVerified ? "Unverify" : "Verify";
                VerifyButton.ToolTip = _isVerified
                    ? "Clear the verified flag on this album"
                    : "Mark this album as verified (Date and Artist required)";
            }
        }

        // ===== BASELINE / MARKER / UNVERIFY-ON-EDIT (prompt 6) =====

        /// <summary>
        /// Snapshots the current value of every tracked album-level control into
        /// <see cref="_baselineValues"/>. Called after LoadData populates the form
        /// and after a successful save, so subsequent edits diff against the
        /// load-time (or last-saved) state. See memo § 6 Scenario A.
        /// </summary>
        private void CaptureBaseline()
        {
            _baselineValues["Artist"] = ArtistTextBox.Text ?? "";
            _baselineValues["Date"] = AlbumDateTextBox.Text ?? "";
            _baselineValues["Venue"] = VenueTextBox.Text ?? "";
            _baselineValues["CityState"] = CityStateTextBox.Text ?? "";
            _baselineValues["AlbumName"] = AlbumNameTextBox.Text ?? "";
            _baselineValues["Year"] = YearTextBox.Text ?? "";
            _baselineValues["AlbumType"] = (_albumInfo?.Type.ToString()) ?? "";
            _baselineValues["ArchivistNote"] = ArchivistNoteTextBox.Text ?? "";
        }

        /// <summary>
        /// Recomputes the dirty-marker visibility for every tracked album-level
        /// control by comparing its current value to the captured baseline.
        /// Cheap (eight comparisons); called from every tracked edit handler.
        /// No-op until the baseline has been captured.
        /// </summary>
        private void RecomputeAllMarkers()
        {
            if (_baselineValues.Count == 0) return;

            SetTextBoxMarker(ArtistTextBox, "Artist");
            SetTextBoxMarker(AlbumDateTextBox, "Date");
            SetTextBoxMarker(VenueTextBox, "Venue");
            SetTextBoxMarker(CityStateTextBox, "CityState");
            SetTextBoxMarker(AlbumNameTextBox, "AlbumName");
            SetTextBoxMarker(YearTextBox, "Year");
            SetTextBoxMarker(ArchivistNoteTextBox, "ArchivistNote");

            var typeBaseline = _baselineValues.TryGetValue("AlbumType", out var tb) ? tb : "";
            var typeCurrent = _albumInfo?.Type.ToString() ?? "";
            AlbumTypeMarkerBorder.Visibility = EditUnverifyRule.IsDirty(typeBaseline, typeCurrent)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void SetTextBoxMarker(System.Windows.Controls.TextBox tb, string fieldName)
        {
            var baseline = _baselineValues.TryGetValue(fieldName, out var b) ? b : "";
            EditMarkers.SetIsDirty(tb, EditUnverifyRule.IsDirty(baseline, tb.Text ?? ""));
        }

        /// <summary>
        /// Recomputes the amber unmatched-row tint for every track. A row goes amber
        /// only when Match Setlist has run this session AND the track was left
        /// unplaced (<c>IsMatched != true</c>) — never on fresh load, where every
        /// track is unmatched. Mirrors the <see cref="RecomputeAllMarkers"/> pattern;
        /// called after a match runs and after a manual Match-to-Song so a
        /// just-matched row drops out of amber.
        /// </summary>
        private void RecomputeUnmatchedHighlights()
        {
            foreach (var vm in _tracks)
            {
                vm.ShowUnmatchedWarning = _matchSetlistHasRun && vm.Track.IsMatched != true;
            }
        }

        /// <summary>
        /// If the album is currently verified and the caller represents an edit
        /// to a manifest-tracked field (anything other than the Archivist Note),
        /// drops the verified flag and writes the status-line message. Once
        /// IsVerified is false, subsequent calls are a no-op — so the "first
        /// edit only" message dedupe falls out naturally.
        /// Memo § 5 (unverify-on-edit) + § 6 Scenario A (status-line message).
        /// </summary>
        private void MaybeUnverifyAlbumEdit()
        {
            if (!IsVerified) return;
            IsVerified = false;
            StatusTextBlock.Text = "Verification cleared by edit";
        }

        /// <summary>
        /// Writes the manifest for the current album folder, carrying the in-memory
        /// IsVerified flag and ArchivistNote. Called by SaveChangesAsync after tag
        /// writes succeed, and by VerifyButton_Click when the user toggles state.
        /// User-visible error on failure; tag writes are not unwound.
        /// </summary>
        private void WriteManifestForCurrentAlbum()
        {
            if (_albumInfo == null) return;
            var folder = GetPrimaryAlbumFolder();
            if (string.IsNullOrEmpty(folder)) return;

            try
            {
                var saveTrackList = _tracks.Select(t => t.Track).ToList();
                _manifestService.WriteManifest(folder, _albumInfo, saveTrackList, IsVerified, ArchivistNote);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MANIFEST] Write failed for {folder}: {ex}");
                App.Alerts.Notify(
                    $"Tag changes were saved, but the manifest sidecar could not be written:\n\n{ex.Message}\n\nSave again to retry the manifest write.",
                    AlertSeverity.Warning,
                    "Manifest write failed");
            }
        }

        /// <summary>
        /// Returns the primary album folder for manifest read/write. Multi-folder box
        /// sets only get a manifest on their first folder in MVP — replication across
        /// folders is a known follow-up (memo § Deferred).
        /// </summary>
        private string? GetPrimaryAlbumFolder()
        {
            if (_show.FolderPaths != null && _show.FolderPaths.Count > 1)
            {
                Debug.WriteLine($"[MANIFEST] Multi-folder album ({_show.FolderPaths.Count} folders); writing manifest only for first folder: {_show.FolderPaths[0]}");
                return _show.FolderPaths[0];
            }
            if (_show.FolderPaths != null && _show.FolderPaths.Count == 1)
                return _show.FolderPaths[0];
            return string.IsNullOrEmpty(_show.FolderPath) ? null : _show.FolderPath;
        }

        /// <summary>
        /// Reads the manifest for the primary album folder and overlays its values onto
        /// _albumInfo (album-level fields) and the raw tracks (track-level fields matched
        /// by filename). No-op if no manifest exists or read fails.
        /// </summary>
        private void ApplyManifestOverrides(List<TrackInfo> rawTracks)
        {
            if (_albumInfo == null) return;

            var folder = GetPrimaryAlbumFolder();
            if (string.IsNullOrEmpty(folder)) return;

            AlbumManifest? manifest;
            try
            {
                manifest = _manifestService.ReadManifest(folder);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MANIFEST] Read failed for {folder}: {ex.Message}");
                return;
            }
            if (manifest == null) return;

            // Album-level overrides
            if (!string.IsNullOrEmpty(manifest.AlbumName)) _albumInfo.AlbumName = manifest.AlbumName;
            if (!string.IsNullOrEmpty(manifest.Artist)) _albumInfo.Artist = manifest.Artist;
            if (!string.IsNullOrEmpty(manifest.Date)) _albumInfo.AlbumDate = manifest.Date;
            if (!string.IsNullOrEmpty(manifest.Venue)) _albumInfo.Venue = manifest.Venue;
            if (!string.IsNullOrEmpty(manifest.Edition)) _albumInfo.Edition = manifest.Edition;
            if (!string.IsNullOrEmpty(manifest.Year)) _albumInfo.Year = manifest.Year;

            // City/State are derived from CityState on AlbumInfo. Compose if the manifest carries either.
            if (!string.IsNullOrEmpty(manifest.City) || !string.IsNullOrEmpty(manifest.State))
            {
                _albumInfo.CityState = (!string.IsNullOrEmpty(manifest.City) && !string.IsNullOrEmpty(manifest.State))
                    ? $"{manifest.City}, {manifest.State}"
                    : (manifest.City ?? manifest.State ?? "");
            }

            if (Enum.TryParse<AlbumType>(manifest.AlbumType, out var parsedType))
                _albumInfo.Type = parsedType;

            // Verification state + curator note. The ArchivistNote assignment
            // sets the TextBox's Text and would fire ArchivistNoteTextBox_TextChanged;
            // _isUpdating guards against that.
            IsVerified = manifest.Verified;
            _isUpdating = true;
            try
            {
                ArchivistNote = manifest.ArchivistNote ?? "";
            }
            finally
            {
                _isUpdating = false;
            }

            // Track-level overrides matched by the composite RelativePath (exact for multi-folder
            // albums), falling back to bare filename for legacy manifests. The resolver is
            // duplicate-tolerant, so a legacy multi-folder bare-name collision no longer throws the
            // ToDictionary it used to (it self-heals on next save, which writes relativePath).
            var resolveOverride = ManifestService.BuildOverrideResolver(manifest);

            foreach (var track in rawTracks)
            {
                if (string.IsNullOrEmpty(track.FilePath)) continue;
                var mt = resolveOverride(track.FilePath);
                if (mt == null) continue;

                track.SongName = mt.SongName ?? "";
                track.TrackDate = mt.TrackDate ?? "";
                track.Segue = mt.Segue;
                if (!string.IsNullOrEmpty(mt.AcoustIdFingerprint))
                    track.AcoustIdFingerprint = mt.AcoustIdFingerprint;
                if (!string.IsNullOrEmpty(mt.Title))
                    track.RawTitle = mt.Title;
            }
        }

        private void LoadArtwork()
        {
            if (_albumInfo?.ArtworkData != null)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = new System.IO.MemoryStream(_albumInfo.ArtworkData);
                    bitmap.EndInit();

                    AlbumArtImage.Source = bitmap;
                    AlbumArtImage.Visibility = Visibility.Visible;
                    ArtworkPlaceholder.Visibility = Visibility.Collapsed;
                    return;
                }
                catch { }
            }

            // Try file-based artwork
            var folders = _show.FolderPaths.Any() ? _show.FolderPaths : new List<string> { _show.FolderPath };
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder)) continue;

                var artworkPath = Path.Combine(folder, "cover.jpg");
                if (!File.Exists(artworkPath))
                    artworkPath = Path.Combine(folder, "folder.jpg");

                if (File.Exists(artworkPath))
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.UriSource = new Uri(artworkPath, UriKind.Absolute);
                        bitmap.EndInit();

                        AlbumArtImage.Source = bitmap;
                        AlbumArtImage.Visibility = Visibility.Visible;
                        ArtworkPlaceholder.Visibility = Visibility.Collapsed;
                        return;
                    }
                    catch { }
                }
            }

            // No artwork found
            AlbumArtImage.Source = null;
            AlbumArtImage.Visibility = Visibility.Collapsed;
            ArtworkPlaceholder.Visibility = Visibility.Visible;
        }

        // ===== ALBUM INFO CHANGE TRACKING =====

        private void AlbumInfo_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_isUpdating || _albumInfo == null) return;

            // Sync text fields back to AlbumInfo
            _albumInfo.Artist = ArtistTextBox.Text;
            _albumInfo.AlbumDate = AlbumDateTextBox.Text;
            _albumInfo.Venue = VenueTextBox.Text;
            _albumInfo.CityState = CityStateTextBox.Text;
            _albumInfo.AlbumName = AlbumNameTextBox.Text;
            _albumInfo.Year = YearTextBox.Text;
            _albumInfo.IsModified = true;

            _hasUnsavedChanges = true;

            // Prompt 6: amber marker on the edited field + unverify-on-edit if
            // the album was verified. All six album-info fields are manifest-
            // tracked, so any of them flipping triggers unverify.
            RecomputeAllMarkers();
            MaybeUnverifyAlbumEdit();

            // Show autocomplete suggestions when Album Name field changes
            if (sender == AlbumNameTextBox && !_isSelectingSuggestion)
            {
                UpdateAlbumNameSuggestions();
            }
        }

        private void AlbumTypeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isUpdating || _albumInfo == null) return;

            _albumInfo.Type = AlbumTypeComboBox.SelectedIndex == 1
                ? AlbumType.OfficialRelease
                : AlbumType.AudienceRecording;
            _albumInfo.IsModified = true;
            _hasUnsavedChanges = true;

            UpdateAlbumFieldHint();
            RecomputeAllMarkers();
            MaybeUnverifyAlbumEdit();
        }

        // ALBUM / RELEASE field tooltip switches with the view's TYPE selection: audience → a
        // source/taper hint, official → the known-release search hint. Shared text + logic live in
        // Helpers.AlbumFieldHint so this stays identical to ImportView.
        private void UpdateAlbumFieldHint()
        {
            if (AlbumNameTextBox == null) return;

            AlbumNameTextBox.ToolTip = Helpers.AlbumFieldHint.ForType(_albumInfo?.Type);
        }

        /// <summary>
        /// TextChanged on the Archivist Note. The note is manifest-tracked and
        /// gets the amber marker like other fields, but per memo § 2 / § 5 its
        /// edits do NOT trigger unverify — the note carries curator context
        /// about the recording, not the recording's metadata. Also closes the
        /// pre-prompt-6 gap where edits to this field didn't set
        /// _hasUnsavedChanges (so cancel-confirm would have lost them silently).
        /// </summary>
        private void ArchivistNoteTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_isUpdating) return;

            _hasUnsavedChanges = true;
            RecomputeAllMarkers();
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
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!AlbumNameSuggestions.IsKeyboardFocusWithin)
                    AlbumNamePopup.IsOpen = false;
            }), System.Windows.Threading.DispatcherPriority.Background);
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

        // ===== ARTWORK EDITING =====

        private void ChangeArtworkButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Album Artwork",
                Filter = "Image Files|*.jpg;*.jpeg;*.png|JPEG|*.jpg;*.jpeg|PNG|*.png",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _newArtworkData = File.ReadAllBytes(dialog.FileName);
                    _newArtworkMimeType = dialog.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                        ? "image/png" : "image/jpeg";
                    _artworkState = ArtworkState.Changed;
                    _hasUnsavedChanges = true;

                    // Preview the new artwork
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = new MemoryStream(_newArtworkData);
                    bitmap.EndInit();

                    AlbumArtImage.Source = bitmap;
                    AlbumArtImage.Visibility = Visibility.Visible;
                    ArtworkPlaceholder.Visibility = Visibility.Collapsed;

                    StatusTextBlock.Text = "Artwork changed (save to apply)";
                }
                catch (Exception ex)
                {
                    StatusTextBlock.Text = $"Error loading image: {ex.Message}";
                }
            }
        }

        private void RemoveArtworkButton_Click(object sender, RoutedEventArgs e)
        {
            _newArtworkData = null;
            _newArtworkMimeType = null;
            _artworkState = ArtworkState.Removed;
            _hasUnsavedChanges = true;

            AlbumArtImage.Source = null;
            AlbumArtImage.Visibility = Visibility.Collapsed;
            ArtworkPlaceholder.Visibility = Visibility.Visible;

            StatusTextBlock.Text = "Artwork removed (save to apply)";
        }

        // ===== SAVE / CANCEL =====

        /// <summary>
        /// Save changes: writes metadata to FLAC files, then navigates back.
        /// Called from HeaderBar "Save Changes" button.
        /// </summary>
        public async Task SaveChangesAsync()
        {
            if (_tracks.Count == 0 || _albumInfo == null)
            {
                StatusTextBlock.Text = "No tracks to save";
                return;
            }

            // Re-entrancy guard: a save already in flight makes any second invocation
            // (rapid Save clicks, or Verify firing while a save runs) a no-op, so the UI
            // can never launch overlapping FLAC writes. WithFileReleased serializes at the
            // file layer regardless; this stops the duplicate work earlier and feeds the
            // button-disable below.
            if (_isSaving) return;
            _isSaving = true;

            try
            {
                StatusTextBlock.Text = "Writing metadata to files...";
                ProgressBar.Visibility = Visibility.Visible;
                ProgressBar.IsIndeterminate = true;

                // Apply artwork changes to AlbumInfo before writing
                if (_artworkState == ArtworkState.Changed && _newArtworkData != null)
                {
                    _albumInfo.ArtworkData = _newArtworkData;
                    _albumInfo.ArtworkMimeType = _newArtworkMimeType;
                }
                else if (_artworkState == ArtworkState.Removed)
                {
                    _albumInfo.ArtworkData = null;
                    _albumInfo.ArtworkMimeType = null;
                }

                // Write metadata to FLAC files IN-PLACE (no copy, no new folder)
                var folders = _show.FolderPaths.Any() ? _show.FolderPaths : new List<string> { _show.FolderPath };
                var saveTrackList = _tracks.Select(t => t.Track).ToList();
                await Task.Run(() =>
                {
                    // Release the NAudio handle if the loaded track is among the files being
                    // written, so TagLib can take exclusive write access. One release window
                    // covers BOTH passes (tag write + MBID write): the MBID pass re-opens the
                    // same files and would silently fail if the player reloaded between passes.
                    // No-ops when none of the edited files is currently loaded.
                    App.PlaybackService.WithFileReleased(
                        saveTrackList.Select(t => t.FilePath),
                        () =>
                        {
                            _metadataService.WriteMetadata(_albumInfo, saveTrackList);

                            // Write MBID to all tracks if set
                            WriteMbidToTracks();
                        });

                    // Handle cover.jpg file alongside FLAC tag artwork
                    foreach (var folder in folders)
                    {
                        if (!Directory.Exists(folder)) continue;

                        if (_artworkState == ArtworkState.Changed && _newArtworkData != null)
                        {
                            // Save as cover.jpg in album folder
                            var coverPath = Path.Combine(folder, "cover.jpg");
                            File.WriteAllBytes(coverPath, _newArtworkData);
                        }
                        else if (_artworkState == ArtworkState.Removed)
                        {
                            // Delete cover.jpg and folder.jpg if they exist
                            var coverPath = Path.Combine(folder, "cover.jpg");
                            var folderJpg = Path.Combine(folder, "folder.jpg");
                            if (File.Exists(coverPath)) File.Delete(coverPath);
                            if (File.Exists(folderJpg)) File.Delete(folderJpg);
                        }
                    }
                });

                // Diagnostic: verify tags were actually written to disk
                if (_tracks.Count > 0)
                {
                    var testPath = _tracks[0].Track.FilePath;
                    Debug.WriteLine($"[SAVE VERIFY] Verifying: {testPath}");
                    using var testFile = TagLib.File.Create(testPath);

                    Debug.WriteLine($"[SAVE VERIFY] ALBUM = '{testFile.Tag.Album}'");

                    if (testFile.GetTag(TagLib.TagTypes.Xiph) is TagLib.Ogg.XiphComment xiph)
                    {
                        var venue = xiph.GetFirstField("VENUE");
                        var cityState = xiph.GetFirstField("CITYSTATE");
                        var albumDate = xiph.GetFirstField("ALBUMDATE");
                        var albumName = xiph.GetFirstField("ALBUMNAME");
                        var albumType = xiph.GetFirstField("ALBUMTYPE");
                        Debug.WriteLine($"[SAVE VERIFY] VENUE = '{venue}'");
                        Debug.WriteLine($"[SAVE VERIFY] CITYSTATE = '{cityState}'");
                        Debug.WriteLine($"[SAVE VERIFY] ALBUMDATE = '{albumDate}'");
                        Debug.WriteLine($"[SAVE VERIFY] ALBUMNAME = '{albumName}'");
                        Debug.WriteLine($"[SAVE VERIFY] ALBUMTYPE = '{albumType}'");
                    }

                    if (testFile.GetTag(TagLib.TagTypes.Id3v2) is TagLib.Id3v2.Tag id3Tag)
                    {
                        foreach (var frame in id3Tag.GetFrames<TagLib.Id3v2.UserTextInformationFrame>())
                        {
                            Debug.WriteLine($"[SAVE VERIFY] TXXX: '{frame.Description}' = '{string.Join("; ", frame.Text)}'");
                        }
                    }
                }

                // Update the LibraryShow object in-place so the library grid
                // reflects the edited fields without creating a duplicate entry.
                // The folder path doesn't change — only the cached display fields do.
                var typeChanged = _show.Type != _albumInfo.Type;
                _show.Type = _albumInfo.Type;
                _show.TypeFromTag = true;
                _show.AlbumName = _albumInfo.AlbumName ?? "";
                _show.Venue = _albumInfo.Venue ?? "";
                _show.Date = _albumInfo.AlbumDate ?? "";
                _show.Edition = _albumInfo.Edition ?? "";
                _show.OfficialRelease = _albumInfo.OfficialRelease ?? "";

                if (!string.IsNullOrEmpty(_albumInfo.CityState))
                {
                    var parts = _albumInfo.CityState.Split(new[] { ", " }, 2, StringSplitOptions.None);
                    _show.City = parts.Length > 0 ? parts[0] : "";
                    _show.State = parts.Length > 1 ? parts[1] : "";
                    _show.Location = _albumInfo.CityState;
                }

                if (int.TryParse(_albumInfo.Year, out var year))
                {
                    _show.ReleaseYear = year;
                }

                // Re-cache track titles for search
                _show.TrackTitles = _tracks
                    .Where(t => !string.IsNullOrEmpty(t.Track.SongName))
                    .Select(t => t.Track.SongName!)
                    .ToList();

                // Manifest write: overlay the in-memory verified flag + archivist note
                // onto a fresh manifest sidecar. Tag writes already succeeded above; a
                // manifest write failure surfaces as a user-visible error but does not
                // unwind the tag changes.
                WriteManifestForCurrentAlbum();

                ProgressBar.Visibility = Visibility.Collapsed;
                StatusTextBlock.Text = $"Saved {_tracks.Count} files";
                _hasUnsavedChanges = false;

                // Prompt 6: recapture baseline so any post-save edit diffs
                // against the just-saved values. RecomputeAllMarkers clears
                // every marker since current == baseline. SaveChangesAsync
                // navigates back below so the user typically doesn't see the
                // cleared state, but the recapture matters if the navigation
                // is suppressed in any future flow.
                CaptureBaseline();
                RecomputeAllMarkers();

                // Notify that save completed (so library can refresh)
                SaveCompleted?.Invoke(this, EventArgs.Empty);

                // If album type changed, warn about potential folder move
                if (typeChanged)
                {
                    var newTypeName = _albumInfo.Type == AlbumType.OfficialRelease
                        ? "Official Release" : "Audience Recording";
                    App.Alerts.Notify(
                        $"Album type changed to {newTypeName}.\n\n" +
                        "The tags have been updated, but the files remain in their current folder. " +
                        "You may need to move the folder to your " +
                        (_albumInfo.Type == AlbumType.OfficialRelease ? "Official Releases" : "Library Root") +
                        " path for correct library organization.",
                        AlertSeverity.Warning,
                        "Album Type Changed");
                }

                // Navigate back to album detail
                _shell.Navigation.GoBack();
            }
            catch (Exception ex)
            {
                ProgressBar.Visibility = Visibility.Collapsed;
                StatusTextBlock.Text = $"Save failed: {ex.Message}";
                App.Alerts.Notify($"Error saving changes:\n\n{ex.Message}", AlertSeverity.Error, "Save Failed");
            }
            finally
            {
                _isSaving = false;
            }
        }

        /// <summary>
        /// Cancel: discard changes and navigate back.
        /// Called from HeaderBar "Cancel" button.
        /// </summary>
        public async void CancelEdit()
        {
            // Bucket-B confirm (alert-system-spec.md #25). Branch mapping preserved from the old
            // YesNo/Question MessageBox: Yes (true) -> discard + GoBack; No/Esc (false) -> stay.
            // async void mirrors the existing UI-handler pattern in this file; the HeaderBar
            // Cancel/Back click handlers call this fire-and-forget (nothing runs after it), so the
            // signature change is contained — NavigateBack() still delegates here unchanged.
            if (_hasUnsavedChanges)
            {
                bool discard = await App.Alerts.ConfirmAsync(
                    "You have unsaved changes. Discard them?",
                    "Discard Changes");

                if (!discard) return;
            }

            _shell.Navigation.GoBack();
        }

        /// <summary>
        /// Navigate back (with unsaved changes confirmation).
        /// Called from HeaderBar back button.
        /// </summary>
        public void NavigateBack()
        {
            CancelEdit();
        }

        // ===== TRACK GRID EDITING =====

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Track any property change (Segue checkbox, etc.) as unsaved
            if (_isUpdating) return;

            if (e.PropertyName == nameof(TrackInfoViewModel.Segue) ||
                e.PropertyName == nameof(TrackInfoViewModel.SongName) ||
                e.PropertyName == nameof(TrackInfoViewModel.TrackDate) ||
                e.PropertyName == nameof(TrackInfoViewModel.DiscNumber) ||
                e.PropertyName == nameof(TrackInfoViewModel.TrackNumber))
            {
                _hasUnsavedChanges = true;
                if (sender is TrackInfoViewModel vm)
                {
                    vm.Track.IsModified = true;
                }

                // SongName affects "no track number" labels, disc/track changes affect
                // duplicate/gap detection — refresh on any of them.
                RefreshValidation();

                // Prompt 6: track-level edits unverify a verified album (no cell
                // marker per design — only album-level controls get markers).
                MaybeUnverifyAlbumEdit();
            }
        }

        /// <summary>
        /// Recomputes the validation banner from the current in-memory tracks.
        /// Caps display at <see cref="MaxBannerIssues"/> entries with an "and N more"
        /// suffix to keep the banner from dwarfing the grid. Toggles banner
        /// visibility so the row collapses to zero height when the album is clean.
        /// </summary>
        private void RefreshValidation()
        {
            var rawIssues = MetadataValidator.Validate(_tracks.Select(vm => vm.Track));

            _validationIssues.Clear();

            if (rawIssues.Count == 0)
            {
                ValidationBanner.Visibility = Visibility.Collapsed;
                return;
            }

            if (rawIssues.Count <= MaxBannerIssues)
            {
                foreach (var issue in rawIssues) _validationIssues.Add(issue);
            }
            else
            {
                for (int i = 0; i < MaxBannerIssues - 1; i++) _validationIssues.Add(rawIssues[i]);
                int remaining = rawIssues.Count - (MaxBannerIssues - 1);
                _validationIssues.Add($"… and {remaining} more issue{(remaining == 1 ? "" : "s")}");
            }

            ValidationBanner.Visibility = Visibility.Visible;
        }

        private void TracksDataGrid_BeginningEdit(object sender, System.Windows.Controls.DataGridBeginningEditEventArgs e)
        {
            // Nothing to block — editable columns are already controlled by IsReadOnly per-column
        }

        private void TracksDataGrid_CellEditEnding(object sender, System.Windows.Controls.DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == System.Windows.Controls.DataGridEditAction.Commit)
            {
                _hasUnsavedChanges = true;

                // Mark the individual track as modified, then refresh DisplayTitle
                if (e.Row.Item is TrackInfoViewModel vm)
                {
                    vm.Track.IsModified = true;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        vm.UpdateDisplayTitle();
                        // Validation runs after the edit's commit cycle so the new
                        // value is reflected in vm.Track. This catches edits to the
                        // # column (negative-clamped to 0, then flagged as zero) that
                        // don't go through the ViewModel proxy setter path.
                        RefreshValidation();
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }

                // Prompt 6: track-level edits unverify a verified album (no
                // cell marker per design).
                MaybeUnverifyAlbumEdit();
            }
        }

        /// <summary>
        /// Rebuild RawTitle from normalized components (SongName + Segue + TrackDate).
        /// Kept for compatibility — WriteMetadata reads SongName directly, so RawTitle
        /// is not strictly needed for save. May be removable in a future cleanup.
        /// </summary>
        private void ReconstructRawTitles()
        {
            foreach (var vm in _tracks)
            {
                var track = vm.Track;
                var title = track.SongName ?? "";

                // Strip any existing segue markers and date suffixes to prevent multiplication
                title = Regex.Replace(title, @"\s*\(\d{4}-\d{2}-\d{2}\)(\s*\(\d{4}-\d{2}-\d{2}\))*\s*$", "");
                title = Regex.Replace(title, @"(\s*>)+\s*$", "").TrimEnd();

                if (track.Segue)
                    title += " >";

                if (!string.IsNullOrEmpty(track.TrackDate))
                    title += $" ({track.TrackDate})";

                track.RawTitle = title;
            }
        }

        // ===== NORMALIZE / RENUMBER =====

        private void NormalizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_tracks.Count == 0)
            {
                StatusTextBlock.Text = "No tracks to normalize";
                return;
            }

            try
            {
                StatusTextBlock.Text = "Normalizing...";

                // Clean SongName before normalizing — edit mode loads raw FLAC titles
                // (e.g., "Dark Star > > (1968-02-23)") which need the same ParseTitleAndDate
                // cleanup that import mode gets during ReadFolder.
                foreach (var vm in _tracks)
                {
                    var track = vm.Track;
                    var (cleanName, parsedSegue, date) = _metadataService.ParseTitleAndDate(track.RawTitle, _albumInfo?.AlbumDate);
                    track.SongName = cleanName;
                    track.HasSegue = parsedSegue;
                    if (!string.IsNullOrEmpty(date) && string.IsNullOrEmpty(track.TrackDate))
                        track.TrackDate = date;
                }

                var trackList = _tracks.Select(t => t.Track).ToList();
                int matched = _normalizationService.NormalizeAll(trackList);

                // Reconstruct RawTitle from normalized components so the grid
                // (bound to RawTitle) shows the corrected title.
                ReconstructRawTitles();

                TracksDataGrid.Items.Refresh();
                RefreshValidation();
                _hasUnsavedChanges = true;

                int unmatched = _tracks.Count - matched;
                var normalizeStatus = $"Matched {matched} of {_tracks.Count} songs" +
                    (unmatched > 0 ? $" ({unmatched} unmatched)" : "");
                NormalizeStatusText.Text = normalizeStatus;
                StatusTextBlock.Text = normalizeStatus;

                if (unmatched > 0)
                {
                    var unmatchedTracks = _tracks
                        .Where(t => t.Track.IsMatched == false)
                        .Select(t => t.Track)
                        .ToList();

                    var parentWindow = Window.GetWindow(this);
                    var dialog = new UnmatchedSongsDialog(unmatchedTracks, _normalizationService)
                    {
                        Owner = parentWindow
                    };

                    bool applied = dialog.ShowDialog() == true;

                    if (applied && dialog.ChangesMade)
                    {
                        ReconstructRawTitles();
                        TracksDataGrid.Items.Refresh();
                        int nowMatched = _tracks.Count(t => t.Track.IsMatched == true);
                        var correctionStatus = $"Corrections applied. Matched {nowMatched} of {_tracks.Count} songs";
                        NormalizeStatusText.Text = correctionStatus;
                        StatusTextBlock.Text = correctionStatus;
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
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Normalization error: {ex.Message}";
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

                foreach (var vm in discGroup)
                {
                    vm.Track.TrackNumber = (discNumber * 100) + trackIndex;
                    vm.Track.IsModified = true;
                    trackIndex++;
                }
            }

            TracksDataGrid.Items.Refresh();
            _hasUnsavedChanges = true;
            RefreshValidation();
            StatusTextBlock.Text = "Tracks renumbered using disc-aware 101/201/301 convention";
        }

        // ===== DATE AUTO-LOOKUP =====

        private void AlbumDateTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isUpdating || _albumInfo == null) return;

            var date = AlbumDateTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(date) || date.Length != 10) return;
            if (!Regex.IsMatch(date, @"^\d{4}-\d{2}-\d{2}$")) return;

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
            }

            UpdateMatchSetlistButton();
        }

        private void UpdateMatchSetlistButton()
        {
            var date = AlbumDateTextBox.Text?.Trim();
            var setlist = ShowLookupService.Instance.GetSetlist(date ?? "");
            if (setlist != null)
            {
                MatchSetlistButton.IsEnabled = true;
                MatchSetlistButton.ToolTip = "Match tracks to setlist data";
            }
            else
            {
                MatchSetlistButton.IsEnabled = false;
                MatchSetlistButton.ToolTip = "No setlist data for this date";
            }
        }

        // ===== VERIFY BUTTON =====

        private async void VerifyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_albumInfo == null) return;

            // Unverify path: no validation, just flip and save.
            if (IsVerified)
            {
                IsVerified = false;
                await SaveChangesAsync();
                return;
            }

            // Verify path: enforce required fields per memo § 3.
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(_albumInfo.Artist))
                missing.Add("Artist");

            var date = _albumInfo.AlbumDate?.Trim() ?? "";
            if (!DateTime.TryParseExact(date, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out _))
                missing.Add("Date (yyyy-MM-dd)");

            if (missing.Count > 0)
            {
                App.Alerts.Notify(
                    $"Cannot verify — the following fields are required:\n\n• {string.Join("\n• ", missing)}",
                    AlertSeverity.Warning,
                    "Required fields missing");
                return;
            }

            IsVerified = true;
            await SaveChangesAsync();
        }

        // ===== FINGERPRINT BUTTON =====

        private async void FingerprintButton_Click(object sender, RoutedEventArgs e)
        {
            if (_tracks.Count == 0)
            {
                StatusTextBlock.Text = "No tracks loaded";
                return;
            }

            try
            {
                FingerprintButton.IsEnabled = false;
                ProgressBar.Visibility = Visibility.Visible;
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Maximum = _tracks.Count;
                ProgressBar.Value = 0;

                var librarySettings = LibrarySettings.Load();
                var fingerprintService = new FingerprintService(librarySettings);

                var trackList = _tracks.Select(t => t.Track).ToList();

                // Progress<T> captures the construction-time SynchronizationContext, so this
                // lambda fires on the UI thread. Safe to touch StatusTextBlock and ProgressBar.
                var progress = new Progress<(int current, int total, string message)>(report =>
                {
                    StatusTextBlock.Text = report.message;
                    ProgressBar.Value = report.current;
                    UpdateFingerprintSummary();
                });

                // Run the entire batch off the UI thread. onTrackComplete also wraps in
                // Task.Run because TagLib.File.Save is synchronous.
                var result = await Task.Run(() => fingerprintService.PrecomputeFingerprintsAsync(
                    trackList,
                    progress,
                    // Release the NAudio handle around each per-track fingerprint write so the
                    // loaded track doesn't block TagLib's exclusive write. Per-track (not one
                    // batch-wide window): only the write needs the lock — fpcalc's read can
                    // share the handle — and only the one loaded track ever matches, so every
                    // other track's write no-ops.
                    onTrackComplete: t => Task.Run(() => App.PlaybackService.WithFileReleased(
                        new[] { t.FilePath },
                        () => FingerprintService.WriteFingerprintToTrackFile(t)))));

                var attempted = trackList.Count - result.SkippedExisting;
                string finalStatus;
                if (attempted == 0)
                {
                    finalStatus = "All tracks already fingerprinted";
                }
                else if (!result.FpcalcAvailable)
                {
                    finalStatus = "fpcalc not configured — open Settings to configure";
                }
                else if (result.Failed == 0)
                {
                    finalStatus = $"Fingerprinted {result.Computed} tracks";
                }
                else
                {
                    finalStatus = $"Fingerprinted {result.Computed} tracks ({result.Failed} failed)";
                }

                StatusTextBlock.Text = finalStatus;
                UpdateFingerprintSummary();
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Fingerprint error: {ex.Message}";
                Debug.WriteLine($"[FINGERPRINT] {ex}");
            }
            finally
            {
                FingerprintButton.IsEnabled = true;
                ProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        // ===== CONTEXT MENU =====

        private void TracksDataGrid_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var hit = VisualTreeHelper.HitTest(TracksDataGrid, e.GetPosition(TracksDataGrid));
            if (hit == null) return;

            var element = hit.VisualHit as FrameworkElement;
            while (element != null && element is not DataGridRow)
            {
                element = VisualTreeHelper.GetParent(element) as FrameworkElement;
            }

            if (element is not DataGridRow row) return;
            if (row.Item is not TrackInfoViewModel clickedVm) return;
            var clickedTrack = clickedVm.Track;

            // Build dark-themed context menu
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

            // Track Info
            menu.Items.Add(new Separator
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3E, 0x3E, 0x42))
            });

            var trackInfoItem = new MenuItem { Header = "\U0001F4C4  Track Info", Style = menuItemStyle };
            trackInfoItem.Click += (s, args) =>
            {
                var dialog = new TrackInfoDialog(
                    clickedTrack,
                    artist: _albumInfo?.Artist,
                    album: _albumInfo?.AlbumName);
                dialog.Owner = Window.GetWindow(this);
                dialog.ShowDialog();
            };
            menu.Items.Add(trackInfoItem);

            // Match to Song — only after Match Setlist has run and for unmatched tracks
            if (_matchSetlistHasRun && clickedTrack.IsMatched != true &&
                _lastSetlistSongs != null && _lastClaimedPositions != null)
            {
                var unmatchedSongs = BuildUnmatchedSetlistSongList();
                if (unmatchedSongs.Count > 0)
                {
                    menu.Items.Add(new Separator
                    {
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3E, 0x3E, 0x42))
                    });

                    var matchItem = new MenuItem { Header = "\U0001F3B5  Match to Song\u2026", Style = menuItemStyle };
                    matchItem.Click += (s, args) => MatchToSong_Click(clickedVm);
                    menu.Items.Add(matchItem);
                }
            }

            menu.IsOpen = true;
            e.Handled = true;
        }

        // ===== MATCH TO SONG =====

        private List<(int SetlistIndex, string DisplayLabel)> BuildUnmatchedSetlistSongList()
        {
            var result = new List<(int, string)>();
            if (_lastSetlistSongs == null || _lastClaimedPositions == null)
                return result;

            var date = AlbumDateTextBox.Text?.Trim() ?? "";
            var setlist = ShowLookupService.Instance.GetSetlist(date);
            if (setlist == null) return result;

            for (int i = 0; i < _lastSetlistSongs.Count; i++)
            {
                if (_lastClaimedPositions.Contains(i)) continue;

                var discTrack = ShowLookupService.Instance.GetDiscTrack(date, _lastSetlistSongs[i].Position);
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

        private void MatchToSong_Click(TrackInfoViewModel vm)
        {
            if (_lastSetlistSongs == null || _lastClaimedPositions == null)
                return;

            var unmatchedSongs = BuildUnmatchedSetlistSongList();
            if (unmatchedSongs.Count == 0) return;

            var track = vm.Track;
            var cleanedTitle = track.SongName ?? "";
            var dialog = new MatchToSongDialog(cleanedTitle, unmatchedSongs);
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() != true || dialog.SelectedSetlistIndex < 0)
                return;

            var selectedIndex = dialog.SelectedSetlistIndex;
            var selectedSong = _lastSetlistSongs[selectedIndex];

            // Update song name to canonical title (no disc/track reassignment in Edit Metadata)
            track.SongName = selectedSong.Canonical;
            track.IsMatched = true;
            track.IsModified = true;

            // Apply segue from setlist
            if (selectedSong.Segue)
                track.Segue = true;

            // Mark position as claimed
            _lastClaimedPositions.Add(selectedIndex);

            // Auto-add alias to songs.json
            var aliasCandidate = cleanedTitle.Trim();
            if (!string.IsNullOrEmpty(aliasCandidate) && !string.IsNullOrEmpty(selectedSong.Canonical))
            {
                _normalizationService.AddAlias(selectedSong.Canonical, aliasCandidate);
            }

            ReconstructRawTitles();
            TracksDataGrid.Items.Refresh();
            _hasUnsavedChanges = true;
            RefreshValidation();

            // This row just became matched — drop it out of the amber tint.
            RecomputeUnmatchedHighlights();

            int remaining = _lastSetlistSongs.Count - _lastClaimedPositions.Count;
            StatusTextBlock.Text = $"Matched '{aliasCandidate}' → '{selectedSong.Canonical}'. {remaining} setlist songs remaining.";
        }

        // ===== MATCH SETLIST =====

        private async void MatchSetlistButton_Click(object sender, RoutedEventArgs e)
        {
            if (_tracks.Count == 0 || _albumInfo == null)
            {
                StatusTextBlock.Text = "No tracks to match";
                return;
            }

            var date = AlbumDateTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(date)) return;

            var setlist = ShowLookupService.Instance.GetSetlist(date);
            if (setlist == null)
            {
                StatusTextBlock.Text = "No setlist data available for this date";
                return;
            }

            // Flatten setlist into the shared projection. setlistSongs (tuples) feeds the
            // Match-to-Song fallback via _lastSetlistSongs; matcherSetlist is the SetlistEntry list
            // the shared pure matcher consumes. Both are thin adapters off the single pure
            // SetlistProjection.Build, the same source ImportView and the reference side-panel use
            // (reference-side-panel-spec.md §5.1). Mirrors ImportView's Match Setlist flow.
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

            // Model B: matched tracks get SongName/Segue/IsMatched/IsModified
            // decoration; unmatched tracks are left entirely untouched
            // (DiscNumber/TrackNumber preserved). The resolver mirrors Import's
            // Normalize -> GetOfficialTitle wiring.
            var trackList = _tracks.Select(vm => vm.Track).ToList();

            // B2b: route through the preview-before-apply review surface instead
            // of a blind MatchAndDecorate. ComputeProposals -> review dialog ->
            // Apply(edited subset). A null result means the user cancelled — the
            // surface protects against the blind segue overwrite (1977-05-11).
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
                // Cancelled: apply nothing. Do not touch _lastClaimedPositions,
                // _matchSetlistHasRun, the grid, amber highlights, or
                // _hasUnsavedChanges — prior match state must persist intact.
                StatusTextBlock.Text = "Match Setlist cancelled.";
                return;
            }

            int matchCount = result.MatchedCount;
            int segueCount = result.SegueCount;

            // Store state for Match to Song
            _lastSetlistSongs = setlistSongs;
            _lastClaimedPositions = result.ClaimedPositions;
            _matchSetlistHasRun = true;

            ReconstructRawTitles();
            TracksDataGrid.Items.Refresh();
            _hasUnsavedChanges = true;
            RefreshValidation();

            // Match has run — paint unplaced tracks amber.
            RecomputeUnmatchedHighlights();

            int unmatchedCount = _tracks.Count - matchCount;
            var segueMsg = segueCount > 0 ? $", {segueCount} segues" : "";
            var unmatchedMsg = unmatchedCount > 0
                ? $". {unmatchedCount} unmatched \u2014 right-click to match manually."
                : "";
            StatusTextBlock.Text = $"Matched {matchCount} of {_tracks.Count} tracks to setlist{segueMsg}{unmatchedMsg}";

            // Persist confirmed combines as canonical aliases (alias-setlists-spec.md \u00a74, seam (b);
            // Option A wires the EDIT seam only). EVERY Count>1 combine is recorded regardless of the
            // SongName/Segue accept-ignore state \u2014 the alias records coverage, not the displayed
            // name. PersistAliasSetlist is sync and does file I/O, so the loop runs off the UI thread.
            // The date was guarded non-empty at the top of this handler and resolves to a cached
            // concert (the same source GetSetlist read), so ConcertNotFound is defensive only.
            if (result.ConfirmedCombines.Count > 0)
            {
                int persistedCount = 0;
                try
                {
                    await Task.Run(() =>
                    {
                        foreach (var p in result.ConfirmedCombines)
                        {
                            var entry = new AliasEntry { CoveredOfficialIndices = p.CoveredEntryIndices };
                            var outcome = ConcertLookupService.Instance.PersistAliasSetlist(date, entry);
                            switch (outcome)
                            {
                                case AliasPersistResult.Persisted:
                                    persistedCount++;
                                    break;
                                case AliasPersistResult.DuplicateNoOp:
                                    break; // idempotent re-confirm \u2014 silent, nothing written
                                case AliasPersistResult.ConcertNotFound:
                                    Debug.WriteLine($"[ALIAS] No concert cached for {date}; combine not persisted.");
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
                    // back). Surface it via the Edit save-failure pattern; do not let it escape the
                    // async void handler.
                    StatusTextBlock.Text = $"Combine aliases not saved: {ex.Message}";
                    App.Alerts.Notify($"Error saving combine aliases:\n\n{ex.Message}",
                        AlertSeverity.Error, "Alias Save Failed");
                }
            }
        }

        // ===== DRAG-TO-REORDER =====

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
                WpfPoint current = e.GetPosition(null);
                WpfVector diff = _dragStartPoint - current;

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
                    // Block cross-disc drags
                    if (_draggedItem.DiscNumber != targetItem.DiscNumber)
                    {
                        HideDropIndicator();
                        e.Handled = true;
                        return;
                    }

                    int draggedIndex = _tracks.IndexOf(_draggedItem);
                    int targetIndex = _tracks.IndexOf(targetItem);

                    if (draggedIndex >= 0 && targetIndex >= 0)
                    {
                        _tracks.RemoveAt(draggedIndex);
                        _tracks.Insert(targetIndex, _draggedItem);
                        TracksDataGrid.SelectedItem = _draggedItem;

                        // Auto-renumber within the affected disc
                        RenumberDisc(_draggedItem.Track.DiscNumber > 0 ? _draggedItem.Track.DiscNumber : 1);

                        TracksDataGrid.Items.Refresh();
                        _hasUnsavedChanges = true;
                        RefreshValidation();
                    }
                }
            }

            HideDropIndicator();
            e.Handled = true;
        }

        private void RenumberDisc(int discNumber)
        {
            int trackNum = 1;
            foreach (var vm in _tracks)
            {
                var track = vm.Track;
                var trackDisc = track.DiscNumber > 0 ? track.DiscNumber : 1;
                if (trackDisc == discNumber)
                {
                    track.TrackNumber = discNumber * 100 + trackNum;
                    track.IsModified = true;
                    trackNum++;
                }
            }
        }

        private void UpdateDropIndicator(DataGridRow targetRow)
        {
            try
            {
                var position = targetRow.TranslatePoint(new WpfPoint(0, 0), TracksDataGrid);
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

        // ===== SAVE: MBID WRITE =====

        private void WriteMbidToTracks()
        {
            var mbid = _show.MusicBrainzReleaseId;
            if (string.IsNullOrEmpty(mbid)) return;

            foreach (var vm in _tracks)
            {
                if (string.IsNullOrEmpty(vm.Track.FilePath) || !File.Exists(vm.Track.FilePath))
                    continue;

                try
                {
                    using var file = TagLib.File.Create(vm.Track.FilePath);

                    if (file is TagLib.Flac.File)
                    {
                        var xiph = (TagLib.Ogg.XiphComment?)file.GetTag(TagLib.TagTypes.Xiph);
                        xiph?.SetField("MUSICBRAINZ_ALBUMID", mbid);
                    }
                    else
                    {
                        var id3v2 = (TagLib.Id3v2.Tag?)file.GetTag(TagLib.TagTypes.Id3v2, true);
                        if (id3v2 != null)
                        {
                            var frame = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2, "MusicBrainz Album Id", true);
                            frame.Text = new[] { mbid };
                        }
                    }

                    file.Save();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MBID Write] Error writing to {vm.Track.FilePath}: {ex.Message}");
                }
            }
        }

        // ===== OPEN FOLDER =====

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var path = FolderPathTextBox.Text;
            if (string.IsNullOrEmpty(path) || path == "(unknown)")
                return;

            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Folder not found: {path}";
                Debug.WriteLine($"[EDIT] Open folder failed: {ex.Message}");
            }
        }
    }
}
