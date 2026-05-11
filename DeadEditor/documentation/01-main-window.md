# MainWindow — Concert Import & Metadata Editor

## Purpose

MainWindow is the concert import workflow hub for DeadEditor. It loads audio files from a folder, parses metadata from filenames and ID3 tags, normalizes song titles using fuzzy matching against the song database, and writes standardized metadata back to the files. The window supports two album types (`AudienceRecording` and `OfficialRelease`); `OfficialRelease` covers studio albums, official live releases, and box sets. After metadata preparation, concerts can be imported into the library with proper folder structure and naming conventions. The window also supports MusicBrainz integration via audio fingerprinting or manual search.

**TWO MODES:** MainWindow operates in two distinct modes with different UI layouts and workflows:

1. **Import Mode** (default): Import new concerts from external folders into the library
2. **Edit Mode**: Edit metadata of existing library albums in place

## Window Modes

### Import Mode

**How to open:** File menu > Import New Concert (from LibraryBrowserWindow)

**UI shown:**
- "Select Folder..." button + folder path display (top bar)
- "Read" button (toolbar)
- "Import to Library" button (bottom right)
- Window title: "Dead Editor - Import"

**Workflow:**
1. User selects folder containing audio files
2. User normalizes song titles, edits metadata as needed
3. User clicks "Import to Library" to copy files to library folder structure (tags are written to the managed copy during this step)
4. Window clears after successful import

**Constructor:** `new MainWindow()` (parameterless)

### Edit Mode

**How to open:** "Edit Metadata" button (from concert detail view in LibraryBrowserWindow)

**UI shown:**
- "Editing: [Album Name]" label (top bar) - **FIX 2:** Shows album description using priority logic: AlbumName > Date+Venue > folder name
  - **Examples:**
    - Official releases: "Editing: Blues for Allah 50th Anniversary (2025)"
    - Live recordings: "Editing: 1975-08-12 - Great American Music Hall, San Francisco, CA"
    - Multi-date albums with no AlbumName: "Editing: [folder name]"
  - **Previously:** Could show trailing " - " for albums with empty Date/Venue
- NO "Select Folder" button or folder path display
- NO "Read" button (toolbar)
- "Save Changes" button (bottom right) - combines Write + Import in one action
- Window title: "Dead Editor - Edit Metadata"

**Workflow:**
1. Window opens with existing library album pre-loaded
2. User edits metadata (song titles, dates, venue, artwork, etc.)
3. User clicks "Save Changes" to write metadata AND update library record
4. Window closes after successful save
5. Library automatically refreshes to show changes

**Constructor:** `new MainWindow(LibraryShow existingShow)` (passes existing show reference)

**Key Difference:** In Edit mode, "Save Changes" performs both metadata write AND library update in a single operation, then closes the window. Files are edited in-place within the library folder - no copying occurs.

## Screen Layout

