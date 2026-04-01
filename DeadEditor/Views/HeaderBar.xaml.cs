using DeadEditor.Models;
using System;
using System.Windows;
using System.Windows.Controls;

namespace DeadEditor
{
    public partial class HeaderBar : System.Windows.Controls.UserControl
    {
        private AlbumDetailView? _currentAlbumView;
        private EditMetadataView? _currentEditView;
        private bool _isUpdatingSearch = false;

        /// <summary>Fired when the user clicks "← Library" from the Import header.</summary>
        public event EventHandler? ImportCancelRequested;

        /// <summary>Fired when the user clicks "Edit Metadata" from the Album Detail header.</summary>
        public event EventHandler? EditMetadataRequested;

        /// <summary>Fired when the search text or type filter changes.</summary>
        public event EventHandler<LibraryFilterEventArgs>? LibraryFilterChanged;

        /// <summary>Fired when Advanced Search is clicked.</summary>
        public event EventHandler? AdvancedSearchRequested;

        /// <summary>Fired when Delete is clicked from the Album Detail header.</summary>
        public event EventHandler? DeleteAlbumRequested;

        /// <summary>Fired when Edit Dates / Save / Cancel buttons are clicked.</summary>
        public event EventHandler? EditDatesRequested;
        public event EventHandler? SaveDatesRequested;
        public event EventHandler? CancelDatesRequested;

        public HeaderBar()
        {
            InitializeComponent();
        }

        /// <summary>Gets the current search text.</summary>
        public string SearchText => SearchBox?.Text ?? "";

        /// <summary>Gets the current type filter selection.</summary>
        public string TypeFilter
        {
            get
            {
                if (ShowTypeFilter?.SelectedItem is ComboBoxItem item)
                    return item.Content?.ToString() ?? "All";
                return "All";
            }
        }

        /// <summary>Focus the search box (for Ctrl+F shortcut).</summary>
        public void FocusSearch()
        {
            if (LibraryHeader.Visibility == Visibility.Visible)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            }
        }

        /// <summary>Clear the search box.</summary>
        public void ClearSearch()
        {
            if (!string.IsNullOrEmpty(SearchBox.Text))
            {
                SearchBox.Text = "";
            }
        }

        private void HideAllHeaders()
        {
            LibraryHeader.Visibility = Visibility.Collapsed;
            AlbumDetailHeader.Visibility = Visibility.Collapsed;
            ImportHeader.Visibility = Visibility.Collapsed;
            EditMetadataHeader.Visibility = Visibility.Collapsed;
            PlaceholderHeader.Visibility = Visibility.Collapsed;
        }

        public void ShowLibraryHeader(LibraryGridView libraryView)
        {
            HideAllHeaders();
            LibraryHeader.Visibility = Visibility.Visible;

            // Update concert count (filtered vs total)
            UpdateConcertCount(libraryView.FilteredCount, libraryView.ConcertCount, libraryView.IsByDateMode);
        }

        public void ShowAlbumDetailHeader(AlbumDetailView albumView, object? context)
        {
            _currentAlbumView = albumView;

            HideAllHeaders();
            AlbumDetailHeader.Visibility = Visibility.Visible;

            // Update album name and type
            AlbumNameText.Text = albumView.AlbumName;

            if (context is LibraryShow show)
            {
                AlbumTypeText.Text = show.Type == AlbumType.OfficialRelease ? "Official Release" : "Audience Recording";
            }
        }

        public void ShowEditMetadataHeader(EditMetadataView editView)
        {
            _currentEditView = editView;

            HideAllHeaders();
            EditMetadataHeader.Visibility = Visibility.Visible;

            // Back button shows "← {Album Name}"
            EditBackButton.Content = $"\u2190 {editView.AlbumName}";
        }

        public void ShowImportHeader()
        {
            HideAllHeaders();
            ImportHeader.Visibility = Visibility.Visible;
        }

        public void ShowSettingsHeader()
        {
            HideAllHeaders();
            PlaceholderHeader.Visibility = Visibility.Visible;
            PlaceholderText.Text = "Settings";
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            _currentAlbumView?.NavigateBack();
        }

        private void EditMetadataButton_Click(object sender, RoutedEventArgs e)
        {
            EditMetadataRequested?.Invoke(this, EventArgs.Empty);
        }

        private void EditBackButton_Click(object sender, RoutedEventArgs e)
        {
            _currentEditView?.NavigateBack();
        }

