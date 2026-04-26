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
        private System.Windows.Controls.UserControl? _settingsView;

        // Public property for child views to access navigation
        public NavigationService Navigation => _navigationService;

        public ShellWindow()
        {
            InitializeComponent();

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
            HeaderBar.EditSetlistRequested += HeaderBar_EditSetlistRequested;
            HeaderBar.DeleteConcertRequested += HeaderBar_DeleteConcertRequested;

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
            else if (view is MbidMigrationView)
            {
                HeaderBar.ShowSettingsHeader();
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
                _libraryView.ApplyFilter(e.SearchText, e.TypeFilter, e.YearFilter);
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

        private void HeaderBar_DeleteAlbumRequested(object? sender, EventArgs e)
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

            var result = System.Windows.MessageBox.Show(
                $"Delete '{displayName}'?\n\n" +
                $"This will send {fileCount} audio file{(fileCount == 1 ? "" : "s")} to the Recycle Bin.\n\n" +
                $"This action can be undone by restoring from the Recycle Bin.",
                "Delete Album",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
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
                System.Windows.MessageBox.Show(
                    $"Error deleting album:\n\n{ex.Message}",
                    "Delete Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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

        private void HeaderBar_ImportCancelRequested(object? sender, EventArgs e)
        {
            if (_importView is ImportView importView && importView.HasUnsavedWork)
            {
                var result = System.Windows.MessageBox.Show(
                    "You have tracks loaded that haven't been imported. Leave Import?",
                    "Leave Import",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes) return;
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
            }

            // Navigate to root (clears back stack)
            _navigationService.NavigateToRoot(_libraryView);
        }

        private void NavigateToImport()
        {
            if (_importView == null)
            {
                var importView = new ImportView();

                // When an import completes, refresh the library grid
                importView.ImportCompleted += (s, destinationPath) =>
                {
                    // Reload library data so the new album shows up immediately
                    _libraryView?.ReloadLibrary();
                };

                _importView = importView;
            }

            _navigationService.NavigateToRoot(_importView);
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
            _navigationService.NavigateToRoot(_concertsView);
        }

        private void HeaderBar_ConcertsSearchChanged(object? sender, string searchText)
        {
            if (_concertsView != null)
            {
                _concertsView.ApplyFilter(searchText);
                HeaderBar.UpdateConcertsCount(_concertsView.FilteredCount, _concertsView.TotalCount);
            }
        }

        /// <summary>
        /// Navigate to the Concert Detail view for a specific concert.
        /// Called by ConcertDatabaseView on double-click.
        /// </summary>
        public void NavigateToConcertDetail(ConcertReference concert)
        {
            var detailView = new ConcertDetailView(this, concert);
            _navigationService.NavigateTo(detailView, concert);
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

        private void HeaderBar_DeleteConcertRequested(object? sender, EventArgs e)
        {
            if (_navigationService.CurrentContext is not ConcertReference concert)
                return;

            var result = System.Windows.MessageBox.Show(
                $"Delete the setlist for {concert.Date}?\n\n" +
                $"This will remove the concert JSON file from the database.",
                "Delete Concert",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
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

                // Navigate back to concerts grid
                NavigateToConcerts();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Error deleting concert:\n\n{ex.Message}",
                    "Delete Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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

        public void NavigateToMbidMigration()
        {
            // Get library shows for the migration view
            var libraryShows = _libraryView?.Shows?.ToList() ?? new List<LibraryShow>();
            var migrationView = new Views.MbidMigrationView(libraryShows);
            _navigationService.NavigateTo(migrationView);
        }
    }
}
