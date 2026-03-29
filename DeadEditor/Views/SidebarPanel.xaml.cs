using System;
using System.Windows;

namespace DeadEditor
{
    public partial class SidebarPanel : System.Windows.Controls.UserControl
    {
        public event EventHandler<string>? NavigationRequested;

        public SidebarPanel()
        {
            InitializeComponent();

            // Set Library as active by default
            SetActiveButton(LibraryButton);
        }

        private void LibraryButton_Click(object sender, RoutedEventArgs e)
        {
            SetActiveButton(LibraryButton);
            NavigationRequested?.Invoke(this, "Library");
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            SetActiveButton(ImportButton);
            NavigationRequested?.Invoke(this, "Import");
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SetActiveButton(SettingsButton);
            NavigationRequested?.Invoke(this, "Settings");
        }

        /// <summary>
        /// Updates the active sidebar icon to match the given view type.
        /// Called by ShellWindow on every navigation (not just sidebar clicks).
        /// </summary>
        public void SetActiveForView(System.Windows.Controls.UserControl view)
        {
            if (view is LibraryGridView or AlbumDetailView or EditMetadataView)
                SetActiveButton(LibraryButton);
            else if (view is ImportView)
                SetActiveButton(ImportButton);
            else if (view is SettingsView)
                SetActiveButton(SettingsButton);
        }

        private void SetActiveButton(System.Windows.Controls.Button activeButton)
        {
            // Clear all active states
            LibraryButton.Tag = null;
            ImportButton.Tag = null;
            SettingsButton.Tag = null;

            // Set active state
            activeButton.Tag = "Active";
        }
    }
}