        private async void SaveChangesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentEditView != null)
            {
                await _currentEditView.SaveChangesAsync();
            }
        }

        private void CancelEditButton_Click(object sender, RoutedEventArgs e)
        {
            _currentEditView?.CancelEdit();
        }

        private void ImportBackButton_Click(object sender, RoutedEventArgs e)
        {
            ImportCancelRequested?.Invoke(this, EventArgs.Empty);
        }

        // ===== SEARCH & FILTER =====

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingSearch) return;

            // Show/hide placeholder and clear button
            var hasText = !string.IsNullOrEmpty(SearchBox.Text);
            SearchPlaceholder.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
            ClearSearchButton.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;

            RaiseFilterChanged();
        }

        private void ShowTypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Guard: this fires during InitializeComponent before other controls exist
            if (SearchBox == null) return;
            RaiseFilterChanged();

            // Show/hide Edit Dates button based on mode
            bool isByDate = TypeFilter == "By Date";
            UpdateDateEditButtons(isByDate, false);

            // Hide advanced search in "Shows I Don't Have" mode (not applicable)
            AdvancedSearchButton.Visibility = TypeFilter == "Shows I Don't Have"
                ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = "";
            SearchBox.Focus();
        }

        private void AdvancedSearchButton_Click(object sender, RoutedEventArgs e)
        {
            AdvancedSearchRequested?.Invoke(this, EventArgs.Empty);
        }

        private void DeleteAlbumButton_Click(object sender, RoutedEventArgs e)
        {
            DeleteAlbumRequested?.Invoke(this, EventArgs.Empty);
        }

        private void EditDatesButton_Click(object sender, RoutedEventArgs e)
        {
            EditDatesRequested?.Invoke(this, EventArgs.Empty);
        }

        private void SaveDatesButton_Click(object sender, RoutedEventArgs e)
        {
            SaveDatesRequested?.Invoke(this, EventArgs.Empty);
        }

        private void CancelDatesButton_Click(object sender, RoutedEventArgs e)
        {
            CancelDatesRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Updates the edit mode button visibility for By Date view.
        /// </summary>
        public void UpdateDateEditButtons(bool isByDateMode, bool isEditMode)
        {
            EditDatesButton.Visibility = (isByDateMode && !isEditMode) ? Visibility.Visible : Visibility.Collapsed;
            SaveDatesButton.Visibility = (isByDateMode && isEditMode) ? Visibility.Visible : Visibility.Collapsed;
            CancelDatesButton.Visibility = (isByDateMode && isEditMode) ? Visibility.Visible : Visibility.Collapsed;

            // Disable Show dropdown during editing to prevent switching away mid-edit
            ShowTypeFilter.IsEnabled = !isEditMode;
        }

        private void RaiseFilterChanged()
        {
            LibraryFilterChanged?.Invoke(this, new LibraryFilterEventArgs(SearchText, TypeFilter));
        }

        /// <summary>
        /// Updates the concert count display. Shows "X of Y concerts" when filtered,
        /// "X dates" in By Date mode, or "X shows you don't have" in missing shows mode.
        /// </summary>
        public void UpdateConcertCount(int filteredCount, int totalCount, bool isByDateMode = false, bool isMissingShowsMode = false)
        {
            if (isMissingShowsMode)
            {
                if (filteredCount == totalCount)
                {
                    ConcertCountText.Text = $"{totalCount:N0} shows you don't have";
                }
                else
                {
                    ConcertCountText.Text = $"{filteredCount:N0} of {totalCount:N0} missing shows";
                }
                return;
            }

            var unit = isByDateMode ? "dates" : "concerts";
            var singularUnit = isByDateMode ? "date" : "concert";

            if (filteredCount == totalCount)
            {
                ConcertCountText.Text = totalCount == 1 ? $"1 {singularUnit}" : $"{totalCount} {unit}";
            }
            else
            {
                ConcertCountText.Text = $"{filteredCount} of {totalCount} {unit}";
            }
        }
    }

    /// <summary>Event args for library search/filter changes.</summary>
    public class LibraryFilterEventArgs : EventArgs
    {
        public string SearchText { get; }
        public string TypeFilter { get; }

        public LibraryFilterEventArgs(string searchText, string typeFilter)
        {
            SearchText = searchText;
            TypeFilter = typeFilter;
        }
    }
}
