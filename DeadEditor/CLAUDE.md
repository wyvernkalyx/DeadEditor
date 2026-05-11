# DeadEditor - AI Assistant Context Document

## Project Overview

**DeadEditor** is a digital music metadata editor and library manager specifically designed for live concert recordings. Unlike generic metadata editors, DeadEditor provides specialized features for managing, searching, and playing live concert collections with consistent naming conventions and rich metadata.

### Target Users
People who collect live music recordings (tapers, collectors, archivists).

**Note:** While development/testing uses Grateful Dead as sample data, the application is **artist-agnostic by design**. The codebase makes no hardcoded assumptions about specific artists and can manage any live music collection.

### Core Problem Solved
Live music metadata suffers from:
- Inconsistent date formatting across sources
- Typos in song titles and venue information
- Lack of standardization in album/track naming conventions
- Difficulty searching across large collections for specific performances or song sequences
- Multiple editions of official releases treated as identical

**DeadEditor's Solution:** Enforce strict yyyy-MM-dd date format conventions and provide intelligent fuzzy matching to handle real-world metadata inconsistencies.

---

## Ground Rules

These are invariants. They apply to every feature, every commit, every session. If a proposed change appears to require violating one, stop and raise it explicitly — do not work around it.

### Source files are read-only

DeadEditor never writes to source folders. The user's source archive — the folder they Browse to in the Import view, or any path outside `LibrarySettings.LibraryRootPath` — is treated as read-only. We copy from it, never to it.

All tag writes, file renames, artwork updates, and fingerprint persistence operate exclusively on copies inside the managed library. If a feature needs to mutate audio files, the change must land on the managed copy.

This is enforced two ways:

1. **Structurally.** Every tag-writing entry point in the app is reachable only from a `LibraryShow` (managed-by-construction) or from the Library Import target path (computed from `LibraryRoot`). There is no UI surface that opens a non-managed folder for write.
2. **At runtime.** `PathGuard.EnsureWithinLibrary` is called at the top of `MetadataService.WriteMetadata`, `FingerprintService.WriteFingerprintToTrackFile`, and `ManifestService.WriteManifest`. Any path outside the managed library throws `InvalidOperationException` immediately, before any file handle is opened.

The single legitimate exception is the import copy step itself, which reads the source. Reads are unconstrained; writes are guarded.

**If we destroy data, it is always our copy of the data — never the user's source archive.**

When adding a new feature that writes to disk:

- Confirm the write target is under `LibraryRoot`. Verify it, don't assume it.
- If you add a new write service, call `PathGuard.EnsureWithinLibrary` at the top of it.
- Do not add an `allowSourceWrite` escape hatch or any equivalent bypass. There is no legitimate source-write in DeadEditor; an escape hatch is a foot-gun.
- If a workflow seems to require writing to source, stop and raise it. There is almost certainly a managed-side equivalent (or one we should build).

---

## Documentation Handbook

**RULE:** Before modifying any file, check this table and read the relevant documentation file first. **The documentation is the spec — code must match the doc.**

### Documentation Lookup Table

