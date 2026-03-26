using DeadEditor.Models;
using System;
using System.Collections.Generic;
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
        private readonly Stack<System.Windows.Controls.UserControl> _navigationStack = new();
        private System.Windows.Controls.UserControl? _currentView;

        // View instances (created on demand)
        private System.Windows.Controls.UserControl? _libraryView;
        private System.Windows.Controls.UserControl? _importView;
        private System.Windows.Controls.UserControl? _settingsView;

        public ShellWindow()
        {
            InitializeComponent();

            _settings = LibrarySettings.Load();

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
            if (_libraryView == null)
            {
                _libraryView = new LibraryGridView();
            }

            CurrentView = _libraryView;
            _navigationStack.Clear();
            _navigationStack.Push(_libraryView);
        }

        private void NavigateToImport()
        {
            if (_importView == null)
            {
                _importView = new ImportView();
            }

            CurrentView = _importView;
            _navigationStack.Clear();
            _navigationStack.Push(_importView);
        }

        private void NavigateToSettings()
        {
            if (_settingsView == null)
            {
                _settingsView = new SettingsView();
            }

            CurrentView = _settingsView;
            _navigationStack.Clear();
            _navigationStack.Push(_settingsView);
        }

        // Future: NavigateTo(UserControl view) for drill-in navigation
        // Future: GoBack() for back navigation
    }
}
