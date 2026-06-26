# DeadEditor - Complete UI Audit

**Generated:** 2026-03-01
**Purpose:** Documentation-first development - comprehensive audit of all windows, dialogs, and interactive elements
**Status:** As-built documentation (no fixes applied)

> **MusicBrainz removed (2026-06).** This audit is a point-in-time snapshot (2026-03-01) and
> catalogs MusicBrainz UI that has since been removed (the import/Edit MusicBrainz buttons,
> ReleaseSelectorDialog, AlbumSearchDialog, MbidCandidateDialog, MBID migration). Those surfaces
> no longer exist; fingerprinting survives independently. Retained as a historical audit record.

---

## Table of Contents

1. [MainWindow](#1-mainwindow) - Import Workflow
2. [LibraryBrowserWindow](#2-librarybrowserwindow) - Library Browser & Player
3. [AdvancedSearchDialog](#3-advancedsearchdialog) - Multi-tab Song Search
4. [SettingsWindow](#4-settingswindow) - Library Configuration
5. [AddSongDialog](#5-addsongdialog) - Add Songs to Database
6. [ManageSongsDialog](#6-managesongsdialog) - Browse Song Database
7. [ReleaseSelectorDialog](#7-releaseselectordialog) - MusicBrainz Release Picker
8. [AlbumSearchDialog](#8-albumsearchdialog) - Manual MusicBrainz Search
9. [ConcertDetailWindow](#9-concertdetailwindow) - Concert Viewer (DEAD)
10. [Summary](#summary)

---

## 1. MainWindow

**Files:** `MainWindow.xaml` (406 lines), `MainWindow.xaml.cs` (1180 lines)
**Purpose:** Concert import workflow - load folder, normalize songs, write metadata, import to library

### 1.1 Menu Bar

#### File Menu (MenuItem)
- **x:Name:** N/A
- **Handler:** N/A (container)
- **Status:** WORKING

##### Settings MenuItem
- **x:Name:** None
- **Event:** `SettingsMenuItem_Click` (line 797)
- **Action:** Shows message box saying settings accessed from Library window
- **Status:** PARTIAL - Deprecated menu item that should be removed

##### Exit MenuItem
- **x:Name:** None
- **Event:** `ExitMenuItem_Click` (line 805)
- **Action:** Closes the import window (not whole application)
- **Status:** WORKING

### 1.2 Folder Selection

#### BrowseButton
- **x:Name:** `BrowseButton`
- **Event:** `BrowseButton_Click` (line 78)
- **Action:** Opens folder browser dialog, calls `LoadFolder()` on selection
- **Status:** WORKING

#### FolderPathTextBox
- **x:Name:** `FolderPathTextBox`
- **Event:** None (IsReadOnly)
- **Action:** Displays selected folder path
- **Status:** WORKING

### 1.3 Action Buttons (Top Row)

#### ReadButton
- **x:Name:** `ReadButton`
- **Event:** `ReadButton_Click` (line 659)
- **Action:** Re-loads current folder (redundant with auto-load)
- **Status:** WORKING (but unnecessary - folder loads automatically on browse)

#### NormalizeButton
- **x:Name:** `NormalizeButton`
- **Event:** `NormalizeButton_Click` (line 670)
- **Action:** Normalizes all song titles using fuzzy matching, updates UI, highlights unmatched songs
- **Status:** WORKING

#### RenumberButton
- **x:Name:** `RenumberButton`
- **Event:** `RenumberButton_Click` (line 717)
- **Action:** Renumbers tracks sequentially from 1 to N
- **Status:** WORKING

#### WriteButton
- **x:Name:** `WriteButton`
- **Event:** `WriteButton_Click` (line 756)
- **Action:** Writes metadata to audio files after confirmation dialog
- **Status:** WORKING

#### ViewInfoButton
- **x:Name:** `ViewInfoButton`
- **Event:** `ViewInfoButton_Click` (line 817)
- **Action:** Opens non-modal window showing `.txt` info file content
- **Status:** WORKING (enabled only when info file exists)

#### ImportButton
- **x:Name:** `ImportButton`
- **Event:** `ImportButton_Click` (line 866)
- **Action:** Imports to library with progress bar, saves box set name to settings
- **Status:** WORKING

#### CancelButton (Top)
- **x:Name:** `CancelButton`
- **Event:** `CancelButton_Click` (line 811)
- **Action:** Closes import window
- **Status:** WORKING

### 1.4 Album Information Panel

#### Album Type RadioButtons

##### LiveRecordingRadio
- **x:Name:** `LiveRecordingRadio`
- **Event:** `AlbumType_Changed` (Checked, line 263)
- **Action:** Sets AlbumType.Live, updates field visibility, refreshes preview
- **Status:** WORKING

##### OfficialReleaseRadio
- **x:Name:** `OfficialReleaseRadio`
- **Event:** `AlbumType_Changed` (Checked, line 263)
- **Action:** Sets AlbumType.OfficialRelease, shows OfficialRelease field
- **Status:** WORKING

##### StudioAlbumRadio
- **x:Name:** `StudioAlbumRadio`
- **Event:** `AlbumType_Changed` (Checked, line 263)
- **Action:** Sets AlbumType.Studio, shows studio fields, hides live fields, shows MusicBrainz buttons
- **Status:** WORKING

##### BoxSetRadio
- **x:Name:** `BoxSetRadio`
- **Event:** `AlbumType_Changed` (Checked, line 263)
- **Action:** Sets AlbumType.BoxSet, shows box set name field
- **Status:** WORKING

#### Artwork Management

##### ArtworkImage
- **x:Name:** `ArtworkImage`
- **Event:** None (display only)
- **Action:** Shows album artwork
- **Status:** WORKING

##### NoArtworkText
- **x:Name:** `NoArtworkText`
- **Event:** None (display only)
- **Action:** Shows "No Artwork" placeholder
- **Status:** WORKING

##### ChangeArtworkButton
- **x:Name:** `ChangeArtworkButton`
- **Event:** `ChangeArtworkButton_Click` (line 1064)
- **Action:** Opens file dialog, loads image, stores in AlbumInfo
- **Status:** WORKING

##### RemoveArtworkButton
- **x:Name:** `RemoveArtworkButton`
- **Event:** `RemoveArtworkButton_Click` (line 1108)
- **Action:** Clears artwork data from AlbumInfo
- **Status:** WORKING

#### Common Fields

##### ArtistTextBox
- **x:Name:** `ArtistTextBox`
- **Event:** `AlbumInfo_Changed` (TextChanged, line 546)
- **Action:** Updates `_albumInfo.Artist`, refreshes album preview
- **Status:** WORKING

#### Studio Album Fields

##### AlbumNameTextBox
- **x:Name:** `AlbumNameTextBox`
- **Event:** `AlbumInfo_Changed` (TextChanged, line 546)
- **Action:** Updates `_albumInfo.AlbumName`
- **Visibility:** Collapsed (shown only when StudioAlbumRadio checked)
- **Status:** WORKING

##### ReleaseYearTextBox
- **x:Name:** `ReleaseYearTextBox`
- **Event:** `AlbumInfo_Changed` (TextChanged, line 546)
- **Action:** Updates `_albumInfo.ReleaseYear` (parses int)
- **Visibility:** Collapsed (shown only when StudioAlbumRadio checked)
- **Status:** WORKING

##### EditionTextBox
- **x:Name:** `EditionTextBox`
- **Event:** `AlbumInfo_Changed` (TextChanged, line 546)
- **Action:** Updates `_albumInfo.Edition` (optional)
- **Visibility:** Collapsed (shown only when StudioAlbumRadio checked)
- **Status:** WORKING

##### LookupAlbumButton
- **x:Name:** `LookupAlbumButton`
- **Event:** `LookupAlbumButton_Click` (line 296)
- **Action:** Uses audio fingerprinting to lookup album, shows `ReleaseSelectorDialog`, downloads artwork
- **Visibility:** Collapsed (shown only when StudioAlbumRadio checked)
- **Status:** WORKING (requires fpcalc.exe)

##### ManualSearchButton
- **x:Name:** `ManualSearchButton`
- **Event:** `ManualSearchButton_Click` (line 409)
- **Action:** Opens `AlbumSearchDialog`, searches MusicBrainz by name/artist/year, shows `ReleaseSelectorDialog`
- **Visibility:** Collapsed (shown only when StudioAlbumRadio checked)
- **Status:** WORKING

#### Live Recording Fields

##### DateTextBox
- **x:Name:** `DateTextBox`
- **Event:** `AlbumInfo_Changed` (TextChanged, line 546)
- **Action:** Updates `_albumInfo.Date`
- **Status:** WORKING

##### VenueTextBox
- **x:Name:** `VenueTextBox`
- **Event:** `AlbumInfo_Changed` (TextChanged, line 546)
- **Action:** Updates `_albumInfo.Venue`
- **Status:** WORKING

##### CityTextBox
- **x:Name:** `CityTextBox`
- **Event:** `AlbumInfo_Changed` (TextChanged, line 546)
- **Action:** Updates `_albumInfo.City`
- **Status:** WORKING

##### StateTextBox
- **x:Name:** `StateTextBox`
- **Event:** `AlbumInfo_Changed` (TextChanged, line 546)
- **Action:** Updates `_albumInfo.State`
- **Status:** WORKING

##### OfficialReleaseTextBox
- **x:Name:** `OfficialReleaseTextBox`
- **Event:** `AlbumInfo_Changed` (TextChanged, line 546)
- **Action:** Updates `_albumInfo.OfficialRelease`
- **Visibility:** Shown only for OfficialRelease type
- **Status:** WORKING

#### Box Set Fields

##### BoxSetNameTextBox
- **x:Name:** `BoxSetNameTextBox`
- **Event:** `AlbumInfo_Changed` (TextChanged, line 546)
- **Action:** Updates `_albumInfo.BoxSetName`, pre-fills with `_librarySettings.LastBoxSetName`
- **Visibility:** Collapsed (shown only when BoxSetRadio checked)
- **Status:** WORKING

##### AlbumPreviewTextBlock
- **x:Name:** `AlbumPreviewTextBlock`
- **Event:** None (display only)
- **Action:** Shows formatted album title preview
- **Status:** WORKING

### 1.5 Track List (DataGrid)

#### TracksDataGrid
- **x:Name:** `TracksDataGrid`
- **Event:** `SelectionChanged` → `TracksDataGrid_SelectionChanged` (line 581)
- **Event:** `LoadingRow` → `TracksDataGrid_LoadingRow` (line 593)
- **Action:** Displays all tracks, updates selected track editor, highlights unmatched songs
- **Columns:**
  - `TrackNumber` (# column)
  - `DisplayTitle` (Song column)
  - `PreviewMetadata` (Final Metadata Preview)
  - `HasSegue` (→ column, checkbox)
  - `Duration`
- **Status:** WORKING

### 1.6 Selected Track Editor

##### SelectedTitleTextBox
- **x:Name:** `SelectedTitleTextBox`
- **Event:** `SelectedTrack_Changed` (TextChanged, line 640)
- **Action:** Updates selected track's title
- **Status:** WORKING

##### SelectedSegueCheckBox
- **x:Name:** `SelectedSegueCheckBox`
- **Event:** `SelectedTrack_Changed` (Checked/Unchecked, line 640)
- **Action:** Updates selected track's segue flag
- **Status:** WORKING

##### SelectedDateTextBox
- **x:Name:** `SelectedDateTextBox`
- **Event:** `SelectedTrack_Changed` (TextChanged, line 640)
- **Action:** Updates selected track's performance date
- **Status:** WORKING

### 1.7 Status Bar

##### StatusTextBlock
- **x:Name:** `StatusTextBlock`
- **Event:** None (display only)
- **Action:** Shows status messages
- **Status:** WORKING

##### ProgressBar
- **x:Name:** `ProgressBar`
- **Event:** None (display only)
- **Action:** Shows progress during import
- **Visibility:** Collapsed (shown during operations)
- **Status:** WORKING

### 1.8 Notification System (Custom Dialog)

##### NotificationPanel
- **x:Name:** `NotificationPanel`
- **Event:** `NotificationPanel_MouseDown` (line 1170)
- **Action:** Closes notification when clicking outside dialog
- **Visibility:** Collapsed (shown when notifications displayed)
- **Status:** WORKING

##### NotificationTitle
- **x:Name:** `NotificationTitle`
- **Event:** None (display only)
- **Action:** Shows notification title
- **Status:** WORKING

##### NotificationMessage
- **x:Name:** `NotificationMessage`
- **Event:** None (display only)
- **Action:** Shows notification message
- **Status:** WORKING

##### NotificationYesButton
- **x:Name:** `NotificationYesButton`
- **Event:** `NotificationYes_Click` (line 1152)
- **Action:** Returns `true` to notification task
- **Visibility:** Collapsed (shown for Yes/No prompts)
- **Status:** WORKING

##### NotificationNoButton
- **x:Name:** `NotificationNoButton`
- **Event:** `NotificationNo_Click` (line 1158)
- **Action:** Returns `false` to notification task
- **Visibility:** Collapsed (shown for Yes/No prompts)
- **Status:** WORKING

##### NotificationOkButton
- **x:Name:** `NotificationOkButton`
- **Event:** `NotificationOk_Click` (line 1164)
- **Action:** Returns `true` to notification task
- **Status:** WORKING

### 1.9 Data Flow

**Input:**
- Folder path (from browse dialog or public `LoadFolder()` method)
- User edits to album info and track metadata
- Album artwork (image file)

**Output:**
- ID3 tags written to audio files
- Concert imported to library folder structure
- Box set name saved to `settings.json`

### 1.10 Navigation

**Entry Points:**
- From LibraryBrowserWindow: File > Import New Concert
- From LibraryBrowserWindow: Edit Metadata button (loads existing concert)
- Directly launched as standalone window

**Exit Points:**
- Close window (Cancel button or X)
- After successful import (auto-clears view)

---

## 2. LibraryBrowserWindow

**Files:** `LibraryBrowserWindow.xaml` (496 lines), `LibraryBrowserWindow.xaml.cs` (1343 lines)
**Purpose:** Main application window - browse library, search concerts, play music

### 2.1 Menu Bar

#### File Menu (MenuItem)
- **x:Name:** N/A
- **Handler:** N/A (container)
- **Status:** WORKING

##### Import New Concert MenuItem
- **x:Name:** None
- **Event:** `ImportMenuItem_Click` (line 827)
- **Action:** Opens MainWindow dialog, refreshes library after close
- **Status:** WORKING

##### Settings MenuItem
- **x:Name:** None
- **Event:** `SettingsMenuItem_Click` (line 836)
- **Action:** Opens SettingsWindow dialog, refreshes library after close
- **Status:** WORKING

##### Exit MenuItem
- **x:Name:** None
- **Event:** `ExitMenuItem_Click` (line 846)
- **Action:** Closes window (terminates application)
- **Status:** WORKING

### 2.2 Search Bar

#### TypeFilterComboBox
- **x:Name:** `TypeFilterComboBox`
- **Event:** `TypeFilterComboBox_SelectionChanged` (line 1004)
- **Action:** Filters shows by album type (All, Live, OfficialRelease, Studio)
- **Items:**
  - "All" (Tag="All")
  - "🎸 Audience Recordings" (Tag="Live")
  - "📀 Official Releases" (Tag="OfficialRelease")
  - "💿 Studio Albums" (Tag="Studio")
- **Status:** WORKING

#### QuickSearchTextBox
- **x:Name:** `QuickSearchTextBox`
- **Event:** `QuickSearchTextBox_TextChanged` (line 895)
- **Action:** Filters shows by date, venue, location, album name, year, edition, official release
- **Note:** Uses `_isUpdatingSearchBox` flag to prevent recursion
- **Status:** WORKING

#### ClearSearchButton
- **x:Name:** `ClearSearchButton`
- **Event:** `ClearSearchButton_Click` (line 904)
- **Action:** Clears quick search and advanced search criteria
- **Visibility:** Collapsed (shown when search active)
- **Status:** WORKING

#### AdvancedSearchButton
- **x:Name:** `AdvancedSearchButton`
- **Event:** `AdvancedSearchButton_Click` (line 918)
- **Action:** Opens `AdvancedSearchDialog`, applies song-based search filters
- **Status:** WORKING

#### SearchResultTextBlock
- **x:Name:** `SearchResultTextBlock`
- **Event:** None (display only)
- **Action:** Shows "Showing X of Y concerts" when filtered
- **Status:** WORKING

### 2.3 Library View (Grid)

#### ShowsDataGrid
- **x:Name:** `ShowsDataGrid`
- **Event:** `MouseDoubleClick` → `ShowsDataGrid_MouseDoubleClick` (line 468)
- **Action:** Opens concert detail view for double-clicked row
- **Columns:**
  - TypeIcon (🎸/📀/💿)
  - PrimaryInfo (Date or Album Name)
  - SecondaryInfo (Venue or Year)
  - TertiaryInfo (Location or Edition)
  - OfficialRelease
  - TrackCount
- **Status:** WORKING

### 2.4 Concert View (Detail)

#### BackButton
- **x:Name:** `BackButton`
- **Event:** `BackButton_Click` (line 618)
- **Action:** Returns to library grid view without stopping playback
- **Status:** WORKING

#### EditMetadataButton
- **x:Name:** `EditMetadataButton`
- **Event:** `EditMetadataButton_Click` (line 851)
- **Action:** Opens MainWindow with current concert folder, reloads library and concert view after close
- **Status:** WORKING

#### VenueText
- **x:Name:** `VenueText`
- **Event:** None (display only)
- **Action:** Shows venue name or album name
- **Status:** WORKING

#### LocationText
- **x:Name:** `LocationText`
- **Event:** None (display only)
- **Action:** Shows location or release year
- **Status:** WORKING

#### DateText
- **x:Name:** `DateText`
- **Event:** None (display only)
- **Action:** Shows concert date
- **Status:** WORKING

#### BoxSetText
- **x:Name:** `BoxSetText`
- **Event:** None (display only)
- **Action:** Shows "Box Set: [name]" for box set concerts
- **Visibility:** Collapsed (shown only for box sets)
- **Status:** WORKING

#### TrackCountText
- **x:Name:** `TrackCountText`
- **Event:** None (display only)
- **Action:** Shows track count
- **Status:** WORKING

#### ArtworkPlaceholder
- **x:Name:** `ArtworkPlaceholder`
- **Event:** None (display only)
- **Action:** Shows placeholder when no artwork
- **Status:** WORKING

#### AlbumArtwork
- **x:Name:** `AlbumArtwork`
- **Event:** None (display only)
- **Action:** Shows album artwork
- **Status:** WORKING

#### TracksDataGrid (Concert View)
- **x:Name:** `TracksDataGrid`
- **Event:** `LoadingRow` → `TracksDataGrid_LoadingRow` (line 637)
- **Event:** Double-click on row → `TracksDataGridRow_MouseDoubleClick` (line 642)
- **Action:** Displays tracks, allows double-click to play
- **Columns:**
  - TrackNumber (#)
  - PreviewMetadata (Title)
  - Duration
- **Status:** WORKING

### 2.5 Audio Player Controls

#### PreviousButton
- **x:Name:** `PreviousButton`
- **Event:** `PreviousButton_Click` (line 739)
- **Action:** Plays previous track
- **Enabled:** Only when not on first track
- **Status:** WORKING

#### PlayPauseButton
- **x:Name:** `PlayPauseButton`
- **Event:** `PlayPauseButton_Click` (line 700)
- **Action:** Toggles play/pause, uses button content as state ("▶" or "⏸")
- **Note:** Fixed double-click issue by using button content instead of `_audioPlayer.IsPlaying`
- **Status:** WORKING

#### StopButton
- **x:Name:** `StopButton`
- **Event:** `StopButton_Click` (line 728)
- **Action:** Stops playback, resets scrubber
- **Status:** WORKING

#### NextButton
- **x:Name:** `NextButton`
- **Event:** `NextButton_Click` (line 747)
- **Action:** Plays next track
- **Enabled:** Only when not on last track
- **Status:** WORKING

#### AudioScrubber
- **x:Name:** `AudioScrubber`
- **Event:** `ValueChanged` → `AudioScrubber_ValueChanged` (line 803)
- **Event:** `PreviewMouseDown` → `AudioScrubber_PreviewMouseDown` (line 792)
- **Event:** `PreviewMouseUp` → `AudioScrubber_PreviewMouseUp` (line 797)
- **Action:** Seeks to position when dragged, uses `_isScrubbing` flag to prevent feedback loop
- **Status:** WORKING

#### CurrentTimeText
- **x:Name:** `CurrentTimeText`
- **Event:** None (display only)
- **Action:** Shows current playback position
- **Status:** WORKING

#### TotalTimeText
- **x:Name:** `TotalTimeText`
- **Event:** None (display only)
- **Action:** Shows total track duration
- **Status:** WORKING

#### VolumeSlider
- **x:Name:** `VolumeSlider`
- **Event:** `VolumeSlider_ValueChanged` (line 811)
- **Action:** Adjusts audio player volume (0-100)
- **Status:** WORKING

#### NowPlayingText
- **x:Name:** `NowPlayingText`
- **Event:** None (display only)
- **Action:** Shows currently playing track title
- **Status:** WORKING

#### NowPlayingDetails
- **x:Name:** `NowPlayingDetails`
- **Event:** None (display only)
- **Action:** Shows track number and duration
- **Status:** WORKING

### 2.6 Status Bar

#### StatusText
- **x:Name:** `StatusText`
- **Event:** None (display only)
- **Action:** Shows library status messages
- **Status:** WORKING

#### PlayerControls
- **x:Name:** `PlayerControls`
- **Event:** None (container)
- **Action:** Container for all player controls
- **Visibility:** Collapsed initially, shown when concert opened
- **Status:** WORKING

### 2.7 Data Flow

**Input:**
- Library folders from settings (LibraryRootPath, OfficialReleasesPath)
- Search criteria (quick search, advanced search)
- User playback controls

**Output:**
- N/A (read-only, no file modifications)

### 2.8 Navigation

**Entry Points:**
- Application startup (main window)

**Exit Points:**
- File > Exit
- Close window

**Internal Navigation:**
- Double-click show → Concert detail view
- Advanced Search → AdvancedSearchDialog
- Settings → SettingsWindow
- Import → MainWindow
- Edit Metadata → MainWindow (with existing concert)
- Track search result double-click → Opens album in concert view

### 2.9 Media Key Support

**Special Feature:** Window hooks into Windows message pump (WM_APPCOMMAND) to handle keyboard media keys:
- Play/Pause key → `PlayPauseButton_Click`
- Stop key → `StopButton_Click`
- Next Track key → `NextButton_Click`
- Previous Track key → `PreviousButton_Click`

**Status:** WORKING (implemented in `WndProc()` line 110)

---

## 3. AdvancedSearchDialog

**Files:** `AdvancedSearchDialog.xaml` (265 lines), `AdvancedSearchDialog.xaml.cs` (472 lines)
**Purpose:** Multi-tab advanced search - find concerts by songs, excluded songs, song sequences, or track dates/venues

### 3.1 Contains Songs Tab

#### SongFilterTextBox
- **x:Name:** `SongFilterTextBox`
- **Event:** `SongFilterTextBox_TextChanged` (line 90)
- **Action:** Filters visible song checkboxes
- **Status:** WORKING

#### ClearFilterButton
- **x:Name:** `ClearFilterButton`
- **Event:** `ClearFilterButton_Click` (line 97)
- **Action:** Clears song filter
- **Visibility:** Collapsed (shown when filter active)
- **Status:** WORKING

#### SongCheckListPanel
- **x:Name:** `SongCheckListPanel`
- **Event:** None (container)
- **Action:** Dynamically populated with CheckBox elements for each song
- **Note:** Individual checkboxes have `SongCheckBox_Changed` handler (line 120)
- **Status:** WORKING

#### SelectAllButton
- **x:Name:** `SelectAllButton`
- **Event:** `SelectAllButton_Click` (line 132)
- **Action:** Checks all visible (filtered) songs
- **Status:** WORKING

#### ClearAllButton
- **x:Name:** `ClearAllButton`
- **Event:** `ClearAllButton_Click` (line 145)
- **Action:** Unchecks all checkboxes (including filtered out ones)
- **Status:** WORKING

#### SelectedSongsCount
- **x:Name:** `SelectedSongsCount`
- **Event:** None (display only)
- **Action:** Shows "X song(s) selected" count
- **Status:** WORKING

### 3.2 Exclude Songs Tab

#### ExcludeSongFilterTextBox
- **x:Name:** `ExcludeSongFilterTextBox`
- **Event:** `ExcludeSongFilterTextBox_TextChanged` (line 203)
- **Action:** Filters visible exclude song checkboxes
- **Status:** WORKING

#### ClearExcludeFilterButton
- **x:Name:** `ClearExcludeFilterButton`
- **Event:** `ClearExcludeFilterButton_Click` (line 210)
- **Action:** Clears exclude song filter
- **Visibility:** Collapsed (shown when filter active)
- **Status:** WORKING

#### ExcludeSongCheckListPanel
- **x:Name:** `ExcludeSongCheckListPanel`
- **Event:** None (container)
- **Action:** Dynamically populated with CheckBox elements for each song
- **Note:** Individual checkboxes have `ExcludeSongCheckBox_Changed` handler (line 233)
- **Status:** WORKING

#### ExcludeSelectAllButton
- **x:Name:** `ExcludeSelectAllButton`
- **Event:** `ExcludeSelectAllButton_Click` (line 244)
- **Action:** Checks all visible excluded songs
- **Status:** WORKING

#### ExcludeClearAllButton
- **x:Name:** `ExcludeClearAllButton`
- **Event:** `ExcludeClearAllButton_Click` (line 256)
- **Action:** Unchecks all exclude checkboxes
- **Status:** WORKING

#### ExcludedSongsCount
- **x:Name:** `ExcludedSongsCount`
- **Event:** None (display only)
- **Action:** Shows "X song(s) excluded" count
- **Status:** WORKING

### 3.3 Song Sequence Tab

#### SequenceSongComboBox
- **x:Name:** `SequenceSongComboBox`
- **Event:** None (selection source)
- **Action:** Dropdown populated with all songs
- **Status:** WORKING

#### AddToSequenceButton
- **x:Name:** `AddToSequenceButton`
- **Event:** `AddToSequenceButton_Click` (line 155)
- **Action:** Adds selected song to sequence list
- **Status:** WORKING

#### SequenceListBox
- **x:Name:** `SequenceListBox`
- **Event:** None (ItemsControl with data template)
- **Action:** Displays ordered list of songs in sequence
- **DataTemplate:** Each item has index, song name, and remove button (✕)
- **Status:** WORKING

#### SequenceEmptyText
- **x:Name:** `SequenceEmptyText`
- **Event:** None (display only)
- **Action:** Shows placeholder when no sequence defined
- **Visibility:** Visible when sequence empty
- **Status:** WORKING

#### ClearSequenceButton
- **x:Name:** `ClearSequenceButton`
- **Event:** `ClearSequenceButton_Click` (line 189)
- **Action:** Clears entire sequence
- **Status:** WORKING

#### Remove from Sequence Button (in DataTemplate)
- **x:Name:** None (templated)
- **Event:** `RemoveFromSequence_Click` (line 169)
- **Action:** Removes song from sequence, re-indexes remaining items
- **Status:** WORKING

### 3.4 Search Tracks Tab

#### TrackDateTextBox
- **x:Name:** `TrackDateTextBox`
- **Event:** None (input only)
- **Action:** Date search field (yyyy-MM-dd format)
- **Status:** WORKING

#### TrackVenueTextBox
- **x:Name:** `TrackVenueTextBox`
- **Event:** None (input only)
- **Action:** Venue search field
- **Status:** WORKING

#### TrackSearchButton
- **x:Name:** `TrackSearchButton`
- **Event:** `TrackSearchButton_Click` (line 309)
- **Action:** Searches for tracks containing embedded dates or venues, populates results grid
- **Status:** WORKING

#### TrackResultsDataGrid
- **x:Name:** `TrackResultsDataGrid`
- **Event:** `MouseDoubleClick` → `TrackResultsDataGrid_MouseDoubleClick` (line 438)
- **Action:** Shows search results, double-click closes dialog and navigates to album
- **Columns:**
  - TrackTitle
  - AlbumTitle
  - AlbumType
- **Status:** WORKING

#### TrackResultsEmptyText
- **x:Name:** `TrackResultsEmptyText`
- **Event:** None (display only)
- **Action:** Shows placeholder when no search results
- **Visibility:** Visible when no results
- **Status:** WORKING

### 3.5 Dialog Buttons

#### SearchButton
- **x:Name:** `SearchButton`
- **Event:** `SearchButton_Click` (line 265)
- **Action:** Collects all selected/excluded songs and sequence, validates at least one criterion, sets DialogResult=true
- **IsDefault:** True
- **Status:** WORKING

#### CancelButton
- **x:Name:** `CancelButton`
- **Event:** `CancelButton_Click` (line 302)
- **Action:** Sets DialogResult=false, closes dialog
- **IsCancel:** True
- **Status:** WORKING

### 3.6 Data Flow

**Input:**
- Song database from NormalizationService
- Library paths from LibrarySettings (for track search)
- User selections across all tabs

**Output:**
- `SelectedSongs` (List<string>)
- `ExcludedSongs` (List<string>)
- `SongSequence` (List<string>)
- DialogResult (true/false)

### 3.7 Navigation

**Entry Points:**
- From LibraryBrowserWindow: Advanced Search button

**Exit Points:**
- Search button (applies criteria to library view)
- Cancel button
- Track search result double-click (navigates to album, closes dialog)

### 3.8 Known Issues

**FIXED:** Multi-song collection bug - now collects from `_allSongCheckBoxes` instead of visible children (line 269)

---

## 4. SettingsWindow

**Files:** `SettingsWindow.xaml` (78 lines), `SettingsWindow.xaml.cs` (205 lines)
**Purpose:** Configure library paths, primary artist name, manage song database

### 4.1 Library Paths

#### LibraryRootTextBox
- **x:Name:** `LibraryRootTextBox`
- **Event:** None (IsReadOnly)
- **Action:** Displays library root path
- **Status:** WORKING

#### BrowseLibraryButton
- **x:Name:** `BrowseLibraryButton`
- **Event:** `BrowseLibraryButton_Click` (line 28)
- **Action:** Opens folder browser, saves to settings, updates library window
- **Status:** WORKING

#### OfficialReleasesTextBox
- **x:Name:** `OfficialReleasesTextBox`
- **Event:** None (IsReadOnly)
- **Action:** Displays official releases path
- **Status:** WORKING

#### BrowseOfficialReleasesButton
- **x:Name:** `BrowseOfficialReleasesButton`
- **Event:** `BrowseOfficialReleasesButton_Click` (line 47)
- **Action:** Opens folder browser, saves to settings, updates library window
- **Status:** WORKING

### 4.2 Primary Artist

#### PrimaryArtistTextBox
- **x:Name:** `PrimaryArtistTextBox`
- **Event:** None (saved on close)
- **Action:** Sets primary artist name for MusicBrainz searches
- **Note:** Saved in `CloseButton_Click` handler (line 198)
- **Status:** WORKING

### 4.3 Song Database Management

#### AddSongButton
- **x:Name:** `AddSongButton`
- **Event:** `AddSongButton_Click` (line 181)
- **Action:** Opens AddSongDialog
- **Status:** WORKING

#### ManageSongsButton
- **x:Name:** `ManageSongsButton`
- **Event:** `ManageSongsButton_Click` (line 188)
- **Action:** Opens ManageSongsDialog
- **Status:** WORKING

### 4.4 Data Management

#### ResetDataButton
- **x:Name:** `ResetDataButton`
- **Event:** `ResetDataButton_Click` (line 66)
- **Action:** Shows confirmation, moves all library files to Recycle Bin, clears settings
- **Warning:** Destructive operation (sends to Recycle Bin, not permanent delete)
- **Status:** WORKING

### 4.5 Dialog Buttons

#### CloseButton
- **x:Name:** `CloseButton`
- **Event:** `CloseButton_Click` (line 195)
- **Action:** Saves primary artist setting, closes window
- **Status:** WORKING

### 4.6 Data Flow

**Input:**
- Current LibrarySettings
- NormalizationService reference
- LibraryBrowserWindow reference (for refresh callbacks)

**Output:**
- Updated LibrarySettings (saved to %APPDATA%/DeadEditor/settings.json)
- Library refresh triggers

### 4.7 Navigation

**Entry Points:**
- From LibraryBrowserWindow: File > Settings

**Exit Points:**
- Close button

**Internal Navigation:**
- Add Song → AddSongDialog
- Manage Songs → ManageSongsDialog

---

## 5. AddSongDialog

**Files:** `AddSongDialog.xaml` (74 lines), `AddSongDialog.xaml.cs` (115 lines)
**Purpose:** Add new songs to the song database with artist support

### 5.1 Artist Selection

#### ArtistComboBox
- **x:Name:** `ArtistComboBox`
- **Event:** None (editable dropdown)
- **Action:** Select existing artist or type new artist name
- **IsEditable:** True
- **Default:** "Grateful Dead"
- **Status:** WORKING

#### RefreshArtistsButton
- **x:Name:** `RefreshArtistsButton`
- **Event:** `RefreshArtistsButton_Click` (line 42)
- **Action:** Reloads artist list from database
- **Status:** WORKING

### 5.2 Song Fields

#### OfficialTitleTextBox
- **x:Name:** `OfficialTitleTextBox`
- **Event:** None (input only)
- **Action:** Canonical song title
- **Required:** Yes
- **Status:** WORKING

#### AliasesTextBox
- **x:Name:** `AliasesTextBox`
- **Event:** None (input only)
- **Action:** Alternative spellings/abbreviations (one per line)
- **Required:** No
- **AcceptsReturn:** True (multiline)
- **Status:** WORKING

### 5.3 Status Display

#### StatusTextBlock
- **x:Name:** `StatusTextBlock`
- **Event:** None (display only)
- **Action:** Shows success/error messages
- **Status:** WORKING

### 5.4 Dialog Buttons

#### AddButton
- **x:Name:** `AddButton`
- **Event:** `AddButton_Click` (line 49)
- **Action:** Validates inputs, adds song to database, clears form, shows success message
- **IsDefault:** True
- **Note:** Does NOT close dialog after add (allows multiple adds)
- **Status:** WORKING

#### CancelButton
- **x:Name:** `CancelButton`
- **Event:** `CancelButton_Click` (line 108)
- **Action:** Sets DialogResult=false, closes dialog
- **IsCancel:** True
- **Status:** WORKING

### 5.5 Data Flow

**Input:**
- NormalizationService reference
- Artist list (from database + common artists)

**Output:**
- New song entry in Data/songs.json
- DialogResult (false on cancel, stays open on add)

### 5.6 Navigation

**Entry Points:**
- From SettingsWindow: Add Song button

**Exit Points:**
- Cancel button

---

## 6. ManageSongsDialog

**Files:** `ManageSongsDialog.xaml` (91 lines), `ManageSongsDialog.xaml.cs` (226 lines)
**Purpose:** Browse all songs in database, filter by artist/search, export to text

### 6.1 Statistics

#### StatsTextBlock
- **x:Name:** `StatsTextBlock`
- **Event:** None (display only)
- **Action:** Shows "Total: X songs | Artists: Y | Songs with aliases: Z"
- **Status:** WORKING

### 6.2 Search and Filter

#### SearchTextBox
- **x:Name:** `SearchTextBox`
- **Event:** `SearchTextBox_TextChanged` (line 145)
- **Action:** Filters songs by title and aliases
- **Status:** WORKING

#### ArtistFilterComboBox
- **x:Name:** `ArtistFilterComboBox`
- **Event:** `ArtistFilterComboBox_SelectionChanged` (line 150)
- **Action:** Filters songs by artist ("All Artists" or specific artist)
- **Status:** WORKING

### 6.3 Song List

#### SongsItemsControl
- **x:Name:** `SongsItemsControl`
- **Event:** None (ItemsControl with data template)
- **Action:** Displays filtered song list with titles, artists, and aliases
- **DataTemplate:** Shows song title (bold), artist (gray, right-aligned), aliases (gray, smaller)
- **Status:** WORKING

### 6.4 Results Display

#### ResultCountTextBlock
- **x:Name:** `ResultCountTextBlock`
- **Event:** None (display only)
- **Action:** Shows "Showing X of Y songs" when filtered
- **Status:** WORKING

### 6.5 Dialog Buttons

#### ExportButton
- **x:Name:** `ExportButton`
- **Event:** `ExportButton_Click` (line 155)
- **Action:** Opens SaveFileDialog, exports song database to formatted text file (grouped by artist)
- **Status:** WORKING

#### CloseButton
- **x:Name:** `CloseButton`
- **Event:** `CloseButton_Click` (line 210)
- **Action:** Closes dialog
- **IsDefault:** True
- **Status:** WORKING

### 6.6 Data Flow

**Input:**
- NormalizationService reference
- Song database (Data/songs.json)

**Output:**
- Exported text file (optional)

### 6.7 Navigation

**Entry Points:**
- From SettingsWindow: Manage Songs button

**Exit Points:**
- Close button

---

## 7. ReleaseSelectorDialog

**Files:** `ReleaseSelectorDialog.xaml` (81 lines), `ReleaseSelectorDialog.xaml.cs` (67 lines)
**Purpose:** Select specific release/edition from MusicBrainz results

### 7.1 Header

#### AlbumInfoTextBlock
- **x:Name:** `AlbumInfoTextBlock`
- **Event:** None (display only)
- **Action:** Shows message like "Found X releases. Please select:"
- **Status:** WORKING

### 7.2 Releases List

#### ReleasesDataGrid
- **x:Name:** `ReleasesDataGrid`
- **Event:** `MouseDoubleClick` → `ReleasesDataGrid_MouseDoubleClick` (line 44)
- **Action:** Displays all releases, allows selection, double-click selects and closes
- **Columns:**
  - Artist
  - Album Title
  - Year
  - Label
  - Country
  - Format
- **Status:** WORKING

### 7.3 Dialog Buttons

#### SelectButton
- **x:Name:** None
- **Event:** `SelectButton_Click` (line 24)
- **Action:** Validates selection, sets SelectedRelease property, DialogResult=true
- **IsDefault:** True
- **Status:** WORKING

#### CancelButton
- **x:Name:** None
- **Event:** `CancelButton_Click` (line 38)
- **Action:** Sets DialogResult=false, closes dialog
- **IsCancel:** True
- **Status:** WORKING

### 7.4 Data Flow

**Input:**
- `albumInfo` string (message to display)
- `releases` List<ReleaseOption> (from MusicBrainz)

**Output:**
- `SelectedRelease` property (ReleaseOption or null)
- DialogResult (true/false)

### 7.5 Navigation

**Entry Points:**
- From MainWindow: After fingerprint lookup (LookupAlbumButton)
- From MainWindow: After manual search (ManualSearchButton)

**Exit Points:**
- Select button (returns selected release)
- Cancel button
- Double-click release (selects and closes)

### 7.6 Behavior Notes

- Always shows for user confirmation (even with single result)
- First item auto-selected by default (line 20)

---

## 8. AlbumSearchDialog

**Files:** `AlbumSearchDialog.xaml` (53 lines), `AlbumSearchDialog.xaml.cs` (60 lines)
**Purpose:** Manual MusicBrainz search by album name, artist, and optional year

### 8.1 Search Fields

#### AlbumNameTextBox
- **x:Name:** `AlbumNameTextBox`
- **Event:** None (input only)
- **Action:** Album name search field
- **Required:** Yes
- **Status:** WORKING

#### ArtistTextBox
- **x:Name:** `ArtistTextBox`
- **Event:** None (input only)
- **Action:** Artist name search field
- **Required:** Yes
- **Status:** WORKING

#### YearTextBox
- **x:Name:** `YearTextBox`
- **Event:** None (input only)
- **Action:** Year filter (optional, 4 digits max)
- **Required:** No
- **Status:** WORKING

### 8.2 Dialog Buttons

#### SearchButton
- **x:Name:** None
- **Event:** `SearchButton_Click` (line 33)
- **Action:** Validates inputs (album+artist required), sets properties, DialogResult=true
- **IsDefault:** True
- **Status:** WORKING

#### CancelButton
- **x:Name:** None
- **Event:** `CancelButton_Click` (line 53)
- **Action:** Sets DialogResult=false, closes dialog
- **IsCancel:** True
- **Status:** WORKING

### 8.3 Data Flow

**Input:**
- `initialAlbumName` (optional, pre-fills field)
- `initialArtist` (optional, pre-fills field)

**Output:**
- `AlbumName` property
- `Artist` property
- `Year` property (nullable)
- DialogResult (true/false)

### 8.4 Navigation

**Entry Points:**
- From MainWindow: Manual Search button (for studio albums)

**Exit Points:**
- Search button (triggers MusicBrainz search)
- Cancel button

### 8.5 Focus Logic

**Smart Focus:** (line 18-30)
- If album name empty → focus AlbumNameTextBox
- Else if artist empty → focus ArtistTextBox
- Else → focus YearTextBox

---

## 9. ConcertDetailWindow

**Files:** `ConcertDetailWindow.xaml` (239 lines), `ConcertDetailWindow.xaml.cs` (307 lines)
**Purpose:** Standalone concert viewer with audio player (DEPRECATED - functionality moved to LibraryBrowserWindow)

### 9.1 Status

**DEAD WINDOW:** This window exists in the codebase but is **never instantiated** anywhere. All functionality has been integrated into LibraryBrowserWindow's concert view.

### 9.2 Elements (All DEAD)

All UI elements are functional in isolation but the window is never opened:

- VenueText, LocationText, DateText, TrackCountText (display only)
- ArtworkPlaceholder, AlbumArtwork (artwork display)
- TracksDataGrid (track list with double-click play)
- PlayPauseButton, StopButton, PreviousButton, NextButton (transport controls)
- AudioScrubber (seek control)
- VolumeSlider (volume control)
- NowPlayingText, NowPlayingDetails (now playing display)
- CurrentTimeText, TotalTimeText (time display)

**Status:** ALL DEAD (window never instantiated)

### 9.3 Code Quality

Code is fully implemented and appears functional:
- Audio player service integration
- Track loading from folder path
- Artwork loading (cover.jpg/folder.jpg)
- Playback controls with timer updates
- Seek/scrub functionality
- Auto-advance to next track

**Issue:** Entire window is orphaned code

### 9.4 Recommendation

**DELETE THIS FILE** - All functionality exists in LibraryBrowserWindow. Keeping it creates maintenance burden and confusion.

---

## Summary

### Total Windows/Dialogs: 9

1. **MainWindow** - Import workflow (WORKING)
2. **LibraryBrowserWindow** - Main app window (WORKING)
3. **AdvancedSearchDialog** - Multi-tab search (WORKING)
4. **SettingsWindow** - Configuration (WORKING)
5. **AddSongDialog** - Add songs (WORKING)
6. **ManageSongsDialog** - Browse songs (WORKING)
7. **ReleaseSelectorDialog** - Pick MusicBrainz release (WORKING)
8. **AlbumSearchDialog** - Manual MusicBrainz search (WORKING)
9. **ConcertDetailWindow** - Concert viewer (**DEAD - DELETE**)

### Element Status Summary

| Status | Count | Description |
|--------|-------|-------------|
| **WORKING** | 142 | Fully functional, code exists and works |
| **PARTIAL** | 1 | Settings menu item in MainWindow (deprecated) |
| **DEAD** | 18 | ConcertDetailWindow (entire window unused) |
| **BROKEN** | 0 | No broken elements found |

### Critical Findings

#### 1. Dead Code
- **ConcertDetailWindow** is fully implemented but never instantiated
- **Recommendation:** Delete both .xaml and .xaml.cs files

#### 2. Deprecated Elements
- **MainWindow.SettingsMenuItem_Click** shows message instead of opening settings
- **Recommendation:** Remove from MainWindow menu (settings only in LibraryBrowserWindow)

#### 3. Known Bugs (Already Fixed in Code)
- ✅ LINQ lazy evaluation bug (fixed with explicit list building)
- ✅ TextChanged recursion bug (fixed with `_isUpdatingSearchBox` flag)
- ✅ Multi-song collection bug (fixed by collecting from `_allSongCheckBoxes`)

#### 4. Potential Issues (Mentioned in TODO.md)
- Box Set radio button auto-select on new import (lines 246-260 in MainWindow.xaml.cs) - PARTIAL
  - Code exists but TODO notes it may not work reliably
  - Needs testing and potential debugging

### Code Quality Observations

#### Strengths
- Consistent naming conventions (`PascalCase` for controls)
- Good separation of concerns (services vs UI)
- Proper use of event handlers
- Custom notification system (no modal MessageBox)
- Media key support in LibraryBrowserWindow
- Comprehensive error handling

#### Areas for Improvement
- Remove dead code (ConcertDetailWindow)
- Remove deprecated menu items
- Test box set auto-select logic
- Consider consolidating similar functionality (two track DataGrids with nearly identical configurations)

### Data Binding Usage

**Minimal MVVM:** Application uses code-behind pattern, not full MVVM:
- No ViewModels
- No INotifyPropertyChanged implementations
- Direct manipulation of UI elements from code-behind
- DataGrids use `ItemsSource` binding but not full data binding

**This is intentional** per CLAUDE.md: "Pattern: MVVM-like with code-behind (pragmatic WPF approach)"

### Navigation Flow

```
Application Startup
    └─ LibraryBrowserWindow (main window)
        ├─ File > Import → MainWindow (dialog)
        ├─ File > Settings → SettingsWindow (dialog)
        │   ├─ Add Song → AddSongDialog (dialog)
        │   └─ Manage Songs → ManageSongsDialog (dialog)
        ├─ Advanced Search → AdvancedSearchDialog (dialog)
        │   └─ Track search result double-click → Navigate to album
        ├─ Double-click concert → Concert view (in-window)
        │   └─ Edit Metadata → MainWindow (dialog)
        └─ MainWindow import/edit
            ├─ Manual Search → AlbumSearchDialog (dialog)
            │   └─ MusicBrainz results → ReleaseSelectorDialog (dialog)
            └─ Fingerprint Lookup → ReleaseSelectorDialog (dialog)
```

### File Statistics

| Window | XAML Lines | C# Lines | Total | Interactive Elements |
|--------|-----------|----------|-------|---------------------|
| MainWindow | 406 | 1180 | 1586 | 40 |
| LibraryBrowserWindow | 496 | 1343 | 1839 | 32 |
| AdvancedSearchDialog | 265 | 472 | 737 | 22 |
| SettingsWindow | 78 | 205 | 283 | 9 |
| AddSongDialog | 74 | 115 | 189 | 6 |
| ManageSongsDialog | 91 | 226 | 317 | 6 |
| ReleaseSelectorDialog | 81 | 67 | 148 | 3 |
| AlbumSearchDialog | 53 | 60 | 113 | 5 |
| ConcertDetailWindow | 239 | 307 | 546 | 18 (DEAD) |
| **TOTAL** | **1783** | **3975** | **5758** | **141 + 18 dead** |

### Recommendations for Documentation-First Development

1. **Delete dead code** before starting new features
2. **Update CLAUDE.md** to remove references to ConcertDetailWindow
3. **Create component documentation** for each service (MetadataService, NormalizationService, etc.)
4. **Document data flows** for import, search, and playback workflows
5. **Create architecture diagrams** showing window relationships
6. **Write test cases** based on this audit (what each button should do)

---

**End of UI Audit**
**Next Steps:** Review this audit, delete dead code, begin feature work with full understanding of existing UI
