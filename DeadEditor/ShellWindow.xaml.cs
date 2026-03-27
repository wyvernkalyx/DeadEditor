using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

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
            // Save window position
            _settings.LibraryWindowLeft = Left;
            _settings.LibraryWindowTop = Top;
            _settings.LibraryWindowWidth = Width;
            _settings.LibraryWindowHeight = Height;
            _settings.Save();
        }

        // ===== NAVIGATION =====

        private void NavigationService_NavigationRequested(object? sender, NavigationEventArgs e)
        {
            // Update current view
            CurrentView = e.View;

            // Update header bar based on view type
            UpdateHeaderBar(e.View, e.Context);
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
            else if (view is ImportView)
            {
                HeaderBar.ShowImportHeader();
            }
            else if (view is SettingsView)
            {
                HeaderBar.ShowSettingsHeader();
            }
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
            }

            // Navigate to root (clears back stack)
            _navigationService.NavigateToRoot(_libraryView);
        }

        private void NavigateToImport()
        {
            if (_importView == null)
            {
                _importView = new ImportView();
            }

            _navigationService.NavigateToRoot(_importView);
        }

        private void NavigateToSettings()
        {
            if (_settingsView == null)
            {
                _settingsView = new SettingsView();
            }

            _navigationService.NavigateToRoot(_settingsView);
        }
    }
}
