# Service Documentation: LibraryImportService

## Purpose

The **LibraryImportService** implements a universal single-path library system with a flat artist/album folder structure. All album types (audience recordings, official releases, studio albums) use the same folder convention:

```
{LibraryRoot}/{Artist}/{Artist} - {Date} - {Venue} - {City}, {State} - {AlbumName}/
```

It handles file copying, metadata writing, progress reporting, duplicate detection, and filename sanitization while preserving original metadata fields (genre, comment, copyright, publisher, composer).

---

## Migration Note (Phase 4)

Libraries created before Phase 4 used a different folder structure:
- Audience recordings: `{LibraryRoot}/{Year}/{Date} - {Venue} - {City}, {State}/`
- Official releases: `{OfficialReleasesPath}/{Series}/{Release Name}/`

Phase 4 replaced this with a universal convention:
- All albums: `{LibraryRoot}/{Artist}/{Artist} - {Date} - {Venue} - {City}, {State} - {AlbumName}/`

**Old libraries MUST be cleared and re-imported.** The library scanner expects
`{LibraryRoot}/{Artist}/*` — old year-subfolder structures will be
misinterpreted (e.g., "1972" parsed as an artist name).

---

## Dependencies

- **MetadataService** - Track metadata writing
- **TagLib-Sharp** - ID3 tag preservation
- **System.IO** - File/directory operations

---

## Public Methods

### ImportToLibrary

**Signature:**
```csharp
public void ImportToLibrary(string libraryRoot, AlbumInfo albumInfo, List<TrackInfo> tracks,
    IProgress<(int current, int total, string message)>? progress = null,
    string? sourceFolderPath = null,
    ConflictPromptCallback? onConflict = null)
```

**Purpose:** Copy audio files to organized library structure, write metadata, and optionally copy non-audio source files.

**Parameters:**
- `libraryRoot` (string) - Single library root path for all album types
- `albumInfo` (AlbumInfo) - Album metadata
- `tracks` (List<TrackInfo>) - Tracks to import
- `progress` (IProgress?) - Progress callback (current, total, message)
- `sourceFolderPath` (string?) - Source folder path for non-audio file copy; when non-null, all non-audio files from this folder (recursively) are copied to the managed folder (flattened), skipping OS junk files
- `onConflict` (ConflictPromptCallback?) - Callback for per-file conflict prompts; when null, falls back to overwrite behavior

**Universal Folder Structure:**

All album types use the same base structure:
```
{libraryRoot}/
  {Artist}/
    {BuildLibraryFolderName()}/
      01 - Song Title (yyyy-MM-dd).flac
```

**Live Recordings (audience or official release with date+venue):**
```
{libraryRoot}/
  Grateful Dead/
    Grateful Dead - 1972-05-25 - Lyceum Ballroom - London, GB - sbd.miller.87682.sbeok.flac16/
      01 - Song Title.flac
```

**Studio Albums (official release without date+venue):**
```
{libraryRoot}/
  Grateful Dead/
    Grateful Dead - 1970 - American Beauty/
      01 - Box of Rain.flac
```

**Business Logic:**

1. **Validate library root** — throws `ArgumentException` if empty, creates directory if missing
2. **Build universal folder path:**
   - Artist folder: `SanitizeFolderName(albumInfo.Artist ?? "Unknown Artist")`
   - Album folder: `BuildLibraryFolderName(albumInfo)`
   - Destination: `{libraryRoot}/{artistFolder}/{albumFolder}`
3. **Determine filename format** — official/studio releases omit date in filename; audience recordings include date
4. **Import all tracks** to the single destination folder

**Error Handling:** Throws `ArgumentException` if library root not set, creates directories if missing.

---

### ShowExistsInLibrary

**Signature:**
```csharp
public bool ShowExistsInLibrary(string libraryRoot, AlbumInfo albumInfo)
```

**Purpose:** Check if album already exists before import (duplicate detection).

