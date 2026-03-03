# Feature Documentation: Advanced Search Dialog

## Purpose

The **Advanced Search Dialog** is a multi-tab search interface that enables sophisticated concert and track discovery across the entire library. It provides four distinct search modes: **Contains Songs** (find concerts with ALL selected songs in any order), **Exclude Songs** (find concerts WITHOUT specific songs), **Song Sequence** (find concerts with songs in a specific consecutive order), and **Search Tracks** (find individual tracks by date or venue across all album types). Each search type includes fuzzy matching, real-time filtering, and integration with the Library Browser for seamless navigation.

---

## Screen Layout

### Visual Structure
A centered modal dialog (700x550px, resizable with 650x500 minimum) with:
- **Header:** "Advanced Concert Search" title (18pt bold)
- **Tab Control:** Four tabs taking up majority of space
  - Contains Songs tab (checkboxes with filter)
  - Exclude Songs tab (checkboxes with filter)
  - Song Sequence tab (combo box + ordered list)
  - Search Tracks tab (text fields + results grid)
- **Footer:** Search/Cancel buttons (right-aligned)

### Tab 1: Contains Songs
- **Description Text:** "Find concerts containing ALL of the selected songs (in any order)"
- **Filter Box:** Text field with clear button (✕)
- **Checkbox List:** Scrollable panel with 600+ song checkboxes
- **Selection Tools:** Select All, Clear All buttons
- **Counter:** "{N} song(s) selected" (italic gray text)

### Tab 2: Exclude Songs
- **Description Text:** "Find concerts that DO NOT contain any of the selected songs" (red bold "DO NOT")
- **Filter Box:** Separate filter for exclude list
- **Checkbox List:** Independent scrollable panel with all songs
- **Selection Tools:** Separate Select All/Clear All for exclude
- **Counter:** "{N} song(s) excluded"

### Tab 3: Song Sequence
- **Description Text:** "Find concerts with songs in a specific order (e.g., China Cat > I Know You Rider)"
- **Song Selector:** ComboBox with all songs + "Add →" button
- **Sequence List:** ListBox showing numbered sequence with remove (✕) buttons
- **Empty State:** "No sequence defined. Add songs above to build a sequence."
- **Clear Button:** "Clear Sequence" button below list

### Tab 4: Search Tracks
- **Description Text:** "Search for individual tracks by date or venue (finds live tracks in studio albums, official releases, and live recordings)"
- **Date Field:** TextBox for yyyy-MM-dd format
- **Venue Field:** TextBox for venue name
- **Search Button:** Blue "Search Tracks" button (font-weight: SemiBold)
- **Results Grid:** Three columns (Track, Album, Type)
- **Empty State:** "No results. Enter a date or venue and click Search Tracks."