| Working on... | Read first | Lines | Purpose |
|--------------|------------|-------|---------|
| **Shell** | | | |
| `ShellWindow.xaml/.cs` | [documentation/18-shell-redesign-spec.md](documentation/18-shell-redesign-spec.md) | ~430 lines | Single-window shell, sidebar, navigation, view switching |
| `Services/NavigationService.cs` | [documentation/18-shell-redesign-spec.md](documentation/18-shell-redesign-spec.md) § Navigation | ~130 lines | View stack, forward/back/root navigation |
| **Views (UserControls in Views/)** | | | |
| `Views/LibraryGridView.xaml/.cs` | [documentation/02-library-browser.md](documentation/02-library-browser.md) | - | Library grid, quick search, concert list |
| `Views/AlbumDetailView.xaml/.cs` | [documentation/18-shell-redesign-spec.md](documentation/18-shell-redesign-spec.md) § Album Detail | - | Album art, track list, date grouping, playback |
| `Views/ImportView.xaml/.cs` | [documentation/01-main-window.md](documentation/01-main-window.md) | - | Import workflow, folder selection, normalize, renumber |
| `Views/EditMetadataView.xaml/.cs` | [documentation/18-shell-redesign-spec.md](documentation/18-shell-redesign-spec.md) § Edit Metadata | - | Edit FLAC tags in-place, save changes |
| `Views/SettingsView.xaml/.cs` | [documentation/06-settings-window.md](documentation/06-settings-window.md) | - | Library paths, fpcalc, primary artist, reset data |
| `Views/HeaderBar.xaml/.cs` | [documentation/18-shell-redesign-spec.md](documentation/18-shell-redesign-spec.md) § Header Bar | - | Context-sensitive header with nav + action buttons |
| `Views/SidebarPanel.xaml/.cs` | [documentation/18-shell-redesign-spec.md](documentation/18-shell-redesign-spec.md) § Sidebar | - | Library/Import/Settings icon navigation |
| `Views/PlayerBar.xaml/.cs` | [documentation/18-shell-redesign-spec.md](documentation/18-shell-redesign-spec.md) § Player Bar | - | Transport controls, progress, volume |
| `Views/PlaylistPanel.xaml/.cs` | [documentation/18-shell-redesign-spec.md](documentation/18-shell-redesign-spec.md) § Playlist | - | Compact track list, always visible |
| **Dialogs (modal, overlay shell)** | | | |
| `AdvancedSearchDialog.xaml/.cs` | [documentation/03-advanced-search-dialog.md](documentation/03-advanced-search-dialog.md) | ~9,500 words | 3-tab search (Contains/Exclude/Sequence) |
| `AddSongDialog.xaml/.cs` | [documentation/04-add-song-dialog.md](documentation/04-add-song-dialog.md) | ~7,600 words | Add songs to database with artist support |
| `ManageSongsDialog.xaml/.cs` | [documentation/05-manage-songs-dialog.md](documentation/05-manage-songs-dialog.md) | ~8,200 words | Browse songs, filter, export to text |
| `ReleaseSelectorDialog.xaml/.cs` | [documentation/07-release-selector-dialog.md](documentation/07-release-selector-dialog.md) | ~7,400 words | Select from multiple MusicBrainz releases |
| `AlbumSearchDialog.xaml/.cs` | [documentation/08-album-search-dialog.md](documentation/08-album-search-dialog.md) | ~7,000 words | Manual MusicBrainz search by name |
| `MatchToSongDialog.xaml/.cs` | [documentation/01-main-window.md](documentation/01-main-window.md) § Match to Song | - | Manual match unmatched track to setlist song with auto-alias |
| **Services** | | | |
| `MetadataService.cs` | [documentation/11-metadata-service.md](documentation/11-metadata-service.md) | ~8,000 words | ID3 tags, ParseAlbumTitle regex, box set vs official release |
| `NormalizationService.cs` | [documentation/12-normalization-service.md](documentation/12-normalization-service.md) | ~7,000 words | 14-stage normalization, fuzzy matching, Levenshtein distance |
| `LibraryImportService.cs` | [documentation/13-library-import-service.md](documentation/13-library-import-service.md) | ~5,000 words | Universal single-path library system, folder creation, metadata preservation |
| `MusicBrainzService.cs` | [documentation/14-musicbrainz-service.md](documentation/14-musicbrainz-service.md) | ~9,500 words | AcoustID fingerprinting, fpcalc.exe, MusicBrainz API, rate limiting |
| `ShowLookupService.cs` | (inline — no separate doc) | - | Loads Data/shows.json, O(1) venue lookup by yyyy-MM-dd date |
| `ReleaseLookupService.cs` | (inline — no separate doc) | - | Loads Data/releases.json, autocomplete for album/release names |
| **Models** | | | |
| `AlbumInfo.cs` | [documentation/15-data-model.md](documentation/15-data-model.md) § AlbumInfo | ~11,000 words | Album metadata, type-based polymorphism, AlbumTitle format |
| `TrackInfo.cs` | [documentation/15-data-model.md](documentation/15-data-model.md) § TrackInfo | ~11,000 words | Track metadata, segue notation, GetFinalMetadataTitle |
| `LibrarySettings.cs` | [documentation/15-data-model.md](documentation/15-data-model.md) § LibrarySettings | ~11,000 words | User settings, single library root, window positions |
| `SongDatabase.cs` | [documentation/15-data-model.md](documentation/15-data-model.md) § SongDatabase | ~11,000 words | Song database structure, artist-based organization |
| **Data Files** | | | |
| `Data/songs.json` | [documentation/15-data-model.md](documentation/15-data-model.md) § songs.json | ~11,000 words | JSON schema, examples, 598 songs across 2 artists |
| `Data/shows.json` | (inline — no separate doc) | - | setlist.fm venue data keyed by yyyy-MM-dd, used by ShowLookupService |
| `Data/releases.json` | (inline — no separate doc) | - | Series templates + standalone album names for autocomplete |
| `%APPDATA%/DeadEditor/settings.json` | [documentation/15-data-model.md](documentation/15-data-model.md) § settings.json | ~11,000 words | Settings JSON schema, all 12 keys with defaults |

