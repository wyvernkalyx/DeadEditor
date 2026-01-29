# DeadEditor - Pending Tasks

## Current Status (2026-01-29)

**Latest Commit:** `dc2af2b` - "Add studio albums as next session focus in TODO.md"

### Session 2026-01-29 - COMPLETED

**Focus:** Implementing Box Set support for managing multi-concert collections (e.g., "Enjoying the Ride" digital box set)

#### Completed This Session:
- ✅ **Box Set Type Support** - Added AlbumType.BoxSet with dedicated UI fields
- ✅ **Box Set Name Memory** - Remembers last used box set name for faster multi-concert imports
- ✅ **Box Set Detection** - Automatically detects box sets from metadata when editing existing concerts
- ✅ **Library Display** - Box set names appear in "Official Release" column
- ✅ **Concert Detail Panel** - Shows "Box Set: Enjoying the Ride" in library browser
- ✅ **Automatic Library Refresh** - Library updates immediately after editing metadata
- ✅ **MusicBrainz Manual Search** - Added manual album search dialog with year filter
- ✅ **MusicBrainz Always-Show Selector** - Always shows release selector for user confirmation
- ✅ **Double Date Bug Fix** - Fixed duplicate dates in song titles (e.g., "Jack Straw (1978-05-13) (1978-05-13)")

#### Key Features Added:

**1. Box Set Import Workflow:**
- Select "Box Set" album type in import window
- "Box Set Name (optional)" field appears
- Name is remembered for next import (stored in settings.json)
- Album title format: `"Date - Venue - City, State: Box Set Name"` (no space before colon)

**2. Box Set Metadata Detection:**
- Distinguishes between Box Set (`: `) and Official Release (` : `) formats via regex
- `ParseAlbumTitle()` correctly identifies box sets from existing metadata
- Library browser reads album tags to detect box set type