### Footer Buttons
- **Search:** Blue background (#094771), white text, default button (Enter key)
- **Cancel:** Standard styling, cancel button (Escape key)

---

## Interactive Elements

### Tab Control and Navigation

| Element | Type | Action | Event Handler | Status |
|---------|------|--------|---------------|--------|
| Contains Songs tab | TabItem | Switch to contains mode | (Built-in TabControl) | WORKING |
| Exclude Songs tab | TabItem | Switch to exclude mode | (Built-in TabControl) | WORKING |
| Song Sequence tab | TabItem | Switch to sequence mode | (Built-in TabControl) | WORKING |
| Search Tracks tab | TabItem | Switch to track search mode | (Built-in TabControl) | WORKING |

### Contains Songs Tab Elements

| Element | Type | Action | Event Handler | API/Service Call | Status |
|---------|------|--------|---------------|------------------|--------|
| SongFilterTextBox | TextBox | Filter song checklist (real-time) | `SongFilterTextBox_TextChanged` | None (UI filter) | WORKING |
| ClearFilterButton | Button (✕) | Clear filter text | `ClearFilterButton_Click` | None | WORKING |
| SongCheckListPanel | StackPanel | Display filtered checkboxes | N/A | `_normalizationService.GetAllTitles()` | WORKING |
| (Dynamic CheckBoxes) | CheckBox | Toggle song selection | `SongCheckBox_Changed` | None | WORKING |
| SelectAllButton | Button | Check all visible songs | `SelectAllButton_Click` | None (UI operation) | WORKING |
| ClearAllButton | Button | Uncheck all songs (including hidden) | `ClearAllButton_Click` | None | WORKING |
| SelectedSongsCount | TextBlock | Show "{N} song(s) selected" | Updated by `UpdateSelectedSongsCount()` | None | WORKING |

### Exclude Songs Tab Elements

| Element | Type | Action | Event Handler | API/Service Call | Status |
|---------|------|--------|---------------|------------------|--------|
| ExcludeSongFilterTextBox | TextBox | Filter exclude checklist | `ExcludeSongFilterTextBox_TextChanged` | None (UI filter) | WORKING |
| ClearExcludeFilterButton | Button (✕) | Clear exclude filter | `ClearExcludeFilterButton_Click` | None | WORKING |
| ExcludeSongCheckListPanel | StackPanel | Display filtered exclude checkboxes | N/A | `_normalizationService.GetAllTitles()` | WORKING |
| (Dynamic CheckBoxes) | CheckBox | Toggle song exclusion | `ExcludeSongCheckBox_Changed` | None | WORKING |
| ExcludeSelectAllButton | Button | Check all visible exclude songs | `ExcludeSelectAllButton_Click` | None | WORKING |
| ExcludeClearAllButton | Button | Uncheck all exclude songs | `ExcludeClearAllButton_Click` | None | WORKING |
| ExcludedSongsCount | TextBlock | Show "{N} song(s) excluded" | Updated by `UpdateExcludedSongsCount()` | None | WORKING |

### Song Sequence Tab Elements

| Element | Type | Action | Event Handler | API/Service Call | Status |
|---------|------|--------|---------------|------------------|--------|
| SequenceSongComboBox | ComboBox | Select song to add | None (selection only) | `_normalizationService.GetAllTitles()` | WORKING |
| AddToSequenceButton | Button | Add selected song to sequence | `AddToSequenceButton_Click` | None | WORKING |
| SequenceListBox | ListBox | Display ordered sequence | N/A (ItemsSource binding) | None | WORKING |
| (Dynamic Remove Buttons) | Button (✕) | Remove song from sequence | `RemoveFromSequence_Click` | None | WORKING |
| ClearSequenceButton | Button | Clear entire sequence | `ClearSequenceButton_Click` | None | WORKING |
| SequenceEmptyText | TextBlock | Show empty state message | Visibility controlled by `RefreshSequenceList()` | None | WORKING |

### Search Tracks Tab Elements

| Element | Type | Action | Event Handler | API/Service Call | Status |
|---------|------|--------|---------------|------------------|--------|
| TrackDateTextBox | TextBox | Enter date (yyyy-MM-dd) | None (input only) | None | WORKING |
| TrackVenueTextBox | TextBox | Enter venue name | None (input only) | None | WORKING |
| TrackSearchButton | Button | Execute track search | `TrackSearchButton_Click` | `SearchTracksInLibrary()` | WORKING |
| TrackResultsDataGrid | DataGrid | Display matching tracks | N/A (ItemsSource binding) | `_metadataService.ReadFolder()` | WORKING |
| TrackResultsDataGrid | DataGrid | Double-click to navigate | `TrackResultsDataGrid_MouseDoubleClick` | `_libraryBrowserWindow?.NavigateToAlbumByPath()` | WORKING |
| TrackResultsEmptyText | TextBlock | Show "No results" message | Visibility controlled by search results | None | WORKING |

### Footer Buttons

| Element | Type | Action | Event Handler | API/Service Call | Status |
|---------|------|--------|---------------|------------------|--------|
| SearchButton | Button | Execute search & close dialog | `SearchButton_Click` | None (sets `DialogResult = true`) | WORKING |
| CancelButton | Button | Close dialog without searching | `CancelButton_Click` | None (sets `DialogResult = false`) | WORKING |

**Total Interactive Elements:** 32 (4 tabs, 28 controls across tabs)

---

## User Workflows

### Workflow 1: Search for Concerts Containing Multiple Songs

**Goal:** Find all concerts that contain ALL selected songs (in any order).

**Steps:**
1. User clicks "Advanced Search" from Library Browser menu
2. Dialog opens with "Contains Songs" tab active by default
3. User types partial song name in filter box (e.g., "dark")
   - Filter text stored in `_songFilter` (line 92)
   - `ApplySongFilter()` called (line 105)
   - Only matching checkboxes shown in `SongCheckListPanel`
   - Clear button (✕) appears when filter has text
4. User checks desired songs from filtered list
   - `SongCheckBox_Changed` handler called (line 120)
   - `UpdateSelectedSongsCount()` shows count below buttons (line 125)
   - Counter counts ALL checked boxes, not just visible ones (line 128)
5. User repeats filter/select process for additional songs
6. User clicks "Search" button
   - `SearchButton_Click` collects checked songs from `_allSongCheckBoxes` list (line 269)
   - **Critical:** Iterates `_allSongCheckBoxes`, NOT `SongCheckListPanel.Children` (avoids filtering bug)
   - Validates at least one criterion selected (line 291)
   - Sets `SelectedSongs` property (line 268)
   - Sets `DialogResult = true` and closes (line 298)
7. Library Browser receives `SelectedSongs` list and filters concerts

**Success Path:** Dialog returns with `SelectedSongs` populated, Library Browser performs fuzzy match search.

**Failure Paths:**
- User clicks Search with no songs selected → Warning: "Please select some songs, excluded songs, or create a sequence to search for." (line 293)
- User cancels → Dialog returns `DialogResult = false`

**Data Flow:**
- **Input:** User checkbox selections
- **Processing:** Collect from `_allSongCheckBoxes` (includes hidden checkboxes)
- **Output:** `SelectedSongs` property set to List<string> of canonical song names
- **Service Call:** None (search performed by Library Browser after dialog closes)

---

### Workflow 2: Search for Concerts Excluding Specific Songs

**Goal:** Find all concerts that DO NOT contain any of the selected songs (NOT query).

**Steps:**
1. User clicks "Exclude Songs" tab
2. User filters song list using `ExcludeSongFilterTextBox`
   - Separate filter state in `_excludeSongFilter` (line 205)
   - `ApplyExcludeSongFilter()` updates `ExcludeSongCheckListPanel` (line 218)
3. User checks songs to exclude
   - `ExcludeSongCheckBox_Changed` handler updates count (line 233)
   - `UpdateExcludedSongsCount()` shows "{N} song(s) excluded" (line 238)
4. User clicks "Search" button
   - `SearchButton_Click` collects from `_allExcludeSongCheckBoxes` (line 279)
   - Sets `ExcludedSongs` property (line 278)
   - Sets `DialogResult = true` (line 298)
5. Library Browser receives `ExcludedSongs` list and filters out matching concerts

**Success Path:** Dialog returns with `ExcludedSongs` populated.

**Failure Paths:**
- No songs excluded and no other criteria → Warning message
- User cancels → Dialog returns `DialogResult = false`

**Data Flow:**
- **Input:** User checkbox selections in Exclude tab
- **Processing:** Collect from `_allExcludeSongCheckBoxes` (independent from Contains tab)
- **Output:** `ExcludedSongs` property set to List<string>
- **Service Call:** None (filtering done by Library Browser)

**Business Rule:** Exclude list is independent from Contains list. User can use both simultaneously (e.g., "find concerts WITH Dark Star but WITHOUT Drums").

---

### Workflow 3: Search for Concerts with Song Sequence

**Goal:** Find concerts where specific songs appear in consecutive order (e.g., China Cat Sunflower → I Know You Rider).

**Steps:**
1. User clicks "Song Sequence" tab
2. User selects song from `SequenceSongComboBox` dropdown
   - ComboBox populated with all 600+ songs from `_normalizationService.GetAllTitles()` (line 87)
3. User clicks "Add →" button
   - `AddToSequenceButton_Click` creates `SequenceItem` with index (line 159)
   - Item added to `_sequenceItems` list
   - `RefreshSequenceList()` updates UI (line 195)
   - ListBox shows numbered list (1, 2, 3...) with song names
   - Empty state message hidden when items exist (line 200)
4. User repeats to add more songs in desired order
5. User removes incorrect song by clicking (✕) button
   - `RemoveFromSequence_Click` finds item by `Tag` (line 171)
   - Item removed from `_sequenceItems`
   - Remaining items re-indexed (1, 2, 3...) (line 179)
   - UI refreshed (line 184)
6. User clicks "Search" button
   - `SearchButton_Click` collects sequence from `_sequenceItems` (line 288)
   - Sets `SongSequence` property to List<string> in order
   - Sets `DialogResult = true`
7. Library Browser receives ordered list and searches for consecutive matches

**Success Path:** Dialog returns with `SongSequence` populated in user-specified order.

**Failure Paths:**
- Empty sequence and no other criteria → Warning message
- User clears sequence mid-build → "Clear Sequence" button resets `_sequenceItems` (line 191)

**Data Flow:**
- **Input:** ComboBox selections + Add button clicks
- **Processing:** Build ordered `_sequenceItems` list with 1-based indexing
- **Output:** `SongSequence` property as ordered List<string>
- **Service Call:** `_normalizationService.GetAllTitles()` to populate dropdown

**Technical Note:** Re-indexing after removal ensures ListBox always shows 1, 2, 3... without gaps (line 179-182).

---

### Workflow 4: Search for Individual Tracks by Date or Venue

**Goal:** Find specific performances within albums (e.g., live bonus tracks in studio albums with embedded dates).

**Steps:**
1. User clicks "Search Tracks" tab
2. User enters search criteria:
   - **Date:** yyyy-MM-dd format in `TrackDateTextBox` (e.g., "1972-05-04")
   - **Venue:** Partial venue name in `TrackVenueTextBox` (e.g., "Bickershaw")
   - Either field can be empty (OR logic if both provided)
3. User clicks "Search Tracks" button
   - `TrackSearchButton_Click` validates at least one field has value (line 314)
   - Calls `SearchTracksInLibrary()` with both parameters (line 321)
4. System searches three library locations:
   - **Audience Recordings:** `_librarySettings.LibraryRootPath` (line 345)
   - **Official Releases:** `_librarySettings.OfficialReleasesPath` (line 351)
   - **Studio Albums:** `LibraryRootPath + "Studio Albums"` (line 355)
5. For each album folder found:
   - `SearchInFolder()` iterates all .flac/.mp3 files (line 372-374)
   - `_metadataService.ReadFolder()` reads track metadata (line 379)
   - Each track checked against criteria:
     - **Date Match:** Folder name contains date (live albums) OR track title has `(yyyy-MM-dd)` pattern (line 390-398)
     - **Venue Match:** Folder name OR track title contains venue (case-insensitive) (line 401-409)
   - Matching tracks added to `TrackSearchResult` list (line 413-419)
6. Results displayed in DataGrid:
   - **Track column:** Track title (width: *)
   - **Album column:** Folder name (width: 300)
   - **Type column:** AlbumType (width: 100)
   - Empty state shown if no results (line 333)
7. User double-clicks result row:
   - `TrackResultsDataGrid_MouseDoubleClick` triggered (line 438)
   - Dialog closes with `DialogResult = false` (no search performed) (line 443)
   - Calls `_libraryBrowserWindow?.NavigateToAlbumByPath(result.AlbumPath)` (line 447)
   - Library Browser opens album and scrolls to it in grid

**Success Path:** User finds track, double-clicks, Library Browser navigates to containing album.

**Failure Paths:**
- No date or venue entered → Warning: "Please enter a date or venue to search." (line 316)
- No matches found → DataGrid hidden, empty text shown: "No tracks found matching your search criteria." (line 333)
- Directory access errors → Caught and logged, search continues (line 424-427)

**Data Flow:**
- **Input:** Date string (yyyy-MM-dd) and/or venue string (partial)
- **Processing:**
  - Read all album folders from three library locations
  - For each folder, read tracks via `MetadataService.ReadFolder()`
  - Match date via folder name (live) or embedded `(date)` pattern in title
  - Match venue via case-insensitive substring search
- **Output:** List<TrackSearchResult> with TrackTitle, AlbumTitle, AlbumType, AlbumPath
- **Service Calls:**
  - `_metadataService.ReadFolder(albumFolder)` - Read track metadata from audio files
  - `_libraryBrowserWindow?.NavigateToAlbumByPath(path)` - Navigate to album (on double-click)

**Technical Details:**
- **Date Pattern Matching:** `TrackContainsDate()` uses regex `[\(\[]yyyy-MM-dd[\)\]]` (line 434)
- **Recursive Search:** `Directory.GetDirectories()` with `SearchOption.AllDirectories` (line 368)
- **File Extensions:** Searches both .flac and .mp3 (line 372-373)

---

### Workflow 5: Use Filter to Find Songs in Large Database

**Goal:** Quickly locate songs in 600+ song checklist.

**Steps:**
1. User starts typing in filter box (e.g., "eyes")
2. `SongFilterTextBox_TextChanged` fires on each keystroke (line 90)
   - Text converted to lowercase and stored in `_songFilter` (line 92)
   - Clear button (✕) becomes visible (line 93)
   - `ApplySongFilter()` called immediately (line 94)
3. `ApplySongFilter()` updates UI (line 105):
   - Clears `SongCheckListPanel.Children` (line 107)
   - Iterates `_allSongCheckBoxes` collection (NOT panel children)
   - If checkbox content contains filter string (case-insensitive), adds to panel (line 113-116)
   - Filtered-out checkboxes remain in `_allSongCheckBoxes` with their checked state preserved
4. User sees only matching songs (e.g., "Eyes of the World", "Samson and Delilah" hidden)
5. User checks desired songs from filtered list
6. User clears filter to see all songs again
   - Clicks (✕) button or deletes text
   - `ClearFilterButton_Click` sets text to empty (line 99)
   - `ApplySongFilter()` re-adds all checkboxes to panel (line 113 condition passes)

**Success Path:** User filters, selects, clears filter, selections preserved.

**Key Design Decision:** Filter operates on UI visibility only. The `_allSongCheckBoxes` collection maintains all checkboxes with their checked state regardless of filter. This prevents the "multi-song collection bug" where collecting from panel children would miss filtered-out selections.

**Data Flow:**
- **Input:** User keystrokes in TextBox
- **Processing:** Case-insensitive substring matching on checkbox content
- **Output:** Filtered view in `SongCheckListPanel`
- **State Preservation:** `_allSongCheckBoxes` maintains complete state

---

### Workflow 6: Select All Visible vs Clear All Global

**Goal:** Understand difference between "Select All" (filtered only) and "Clear All" (everything).

**Steps:**
1. User filters songs to "dark" (shows: "Dark Star", "Dark Hollow", etc.)
2. User clicks "Select All" button
   - `SelectAllButton_Click` iterates `SongCheckListPanel.Children` (line 135)
   - Only visible (filtered) checkboxes are checked (line 139)
   - Counter updates to show total selected count (line 142)
3. User clears filter
   - All songs visible again, filtered songs remain checked
   - Previously invisible songs remain unchecked
4. User clicks "Clear All" button
   - `ClearAllButton_Click` iterates `_allSongCheckBoxes` (line 148)
   - ALL checkboxes unchecked, including filtered-out ones (line 150)
   - Counter resets to empty string (line 152)

**Business Rule:** "Select All" operates on filtered view only (current UI state). "Clear All" operates on complete checkbox collection (global reset).

**Rationale:** "Select All" on filtered view allows power users to quickly select a category (e.g., all songs with "lost" in the name). "Clear All" provides guaranteed reset regardless of filter state.

**Data Flow:**
- **Select All:** `SongCheckListPanel.Children` (filtered view) → CheckBox.IsChecked = true
- **Clear All:** `_allSongCheckBoxes` (complete collection) → CheckBox.IsChecked = false

---

### Workflow 7: Combine Multiple Search Criteria

**Goal:** Use multiple tabs simultaneously (e.g., "contains Dark Star but not Drums, in sequence with Wharf Rat").

**Steps:**
1. User selects songs in "Contains Songs" tab (e.g., "Dark Star")
2. User switches to "Exclude Songs" tab and selects "Drums"
3. User switches to "Song Sequence" tab and builds sequence: "Dark Star" → "Wharf Rat"
4. User clicks "Search" button
5. `SearchButton_Click` collects all three lists (line 265-288):
   - `SelectedSongs` from Contains tab
   - `ExcludedSongs` from Exclude tab
   - `SongSequence` from Sequence tab
6. Validation checks if ANY list has values (line 291)
7. All three properties set, dialog returns `DialogResult = true`
8. Library Browser receives all three lists and applies combined filter logic

**Success Path:** All three criteria passed to Library Browser for complex filtering.

**Business Rule:** All three search types can be combined. Library Browser applies AND logic (must contain ALL selected songs AND not contain ANY excluded songs AND match sequence if specified).

**Technical Note:** Each tab maintains independent state in separate collections (`_allSongCheckBoxes`, `_allExcludeSongCheckBoxes`, `_sequenceItems`).

---

## Data Flow

### Input Sources
1. **User Input:**
   - Checkbox selections (Contains/Exclude tabs)
   - Song sequence order (Sequence tab)
   - Date/venue strings (Search Tracks tab)
   - Filter text (all tabs with checklists)

2. **Service Data:**
   - `NormalizationService.GetAllTitles()` → 600+ canonical song names for checklists/dropdowns
   - `MetadataService.ReadFolder()` → Track metadata from audio files (Search Tracks feature)

### Processing Logic

#### Contains/Exclude Search
```csharp
// Collect selected songs (line 268-275)
SelectedSongs.Clear();
foreach (var cb in _allSongCheckBoxes)
{
    if (cb.IsChecked == true)
    {
        SelectedSongs.Add(cb.Content.ToString() ?? "");
    }
}
```

**Key Pattern:** Iterate `_allSongCheckBoxes` (complete collection), NOT `SongCheckListPanel.Children` (filtered view). This prevents bug where filtered-out selections are lost.

#### Sequence Collection
```csharp
// Collect sequence (line 288)
SongSequence = _sequenceItems.Select(i => i.Song).ToList();
```

**Data Structure:** `_sequenceItems` is List<SequenceItem> with `{ Index, Song }`. Index used for UI display, only `Song` values passed to Library Browser.

#### Track Search
```csharp
// Search across three library locations (line 343-359)
SearchInFolder(_librarySettings.LibraryRootPath, AlbumType.Live, searchDate, searchVenue, results);
SearchInFolder(_librarySettings.OfficialReleasesPath, AlbumType.OfficialRelease, searchDate, searchVenue, results);
SearchInFolder(studioAlbumsPath, AlbumType.Studio, searchDate, searchVenue, results);

// Match logic (line 387-409)
if (!string.IsNullOrEmpty(searchDate))
{
    // Live albums: check folder name
    if (albumType == AlbumType.Live && folderName.Contains(searchDate))
        matches = true;
    // All albums: check embedded dates in track titles
    else if (TrackContainsDate(track.Title, searchDate))
        matches = true;
}

if (!string.IsNullOrEmpty(searchVenue) && !matches)
{
    // Case-insensitive substring match in folder or track title
    if (folderName.IndexOf(searchVenue, StringComparison.OrdinalIgnoreCase) >= 0 ||
        track.Title.IndexOf(searchVenue, StringComparison.OrdinalIgnoreCase) >= 0)
        matches = true;
}
```

**Matching Strategy:**
- **Date:** Folder name (for live albums) OR embedded `(yyyy-MM-dd)` pattern in track title
- **Venue:** Case-insensitive substring search in folder name or track title
- **OR Logic:** If either date OR venue matches, track included in results

### Output Data
1. **Contains/Exclude/Sequence Search:**
   - `SelectedSongs` (List<string>) - Songs that must appear
   - `ExcludedSongs` (List<string>) - Songs that must NOT appear
   - `SongSequence` (List<string>) - Songs in specific consecutive order
   - `DialogResult = true` - Indicates search should be performed

2. **Track Search:**
   - `TrackResultsDataGrid.ItemsSource` (List<TrackSearchResult>)
   - `TrackSearchResult { TrackTitle, AlbumTitle, AlbumType, AlbumPath }`
   - Double-click triggers navigation via `_libraryBrowserWindow?.NavigateToAlbumByPath()`

### Service Calls

| Service | Method | Purpose | Called From |
|---------|--------|---------|-------------|
| `NormalizationService` | `GetAllTitles()` | Load 600+ canonical song names | `LoadSongs()` (line 49) |
| `MetadataService` | `ReadFolder(albumFolder)` | Read track metadata from audio files | `SearchInFolder()` (line 379) |
| `LibraryBrowserWindow` | `NavigateToAlbumByPath(path)` | Navigate to album containing track | `TrackResultsDataGrid_MouseDoubleClick()` (line 447) |

---

## Business Rules

### 1. Song List Population
- **Rule:** All song checklists populated from `NormalizationService.GetAllTitles()`
- **Canonical Names Only:** Only canonical song names shown, not aliases
- **Rationale:** Fuzzy matching in Library Browser handles typos/variations automatically
- **Implementation:** `LoadSongs()` called in constructor (line 47-88)

### 2. Filter Behavior
- **Rule:** Filter operates on UI visibility only, does not destroy checkbox state
- **Case-Insensitive:** All filter comparisons use `ToLowerInvariant()` (line 92, 205)
- **Substring Matching:** Filter matches if song name contains filter text anywhere
- **Clear Button:** ✕ button appears when filter has text (line 93, 206)
- **State Preservation:** Filtered-out checkboxes remain in `_allSongCheckBoxes` with checked state intact
- **Implementation:** `ApplySongFilter()` and `ApplyExcludeSongFilter()` (line 105-118, 218-231)

### 3. Select All vs Clear All
- **Select All Rule:** Operates on filtered view (`SongCheckListPanel.Children`)
- **Clear All Rule:** Operates on complete collection (`_allSongCheckBoxes`)
- **Rationale:** Select All enables category selection, Clear All guarantees complete reset
- **Implementation:** `SelectAllButton_Click` (line 135), `ClearAllButton_Click` (line 148)

### 4. Search Collection Logic
- **Rule:** Always collect from `_allSongCheckBoxes` and `_allExcludeSongCheckBoxes`, NEVER from panel children
- **Rationale:** Prevents "multi-song collection bug" where filtered-out selections are lost
- **Critical Code:** Lines 269-275 (Contains), 279-285 (Exclude)
- **Historical Context:** Bug fixed in session 2026-01-08 (see CLAUDE.md)

### 5. Sequence Indexing
- **Rule:** Sequence items display 1-based index (1, 2, 3...)
- **Re-indexing:** After removal, remaining items re-numbered to eliminate gaps
- **Implementation:** `RemoveFromSequence_Click` (line 179-182)
- **UI Binding:** `SequenceItem.Index` bound to ListBox (line 160)

### 6. Search Validation
- **Rule:** At least one search criterion must be selected before Search button succeeds
- **Criteria:** SelectedSongs.Any() OR ExcludedSongs.Any() OR SongSequence.Any()
- **Warning Message:** "Please select some songs, excluded songs, or create a sequence to search for."
- **Implementation:** `SearchButton_Click` (line 291-296)

### 7. Track Search Validation
- **Rule:** At least one field (date or venue) must be filled before track search executes
- **Warning Message:** "Please enter a date or venue to search."
- **Implementation:** `TrackSearchButton_Click` (line 314-319)

### 8. Track Search Locations
- **Rule:** Search all three library locations for track matches
- **Locations:**
  1. `LibraryRootPath` (audience recordings/box sets)
  2. `OfficialReleasesPath` (official releases)
  3. `LibraryRootPath + "Studio Albums"` (studio albums)
- **Rationale:** Live bonus tracks can appear in any album type
- **Implementation:** `SearchTracksInLibrary()` (line 343-361)

### 9. Date Matching Patterns
- **Rule:** Date matches if:
  - **Live Albums:** Folder name contains date string (e.g., "1972-05-04 Bickershaw...")
  - **All Albums:** Track title contains `(yyyy-MM-dd)` or `[yyyy-MM-dd]` pattern
- **Regex:** `[\(\[]yyyy-MM-dd[\)\]]` (line 434)
- **Rationale:** Studio albums use parenthetical dates for live bonus tracks
- **Implementation:** `TrackContainsDate()` (line 431-436)

### 10. Venue Matching
- **Rule:** Venue matches via case-insensitive substring search
- **Locations Searched:** Folder name AND track title
- **Example:** "Bickershaw" matches "Bickershaw Festival" in folder name
- **Implementation:** `StringComparison.OrdinalIgnoreCase` (line 404-405)

### 11. Track Search Navigation
- **Rule:** Double-clicking track search result closes dialog WITHOUT performing song-based search
- **Behavior:** Sets `DialogResult = false` to skip song search (line 443)
- **Navigation:** Calls `LibraryBrowserWindow.NavigateToAlbumByPath()` to open containing album
- **Rationale:** Track search is a separate feature from concert search; user wants to view album, not filter grid

### 12. Tab Independence
- **Rule:** Each tab maintains independent state; multiple tabs can have selections simultaneously
- **Collections:**
  - Contains tab: `_allSongCheckBoxes`
  - Exclude tab: `_allExcludeSongCheckBoxes`
  - Sequence tab: `_sequenceItems`
  - Track Search tab: Independent text fields and results
- **Combination:** All three song-based tabs collected in `SearchButton_Click` (line 268-288)

### 13. Error Handling in Track Search
- **Rule:** Directory access errors logged but don't halt search
- **Behavior:** Catch exceptions per folder, continue searching remaining folders
- **Implementation:** Try/catch in `SearchInFolder()` (line 424-427)
- **Logging:** `System.Diagnostics.Debug.WriteLine()` for debugging

---

## Known Issues

### 1. FIXED: Multi-Song Collection Bug (Session 2026-01-08)
**Problem:** When filter active, clicking Search would only collect visible songs, losing filtered-out selections.

**Root Cause:** Original code collected from `SongCheckListPanel.Children` instead of `_allSongCheckBoxes`.

**Solution:** Introduced `_allSongCheckBoxes` and `_allExcludeSongCheckBoxes` collections. Always collect from these lists instead of UI panel children (line 269, 279).

**Status:** FIXED - Verified in testing.

---

### 2. FIXED: TextChanged Recursion (Session 2026-01-08)
**Problem:** Setting `TextBox.Text` programmatically triggered `TextChanged` event, causing duplicate filter operations.

**Root Cause:** WPF fires `TextChanged` even when text set via code (not just user input).

**Solution:** Direct assignment to `_songFilter` and `_excludeSongFilter` instead of modifying TextBox.Text (line 92, 205).

**Status:** FIXED - No recursion issues remain.

---

### 3. Track Search Performance
**Note:** Track search with broad criteria (e.g., empty venue, common date) can be slow on large libraries (10,000+ files).

**Reason:** Recursive directory search with `SearchOption.AllDirectories` (line 368) + `MetadataService.ReadFolder()` for each album folder (line 379).

**Mitigation:** Search only three root paths (not entire filesystem), results appear when ready.

**Status:** NOT A BUG - Expected behavior for comprehensive search. No indexing/caching implemented yet.

---

### 4. Sequence Duplicate Songs
**Behavior:** User can add same song multiple times to sequence (e.g., "Dark Star → Dark Star → Dark Star").

**Validation:** None - dialog allows duplicate consecutive songs.

**Rationale:** Some concerts feature same song multiple times (reprises, medleys). Library Browser handles matching logic.

**Status:** WORKING AS DESIGNED - No restriction on duplicates.

---

## Edge Cases

### 1. Empty Song Database
**Scenario:** `NormalizationService.GetAllTitles()` returns empty list (corrupted/missing songs.json).

**Behavior:**
- Checklists render empty
- ComboBox for sequence has no items
- User cannot select any songs
- Clicking Search shows validation warning

**Handling:** No explicit check in constructor. Dialog allows opening but is non-functional.

**User Impact:** Cannot perform song-based searches until songs.json restored.

---

### 2. Filter Eliminates All Songs
**Scenario:** User types filter that matches no songs (e.g., "zzzzz").

**Behavior:**
- `SongCheckListPanel.Children` becomes empty (line 107)
- Scroll area shows blank white space
- Select All button has no effect (no visible checkboxes)
- Counter still shows previously selected count (from hidden checkboxes)

**Handling:** Working as designed - user can clear filter to restore view.

**User Impact:** May be confusing if counter shows "5 songs selected" but checklist is empty.

---

### 3. All Three Search Tabs Empty
**Scenario:** User opens dialog, clicks Search without making any selections.

**Behavior:**
- Validation check fails (line 291)
- MessageBox shown: "Please select some songs, excluded songs, or create a sequence to search for."
- Dialog remains open (no `DialogResult` set)

**Handling:** Explicit validation prevents empty search.

**User Impact:** Must dismiss warning and make selections before proceeding.

---

### 4. Library Paths Not Configured
**Scenario:** `LibrarySettings.LibraryRootPath` or `OfficialReleasesPath` is null/empty.

**Behavior in Track Search:**
- `Directory.Exists()` returns false (line 343, 349)
- That path skipped, search continues with other paths
- If all paths missing, results list remains empty
- "No tracks found" message shown

**Handling:** Graceful - only searches paths that exist.

**User Impact:** Track search may return incomplete results if paths not configured.

---

### 5. Corrupted Audio Files in Track Search
**Scenario:** Audio file exists but TagLib-Sharp cannot read metadata.

**Behavior:**
- `MetadataService.ReadFolder()` may throw exception or return empty list
- Exception caught by try/catch in `SearchInFolder()` (line 424)
- Error logged to Debug output
- Search continues with remaining folders

**Handling:** Per-folder error handling prevents one bad folder from halting entire search.

**User Impact:** Some tracks may be missing from results, no user notification of errors.

---

### 6. Track Search with Both Date and Venue
**Scenario:** User enters both date ("1972-05-04") and venue ("Bickershaw").

**Behavior:**
- Date checked first (line 387)
- If date matches, `matches = true`
- Venue check skipped due to `&& !matches` condition (line 401)
- Track included in results if EITHER criterion matches (OR logic)

**Handling:** OR logic allows broader results.

**User Impact:** User may expect AND logic (both must match). Current implementation is OR logic.

---

### 7. Date in Wrong Format
**Scenario:** User enters date as "05/04/1972" instead of "1972-05-04".

**Behavior:**
- Folder name substring match fails (live albums use yyyy-MM-dd format)
- Embedded date regex pattern fails (requires specific format)
- No results found (assuming no album folder happens to contain that string)

**Handling:** No format validation or conversion.

**User Impact:** Search returns no results. User must use yyyy-MM-dd format (tooltip provides hint).

---

### 8. Venue with Special Regex Characters
**Scenario:** User searches for venue like "O'Brien's (Boston)".

**Behavior in Folder Match:**
- `IndexOf()` uses literal string comparison (line 404)
- Parentheses treated as literal characters
- Match succeeds if folder contains exact substring

**Behavior in Track Title Match:**
- Same literal string comparison (line 405)
- No regex escaping needed

**Handling:** `IndexOf()` is regex-safe (literal comparison only).

**User Impact:** Works correctly - no special character issues.

---

### 9. Track Search Results DataGrid Empty State
**Scenario:** Search executed but no results found.

**Behavior:**
- `results.Count == 0` condition true (line 329)
- `TrackResultsDataGrid.ItemsSource` set to null (line 331)
- DataGrid visibility set to Collapsed (line 332)
- `TrackResultsEmptyText` shown with message (line 334)

**Handling:** Explicit empty state handling.

**User Impact:** Clear feedback that search found nothing (not just blank grid).

---

### 10. Track Search Results Double-Click on Empty Area
**Scenario:** User double-clicks DataGrid but not on a specific row.

**Behavior:**
- `TrackResultsDataGrid.SelectedItem` is null
- `if (... is TrackSearchResult result)` condition fails (line 440)
- No navigation occurs
- Dialog remains open

**Handling:** Null-check via pattern matching.

**User Impact:** Double-clicking empty space does nothing (expected behavior).

---

### 11. Sequence with One Song
**Scenario:** User adds single song to sequence and searches.

**Behavior:**
- `_sequenceItems` has one item with Index=1
- `SongSequence` property set to list with one element (line 288)
- Library Browser receives single-song sequence
- Should match concerts with that song (no consecutive order requirement)

**Handling:** No minimum length validation.

**User Impact:** Single-song sequence functionally identical to "Contains Songs" search.

---

### 12. Remove From Sequence While Empty
**Scenario:** User clears sequence, empty state shown, no remove buttons available.

**Behavior:**
- `_sequenceItems.Count == 0`
- `SequenceListBox.ItemsSource` set to empty list (line 198)
- `SequenceEmptyText` visibility set to Visible (line 200)
- No ListBox items render, no remove buttons available

**Handling:** UI correctly shows empty state.

**User Impact:** Cannot click remove button (doesn't exist) - working as designed.

---

### 13. Contains and Exclude Same Song
**Scenario:** User selects "Dark Star" in Contains tab AND Exclude tab simultaneously.

**Behavior:**
- Both `SelectedSongs` and `ExcludedSongs` contain "Dark Star" (line 273, 283)
- Dialog returns `DialogResult = true` with both lists populated
- Library Browser receives contradictory criteria

**Handling:** No validation preventing overlap.

**User Impact:** Library Browser search logic determines behavior (likely returns zero results due to impossible criteria).

---

### 14. Filter with Leading/Trailing Spaces
**Scenario:** User types " dark " (spaces around word).

**Behavior:**
- Filter stored with spaces: `_songFilter = " dark "` (line 92)
- Substring match: `"Dark Star".ToLowerInvariant().Contains(" dark ")` → false
- No songs shown (spaces prevent match)

**Handling:** No trimming of filter text.

**User Impact:** User may be confused why filter finds nothing. Clearing spaces fixes issue.

---

### 15. Library Browser Window Null in Track Search
**Scenario:** Dialog instantiated without `LibraryBrowserWindow` reference (null parameter).

**Behavior:**
- `_libraryBrowserWindow` is null (line 43)
- Track search executes normally
- Results displayed correctly
- Double-click calls `_libraryBrowserWindow?.NavigateToAlbumByPath()` with null-conditional operator (line 447)
- Navigation silently fails (no exception)

**Handling:** Null-conditional operator prevents crash.

**User Impact:** Track search results visible but double-click navigation doesn't work (no visual feedback).

---

### 16. Extremely Long Song Names in Sequence ListBox
**Scenario:** Song name exceeds ListBox width.

**Behavior:**
- TextBlock bound to `{Binding Song}` (line 162)
- No `TextWrapping` or `TextTrimming` specified
- Song name rendered on single line, may overflow ListBox width

**Handling:** WPF default behavior - text may be cut off.

**User Impact:** User can hover to see tooltip (if implemented) or resize dialog to see full name.

---

### 17. Search Button Clicked During Track Search
**Scenario:** User switches to "Search Tracks" tab, enters criteria, clicks main "Search" button (not "Search Tracks" button).

**Behavior:**
- Main Search button collects Contains/Exclude/Sequence criteria (line 268-288)
- Track search fields ignored (not part of validation, line 291)
- If no other criteria selected, validation fails
- If other tabs have selections, dialog closes and performs song-based search (ignoring track fields)

**Handling:** Track search uses separate button (`TrackSearchButton`), main Search button only for song-based searches.

**User Impact:** User must click correct button per search type. No visual indication main Search button is disabled on Track Search tab.

---

## Dependencies

### Services
- **NormalizationService** - Provides canonical song names via `GetAllTitles()`
- **MetadataService** - Reads track metadata from audio files via `ReadFolder()`
- **LibrarySettings** - Provides library paths for track search
- **LibraryBrowserWindow** (optional) - Navigation target for track search results

### Data Models
- **SongCheckItem** - `{ Song: string, IsChecked: bool }` (line 452-456)
- **SequenceItem** - `{ Index: int, Song: string }` (line 458-462)
- **TrackSearchResult** - `{ TrackTitle, AlbumTitle, AlbumType, AlbumPath }` (line 464-470)
- **AlbumType** - Enum (Live, Studio, OfficialRelease)

### External Dependencies
- **System.IO** - Directory/file operations for track search
- **System.Text.RegularExpressions** - Date pattern matching
- **System.Windows.Controls** - CheckBox, ListBox, DataGrid

---

## File Paths

**XAML:** [AdvancedSearchDialog.xaml](AdvancedSearchDialog.xaml)
**Code-Behind:** [AdvancedSearchDialog.xaml.cs](AdvancedSearchDialog.xaml.cs)

**Lines of Code:** 264 XAML, 472 C# (736 total)

---

## Related Documentation

- [00-ui-audit.md](documentation/00-ui-audit.md) - Complete UI audit
- [01-main-window.md](documentation/01-main-window.md) - Main Window feature documentation
- [02-library-browser.md](documentation/02-library-browser.md) - Library Browser feature documentation

---

**Last Updated:** 2026-03-01
**Status:** Complete feature documentation