The MainWindow uses a dark theme (#1E1E1E background) with a two-column layout:

**Top Section:**
- **Import Mode:** Folder selection (browse button + read-only path display)
- **Edit Mode:** "Editing: [Album Name]" label

**Action Bar:**
- **Import Mode:** Read, MusicBrainz, Normalize, Renumber, Match Setlist, View Info (left) | Import to Library, Cancel (right)
- **Edit Mode:** MusicBrainz, Normalize, Renumber, View Info (left) | Save Changes, Cancel (right)

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
- Selected Track Editor (middle GroupBox):
  - Title textbox, Segue checkbox, Performance Date textbox

**Playback Note:** Audio playback is handled globally by PlayerWindow. When a folder is loaded, tracks are sent to `App.PlaybackService.LoadPlaylist()`. See [documentation/17-player-window.md](17-player-window.md) for playback details.

**Bottom Section:**
- Status bar with text and progress bar (hidden by default)

**Overlay:**
- Custom notification panel (modal overlay replacing MessageBox)

## Interactive Elements

### Workflow Stepper (Import mode only)

A horizontal stepper above the toolbar visualizes the canonical Import flow as
five stages: **Load → Enrich → Clean → Structure → Import**.

Each stage shows one of four states:

- **Completed** (green, ✓ indicator) — the stage's heuristic is satisfied
- **Current** (blue, → indicator, semi-bold) — the next stage to do
- **Upcoming** (muted) — a future stage
- **Skipped** (muted with amber `!`) — a logically-past stage that wasn't
  completed (the user moved past it without satisfying its heuristic)

State is derived heuristically from the loaded track list and folder context
by `Services/ImportWorkflowState.cs`:

- **Load** — track list is non-empty
- **Enrich** — at least one source file carried a MusicBrainz Album ID at
  folder-load time, OR a MusicBrainz lookup has been applied this session.
  The flag is cached on folder load (one-shot scan via `MbidModalHelper`)
  and flipped to true after a successful MusicBrainz click.
- **Clean** — no track title contains a comma+slash-date suffix or a
  parenthesized venue/date string
- **Structure** — at least 80% of track titles are exact alias-table hits in
  `NormalizationService.GetOfficialTitle()` (i.e., canonical song names from
  `songs.json`)
- **Import** — the loaded folder resolves under `LibrarySettings.LibraryRootPath`,
  using the same trailing-separator semantics as `PathGuard` (via the new
  `PathGuard.IsPathUnderRoot` predicate)

The stepper is informational only. It does not enforce step order or disable
any toolbar action. Renumber and View Info are not stages — Renumber happens
implicitly inside Match Setlist, and View Info is a spot-check tool; both
remain as tertiary toolbar utilities.

Refresh hooks: end of `LoadFolderAsync`, `NormalizeButton_Click`,
`MatchSetlistButton_Click`, `ApplyMusicBrainzData`, and `ClearView`. Per-cell
edits do not refresh — the heuristics are too coarse-grained for keystroke
updates to matter.

Proof-of-concept addition. The five stages will be revisited (possibly
collapsed to four if Clean and Structure prove too overlapping in practice)
once the stepper has had time in the user's hands.

### Folder Selection / Editing Header Section

**Import Mode:**

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Select Folder button | Button | `BrowseButton` | Opens folder browser dialog, calls LoadFolder() with selected path | `FolderBrowserDialog.ShowDialog()` → `LoadFolder()` | Working |
| Folder path display | TextBox | `FolderPathTextBox` | Read-only display of selected folder path | N/A (display only) | Working |

**Edit Mode:**

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Editing label | TextBlock | `EditingLabel` | Displays "Editing: [Album Name]" or "Editing: [Date] - [Venue]" | N/A (display only) | Working |

### Action Buttons Section

| Element | Type | Name | Action | API/Service Call | Status | Visible In |
|---------|------|------|--------|-----------------|--------|------------|
| Read from Files | Button | `ReadButton` | Re-loads current folder (redundant, auto-loads on browse) | `LoadFolder(FolderPathTextBox.Text)` | Working (unnecessary) | Import mode only |
| Normalize All Songs | Button | `NormalizeButton` | Normalizes all track titles using fuzzy matching, highlights unmatched songs in yellow/gold | `_normalizationService.NormalizeAll(_tracks)` | Working | Both modes |
| Renumber Tracks | Button | `RenumberButton` | Renumbers tracks using disc-aware 101/201/301 convention based on current row order (use after drag-to-reorder) | Disc-aware sequential numbering (line 424-451) | Working | Both modes |
| Match Setlist | Button | `MatchSetlistButton` | Matches imported tracks to known setlist data by song name, assigns disc/track numbers and segue flags from setlist. Enabled only when setlist data exists for the album date. Uses NormalizationService for fuzzy title matching and ShowLookupService for setlist lookup. Does not auto-apply — user reviews suggestions before import. Renumber button overrides setlist suggestions if clicked afterward. | `ShowLookupService.GetSetlist()`, `NormalizationService.Normalize()`, `ShowLookupService.GetDiscTrack()` | Working | Import mode only |
| View Info File | Button | `ViewInfoButton` | Opens non-modal window showing .txt info file content | Opens new Window with TextBox (line 823-851) | Working | Both modes |
| Import to Library | Button | `ImportButton` | Imports concert to library folder structure with progress bar | `_libraryImportService.ImportToLibrary(...)` | Working | Import mode only |
| Save Changes | Button | `SaveChangesButton` | Writes metadata to files AND updates library record in one operation, then closes window | `_metadataService.WriteMetadata()` → `_libraryImportService.ImportToLibrary()` → `Close()` | Working | Edit mode only |
| Cancel | Button | `CancelButton` | Closes window without saving | `this.Close()` | Working | Both modes |

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
| Album Type combobox | ComboBox | `AlbumTypeComboBox` | Sets `_albumInfo.Type` to `AudienceRecording` or `OfficialRelease` (or leaves at inferred value when Auto-detect is selected) | `AlbumTypeComboBox_SelectionChanged` → preview refresh | Working |

The combobox is the single album-type control. It replaced the prior four radio buttons (`LiveRecordingRadio`, `OfficialReleaseRadio`, `StudioAlbumRadio`, `BoxSetRadio`) when the `AlbumType` enum was collapsed to two values: `Studio`, `Live`, `OfficialRelease`, and `BoxSet` all map to today's `OfficialRelease`, and the original `Live` is now `AudienceRecording`.

#### Common Fields

All album-info fields are always visible and editable regardless of `Type`. There is no per-type field-visibility toggle in the current UI.

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Artist | TextBox | `ArtistTextBox` | Updates `_albumInfo.Artist`, triggers preview refresh | `AlbumInfo_Changed` → `UpdateAlbumPreview()` | Working |
| Date | TextBox | `DateTextBox` | Updates `_albumInfo.AlbumDate` (yyyy-MM-dd format), triggers preview refresh | `AlbumInfo_Changed` → `UpdateAlbumPreview()` | Working |
| Venue | TextBox | `VenueTextBox` | Updates `_albumInfo.Venue`, triggers preview refresh | `AlbumInfo_Changed` → `UpdateAlbumPreview()` | Working |
| City, State | TextBox | `CityStateTextBox` | Updates `_albumInfo.CityState`, triggers preview refresh | `AlbumInfo_Changed` → `UpdateAlbumPreview()` | Working |
| Album Name | TextBox | `AlbumNameTextBox` | Updates `_albumInfo.AlbumName`, triggers preview refresh | `AlbumInfo_Changed` → `UpdateAlbumPreview()` | Working |
| Collection Name | TextBox | `CollectionNameTextBox` | Updates `_albumInfo.CollectionName` (optional; applies to either type), triggers preview refresh | `AlbumInfo_Changed` → `UpdateAlbumPreview()` | Working |
| Year | TextBox | `YearTextBox` | Updates `_albumInfo.Year` (string), triggers preview refresh | `AlbumInfo_Changed` → `UpdateAlbumPreview()` | Working |

#### MusicBrainz Lookup (Available for both album types)

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Fingerprint Lookup | Button | `LookupAlbumButton` | Uses audio fingerprinting (AcoustID) to lookup album, shows ReleaseSelectorDialog, downloads artwork | `_musicBrainzService.LookupAllReleasesAsync(_tracks)` | Working (requires fpcalc.exe) |
| Manual Search | Button | `ManualSearchButton` | Opens AlbumSearchDialog, searches MusicBrainz by name/artist/year, shows ReleaseSelectorDialog | `_musicBrainzService.SearchReleasesByNameAsync(...)` | Working |

**Note:** MusicBrainz is a user-populated database that contains entries for all types of recordings. Both lookup methods are available regardless of album type selected.

#### Preview Display

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Album Title Preview | TextBlock | `AlbumPreviewTextBlock` | Read-only display of formatted album title from `_albumInfo.AlbumTitle` | N/A (display only) | Working |

### Track List Section

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Tracks grid | DataGrid | `TracksDataGrid` | Displays all tracks, shows preview metadata, highlights unmatched songs (yellow/gold background) | `_metadataService.ReadFolder(folderPath)` | Working |
| Row selection | DataGridRow | N/A | Updates selected track editor when row selected | `TracksDataGrid_SelectionChanged` → updates SelectedTitle/Date/Segue fields | Working |
| Row double-click | DataGridRow | N/A | Sends track to global playlist and plays it (delegates to PlayerWindow for playback controls) | `TracksDataGrid_MouseDoubleClick` → `AddTracksToPlaylist()` → `App.PlaybackService.Play(track)` | Working |
| Row loading | DataGridRow | N/A | Sets row background color based on normalization status | `TracksDataGrid_LoadingRow` → `UpdateRowBackground()` | Working |
| Row drag-to-reorder | DataGridRow | N/A | Drag rows to reorder tracks, visual drop indicator shows insertion point | `Row_PreviewMouseLeftButtonDown` → `Row_MouseMove` → `Row_Drop` → reorders `_tracks` collection | Working |

**Columns (in display order):**
- `#` - **Disc-aware track number (DisplayTrackNumber, read-only)** - **FIX 3:** Changed from editable TrackNumber to read-only DisplayTrackNumber. Shows disc-aware format (101, 102, 201, 202, 301, 302...) instead of raw 1, 2, 3. Format: `{DiscNumber}{TrackNumber:D2}`. Single-disc albums show 101, 102, 103... Multi-disc albums show 101, 102... (Disc 1), 201, 202... (Disc 2). **Not sortable** (use Disc column for sorting)
- `Disc` - Disc number (DiscNumber) - **Editable:** Click cell and type new disc number for multi-disc albums. **Sortable:** Compound sort by disc number (primary), then track number (secondary). Click header to sort ascending (Disc 1 Track 1...N, Disc 2 Track 1...N), click again for descending (Disc 3 Track 1...N, Disc 2 Track 1...N, Disc 1 Track 1...N)
- `Title` - Display title (normalized or original) (DisplayTitle) - Takes remaining width when window resizes. **Sortable:** Alphabetical sort
- `→` - Segue checkbox (Segue) - **Positioned immediately after Title** with no gap. **Not sortable**
- `Date` - **Effective date** (TrackDate or inherited from album) - Shows track-specific date in white, or album date in muted steel blue-gray if no track date set. **Sortable:** Chronological sort
- `Time` - Track duration (Duration). **Sortable:** Duration sort

**Layout:**
- Font size: **18pt** for improved readability
- Grid is in side-by-side layout with artwork panel on right (230px fixed width)
- Track grid takes all remaining width when window resizes (scalable)

### Selected Track Editor Section

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Title | TextBox | `SelectedTitleTextBox` | Updates selected track's `Title` property, refreshes preview | `SelectedTrack_Changed` → `UpdateTrackPreviews()` | Working |
| Segue checkbox | CheckBox | `SelectedSegueCheckBox` | Updates selected track's `HasSegue` property, refreshes preview | `SelectedTrack_Changed` → `UpdateTrackPreviews()` | Working |
| Performance Date | TextBox | `SelectedDateTextBox` | Updates selected track's `PerformanceDate` property (for live tracks in studio albums) | `SelectedTrack_Changed` → `UpdateTrackPreviews()` | Working |

### Audio Playback

**REMOVED in Phase 6:** Playback controls have been removed from MainWindow. Audio playback is now handled globally by PlayerWindow.

**Current Behavior:**
- When a folder is loaded via `LoadFolder()`, tracks are automatically sent to the global playback service: `App.PlaybackService.LoadPlaylist(trackList)`
- Users can play tracks using PlayerWindow (see [17-player-window.md](17-player-window.md))
- MainWindow opens as a **non-modal window** (using `Show()`) so the player remains fully interactive during import
- Library grid refreshes automatically when MainWindow closes via `Closed` event handler

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
   - **FIX 4 & 5:** `_metadataService.ReadFolder(folderPath, editMode: _mode == WindowMode.Edit)` reads all audio files
     - **Import Mode (editMode = false):** Runs full transformation pipeline
       - Calls `ParseTitleAndDate()` to extract song name and date from titles
       - Strips segue markers from SongName (FIX 1)
       - Runs `DetectSegues()` to auto-identify common segue pairs
     - **Edit Mode (editMode = true):** Reads tags as-is, no transformations
       - FLAC file tags ARE the source of truth (already contain final formatted metadata)
       - SongName = raw TITLE tag (e.g., "Help on the Way > (1975-08-12)")
       - Skips ParseTitleAndDate and DetectSegues
   - **FIX 5:** Multi-night sort logic applied to track list
     - Detects multi-night albums by counting distinct TrackDate values
     - **Multi-night (2+ dates):** Sort by TrackDate → DiscNumber → TrackNumber
     - **Single-night with discs:** Sort by DiscNumber → TrackNumber
     - **Single-night sequential:** Sort by TrackNumber only
   - If no audio files found → shows "No Files" notification, exits workflow
   - `_metadataService.ReadAlbumInfo(folderPath, _tracks)` parses folder name and reads info file (line 124)
     - **FIX 2:** No longer fabricates "{Year}-01-01" dates from Year tag
     - **FIX 1:** If ParseAlbumTitle() doesn't match concert-style patterns, raw ALBUM tag stored in AlbumName (handles official releases)
     - Folder name format: `yyyy-MM-dd [Venue], [City, State]` or `[Album Name] ([Year])`
     - Reads `.txt` info file if present (e.g., `2024-10-31.txt`)
   - `RefreshUI()` populates all fields (line 127)
     - Sets album type radio button (defaults to Live Recording)
     - Populates Date, Venue, City, State fields from parsed folder name
     - **For official releases:** Populates AlbumName field from raw ALBUM tag (via Fix 1)
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

5. **User clicks "Import to Library" button**
   - `ImportButton_Click` fires (line 866)
   - If no tracks or album info → shows "No Files" notification, exits
   - If `_librarySettings.LibraryRootPath` not set → shows "Library Not Set" notification, exits
   - If audience recording with no date → shows "Missing Date" notification, exits
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

**Success Path:** Folder loaded → Songs normalized → Imported to library (tags written to managed copy) → UI cleared
**Failure Paths:**
- No audio files → notification, stop at step 2
- Songs not in database → highlighted in yellow, user can proceed with original titles or add songs
- Library path not set → notification, stop at step 5
- Import cancelled or failed → UI not cleared, user can retry

---

### Workflow 2: Import a Studio Album

**Goal:** Import a studio album with MusicBrainz metadata lookup.

1. **User clicks "Select Folder" button**
   - Same as Workflow 1 steps 1-2
   - `LoadFolder()` reads files and parses folder name
   - If folder name format is `[Album Name] ([Year])` → auto-detects as `OfficialRelease` (studio-style)
   - Otherwise defaults to `AudienceRecording` (user may change via the combobox)

2. **User selects "Official Release" in the album-type combobox**
   - `AlbumTypeComboBox_SelectionChanged` fires
   - Sets `_albumInfo.Type = AlbumType.OfficialRelease`
   - All album-info fields remain visible/editable (no per-type field-visibility toggle in current UI)
   - `UpdateAlbumPreview()` formats title via `AlbumInfo.AlbumTitle` — when Date+Venue are empty, it falls back to `[AlbumName] ([Year])`
   - `UpdateTrackPreviews()` updates preview without date appended for studio-style imports

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

7. **User clicks "Import to Library" button**
   - `ImportButton_Click` fires (line 866)
   - Same validation checks as Workflow 1 step 5
   - Confirmation shows: "Structure: Studio Albums\[Album Name] ([Year])\"
   - User clicks "Yes"
   - `_libraryImportService.ImportToLibrary(...)` runs
   - Copies files to `[LibraryRoot]/Studio Albums/[Album Name] ([Year])/`
   - Tags written to managed copy. **Important:** Studio album tracks do NOT get date appended (line 190-192) — preview shows clean song titles like "Morning Dew", not "Morning Dew (1972-05-04)"
   - Success notification shows full path
   - `ClearView()` resets UI

**Success Path:** Folder loaded → Studio Album selected → MusicBrainz lookup → Metadata reviewed → Imported (tags written to managed copy)
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
   - Defaults to `AudienceRecording` unless folder name signals otherwise

2. **User selects "Official Release" in the album-type combobox**
   - `AlbumTypeComboBox_SelectionChanged` fires
   - Sets `_albumInfo.Type = AlbumType.OfficialRelease` (box sets no longer have a dedicated enum value; they are a flavor of `OfficialRelease` distinguished by populating `CollectionName`)
   - `UpdateAlbumPreview()` updates preview

3. **User enters/edits metadata**
   - Collection Name: e.g., "Enjoying the Ride", "Digital Album Live" (stored on `_albumInfo.CollectionName`)
   - Date, Venue, City/State for this specific concert
   - All fields are always visible/editable

4. **User normalizes, writes, and imports** (same as Workflow 1 steps 3-6)
   - Import path: universal `{LibraryRoot}/{Artist}/{AlbumFolder}/` from `LibraryImportService.BuildLibraryFolderName()`
   - `_librarySettings.LastBoxSetName` is updated on successful import so the next import can pre-fill the same collection name

5. **User imports additional concerts from same box set**
   - Click "Select Folder", choose next concert folder
   - User keeps the same Collection Name (or it pre-fills from `LastBoxSetName`)
   - Import → same collection name applied consistently across concerts

**Success Path:** Folder loaded → `OfficialRelease` selected → Collection Name entered → Imported → Name remembered for next import

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

4. **User clicks "Save Changes"**
   - Writes metadata in place to the managed-library files
   - LibraryBrowserWindow refreshes to show updated metadata

**Success Path:** Concert loaded → Metadata edited → Saved → Library refreshed
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

5. **User clicks "Import to Library"**
   - Files copied to the managed library and metadata written with new track numbers to the managed copies

**Success Path:** Tracks loaded → Dragged to correct order → Renumbered → Imported to library

**Notes:**
- Dragging does NOT automatically renumber - user must click Renumber button
- Drag-to-reorder works independently of disc numbers (move tracks across disc boundaries freely)
- After reordering, Renumber button respects current row order when assigning numbers
- Visual feedback (insertion line) shows exactly where row will land before dropping

### Match Setlist Workflow

When the album date corresponds to a show with setlist data in shows.json (~1,800 shows), the "Match Setlist" button becomes enabled and decorates matched tracks with canonical song names and segue flags from the setlist.

**Audio is the archive.** `shows.json` documents what songs were *performed*; the audio documents what was *recorded*. They overlap heavily but are not identical — an audience tape may include tuning passages, banter, or applause that no setlist would document. DeadEditor preserves the audio recording as the archival truth: Match Setlist decorates matched tracks with names and segues, and leaves everything else (including disc/track numbers, and unmatched tracks of any kind) untouched.

**How it works:**
1. **Button state:** Enabled only when `ShowLookupService.GetSetlist(date)` returns non-null. Tooltip shows song/set count when enabled, "No setlist data for this date" when disabled.
2. **User clicks "Match Setlist":**
   - Flattens the setlist across all sets into an ordered song list, resolving each entry to its canonical title via `NormalizationService.GetOfficialTitle()`.
   - For each imported track, normalizes the title via `NormalizationService.Normalize()` and resolves to canonical for comparison (case-insensitive).
   - For matched tracks: sets `SongName` to the canonical setlist title, copies the segue flag, marks the track matched and modified.
   - For unmatched tracks: leaves them entirely untouched. Original disc/track numbers, song name, and segue are preserved.
3. **Duplicate handling:** If a song appears multiple times in the setlist, each import track matches the first unclaimed setlist occurrence (positional order).
4. **No overflow disc, no renumbering.** A 14-track audience recording with 2 tuning passages stays as 14 tracks in their original audio sequence — the tuning tracks keep their position between the songs they actually sit between in the recording. Disc and track numbers reflect the source recording's structure, not the setlist's.
5. **User reviews:** The grid updates immediately; user can manually edit any cell before import.
6. **Renumber** is unaffected. Clicking it after Match Setlist re-establishes 100-format numbering based on current row order — same job as before.

**Status message:** "Matched N of M tracks to setlist, K segues. X unmatched — right-click to match manually."

The matching algorithm is implemented in `Services/SetlistMatcher.cs` as a pure static method, parameterized on a canonical-resolver callback and unit-tested without WPF. The click handler in `ImportView.xaml.cs` does only I/O (reading the date textbox, flattening the setlist, applying status text).

**EditMetadataView's Match Setlist** ([feature-parity-spec.md §6](feature-parity-spec.md)) has been non-destructive since its introduction. Both views now share the same Model B framing: matched tracks get decoration; unmatched tracks are left alone.

The audio-as-archive design framing that motivated this behavior is captured in [audio-as-archive-design-memo.md](audio-as-archive-design-memo.md).

### Right-Click "Match to Song..." (Manual Matching)

After Match Setlist runs, unmatched tracks can be manually matched to unclaimed setlist songs via the right-click context menu:

1. **Right-click an unmatched track** (any track where `IsMatched != true` after Match Setlist has been run) → context menu shows "🎵 Match to Song..." item (below separator, after Play Now / Add to Playlist).
2. **Dialog appears** showing:
   - The track's current title (in gold)
   - A list of setlist songs that have NOT yet been matched, with set labels (e.g., "Goin' Down The Road Feelin' Bad (Set 2, #9)")
3. **User selects a song and clicks Match:**
   - Track's `SongName` is set to the canonical setlist title.
   - Segue flag is applied if the setlist indicates one.
   - Track's prior title is added as an alias in `songs.json` for that canonical name (so future imports of the same variant match automatically).
   - **Disc and track numbers are NOT changed.** The track stays where it sits in the audio sequence; only the displayed name and segue update.
   - Status message updates with new match count.
4. **Disabled when:** Track is already matched, no setlist data exists, Match Setlist hasn't been run yet, or all setlist songs are already matched.

**Auto-alias learning:** When a track like "Goin' Down the Road Feeling Bad" is matched to "Goin' Down The Road Feelin' Bad", the variant is saved as an alias in `songs.json`. Next time any recording with that variant is imported, it matches automatically without manual intervention.

### Right-Click "Track Info" (File Metadata Inspector)

The Track Info dialog (`TrackInfoDialog.xaml.cs`) is a diagnostic tool for inspecting a track's actual metadata. Opened via the right-click context menu on any track in the grid.

**Two-section layout:**

1. **File Metadata** — Read fresh from FLAC/MP3 tags on disk using TagLib#. Shows ground-truth values:
   - Standard tags: Title, Track Number, Disc Number, Artist, Album Artist, Album, Year, Genre, Comment, Duration
   - Custom FLAC Vorbis Comment fields: ALBUMDATE, VENUE, CITYSTATE, ALBUMNAME, ALBUMTYPE
   - For MP3: reads equivalent ID3v2 user text frames

2. **Import Status** — In-memory pipeline state from the TrackInfo object:
   - File Path, Original Title (RawTitle), Current Song Name, Is Matched, Is Modified

**Key design principle:** The File Metadata section always reflects what is actually stored in the file on disk, NOT the in-memory working values that Normalize/Match Setlist/Match to Song may have changed. This makes it a reliable diagnostic tool for verifying whether changes have been written to files.

**Error handling:** If the file cannot be read (locked, missing, etc.), an error message is displayed in the File Metadata section instead of crashing.

---

## Data Flow

### Inputs

**Files:**
- Audio files (FLAC/MP3) from user-selected folder
- Existing ID3 tags in audio files (if present)
  - **IMPORTANT - Album-Level Field Mapping:** `ReadAlbumInfo()` reads from first track's FLAC file using three-tier approach:
    1. **Tier 1:** Try custom FLAC Vorbis Comment fields first (ALBUMDATE, VENUE, CITYSTATE, ALBUMNAME, ALBUMTYPE)
    2. **Tier 2:** Fallback to `ParseAlbumTitle()` to extract metadata from ALBUM tag using 4 regex patterns (concert-based formats)
    3. **Tier 3 (FIX 1):** If no pattern matches and AlbumName still empty, use raw ALBUM tag value as AlbumName
  - **Why Tier 3 Needed:** Official releases like "Blues for Allah 50th Anniversary (2025)" don't match concert-style regex patterns, so raw ALBUM tag must be preserved
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
- Confirmation dialogs (import, overwrite)

**External APIs:**
- MusicBrainz API (via `_musicBrainzService`)
  - Audio fingerprinting via AcoustID + fpcalc.exe
  - Text search by album name/artist
  - Release metadata (title, year, label, country, format)
  - Cover Art Archive (artwork URLs)

### Outputs

**Files Written:**
- **When "Import to Library" clicked:**
  - Copies all audio files to library folder structure
  - Writes metadata to copied files (source files are never written to)
    - Tags written: Artist, Album, Title, Track#, Year, Date, Genre, Comment, AlbumArtist
    - Embedded artwork (APIC frame) if `_albumInfo.ArtworkData` present
  - Copies artwork as `cover.jpg` in album folder
  - Copies info file (if present) to album folder
  - Creates folder structure (universal layout, all types): `{LibraryRoot}/{Artist}/{AlbumFolder}/`
    - `AlbumFolder` = `{Artist} - {Date} - {Venue} - {City}, {State} - {AlbumName}` for live recordings
    - `AlbumFolder` = `{Artist} - {Year} - {AlbumName}` for studio-style official releases
    - See [13-library-import-service.md](13-library-import-service.md) for the full naming rules.

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

All album types share the universal library path `{LibraryRoot}/{Artist}/{AlbumFolder}/`. What varies is how `AlbumFolder` is composed (see [13-library-import-service.md](13-library-import-service.md)) and whether the track filename carries a date.

**AudienceRecording:**
- Fields populated: AlbumDate, Venue, CityState, AlbumName (optional), CollectionName (optional)
- Album title format: `yyyy-MM-dd - Venue - City, ST` (with optional album-name and collection-name suffixes)
- Track filenames include the date: `01 - Song Title (1972-05-04).flac`

**OfficialRelease — live flavor (Date + Venue present):**
- Fields populated: AlbumDate, Venue, CityState, AlbumName, CollectionName (optional)
- Album title format: `yyyy-MM-dd - Venue - City, ST - AlbumName` (with optional collection-name suffix)
- Track filenames omit the date: `01 - Song Title.flac`

**OfficialRelease — studio-style (Date or Venue empty):**
- Fields populated: AlbumName, Year, CollectionName (optional); Edition is preserved as a separate field
- Album title format: `AlbumName (Year)` (year omitted if AlbumName already contains parenthesized year)
- Track filenames omit the date
- Hybrid albums (studio + live bonus tracks) stay as a single `OfficialRelease`

**Box sets:** No longer a separate type. A box set is an `OfficialRelease` whose `CollectionName` holds the box-set name. The same naming rules above apply. `LastBoxSetName` is remembered between imports for the user's convenience.

### Segue Detection
- **Notation:** `China Cat Sunflower > I Know You Rider`
- **Storage:** Tracks stored separately, linked with `>` marker
- **Display:** Both song names shown with segue marker in UI
- **Checkbox:** `HasSegue` flag in track list (→ column)

### Artwork Handling
- **Supported formats:** JPEG, PNG
- **Storage:** In-memory as byte array in `_albumInfo.ArtworkData`
- **MIME type:** Detected from file extension, stored in `_albumInfo.ArtworkMimeType`
- **Embed on import:** Written to ID3 APIC frame on the managed copy during Import to Library
- **Import to library:** Saved as `cover.jpg` in album folder
- **MusicBrainz download:** Automatically downloads from Cover Art Archive if available

### Metadata Writing Rules
- **Write target:** Managed-library copies only — Import to Library writes to the copied files. Source files are never written to from Import view.
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

### 1. Deprecated Settings Menu Item
**Location:** Menu bar, `SettingsMenuItem_Click()` line 797
**Issue:** Settings menu item in MainWindow just shows a message box instead of opening settings
**Message:** "Settings can be accessed from the main Library window."
**Recommendation:** Remove this menu item entirely from MainWindow
**Status:** Working as designed but should be removed for clarity

### 2. Read Button Redundancy
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