**3. Library Display:**
- Box set names show in "Official Release" column in main library grid
- Concert detail panel shows "Box Set: Enjoying the Ride" below the date
- Gold color (#D7BA7D) for box set text

**4. MusicBrainz Enhancements:**
- Manual search dialog with Album Name, Artist, and Year (optional) fields
- Always shows release selector even for single results
- Both fingerprinting and manual search workflows supported

#### Bug Fixes:
- ✅ Fixed box set name not being saved during import (was checking `AlbumType.Studio` instead of `AlbumType.BoxSet`)
- ✅ Fixed duplicate dates in WriteMetadata - strips existing dates before adding new one
- ✅ Fixed library not refreshing after editing concerts - added `LoadShows()` call
- ✅ Fixed ParseAlbumTitle regex to properly distinguish box sets from official releases using negative lookbehind

#### Known Issues to Address Next Session:
- ⚠️ **Auto-select Box Set radio on new import** - When importing a new concert after remembering a box set name, the Box Set radio button should be auto-selected but currently isn't working reliably
- ⚠️ **Investigate box set name field visibility** - Need to verify pre-fill logic when loading new folders

---

## Next Session Priorities

### 1. Fix Auto-Select Box Set Radio Button (URGENT)
**Priority:** High
**Status:** Partially implemented, needs debugging
**Issue:** When importing a new concert from a remembered box set, the box set name is pre-filled but the Box Set radio button isn't automatically selected
**Location:** MainWindow.xaml.cs lines 246-260
**Current Behavior:** Box set name shows in preview, but radio shows "Live (Audience Recording)"
**Expected Behavior:** Box Set radio should be auto-selected when box set name is remembered

### 2. Complete Three Album Type Testing
**Priority:** High
**Status:** Ready to test
- Test importing a Live recording
- Test importing a Studio album
- Test importing an Official Release
- Test importing a Box Set (multiple concerts)

### 3. Track-Level Search (Previously Planned)
**Priority:** Medium (deferred from previous session)
**Description:** Enhanced Advanced Search for hybrid albums with embedded dates
- Parse embedded dates from track titles
- Show search results with album context
- Open containing album folder when clicked

---

## Technical Details

### Files Modified This Session (2026-01-29):
- `Models/LibrarySettings.cs` - Added `LastBoxSetName` property
- `Models/AlbumInfo.cs` - Box set name format in AlbumTitle property
- `MainWindow.xaml` - Added ManualSearchButton for MusicBrainz
- `MainWindow.xaml.cs` - Box set save logic, auto-select logic (needs fixing)
- `LibraryBrowserWindow.xaml` - Added BoxSetText display field
- `LibraryBrowserWindow.xaml.cs` - Box set detection, library auto-refresh, concert detail display
- `Services/MetadataService.cs` - Fixed ParseAlbumTitle regex, duplicate date removal
- `Services/MusicBrainzService.cs` - Added SearchReleasesByNameAsync() method
- `AlbumSearchDialog.xaml/.xaml.cs` - **NEW** - Manual MusicBrainz search dialog

### Box Set Metadata Format:
**Album Tag Format:**
- Box Set: `"1972-09-15 - Boston Music Hall - Boston, MA: Enjoying the Ride"` (no space before colon)
- Official Release: `"1972-09-15 - Boston Music Hall - Boston, MA : Dave's Picks Vol. 1"` (space before colon)
- Live Recording: `"1972-09-15 - Boston Music Hall - Boston, MA"` (no colon)

**Regex Patterns:**
- Box Set: `@"^(\d{4}-\d{2}-\d{2})\s*-\s*([^-]+)\s*-\s*([^,]+),\s*([^:\s]+)(?<!\s):\s*(.+)$"`
  - Uses negative lookbehind `(?<!\s)` to ensure no space before colon
- Official Release: `@"^(\d{4}-\d{2}-\d{2})\s*-\s*([^-]+)\s*-\s*([^,]+),\s*([^:]+?)\s:\s*(.+)$"`
  - Uses `\s:` to require space before colon

### Settings Storage:
```json
{
  "LastBoxSetName": "Enjoying the Ride",
  "LibraryRootPath": "D:/Projects/library",
  ...
}
```

---

## Potential Future Enhancements

### 1. Additional Song Coverage
**Priority:** Low
**Status:** Ongoing as needed
**Current Coverage:** 598 songs with aliases and fuzzy matching

### 2. UI/UX Improvements
**Priority:** Low
**Ideas:**
- Batch import multiple concerts at once
- Export concert metadata to CSV/JSON
- Dark/light theme toggle
- Keyboard shortcuts for common actions
- Fix: Main window opens in background at startup

### 3. Advanced Features
**Priority:** Low
**Ideas:**
- Duplicate concert detection (same date/venue)
- Show statistics (most played songs, venue counts)
- Integration with online databases (archive.org, etree.org)
- Automated backup/sync functionality

---

## Recently Completed (Session 2026-01-08)

### Major Features Added:
- ✅ **Advanced Search Dialog** - 3-tab search (Contains, Exclude, Sequence)
- ✅ **Song Filter** - Real-time filtering in song selection
- ✅ **Add Song Dialog** - Add songs on-the-fly with artist support
- ✅ **Manage Songs Dialog** - Browse 598 songs, export to text
- ✅ **Artist-based Database** - Songs organized by artist
- ✅ **Quick Search** - Search by date, venue, location

### Critical Bug Fixes:
- ✅ **LINQ Lazy Evaluation Bug** - Fixed inconsistent search results
- ✅ **TextChanged Recursion Bug** - Fixed duplicate searches
- ✅ **Multi-song Collection Bug** - Fixed advanced search collection

**Commit:** `c6f4801` - "Fix song search bugs and add exclude functionality"

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
   taskkill //F //IM DeadEditor.exe
   ```

4. **Build and test:**
   ```bash
   dotnet build DeadEditor.csproj
   dotnet run --project DeadEditor.csproj
   ```

5. **Focus on:** Fixing auto-select Box Set radio button issue (see Next Session Priorities)

---

## Database Coverage (As of 2026-01-08)
- **Total Songs:** 598 (594 Grateful Dead, 4 NRPS)
- **Organization:** Artist-based with backward compatibility
- **Fuzzy Matching:** Handles up to 2 character typos automatically
- **Management:** Add/edit songs via UI without recompiling