**Return Value:** `true` if folder exists, `false` otherwise

**Detection Logic:**
- Build expected folder path: `{libraryRoot}/{artistFolder}/{BuildLibraryFolderName()}`
- Check if that exact folder exists
- The full folder name IS the unique identity key — two albums with the same date but different album names are NOT duplicates

---

### BuildLibraryFolderName

**Signature:**
```csharp
public string BuildLibraryFolderName(AlbumInfo albumInfo)
```

**Purpose:** Compose the universal library folder name from AlbumInfo fields.

**Logic:**
1. Start with Artist (always present, falls back to "Unknown Artist")
2. **Studio albums** (`OfficialRelease` type with no date or no venue):
   - Pattern: `{Artist} - {Year} - {AlbumName}`
   - Omit Year segment if empty
   - Omit AlbumName segment if empty
3. **Live recordings** (all other types):
   - Pattern: `{Artist} - {Date} - {Venue} - {City}, {State} - {AlbumName}`
   - Each segment is omitted if empty (not replaced with fallback text)
4. Join segments with " - " separator
5. Run through `SanitizeFolderName()`

**Examples:**

| AlbumInfo | Folder Name |
|-----------|-------------|
| GD, 1972-05-25, Lyceum Ballroom, London GB, sbd.miller | `Grateful Dead - 1972-05-25 - Lyceum Ballroom - London, GB - sbd.miller.87682.sbeok.flac16` |
| GD, 1972-05-25, Lyceum Ballroom, London GB, Dave's Picks Vol. 50 | `Grateful Dead - 1972-05-25 - Lyceum Ballroom - London, GB - Dave's Picks Vol. 50` |
| GD, studio, year=1970, American Beauty | `Grateful Dead - 1970 - American Beauty` |
| GD, 1977-05-08, Barton Hall, Ithaca NY, (no album name) | `Grateful Dead - 1977-05-08 - Barton Hall - Ithaca, NY` |

---

## Private Helper Methods

### SanitizeFolderName / SanitizeFileName

**Purpose:** Remove invalid Windows path/filename characters.

**Processing:**
1. Replace colon with " -" for readability (e.g., "Listen to the River: St. Louis" → "Listen to the River - St. Louis")
2. Replace remaining invalid filename chars with underscore: `< > " | ? *` etc.
3. Collapse multiple spaces: `\s+` → single space
4. Trim periods and spaces
5. Return "Unknown" if empty

**Difference:**
- `SanitizeFolderName`: Uses `Path.GetInvalidFileNameChars()` (includes all restricted chars)
- `SanitizeFileName`: Uses `Path.GetInvalidFileNameChars()`, preserves extension

---

### ImportTracksToFolder

**Purpose:** Copy tracks to specific folder, write metadata.

**Filename Formats:**
- **Studio Album / Official Release:** `{TrackNum:D2} - {Title}.flac`
- **Live Recording:** `{TrackNum:D2} - {Title} ({Date}).flac`

**Metadata Preservation:**
Reads original file's:
- Genre, Comment, Copyright, Publisher, Composer
Writes back after updating core fields.

**Title Writing Logic:**
All album types use centralized `BuildFinalTitle()` method:
- Resolves date: `dateForTitle` param → `track.TrackDate` → `albumInfo.Date`
- Calls `BuildFinalTitle(songName, hasSegue, trackDate, albumDate)`
- Returns final title with date suffix and segue marker in correct order
- Includes double-date prevention (see BuildFinalTitle section below)

**Custom FLAC/MP3 Tags Written:**
- `ALBUMDATE` — album-level date
- `VENUE` — venue name
- `CITYSTATE` — city, state combined
- `ALBUMNAME` — album/release name
- `ALBUMTYPE` — "AudienceRecording" or "OfficialRelease"

These custom tags are the source of truth for library loading (overriding folder-name parsing).

