# MainWindow — Concert Import & Metadata Editor

## Purpose

MainWindow is the concert import workflow hub for DeadEditor. It loads audio files from a folder, parses metadata from filenames and ID3 tags, normalizes song titles using fuzzy matching against the song database, and writes standardized metadata back to the files. The window supports four album types (Live Recordings, Official Releases, Studio Albums, and Box Sets) and provides specialized workflows for each. After metadata preparation, concerts can be imported into the library with proper folder structure and naming conventions. The window also supports MusicBrainz integration for studio albums via audio fingerprinting or manual search.

## Screen Layout

The MainWindow uses a dark theme (#1E1E1E background) with a two-column layout:

**Top Section (Action Bar):**
- Folder selection (browse button + read-only path display)
- Action buttons in a horizontal row: Read from Files, Normalize All Songs, Renumber Tracks, Write to Files, View Info File, Import to Library, Cancel

**Main Content (Two-Column Grid):**

**Left Column (350px wide):**
- Album Information Panel (scrollable GroupBox):
  - Artwork display (200x200 border with "No Artwork" placeholder)
  - Change/Remove artwork buttons
  - Album Type radio button group (4 types with descriptions)
  - Context-sensitive fields (different fields shown based on album type)
  - Album Title Preview (read-only display of formatted album title)

**Right Column (Remaining width):**
- Track List (DataGrid in upper GroupBox) showing:
  - Track #, Song, Final Metadata Preview, Segue checkbox (→), Duration
  - Rows highlight in dark yellow/gold when song not in database
  - Double-click track to play audio
- Selected Track Editor (middle GroupBox):
  - Title textbox, Segue checkbox, Performance Date textbox
- Audio Playback Controls (lower GroupBox):
  - Transport buttons (Previous, Play/Pause, Stop, Next)
  - Now playing display with track info
  - Progress slider with time display
  - Volume slider

**Bottom Section:**
- Status bar with text and progress bar (hidden by default)

**Overlay:**
- Custom notification panel (modal overlay replacing MessageBox)

## Interactive Elements

### Folder Selection Section

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Select Folder button | Button | `BrowseButton` | Opens folder browser dialog, calls LoadFolder() with selected path | `FolderBrowserDialog.ShowDialog()` → `LoadFolder()` | Working |
| Folder path display | TextBox | `FolderPathTextBox` | Read-only display of selected folder path | N/A (display only) | Working |

### Action Buttons Section

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Read from Files | Button | `ReadButton` | Re-loads current folder (redundant, auto-loads on browse) | `LoadFolder(FolderPathTextBox.Text)` | Working (unnecessary) |
| Normalize All Songs | Button | `NormalizeButton` | Normalizes all track titles using fuzzy matching, highlights unmatched songs in yellow/gold | `_normalizationService.NormalizeAll(_tracks)` | Working |
| Renumber Tracks | Button | `RenumberButton` | Renumbers tracks using disc-aware 101/201/301 convention based on current row order (use after drag-to-reorder) | Disc-aware sequential numbering (line 424-451) | Working |
| Write to Files | Button | `WriteButton` | Writes metadata to audio files after confirmation | `_metadataService.WriteMetadata(_albumInfo, _tracks)` | Working |
| View Info File | Button | `ViewInfoButton` | Opens non-modal window showing .txt info file content | Opens new Window with TextBox (line 823-851) | Working |
| Import to Library | Button | `ImportButton` | Imports concert to library folder structure with progress bar | `_libraryImportService.ImportToLibrary(...)` | Working |
| Cancel | Button | `CancelButton` | Closes window without saving | `this.Close()` | Working |

### Album Information Panel

#### Artwork Management

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Artwork display | Image | `ArtworkImage` | Shows album artwork from `_albumInfo.ArtworkData` | BitmapImage from memory stream | Working |
| No artwork placeholder | TextBlock | `NoArtworkText` | Shows "No Artwork" when no artwork loaded | N/A (display only) | Working |
| Change artwork | Button | `ChangeArtworkButton` | Opens file dialog, loads image (JPG/PNG), stores in `_albumInfo.ArtworkData` | `File.ReadAllBytes()` | Working |
| Remove artwork | Button | `RemoveArtworkButton` | Clears `_albumInfo.ArtworkData` and `ArtworkMimeType` | Direct property assignment | Working |

#### Album Type Selection

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Live Recording radio | RadioButton | `LiveRecordingRadio` | Sets `_albumInfo.Type = AlbumType.Live`, shows live fields | `AlbumType_Changed` → `UpdateFieldVisibility()` | Working |
| Official Release radio | RadioButton | `OfficialReleaseRadio` | Sets `_albumInfo.Type = AlbumType.OfficialRelease`, shows official release field | `AlbumType_Changed` → `UpdateFieldVisibility()` | Working |
| Studio Album radio | RadioButton | `StudioAlbumRadio` | Sets `_albumInfo.Type = AlbumType.Studio`, shows studio album fields | `AlbumType_Changed` → `UpdateFieldVisibility()` | Working |
| Box Set radio | RadioButton | `BoxSetRadio` | Sets `_albumInfo.Type = AlbumType.BoxSet`, shows box set name field, pre-fills last used box set name | `AlbumType_Changed` → `UpdateFieldVisibility()` | Partial (auto-select logic needs testing) |

#### Common Fields

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Artist | TextBox | `ArtistTextBox` | Updates `_albumInfo.Artist`, triggers preview refresh | `AlbumInfo_Changed` → `UpdateAlbumPreview()` | Working |

#### MusicBrainz Lookup (Available for all album types)

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Fingerprint Lookup | Button | `LookupAlbumButton` | Uses audio fingerprinting (AcoustID) to lookup album, shows ReleaseSelectorDialog, downloads artwork | `_musicBrainzService.LookupAllReleasesAsync(_tracks)` | Working (requires fpcalc.exe) |
| Manual Search | Button | `ManualSearchButton` | Opens AlbumSearchDialog, searches MusicBrainz by name/artist/year, shows ReleaseSelectorDialog | `_musicBrainzService.SearchReleasesByNameAsync(...)` | Working |

**Note:** MusicBrainz is a user-populated database that contains entries for all types of recordings (live concerts, studio albums, official releases, box sets). Both lookup methods are available regardless of album type selected.

#### Studio Album Fields (Collapsed unless Studio Album type selected)

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Album Name | TextBox | `AlbumNameTextBox` | Updates `_albumInfo.AlbumName`, triggers preview refresh | `AlbumInfo_Changed` → `UpdateAlbumPreview()` | Working |
| Release Year | TextBox | `ReleaseYearTextBox` | Parses int, updates `_albumInfo.ReleaseYear`, triggers preview refresh | `AlbumInfo_Changed` → `UpdateAlbumPreview()` | Working |
| Edition/Remaster | TextBox | `EditionTextBox` | Updates `_albumInfo.Edition` (optional), triggers preview refresh | `AlbumInfo_Changed` → `UpdateAlbumPreview()` | Working |

#### Live Recording Fields (Collapsed for Studio Album type)

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Date | TextBox | `DateTextBox` | Updates `_albumInfo.Date` (yyyy-MM-dd format), triggers preview refresh | `AlbumInfo_Changed` → `UpdateAlbumPreview()` | Working |
| Venue | TextBox | `VenueTextBox` | Updates `_albumInfo.Venue`, triggers preview refresh | `AlbumInfo_Changed` → `UpdateAlbumPreview()` | Working |
| City | TextBox | `CityTextBox` | Updates `_albumInfo.City`, triggers preview refresh | `AlbumInfo_Changed` → `UpdateAlbumPreview()` | Working |
| State | TextBox | `StateTextBox` | Updates `_albumInfo.State`, triggers preview refresh | `AlbumInfo_Changed` → `UpdateAlbumPreview()` | Working |
| Official Release | TextBox | `OfficialReleaseTextBox` | Updates `_albumInfo.OfficialRelease` (optional), shown only for OfficialRelease type | `AlbumInfo_Changed` → `UpdateAlbumPreview()` | Working |

#### Box Set Fields (Collapsed unless Box Set type selected)

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Box Set Name | TextBox | `BoxSetNameTextBox` | Updates `_albumInfo.BoxSetName`, pre-fills with `_librarySettings.LastBoxSetName` on visibility | `AlbumInfo_Changed` → `UpdateAlbumPreview()` | Working |

#### Preview Display

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Album Title Preview | TextBlock | `AlbumPreviewTextBlock` | Read-only display of formatted album title from `_albumInfo.AlbumTitle` | N/A (display only) | Working |

### Track List Section

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Tracks grid | DataGrid | `TracksDataGrid` | Displays all tracks, shows preview metadata, highlights unmatched songs (yellow/gold background) | `_metadataService.ReadFolder(folderPath)` | Working |
| Row selection | DataGridRow | N/A | Updates selected track editor when row selected | `TracksDataGrid_SelectionChanged` → updates SelectedTitle/Date/Segue fields | Working |
| Row loading | DataGridRow | N/A | Sets row background color based on normalization status | `TracksDataGrid_LoadingRow` → `UpdateRowBackground()` | Working |
| Row drag-to-reorder | DataGridRow | N/A | Drag rows to reorder tracks, visual drop indicator shows insertion point | `Row_PreviewMouseLeftButtonDown` → `Row_MouseMove` → `Row_Drop` → reorders `_tracks` collection | Working |

**Columns:**
- `#` - Track number (TrackNumber) - **Editable:** Click cell and type new number, rows stay in place
- `Song` - Display title (normalized or original) (DisplayTitle)
- `Final Metadata (Preview)` - Preview of metadata to be written (PreviewMetadata)
- `→` - Segue checkbox (HasSegue)
- `Duration` - Track duration (Duration)

### Selected Track Editor Section

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Title | TextBox | `SelectedTitleTextBox` | Updates selected track's `Title` property, refreshes preview | `SelectedTrack_Changed` → `UpdateTrackPreviews()` | Working |
| Segue checkbox | CheckBox | `SelectedSegueCheckBox` | Updates selected track's `HasSegue` property, refreshes preview | `SelectedTrack_Changed` → `UpdateTrackPreviews()` | Working |
| Performance Date | TextBox | `SelectedDateTextBox` | Updates selected track's `PerformanceDate` property (for live tracks in studio albums) | `SelectedTrack_Changed` → `UpdateTrackPreviews()` | Working |

### Audio Playback Controls Section

Allows users to audition tracks while reviewing metadata before import. Uses the same `AudioPlayerService` as LibraryBrowserWindow for consistent playback behavior.

#### Transport Controls

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Previous button | Button | `PreviousTrackButton` | Plays previous track in list, disabled if at first track | `PreviousTrackButton_Click` → `PlayTrack(_currentTrackIndex - 1)` | Working |
| Play/Pause button | Button | `PlayPauseButton` | Toggles playback, content toggles between "▶ Play" and "⏸ Pause" | `PlayPauseButton_Click` checks `_audioPlayer.IsPlaying` | Working |
| Stop button | Button | `StopButton` | Stops playback, resets UI, disables until next play | `StopButton_Click` → `_audioPlayer.Stop()` | Working |
| Next button | Button | `NextTrackButton` | Plays next track in list, disabled if at last track | `NextTrackButton_Click` → `PlayTrack(_currentTrackIndex + 1)` | Working |

#### Now Playing Display

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Now playing text | TextBlock | `NowPlayingText` | Shows "♪ [Song Title]" or "No track playing" | Updated in `PlayTrack()` | Working |
| Track info | TextBlock | `TrackInfoText` | Shows "Track X of Y" | Updated in `PlayTrack()` | Working |

#### Playback Progress Controls

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Current time | TextBlock | `CurrentTimeText` | Shows current playback position (M:SS format) | Updated by `_playbackTimer` every 100ms | Working |
| Progress slider | Slider | `ProgressSlider` | Shows/seeks playback position, uses `_isScrubbing` flag | `PreviewMouseUp` → `_audioPlayer.Seek()` | Working |
| Total time | TextBlock | `TotalTimeText` | Shows total track duration (M:SS format) | Set in `PlayTrack()` from `_audioPlayer.TotalDuration` | Working |
| Volume slider | Slider | `VolumeSlider` | Adjusts volume 0-100%, default 75% | `VolumeSlider_ValueChanged` → `_audioPlayer.Volume` | Working |

#### Playback Behavior

- **Track Selection:** Double-clicking a track in the DataGrid or selecting and clicking Play starts playback
- **Auto-advance:** When a track finishes, automatically plays next track (if not last)
- **Cleanup:** Playback stops automatically when:
  - User selects new folder (LoadFolder)
  - User closes window (Window_Closing)
  - User clicks Import to Library
- **Background operation:** Playback continues while user edits metadata, normalizes songs, etc.

### Status Bar Section

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Status text | TextBlock | `StatusTextBlock` | Displays status messages (e.g., "Ready", "Normalizing...", "X tracks loaded") | N/A (display only) | Working |
| Progress bar | ProgressBar | `ProgressBar` | Shows progress during import, collapsed by default | N/A (display only) | Working |

### Notification Panel (Custom Modal Dialog)

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Notification overlay | Border | `NotificationPanel` | Semi-transparent overlay covering entire window, closes on click outside dialog | `NotificationPanel_MouseDown` | Working |
| Title | TextBlock | `NotificationTitle` | Displays notification title | N/A (display only) | Working |
| Message | TextBlock | `NotificationMessage` | Displays notification message (scrollable) | N/A (display only) | Working |
| Yes button | Button | `NotificationYesButton` | Returns `true` to awaiting task, visible for Yes/No prompts | `NotificationYes_Click` → `_notificationResult.SetResult(true)` | Working |
| No button | Button | `NotificationNoButton` | Returns `false` to awaiting task, visible for Yes/No prompts | `NotificationNo_Click` → `_notificationResult.SetResult(false)` | Working |
| OK button | Button | `NotificationOkButton` | Returns `true` to awaiting task, visible for info/error prompts | `NotificationOk_Click` → `_notificationResult.SetResult(true)` | Working |

### Menu Bar

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Settings menu item | MenuItem | N/A | Shows message box directing user to Library window for settings | `MessageBox.Show(...)` | Partial (deprecated, should be removed) |
| Exit menu item | MenuItem | N/A | Closes import window (does not terminate application) | `this.Close()` | Working |

## User Workflows

### Workflow 1: Import a Live Recording

**Goal:** Import a taper recording from a concert into the library.

1. **User clicks "Select Folder" button**
   - `BrowseButton_Click` fires (line 78)
   - `FolderBrowserDialog` opens
   - User selects folder containing FLAC/MP3 files

2. **Handler calls `LoadFolder(folderPath)` (line 101)**
   - `_metadataService.ReadFolder(folderPath)` reads all audio files and parses ID3 tags
   - Tracks sorted by disc number, then track number (line 111-114)
   - If no audio files found → shows "No Files" notification, exits workflow
   - `_metadataService.ReadAlbumInfo(folderPath, _tracks)` parses folder name and reads info file (line 124)
     - Folder name format: `yyyy-MM-dd [Venue], [City, State]` or `[Album Name] ([Year])`
     - Reads `.txt` info file if present (e.g., `2024-10-31.txt`)
   - `RefreshUI()` populates all fields (line 127)
     - Sets album type radio button (defaults to Live Recording)
     - Populates Date, Venue, City, State fields from parsed folder name
     - Loads artwork if `cover.jpg` or `folder.jpg` exists
     - Enables "View Info File" button if info file content exists
   - Status: "X tracks loaded"

3. **User clicks "Normalize All Songs" button**
   - `NormalizeButton_Click` fires (line 670)
   - If no tracks → shows "No Files" notification, exits
   - `_normalizationService.NormalizeAll(_tracks)` fuzzy-matches all song titles
     - **First:** Normalizes slash-formatted dates in `track.Title` (e.g., "(1971/07/02 Filmore West)" → "(1971-07-02)")
     - **Then:** Tries exact match first (canonical title or alias)
     - Falls back to Levenshtein distance (max 2 characters or 20% of string length)
     - Sets `track.Title` with normalized date format (if date found)
     - Sets `track.NormalizedTitle` for matched songs, leaves empty for unmatched
   - `UpdateTrackPreviews()` refreshes preview metadata column (line 684)
     - **Important:** Preserves MusicBrainz data in Preview column
     - Only updates Preview for tracks where `HasMusicBrainzData == false`
     - Tracks with MusicBrainz data keep their Preview values unchanged
   - `TracksDataGrid.Items.Refresh()` re-renders grid (line 687)
   - `UpdateAllRowBackgrounds()` highlights unmatched songs in yellow/gold (line 690)
   - Status: "Normalized X of Y songs"
   - If some songs unmatched → shows notification "X songs not in database (will use original titles)"

4. **User reviews unmatched songs (yellow/gold rows)**
   - **Option A: Add missing songs to database**
     - User would need to open Settings > Add Song (from Library window)
     - Re-run normalization
   - **Option B: Manually edit song titles**
     - User clicks row to select track
     - `TracksDataGrid_SelectionChanged` fires (line 581)
     - Selected Track Editor populates with track's Title, Date, Segue
     - User edits `SelectedTitleTextBox` or `SelectedSegueCheckBox`
     - `SelectedTrack_Changed` fires (line 640)
     - Updates `track.Title`, `track.HasSegue`, `track.PerformanceDate`
     - `UpdateTrackPreviews()` and `TracksDataGrid.Items.Refresh()` update preview

5. **User clicks "Write to Files" button**
   - `WriteButton_Click` fires (line 756)
   - If no tracks or album info → shows "No Files" notification, exits
   - Shows confirmation notification: "This will write metadata to X audio files. This operation cannot be undone. Continue?"
   - User clicks "Yes" in notification panel
     - `_notificationResult.Task` resolves to `true` (line 764)
   - `_metadataService.WriteMetadata(_albumInfo, _tracks)` writes ID3 tags (line 775)
     - For each track: writes Artist, Album, Title, Track#, Date, Venue, etc.
     - **Important:** For studio albums, does NOT append date to track titles
     - For live recordings: may append date like "Song Title (yyyy-MM-dd)" if `PerformanceDate` set
   - Status: "Successfully wrote metadata to X files"
   - Shows success notification
   - Marks `_albumInfo.IsModified = false` and `track.IsModified = false`

6. **User clicks "Import to Library" button**
   - `ImportButton_Click` fires (line 866)
   - If no tracks or album info → shows "No Files" notification, exits
   - If `_librarySettings.LibraryRootPath` not set → shows "Library Not Set" notification, exits
   - Checks if show already exists: `_libraryImportService.ShowExistsInLibrary(...)` (line 890)
     - If exists → shows "Show Already Exists. Overwrite?" notification
     - User clicks "No" → exits workflow
   - Shows confirmation: "Import X tracks to library? Destination: [path], Structure: Year\Date - Venue, City, State"
   - User clicks "Yes"
   - Progress bar becomes visible (line 940)
   - `_libraryImportService.ImportToLibrary(...)` runs in background thread (line 952-955)
     - Copies files to `[LibraryRoot]/[Year]/[Date] - [Venue], [City, State]/`
     - Writes metadata to copied files
     - Copies artwork as `cover.jpg`
     - Copies info file if present
     - Reports progress via `IProgress<(int, int, string)>`
   - Progress bar updates in real-time
   - On success:
     - Status: "Successfully imported X tracks to library"
     - Shows success notification with full path
     - `ClearView()` resets entire UI (line 990)
   - On failure:
     - Shows error notification
     - Status: "Error importing to library"
     - UI not cleared (user can retry or fix issues)

**Success Path:** Folder loaded → Songs normalized → Metadata written → Imported to library → UI cleared
**Failure Paths:**
- No audio files → notification, stop at step 2
- Songs not in database → highlighted in yellow, user can proceed with original titles or add songs
- Write cancelled → stop at step 5
- Library path not set → notification, stop at step 6
- Import cancelled or failed → UI not cleared, user can retry

---

### Workflow 2: Import a Studio Album

**Goal:** Import a studio album with MusicBrainz metadata lookup.

1. **User clicks "Select Folder" button**
   - Same as Workflow 1 steps 1-2
   - `LoadFolder()` reads files and parses folder name
   - If folder name format is `[Album Name] ([Year])` → auto-detects as Studio Album
   - Otherwise defaults to Live Recording (user must change manually)

2. **User selects "Studio Album" radio button**
   - `AlbumType_Changed` fires (line 263)
   - Sets `_albumInfo.Type = AlbumType.Studio` (line 278)
   - `UpdateFieldVisibility()` shows studio fields (line 287):
     - Shows: Album Name, Release Year, Edition, Fingerprint Lookup, Manual Search
     - Hides: Date, Venue, City, State, Official Release
   - `UpdateAlbumPreview()` formats title as `[Album Name] ([Year])`
   - `UpdateTrackPreviews()` updates preview without date appended

3. **User clicks "🔍 Fingerprint Lookup" button**
   - `LookupAlbumButton_Click` fires (line 296)
   - If no tracks → shows "No Tracks" notification, exits
   - Button disabled, progress bar appears (indeterminate)
   - Status: "Looking up album information..."
   - `_musicBrainzService.LookupAllReleasesAsync(_tracks)` runs (line 312)
     - **Requires `fpcalc.exe`** (Chromaprint) in project directory or PATH
     - If fpcalc.exe missing → returns null, shows "Not Found" notification
     - Fingerprints first track using AcoustID API
     - Gets MusicBrainz Recording ID from AcoustID
     - Queries MusicBrainz for all releases containing that recording
     - Filters to "Official" status and "Album" type
     - Returns list of `ReleaseOption` objects with Title, Year, Label, Country, Format, ArtworkUrl
   - **On success (1+ releases found):**
     - Always shows `ReleaseSelectorDialog` for user confirmation (line 327)
       - Dialog shows grid with Artist, Album Title, Year, Label, Country, Format
       - User selects desired release (e.g., "2003 Remaster" vs "1970 Original")
       - User clicks "Select" or double-clicks row
     - If user cancels → Status: "Album lookup cancelled", workflow ends
     - If user selects release:
       - Shows **MusicBrainz Confirmation Dialog** with details of what will be populated:
         - Release details: Title, Artist, Year, Label, Country, Format
         - Fields that will be updated (varies by album type):
           * **Studio Album**: Album Name, Release Year, Artist, Artwork, Track Titles
           * **Box Set**: Box Set Name, Artist, Year, Artwork, Track Titles
           * **Official Release**: Official Release, Artist, Year, Artwork, Track Titles
           * **Live Recording**: Artist, Year, Artwork, Track Titles (Date/Venue/City/State remain unchanged)
         - Track count: "X MusicBrainz tracks will populate preview column"
         - Buttons: "Apply Changes" (accept) or "Cancel" (reject)
       - If user clicks "Apply Changes":
         - **Universal fields (all album types):**
           - Updates `Artist` field (always visible)
           - Updates `ReleaseYear` (stored in model for all types)
           - Downloads artwork from `selectedRelease.ArtworkUrl`
             - Stores in `_albumInfo.ArtworkData` as byte array
             - Sets `_albumInfo.ArtworkMimeType = "image/jpeg"`
             - `UpdateArtworkDisplay()` renders artwork
         - **Album-type-specific fields:**
           - **Studio Album:** Populates `AlbumName`, `ReleaseYear`, `Edition` (if present) fields
           - **Box Set:** Populates `BoxSetName` with MusicBrainz title
           - **Official Release:** Populates `OfficialRelease` with MusicBrainz title
           - **Live Recording:** No auto-fill (user manually enters Date/Venue/City/State)
         - **Track data (if available):**
           - MusicBrainz track titles shown in "Final Metadata Preview" column
           - File-based titles remain in "Song" column
           - Sets `HasMusicBrainzData = true` flag to preserve MusicBrainz data
           - User compares both and can manually edit as needed
           - `TracksDataGrid.Items.Refresh()` called to force grid re-render
           - **Important:** Subsequent "Normalize All Songs" will NOT overwrite Preview column for tracks with MusicBrainz data
         - Refreshes all visible UI fields
         - `UpdateAlbumPreview()` updates preview
         - Status: "Album identified: [Title] ([Year])"
       - If user clicks "Cancel":
         - No changes applied
         - Status: "MusicBrainz update cancelled"
   - **On failure (0 releases or error):**
     - Shows "Not Found" notification: "Could not identify this album using audio fingerprinting. Please enter the information manually."
     - Status: "Album lookup failed"
     - User can try manual search or enter info manually
   - Progress bar hidden, button re-enabled

4. **Alternative: User clicks "🔎 Manual Search" button**
   - `ManualSearchButton_Click` fires (line 409)
   - If no album info → shows "No Album" notification, exits
   - Opens `AlbumSearchDialog` with current Album Name and Artist pre-filled (line 422)
   - User enters/edits Album Name, Artist, and optional Year
   - User clicks "Search"
   - Dialog closes, returns search criteria
   - If dialog cancelled → workflow ends (line 427-430)
   - Button disabled, progress bar appears
   - Status: "Searching MusicBrainz..."
   - `_musicBrainzService.SearchReleasesByNameAsync(name, artist, year)` runs (line 441-444)
     - Searches MusicBrainz by text query
     - Returns list of matching releases
   - **On success (1+ releases found):**
     - Same ReleaseSelectorDialog flow as fingerprint lookup (line 451-510)
   - **On failure (0 releases found):**
     - Shows "Not Found" notification with search criteria
     - Status: "No releases found"
   - Progress bar hidden, buttons re-enabled

5. **User reviews and edits metadata**
   - Album Name, Release Year, Artist, Edition fields populated (either from lookup or manual entry)
   - User can edit Edition field (e.g., "2025 Remaster", "Deluxe Edition")
   - User can change artwork or remove it
   - User reviews track list

6. **User clicks "Normalize All Songs" button** (optional but recommended)
   - Same as Workflow 1 step 3
   - Normalizes song titles against database
   - Highlights unmatched songs

7. **User clicks "Write to Files" button**
   - Same as Workflow 1 step 5
   - **Important difference:** Studio album tracks do NOT get date appended (line 190-192)
   - Preview metadata shows clean song titles: "Morning Dew", not "Morning Dew (1972-05-04)"

8. **User clicks "Import to Library" button**
   - `ImportButton_Click` fires (line 866)
   - Same validation checks as Workflow 1 step 6
   - Confirmation shows: "Structure: Studio Albums\[Album Name] ([Year])\"
   - User clicks "Yes"
   - `_libraryImportService.ImportToLibrary(...)` runs
   - Copies files to `[LibraryRoot]/Studio Albums/[Album Name] ([Year])/`
   - Success notification shows full path
   - `ClearView()` resets UI

**Success Path:** Folder loaded → Studio Album selected → MusicBrainz lookup → Metadata reviewed → Written → Imported
**Failure Paths:**
- fpcalc.exe missing → fingerprint lookup fails, use manual search
- MusicBrainz API unreachable → lookup/search fails, enter manually
- No releases found → enter manually
- User cancels release selection → workflow paused, can retry

---

### Workflow 3: Import a Box Set

**Goal:** Import a multi-concert collection (e.g., digital download box set) with a shared box set name.

1. **User clicks "Select Folder" button**
   - Same as Workflow 1 steps 1-2
   - `LoadFolder()` reads files and parses folder name
   - Defaults to Live Recording type

2. **User selects "Box Set" radio button**
   - `AlbumType_Changed` fires (line 263)
   - Sets `_albumInfo.Type = AlbumType.BoxSet` (line 282)
   - `UpdateFieldVisibility()` shows box set field (line 287)
     - Shows: Date, Venue, City, State, Box Set Name (optional)
     - Hides: Album Name, Release Year, Edition, MusicBrainz buttons, Official Release
   - **Auto-fills Box Set Name** with `_librarySettings.LastBoxSetName` if set (line 239-244)
     - Example: User previously imported from "Enjoying the Ride" box set
     - Next import automatically suggests "Enjoying the Ride"
     - User can keep it or change it
   - `UpdateAlbumPreview()` updates preview

3. **User enters/edits metadata**
   - Box Set Name: e.g., "Enjoying the Ride", "Digital Album Live"
   - Date, Venue, City, State for this specific concert
   - All fields work same as Live Recording

4. **User normalizes, writes, and imports** (same as Workflow 1 steps 3-6)
   - Import path: `[LibraryRoot]/[Year]/[Date] - [Venue], [City, State]/`
   - Metadata includes Box Set Name in `_albumInfo.BoxSetName` field
   - **On successful import:**
     - `_librarySettings.LastBoxSetName = _albumInfo.BoxSetName` (line 964)
     - `_librarySettings.Save()` persists box set name for next import
     - Next concert from same box set will auto-fill this name

5. **User imports additional concerts from same box set**
   - Click "Select Folder", choose next concert folder
   - Box Set radio button **should auto-select** and pre-fill box set name
     - **Note:** Auto-select logic exists (line 246-260) but TODO.md notes it may not work reliably
   - User confirms/edits fields
   - Import → same box set name applied consistently across all concerts

**Success Path:** Folder loaded → Box Set selected → Box set name entered → Imported → Name remembered for next import
**Partial Issue:** Auto-select Box Set radio button logic (line 246-260) needs testing and debugging

---

### Workflow 4: Edit Existing Concert Metadata

**Goal:** Re-open an already-imported concert to edit its metadata.

**Entry Point:** Called from `LibraryBrowserWindow.EditMetadataButton_Click` (not from MainWindow itself)

1. **User clicks "Edit Metadata" button in Library Browser**
   - LibraryBrowserWindow creates new MainWindow instance
   - Calls `mainWindow.LoadFolder(concertFolderPath)` with existing concert path

2. **MainWindow loads existing concert** (same as Workflow 1 step 2)
   - Reads all files and existing metadata
   - Populates all fields from existing ID3 tags and folder structure
   - Album type radio button reflects current type
   - Artwork loaded if present

3. **User edits metadata**
   - Change song titles, segues, performance dates
   - Change album info (date, venue, artist, etc.)
   - Change album type if needed
   - Add/remove/change artwork

4. **User clicks "Write to Files"**
   - Same as previous workflows
   - Overwrites existing metadata in place

5. **User closes window (does NOT click "Import to Library")**
   - `this.Close()` via Cancel button or X
   - LibraryBrowserWindow detects close (line 860 in LibraryBrowserWindow.xaml.cs)
   - Refreshes library view to show updated metadata
   - Re-loads concert view to show changes

**Success Path:** Concert loaded → Metadata edited → Written → Window closed → Library refreshed
**Note:** User does NOT re-import; files are edited in place within library folder

---

### Workflow 5: Drag-to-Reorder Tracks

**Goal:** Reorder tracks by dragging rows to new positions, then renumber them.

**Use Case:** Fix incorrect track order from badly-tagged files, or manually arrange tracks when disc numbers are missing.

1. **User loads concert folder**
   - Tracks appear in DataGrid in current order (sorted by disc number, then track number)
   - Track numbers may be incorrect or inconsistent (e.g., 1, 2, 3 instead of 101, 201, 301)

2. **User drags a row to new position**
   - Click and hold left mouse button on any part of the row
   - Drag up or down - a visual insertion line appears showing drop position
   - Release mouse button to drop row at new position
   - Row immediately moves to new position in the grid
   - **Important:** Track numbers do NOT auto-update on drop

3. **User continues reordering as needed**
   - Any row can be dragged to any position (no disc boundary constraints)
   - Rows stay in their new positions until dragged again
   - Manual # edits and drag-to-reorder can be mixed freely

4. **User clicks "Renumber Tracks" button**
   - Renumber button assigns disc-aware 101/201/301 numbers based on current row order
   - Groups tracks by DiscNumber, then numbers sequentially within each disc
   - Example: If tracks are reordered but still have DiscNumber 1, 2, 1, 2:
     - Disc 1 tracks get 101, 102 in current order
     - Disc 2 tracks get 201, 202 in current order

5. **User clicks "Write to Files"**
   - Metadata written with new track numbers to audio files

**Success Path:** Tracks loaded → Dragged to correct order → Renumbered → Written to files

**Notes:**
- Dragging does NOT automatically renumber - user must click Renumber button
- Drag-to-reorder works independently of disc numbers (move tracks across disc boundaries freely)
- After reordering, Renumber button respects current row order when assigning numbers
- Visual feedback (insertion line) shows exactly where row will land before dropping

---

## Data Flow

### Inputs

**Files:**
- Audio files (FLAC/MP3) from user-selected folder
- Existing ID3 tags in audio files (if present)
- Optional `.txt` info file in concert folder (e.g., `2024-10-31.txt`)
- Optional album artwork (`cover.jpg`, `folder.jpg`, or user-selected image file)
- `Data/songs.json` - Song database for normalization
- `%APPDATA%/DeadEditor/settings.json` - Library settings (paths, window position, last box set name)

**User Input:**
- Folder path selection
- Album type selection (Live, Official Release, Studio, Box Set)
- Album metadata (artist, date, venue, album name, year, edition, box set name)
- Track edits (titles, segues, performance dates)
- MusicBrainz search criteria (album name, artist, year)
- Confirmation dialogs (write, import, overwrite)

**External APIs:**
- MusicBrainz API (via `_musicBrainzService`)
  - Audio fingerprinting via AcoustID + fpcalc.exe
  - Text search by album name/artist
  - Release metadata (title, year, label, country, format)
  - Cover Art Archive (artwork URLs)

### Outputs

**Files Written:**
- **When "Write to Files" clicked:**
  - ID3 tags updated in original audio files (in place)
  - Tags written: Artist, Album, Title, Track#, Year, Date, Genre, Comment, AlbumArtist
  - Embedded artwork (APIC frame) if `_albumInfo.ArtworkData` present

- **When "Import to Library" clicked:**
  - Copies all audio files to library folder structure
  - Writes metadata to copied files (not originals)
  - Copies artwork as `cover.jpg` in album folder
  - Copies info file (if present) to album folder
  - Creates folder structure:
    - Live/Box Set: `[LibraryRoot]/[Year]/[Date] - [Venue], [City, State]/`
    - Official Release: `[OfficialReleasesPath]/[Series]/[OfficialRelease]/`
    - Studio Album: `[LibraryRoot]/Studio Albums/[Album Name] ([Year])/`

**Settings Updated:**
- `%APPDATA%/DeadEditor/settings.json`:
  - `LastBoxSetName` (when importing box set)
  - `MainWindowLeft`, `MainWindowTop`, `MainWindowWidth`, `MainWindowHeight` (on window close)

**In-Memory State:**
- `_albumInfo` (AlbumInfo object with all album metadata)
- `_tracks` (List<TrackInfo> with all track metadata)
- Modified flags set on `_albumInfo.IsModified` and `track.IsModified`

### Services Used

**MetadataService (`_metadataService`):**
- `ReadFolder(string folderPath)` → List<TrackInfo>
  - Scans folder for FLAC/MP3 files
  - Reads ID3 tags from each file
  - Returns list of TrackInfo objects
- `ReadAlbumInfo(string folderPath, List<TrackInfo> tracks)` → AlbumInfo
  - Parses folder name for date/venue or album/year
  - Reads `.txt` info file if present
  - Loads artwork from `cover.jpg` or `folder.jpg`
  - Returns AlbumInfo object
- `WriteMetadata(AlbumInfo albumInfo, List<TrackInfo> tracks)` → void
  - Writes ID3 tags to all audio files
  - Embeds artwork if present

**NormalizationService (`_normalizationService`):**
- `NormalizeAll(List<TrackInfo> tracks)` → int
  - Fuzzy-matches all track titles against song database
  - Sets `track.NormalizedTitle` for matched songs
  - Returns count of matched songs

**LibraryImportService (`_libraryImportService`):**
- `ShowExistsInLibrary(string libraryPath, AlbumInfo albumInfo, string? officialReleasesPath)` → bool
  - Checks if concert already exists in library
  - Returns true if duplicate found
- `ImportToLibrary(string libraryPath, AlbumInfo albumInfo, List<TrackInfo> tracks, IProgress<...> progress, string? officialReleasesPath)` → void
  - Copies files to library folder structure
  - Writes metadata to copied files
  - Reports progress via IProgress

**MusicBrainzService (`_musicBrainzService`):**
- `LookupAllReleasesAsync(List<TrackInfo> tracks)` → Task<List<ReleaseOption>?>
  - Fingerprints first track using fpcalc.exe
  - Queries AcoustID API for Recording ID
  - Queries MusicBrainz for all releases
  - Returns list of release options or null on failure
- `SearchReleasesByNameAsync(string albumName, string artist, string? year)` → Task<List<ReleaseOption>?>
  - Searches MusicBrainz by text query
  - Returns list of matching releases

**LibrarySettings (`_librarySettings`):**
- `Load()` → LibrarySettings (static)
  - Loads settings from `%APPDATA%/DeadEditor/settings.json`
- `Save()` → void
  - Persists settings to disk

---

## Business Rules

### Date Format Convention
- **Strict yyyy-MM-dd format** required for all dates
- Folder names: `yyyy-MM-dd [Venue], [City, State]`
- Reasoning: Eliminates MM/dd/yyyy vs dd/MM/yyyy confusion, ensures consistent sorting
- Enforced in: Folder parsing, album preview, metadata writing, library import

### Song Title Normalization
- **Process:**
  1. Strip leading track numbers (`01 `, `1-01 `, `d1t01 `)
  2. Remove tape markers (`//`, `/ /`)
  3. Remove box-drawing characters (`─`, `—`, `–`)
  4. Strip embedded dates from titles (`(yyyy-MM-dd)`, `[yyyy-MM-dd]`)
  5. Normalize to canonical song name via fuzzy matching (max 2 char diff or 20%)
  6. Preserve segue markers (`>`, `->`, `→`)
- **Fuzzy Matching:**
  - Max Levenshtein distance: 2 characters OR 20% of string length
  - Examples: "Dnacing" → "Dancing", "Wkae" → "Wake", "Monkey &" → "Monkey & the Engineer"
- **Unmatched Songs:**
  - Highlighted in yellow/gold background in track grid
  - Use original title (not normalized)
  - User can manually edit or add song to database

### Album Type-Specific Rules

**Live Recordings:**
- Fields: Date, Venue, City, State, OfficialRelease (optional)
- Album title format: `yyyy-MM-dd - Venue, City, State`
- Library path: `[LibraryRoot]/[Year]/[Date] - [Venue], [City, State]/`
- Track titles: May include performance date if different from album date

**Official Releases:**
- Fields: Date, Venue, City, State, OfficialRelease (required)
- Album title format: `yyyy-MM-dd - OfficialRelease`
- Library path: `[OfficialReleasesPath]/[Series]/[OfficialRelease]/`
- Example: `Dave's Picks/Dave's Picks Vol. 53/`

**Studio Albums:**
- Fields: AlbumName, ReleaseYear, Edition (optional)
- Album title format: `AlbumName (Year)` or `AlbumName (Year) - Edition`
- Library path: `[LibraryRoot]/Studio Albums/[AlbumName] ([Year])/`
- **Track titles do NOT include dates** (line 190-192)
- Hybrid albums (studio + live bonus tracks) treated as single Studio Album
- Edition field distinguishes remasters/deluxe editions as separate albums

**Box Sets:**
- Fields: Date, Venue, City, State, BoxSetName (optional)
- Album title format: `yyyy-MM-dd - Venue, City, State`
- Library path: Same as Live Recordings
- Box set name stored in metadata but not in folder structure
- **Box set name memory:** Last used box set name auto-fills for next import (line 964-966)

### Segue Detection
- **Notation:** `China Cat Sunflower > I Know You Rider`
- **Storage:** Tracks stored separately, linked with `>` marker
- **Display:** Both song names shown with segue marker in UI
- **Checkbox:** `HasSegue` flag in track list (→ column)

### Artwork Handling
- **Supported formats:** JPEG, PNG
- **Storage:** In-memory as byte array in `_albumInfo.ArtworkData`
- **MIME type:** Detected from file extension, stored in `_albumInfo.ArtworkMimeType`
- **Embed on write:** Written to ID3 APIC frame when "Write to Files" clicked
- **Import to library:** Saved as `cover.jpg` in album folder
- **MusicBrainz download:** Automatically downloads from Cover Art Archive if available

### Metadata Writing Rules
- **Write target:** Original audio files when "Write to Files" clicked
- **Import target:** Copied files in library when "Import to Library" clicked
- **Irreversible:** No undo functionality (confirmed in dialog)
- **Tags written:**
  - Artist (from `_albumInfo.Artist`)
  - Album (from `_albumInfo.AlbumTitle`)
  - Title (from `track.Title` or `track.NormalizedTitle`)
  - Track# (from `track.TrackNumber`)
  - Year (from `_albumInfo.ReleaseYear` or parsed from Date)
  - Date (from `_albumInfo.Date` or `track.PerformanceDate`)
  - Genre, Comment, AlbumArtist (as appropriate)
  - APIC (artwork) if present

### Library Import Rules
- **Duplicate check:** Checks if concert already exists before import
  - If exists → prompts "Overwrite?" (line 890-905)
- **Original files NOT modified:** Import copies files, originals untouched (line 932)
- **Folder structure enforced:** Strict directory naming based on album type
- **Progress reporting:** Real-time progress bar during import
- **Settings persistence:** Box set name saved for next import

### Box Set Colon Format (NOT Enforced)
- **Note:** Unlike some applications, DeadEditor does NOT require colons in box set names
- Box set name is free-form text (e.g., "Enjoying the Ride", "Digital Album Live")
- No specific formatting rules enforced

---

## Known Issues

### 1. Box Set Auto-Select Logic (Partial - Needs Testing)
**Location:** `UpdateFieldVisibility()` lines 246-260
**Issue:** Auto-select Box Set radio button when new import has remembered box set name may not work reliably
**Code:**
```csharp
// Auto-select Box Set radio if we pre-filled a box set name for a new import
if (!isBoxSet && !string.IsNullOrEmpty(_librarySettings.LastBoxSetName) &&
    string.IsNullOrWhiteSpace(_albumInfo?.BoxSetName) && _albumInfo?.Type == AlbumType.Live)
{
    // This appears to be a new import (Live type by default) but we have a remembered box set name
    _isUpdating = true;
    BoxSetRadio.IsChecked = true;
    _albumInfo.Type = AlbumType.BoxSet;
    _isUpdating = false;
    UpdateFieldVisibility();
}
```
**Expected:** When importing a new concert after previously importing from a box set, Box Set radio should auto-select
**Actual:** Behavior unclear, needs testing
**Workaround:** User manually selects Box Set radio button

### 2. Deprecated Settings Menu Item
**Location:** Menu bar, `SettingsMenuItem_Click()` line 797
**Issue:** Settings menu item in MainWindow just shows a message box instead of opening settings
**Message:** "Settings can be accessed from the main Library window."
**Recommendation:** Remove this menu item entirely from MainWindow
**Status:** Working as designed but should be removed for clarity

### 3. Read Button Redundancy
**Location:** `ReadButton` in action bar
**Issue:** "Read from Files" button is redundant - folder auto-loads on browse
**Current behavior:** Re-loads current folder (useful if files changed externally)
**Recommendation:** Consider removing or renaming to "Refresh"
**Status:** Working but unnecessary in typical workflow

---

## Edge Cases

### What happens with an empty folder?
**Scenario:** User selects folder with no FLAC/MP3 files
**Behavior:**
1. `LoadFolder()` reads folder (line 101)
2. `_metadataService.ReadFolder()` returns empty list (line 111)
3. Check `_tracks.Count == 0` triggers (line 116)
4. Shows notification: "No Files - No audio files (FLAC/MP3) found in the selected folder." (line 118)
5. Status: "No audio files found" (line 119)
6. Workflow stops, no data loaded
7. User can browse again to select different folder

**Result:** Safe failure with clear error message, no crash

---

### What happens with non-audio files in the folder?
**Scenario:** Folder contains images, text files, videos alongside audio files
**Behavior:**
1. `_metadataService.ReadFolder()` filters by extension (`.flac`, `.mp3`) (MetadataService implementation)
2. Non-audio files ignored completely
3. Only FLAC/MP3 files loaded into track list
4. Other files (`.txt`, `.jpg`, `.png`) handled specially:
   - `.txt` files parsed as info file by `ReadAlbumInfo()` (if named matching album)
   - `.jpg`/`.png` files checked for artwork (`cover.jpg`, `folder.jpg`)
   - Other files ignored

**Result:** Clean separation, only audio files in track list

---

### What happens if MusicBrainz is unreachable?
**Scenario:** User clicks "Fingerprint Lookup" or "Manual Search" but MusicBrainz API is down or network unavailable
**Behavior:**
1. Button disabled, progress bar shown (line 306-309 or 435-439)
2. `_musicBrainzService.LookupAllReleasesAsync()` or `SearchReleasesByNameAsync()` called
3. HTTP request fails with exception (timeout, network error, 503 service unavailable)
4. `catch (Exception ex)` block catches error (line 392-406 or 521-535)
5. Progress bar hidden
6. Shows notification: "Error - Error looking up album: [exception message]"
7. Status: "Album lookup error" or "Search error"
8. Buttons re-enabled

**User options after failure:**
- Retry lookup/search (network may recover)
- Use alternative method (manual search if fingerprint failed, or vice versa)
- Enter metadata manually
- Import without MusicBrainz data

**Result:** Graceful failure, user can continue workflow manually

---

### What happens with missing fpcalc.exe?
**Scenario:** User clicks "Fingerprint Lookup" but `fpcalc.exe` (Chromaprint) is not installed
**Behavior:**
1. `_musicBrainzService.LookupAllReleasesAsync(_tracks)` called (line 312)
2. MusicBrainzService searches for fpcalc.exe in:
   - Current directory
   - Parent directories up to 3 levels
   - System PATH environment variable
3. If not found → returns `null` (not an exception)
4. Code checks `releases != null && releases.Count > 0` (line 318)
5. `else` block triggers (line 386)
6. Shows notification: "Not Found - Could not identify this album using audio fingerprinting. Please enter the information manually." (line 389)
7. Status: "Album lookup failed" (line 388)

**User options:**
- Install fpcalc.exe from https://acoustid.org/chromaprint
  - Place in project directory OR add to PATH
- Use Manual Search instead (doesn't require fpcalc.exe)
- Enter metadata manually

**Note:** MusicBrainz service logs to console (extensive `Console.WriteLine` statements) showing fpcalc.exe search paths

**Result:** Soft failure with helpful message, alternative workflow available

---

### What happens if user selects a folder that's already in the library?
**Scenario:** User browses to library folder and selects an already-imported concert
**Behavior:**
1. `LoadFolder()` loads files normally (no check at this stage)
2. User edits metadata, clicks "Import to Library"
3. `_libraryImportService.ShowExistsInLibrary()` detects duplicate (line 890)
4. Shows notification: "Show Already Exists - This album ([description]) already exists in your library. Overwrite?" (line 896-899)
5. **If user clicks "Yes":**
   - Proceeds with import
   - Overwrites existing files in library folder (line 954)
6. **If user clicks "No":**
   - Workflow stops
   - User can edit metadata and retry, or cancel

**Result:** Duplicate detection with user confirmation before overwrite

---

### What happens if user modifies files externally while MainWindow is open?
**Scenario:** User loads folder, then edits audio files in another application (e.g., manually changes ID3 tags in Mp3tag)
**Behavior:**
1. MainWindow holds in-memory copies of metadata in `_tracks` and `_albumInfo`
2. External changes NOT detected automatically
3. User clicks "Read from Files" button (line 659)
4. `LoadFolder(FolderPathTextBox.Text)` re-reads folder (line 667)
5. Metadata reloaded from files
6. UI refreshed with new data (line 127)

**Alternative:** User can close and re-open folder (via Browse button)

**Result:** "Read from Files" button serves as refresh mechanism for external changes

---

### What happens if track title contains invalid filename characters?
**Scenario:** Song title is "Monkey & the Engineer" (contains `&`)
**Behavior:**
1. Metadata written to ID3 tags (no restrictions on tag content)
2. During import, `_libraryImportService.ImportToLibrary()` copies files
3. Filename determined by `track.FilePath` (original filename, not track title)
4. No issue as long as original filename is valid

**Potential issue:** If creating new files based on track titles (not current behavior), would need to sanitize

**Result:** No impact with current implementation (copies existing files)

---

### What happens if user enters invalid year in Release Year field?
**Scenario:** User types "abcd" in Release Year textbox
**Behavior:**
1. `AlbumInfo_Changed` handler fires (line 546)
2. `int.TryParse(ReleaseYearTextBox.Text, out var year)` fails (line 555)
3. `else` block sets `_albumInfo.ReleaseYear = null` (line 561)
4. Album preview shows `[AlbumName] ()` with empty year
5. No error message shown

**On import:**
- If year is null, folder name may be malformed or default to empty year
- LibraryImportService should handle this gracefully

**Improvement needed:** Validate year format and show error message

**Result:** Soft failure, no crash, but invalid metadata

---

### What happens if user clicks "Write to Files" multiple times?
**Scenario:** User clicks "Write to Files", then clicks again
**Behavior:**
1. First write succeeds
2. Sets `_albumInfo.IsModified = false` and `track.IsModified = false` (line 783-787)
3. Second click:
   - Confirmation dialog shown (line 764-768)
   - User clicks "Yes"
   - `_metadataService.WriteMetadata()` writes again (line 775)
   - Overwrites same tags with same data
4. Status: "Successfully wrote metadata to X files" (both times)

**Result:** Harmless redundancy, no data corruption

---

### What happens if artwork file is corrupted or invalid?
**Scenario:** User clicks "Change Artwork" and selects a corrupt JPEG file
**Behavior:**
1. `File.ReadAllBytes()` reads file successfully (line 1080)
2. Bytes stored in `_albumInfo.ArtworkData` (line 1092)
3. `UpdateArtworkDisplay()` called (line 1097)
4. `BitmapImage.BeginInit()` / `bitmap.StreamSource = new MemoryStream(...)` / `bitmap.EndInit()` called (line 1038-1042)
5. If image invalid → exception thrown in `EndInit()`
6. `catch` block triggers (line 1048)
7. Artwork not displayed, shows "No Artwork" placeholder (line 1051-1053)
8. No error message to user (silent failure)

**On write:**
- Corrupt artwork data written to ID3 tags
- Other applications may fail to display artwork

**Improvement needed:** Validate image before storing, show error notification

**Result:** Silent failure with potential invalid metadata written

---

### What happens if library path changes after concert loaded but before import?
**Scenario:** User loads concert, then changes library path in Settings (from different window), then clicks "Import to Library"
**Behavior:**
1. MainWindow holds `_librarySettings` reference from construction (line 29, 44)
2. Settings change in different window updates `settings.json` file
3. MainWindow's in-memory `_librarySettings` NOT updated (no reload)
4. Import uses old path from `_librarySettings.LibraryRootPath`
5. Imports to wrong location

**Improvement needed:** Reload settings before import, or make settings globally observable

**Result:** Import to outdated path (incorrect behavior)

---

### What happens if user closes window during import?
**Scenario:** Import in progress (progress bar showing), user clicks X to close window
**Behavior:**
1. Import runs in background thread via `Task.Run()` (line 952-955)
2. Window close triggers `MainWindow_Closing` event (line 68)
3. Saves window position (line 71-75)
4. Window closes immediately
5. Background thread continues running (orphaned)
6. Partial import may occur (some files copied, not all)
7. No cleanup or cancellation

**Improvement needed:** Implement cancellation token, disable close during import, or prompt user

**Result:** Partial import with no user feedback (data inconsistency)

---

## Summary

MainWindow is a comprehensive metadata editor with four distinct workflows for different album types. It integrates fuzzy song matching, MusicBrainz lookups, and library import automation. The UI enforces strict date formatting and provides real-time preview of final metadata. Edge cases are generally handled gracefully with user notifications, though some areas (artwork validation, concurrent settings changes, import cancellation) need improvement. The custom notification system provides a consistent, non-blocking user experience superior to standard MessageBox dialogs.

**Key Strengths:**
- Clear visual separation of album types with context-sensitive fields
- MusicBrainz integration with user confirmation dialogs
- Fuzzy matching handles real-world metadata inconsistencies
- Progress reporting during import
- Box set name memory for efficient multi-concert imports
- Non-blocking notification system

**Areas for Improvement:**
- Remove deprecated Settings menu item
- Validate year and artwork inputs
- Handle concurrent settings changes
- Implement import cancellation
- Test/fix box set auto-select logic
- Add error recovery for partial imports
