# LibraryBrowserWindow — Library Browser & Music Player

## Purpose

LibraryBrowserWindow is the main application window and serves as both the library browser and music player for DeadEditor. It scans configured library paths to discover all imported concerts (audience recordings, official releases, and studio albums), displays them in a searchable grid with type-based filtering, and provides detailed concert views with integrated audio playback. The window supports quick text search across metadata fields, advanced song-based searches (contains/exclude/sequence), and seamless playback with media key support. Users can double-click concerts to view tracks and play music, edit metadata without leaving the library, and navigate between library grid and concert detail views while music continues playing.

## Screen Layout

The LibraryBrowserWindow uses a dark theme (#1E1E1E background) with four main sections:

**Top Section (Menu Bar):**
- File menu with Import, Settings, Exit

**Search Bar (Second Row):**
- Type filter dropdown (All, Audience Recordings, Official Releases, Studio Albums)
- Quick search textbox with clear button
- Advanced Search button
- Search result count display

**Main Content Area (Adaptive Views):**

**Library Grid View** (default, Visibility="Visible"):
- DataGrid showing all concerts with columns:
  - Icon (🎸/📀/💿 based on type)
  - Date / Album (adaptive based on type)
  - Venue / Year (adaptive based on type)
  - Location / Edition (adaptive based on type)
  - Official Release (for box sets/releases)
  - Tracks (count)
- Double-click row to open concert view

**Concert Detail View** (Visibility="Collapsed" until opened):
- Left column (340px):
  - Back/Edit buttons bar
  - Album artwork (300x300 with placeholder)
  - Venue/Album name, Location/Year, Date
  - Box set name (if applicable)
  - Track count
- Right column:
  - **TRACKS header** with Jump to Date dropdown (multi-night shows only):
    - Dropdown appears above track list when album contains tracks from multiple concert dates
    - Label: "Jump to Date:" followed by ComboBox showing all dates (width 180px)
    - Selecting a date scrolls to that date's section header in the track list
    - ComboBox resets to no selection after navigation (acts as navigation trigger only)
    - Hidden for single-date concerts
  - **Track list DataGrid** with columns: #, Title, Duration
  - For multi-night shows: tracks grouped by date with collapsible section headers
  - Expand/Collapse All buttons (⊞/⊟) appear with Jump to Date dropdown
  - Double-click row to play track

**Bottom Section (Status Bar / Placeholder Bar):**
- **Library view:** Status text ("Double-click a concert to open")
- **Concert view:** Placeholder bar showing:
  - "Now Playing: [track]" when PlayerWindow is docked (or empty if nothing playing)
  - "⊞ Re-dock Player" button when PlayerWindow is undocked

**Playback Note:** Audio playback is handled globally by PlayerWindow. See [documentation/17-player-window.md](17-player-window.md) for playback details.

## Interactive Elements

### Menu Bar

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Import New Concert | MenuItem | N/A | Opens MainWindow (non-modal), refreshes library when window closes | `new MainWindow().Show()` + `Closed` event → `LoadShows()` | Working |
| Settings | MenuItem | N/A | Opens SettingsWindow dialog, refreshes library after close | `new SettingsWindow(...).ShowDialog()` → `LoadShows()` | Working |
| Exit | MenuItem | N/A | Closes window (terminates application) | `Close()` | Working |

### Search Bar

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Show type filter | ComboBox | `TypeFilterComboBox` | Filters by album type (All/Live/OfficialRelease/Studio/**ByDate**), re-applies all filters | `TypeFilterComboBox_SelectionChanged` → `ApplySearchFilter()` | Working |
| Quick search textbox | TextBox | `QuickSearchTextBox` | Searches date, venue, location, album name, year, edition, official release; shows/hides clear button | `QuickSearchTextBox_TextChanged` → `ApplySearchFilter()` | Working |
| Clear search button | Button | `ClearSearchButton` | Clears quick search + advanced search criteria, resets to all shows | `ClearSearchButton_Click` clears `_quickSearchText`, `_advancedSearchSongs`, etc. | Working |
| Advanced Search button | Button | `AdvancedSearchButton` | Opens AdvancedSearchDialog, applies song-based filters (contains/exclude/sequence) | `new AdvancedSearchDialog(...).ShowDialog()` → `ApplySearchFilter()` | Working |
| Search result count | TextBlock | `SearchResultTextBlock` | Shows "Showing X of Y concerts" when filtered | N/A (display only) | Working |

**Type Filter Options:**
- **All** - Shows all concerts (default)
- **Audience Recordings** - Shows only taper recordings and audience recordings
- **Official Releases** - Shows only official live releases, box sets, and studio albums
- **📅 By Date** - Special date-based view that displays individual concert dates as rows (see below)

### "By Date" View Mode

The "📅 By Date" option in the type filter switches the library grid from album-based display to **date-based display**. This mode is especially useful for box sets and official releases that span multiple concert dates.

**How It Works:**

1. **Official Releases & Box Sets:** Dates are extracted from track TITLE tags (authoritative source)
   - Reads all tracks from all folders for each LibraryShow
   - Extracts unique dates from `track.TrackDate` (populated from TITLE tag parsing)
   - Creates one ConcertDate row per unique date found
   - Track count shows tracks for that specific date only (not the entire box set)

2. **Audience Recordings:** Dates are extracted from folder names
   - Uses the date parsed from the folder name (e.g., "1977-05-08 Barton Hall...")
   - For multi-folder albums with the same Album tag, matches folders by date string
   - Track count shows tracks in matched folder(s) for that date

**Example Use Case:**

"Enjoying the Ride" box set contains 61 total tracks from multiple concerts at Alpine Valley Music Theatre in 1980 and 1981. In album view, it shows as ONE row with 61 tracks. In "By Date" view, it shows as MULTIPLE rows:
- 1980-08-23 - Alpine Valley Music Theatre - East Troy, WI - 28 tracks
- 1981-08-12 - Alpine Valley Music Theatre - East Troy, WI - 33 tracks

**Grid Columns in By Date View:**

The same DataGrid columns are reused, but bound to `ConcertDate` properties instead of `LibraryShow`:

| Header | Binding Property | Description | Data Source |
|--------|-----------------|-------------|-------------|
| (icon) | `TypeIcon` | 📅 (always) | Fixed for date view |
| Date | `Date` | Concert date (yyyy-MM-dd) | From track TITLE tags (Official Releases) or folder name (Audience Recordings) |
| Venue | `Venue` | Venue name | From LibraryShow.Venue |
| Location | `Location` | "City, State" | From LibraryShow.Location |
| Collection | `CollectionName` | Album/Release/Box Set name | From LibraryShow.AlbumName or LibraryShow.OfficialRelease |
| Tracks | `TrackCount` | Track count for this date only | Count of tracks with matching TrackDate |

**Double-Clicking a Date Row:**

Opens the concert detail view scoped to only the tracks from that specific date:
- `ShowsDataGrid_MouseDoubleClick` detects `ConcertDate` type
- Calls `OpenConcertDateView(concertDate)` instead of `OpenConcertView(show)`
- Loads tracks from only the folder(s) associated with that date
- Displays collection name (e.g., "Collection: Enjoying the Ride")

**Performance Note:**

Building the "By Date" view for Official Releases requires reading track metadata from disk (to extract dates from TITLE tags). This happens lazily when the user selects "📅 By Date" from the filter dropdown, not during initial library load.

### Library Grid View

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Shows grid | DataGrid | `ShowsDataGrid` | Displays all concerts (filtered), double-click to open concert view | `ShowsDataGrid_MouseDoubleClick` → `OpenConcertView(show)` | Working |
| Status text | TextBlock | `StatusText` | Shows concert count or search messages | N/A (display only) | Working |

**Grid Columns (Data Binding):**

| Header | Binding Property | Description | Data Source |
|--------|-----------------|-------------|-------------|
| (icon) | `TypeIcon` | 🎸 (Live), 📀 (Official Release), 💿 (Studio) | Computed from `LibraryShow.Type` |
| Date / Album | `PrimaryInfo` | Date for Live/Official, Album Name for Studio | Adaptive property: `Date` or `AlbumName` |
| Venue / Year | `SecondaryInfo` | Venue for Live, Year for Studio, Dates for Official (multi-date) | Adaptive: `Venue`, `ReleaseYear`, or `ContainsDates` |
| Location / Edition | `TertiaryInfo` | "City, State" for Live, Edition for Studio, Venues for Official (multi-venue) | Adaptive: `Location`, `Edition`, or `ContainsVenues` |
| Official Release | `OfficialRelease` | Official Release name or Box Set name | From ID3 tags `Album` field (parsed for `:` format) |
| Tracks | `TrackCount` | Number of audio files in folder | File count (FLAC/MP3) |

### Concert Detail View

#### Back and Edit Buttons

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Back to Library | Button | `BackButton` | Returns to grid view, keeps playback running, keeps player controls visible | `BackButton_Click` switches visibility, doesn't stop `_updateTimer` | Working |
| Edit Metadata | Button | `EditMetadataButton` | Opens MainWindow (non-modal) with current concert folder, reloads library + concert view when window closes | `new MainWindow().LoadFolder()` + `.Show()` + `Closed` event → `LoadShows()` | Working |

#### Concert Information Display

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Artwork | Image | `AlbumArtwork` | Shows artwork from `cover.jpg`, `folder.jpg`, or embedded APIC tag | Loads from file or `TagLib.File.Tag.Pictures[0]` | Working |
| Artwork placeholder | Grid | `ArtworkPlaceholder` | Shows 🎵 icon when no artwork found | N/A (display only) | Working |
| Venue/Album name | TextBlock | `VenueText` | Shows venue name for live, album name for studio | From `LibraryShow.Venue` or `LibraryShow.AlbumName` | Working |
| Location/Year | TextBlock | `LocationText` | Shows "City, State" for live, "Released YYYY" for studio | From `LibraryShow.Location` or `LibraryShow.ReleaseYear` | Working |
| Date | TextBlock | `DateText` | Shows concert date (yyyy-MM-dd), empty for studio albums | From `LibraryShow.Date` | Working |
| Box set name | TextBlock | `BoxSetText` | Shows "Box Set: [name]" for box sets only | From `LibraryShow.OfficialRelease` when `Type == AlbumType.BoxSet` | Working |
| Track count | TextBlock | `TrackCountText` | Shows "X tracks" | From `_currentTracks.Count` | Working |

#### Track List

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Tracks grid | DataGrid | `TracksDataGrid` | Displays all tracks in concert, double-click row to play | `TracksDataGrid_LoadingRow` adds double-click handler | Working |

**Columns:**
- `#` - Disc-aware track number (`TrackInfo.DisplayTrackNumber`)
  - Displays disc-aware format when FLAC files contain DISCNUMBER tags:
    - Single-disc albums: 101, 102, 103, 104... (Disc 1)
    - Multi-disc albums: 101, 102... (Disc 1), 201, 202... (Disc 2), 301, 302... (Disc 3)
    - Format: `{DiscNumber}{TrackNumber:D2}` (e.g., Disc 2, Track 3 → "203")
  - Albums without DISCNUMBER tags display sequential track numbers: 1, 2, 3... 29
  - **Sort behavior:** Column sorts by numeric TrackNumber property (not the displayed string)
    - With disc tags: Sorts by DiscNumber, then TrackNumber
    - No disc tags: Sorts by TrackNumber alone (correct for sequential numbering)
  - Fallback: Treats missing/zero `DiscNumber` as Disc 1
- `Title` - Song title with segue markers (`TrackInfo.PreviewMetadata`)
- `Duration` - Track duration (`TrackInfo.Duration`)

#### Track Interactions — Playlist Population

The concert detail view provides multiple ways to add tracks to the global playlist managed by `AudioPlayerService`:

**1. Double-Click Track**
- **Action:** Adds track + forward segue chain to playlist (if not already present) and immediately plays the clicked track
- **Handler:** `TracksDataGrid_MouseDoubleClick`
- **Segue Chain Logic:** Uses `GetForwardSegueChain()` helper to walk forward through consecutive tracks that have `Segue = true`
  - Example: Double-clicking "China Cat Sunflower >" adds both "China Cat >" and "I Know You Rider" (if Rider doesn't segue)
  - Example: Double-clicking "Dark Star" (no segue) adds only "Dark Star"
  - Stops when reaching a track that does NOT segue (no `>` marker)
- **Playlist Behavior:** Uses `AddTracksToPlaylist()` helper to check for duplicates via `TrackInfo.Equals()` (FilePath comparison)
- **Implementation:**
  ```csharp
  var track = GetTrackFromDataGridSelection(TracksDataGrid.SelectedItem);
  if (track != null) {
      var segueChain = GetForwardSegueChain(track, _currentTracks);
      AddTracksToPlaylist(segueChain);
      App.PlaybackService.Play(track);
  }
  ```

**2. Right-Click Context Menu**

The track DataGrid includes a context menu with the following options:

| Menu Item | Shortcut | Action | Plays Track? | Implementation |
|-----------|----------|--------|--------------|----------------|
| ▶ Play Now | (none) | Adds track + forward segue chain to playlist and plays it | Yes | `PlayNowMenuItem_Click` → `GetForwardSegueChain()` → `AddTracksToPlaylist()` → `Play(track)` |
| ＋ Add to Playlist | (none) | Adds track + forward segue chain to playlist without playing | No | `AddToPlaylistMenuItem_Click` → `GetForwardSegueChain()` → `AddTracksToPlaylist()` |
| ✕ Remove from Playlist | (none) | Removes track from playlist | No | `RemoveFromPlaylistMenuItem_Click` → `Playlist.Remove(track)` |
| ＋ Add Selected to Playlist | (none) | Adds all selected tracks (multi-select) | No | `AddSelectedToPlaylistMenuItem_Click` → loops selected items |

**Segue Chain Behavior (Play Now & Add to Playlist):**
- Both "Play Now" and "Add to Playlist" use the same segue chain logic as double-click
- Uses `GetForwardSegueChain()` to walk forward through consecutive tracks with `Segue = true`
- Example: Right-clicking "China Cat Sunflower >" → adds both "China Cat >" and "I Know You Rider"
- Example: Right-clicking "Dark Star" (no segue) → adds only "Dark Star"
- Stops when reaching a track that does NOT segue (no `>` marker)
- This ensures consistent playlist behavior across all three interaction methods (double-click, Play Now, Add to Playlist)

**Context Menu Smart Behavior:**
- **Remove from Playlist** is disabled (greyed out) if the track is NOT currently in the playlist
- **Add Selected to Playlist** is disabled if only one track is selected
- Menu opening handler: `TracksContextMenu_Opened` checks `Playlist.Contains(track)` to enable/disable items

**3. Add All Button (Multi-Night Concerts Only)**

For box sets and official releases spanning multiple concert dates, each date section header includes an **"＋ Add All"** button:

| Element | Visibility | Location | Action | Implementation |
|---------|-----------|----------|--------|----------------|
| Add All button | Multi-night view only | Right side of date section header | Adds all tracks for that date to playlist | `AddAllTracksButton_Click` → filters `_currentTracks` by `TrackDate` |

**Behavior:**
- Button.Tag contains the concert date (yyyy-MM-dd)
- Filters `_currentTracks.Where(t => t.TrackDate == date)` to get all tracks for that date
- Calls `AddTracksToPlaylist(tracksForDate)` to add all tracks at once
- Shows confirmation message in status bar: "X tracks from yyyy-MM-dd added to playlist" (auto-clears after 3 seconds)

**4. Multi-Select Support**

The tracks DataGrid supports standard Windows multi-selection:
- **Ctrl+Click:** Select/deselect individual tracks
- **Shift+Click:** Select range of tracks
- **Right-click → Add Selected to Playlist:** Adds all selected tracks at once

**Implementation:** `TracksDataGrid.SelectedItems` is enumerated, each item is passed through `GetTrackFromDataGridSelection()` to extract the `TrackInfo` (handles both single-night `TrackInfo` and multi-night `TrackViewItem` wrappers).

**Duplicate Prevention:**

All playlist addition methods use the `AddTracksToPlaylist()` helper:
```csharp
private void AddTracksToPlaylist(IEnumerable<TrackInfo> tracks)
{
    foreach (var track in tracks)
    {
        // FilePath equality already handled by TrackInfo.Equals()
        if (!App.PlaybackService.Playlist.Contains(track))
            App.PlaybackService.Playlist.Add(track);
    }
}
```

This ensures tracks are never duplicated in the playlist, even if added multiple times from different UI interactions.

**Helper Method — GetTrackFromDataGridSelection:**

Because the DataGrid shows different item types in single-night vs multi-night views, a helper method extracts the `TrackInfo`:

```csharp
private TrackInfo? GetTrackFromDataGridSelection(object? item)
{
    if (item == null) return null;

    // Single-night view: item is directly TrackInfo
    if (item is TrackInfo track) return track;

    // Multi-night view: item is TrackViewItem wrapping TrackInfo
    if (item is TrackViewItem trackViewItem) return trackViewItem.Track;

    // DateHeaderItem or other type - not a track
    return null;
}
```

### Audio Player Controls

#### Now Playing Display

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Now playing title | TextBlock | `NowPlayingText` | Shows "♪ [Song Title]" or "No track playing" | Updated in `PlayTrack()` | Working |
| Now playing details | TextBlock | `NowPlayingDetails` | Shows "Track X - Y:YY" | Updated in `PlayTrack()` | Working |

#### Placeholder Bar (Status Bar)

**REMOVED in Phase 6:** Playback controls have been removed from LibraryBrowserWindow. Audio playback is now handled globally by PlayerWindow.

**Current Status Bar Elements:**

| Element | Type | Name | Action | API/Service Call | Status |
|---------|------|------|--------|-----------------|--------|
| Status text | TextBlock | `StatusText` | Shows "Double-click a concert to open" in library view | N/A (display only) | Working |
| Now Playing placeholder | TextBlock | `NowPlayingPlaceholder` | Shows "Now Playing: [track]" when PlayerWindow is docked | Updated by `AudioPlayer_TrackChanged` | Working |
| Re-dock Player button | Button | `RedockPlayerButton` | Shows "⊞ Re-dock Player" when PlayerWindow is undocked, re-docks when clicked | `RedockPlayerButton_Click` → finds PlayerWindow → calls `DockToMainWindow()` | Working |

**Placeholder Bar Behavior:**
- **Library View:** Shows `StatusText` ("Double-click a concert to open")
- **Concert View + PlayerWindow Docked:** Shows `NowPlayingPlaceholder` with current track
- **Concert View + PlayerWindow Undocked:** Shows `RedockPlayerButton` to bring PlayerWindow back

**Playback Integration:**
- Double-clicking a concert opens the concert detail view (does NOT modify playlist)
- Playlist is only modified by explicit user actions: double-click track, right-click → Add/Play Now, + Add button
- Double-clicking a track adds the segue chain to playlist and plays the track
- All transport controls, scrubber, and volume are in PlayerWindow (see [17-player-window.md](17-player-window.md))

### Media Key Support

**CHANGED in Phase 6:** Media key handlers have been stubbed out (no-op). Media keys are now handled by PlayerWindow when it has focus.

| Media Key | Action | Handler | Status |
|-----------|--------|---------|--------|
| Play/Pause key | Handled by PlayerWindow | `WndProc()` → no-op | Deferred to PlayerWindow |
| Stop key | Handled by PlayerWindow | `WndProc()` → no-op | Deferred to PlayerWindow |
| Next Track key | Handled by PlayerWindow | `WndProc()` → no-op | Deferred to PlayerWindow |
| Previous Track key | Handled by PlayerWindow | `WndProc()` → no-op | Deferred to PlayerWindow |

## User Workflows

### Workflow 1: Browse and Play a Concert

**Goal:** Find a concert in the library and play it.

1. **Application starts**
   - `LibraryBrowserWindow` constructor runs (line 36)
   - Loads `LibrarySettings` from `%APPDATA%/DeadEditor/settings.json` (line 40)
   - Creates services: `MetadataService`, `NormalizationService`, `AudioPlayerService` (line 41-43)
   - Sets up audio player event: `_audioPlayer.PlaybackStopped += AudioPlayer_PlaybackStopped` (line 46)
   - Creates update timer (100ms interval) for scrubber updates (line 49-53)
   - Restores window position/size from settings (line 56)
   - **Calls `LoadShows()`** (line 62)

2. **`LoadShows()` scans library folders** (line 143)
   - Clears `_shows` and `_allShows` lists (line 145-146)
   - **If `LibraryRootPath` is set:**
     - Calls `LoadAudienceRecordings()` (line 152)
     - Scans top-level folders in library root (line 184)
     - **For "Studio Albums" folder:**
       - Scans album subfolders (line 194)
       - Parses folder name: `"Album Name (Year)"` using regex (line 209-219)
       - Reads first audio file's `Album` tag to check for `[Edition]` suffix (line 221-242)
       - Creates `LibraryShow` with `Type = AlbumType.Studio` (line 244-252)
     - **For year folders (e.g., "1977", "1990"):**
       - Scans show subfolders (line 258)
       - Parses folder name: `"yyyy-MM-dd - Venue, City, State"` (line 267-293)
       - Counts FLAC/MP3 files (line 295-297)
       - Reads first audio file's `Album` tag to detect Box Set or Official Release (line 299-338)
         - **Box Set format:** `"...: Box Set Name"` (no space before colon) (line 326-330)
         - **Official Release format:** `"... : Release Name"` (space before colon) (line 320-324)
       - Creates `LibraryShow` with `Type = AlbumType.Live` or `AlbumType.BoxSet` (line 340-353)
   - **If `OfficialReleasesPath` is set:**
     - Calls `LoadOfficialReleasesInto()` — scans Studio Albums and series folders
     - Series folder scan **skips year-named folders** (e.g. "1971", "2024") to avoid
       misidentifying audience recordings when `LibraryRootPath == OfficialReleasesPath`
     - For each release folder:
       - Counts audio files for `TrackCount` (directory listing only, NO TagLib reads)
       - Reads ONE file per album via `ReadCustomFieldsIntoShow()` for venue/city/year/albumtype/albumname tags
         - `AlbumName` priority: custom `ALBUMNAME` Xiph/TXXX field first; if empty, falls back to standard `ALBUM` tag (skipping DeadEditor-computed "yyyy-MM-dd - Venue" format values)
       - Creates `LibraryShow` with `Type = AlbumType.OfficialRelease`
       - **TrackTitles and ContainsDates are lazy-loaded** — NOT read at startup.
         They load on first access (triggered by search or By Date mode).
         This avoids the ~50s penalty of opening every audio file with TagLib at startup.
   - **Deduplication:** After both loading methods complete, shows are deduplicated by `FolderPath`.
     When the same folder appears in both scans (overlapping paths), the entry whose `Type` was
     set from the `ALBUMTYPE` FLAC/ID3v2 tag is preferred; otherwise the first entry (audience) wins.
   - If no shows found → Status: "No library paths set or no shows found..." (line 166)
   - Sorts shows: by Type first (Live/Official/Studio), then by Date desc or AlbumName desc (line 171-174)
   - Calls `ApplySearchFilter()` to display (line 177)

3. **Shows displayed in grid**
   - `_shows` list bound to `ShowsDataGrid.ItemsSource` (line 1106)
   - Each row displays adaptive columns based on `LibraryShow.Type`:
     - **Live:** Icon=🎸, PrimaryInfo=Date, SecondaryInfo=Venue, TertiaryInfo=Location, OfficialRelease=Box Set Name
     - **Official Release:** Icon=📀, PrimaryInfo=OfficialRelease, SecondaryInfo=Dates, TertiaryInfo=Venues
     - **Studio:** Icon=💿, PrimaryInfo=AlbumName, SecondaryInfo=Year, TertiaryInfo=Edition
   - Status: "X concerts in library" (line 1125)

4. **User double-clicks a concert row**
   - `ShowsDataGrid_MouseDoubleClick` fires (line 468)
   - Gets selected `LibraryShow` object
   - Calls `OpenConcertView(show)` (line 472)

5. **`OpenConcertView()` loads concert details** (line 476)
   - Saves reference to `_currentShow` (line 481)
   - **If Studio album:**
     - `VenueText` = album name (line 487)
     - `LocationText` = "Released YYYY" (line 488)
     - `DateText` = "" (line 489)
     - `BoxSetText` hidden (line 490)
   - **If Live recording:**
     - `VenueText` = venue (line 495)
     - `LocationText` = "City, State" (line 496)
     - `DateText` = date (line 497)
     - **If Box Set:** `BoxSetText` = "Box Set: [name]" (line 500-503)
   - **Loads artwork** (line 511):
     - Try `cover.jpg` in folder (line 512)
     - Try `folder.jpg` if not found (line 515)
     - Try embedded APIC tag from first audio file (line 530-557)
     - If found → displays artwork, hides placeholder (line 560-563)
     - If not found → shows placeholder 🎵 icon (line 565-568)
   - **Loads tracks:** `_metadataService.ReadFolder(show.FolderPath)` (line 572)
   - **Updates preview metadata** for each track (line 574-586):
     - **Studio albums:** `track.GetFinalMetadataTitle(null)` (no date appended) (line 580)
     - **Live recordings:** `track.GetFinalMetadataTitle(show.Date)` (may append date) (line 584)
   - Binds tracks to `TracksDataGrid.ItemsSource` (line 588)
   - Updates track count display (line 589)
   - Enables Play button if tracks exist (line 592-595)
   - **Switches views:**
     - `LibraryView.Visibility = Collapsed` (line 598)
     - `ConcertView.Visibility = Visible` (line 599)
     - `StatusText.Visibility = Collapsed` (line 600)
     - `PlayerControls.Visibility = Visible` (line 603-606)
   - Updates window title to concert date/venue (line 609)

6. **User double-clicks a track**
   - `TracksDataGrid_LoadingRow` wires up double-click handler for each row (line 637-639)
   - `TracksDataGridRow_MouseDoubleClick` fires (line 642)
   - Gets track index in `_currentTracks` list (line 646)
   - Calls `PlayTrack(index)` (line 649)

7. **`PlayTrack()` starts playback** (line 654)
   - Sets `_isManualTrackChange = true` to prevent auto-advance during track change (line 661)
   - Saves `_currentTrackIndex` (line 662)
   - `_audioPlayer.LoadFile(track.FilePath)` loads audio file into NAudio (line 666)
   - `_audioPlayer.Play()` starts playback (line 667)
   - **Updates UI:**
     - `PlayPauseButton.Content = "⏸"` (pause icon) (line 670)
     - Enables Stop button (line 672)
     - Enables/disables Previous/Next based on position (line 673-674)
     - `NowPlayingText` = "♪ [Title]" (line 676)
     - `NowPlayingDetails` = "Track X - Y:YY" (line 677)
     - Sets scrubber maximum to track duration (line 680)
     - Displays total time (line 681)
   - **Starts update timer** (100ms interval) (line 684)
     - `UpdateTimer_Tick()` updates scrubber position and current time every 100ms (line 782-789)
   - Clears `_isManualTrackChange` flag after short delay (line 687-690)

8. **Track plays to completion**
   - NAudio fires `PlaybackStopped` event
   - `AudioPlayer_PlaybackStopped` handler runs (line 755)
   - Stops update timer (line 759)
   - Changes Play button back to "▶" (line 760)
   - **Auto-advance logic** (line 762-766):
     - If NOT manual track change AND not last track → `PlayTrack(_currentTrackIndex + 1)`
     - Automatically plays next track in sequence
   - **If last track:**
     - Resets scrubber to 0 (line 770)
     - Shows "Playlist ended" (line 772)
     - Disables transport buttons (line 774-777)

**Success Path:** Library loads → Concert displayed in grid → Double-click concert → Concert view opens → Double-click track → Music plays → Auto-advances through playlist
**Special Feature:** Music continues playing when clicking "Back to Library" button - player controls stay visible, can return to library grid while listening

---

### Workflow 2: Quick Search for Concerts

**Goal:** Find concerts using text search across metadata fields.

1. **User types in Quick Search box**
   - `QuickSearchTextBox_TextChanged` fires on each keystroke (line 895)
   - **Guard against recursion:** Checks `_isUpdatingSearchBox` flag (line 897)
     - This prevents infinite loop when advanced search updates the textbox (line 995-997)
   - Saves search text: `_quickSearchText = QuickSearchTextBox.Text.Trim()` (line 899)
   - Shows/hides clear button based on text presence (line 900)
   - Calls `ApplySearchFilter()` (line 901)

2. **`ApplySearchFilter()` filters shows** (line 1013)
   - Starts with `_allShows` as base (line 1030)
   - **Applies type filter** from dropdown (line 1032-1044):
     - If "All" selected → no type filtering
     - If "Live" selected → filters to `AlbumType.Live`
     - If "OfficialRelease" selected → filters to `AlbumType.OfficialRelease`
     - If "Studio" selected → filters to `AlbumType.Studio`
   - **Applies quick search filter** (line 1047-1063):
     - Converts search text to lowercase (line 1049)
     - Searches across ALL these fields (case-insensitive contains):
       - `Date` (e.g., "1977-05-08")
       - `Venue` (e.g., "Barton Hall")
       - `City` (e.g., "Ithaca")
       - `State` (e.g., "NY")
       - `Location` (e.g., "Ithaca, NY")
       - `AlbumName` (for studio albums, e.g., "American Beauty")
       - `Edition` (for studio albums, e.g., "2025 Remaster")
       - `OfficialRelease` (e.g., "Dave's Picks Volume 38")
       - `ReleaseYear` (e.g., "1970")
       - `ContainsDates` array (for multi-date official releases)
       - `ContainsVenues` array (for multi-venue official releases)
     - **Note:** Does NOT search song titles - must use Advanced Search for that
   - **No advanced search active** → Uses quick search results only (line 1101-1104)
   - Sets filtered results to `_shows` list (line 1103)
   - Binds to grid: `ShowsDataGrid.ItemsSource = _shows` (line 1106)
   - **Updates status messages:**
     - If filtered count != total count:
       - `SearchResultTextBlock` = "Showing X of Y concerts" (line 1111)
       - If 0 results and only quick search active → helpful hint: "Quick search only searches date, venue, and location. Use Advanced Search to search by songs." (line 1113-1115)
       - Otherwise: "X concerts match search" (line 1119)
     - If no filtering active:
       - Clears result text (line 1124)
       - Status: "X concerts in library" (line 1125)

3. **User clicks clear button**
   - `ClearSearchButton_Click` fires (line 904)
   - Sets `_isUpdatingSearchBox = true` to prevent TextChanged event (line 906)
   - Clears textbox text (line 907)
   - Sets textbox to editable (line 908)
   - Clears all search criteria (line 909-912):
     - `_quickSearchText = ""`
     - `_advancedSearchSongs.Clear()`
     - `_advancedSearchExcludedSongs.Clear()`
     - `_advancedSearchSequence.Clear()`
   - Hides clear button (line 913)
   - Resets flag (line 914)
   - Calls `ApplySearchFilter()` to show all shows again (line 915)

**Success Path:** User types text → Filter applies in real-time → Grid shows matching concerts → Status shows count
**Edge Case:** Quick search returns 0 results → Status shows helpful message directing user to Advanced Search for song-based searches

---

### Workflow 3: Advanced Search - Contains Songs

**Goal:** Find concerts containing ALL selected songs (in any order).

1. **User clicks "Advanced Search" button**
   - `AdvancedSearchButton_Click` fires (line 918)
   - Creates `AdvancedSearchDialog` with services and reference to this window (line 920)
   - Sets dialog owner to this window for centering (line 921)
   - Opens dialog modally: `ShowDialog()` (line 923)

2. **User interacts with "Contains Songs" tab** (detailed in AdvancedSearchDialog doc)
   - Filters song list using search box
   - Checks boxes for desired songs (e.g., "Dark Star", "China Cat Sunflower", "I Know You Rider")
   - Dialog shows count: "3 songs selected"
   - User clicks "Search" button
   - Dialog returns `DialogResult = true`

3. **Dialog closes, returns to library window**
   - `if (dialog.ShowDialog() == true)` succeeds (line 923)
   - Extracts search criteria from dialog (line 925-928):
     - `_advancedSearchSongs = dialog.SelectedSongs` (list of song titles)
     - `_advancedSearchExcludedSongs = dialog.ExcludedSongs` (empty for this workflow)
     - `_advancedSearchSequence = dialog.SongSequence` (empty for this workflow)
   - Calls `ApplySearchFilter()` (line 930)

4. **`ApplySearchFilter()` performs song-based search** (line 1013)
   - Applies type and quick search filters first (same as Workflow 2)
   - **Advanced search active** (line 1067):
     - Checks if any advanced criteria set: `_advancedSearchSongs.Any() || ...` (line 1067)
     - **Forces immediate evaluation** to avoid LINQ lazy evaluation bug (line 1070):
       - `var filteredList = filtered.ToList()` creates snapshot
     - Creates empty result list: `var matchingShows = new List<LibraryShow>()` (line 1071)
     - **Iterates through each show** (line 1076-1084):
       - Calls `ShowMatchesSongCriteria(show)` for each concert (line 1079)
       - If match → adds to `matchingShows` list (line 1082)
     - Sets `_shows = matchingShows` (line 1086)
     - **Debug logging** to temp file for troubleshooting (line 1089-1099)

5. **`ShowMatchesSongCriteria()` checks individual concert** (line 1129)
   - **Loads tracks:** `_metadataService.ReadFolder(show.FolderPath)` (line 1134)
   - **Normalizes titles:** `_normalizationService.NormalizeAll(tracks)` (line 1137)
     - This applies fuzzy matching to get canonical song titles
     - Sets `track.NormalizedTitle` for each track
   - **Checks required songs** (line 1140-1193):
     - Gets list of normalized titles (non-empty only) (line 1143-1146)
     - **For each required song:**
       - Checks if ANY normalized title matches (case-insensitive) (line 1170-1171)
       - If NOT found → returns `false` immediately (line 1188-1190)
     - **Must have ALL required songs** to pass
   - Returns `true` only if all songs found (line 1226)
   - **Debug logging** for first show to help diagnose search issues (line 1149-1186)

6. **Results displayed**
   - Grid updates with matching concerts only
   - `SearchResultTextBlock` shows "Showing X of Y concerts" (line 1111)
   - Status shows "X concerts match search" (line 1119)
   - **Search box becomes read-only** and displays song list (line 995-997):
     - Example: "Dark Star, China Cat Sunflower, I Know You Rider"
     - If more than 3 songs: "Dark Star, China Cat Sunflower, and 2 more" (line 949-950)
   - Clear button visible to reset search (line 998)

**Success Path:** Advanced Search opens → User selects songs → Clicks Search → All concerts with those songs displayed
**Performance Note:** Loads and normalizes EVERY concert's tracks, can be slow with large libraries

---

### Workflow 4: Advanced Search - Exclude Songs

**Goal:** Find concerts that do NOT contain specific songs (NOT query).

1. **User clicks "Advanced Search" button** (same as Workflow 3 step 1)

2. **User interacts with "Exclude Songs" tab**
   - Switches to second tab
   - Checks boxes for songs to EXCLUDE (e.g., "Drums", "Space")
   - Dialog shows count: "2 songs excluded"
   - User clicks "Search"

3. **Dialog returns excluded songs list**
   - `_advancedSearchExcludedSongs = dialog.ExcludedSongs` (line 927)
   - `ApplySearchFilter()` runs (line 930)

4. **`ShowMatchesSongCriteria()` checks for excluded songs** (line 1196-1215)
   - Gets normalized titles from tracks (line 1199-1202)
   - **For each excluded song:**
     - Checks if ANY track matches (line 1207-1208)
     - If found → returns `false` immediately (line 1210-1212)
       - Concert rejected because it contains an excluded song
   - Returns `true` only if NONE of the excluded songs found (line 1226)

5. **Results displayed**
   - Grid shows only concerts WITHOUT the excluded songs
   - Search box displays: "NOT (Drums, Space)" (line 957-964)
   - **Can combine with required songs:**
     - "Dark Star NOT Drums" (both tabs used)
     - Finds concerts with Dark Star but without Drums

**Success Path:** User excludes songs → Only concerts without those songs displayed
**Use Case:** Find acoustic sets (exclude "Drums", "Space"), find early shows (exclude songs from later years)

---

### Workflow 5: Advanced Search - Song Sequence

**Goal:** Find concerts with songs played in specific order.

1. **User clicks "Advanced Search" button** (same as Workflow 3 step 1)

2. **User interacts with "Song Sequence" tab**
   - Switches to third tab
   - Selects song from dropdown (e.g., "China Cat Sunflower")
   - Clicks "Add →" button
   - Selects next song (e.g., "I Know You Rider")
   - Clicks "Add →" button
   - Sequence list shows:
     - `1. China Cat Sunflower`
     - `2. I Know You Rider`
   - User clicks "Search"

3. **Dialog returns sequence list**
   - `_advancedSearchSequence = dialog.SongSequence` (line 928)
   - `ApplySearchFilter()` runs (line 930)

4. **`ShowMatchesSongCriteria()` checks for sequence** (line 1218-1224)
   - Calls `ShowContainsSequence(tracks, _advancedSearchSequence)` (line 1220)
   - If not found → returns `false`

5. **`ShowContainsSequence()` finds consecutive songs** (line 1236)
   - Gets all normalized titles as lowercase list (line 1240)
   - **Sliding window search** (line 1243-1258):
     - For each possible starting position (line 1243)
     - Checks if next N tracks match sequence exactly (line 1245-1253)
     - If complete match found → returns `true` (line 1254-1256)
   - Returns `false` if no match (line 1260)
   - **Songs must be consecutive** - no gaps allowed

6. **Results displayed**
   - Grid shows only concerts with that song sequence
   - Search box displays: "Sequence: China Cat Sunflower > I Know You Rider" (line 982-992)

**Success Path:** User builds sequence → Only concerts with songs in that exact order displayed
**Use Case:** Find famous segues (China Cat > I Know You Rider, Scarlet > Fire, Help > Slip > Frank)

---

### Workflow 6: Edit Concert Metadata

**Goal:** Update metadata for an already-imported concert without re-importing.

1. **User opens concert in detail view** (double-click from library grid)
   - `OpenConcertView()` runs (as in Workflow 1 steps 5-6)
   - Concert details displayed
   - Saves `_currentShow` reference (line 481)

2. **User clicks "Edit Metadata" button**
   - `EditMetadataButton_Click` fires (line 851)
   - Validates `_currentShow != null` (line 853-858)
   - Creates new `MainWindow` instance (line 861)
   - Sets this window as owner (line 862)
   - **Calls `LoadFolder()` on MainWindow** passing concert's folder path (line 863)
     - This loads the concert into MainWindow's import workflow
     - All existing metadata read from files
   - Opens MainWindow as **non-modal window** using `.Show()` (not `.ShowDialog()`)
   - Registers `Closed` event handler to refresh library when window closes

3. **User edits metadata in MainWindow (non-blocking)**
   - Changes song titles, venues, dates, artwork, etc.
   - Normalizes songs, renumbers tracks
   - **Player remains fully interactive** - user can move/control player windows during editing
   - **Clicks "Write to Files"** button
     - Metadata written to audio files IN PLACE (in library folder)
     - Does NOT use "Import to Library" button (concert already in library)
   - Closes MainWindow (Cancel button or X)

4. **MainWindow closes, `Closed` event fires**
   - Automatically calls `LoadShows()` to refresh library grid
     - Re-scans all folders to pick up metadata changes
     - Updates box set names, venue names, etc. in grid
   - **Reloads concert view:** `OpenConcertView(_currentShow)` (line 870)
     - Re-reads tracks from updated files
     - Refreshes track list with new metadata
     - Shows updated artwork if changed

5. **Updated metadata displayed**
   - Grid shows new concert info (if venue/date changed)
   - Concert view shows updated tracks
   - Playback continues uninterrupted (if music was playing)

**Success Path:** Open concert → Edit Metadata → Make changes → Write to Files → Close → Library refreshed with updates
**Key Design:** Editing does NOT re-import, just updates files in place within library folder structure

---

### Workflow 7: Navigate from Track Search Result

**Goal:** Advanced Search "Search Tracks" tab finds track by date/venue, navigate to containing album.

1. **User performs track search in AdvancedSearchDialog**
   - Enters date (e.g., "1972-05-04") or venue (e.g., "Bickershaw Festival")
   - Clicks "Search Tracks" button
   - Dialog searches all albums (live recordings, studio albums, official releases)
   - Finds tracks with embedded dates/venues (e.g., "Morning Dew (1972-05-04)" in studio album bonus tracks)
   - Shows results in DataGrid with columns: Track, Album, Type

2. **User double-clicks a track result**
   - Dialog calls `_libraryBrowserWindow?.NavigateToAlbumByPath(result.AlbumPath)` (line 447 in AdvancedSearchDialog.xaml.cs)
   - Dialog closes (line 443)

3. **`NavigateToAlbumByPath()` opens album** (line 1264)
   - Searches `_allShows` for matching folder path (line 1267-1268)
   - **If found in current list:**
     - Calls `OpenConcertView(matchingShow)` (line 1273)
   - **If NOT found (album filtered out):**
     - Reloads entire library: `LoadShows()` (line 1278)
     - Searches again in refreshed `_allShows` (line 1279-1280)
     - Opens concert view (line 1284)
   - Library view switches to concert detail view
   - User can now see full album with that track highlighted

**Success Path:** Track search → Double-click result → Dialog closes → Album opens in concert view
**Smart Refresh:** If album not visible due to filters, library reloads to find it

---

## Data Flow

### Inputs

**Configuration Files:**
- `%APPDATA%/DeadEditor/settings.json` - Library settings loaded at startup
  - `LibraryRootPath` - Path to audience recordings library
  - `OfficialReleasesPath` - Path to official releases library
  - `LibraryWindowLeft`, `LibraryWindowTop`, `LibraryWindowWidth`, `LibraryWindowHeight` - Window position

**Library Folders:**
- Scans recursively for concert folders based on structure:
  - **Audience recordings:** `[LibraryRoot]/[Year]/[yyyy-MM-dd - Venue, City, State]/`
  - **Studio albums:** `[LibraryRoot]/Studio Albums/[Album Name (Year)]/`
  - **Official releases:** `[OfficialReleasesPath]/[Series]/[Release Name]/`
- Audio files (FLAC/MP3) in each folder
- Artwork files (`cover.jpg`, `folder.jpg`)
- Embedded artwork in audio file APIC tags

**Audio File Metadata (ID3 Tags):**
- `Album` tag - Used to detect Box Set vs Official Release format:
  - Box Set: `"yyyy-MM-dd - Venue, City, State: Box Set Name"` (no space before `:`)
  - Official Release: `"yyyy-MM-dd - Venue, City, State : Release Name"` (space before `:`)
- `Album` tag for studio albums - May contain `[Edition]` suffix
- `Title` tag - Used to extract dates/venues from official release tracks
- `Pictures[0]` - Embedded artwork (fallback if no file)

**User Input:**
- Type filter selection (All/Live/Official/Studio)
- Quick search text
- Advanced search criteria (from AdvancedSearchDialog)
- Double-clicks on grid rows and track rows
- Transport button clicks
- Scrubber/volume slider interactions
- Media key presses (keyboard)

**Services:**
- `MetadataService.ReadFolder()` - Returns `List<TrackInfo>` for each concert
- `NormalizationService.NormalizeAll()` - Fuzzy-matches song titles
- `AudioPlayerService` - NAudio wrapper for playback

### Outputs

**In-Memory State:**
- `_allShows` - Complete list of all concerts (unfiltered)
- `_shows` - Filtered list bound to DataGrid
- `_currentTracks` - Tracks in currently open concert
- `_currentShow` - Currently viewed concert
- `_currentTrackIndex` - Currently playing track position
- Search state:
  - `_quickSearchText`
  - `_advancedSearchSongs`
  - `_advancedSearchExcludedSongs`
  - `_advancedSearchSequence`

**Configuration Updates:**
- `settings.json` updated on window close with window position/size (line 80-88)

**No File Modifications:**
- LibraryBrowserWindow is read-only - does NOT write to audio files
- Metadata editing happens in MainWindow (opened via Edit Metadata button)

**Audio Output:**
- Plays audio through `AudioPlayerService` → NAudio → system audio device
- Updates every 100ms via `_updateTimer`

**Debug Logging:**
- `deadedit_search_debug.txt` in temp folder (for advanced search debugging) (line 1089-1186)

### Services Used

**MetadataService (`_metadataService`):**
- `ReadFolder(string folderPath)` → List<TrackInfo>
  - Scans folder for FLAC/MP3 files
  - Reads ID3 tags from each file
  - Returns list of TrackInfo objects with metadata

**NormalizationService (`_normalizationService`):**
- `NormalizeAll(List<TrackInfo> tracks)` → int
  - Fuzzy-matches all track titles against song database
  - Sets `track.NormalizedTitle` for matched songs
  - Returns count of matched songs
  - Used during advanced search song matching

**AudioPlayerService (`_audioPlayer`):**
- `LoadFile(string filePath)` → void
  - Loads audio file into NAudio
- `Play()` → void
  - Starts/resumes playback
- `Pause()` → void
  - Pauses playback
- `Stop()` → void
  - Stops playback, resets position
- `Seek(TimeSpan position)` → void
  - Seeks to specific position
- `Volume` property (float 0.0-1.0)
  - Sets playback volume
- `TotalDuration` property (TimeSpan)
  - Gets total track duration
- `CurrentPosition` property (TimeSpan)
  - Gets current playback position
- `PlaybackStopped` event
  - Fires when track finishes or is stopped

**LibrarySettings:**
- `Load()` → LibrarySettings (static)
  - Loads from `%APPDATA%/DeadEditor/settings.json`
- `Save()` → void
  - Persists to disk

---

## Business Rules

### Library Structure Requirements

**Folder Naming Conventions:**

1. **Audience Recordings:**
   - Root: `[LibraryRootPath]/[Year]/`
   - Folder: `yyyy-MM-dd - Venue, City, State`
   - Alternative: `yyyy-MM-dd - Venue - City, State` (both supported)
   - Year extracted from date (e.g., "1977-05-08" goes in `1977/` folder)

2. **Studio Albums:**
   - Root: `[LibraryRootPath]/Studio Albums/`
   - Folder: `Album Name (Year)` or just `Album Name`
   - Regex pattern: `^(.+?)\s*\((\d{4})\)\s*$` (line 209-210)
   - Edition stored in ID3 `Album` tag with `[Edition]` suffix (line 230-234)

3. **Official Releases:**
   - Root: `[OfficialReleasesPath]/[Series]/`
   - Series folders: Dave's Picks, Dick's Picks, Road Trips, Download Series, etc.
   - Folder name can be anything (parsed from ID3 tags, not folder name)

### Album Type Detection

**Box Set vs Official Release (Colon Format):**

- **Box Set:** Album tag format `"...: Box Set Name"` (no space before colon) (line 326-330)
  - Example: `"1977-05-08 - Barton Hall, Ithaca, NY: Enjoying the Ride"`
  - Creates `AlbumType.BoxSet`
  - `OfficialRelease` property stores box set name
- **Official Release:** Album tag format `"... : Release Name"` (space before colon) (line 320-324)
  - Example: `"1977-05-08 - Barton Hall, Ithaca, NY : Cornell '77"`
  - Creates `AlbumType.Live` (not OfficialRelease type)
  - `OfficialRelease` property stores release name
- **Live Recording:** No colon in Album tag
  - Creates `AlbumType.Live`
  - `OfficialRelease` property empty

**Regex Patterns Used:**

- **Box Set detection:** `@":\s*([^:]+)$"` - Colon followed by optional whitespace and text to end (line 313-314)
- **Official Release detection:** `@"\s:\s*(.+)$"` - Space, colon, optional whitespace, text to end (line 317-318)
- **Official release name:** `@"((?:Dave's Picks|Dick's Picks|Road Trips|Download Series)\s*,?\s*Vol(?:ume|\.)?\s+\d+(?:\s+No\.\s+\d+)?)"` (line 397-400)
  - Matches: "Dave's Picks Volume 38", "Road Trips, Vol. 3 No. 4", "Dick's Picks Vol. 12"
- **Date extraction:** `@"(\d{4}-\d{2}-\d{2})"` (line 408-409)
- **Venue extraction (Live at):** `@"Live (?:at|in) ([^,]+),"` (line 419)
- **Venue extraction (Filler):** `@"Filler:\s*\d{4}-\d{2}-\d{2}\s*-\s*([^,]+),"` (line 427-428)
- **Edition extraction:** `@"\[([^\]]+)\]\s*$"` - Finds `[Edition]` at end of Album tag (line 230-231)

### Grid Sorting Rules

**Default Sort Order:** (line 171-174)
1. Primary: By `Type` (Live, OfficialRelease, Studio)
2. Secondary:
   - **Live/OfficialRelease:** By `Date` descending (newest first)
   - **Studio:** By `AlbumName` descending (reverse alphabetical)

**No User Sorting:** DataGrid columns are not sortable (no click-to-sort headers)

### Search Behavior

**Quick Search Fields (Exact Substring Match):**
- Searches across: Date, Venue, City, State, Location, AlbumName, Edition, OfficialRelease, ReleaseYear
- For multi-date/multi-venue official releases: searches within `ContainsDates[]` and `ContainsVenues[]` arrays
- Case-insensitive contains match (line 1047-1063)
- **Does NOT search:** Song titles (must use Advanced Search)
- Real-time filtering as user types (no "Search" button needed)

**Advanced Search Song Matching:**
- **Requires ALL songs** (AND logic) - concert must have every selected song (line 1167-1192)
- **Excludes ANY song** (OR logic) - concert rejected if it has ANY excluded song (line 1196-1215)
- **Sequence must be consecutive** - no gaps allowed between songs (line 1236-1260)
- Uses normalized titles (canonical song names) via fuzzy matching
- Loads and normalizes EVERY concert's tracks (performance cost)

**Search Combination Rules:**
- Type filter + Quick search + Advanced search all combine (AND logic across categories)
- Within Advanced Search:
  - Required songs (Contains) AND
  - Excluded songs (Exclude) AND
  - Sequence match
- Example: "1977" (quick) + "Dark Star" (contains) + NOT "Drums" (exclude) = 1977 concerts with Dark Star but no Drums

### Preview Metadata Rules

**Track Title Display:**
- **Studio albums:** Clean song titles, NO date appended (line 578-580)
  - Example: "Morning Dew"
- **Live recordings:** May append performance date if different from album date (line 582-584)
  - Calls `track.GetFinalMetadataTitle(show.Date)`
  - Example: "Morning Dew (1972-05-04)" in bonus live tracks

### Playback Rules

**Auto-Advance:**
- When track finishes naturally → auto-plays next track (line 763-765)
- `_isManualTrackChange` flag prevents auto-advance when user manually changes tracks (line 661, 689, 762)
- Last track finishes → shows "Playlist ended", disables transport (line 767-777)

**Button State Logic:**
- Play/Pause button uses **content as source of truth** (line 704)
  - "⏸" (pause icon) = currently playing
  - "▶" (play icon) = paused or stopped
  - Prevents double-click bug from checking `_audioPlayer.IsPlaying` directly
- Previous button enabled only if `_currentTrackIndex > 0` (line 673)
- Next button enabled only if `_currentTrackIndex < _currentTracks.Count - 1` (line 674)
- Stop button disables itself after stopping (line 735)

**Scrubber Behavior:**
- `_isScrubbing` flag prevents feedback loop (line 794, 799, 805)
- While scrubbing: shows preview time, doesn't update from timer (line 784-788)
- On release: seeks to scrubber position (line 800)
- Updates every 100ms when not scrubbing (line 782-789)

### View Switching Rules

**Back Button Behavior:**
- Returns to library grid view (line 618-635)
- **Does NOT stop playback** - music keeps playing (line 620-621)
- **Does NOT hide player controls** - stays visible at bottom (line 631)
- User can browse library while listening to concert
- Clicking another concert while playing → stops current track, loads new concert

**Window Title Updates:**
- Library view: "DeadEditor - Library" (line 634)
- Concert view: "yyyy-MM-dd - Venue" (line 609)

### Media Key Integration

**Windows Message Hooking:**
- `OnSourceInitialized()` hooks into Windows message pump (line 91-100)
- `WndProc()` intercepts `WM_APPCOMMAND` messages (line 110-141)
- Maps to transport button clicks:
  - `APPCOMMAND_MEDIA_PLAY_PAUSE` → `PlayPauseButton_Click()`
  - `APPCOMMAND_MEDIA_STOP` → `StopButton_Click()`
  - `APPCOMMAND_MEDIA_NEXTTRACK` → `NextButton_Click()`
  - `APPCOMMAND_MEDIA_PREVIOUSTRACK` → `PreviousButton_Click()`
- Works with physical keyboard media keys and wireless headset buttons

---

## Known Issues

### 1. Lazy Evaluation Bug (FIXED)
**Location:** `ApplySearchFilter()` line 1070
**Issue:** LINQ `.Where()` caused inconsistent search results due to lazy evaluation
**Fix:** Force immediate evaluation with `.ToList()` before song matching loop
**Status:** Working (fixed in session 2026-01-08)

### 2. TextChanged Recursion Bug (FIXED)
**Location:** `QuickSearchTextBox_TextChanged` line 897
**Issue:** Advanced search sets textbox text, triggering TextChanged, causing infinite loop
**Fix:** `_isUpdatingSearchBox` flag prevents recursive calls
**Status:** Working (fixed in session 2026-01-08)

### 3. Play Button Double-Click Bug (FIXED)
**Location:** `PlayPauseButton_Click` line 704
**Issue:** Checking `_audioPlayer.IsPlaying` created race condition, button required double-click
**Fix:** Use button content ("▶"/"⏸") as source of truth instead of player state
**Status:** Working (noted in code comments)

### 4. Advanced Search Performance
**Location:** `ShowMatchesSongCriteria()` line 1129
**Issue:** Loads and normalizes EVERY concert's tracks, can be slow with 500+ concerts
**Severity:** Not a bug, inherent to feature design
**Workaround:** Use type filter or quick search first to reduce concert count before advanced search
**Future Enhancement:** Could cache normalized track lists, but adds complexity

### 5. Search Debug Logging
**Location:** `ShowMatchesSongCriteria()` line 1149-1186
**Issue:** Extensive debug logging to temp file still exists in production code
**Purpose:** Helps diagnose search issues
**Impact:** Creates `deadedit_search_debug.txt` file in temp folder
**Recommendation:** Consider removing or disabling in production builds

---

## Edge Cases

### What happens if library paths not configured?
**Scenario:** User starts app for first time, no paths set in settings
**Behavior:**
1. `LoadShows()` runs (line 143)
2. Checks `LibraryRootPath` and `OfficialReleasesPath` (line 149-160)
3. Both empty or non-existent directories
4. `_allShows` list stays empty (line 163)
5. Status: "No library paths set or no shows found. Go to File > Settings to configure." (line 166)
6. Grid shows empty
7. User can click File > Settings to configure paths

**Result:** Safe startup with helpful message directing user to settings

---

### What happens if a concert folder has no audio files?
**Scenario:** Folder exists in library structure but contains only text files, images, etc.
**Behavior:**
1. `LoadAudienceRecordings()` or `LoadOfficialReleases()` scans folder (line 295-297 or 373-377)
2. `Directory.GetFiles(folder, "*.flac").Concat(Directory.GetFiles(folder, "*.mp3"))` returns empty array
3. **For audience recordings:** `TrackCount = 0`, show still added to grid (line 295-353)
4. **For official releases:** `if (audioFiles.Length == 0) continue` skips folder entirely (line 377)
5. Empty concert appears in grid with "0 tracks"
6. Double-clicking empty concert:
   - Loads folder, reads 0 tracks (line 572)
   - Track list empty
   - Play button disabled (line 592-595)
   - No crash

**Result:** Audience recordings with 0 tracks shown but unplayable; official releases with 0 tracks skipped

---

### What happens if audio file ID3 tags are completely empty?
**Scenario:** Concert folder has FLAC files with no metadata tags
**Behavior:**
1. `LoadAudienceRecordings()` reads files (line 199-201)
2. `TagLib.File.Create()` reads tag (line 226)
3. `Album` tag is null or empty (line 228)
4. Regex matches fail (line 313-331)
5. Concert created with:
   - `AlbumType.Live` (default) (line 302)
   - `OfficialRelease = ""` (empty) (line 300)
   - No box set detection
6. Concert appears in grid with folder-parsed date/venue
7. Opening concert:
   - `_metadataService.ReadFolder()` reads files, may have blank titles
   - Track list shows tracks with empty or filename-based titles
   - Playback still works (NAudio doesn't require tags)

**Result:** Concert loads with minimal metadata, playback functional

---

### What happens if folder name doesn't match expected format?
**Scenario:** Folder named "Random Concert Files" instead of "yyyy-MM-dd - Venue, City, State"
**Behavior:**
1. `LoadAudienceRecordings()` parses folder name (line 267)
2. `Split(" - ")` returns single element array (no " - " separator)
3. `parts.Length >= 2` check fails (line 274)
4. All metadata fields stay empty: `date = ""`, `venue = ""`, etc. (line 269-272)
5. Concert added with empty Date/Venue/City/State (line 340-353)
6. Grid shows concert with:
   - PrimaryInfo = "" (empty date)
   - SecondaryInfo = "" (empty venue)
   - TertiaryInfo = "" (empty location)
7. Concert still playable, just no metadata

**Result:** Concert appears with blank metadata but functional playback

---

### What happens if user performs advanced search with 0 songs selected?
**Scenario:** User opens Advanced Search dialog, doesn't select any songs, clicks "Search"
**Behavior:**
1. Dialog validates search criteria (line 291-296 in AdvancedSearchDialog.xaml.cs)
2. `if (!SelectedSongs.Any() && !ExcludedSongs.Any() && !SongSequence.Any())` triggers
3. Shows warning: "Please select some songs, excluded songs, or create a sequence to search for."
4. Dialog stays open, does not return results
5. User must select at least one criterion or click Cancel

**Result:** Validation prevents empty search, forces user to select criteria

---

### What happens if advanced search finds 0 concerts?
**Scenario:** User searches for "Dark Star" AND "Terrapin Station" (songs rarely played together)
**Behavior:**
1. `ApplySearchFilter()` loops through all concerts (line 1076-1084)
2. No concerts match criteria
3. `_shows = matchingShows` with empty list (line 1086)
4. Grid empties (line 1106)
5. `SearchResultTextBlock` shows "Showing 0 of X concerts" (line 1111)
6. Status shows "0 concerts match search" (line 1119)
7. Clear button visible to reset search (line 998)

**Result:** Empty grid with clear message and ability to reset search

---

### What happens if user clicks Play with no concert open?
**Scenario:** Application starts, library view showing, user clicks Play button (via media key)
**Behavior:**
1. Media key triggers `PlayPauseButton_Click()` (line 119 in WndProc)
2. Handler checks `_currentTracks.Count > 0` (line 720)
3. No tracks loaded → condition fails
4. **No action taken** (button is disabled, click does nothing)
5. Play button remains disabled (`IsEnabled=False` initially, line 406)

**Result:** Safe no-op, button disabled until concert opened

---

### What happens if audio file gets corrupted or deleted while playing?
**Scenario:** User deletes FLAC file from disk while that track is playing
**Behavior:**
1. NAudio has file handle open, deletion may fail on Windows (file in use)
2. If deletion succeeds (file on network drive, etc.):
   - NAudio playback continues from buffered data
   - When buffer exhausted → throws exception
3. Exception caught in `PlayTrack()` try-catch (line 692-697)
4. Shows MessageBox: "Error playing track: [exception message]"
5. Playback stops
6. `_isManualTrackChange` reset (line 696)
7. User can try other tracks or close concert

**Result:** Error message shown, playback stops gracefully

---

### What happens if user has duplicate folder names (same date, same venue)?
**Scenario:** Library has two folders: `1977/1977-05-08 - Barton Hall, Ithaca, NY` (different tapers)
**Behavior:**
1. `LoadAudienceRecordings()` scans both folders (line 258-356)
2. Both parsed with identical metadata: same Date, Venue, City, State
3. Both folders read Album tag from first audio file
4. **If Album tags DIFFER:** Both added to `_allShows` as separate shows
5. **If Album tags MATCH:** `GroupMultiFolderAlbums()` merges them into ONE LibraryShow (line 532-603)
   - FolderPaths contains both folder paths
   - TrackCount = sum of both folders' track counts
   - OpenConcertView loads tracks from BOTH folders
6. Grid shows ONE row with combined track count
7. Double-clicking opens concert view with ALL tracks from ALL folders

**Result:** Folders with matching Album tags are grouped; folders without Album tags or with different Album tags remain separate

---

### What happens if user imports multiple folders with the same Album Name?
**Scenario:** User imports 3 concerts (1971-04-25, 1971-04-26, 1971-04-27), all with Album tag = "Enjoying the Ride"
**Behavior:**
1. All 3 folders imported to library (e.g., `1971/1971-04-25 - Fillmore East...`, `1971/1971-04-26 - Fillmore East...`, `1971/1971-04-27 - Fillmore East...`)
2. `LoadAudienceRecordings()` creates 3 LibraryShow objects initially
3. `GroupMultiFolderAlbums()` detects matching Album tag "Enjoying the Ride" (line 540-542)
4. **Groups within same AlbumType** - only groups if Type matches (line 541)
5. Merges 3 shows into ONE LibraryShow (line 555-593):
   - `FolderPaths` = list of 3 folder paths
   - `TrackCount` = sum of all tracks (e.g., 28 + 33 + 25 = 86)
   - `ContainsDates` = `["1971-04-25", "1971-04-26", "1971-04-27"]`
   - `ContainsVenues` = `["Fillmore East"]` (deduplicated)
6. Grid shows **ONE row** with Album Name "Enjoying the Ride" and track count 86
7. User double-clicks row → `OpenConcertView()` loads tracks from ALL 3 folders (line 753-779)
8. **Multi-night sort detection** (line 980-1005):
   - Counts distinct TrackDate values across all tracks
   - Checks if ANY track has DiscNumber > 0 (disc-aware numbering vs sequential numbering)
   - **If 2+ distinct dates found:**
     - **With disc numbers:** Sort by TrackDate (ascending), then DiscNumber, then TrackNumber
     - **No disc numbers:** Sort by TrackDate (ascending), then TrackNumber only
   - **If 0-1 dates:**
     - **With disc numbers:** Sort by DiscNumber, then TrackNumber
     - **No disc numbers:** Sort by TrackNumber only (globally sequential: 1, 2, 3... 29)
9. All 86 tracks displayed grouped by date, then sorted within each date section

**Result:** Multi-folder, multi-night albums appear as ONE entry with tracks sorted chronologically by date first, preventing interleaving of different concert nights

---

### What happens if user changes library path in Settings while concert is playing?
**Scenario:** User listens to concert, opens Settings, changes LibraryRootPath, closes Settings
**Behavior:**
1. Settings dialog opens (line 836)
2. User changes path in SettingsWindow
3. Settings saved to disk (line 87 in SettingsWindow.xaml.cs)
4. Dialog closes, returns to library window (line 843)
5. **`LoadShows()` called** (line 843)
   - Re-scans library with new path
   - May not find currently playing concert (old path)
   - `_allShows` repopulated from new path
   - Grid refreshes
6. **Audio player NOT affected:**
   - `_currentTracks` still has old file paths
   - Playback continues from old location
   - `_currentShow` still references old folder path
7. **If user clicks Back then tries to re-open concert:**
   - Concert not in new library → can't be found
   - User must use old path or move concert to new library

**Result:** Playback continues but concert may not be in library anymore (orphaned state)

---

### What happens if official release has tracks from 10+ different dates?
**Scenario:** Boxed set with tracks from 20 different concerts
**Behavior:**
1. `LoadOfficialReleases()` reads all audio files (line 384-440)
2. Extracts dates from `Title` tags using regex (line 408-412)
3. All unique dates added to `ContainsDates` list (line 412)
4. All unique venues added to `ContainsVenues` list (line 422)
5. Grid display:
   - SecondaryInfo = `string.Join(", ", ContainsDates)` (line 1327)
   - TertiaryInfo = `string.Join(", ", ContainsVenues)` (line 1332)
   - Column shows: "1972-05-04, 1972-05-07, 1972-05-11, ..." (all dates comma-separated)
6. **Grid column may truncate** if too many dates (UI clipping)
7. Quick search can find by ANY date in the list (line 1061)

**Result:** All dates/venues shown (comma-separated), searchable individually

---

### What happens if user scrubs to a position beyond track duration?
**Scenario:** User drags scrubber past end while track is loading
**Behavior:**
1. `AudioScrubber_PreviewMouseUp` fires (line 797)
2. Calls `_audioPlayer.Seek(TimeSpan.FromSeconds(AudioScrubber.Value))` (line 800)
3. If value > total duration → NAudio seeks to end
4. Track stops immediately (already at end)
5. `PlaybackStopped` event fires
6. Auto-advance to next track (line 763-765)

**Result:** Seeking past end acts like track finished, auto-advances

---

### What happens if Box Set name contains a colon?
**Scenario:** Box set named "Dave's Picks: The Best Of Volume 1"
**Behavior:**
1. Album tag: `"1977-05-08 - Barton Hall: Dave's Picks: The Best Of Volume 1"` (multiple colons)
2. Box set regex: `@":\s*([^:]+)$"` matches text after LAST colon (line 313-314)
3. Captures: "The Best Of Volume 1" (everything after last colon)
4. `OfficialRelease` property = "The Best Of Volume 1" (partial name)
5. Grid shows partial box set name

**Issue:** Regex only captures after last colon, truncates box set name with embedded colons
**Workaround:** Avoid colons in box set names, or use different separator

**Recommendation:** Update regex to be greedy: `@":\s*(.+)$"` (captures everything after first colon, including subsequent colons)

---

### What happens if user opens concert, edits metadata changing album type, then returns to library?
**Scenario:** User opens Live concert, edits to Studio album, writes metadata, closes
**Behavior:**
1. Edit Metadata button clicked (line 851)
2. MainWindow opens with concert folder (line 863)
3. User changes album type radio button to Studio Album
4. Writes metadata (updates ID3 tags)
5. Closes MainWindow (line 864)
6. **LibraryBrowserWindow reloads:** `LoadShows()` (line 867)
7. Re-scans folder with updated tags
8. Folder still in `[Year]/[Date]` structure (not moved to Studio Albums folder)
9. **Album type in `_allShows` may not change:**
   - `LoadAudienceRecordings()` detects by folder structure, not tags
   - Folder in year folder → still parses as Live recording
10. Grid still shows as Live recording
11. **Re-opens concert view:** `OpenConcertView(_currentShow)` (line 870)
12. Reads updated tags
13. Display logic uses `show.Type` which is still Live
14. Concert displays as Live, not Studio

**Issue:** Album type determined by folder structure at scan time, changing tags doesn't move folders
**Workaround:** User must manually move folder to correct library structure
**Future Enhancement:** Could detect type from tags and update display dynamically

---

## Summary

LibraryBrowserWindow is a comprehensive library management and playback application with sophisticated search capabilities and seamless view transitions. It automatically discovers concerts from configured library paths, provides adaptive grid columns based on album type, and integrates fuzzy-matched song searching with real-time filtering. The audio player supports auto-advance, media key control, and continues playing when switching between library and concert views. Advanced search can find concerts by required songs (AND), excluded songs (NOT), or specific song sequences, with performance tradeoffs for large libraries.

**Key Strengths:**
- Automatic library discovery with three album type support
- Adaptive grid columns show relevant metadata per album type
- Non-destructive quick search with real-time filtering
- Powerful advanced song-based searches (AND/NOT/sequence)
- Seamless playback during view transitions
- Media key integration for keyboard control
- Smart box set vs official release detection via colon format
- Multi-date/multi-venue support for official releases
- Embedded artwork extraction as fallback
- Edit metadata without leaving library browser

**Architecture Highlights:**
- Two-view design (Library Grid / Concert Detail) with visibility toggling
- Comprehensive filtering pipeline (Type → Quick Search → Advanced Search)
- Explicit list evaluation to avoid LINQ lazy evaluation bugs
- Button content as state indicator (Play/Pause icon prevents race conditions)
- Flag-based scrubbing and track change detection
- Windows message hooking for media key support
- Service-based architecture (Metadata, Normalization, AudioPlayer)

**Performance Considerations:**
- Advanced search loads ALL concerts' tracks (100+ ms per concert with normalization)
- Recommend type filter + quick search to reduce scan count before advanced search
- No caching of normalized titles (re-normalized on every search)

**Known Limitations:**
- Album type determined by folder structure, not tags (changing tags doesn't retype album)
- Box set names with colons may truncate (regex captures after last colon)
- Debug logging to temp file still in production code
- No progress indicator during advanced search (appears frozen on large libraries)

**Future Enhancements:**
- Cache normalized track lists per concert (performance)
- Progress bar for advanced search
- Fix box set name colon regex
- Remove or disable debug logging in production
- Support album type changes without folder moves