**Progress Reporting:** Updates per-track: `(current, total, "Importing track N of M: Title")`

**Error Handling:** Try/catch on original metadata read, continues if fails. Retry logic (3 attempts, 500ms delay) for file copy and metadata write to handle transient Windows file locks.

---

### BuildFinalTitle

**Signature:**
```csharp
private string BuildFinalTitle(string songName, bool hasSegue, string? trackDate, string? albumDate)
```

**Purpose:** Builds the final title string to write to TITLE tag with date suffix and double-date prevention. This is the **core invariant** for all metadata writing in the import process.

**Parameters:**
- `songName` (string) - Song name without date/segue markers
- `hasSegue` (bool) - Whether track has segue marker (`>`)
- `trackDate` (string?) - Track-specific date (TrackInfo.TrackDate)
- `albumDate` (string?) - Album-level date fallback (AlbumInfo.AlbumDate)

**Return Value:** Final title string to write to TITLE tag

**Business Logic:**

**1. Date Resolution:**
```csharp
var finalDate = !string.IsNullOrEmpty(trackDate) ? trackDate : albumDate;
```
- trackDate wins if non-empty
- Falls back to albumDate
- May be null/empty if both are empty

**2. Double-Date Prevention:**
Checks if `songName` already contains date suffix `(yyyy-MM-dd)`:

