using DeadEditor.Models;
using DeadEditor.Services;
using DeadEditor.Views;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace DeadEditor
{
    /// <summary>
    /// ShellWindow - Single-window shell for Dead Editor
    /// Manages navigation between views (Library, Import, Settings)
    /// </summary>
    public partial class ShellWindow : Window, INotifyPropertyChanged
    {
        private readonly LibrarySettings _settings;
        private readonly NavigationService _navigationService;
        private System.Windows.Controls.UserControl? _currentView;

        // View instances (kept alive to preserve state)
        private LibraryGridView? _libraryView;
        private System.Windows.Controls.UserControl? _importView;
        private SongsView? _songsView;
        private ReleasesView? _releasesView;
        private ConcertDatabaseView? _concertsView;
        private BoxSetsView? _boxSetsView;
        private System.Windows.Controls.UserControl? _settingsView;

        // Public property for child views to access navigation
        public NavigationService Navigation => _navigationService;

        public ShellWindow()
        {
            InitializeComponent();

            // Wire the in-window alert banner as the shell-wide alert surface (alert-system-spec.md
            // Ruling 1). The shell is the single window created at startup, so the sink is attached
            // before any view can call App.Alerts.Notify.
            AlertService.Instance.RegisterSink(AlertBanner);

            // Wire the dimmed-scrim confirm host as the shell-wide bucket-B surface (Ruling 2).
            // Registered alongside the banner sink, before any view can call App.Alerts.ConfirmAsync.
            AlertService.Instance.RegisterConfirmHost(ConfirmHost);

            // Wire the read-panel host as the shell-wide bucket-C surface (Ruling 3) for the Import
            // info-file viewer (#33).
            AlertService.Instance.RegisterReadPanelHost(ReadPanel);

            // Wire the please-wait status overlay (alert-system-spec.md § Status overlay), driven by
            // App.Alerts.RunWithStatusAsync. Registered alongside the other hosts; no caller is wired
            // yet (Commit B brings the concert-load cold gate).
            AlertService.Instance.RegisterStatusHost(StatusHost);

            _settings = LibrarySettings.Load();
            _navigationService = new NavigationService();

            // Subscribe to navigation events
            _navigationService.NavigationRequested += NavigationService_NavigationRequested;

            // Subscribe to header bar events
            HeaderBar.ImportCancelRequested += HeaderBar_ImportCancelRequested;
            HeaderBar.EditMetadataRequested += HeaderBar_EditMetadataRequested;
            HeaderBar.LibraryFilterChanged += HeaderBar_LibraryFilterChanged;
            HeaderBar.AdvancedSearchRequested += HeaderBar_AdvancedSearchRequested;
            HeaderBar.DeleteAlbumRequested += HeaderBar_DeleteAlbumRequested;
            HeaderBar.EditDatesRequested += HeaderBar_EditDatesRequested;
            HeaderBar.SaveDatesRequested += HeaderBar_SaveDatesRequested;
            HeaderBar.CancelDatesRequested += HeaderBar_CancelDatesRequested;
            HeaderBar.ConcertsSearchChanged += HeaderBar_ConcertsSearchChanged;
            HeaderBar.ConcertsOwnershipFilterChanged += HeaderBar_ConcertsOwnershipFilterChanged;
            HeaderBar.EditSetlistRequested += HeaderBar_EditSetlistRequested;
            HeaderBar.NewConcertRequested += HeaderBar_NewConcertRequested;
            HeaderBar.DeleteConcertRequested += HeaderBar_DeleteConcertRequested;
            HeaderBar.NewBoxSetRequested += HeaderBar_NewBoxSetRequested;
            HeaderBar.BoxSetWizardBackClicked += HeaderBar_BoxSetWizardBackClicked;
            HeaderBar.BoxSetWizardNextClicked += HeaderBar_BoxSetWizardNextClicked;
            HeaderBar.BoxSetWizardSaveClicked += HeaderBar_BoxSetWizardSaveClicked;
            HeaderBar.BoxSetWizardCancelClicked += HeaderBar_BoxSetWizardCancelClicked;

            // Set data context for binding
            DataContext = this;

            // Navigate to Library view by default
            NavigateToLibrary();

            // Restore window position if not maximized
            RestoreWindowPosition();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public System.Windows.Controls.UserControl? CurrentView
        {
            get => _currentView;
            set
            {
                if (_currentView != value)
                {
                    _currentView = value;
                    OnPropertyChanged();
                }
            }
        }

        private void RestoreWindowPosition()
        {
            // Window position restoration only if not maximized
            if (WindowState != WindowState.Maximized)
            {
                if (_settings.LibraryWindowLeft.HasValue && _settings.LibraryWindowTop.HasValue)
                {
                    Left = _settings.LibraryWindowLeft.Value;
                    Top = _settings.LibraryWindowTop.Value;
                }

                if (_settings.LibraryWindowWidth.HasValue && _settings.LibraryWindowHeight.HasValue)
                {
                    Width = _settings.LibraryWindowWidth.Value;
                    Height = _settings.LibraryWindowHeight.Value;
                }
            }
        }

        private void ShellWindow_Closing(object? sender, CancelEventArgs e)
        {
            // Load fresh settings to avoid overwriting changes made by other views
            // (e.g., library paths changed in SettingsView since startup)
            var freshSettings = LibrarySettings.Load();
            freshSettings.LibraryWindowLeft = Left;
            freshSettings.LibraryWindowTop = Top;
            freshSettings.LibraryWindowWidth = Width;
            freshSettings.LibraryWindowHeight = Height;
            freshSettings.Save();
        }

        // ===== KEYBOARD SHORTCUTS =====

        private void ShellWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Confirm host gate (alert-system-spec.md Ruling 2): while a blocking decision is up, the
            // scrim owns the keyboard. Enter = the focused/default Confirm button; Esc = the safe
            // negative answer. Every other key is swallowed so no shell shortcut (Space=play,
            // Ctrl+F, Delete, Esc=GoBack) leaks behind the scrim. This runs first because the
            // Window's PreviewKeyDown tunnels before the card's own elements.
            if (ConfirmHost.IsShowing)
            {
                if (e.Key == Key.Enter)
                    ConfirmHost.ConfirmByKeyboard();
                else if (e.Key == Key.Escape)
                    ConfirmHost.CancelByKeyboard();
                e.Handled = true;
                return;
            }

            // Status overlay gate (alert-system-spec.md § Status overlay): the please-wait surface is
            // NON-CANCELABLE, so while it is showing swallow EVERY key — no Esc dismiss, and no shell
            // shortcut (Space=play, Ctrl+F, Delete, Esc=GoBack) may fire behind the scrim. ConfirmHost
            // (above it at Z 100) is gated first, so a confirm can still own the keyboard if one ever
            // surfaces above the status overlay.
            if (StatusHost.IsShowing)
            {
                e.Handled = true;
                return;
            }

            // Read-panel gate (alert-system-spec.md Ruling 3): while the info-file viewer is up, Esc
            // dismisses it. Every OTHER key falls through UNHANDLED so it reaches the focused text area
            // (PgUp/PgDn/arrows scroll) — but we return before the shell's own shortcuts so none of
            // them (Space=play, Ctrl+F, Delete, Esc=GoBack) fire behind the scrim.
            if (ReadPanel.IsShowing)
            {
                if (e.Key == Key.Escape)
                {
                    ReadPanel.Hide();
                    e.Handled = true;
                }
                return;
            }

            // Don't intercept keys when user is typing in a text box
            var focusedElement = Keyboard.FocusedElement;
            bool isTypingInTextBox = focusedElement is System.Windows.Controls.TextBox;

            // Ctrl+F: Focus search box in Library Grid
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                HeaderBar.FocusSearch();
                e.Handled = true;
                return;
            }

            // Escape: Clear search or navigate back
            if (e.Key == Key.Escape)
            {
                // If typing in search box, clear it and unfocus
                if (isTypingInTextBox && focusedElement is System.Windows.Controls.TextBox textBox)
                {
                    if (!string.IsNullOrEmpty(textBox.Text))
                    {
                        textBox.Text = "";
                        e.Handled = true;
                        return;
                    }
                    // Move focus away from the text box
                    Keyboard.ClearFocus();
                    FocusManager.SetFocusedElement(this, this);
                    e.Handled = true;
                    return;
                }

                // If not in library grid, navigate back
                if (CurrentView is not LibraryGridView && _navigationService.CanGoBack)
                {
                    _navigationService.GoBack();
                    e.Handled = true;
                    return;
                }
            }

            // Space: Play/Pause toggle (only when not typing in a text box)
            if (e.Key == Key.Space && !isTypingInTextBox)
            {
                var player = App.PlaybackService;
                if (player.State == PlaybackState.Playing)
                {
                    player.Pause();
                }
                else
                {
                    player.Play();
                }
                e.Handled = true;
                return;
            }

            // Delete: Remove selected track from playlist (when playlist is focused)
            if (e.Key == Key.Delete && !isTypingInTextBox)
            {
                // Check if the playlist panel's DataGrid has a selected item
                if (PlaylistPanel.TryRemoveSelectedTrack())
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        // ===== NAVIGATION =====

        private void NavigationService_NavigationRequested(object? sender, NavigationEventArgs e)
        {
            // Update current view
            CurrentView = e.View;

            // Update header bar based on view type
            UpdateHeaderBar(e.View, e.Context);

            // Update sidebar active indicator to match current view
            SidebarPanel.SetActiveForView(e.View);

            // The concerts grid is a cached, reused instance bound to live ConcertReference DTOs
            // (no INotifyPropertyChanged). Whenever it becomes the current view — back from
            // detail/edit, or returning via the sidebar — re-query and re-render so in-place edits
            // (verify, venue/date/song) and cache rekeys/evictions made while away are reflected.
            if (e.View is ConcertDatabaseView concertsView)
            {
                concertsView.RefreshFromCache();
            }
        }

        private void UpdateHeaderBar(System.Windows.Controls.UserControl view, object? context)
        {
            // HeaderBar will be updated based on the current view type
            if (view is LibraryGridView libraryView)
            {
                HeaderBar.ShowLibraryHeader(libraryView);
            }
            else if (view is AlbumDetailView albumView)
            {
                HeaderBar.ShowAlbumDetailHeader(albumView, context);
            }
            else if (view is EditMetadataView editView)
            {
                HeaderBar.ShowEditMetadataHeader(editView);
            }
            else if (view is ImportView)
            {
                HeaderBar.ShowImportHeader();
            }
            else if (view is SongsView songsView)
            {
                HeaderBar.ShowSongsHeader(songsView);
            }
            else if (view is ReleasesView releasesView)
            {
                HeaderBar.ShowReleasesHeader(releasesView);
            }
            else if (view is ConcertDetailView concertDetailView)
            {
                HeaderBar.ShowConcertDetailHeader(concertDetailView);
            }
            else if (view is EditSetlistView editSetlistView)
            {
                HeaderBar.ShowEditSetlistHeader(editSetlistView);
            }
            else if (view is ConcertDatabaseView concertsView)
            {
                HeaderBar.ShowConcertsHeader(concertsView);
            }
            else if (view is BoxSetsView boxSetsView)
            {
                HeaderBar.ShowBoxSetsHeader(boxSetsView);
            }
            else if (view is BoxSetWizardView wizardView)
            {
                HeaderBar.ShowBoxSetWizardHeader(wizardView);
            }
            else if (view is SettingsView)
            {
                HeaderBar.ShowSettingsHeader();
            }
        }

        private void HeaderBar_LibraryFilterChanged(object? sender, LibraryFilterEventArgs e)
        {
            if (_libraryView != null)
            {
                _libraryView.ApplyFilter(e.SearchText, e.TypeFilter, e.YearFilter, e.VerificationFilter);
                HeaderBar.UpdateConcertCount(
                    _libraryView.FilteredCount, _libraryView.ConcertCount,
                    _libraryView.IsByDateMode, _libraryView.IsMissingShowsMode,
                    _libraryView.TotalShowsInScope);
            }
        }

        private void HeaderBar_AdvancedSearchRequested(object? sender, EventArgs e)
        {
            // Open the Advanced Search dialog
            try
            {
                var normService = new Services.NormalizationService();
                var metaService = new Services.MetadataService();

                var dialog = new AdvancedSearchDialog(
                    normService, metaService, _settings,
                    navigateToAlbumCallback: (folderPath) =>
                    {
                        // Navigate to the album from the search result
                        if (_libraryView != null)
                        {
                            // Find the show matching this folder path
                            // For now, just switch to library view
                            NavigateToLibrary();
                        }
                    });
                dialog.Owner = this;
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShellWindow] Advanced Search error: {ex.Message}");
            }
        }

        private async void HeaderBar_DeleteAlbumRequested(object? sender, EventArgs e)
        {
            if (_navigationService.CurrentContext is not LibraryShow show)
                return;

            // Count files across all folders
            var folders = show.FolderPaths.Any() ? show.FolderPaths : new List<string> { show.FolderPath };
            int fileCount = 0;
            foreach (var folder in folders)
            {
                if (Directory.Exists(folder))
                {
                    fileCount += Directory.GetFiles(folder, "*.flac").Length;
                    fileCount += Directory.GetFiles(folder, "*.mp3").Length;
                }
            }

            // Build display name
            var displayName = !string.IsNullOrEmpty(show.OfficialRelease) ? show.OfficialRelease
                : !string.IsNullOrEmpty(show.AlbumName) ? show.AlbumName
                : !string.IsNullOrEmpty(show.Venue) ? $"{show.Date} {show.Venue}"
                : show.Date;

            // Bucket-B confirm (alert-system-spec.md #1). Branch mapping preserved from the old
            // YesNo/Warning MessageBox: Yes (true) -> delete; No/Esc (false) -> return. Event
            // subscription, so async void carries no call-site ripple.
            bool confirmed = await App.Alerts.ConfirmAsync(
                $"Delete '{displayName}'?\n\n" +
                $"This will send {fileCount} audio file{(fileCount == 1 ? "" : "s")} to the Recycle Bin.\n\n" +
                $"This action can be undone by restoring from the Recycle Bin.",
                "Delete Album");

            if (!confirmed)
                return;

            try
            {
                // Send each folder's contents to Recycle Bin
                foreach (var folder in folders)
                {
                    if (!Directory.Exists(folder))
                        continue;

                    // Send the entire folder to Recycle Bin
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                        folder,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }

                // Navigate back to Library and refresh
                NavigateToLibrary();
                _libraryView?.ReloadLibrary();
            }
            catch (Exception ex)
            {
                App.Alerts.Notify($"Error deleting album:\n\n{ex.Message}", AlertSeverity.Error, "Delete Failed");
            }
        }

        private void HeaderBar_EditDatesRequested(object? sender, EventArgs e)
        {
            _libraryView?.EnterDateEditMode();
            HeaderBar.UpdateDateEditButtons(true, true);
        }

        private void HeaderBar_SaveDatesRequested(object? sender, EventArgs e)
        {
            _libraryView?.SaveDateEdits();
            HeaderBar.UpdateDateEditButtons(true, false);
        }

        private void HeaderBar_CancelDatesRequested(object? sender, EventArgs e)
        {
            _libraryView?.CancelDateEdits();
            HeaderBar.UpdateDateEditButtons(true, false);
        }

        private void HeaderBar_EditMetadataRequested(object? sender, EventArgs e)
        {
            // Get the current LibraryShow from the navigation context
            if (_navigationService.CurrentContext is LibraryShow show)
            {
                var editView = new EditMetadataView(this, show);

                // When save completes, refresh the library grid
                editView.SaveCompleted += (s, args) =>
                {
                    _libraryView?.ReloadLibrary();
                };

                _navigationService.NavigateTo(editView, show);
            }
        }

        private async void HeaderBar_ImportCancelRequested(object? sender, EventArgs e)
        {
            // Bucket-B confirm (alert-system-spec.md #3). Branch mapping preserved from the old
            // YesNo/Question MessageBox: Yes (true) -> leave Import; No/Esc (false) -> stay.
            // The handler is already an event subscription, so async void carries no call-site ripple.
            if (_importView is ImportView importView && importView.HasUnsavedWork)
            {
                bool leave = await App.Alerts.ConfirmAsync(
                    "You have tracks loaded that haven't been imported. Leave Import?",
                    "Leave Import");

                if (!leave) return;
            }

            NavigateToLibrary();
        }

        private void SidebarPanel_NavigationRequested(object sender, string destination)
        {
            switch (destination)
            {
                case "Library":
                    NavigateToLibrary();
                    break;
                case "Import":
                    NavigateToImport();
                    break;
                case "Songs":
                    NavigateToSongs();
                    break;
                case "Releases":
                    NavigateToReleases();
                    break;
                case "Concerts":
                    NavigateToConcerts();
                    break;
                case "BoxSets":
                    NavigateToBoxSets();
                    break;
                case "Settings":
                    NavigateToSettings();
                    break;
            }
        }

        private void NavigateToLibrary()
        {
            // Create library view on first access (kept alive thereafter)
            if (_libraryView == null)
            {
                _libraryView = new LibraryGridView(this);
                // Subscribe to concert count changes
                _libraryView.ConcertCountChanged += (s, count) =>
                {
                    HeaderBar.UpdateConcertCount(_libraryView.FilteredCount, _libraryView.ConcertCount,
                        _libraryView.IsByDateMode, _libraryView.IsMissingShowsMode,
                        _libraryView.TotalShowsInScope);
                };
                // Refresh concerts view library dates after library loads/reloads
                _libraryView.LibraryLoaded += (s, args) =>
                {
                    RefreshConcertsLibraryDates();
                };
            }

            // Navigate to root (clears back stack)
            _navigationService.NavigateToRoot(_libraryView);
        }

        /// <summary>
        /// Ensures the ImportView exists and is wired up. Returns the typed ImportView.
        /// </summary>
        private ImportView EnsureImportView()
        {
            if (_importView == null)
            {
                var importView = new ImportView();

                // When an import completes, refresh the library grid and concerts dates
                importView.ImportCompleted += (s, destinationPath) =>
                {
                    // Reload library data so the new album shows up immediately
                    _libraryView?.ReloadLibrary();
                    // Note: RefreshConcertsLibraryDates will be called by LibraryLoaded event
                    // after ReloadLibrary completes
                };

                _importView = importView;
            }

            return (ImportView)_importView;
        }

        private void NavigateToImport()
        {
            EnsureImportView();
            _navigationService.NavigateToRoot(_importView!);
        }

        private void NavigateToSongs()
        {
            if (_songsView == null)
            {
                _songsView = new SongsView();
            }

            _songsView.LoadSongs();
            _navigationService.NavigateToRoot(_songsView);
        }

        private void NavigateToReleases()
        {
            if (_releasesView == null)
            {
                _releasesView = new ReleasesView();
            }

            _releasesView.LoadReleases();
            _navigationService.NavigateToRoot(_releasesView);
        }

        private void NavigateToConcerts()
        {
            if (_concertsView == null)
            {
                _concertsView = new ConcertDatabaseView();
            }

            _concertsView.LoadConcerts();

            // Reset ownership filter (session-only persistence)
            _concertsView.ResetOwnershipFilter();
            HeaderBar.ResetConcertsOwnershipFilter();

            // Set library dates if library is loaded
            RefreshConcertsLibraryDates();

            _navigationService.NavigateToRoot(_concertsView);
        }

        private void NavigateToBoxSets()
        {
            if (_boxSetsView == null)
            {
                _boxSetsView = new BoxSetsView();
                // Persistent instance (created once) — subscribe the edit-launch event here so it
                // is wired exactly once. Double-click / Enter on a saved row opens edit-mode.
                _boxSetsView.EditBoxSetRequested += (s, slug) => OpenBoxSetForEdit(slug);
            }

            // Re-read from disk on every navigation (cheap — dozens of files at most,
            // per Phase A R3). Mirrors NavigateToConcerts's LoadConcerts call.
            _boxSetsView.LoadBoxSets();

            _navigationService.NavigateToRoot(_boxSetsView);
        }

        private void HeaderBar_NewBoxSetRequested(object? sender, EventArgs e)
        {
            // Fresh wizard instance per click — no caching (the brief explicitly avoids
            // stale state between sessions). BoxSetService has only static initialization
            // state, so constructing a new instance is cheap and shares the singleton init.
            var wizard = new BoxSetWizardView(new Services.BoxSetService());

            // On wizard Save success or Cancel, return to the list view. NavigateToBoxSets
            // calls LoadBoxSets internally, so a newly-written definition shows up
            // immediately without any explicit refresh.
            wizard.Completed += (s, a) => NavigateToBoxSets();

            // Wizard step changes drive the HeaderBar's Back/Next/Save button state.
            wizard.StepChanged += (s, step) => HeaderBar.UpdateBoxSetWizardStep(step);

            _navigationService.NavigateToRoot(wizard);
        }

        /// <summary>
        /// Opens the wizard in edit-mode for a saved box. Reads a FRESH copy from disk by slug
        /// (the read-fresh copy is itself the isolated editable buffer — edits don't touch the
        /// file until Save, and Cancel discards by dropping the wizard). If the box is gone since
        /// the list was loaded, refreshes the list and notifies rather than opening an empty wizard.
        /// Lands on step 2 AFTER subscribing StepChanged so the HeaderBar receives the step.
        /// </summary>
        private void OpenBoxSetForEdit(string slug)
        {
            var svc = new Services.BoxSetService();
            var existing = svc.Read(slug);
            if (existing == null)
            {
                NavigateToBoxSets(); // re-read from disk; drops the stale row
                App.Alerts.Notify(
                    "This box set could not be found; the list has been refreshed.",
                    AlertSeverity.Info, "Box Set Not Found");
                return;
            }

            var wizard = new BoxSetWizardView(svc, existing, slug);
            wizard.Completed += (s, a) => NavigateToBoxSets();
            wizard.StepChanged += (s, step) => HeaderBar.UpdateBoxSetWizardStep(step);

            _navigationService.NavigateToRoot(wizard);

            // After NavigateToRoot the HeaderBar is showing the wizard header (title "Edit Box Set"
            // via IsEditingExisting) on step 1; advance to step 2 now that StepChanged is wired.
            wizard.GoToStep(2);
        }

        // ===== WIZARD HEADER ROUTING =====
        // ShellWindow plumbs HeaderBar wizard-button events to the active wizard's methods.
        // The wizard owns its own state; ShellWindow doesn't track it beyond
        // _navigationService.CurrentView.

        private void HeaderBar_BoxSetWizardBackClicked(object? sender, EventArgs e)
        {
            if (_navigationService.CurrentView is BoxSetWizardView wizard)
                wizard.GoBack();
        }

        private void HeaderBar_BoxSetWizardNextClicked(object? sender, EventArgs e)
        {
            if (_navigationService.CurrentView is BoxSetWizardView wizard)
                wizard.GoNext();
        }

        private void HeaderBar_BoxSetWizardSaveClicked(object? sender, EventArgs e)
        {
            if (_navigationService.CurrentView is BoxSetWizardView wizard)
                wizard.Save();
        }

        private void HeaderBar_BoxSetWizardCancelClicked(object? sender, EventArgs e)
        {
            if (_navigationService.CurrentView is BoxSetWizardView wizard)
                wizard.Cancel();
        }

        private void HeaderBar_ConcertsSearchChanged(object? sender, string searchText)
        {
            if (_concertsView != null)
            {
                _concertsView.ApplyFilter(searchText);
                HeaderBar.UpdateConcertsCount(_concertsView.FilteredCount, _concertsView.TotalCount);
            }
        }

        private void HeaderBar_ConcertsOwnershipFilterChanged(object? sender, string filter)
        {
            if (_concertsView != null)
            {
                _concertsView.SetOwnershipFilter(filter);
                HeaderBar.UpdateConcertsCount(_concertsView.FilteredCount, _concertsView.TotalCount);
            }
        }

        /// <summary>
        /// Navigate to the Concert Detail view for a specific concert.
        /// Called by ConcertDatabaseView on double-click.
        /// </summary>
        public void NavigateToConcertDetail(ConcertReference concert, List<LibraryShow>? libraryShows = null)
        {
            var detailView = new ConcertDetailView(this, concert, libraryShows);
            _navigationService.NavigateTo(detailView, concert);
        }

        /// <summary>
        /// Opens a folder picker for importing a recording for a specific concert date.
        /// Called from ConcertDatabaseView right-click menu and ConcertDetailView import button.
        /// </summary>
        public void ImportForConcertDate(ConcertReference concert)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = $"Select recording folder for {concert.Date}"
            };

            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            var importView = EnsureImportView();
            importView.ImportForConcert(
                dlg.SelectedPath,
                concert.Date,
                concert.Venue,
                concert.FormattedLocation);

            _navigationService.NavigateToRoot(_importView!);
        }

        /// <summary>
        /// Refreshes the concerts view library dates from the current library shows.
        /// Called after library loads and after each import completion.
        /// </summary>
        private void RefreshConcertsLibraryDates()
        {
            if (_concertsView == null || _libraryView == null) return;

            var showsByDate = new Dictionary<string, List<LibraryShow>>();
            foreach (var show in _libraryView.Shows)
            {
                if (string.IsNullOrEmpty(show.Date)) continue;
                if (!showsByDate.TryGetValue(show.Date, out var list))
                {
                    list = new List<LibraryShow>();
                    showsByDate[show.Date] = list;
                }
                list.Add(show);
            }

            _concertsView.SetLibraryShows(showsByDate);
        }

        private void HeaderBar_EditSetlistRequested(object? sender, EventArgs e)
        {
            if (_navigationService.CurrentContext is ConcertReference concert)
            {
                var editView = new EditSetlistView(this, concert);

                editView.SaveCompleted += (s, args) =>
                {
                    // After save, navigate back happens inside EditSetlistView
                    // The ConcertDetailView will be on the back stack and will show the updated data
                };

                _navigationService.NavigateTo(editView, concert);
            }
        }

        /// <summary>
        /// Creates a brand-new concert by hand (add-concert-spec.md Decision 2). Constructs a blank
        /// ConcertReference and opens the setlist editor directly on it — no intermediate detail view.
        /// The editor's first save writes {date}.json and inserts the absent cache key; back-navigation
        /// lands on the Concerts grid, which RefreshFromCache repaints (a saved concert appears in
        /// sorted position; a cancelled one leaves no file and no cache entry).
        /// </summary>
        private void HeaderBar_NewConcertRequested(object? sender, EventArgs e)
        {
            var concert = new ConcertReference();
            var editView = new EditSetlistView(this, concert);

            editView.SaveCompleted += (s, args) =>
            {
                // After save, navigate back happens inside EditSetlistView. The Concerts grid is on
                // the back stack and RefreshFromCache (on navigation) shows the new row.
            };

            _navigationService.NavigateTo(editView, concert);
        }

        private async void HeaderBar_DeleteConcertRequested(object? sender, EventArgs e)
        {
            if (_navigationService.CurrentContext is not ConcertReference concert)
                return;

            // Bucket-B confirm (alert-system-spec.md #5). Branch mapping preserved from the old
            // YesNo/Warning MessageBox: Yes (true) -> delete; No/Esc (false) -> return. Event
            // subscription, so async void carries no call-site ripple.
            bool confirmed = await App.Alerts.ConfirmAsync(
                $"Delete the setlist for {concert.Date}?\n\n" +
                $"This will remove the concert JSON file from the database.",
                "Delete Concert");

            if (!confirmed)
                return;

            try
            {
                var filePath = Path.Combine(ConcertLookupService.AppDataConcertsPath, $"{concert.Date}.json");
                if (File.Exists(filePath))
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                        filePath,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }

                // Evict from the in-memory cache so the deleted concert no longer
                // resolves before restart (called regardless of whether the file existed).
                ConcertLookupService.Instance.Evict(concert.Date);

                // Navigate back to concerts grid
                NavigateToConcerts();
            }
            catch (Exception ex)
            {
                App.Alerts.Notify($"Error deleting concert:\n\n{ex.Message}", AlertSeverity.Error, "Delete Failed");
            }
        }

        public void NavigateToSettings()
        {
            if (_settingsView == null)
            {
                _settingsView = new SettingsView();
            }

            _navigationService.NavigateToRoot(_settingsView);
        }

        /// <summary>
        /// Reloads the persistent Library grid from disk. Public entry point so callers
        /// (e.g. SettingsView's data reset) can refresh the library regardless of which
        /// view is currently shown. The persistent _libraryView is created at startup
        /// (ctor -> NavigateToLibrary), so the null-conditional is just defensive.
        /// </summary>
        public void ReloadLibrary() => _libraryView?.ReloadLibrary();
    }
}