### Future Design Documents

Stubs and banking memos for design conversations not yet implemented:

- [verification-model.md](documentation/verification-model.md) — verification status for curated records (placeholder)
- [curation-layer-design-memo.md](documentation/curation-layer-design-memo.md) — Layer A setlist authority vs Layer B per-recording manifest, fingerprints as curation lookup key, curation layer as foundational to verification (banking memo)

### Documentation-First Development Workflow

**ALWAYS follow this workflow when making changes:**

1. **DOCUMENT** → Update the doc first if requirements change
   - If changing behavior, update the relevant documentation file BEFORE writing code
   - Document the new business rules, method signatures, parameters, error handling
   - Include examples showing the new behavior

2. **IMPLEMENT** → Write code to match the doc
   - Code must implement EXACTLY what the documentation specifies
   - Method signatures, parameters, return values must match documented spec
   - Business rules in code must match documented business rules

3. **TEST** → Verify the doc's spec is met
   - Test that implementation matches all documented behavior
   - Verify all documented edge cases are handled
   - Check that error handling matches documented error scenarios

4. **VERIFY** → If implementation forced doc changes, update the doc
   - If you discover the doc was wrong during implementation, update the doc
   - If you find new edge cases, document them
   - If error handling differs, update the documentation to match reality

5. **COMMIT** → Code + docs ship together
   - Never commit code without updating relevant documentation
   - Commit message should reference both code and doc changes
   - Documentation and code must stay in sync

### Quick Reference: Finding Documentation

**By Feature:**
- Import workflow → [01-main-window.md](documentation/01-main-window.md)
- Library browsing → [02-library-browser.md](documentation/02-library-browser.md)
- Advanced search → [03-advanced-search-dialog.md](documentation/03-advanced-search-dialog.md)
- Song database management → [04-add-song-dialog.md](documentation/04-add-song-dialog.md), [05-manage-songs-dialog.md](documentation/05-manage-songs-dialog.md)
- MusicBrainz integration → [14-musicbrainz-service.md](documentation/14-musicbrainz-service.md), [07-release-selector-dialog.md](documentation/07-release-selector-dialog.md), [08-album-search-dialog.md](documentation/08-album-search-dialog.md)

**By Business Logic:**
- Album title format (box set vs official release) → [15-data-model.md](documentation/15-data-model.md) § AlbumInfo, [11-metadata-service.md](documentation/11-metadata-service.md) § ParseAlbumTitle
- Song normalization & fuzzy matching → [12-normalization-service.md](documentation/12-normalization-service.md)
- Folder structure & universal single-path system → [13-library-import-service.md](documentation/13-library-import-service.md)
- Segue notation → [15-data-model.md](documentation/15-data-model.md) § TrackInfo
- Track numbering scheme → [15-data-model.md](documentation/15-data-model.md) § TrackInfo