**Case A: Embedded date MATCHES finalDate**
- Use songName as-is (don't append duplicate date)
- Add segue marker if needed (before the existing date)

**Case B: Embedded date DIFFERS from finalDate**
- Log warning, strip embedded date, append finalDate (explicit value wins)

**Case C: No embedded date**
- Proceed to append finalDate normally

**3. Segue Marker Placement:**
- **Critical:** Segue marker MUST come BEFORE date suffix
- Format: `"Song Name > (yyyy-MM-dd)"`

**4. Date Suffix Append:**
- Only appends if finalDate is non-empty

**Output Examples:**

| Input | Output |
|-------|--------|
| `"Bertha"`, no segue, date `"1978-02-01"` | `"Bertha (1978-02-01)"` |
| `"China Cat Sunflower"`, segue, date `"1977-05-08"` | `"China Cat Sunflower > (1977-05-08)"` |
| `"Bertha"`, no segue, no date | `"Bertha"` |
| `"Bertha (1978-02-01)"`, no segue, date `"1978-02-01"` | `"Bertha (1978-02-01)"` (no duplicate) |
| `"Bertha (1970-01-01)"`, no segue, date `"1978-02-01"` | `"Bertha (1978-02-01)"` (corrected) |

**Critical Invariant:**
**The TITLE tag written to disk MUST always follow the format: `"Song Name > (yyyy-MM-dd)"` for segue tracks or `"Song Name (yyyy-MM-dd)"` for non-segue tracks. This is the core requirement for display in car audio systems (Apple Music/Plex).**

---

## Business Rules

### 1. Universal Single-Path System
- **Single Library Root:** All album types stored under one configurable path
- **Structure:** `{LibraryRoot}/{Artist}/{AlbumFolder}/`
- **No separate paths:** The former `OfficialReleasesPath` setting has been removed

### 2. Artist Folder Organization
- **Structure:** `{LibraryRoot}/{Artist}/` contains all albums for that artist
- **Fallback:** "Unknown Artist" if artist is empty/null

### 3. Universal Folder Naming
- **Live recordings:** `{Artist} - {Date} - {Venue} - {City}, {State} - {AlbumName}`
- **Studio albums:** `{Artist} - {Year} - {AlbumName}`
- **Segments omitted** when empty (no "Unknown Venue" fallbacks in folder names)
- **Sanitized** for Windows filesystem restrictions

### 4. Date in Filename (Live Only)
- **Live:** Filename includes date: `01 - Song (1972-05-04).flac`
- **Studio/Official:** No date in filename: `01 - Song.flac`
- **Rationale:** Live tracks need date context, studio tracks don't

### 5. Metadata Field Preservation
- **Preserved:** Genre, Comment, Copyright, Publisher, Composer
- **Overwritten:** Title, Album, Performers, AlbumArtists, Track, Disc, Year, Pictures
- **Rationale:** Preserve original metadata not managed by DeadEditor

### 6. Segue Marker in Filename and Tag
- **Filename:** `01 - Song Title >.flac`
- **ID3 Tag:** `Song Title > (Date)`
- **Placement:** Before date suffix in tag, at end of filename

### 7. Exact Duplicate Detection
- **Method:** Build expected folder name, check if it exists
- **Identity key:** The full folder name (artist + date + venue + album name)
- **Same date, different albums:** NOT duplicates (separate folders)

### 8. File Overwrite Policy
- **Audio files:** `File.Copy(..., overwrite: true)` with retry logic during import (3 retries, 500ms delay)
- **Non-audio files (source folder preservation):** Per-file conflict prompt via `ConflictPromptCallback` when target exists. Options: Overwrite / Skip / Rename / Cancel Import, with "Apply to all remaining" checkbox (session-scoped).
- **Fallback:** When no conflict callback provided, silently overwrites (legacy behavior)
- **Skiplist:** `Thumbs.db`, `.DS_Store`, `desktop.ini`, `*.lnk` (case-insensitive) are never copied

### 9. Multi-Folder Album Grouping
- **Rule:** Multiple folders with the same ALBUMNAME tag are grouped into a single library entry
- **Implementation:** `MergeOfficialReleasesByAlbumName()` in LibraryGridView
- **Identity Key:** ALBUMNAME tag value (case-insensitive)
- **Behavior:**
  - Folders with matching ALBUMNAME tags → Merged into ONE LibraryShow
  - `FolderPaths` property stores ALL folder paths
  - `TrackCount` = sum of all folders' track counts

---

## Critical File Operations

### File Copying
```csharp
CopyFileWithRetry(track.FilePath, targetPath);  // 3 retries, 500ms delay
```

**Source:** Original folder (not modified)
**Destination:** Library organized structure
**Overwrite:** Yes (re-imports replace existing)

### Metadata Writing Sequence
1. Copy file to destination (with retry)
2. Read original file's preserved fields
3. Temporarily change `track.FilePath` to destination
4. Open destination file with TagLib-Sharp (with retry)
5. Write all metadata (core + preserved + custom fields)
6. Save file
7. Restore `track.FilePath` to original in `finally` block

---

## Error Scenarios

| Scenario | Behavior |
|----------|----------|
| Library root not set | Throw `ArgumentException` |
| Library root doesn't exist | Create directory |
| Source file locked/missing | `File.Copy()` retries 3x, then throws |
| Destination file locked | `File.Copy()` retries 3x, then throws |
| Disk full | IOException thrown (unhandled) |
| Invalid folder name chars | Sanitized to underscores |
| Original metadata unreadable | Continue without preserved fields |
| Metadata write fails | Retries 3x, then TagLib exception thrown |

---

## Future: Manifest Generation

Manifest sidecar JSON files will be generated on import and save to capture
verified metadata state. See [19-folder-import-and-manifests.md](19-folder-import-and-manifests.md)
for the full spec. Not yet implemented.

---

## File Path

**Source:** [Services/LibraryImportService.cs](Services/LibraryImportService.cs)

---

## Related Documentation

- [11-metadata-service.md](documentation/11-metadata-service.md) - Used for track metadata
- [01-main-window.md](documentation/01-main-window.md) - Calls ImportToLibrary
- [02-library-browser.md](documentation/02-library-browser.md) - Displays imported library
- [19-folder-import-and-manifests.md](19-folder-import-and-manifests.md) - Folder import spec and manifest design

---

**Last Updated:** 2026-04-14
**Status:** Complete service documentation (updated for universal single-path folder convention)
