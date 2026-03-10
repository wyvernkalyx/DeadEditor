# DeadEditor - Pending Tasks

## Current Status (2026-03-10)

**Latest Commit:** `942a679` - "Update Claude settings"

### Session 2026-03-10 - COMPLETED

**Focus:** Import screen redesign implementation and album type simplification

#### Completed This Session:
- ✅ **Import Screen Redesign** - Implemented documentation/16-import-redesign-spec.md
  - Single editable Title column (removed Preview Metadata column)
  - Inline track editing (double-click Date/Segue columns)
  - Album info bar with unified fields for all types
  - Date auto-append when track date differs from album date
  - MusicBrainz as simple populate action (no confirmation dialog)
  - Playback controls on import screen

- ✅ **Album Type Simplification** - Reduced from 4 types to 2
  - `AudienceRecording` (was Live)
  - `OfficialRelease` (covers Studio, Official Release, Box Set, Series)
  - Updated all code, UI dropdowns, and documentation
  - Added backward compatibility mapping
  - Collection Name field (replaces Box Set Name, works with both types)

- ✅ **Bug Fixes**
  - Fixed MainWindow album type dropdown showing old 4 types
  - Fixed Library Browser track display (now shows full titles with date suffixes)
  - Fixed Library Browser dropdown styling (gray on white text)
  - Fixed DateTime.Parse crash on empty Album Date for studio albums
  - Replaced DateTime.Parse with DateTime.TryParse throughout import path

- ✅ **Data Model Updates**
  - Added RawTitle property to TrackInfo (stores original title from file)
  - Added CollectionName, FolderNameOverride, CustomFolderName to AlbumInfo
  - Updated AlbumTitle property for new folder naming logic
  - Changed IsStudioAlbum to IsOfficialRelease

#### Files Modified This Session:
- `Models/AlbumInfo.cs` - Simplified enum, added Collection Name field
- `Models/TrackInfo.cs` - Added RawTitle for library browser display
- `MainWindow.xaml` - Updated album type dropdown to 3 options
- `MainWindow.xaml.cs` - Fixed dropdown selection handler
- `LibraryBrowserWindow.xaml` - Updated Show filter dropdown
- `LibraryBrowserWindow.xaml.cs` - Updated type filtering and display properties
- `Services/LibraryImportService.cs` - Fixed DateTime.Parse crashes, unified OfficialRelease logic
- `Services/MetadataService.cs` - Store RawTitle for display
- `AdvancedSearchDialog.xaml.cs` - Updated album type references
- `documentation/16-import-redesign-spec.md` - **NEW** - Complete import redesign spec

#### Git Commits This Session:
1. `c7d23e6` - Simplify album types from 4 to 2 and fix library browser track display
2. `804acb5` - Fix MainWindow album type dropdown - remove old 4 types
3. `fc5d416` - Fix import crash on empty Album Date for studio albums
4. `942a679` - Update Claude settings

---

## Known Issues / Next Session Priorities

### 1. Dropdown Styling Inconsistency
**Priority:** Medium
**Status:** Partially fixed
**Issue:** Some dropdowns show white/gray text, others black/white, inconsistent across windows
**Locations:**
- MainWindow album type dropdown (needs styling review)
- Library Browser "Show" dropdown (fixed but verify across themes)
**Action:** Comprehensive UI styling audit for all ComboBox controls

### 2. Song Performance Dates Not Showing in Library Browser Track View
**Priority:** High
**Status:** FIXED (as of this session)
**Solution:** Added RawTitle property to TrackInfo, updated Title getter to return RawTitle
**Verify:** Test with concert that has tracks with date suffixes like "Bertha (1971-04-27)"

### 3. FLAC Tag → App Field → View Mapping Review Needed
**Priority:** Medium
**Status:** Not started
**Description:** Need comprehensive audit of:
- Which FLAC tags are read (TITLE, ALBUM, DATE, etc.)
- Which TrackInfo/AlbumInfo properties they map to
- Which properties are displayed in Library Browser vs Import screen
- Ensure consistency across all three layers
**Action:** Create mapping document in documentation folder

### 4. Documentation Updates for Recent Changes
**Priority:** Medium
**Status:** Partially updated
**Completed:**
- ✅ documentation/16-import-redesign-spec.md - Created
**Still Needed:**
- Update documentation/01-main-window.md for redesigned import screen
- Update documentation/02-library-browser.md for simplified album types
- Update documentation/15-data-model.md for new TrackInfo.RawTitle field
- Update CLAUDE.md with new AlbumType enum values

### 5. Editable Folder Name Preview (Spec'd but Not Implemented)
**Priority:** Low
**Status:** Documented in spec, not yet implemented
**Description:** From documentation/16-import-redesign-spec.md:
- Folder Name Preview should be editable (click to override auto-computed name)
- Add reset button (↻) to revert to auto-computed mode
- Store override state in AlbumInfo.FolderNameOverride and CustomFolderName
**Action:** Implement editable preview with reset button

