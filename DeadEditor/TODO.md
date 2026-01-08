# DeadEditor - Pending Tasks

## Current Status (2026-01-08)

**Latest Commit:** `c6f4801` - "Fix song search bugs and add exclude functionality"

### App is working excellently!
- ✅ Importing random files successfully
- ✅ 598 songs in database (594 Grateful Dead, 4 NRPS) with fuzzy matching
- ✅ Info file viewer working for reference during imports
- ✅ Track number normalization working
- ✅ **Advanced Search** - Search by songs (contains ANY/ALL), song sequences, and excluded songs (NOT)
- ✅ **Song Database Management** - Add/manage songs on-the-fly without recompiling
- ✅ **Artist-based Organization** - Songs organized by artist with migration script
- ✅ All major bugs from previous sessions resolved (including critical search bugs)

---

## Potential Future Enhancements

### 1. Additional Song Coverage
**Priority:** Low
**Status:** Ongoing as needed
**Description:** Continue adding songs/aliases as you encounter unmatched tracks in your taper collection.

**Current Coverage:** 593 songs with aliases and fuzzy matching (max 2 char typos)

---

### 2. UI/UX Improvements
**Priority:** Low
**Status:** Ideas for future consideration

**Potential Ideas:**
- Batch import multiple concerts at once
- ✅ ~~Search/filter in library browser~~ - **COMPLETED** (Quick search + Advanced Search)
- Export concert metadata to CSV/JSON
- Dark/light theme toggle
- Keyboard shortcuts for common actions
- Fix: Main window opens in background at startup (minor UI issue)

---

### 3. Advanced Features
**Priority:** Low
**Status:** Future consideration

**Ideas:**
- Duplicate concert detection (same date/venue)
- Show statistics (most played songs, venue counts, etc.)
- Integration with online databases (archive.org, etree.org)
- Automated backup/sync functionality

---

## Recently Completed (Session 2026-01-08)

### Major Features Added:
- ✅ **Advanced Search Dialog** - Comprehensive search with 3 tabs:
  - "Contains Songs" - Find shows with ALL selected songs (in any order)
  - "Exclude Songs" - Find shows WITHOUT specific songs (NOT queries)
  - "Song Sequence" - Find shows with songs in specific order (e.g., "China Cat > I Know You Rider")
- ✅ **Song Filter** - Real-time filtering in song selection (handles 600+ songs easily)
- ✅ **Add Song Dialog** - Add songs on-the-fly without recompiling, with artist support
- ✅ **Manage Songs Dialog** - Browse all 598 songs, view aliases, filter by artist, export to text
- ✅ **Artist-based Database** - Songs organized by artist (Grateful Dead, NRPS, etc.)
- ✅ **Quick Search** - Search by date, venue, location directly from library browser
- ✅ **Search Display** - Shows actual song names in search box (e.g., "Dark Star, Althea NOT I Know You Rider")

### Critical Bug Fixes:
- ✅ **LINQ Lazy Evaluation Bug** - Fixed song searches returning 0 results inconsistently
  - Root cause: `Where()` clause was re-evaluated multiple times with captured variables
  - Solution: Replace lazy LINQ with immediate `foreach` evaluation
- ✅ **TextChanged Recursion Bug** - Fixed duplicate searches triggered by programmatic text box updates
  - Solution: Added `_isUpdatingSearchBox` flag to prevent recursive calls
- ✅ **Multi-song Collection Bug** - Fixed advanced search only collecting visible filtered songs
  - Solution: Collect from `_allSongCheckBoxes` instead of `SongCheckListPanel.Children`

### UI Improvements:
- ✅ Smart search box display showing 1-3 song names, then "and X more"
- ✅ Excluded songs shown with "NOT" prefix for clarity
- ✅ Select All/Clear All buttons respect filtering
- ✅ Song count indicators ("5 songs selected", "2 songs excluded")
- ✅ Filter clear button (X) appears when typing

### Technical Improvements:
- ✅ Database migration script (`migrate_songs.py`) - Converted 598 songs to artist-based structure
- ✅ Backward compatibility - Reads both new artist-based and legacy song structures
- ✅ `ExcludedSongs` property and filter logic in `ShowMatchesSongCriteria()`
- ✅ Prevented lazy LINQ enumeration issues with explicit list building

**Commit:** `c6f4801` - "Fix song search bugs and add exclude functionality"
**Files Changed:** 19 files (+10,261 insertions, -623 deletions)

---

## Previously Completed (Session 2026-01-07)

