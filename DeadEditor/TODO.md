# DeadEditor - Pending Tasks

## Current Status (2026-01-07)

**Latest Commit:** `6e3aca8` - "Add info file viewer, fuzzy matching, and extensive song database improvements"

### App is working well!
- ✅ Importing random files successfully
- ✅ 593 songs in database with fuzzy matching
- ✅ Info file viewer working for reference during imports
- ✅ Track number normalization working
- ✅ All major bugs from previous sessions resolved

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
- Search/filter in library browser
- Export concert metadata to CSV/JSON
- Dark/light theme toggle
- Keyboard shortcuts for common actions

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

## Recently Completed (Session 2026-01-07)

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

### Key Files Modified This Session:
- `Data/songs.json` - Now contains 593 songs with aliases
- `Services/NormalizationService.cs` - Added fuzzy matching with Levenshtein distance
- `Services/MetadataService.cs` - Auto-imports .txt info files, strips track numbers
- `Models/AlbumInfo.cs` - Added InfoFileContent and InfoFileName properties
- `Models/TrackInfo.cs` - Fixed empty parentheses in date display
- `MainWindow.xaml.cs` - Added View Info File button and handler
- `LibraryBrowserWindow.xaml.cs` - Improved pause button logic

### Database Coverage:
- **Total Songs:** 593
- **Includes:** Grateful Dead core repertoire, NRPS songs, covers, jams, tuning, intro, feedback
- **Fuzzy Matching:** Handles up to 2 character typos automatically
- **Common Aliases:** Handles variations like "Dnacing", "Wkae", "Monkey &", etc.

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