### 6. Collection Name Field Not Yet in UI
**Priority:** Medium
**Status:** Data model ready, UI not implemented
**Description:**
- AlbumInfo.CollectionName added to model
- Not yet exposed in MainWindow album info bar
- Spec says "COLLECTION NAME" field should be in album info bar
**Action:** Add Collection Name TextBox to MainWindow.xaml album info section

---

## Testing Checklist for Next Session

Before considering the redesign complete, test:

- [ ] Import audience recording (with date) - verify folder structure
- [ ] Import studio album (no date, Album Name + Year) - verify no crash
- [ ] Import official release (Dave's Picks, etc.) - verify series folder
- [ ] Import with Collection Name - verify folder naming
- [ ] Library Browser shows full track titles with dates
- [ ] Library Browser "Show" filter works with 2 types
- [ ] MainWindow dropdown shows only 3 options (Auto-detect, Audience Recording, Official Release)
- [ ] Album type auto-detection works correctly
- [ ] Playback controls work on import screen
- [ ] MusicBrainz lookup populates fields correctly

---

## Recently Completed Features

### Import Screen Redesign (Session 2026-03-10)
- Single-column editable track list (Title column)
- Inline editing for Date and Segue columns
- Album info bar with all fields always visible
- Auto-detect album type from filled fields
- MusicBrainz integration as simple button (no confirmation dialog)
- Playback controls integrated into import screen

### Album Type Simplification (Session 2026-03-10)
- Reduced from 4 types (Live, Studio, OfficialRelease, BoxSet) to 2 (AudienceRecording, OfficialRelease)
- All UI updated (MainWindow, LibraryBrowser, AdvancedSearch)
- Backward compatibility for existing imports
- Collection Name replaces Box Set Name (more flexible)

### Bug Fixes (Session 2026-03-10)
- DateTime.Parse crash on empty dates (studio albums)
- Library browser track titles missing date suffixes
- Dropdown styling (gray on white text)
- MainWindow dropdown showing old 4 album types

### Box Set Support (Session 2026-01-29)
- Box set type detection from metadata
- Box set name memory across imports
- Library display with box set information
- MusicBrainz manual search dialog
- Always-show release selector

### Advanced Search (Session 2026-01-08)
- 3-tab search (Contains Songs, Exclude Songs, Song Sequence)
- Song filter for 600+ song database
- Real-time search results
- Fixed LINQ lazy evaluation bugs

---

## Potential Future Enhancements

### 1. Additional UI/UX Improvements
**Priority:** Low
**Ideas:**
- Implement editable Folder Name Preview with reset button
- Batch import multiple concerts at once
- Export concert metadata to CSV/JSON
- Dark/light theme toggle
- Keyboard shortcuts for common actions
- Fix: Main window opens in background at startup

### 2. Advanced Features
**Priority:** Low
**Ideas:**
- Duplicate concert detection (same date/venue)
- Show statistics (most played songs, venue counts)
- Integration with online databases (archive.org, etree.org)
- Automated backup/sync functionality
- Track-level search for hybrid albums with embedded dates

### 3. Additional Song Coverage
**Priority:** Low
**Status:** Ongoing as needed
**Current Coverage:** 598 songs (594 Grateful Dead, 4 NRPS)

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

5. **Focus on:** Testing import workflows and addressing known issues (see Next Session Priorities)

---

## Documentation Structure

DeadEditor has comprehensive documentation in the `documentation/` folder:

### Windows/Dialogs
- `01-main-window.md` - Import workflow (needs update for redesign)
- `02-library-browser.md` - Library grid, search (needs update for 2 types)
- `03-advanced-search-dialog.md` - 3-tab search
- `04-add-song-dialog.md` - Add songs on-the-fly
- `05-manage-songs-dialog.md` - Browse/export song database
- `06-settings-window.md` - Configure library paths
- `07-release-selector-dialog.md` - Select MusicBrainz releases
- `08-album-search-dialog.md` - Manual MusicBrainz search

### Services & Data
- `11-metadata-service.md` - ID3 tag reading/writing
- `12-normalization-service.md` - Song title normalization
- `13-library-import-service.md` - Two-path library system
- `14-musicbrainz-service.md` - AcoustID fingerprinting
- `15-data-model.md` - All model classes (needs update for RawTitle)
- `16-import-redesign-spec.md` - **NEW** - Import screen redesign (implemented)

### Main Docs
- `CLAUDE.md` - AI assistant context document (needs update for AlbumType)
- `TODO.md` - This file (project status and tasks)

---

## Database Coverage (As of 2026-01-08)
- **Total Songs:** 598 (594 Grateful Dead, 4 NRPS)
- **Organization:** Artist-based with backward compatibility
- **Fuzzy Matching:** Handles up to 2 character typos automatically
- **Management:** Add/edit songs via UI without recompiling