**By Data Structure:**
- JSON schemas → [15-data-model.md](documentation/15-data-model.md) (songs.json + settings.json)
- All model classes → [15-data-model.md](documentation/15-data-model.md)

---

## Technology Stack

### Platform
- **Framework:** .NET 8.0 (WPF on Windows)
- **UI Framework:** WPF (Windows Presentation Foundation)
- **Language:** C# with nullable reference types enabled
- **Target Platform:** Windows-only (WinExe)

### Dependencies
- **NAudio 2.2.1** - Audio playback
- **Newtonsoft.Json 13.0.3** - JSON serialization/deserialization
- **TagLibSharp 2.3.0** - ID3 tag reading/writing

### Architecture
- **Pattern:** MVVM-like with code-behind (pragmatic WPF approach)
- **Data Storage:**
  - Song database: `Data/songs.json` (598 songs, artist-organized)
  - User settings: `%APPDATA%/DeadEditor/settings.json`
  - Concert metadata: Embedded in audio file ID3 tags + folder structure
- **Services:**
  - `MetadataService` - ID3 tag reading/writing
  - `NormalizationService` - Song title normalization with fuzzy matching
  - `LibraryImportService` - Import concerts from folder structure
  - `MusicBrainzService` - MusicBrainz API integration for official releases

---

## Library Structure

### Universal Single-Path System
DeadEditor uses a single configurable library root (`LibraryRootPath`) for all albums regardless of type. The folder layout under that root is universal:

```
{LibraryRoot}/{Artist}/{AlbumFolder}/
```

Audience recordings, studio albums, official live releases, and box sets all live side by side under each artist folder. Album type is a metadata tag — not a folder decision. See [documentation/13-library-import-service.md](documentation/13-library-import-service.md) and [documentation/19-folder-import-and-manifests.md](documentation/19-folder-import-and-manifests.md) for the full rationale.