### Major Features Added:
- ✅ **Info File Viewer** - Auto-import .txt files, non-modal window with always-on-top for copy/paste during import
- ✅ **Fuzzy Matching** - Levenshtein distance algorithm handles typos automatically (max 2 chars or 20% string length)
- ✅ **Song Database Expansion** - Expanded from 110 to 593 songs
- ✅ **Track Number Stripping** - Remove "01 ", "02 " prefixes from titles during normalization
- ✅ **Tape Marker Removal** - Strip "//" markers from song titles
- ✅ **NRPS Songs** - Added New Riders of the Purple Sage songs (Tuning, Truck Driving Man, Whatcha Gonna Do, etc.)
- ✅ **Empty Parentheses Fix** - Only show date when present (no more empty "()")

### Bug Fixes:
- ✅ Fixed Peggy-O normalization (box-drawing character ─ support)
- ✅ Fixed library location display showing only state instead of "City, State"
- ✅ Fixed folder name parsing for multiple formats
- ✅ Fixed pause button double-click issue and improved UI styling
- ✅ Strip leading track numbers from titles
- ✅ Handle multiple dash character types (en-dash, em-dash, box-drawing)

### Technical Improvements:
- ✅ Enhanced NormalizationService with Levenshtein distance
- ✅ Updated MetadataService to auto-import .txt files
- ✅ Added InfoFileContent and InfoFileName properties to AlbumInfo
- ✅ Improved CleanTitle regex patterns
- ✅ Made info viewer non-modal with Topmost property

**Commit:** `6e3aca8` - "Add info file viewer, fuzzy matching, and extensive song database improvements"
**Files Changed:** 9 files (+1029 insertions, -574 deletions)

---

## Previously Completed (Session 2026-01-06)

- ✅ Edit Metadata functionality - Edit already-imported concerts
- ✅ Media key support - Keyboard play/pause/stop/next/previous buttons
- ✅ Improved concert view - Full metadata display (title with segue and date)
- ✅ Fixed segue display in library browser
- ✅ Expanded song database from 102 to 110 songs
- ✅ Real-time preview updates when editing track metadata

**Commit:** `20eec7b` - "Add Edit Metadata, media keys, expanded song database, and UI improvements"

---

## Technical Notes for Next Session

### Building and Running:
```bash
# Kill all background processes first
taskkill //F //IM DeadEditor.exe //T
taskkill //F //IM dotnet.exe //T

# Build and run
dotnet build DeadEditor.csproj
dotnet run --project DeadEditor.csproj
```

### Key Files Modified This Session (2026-01-08):
- `AdvancedSearchDialog.xaml/.xaml.cs` - **NEW** - 3-tab search dialog with filtering
- `AddSongDialog.xaml/.xaml.cs` - **NEW** - On-the-fly song addition with artist support
- `ManageSongsDialog.xaml/.xaml.cs` - **NEW** - Browse/export 598 songs
- `ConcertDetailWindow.xaml/.xaml.cs` - **NEW** - Detailed concert view
- `LibraryBrowserWindow.xaml.cs` - Fixed LINQ lazy evaluation, added search functionality
- `Services/NormalizationService.cs` - Added artist-based database support, AddSong() method
- `Models/SongDatabase.cs` - Added ArtistEntry class for organization
- `Data/songs.json` - Now 598 songs organized by artist
- `migrate_songs.py` - **NEW** - Python script to migrate database structure

### Database Coverage:
- **Total Songs:** 598 (594 Grateful Dead, 4 NRPS)
- **Organization:** Artist-based with backward compatibility
- **Includes:** Grateful Dead core repertoire, NRPS songs, covers, jams, tuning, intro, feedback
- **Fuzzy Matching:** Handles up to 2 character typos automatically
- **Common Aliases:** Handles variations like "Dnacing", "Wkae", "Monkey &", etc.
- **Management:** Add/edit songs via UI without recompiling

---

## How to Resume Development

When starting a new session:

1. **Check current status:**
   ```bash
   git status
   git log --oneline -5
   ```

2. **Review this TODO.md** for context on what's been done

3. **Kill any background processes:**
   ```bash
   taskkill //F //IM DeadEditor.exe //T
   taskkill //F //IM dotnet.exe //T
   ```

4. **Build and test:**
   ```bash
   dotnet build DeadEditor.csproj
   dotnet run --project DeadEditor.csproj
   ```

5. **Continue testing** with your taper collection and add songs/aliases as needed
