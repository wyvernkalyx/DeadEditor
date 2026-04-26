using DeadEditor.Models;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace DeadEditor
{
    public partial class HeaderBar : System.Windows.Controls.UserControl
    {
        private AlbumDetailView? _currentAlbumView;
        private EditMetadataView? _currentEditView;
        private ConcertDetailView? _currentConcertDetailView;
        private EditSetlistView? _currentEditSetlistView;
        private bool _isUpdatingSearch = false;
        private bool _isPopulatingYears = false;

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

        /// <summary>Fired when "Edit Setlist" is clicked from the Concert Detail header.</summary>
        public event EventHandler? EditSetlistRequested;

        /// <summary>Fired when "Delete" is clicked from the Concert Detail header.</summary>
        public event EventHandler? DeleteConcertRequested;

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

        /// <summary>Gets the current year filter selection.</summary>
        public string SelectedYear
        {
            get
            {
                if (YearFilter?.SelectedItem is ComboBoxItem item)
                    return item.Content?.ToString() ?? "All Years";
                return "All Years";
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

        /// <summary>Fired when the concerts search text changes.</summary>
        public event EventHandler<string>? ConcertsSearchChanged;

        /// <summary>Fired when the concerts ownership filter changes.</summary>
        public event EventHandler<string>? ConcertsOwnershipFilterChanged;

        private void HideAllHeaders()
        {
            LibraryHeader.Visibility = Visibility.Collapsed;
            AlbumDetailHeader.Visibility = Visibility.Collapsed;
            ImportHeader.Visibility = Visibility.Collapsed;
            EditMetadataHeader.Visibility = Visibility.Collapsed;
            ConcertsHeader.Visibility = Visibility.Collapsed;
            ConcertDetailHeader.Visibility = Visibility.Collapsed;
            EditSetlistHeader.Visibility = Visibility.Collapsed;
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

        public void ShowSongsHeader(SongsView songsView)
        {
            HideAllHeaders();
            PlaceholderHeader.Visibility = Visibility.Visible;
            PlaceholderText.Text = $"Songs    {songsView.SongCount} songs";
        }

        public void ShowReleasesHeader(ReleasesView releasesView)
        {
            HideAllHeaders();
            PlaceholderHeader.Visibility = Visibility.Visible;
            PlaceholderText.Text = $"Releases    {releasesView.ReleaseCount} known releases";
        }

        public void ShowConcertsHeader(ConcertDatabaseView concertsView)
        {
            HideAllHeaders();
            ConcertsHeader.Visibility = Visibility.Visible;
            UpdateConcertsCount(concertsView.FilteredCount, concertsView.TotalCount);
        }

        public void ShowConcertDetailHeader(ConcertDetailView detailView)
        {
            _currentConcertDetailView = detailView;

            HideAllHeaders();
            ConcertDetailHeader.Visibility = Visibility.Visible;
            ConcertDetailDateText.Text = detailView.ConcertDate;
        }

        public void ShowEditSetlistHeader(EditSetlistView editView)
        {
            _currentEditSetlistView = editView;

            HideAllHeaders();
            EditSetlistHeader.Visibility = Visibility.Visible;
            EditSetlistBackButton.Content = $"\u2190 {editView.VenueName}";
        }

        public void UpdateConcertsCount(int filtered, int total)
        {
            if (filtered == total)
                ConcertsCountText.Text = $"{total:N0} shows";
            else
                ConcertsCountText.Text = $"{filtered:N0} of {total:N0} shows";
        }

        /// <summary>Gets the current concerts search text.</summary>
        public string ConcertsSearchText => ConcertsSearchBox?.Text ?? "";

        public void ClearConcertsSearch()
        {
            if (ConcertsSearchBox != null && !string.IsNullOrEmpty(ConcertsSearchBox.Text))
                ConcertsSearchBox.Text = "";
        }

        private void ConcertsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var hasText = !string.IsNullOrEmpty(ConcertsSearchBox.Text);
            ConcertsSearchPlaceholder.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
            ConcertsClearSearchButton.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
            ConcertsSearchChanged?.Invoke(this, ConcertsSearchBox.Text ?? "");
        }

        private void ConcertsClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            ConcertsSearchBox.Text = "";
            ConcertsSearchBox.Focus();
        }

        private void ConcertsOwnershipFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ConcertsOwnershipFilter?.SelectedItem is ComboBoxItem item)
            {
                var filter = item.Content?.ToString() ?? "All";
                ConcertsOwnershipFilterChanged?.Invoke(this, filter);
            }
        }

        /// <summary>Resets the ownership filter to "All" (session-only persistence).</summary>
        public void ResetConcertsOwnershipFilter()
        {
            if (ConcertsOwnershipFilter != null)
                ConcertsOwnershipFilter.SelectedIndex = 0;
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

            bool isMissingShows = TypeFilter == "Shows I Don't Have";

            // Show/hide Year filter dropdown
            YearLabel.Visibility = isMissingShows ? Visibility.Visible : Visibility.Collapsed;
            YearFilter.Visibility = isMissingShows ? Visibility.Visible : Visibility.Collapsed;

            // Reset year to "All Years" when switching back to this mode
            if (isMissingShows && YearFilter.SelectedIndex != 0)
                YearFilter.SelectedIndex = 0;

            RaiseFilterChanged();

            // Show/hide Edit Dates button based on mode
            bool isByDate = TypeFilter == "By Date";
            UpdateDateEditButtons(isByDate, false);

            // Hide advanced search in "Shows I Don't Have" mode (not applicable)
            AdvancedSearchButton.Visibility = isMissingShows
                ? Visibility.Collapsed : Visibility.Visible;
        }

        private void YearFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SearchBox == null || _isPopulatingYears) return;
            RaiseFilterChanged();
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

        // ===== CONCERT DETAIL =====

        private void ConcertDetailBackButton_Click(object sender, RoutedEventArgs e)
        {
            _currentConcertDetailView?.NavigateBack();
        }

        private void EditSetlistButton_Click(object sender, RoutedEventArgs e)
        {
            EditSetlistRequested?.Invoke(this, EventArgs.Empty);
        }

        private void DeleteConcertButton_Click(object sender, RoutedEventArgs e)
        {
            DeleteConcertRequested?.Invoke(this, EventArgs.Empty);
        }

        // ===== EDIT SETLIST =====

        private void EditSetlistBackButton_Click(object sender, RoutedEventArgs e)
        {
            _currentEditSetlistView?.NavigateBack();
        }

        private async void SaveSetlistButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentEditSetlistView != null)
            {
                await _currentEditSetlistView.SaveChangesAsync();
            }
        }

        private void CancelSetlistButton_Click(object sender, RoutedEventArgs e)
        {
            _currentEditSetlistView?.CancelEdit();
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

        /// <summary>
        /// Populates the Year dropdown with distinct years from missing show dates.
        /// Called by LibraryGridView after building the missing shows list.
        /// </summary>
        public void PopulateYearFilter(List<string> missingYears)
        {
            _isPopulatingYears = true;
            try
            {
                var previousSelection = SelectedYear;
                YearFilter.Items.Clear();
                YearFilter.Items.Add(new ComboBoxItem { Content = "All Years" });
                foreach (var year in missingYears)
                {
                    YearFilter.Items.Add(new ComboBoxItem { Content = year });
                }

                // Restore previous selection if it still exists, otherwise default to All Years
                bool restored = false;
                if (previousSelection != "All Years")
                {
                    for (int i = 1; i < YearFilter.Items.Count; i++)
                    {
                        if (YearFilter.Items[i] is ComboBoxItem item && item.Content?.ToString() == previousSelection)
                        {
                            YearFilter.SelectedIndex = i;
                            restored = true;
                            break;
                        }
                    }
                }
                if (!restored)
                    YearFilter.SelectedIndex = 0;
            }
            finally
            {
                _isPopulatingYears = false;
            }
        }

        private void RaiseFilterChanged()
        {
            LibraryFilterChanged?.Invoke(this, new LibraryFilterEventArgs(SearchText, TypeFilter, SelectedYear));
        }

        /// <summary>
        /// Updates the concert count display. Shows "X of Y concerts" when filtered,
        /// "X dates" in By Date mode, or "X shows you don't have" in missing shows mode.
        /// </summary>
        public void UpdateConcertCount(int filteredCount, int totalCount, bool isByDateMode = false, bool isMissingShowsMode = false, int totalShowsInScope = 0)
        {
            if (isMissingShowsMode)
            {
                ConcertCountText.Text = $"Missing {filteredCount:N0} of {totalShowsInScope:N0} shows";
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
        public string YearFilter { get; }

        public LibraryFilterEventArgs(string searchText, string typeFilter, string yearFilter = "All Years")
        {
            SearchText = searchText;
            TypeFilter = typeFilter;
            YearFilter = yearFilter;
        }
    }
}
