# Feature Documentation: Manage Songs Dialog

## Purpose

The **Manage Songs Dialog** provides a read-only browser for the song database (`Data/songs.json`) with search, artist filtering, and export capabilities. Users can view all 600+ songs with their canonical titles, aliases, and artist associations. The dialog displays live statistics (total songs, artist count, songs with aliases) and supports exporting the filtered view to a formatted text file for documentation or backup purposes. This is the companion to AddSongDialog, focusing on browsing rather than editing.

## How It's Opened

**Trigger:** User clicks "Manage Songs..." button in Settings Window

**Caller:** [SettingsWindow.xaml.cs:188-193](SettingsWindow.xaml.cs#L188-L193)
```csharp
private void ManageSongsButton_Click(object sender, RoutedEventArgs e)
{
    var dialog = new ManageSongsDialog(_normalizationService);
    dialog.Owner = this;
    dialog.ShowDialog();
}
```

**Parameters:**
- `NormalizationService` - Service for reading song database

**Modal Behavior:** Yes (ShowDialog blocks until closed)

---

## Screen Layout

### Visual Structure
A centered modal dialog (800x600px, resizable with minimum 600x400) containing:

- **Header Section:**
  - "Song Database Browser" title (18pt bold)
  - Statistics bar showing: "Total: 598 songs | Artists: 2 | Songs with aliases: 420" (gray text)

- **Filter Section (Horizontal Grid):**
  - Search TextBox with label
  - Artist filter ComboBox with label

- **Song List (Main Content):**
  - Scrollable ItemsControl with custom DataTemplate
  - Each song shows:
    - **Title** (bold, 13pt) aligned left
    - **Artist** (gray, 11pt) aligned right
    - **Aliases** (gray, 11pt) on second line (visible only if aliases exist)
  - Items separated by light gray borders

- **Footer Section:**
  - Result count (e.g., "Showing 42 of 598 songs") when filtered
  - "Export to Text" button
  - "Close" button (default)

### Visual Design
- Clean list-based layout (no grid columns)
- Each song item has subtle border separator (#EEEEEE)
- Title/artist on same line (two-column grid)
- Aliases on separate line below (text wrapping enabled)
- Collapsed visibility for songs without aliases (no empty space)

---

## Interactive Elements

| Element | Type | Action | Event Handler | API/Service Call | Status |
|---------|------|--------|---------------|------------------|--------|
| StatsTextBlock | TextBlock | Display database statistics | Updated by `UpdateStatistics()` | None (calculated from `_allSongs`) | WORKING |
| SearchTextBox | TextBox | Filter songs by title or aliases | `SearchTextBox_TextChanged` | None (real-time filtering) | WORKING |
| ArtistFilterComboBox | ComboBox | Filter songs by artist | `ArtistFilterComboBox_SelectionChanged` | None (real-time filtering) | WORKING |
| SongsItemsControl | ItemsControl | Display filtered song list | N/A (ItemsSource binding) | Loaded via `LoadSongs()` | WORKING |
| ResultCountTextBlock | TextBlock | Show "{N} of {Total}" when filtered | Updated by `ApplyFilter()` | None (calculated) | WORKING |
| ExportButton | Button | Export filtered songs to .txt file | `ExportButton_Click` | `SaveFileDialog`, `StreamWriter` | WORKING |
| CloseButton | Button | Close dialog | `CloseButton_Click` | None (closes window) | WORKING |

**Total Interactive Elements:** 7

---

## User Workflows

### Workflow 1: Browse All Songs

**Goal:** View complete song database with statistics.

**Steps:**
1. User clicks "Manage Songs..." in Settings Window
2. Dialog opens and loads database via `LoadSongs()` (line 25-94)
3. System reads `Data/songs.json` and parses structure:
   - **New Structure:** Artist-based with `database.Artists` array (line 58-80)
   - **Legacy Structure:** Flat song list (fallback if no artists found) (line 36-47)
4. Songs converted to `SongDisplayItem` objects:
   ```csharp
   {
       Title = "Dark Star",
       Artist = "Grateful Dead",
       Aliases = ["Darkstar", "Dark Star ->", "-> Dark Star"],
       AliasesDisplay = "Aliases: Darkstar, Dark Star ->, -> Dark Star",
       HasAliases = Visibility.Visible
   }
   ```
5. Songs sorted alphabetically by title (line 84)
6. Artist filter populated with unique artists + "All Artists" option (line 96-103)
7. Statistics calculated and displayed (line 105-112):
   - Total songs count
   - Unique artist count
   - Songs with aliases count (songs where `Aliases.Any()` is true)
8. All songs displayed in scrollable list (line 93)
9. User scrolls through list viewing titles, artists, and aliases

**Success Path:** All songs loaded and displayed with statistics.

**Data Source:** `Data/songs.json` read directly from disk (line 52-56)

---

### Workflow 2: Search for Song by Title

**Goal:** Find specific song by typing partial title.

**Steps:**
1. User types "dark" in search box
2. `SearchTextBox_TextChanged` fires (line 145)
3. `ApplyFilter()` called immediately (line 147)
4. Filter logic executes (line 114-143):
   ```csharp
   var searchText = SearchTextBox.Text?.ToLowerInvariant() ?? "";
   _filteredSongs = _allSongs.Where(s =>
   {
       bool searchMatch = string.IsNullOrWhiteSpace(searchText) ||
           s.Title.ToLowerInvariant().Contains(searchText) ||
           s.Aliases.Any(a => a.ToLowerInvariant().Contains(searchText));
       return searchMatch;
   }).ToList();
   ```
5. Matching songs displayed (e.g., "Dark Star", "Dark Hollow")
6. Result count shown: "Showing 2 of 598 songs" (line 137)
7. User sees only matching songs in list

**Success Path:** Songs filtered in real-time as user types.

**Search Scope:** Searches both canonical titles AND aliases (line 126-127).

---

### Workflow 3: Filter by Artist

**Goal:** View all songs for specific artist.

**Steps:**
1. User selects "Grateful Dead" from artist dropdown
2. `ArtistFilterComboBox_SelectionChanged` fires (line 150)
3. `ApplyFilter()` called (line 152)
4. Artist filter logic (line 122):
   ```csharp
   bool artistMatch = selectedArtist == "All Artists" || s.Artist == selectedArtist;
   ```
5. Only Grateful Dead songs shown (594 songs)
6. Result count shown: "Showing 594 of 598 songs"
7. User can combine with search (e.g., search "dark" within Grateful Dead)

**Success Path:** Songs filtered by artist, search remains active.

**Combined Filtering:** Artist filter AND search filter applied simultaneously (line 129).

---

### Workflow 4: Export Filtered Songs to Text File

**Goal:** Export current filtered view to .txt file for documentation.

**Steps:**
1. User filters songs (e.g., search "rider", artist "Grateful Dead")
2. User clicks "Export to Text" button
3. `ExportButton_Click` handler executes (line 155)
4. SaveFileDialog shown:
   - Default filename: "song_database.txt"
   - Filter: "Text files (*.txt)|*.txt|All files (*.*)|*.*"
5. User selects destination and clicks Save
6. Export logic writes formatted text (line 168-196):
   ```
   SONG DATABASE EXPORT
   Generated: 2026-03-01 14:23:15
   Total Songs: 598

   ================================================================================

   ARTIST: Grateful Dead
   --------------------------------------------------------------------------------

     Dark Star
       Aliases: Darkstar, Dark Star ->, -> Dark Star

     I Know You Rider
       Aliases: Rider, I Know You Rider >
   ```
7. Songs grouped by artist (line 177)
8. Each artist section shows all songs alphabetically (line 185)
9. Aliases shown only if song has them (line 188-191)
10. Success message: "Song database exported to: {path}" (line 199)

**Success Path:** Filtered songs exported to well-formatted text file.

**Export Scope:** Exports `_filteredSongs`, not `_allSongs` (respects current filter).

**Failure Paths:**
- User cancels SaveFileDialog → No export, dialog remains open
- IOException during write → Error message: "Error exporting database: {message}" (line 204)

---

### Workflow 5: View Song Aliases

**Goal:** Check if song has aliases and what they are.

**Steps:**
1. User searches for "china cat" in search box
2. Song "China Cat Sunflower" appears in list
3. User reads title: **"China Cat Sunflower"** (bold)
4. User reads aliases below: "Aliases: China Cat, China Cat Sunflower >" (gray text)
5. User knows fuzzy matching will accept "China Cat" as valid input

**Success Path:** Aliases visible for songs that have them.

**Visibility Logic:** `HasAliases` property controls TextBlock visibility (line 69, 75):
- If `aliases.Any()` → `Visibility.Visible`
- Otherwise → `Visibility.Collapsed` (no empty "Aliases:" line shown)

---

## Data Flow

### Input Sources

1. **Database File:**
   - `Data/songs.json` read from disk (line 52-56)
   - Path: `AppDomain.CurrentDomain.BaseDirectory + "Data/songs.json"`

2. **User Input:**
   - Search text (partial title/alias match)
   - Artist filter selection

### Processing Logic

#### Database Loading (line 25-94)

**Artist-Based Structure (New Format):**
```csharp
var json = File.ReadAllText(path);
var database = Newtonsoft.Json.JsonConvert.DeserializeObject<Models.SongDatabase>(json);

foreach (var artist in database.Artists)
{
    foreach (var song in artist.Songs)
    {
        var aliases = song.Aliases ?? new List<string>();
        _allSongs.Add(new SongDisplayItem
        {
            Title = song.OfficialTitle,
            Artist = artist.Name,
            Aliases = aliases,
            AliasesDisplay = aliases.Any()
                ? $"Aliases: {string.Join(", ", aliases)}"
                : "No aliases",
            HasAliases = aliases.Any() ? Visibility.Visible : Visibility.Collapsed
        });
    }
}
```

**Legacy Structure Fallback (line 33-47):**
If `database.Artists` is null/empty, loads from flat song list:
```csharp
var allTitles = _normalizationService.GetAllTitles();
foreach (var title in allTitles)
{
    _allSongs.Add(new SongDisplayItem
    {
        Title = title,
        Artist = "Unknown",
        Aliases = new List<string>(),
        AliasesDisplay = "No aliases",
        HasAliases = Visibility.Collapsed
    });
}
```

**Backward Compatibility:** Supports both new artist-based and old flat structures.

#### Statistics Calculation (line 105-112)
```csharp
var totalSongs = _allSongs.Count;
var artistCount = _allSongs.Select(s => s.Artist).Distinct().Count();
var songsWithAliases = _allSongs.Count(s => s.Aliases.Any());

StatsTextBlock.Text = $"Total: {totalSongs} songs | Artists: {artistCount} | Songs with aliases: {songsWithAliases}";
```

**Displayed Stats:**
- Total: Count of all songs
- Artists: Count of unique artist names
- Songs with aliases: Count where `Aliases.Count > 0`

#### Filter Application (line 114-143)
```csharp
_filteredSongs = _allSongs.Where(s =>
{
    // Artist filter
    bool artistMatch = selectedArtist == "All Artists" || s.Artist == selectedArtist;

    // Search filter (searches title and aliases)
    bool searchMatch = string.IsNullOrWhiteSpace(searchText) ||
                       s.Title.ToLowerInvariant().Contains(searchText) ||
                       s.Aliases.Any(a => a.ToLowerInvariant().Contains(searchText));

    return artistMatch && searchMatch;
}).ToList();

SongsItemsControl.ItemsSource = _filteredSongs;
```

**Filter Logic:**
- Both filters must pass (AND logic)
- Artist filter: Exact match or "All Artists"
- Search filter: Case-insensitive substring match in title OR any alias
- Empty search text matches all songs

#### Export Format (line 168-196)
```
SONG DATABASE EXPORT
Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
Total Songs: {count}

==========================================================================

ARTIST: {artist name}
--------------------------------------------------------------------------

  {song title}
    Aliases: {alias1, alias2, alias3}

  {next song}
    ...
```

**Grouping:** Songs grouped by artist, alphabetically within each group (line 177, 185)

### Output Data

**Return Value:** None (read-only dialog, no DialogResult used)

**Exported File:** Text file with formatted song database (if user clicks Export)

**Side Effects:** None (read-only, no modifications to database)

---

## Business Rules

### 1. Read-Only Access
- **Rule:** Dialog provides no editing capabilities (view/export only)
- **Rationale:** Prevents accidental database corruption
- **Editing:** User must manually edit `songs.json` to delete/modify songs

### 2. Alphabetical Sorting
- **Rule:** Songs always sorted by title (line 84)
- **Rationale:** Easy lookup in large lists
- **Artist Grouping:** Export groups by artist, but main list does not

### 3. "All Artists" Default
- **Rule:** Artist filter defaults to "All Artists" (shows all) (line 102)
- **Rationale:** User sees complete database on open

### 4. Case-Insensitive Search
- **Rule:** Search uses `ToLowerInvariant()` for matching (line 116, 126-127)
- **Rationale:** User shouldn't have to match case exactly

### 5. Alias Search Included
- **Rule:** Search matches against both titles AND aliases (line 127)
- **Rationale:** User can search for typo to find correct song
- **Example:** Searching "darkstar" finds "Dark Star" (via alias)

### 6. Real-Time Filtering
- **Rule:** Search and artist filter apply immediately (no "Apply" button)
- **Events:** `TextChanged` and `SelectionChanged` trigger `ApplyFilter()` instantly
- **Rationale:** Fast, responsive filtering

### 7. Result Count Visibility
- **Rule:** Result count shown ONLY when filtered (line 135-142)
- **Display:** "Showing 42 of 598 songs"
- **Hidden:** When showing all songs (filtered count == total count)

### 8. Export Filtered View
- **Rule:** Export uses `_filteredSongs`, not `_allSongs` (line 177)
- **Rationale:** User can export subset (e.g., only Grateful Dead songs)
- **Implication:** "Total Songs" in export header shows filtered count

### 9. Alias Display Format
- **Rule:** Aliases shown as comma-separated list: "Aliases: {alias1, alias2, alias3}" (line 73)
- **Visibility:** TextBlock collapsed if no aliases exist (line 69, 75)

### 10. Backward Compatibility
- **Rule:** If new artist structure not found, fall back to legacy flat list (line 33-47)
- **Legacy Behavior:** All songs assigned artist "Unknown"
- **Rationale:** Support older `songs.json` format

### 11. Statistics Always Global
- **Rule:** Statistics bar shows total database stats, NOT filtered stats (line 105-112)
- **Values:** Total songs, total artists, total songs with aliases
- **Rationale:** Provides database overview regardless of current filter

---

## Validation Rules

**No Validation Required** - Read-only dialog with no user input (except search/filter, which have no restrictions)

---

## Known Issues

### 1. No Song Deletion
**Problem:** Users cannot delete songs from database via UI.

**Impact:** Must manually edit `songs.json` to remove unwanted songs.

**Workaround:** Open `Data/songs.json` in text editor, remove song entries, restart application.

**Status:** LIMITATION - No delete functionality implemented.

---

### 2. No Song Editing
**Problem:** Users cannot edit song titles or aliases via UI.

**Impact:** Typos in canonical titles require manual JSON editing.

**Workaround:** Edit `songs.json` directly or delete and re-add song.

**Status:** LIMITATION - Read-only by design.

---

### 3. Export Header Shows Filtered Count as "Total"
**Problem:** When filtered view exported, header says "Total Songs: {filtered count}" instead of clarifying it's filtered.

**Example:**
- Database has 598 songs
- User filters to 42 songs
- Export header says "Total Songs: 42"

**Confusion:** User may think database only has 42 songs.

**Expected:** Header should say "Total Songs: 42 (filtered from 598)" or similar.

**Status:** MINOR ISSUE - Misleading header in filtered exports (line 172).

---

### 4. Statistics Don't Update on External File Changes
**Problem:** If `songs.json` modified while dialog open, statistics don't refresh.

**Impact:** Stats may be outdated if file changed externally.

**Workaround:** Close and reopen dialog.

**Status:** LIMITATION - No file watcher or refresh button.

---

### 5. Legacy Format Shows "Unknown" Artist
**Problem:** Legacy flat song structure assigns all songs to "Unknown" artist (line 42).

**Impact:** Artist filter becomes useless (all songs under one artist).

**Workaround:** Migrate to new artist-based structure.

**Status:** WORKING AS DESIGNED - Legacy compatibility feature.

---

## Edge Cases

### 1. Empty Song Database
**Scenario:** User opens dialog with empty or missing `songs.json`.

**Behavior:**
- File.Exists() returns false (line 53)
- Skip artist-based loading (line 54)
- `_normalizationService.GetAllArtists()` returns empty list (line 30)
- Legacy fallback also returns empty (line 33-47)
- `_allSongs` remains empty
- Statistics show: "Total: 0 songs | Artists: 0 | Songs with aliases: 0"
- ItemsControl displays empty (no error)
- Artist filter shows only "All Artists"

**Handling:** Graceful empty state.

**User Impact:** Can view empty database without errors.

---

### 2. Search Matches No Songs
**Scenario:** User searches for "zzzzz" (matches nothing).

**Behavior:**
- `ApplyFilter()` returns empty `_filteredSongs` (line 130)
- ItemsControl shows blank area (no songs)
- Result count: "Showing 0 of 598 songs" (line 137)

**Handling:** Empty result set displayed correctly.

**User Impact:** Clear indication no matches found.

---

### 3. Artist Filter with One Artist
**Scenario:** Database has only one artist (e.g., only "Grateful Dead").

**Behavior:**
- Artist dropdown shows: ["All Artists", "Grateful Dead"]
- Selecting either shows same songs
- Result count hidden (filtered count == total count)

**Handling:** Works correctly but filter is redundant.

**User Impact:** Filter has no practical use in single-artist database.

---

### 4. Song with No Aliases
**Scenario:** Song entry has empty aliases list.

**Behavior:**
- `aliases.Any()` returns false (line 65)
- `AliasesDisplay` set to "No aliases" (line 74)
- `HasAliases` set to `Visibility.Collapsed` (line 75)
- Alias TextBlock not rendered (collapsed)

**Handling:** No empty "Aliases:" line shown.

**User Impact:** Clean display with no wasted space.

---

### 5. Song with Very Long Alias List
**Scenario:** Song has 50 aliases.

**Behavior:**
- `AliasesDisplay` contains: "Aliases: {alias1, alias2, ..., alias50}"
- TextWrapping="Wrap" enabled (line 68)
- Alias text wraps across multiple lines
- ItemsControl row expands vertically to fit

**Handling:** Text wraps correctly.

**User Impact:** Very long rows for songs with many aliases (may require scrolling).

---

### 6. Export with No Songs (Filtered Out All)
**Scenario:** User filters to zero songs, clicks Export.

**Behavior:**
- SaveFileDialog shown normally
- File created with header:
  ```
  SONG DATABASE EXPORT
  Generated: 2026-03-01 14:23:15
  Total Songs: 0

  ================================================================================
  ```
- No artist sections (grouping empty list produces no groups)
- File saved successfully

**Handling:** Valid export of empty result set.

**User Impact:** Empty export file created (not very useful but not an error).

---

### 7. Export to Read-Only Location
**Scenario:** User tries to export to protected directory (e.g., `C:\Windows\song_database.txt`).

**Behavior:**
- SaveFileDialog allows selection
- StreamWriter throws IOException (line 168)
- Exception caught (line 202)
- Error message: "Error exporting database: Access to path denied" (line 204)

**Handling:** Exception caught, user notified.

**User Impact:** Can retry with different location.

---

### 8. Search with Leading/Trailing Spaces
**Scenario:** User types "  dark star  " in search box.

**Behavior:**
- Search text NOT trimmed (line 116)
- Contains match: `"Dark Star".Contains("  dark star  ")` → false
- No matches found (spaces prevent match)

**Handling:** Spaces treated as part of search term.

**User Impact:** User must avoid extra spaces (potentially confusing).

**Better Approach:** Trim search text before matching.

---

### 9. Extremely Large Database (10,000+ Songs)
**Scenario:** User has massive database with 10,000 songs.

**Behavior:**
- `LoadSongs()` reads entire file (line 52-56)
- All songs loaded into `_allSongs` (memory-based)
- Filtering operates on in-memory list
- ItemsControl with ScrollViewer handles large count
- No virtualization (all items in visual tree)

**Handling:** Works but may be slow to load/filter.

**Performance Impact:** Initial load and filtering may take several seconds.

**User Impact:** Dialog may feel sluggish on very large databases.

---

### 10. Corrupted JSON File
**Scenario:** `songs.json` has invalid JSON syntax.

**Behavior:**
- `JsonConvert.DeserializeObject()` throws exception (line 56)
- Exception NOT caught in LoadSongs() method
- Dialog crashes or shows error (depends on calling context)

**Handling:** NO ERROR HANDLING for JSON parsing.

**User Impact:** Dialog may crash on corrupted file.

**Better Approach:** Wrap JSON parsing in try/catch.

---

### 11. Artist with No Songs
**Scenario:** Artist entry exists but has empty Songs array.

**Behavior:**
- Foreach loop iterates zero times (line 62)
- No songs added for that artist
- Artist appears in filter dropdown (from dropdown population logic)
- Selecting that artist shows empty list

**Handling:** Artist shown but produces no results.

**User Impact:** Confusing - artist in dropdown but no songs.

---

### 12. Duplicate Songs (Same Title, Same Artist)
**Scenario:** Database has two entries for "Dark Star" under "Grateful Dead".

**Behavior:**
- Both loaded into `_allSongs` (line 67)
- Both displayed in list (no deduplication)
- Statistics count includes both (Total: 599 instead of 598)

**Handling:** Duplicates shown as separate entries.

**User Impact:** User can see duplicates but cannot fix via UI.

---

### 13. Close Dialog While Export in Progress
**Scenario:** User clicks Export, selects large file, then clicks Close before write completes.

**Behavior:**
- Export runs synchronously (blocking) (line 168-196)
- Close button unresponsive until export completes
- No cancel option during export

**Handling:** UI blocks until export done.

**User Impact:** Cannot cancel long exports.

---

### 14. Special Characters in Search
**Scenario:** User searches for "C.C. Rider" (includes periods).

**Behavior:**
- Contains() performs literal string match (not regex)
- Periods treated as literal characters
- Matches "C.C. Rider" exactly

**Handling:** Special characters work correctly.

**User Impact:** No escaping needed for special chars.

---

## Dependencies

### Services
- **NormalizationService** - Provides `GetAllArtists()` and `GetAllTitles()` for legacy format

### Data Models
- **SongDatabase** - Root structure with `Artists` array (`Models.SongDatabase`)
- **SongDisplayItem** - UI model with `Title`, `Artist`, `Aliases`, `AliasesDisplay`, `HasAliases` (line 217-224)

### Data Files
- **Data/songs.json** - Read directly from disk (line 52-56)

### External Libraries
- **Newtonsoft.Json** - JSON deserialization (line 56)
- **Microsoft.Win32.SaveFileDialog** - Export file selection (line 157)
- **System.IO.StreamWriter** - Export file writing (line 168)

### Parent Window
- **SettingsWindow** - Sets dialog owner for modal centering

---

## File Paths

**XAML:** [ManageSongsDialog.xaml](ManageSongsDialog.xaml)
**Code-Behind:** [ManageSongsDialog.xaml.cs](ManageSongsDialog.xaml.cs)

**Lines of Code:** 91 XAML, 226 C# (317 total)

---

## Related Documentation

- [00-ui-audit.md](documentation/00-ui-audit.md) - Complete UI audit
- [04-add-song-dialog.md](documentation/04-add-song-dialog.md) - Add songs (write counterpart to this read-only dialog)
- [06-settings-window.md](documentation/06-settings-window.md) - Parent window that opens this dialog

---

**Last Updated:** 2026-03-01
**Status:** Complete feature documentation