### Album Types
The `AlbumType` enum is two values (see [Models/AlbumInfo.cs](Models/AlbumInfo.cs)):
- **AudienceRecording** - Audience/taper/soundboard recordings of a single concert
- **OfficialRelease** - Any official release: studio albums, official live releases (Dave's Picks, Road Trips, etc.), and box sets

### AlbumFolder Naming Convention
`LibraryImportService.BuildLibraryFolderName()` composes the folder from `AlbumInfo` fields:
- **Live recordings** (date + venue present): `{Artist} - {Date} - {Venue} - {City}, {State} - {AlbumName}` (each segment omitted if empty)
- **Studio-style releases** (no date or no venue): `{Artist} - {Year} - {AlbumName}`

All dates use strict **yyyy-MM-dd** format for consistent sorting.

### Hybrid Albums
**Important Design Decision:** Official releases increasingly include live bonus material (e.g., "Europe '72 50th Anniversary Edition" with extra live tracks from different dates). These remain a single `OfficialRelease` with an `Edition` field to keep them as one cohesive unit, rather than splitting studio/live content.

---

## Song Database Design

### Structure
- **File:** `Data/songs.json`
- **Format:** Artist-based organization (supports multiple artists)
- **Current Size:** 598 songs (594 Grateful Dead, 4 NRPS)

### Artist Organization
```json
{
  "artists": [
    {
      "name": "Grateful Dead",
      "songs": [
        {
          "canonical": "Dark Star",
          "aliases": ["Darkstar", "Dark Star ->", "-> Dark Star"]
        }
      ]
    }
  ]
}
```

### Features
- **Canonical Names:** Primary/official song title
- **Aliases:** Common variations and typos
- **Fuzzy Matching:** Levenshtein distance algorithm (max 2 character difference or 20% of string length)
- **Runtime Addition:** Songs can be added via UI without recompiling
- **Backward Compatibility:** Reads both new artist-based and legacy flat structures

### Why Fuzzy Matching?
Real-world metadata from tapers and internet sources contains frequent typos:
- "Dnacing in the Street" instead of "Dancing in the Street"
- "Wkae of the Flood" instead of "Wake of the Flood"
- "Monkey &" instead of "Monkey & the Engineer"

Fuzzy matching (up to 2 character typos) automatically handles these without requiring manual alias entry for every variation.

---

## Core Features

### 1. Import Workflow
- Drag-and-drop folder or browse for concert directory
- Auto-parse folder name for date, venue, city, state
- Auto-import `.txt` info files (if present)
- Parse track listing from audio files
- Normalize song titles using fuzzy matching
- Auto-detect segues (e.g., "China Cat Sunflower > I Know You Rider")
- Write standardized ID3 tags to files
- MusicBrainz integration for official releases (select from multiple releases)

### 2. Library Browser
- Grid view of all imported concerts
- Display: Album artwork, date, venue, location, show type
- Quick search by date, venue, or location
- Advanced search (3 tabs):
  - **Contains Songs** - Find shows with ALL selected songs (in any order)
  - **Exclude Songs** - Find shows WITHOUT specific songs (NOT queries)
  - **Song Sequence** - Find shows with songs in specific order
- Song filter for handling 600+ songs in search dialogs
- Play concerts directly from library
- Edit metadata of already-imported concerts

### 3. MusicBrainz Integration (Official Releases)
- Query MusicBrainz API for official release metadata via audio fingerprinting
- **Release Selector Dialog** - Choose from multiple releases/editions of same album
  - Example: "Workingman's Dead" (1970 original, 2003 remaster, 2020 deluxe edition)
  - Each edition is treated as a separate album
  - **Status:** Dialog UI implemented but not triggering (see Known Issues)
- Pre-fill metadata fields for user validation
- User corrects/validates before final save
- **Dependencies:** Requires `fpcalc.exe` (Chromaprint) for audio fingerprinting

### 4. Audio Playback
- Play full concerts or individual tracks
- Track navigation (next/previous)
- Media key support (keyboard play/pause/stop/next/previous buttons)
- Display current track with segue markers and performance date

### 5. Song Database Management
- **Add Songs Dialog** - Add new songs on-the-fly with artist support
- **Manage Songs Dialog** - Browse all songs, view aliases, filter by artist, export to text
- Songs persist in `Data/songs.json` immediately
- **Artist-agnostic design** - No hardcoded artist assumptions in code

### 6. Metadata Editing
- Edit concert metadata after import
- Real-time preview updates
- Batch operations on track listings

---

## Key Design Decisions

### 1. Date Format Standardization
**Decision:** Always use yyyy-MM-dd format everywhere
**Reasoning:** Eliminates confusion from MM/dd/yyyy vs dd/MM/yyyy formats, ensures consistent sorting

### 2. Segue Detection
**Notation:** "China Cat Sunflower > I Know You Rider"
**Storage:** Tracks stored separately but linked with ">" marker
**Display:** Show both song names with segue marker in UI

### 3. Track Title Normalization
**Process:**
1. Strip leading track numbers ("01 ", "02 ")
2. Remove tape markers ("//")
3. Remove box-drawing characters (─, —, –)
4. Strip embedded dates from titles ("(1972-05-04)")
5. Normalize to canonical song name via fuzzy matching
6. Preserve segue markers

### 4. Universal Single-Path Library System
**Decision:** One library root for all album types; `AlbumType` is a metadata tag, not a folder decision
**Reasoning:**
- Folder selected for import is the atomic unit — files imported together stay together in one library folder
- Multi-date official releases (Road Trips, etc.) no longer scatter across date folders
- Single configuration to manage; same folder convention for every import path
- See [documentation/19-folder-import-and-manifests.md](documentation/19-folder-import-and-manifests.md) for the full design memo

### 5. In-Memory Concert Storage
**Decision:** No separate database file for concerts; read from file system + ID3 tags
**Reasoning:**
- Audio files are source of truth
- Avoids sync issues between database and files
- Simplifies backup (just backup audio files)

---

## Critical Bug Fixes (Historical Context)

### LINQ Lazy Evaluation Bug (Session 2026-01-08)
**Problem:** Song searches returned 0 results inconsistently
**Root Cause:** `Where()` clause was re-evaluated multiple times with captured variables changing between evaluations
**Solution:** Replace lazy LINQ chains with immediate `foreach` evaluation and explicit list building

### TextChanged Recursion Bug (Session 2026-01-08)
**Problem:** Duplicate searches triggered when updating search box programmatically
**Root Cause:** Setting `TextBox.Text` triggered `TextChanged` event handler recursively
**Solution:** Added `_isUpdatingSearchBox` flag to prevent recursive calls

### Multi-song Collection Bug (Session 2026-01-08)
**Problem:** Advanced search only collected visible filtered songs instead of selected songs
**Root Cause:** Collecting from `SongCheckListPanel.Children` (filtered view) instead of full list
**Solution:** Maintain separate `_allSongCheckBoxes` collection and collect from that

---

## File Organization

### Key Files by Category

#### Models
- `AlbumInfo.cs` - Concert/album metadata model
- `TrackInfo.cs` - Individual track metadata
- `LibrarySettings.cs` - User settings (paths, window positions)
- `SongDatabase.cs` - Song database structure with artist support

#### Services
- `MetadataService.cs` - ID3 tag reading/writing, info file import
- `NormalizationService.cs` - Song title normalization, fuzzy matching, Levenshtein distance
- `LibraryImportService.cs` - Concert import from folder structure
- `MusicBrainzService.cs` - MusicBrainz API integration
- `ShowLookupService.cs` - Venue lookup by date from Data/shows.json
- `ReleaseLookupService.cs` - Album name autocomplete from Data/releases.json

#### Shell + Views
- `ShellWindow.xaml/.cs` - Single-window shell, sidebar, navigation
- `Views/LibraryGridView.xaml/.cs` - Library grid, search, concert list
- `Views/AlbumDetailView.xaml/.cs` - Album art, track list, date grouping
- `Views/ImportView.xaml/.cs` - Import workflow, normalize, renumber
- `Views/EditMetadataView.xaml/.cs` - Edit FLAC tags in-place
- `Views/SettingsView.xaml/.cs` - Library paths, fpcalc, primary artist
- `Views/HeaderBar.xaml/.cs` - Context-sensitive header bar
- `Views/SidebarPanel.xaml/.cs` - Sidebar navigation icons
- `Views/PlayerBar.xaml/.cs` - Transport controls, progress, volume
- `Views/PlaylistPanel.xaml/.cs` - Compact playlist panel

#### Dialogs (modal)
- `AdvancedSearchDialog.xaml/.cs` - 3-tab search (Contains/Exclude/Sequence)
- `AddSongDialog.xaml/.cs` - Add songs on-the-fly
- `ManageSongsDialog.xaml/.cs` - Browse/export song database
- `ReleaseSelectorDialog.xaml/.cs` - Select from multiple MusicBrainz releases
- `MatchToSongDialog.xaml/.cs` - Manual match unmatched track to setlist song with auto-alias

#### Data
- `Data/songs.json` - Song database (598 songs, artist-organized)
- `Data/shows.json` - Venue/location lookup by date (from setlist.fm)
- `Data/releases.json` - Series templates + standalone release names for autocomplete

---

## Development Workflow

### Building and Running
```bash
# Kill any background processes
taskkill //F //IM DeadEditor.exe //T
taskkill //F //IM dotnet.exe //T

# Build
dotnet build DeadEditor.csproj

# Run
dotnet run --project DeadEditor.csproj
```

### Running Tests
Automated tests live in `DeadEditor.Tests/` (xUnit, .NET 8). Run from repo root:

```bash
dotnet test DeadEditor.sln
```

- The repo root contains the canonical `.sln`; do not create additional `.sln` files inside subdirectories.
- New regression tests for feature commits go in `DeadEditor.Tests/`, not in the main project.
- Tests should not require WPF runtime initialization. Reference plain models, services, and helpers from the main project; do not instantiate `Window`/`UserControl` types in test code.

### Testing with Real Data
- Primary test library: User's personal Grateful Dead taper collection
- Add songs/aliases as unmatched tracks are encountered
- Verify fuzzy matching against real-world typos
- Test both album types: AudienceRecording and OfficialRelease (the OfficialRelease type covers studio albums, official live releases, and box sets)

---

## Current Development Focus (2026-01-25)

### Multiple Official Release Editions
**Goal:** Support importing different editions of the same studio album as separate entities

**Problem:** Official releases often have multiple editions over time:
- Original release (e.g., "Workingman's Dead" 1970)
- Remastered edition (e.g., "Workingman's Dead" 2003)
- Deluxe/Anniversary edition (e.g., "Workingman's Dead" 2020 with bonus live tracks)

Each edition should be treated as a distinct album with its own metadata, even though they share the same base album name.

**MusicBrainz Integration:** Use `ReleaseSelectorDialog` to let user choose which specific release/edition they're importing, then treat each as separate in the library.

### Track-Level Search with Album Context (Related Feature)
**Goal:** Enable searching for specific performance dates within official releases

**Use Case:** If "Workingman's Dead 2003 Edition" includes live bonus tracks like "Morning Dew (1972-05-04)", searching for "1972-05-04" should find this track within the studio album context.

**Solution:**
1. Parse embedded dates from track titles in official releases
2. Show search results with album context (track name + containing album)
3. Open containing album folder when clicked
4. Preserve all existing search functionality

**Design Decision:** Import hybrid albums (studio + live bonus material) as Studio Albums with Edition field to keep them as one cohesive unit.

---

## Known Issues

### MusicBrainz Release Selector Not Appearing
**Status:** Dialog UI is implemented but never shows up during official release import

**Possible Causes:**
1. **fpcalc.exe missing** - Audio fingerprinting fails silently
   - MusicBrainzService searches for fpcalc.exe in: current dir, parent dirs, system PATH
   - Returns null if not found, causing lookup to fail
   - Check: Is fpcalc.exe in `D:\Projects\fpcalc.exe`?

2. **MusicBrainz API not returning multiple releases** - Only finds 1 release
   - Code auto-selects single release without showing dialog
   - May need to adjust filtering logic in `GetAllReleasesAsync()`
   - Current filter: Only includes "Official" status and "Album" type

3. **AcoustID API key issue** - Service initialization may be failing
   - Check how `_musicBrainzService` is initialized in MainWindow
   - Verify API key is valid

**Expected Behavior:**
- If 0 releases found → Error message
- If 1 release found → Auto-select (no dialog)
- If 2+ releases found → Show `ReleaseSelectorDialog`

**Debug Steps:**
1. Check console output for MusicBrainz logging (extensive Console.WriteLine statements exist)
2. Verify fpcalc.exe location
3. Test with known album that has multiple editions

### Minor UI Issue
- Main window sometimes opens in background at startup (requires Alt+Tab to bring forward)

---

## Future Enhancement Ideas

### High Priority
- Complete testing with both album types (AudienceRecording, OfficialRelease)

### Low Priority
- Batch import multiple concerts at once
- Export concert metadata to CSV/JSON
- Dark/light theme toggle
- Keyboard shortcuts for common actions
- Duplicate concert detection (same date/venue)
- Statistics dashboard (most played songs, venue counts, etc.)
- Integration with online databases (archive.org, etree.org)
- Automated backup/sync functionality

---

## Questions for AI Assistants Resuming Sessions

When resuming development:

1. **Check git status** - What files have been modified?
2. **Read TODO.md** - What's currently in progress?
3. **Review recent commits** - What was done in the last session?
4. **Ask about priorities** - What does the user want to focus on?

### Common Session Workflows

#### Import Testing
- User provides path to concert folder
- Import and verify song matching
- Add missing songs/aliases as needed
- Commit song database updates

#### Bug Fixing
- User reports unexpected behavior
- Reproduce issue
- Identify root cause
- Implement fix
- Test thoroughly

#### Feature Development
- Clarify requirements
- Review existing code
- Implement incrementally
- Test with real data
- Update TODO.md

---

## Useful Context for AI Assistants

### The "Grateful Dead Problem"
Grateful Dead concerts have unique challenges:
- 2,300+ concerts over 30 years
- Songs played in different arrangements
- Segues between songs are musically significant
- Multiple recordings of same show (different tapers)
- Extensive live bonus material in official releases
- Fan community has strong conventions (setlist formats, venue naming)

### Song Title Variations
Common patterns requiring normalization:
- Segue arrows: "->", ">", "→"
- Parenthetical info: "(Live at...)", "(1972-05-04)", "(Acoustic)"
- Track numbers: "01 ", "1-01 ", "d1t01 "
- Tape flip markers: "//", "/ /"
- Typos: "Dnacing", "Wkae", "Monkey &"
- Character encoding: "Peggy─O", "Peggy-O", "Peggy-o"

### Why Fuzzy Matching Matters
A typical taper collection has metadata from:
- Original taper notes (hand-typed)
- Archive.org uploads (crowd-sourced)
- CD rips with OCR errors
- Automated tools with bugs
- Multiple editors over decades

Without fuzzy matching, you'd need hundreds of aliases per song. With 2-character tolerance, most typos are automatically handled.

---

## Conventions for AI Assistants

### When Modifying Code

**CRITICAL:** Before modifying ANY file, read the relevant documentation from the [Documentation Handbook](#documentation-handbook) first.

**Code Standards:**
- Preserve existing patterns (MVVM-like with code-behind)
- Use `Newtonsoft.Json` for JSON operations (already in project)
- Follow nullable reference type conventions
- Avoid LINQ lazy evaluation in search logic (see bug fix notes)
- Use guard clauses to prevent recursion in event handlers
- **NEVER hardcode artist names** - Code must be artist-agnostic
  - Bad: `if (artist == "Grateful Dead")`
  - Good: Use `PrimaryArtistName` from settings or artist-agnostic logic

**Documentation-First Workflow:**
1. **Check [Documentation Lookup Table](#documentation-lookup-table)** - Find the relevant doc file
2. **Read the documentation** - Understand the current spec
3. **Update documentation FIRST** if changing behavior
4. **Write code to match the doc** - Code implements the spec
5. **Update doc if implementation reveals issues** - Keep docs in sync
6. **Commit code + docs together** - Never commit one without the other

### When Adding Features

**CRITICAL:** Follow the [Documentation-First Development Workflow](#documentation-first-development-workflow) for all new features.

**Feature Design:**
- Consider both Live and Official Release workflows
- Test with real concert data
- Update song database if new songs discovered
- Add to TODO.md with session notes
- Consider impact on existing imports (backward compatibility)
- Ensure feature works for any artist, not just Grateful Dead

**Documentation Requirements:**
- Document new methods in relevant doc file (signature, parameters, return value, business logic)
- Document new business rules and validation logic
- Include examples showing the new behavior
- Document error handling and edge cases
- Update related doc sections if feature affects existing behavior

### When Writing Commits
- Reference specific issues/features
- Include session date in TODO.md updates
- Note file counts and line changes for major work

---

## End of Document

**Last Updated:** 2026-03-28
**Architecture:** Single-window shell (Phase 6 complete — search/filter, keyboard shortcuts, UI polish)
**Song Database:** 598 songs (594 Grateful Dead, 4 NRPS)
**Documentation:** 15 comprehensive documentation files covering all windows, dialogs, services, and data models

## Development Environment
- OS: Windows 10.0.26200
- Shell: Git Bash
- Path format: Windows (use forward slashes in Git Bash)
- File system: Case-insensitive
- Line endings: CRLF (configure Git autocrlf)

## Playwright MCP Guide

File paths:
- Screenshots: `./CCimages/screenshots/`
- PDFs: `./CCimages/pdfs/`

Browser version fix:
- Error: "Executable doesn't exist at chromium-XXXX" → Version mismatch
- v1.0.12+ uses Playwright 1.57.0, requires chromium-1200 with `chrome-win64/` structure
- Quick fix: `npx playwright@latest install chromium`
- Manual symlink (if needed): `cd ~/AppData/Local/ms-playwright && cmd //c "mklink /J chromium-1200 chromium-1181"`
