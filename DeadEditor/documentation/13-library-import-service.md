# Service Documentation: LibraryImportService

## Purpose

The **LibraryImportService** implements the two-path library system, managing folder structure creation and file copying for three album types: **Live recordings** (organized by year/date), **Studio albums** (in "Studio Albums" folder), and **Official releases** (organized by series name). It handles file copying, metadata writing, progress reporting, duplicate detection, and filename sanitization while preserving original metadata fields (genre, comment, copyright, publisher, composer).

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
    string? officialReleasesPath = null)
```

**Purpose:** Copy audio files to organized library structure, write metadata.

**Parameters:**
- `libraryRoot` (string) - Base library path (audience recordings/studio albums)
- `albumInfo` (AlbumInfo) - Album metadata
- `tracks` (List<TrackInfo>) - Tracks to import
- `progress` (IProgress?) - Progress callback (current, total, message)
- `officialReleasesPath` (string?) - Separate path for official releases

**Folder Structures:**

**Live Recordings:**
```
{libraryRoot}/
  {Year}/
    {Date} - {Venue} - {City}, {State}/
      01 - Song Title (1972-05-04).flac
      02 - Next Song > (1972-05-04).flac
```

**Studio Albums:**
```
{libraryRoot}/
  Studio Albums/
    American Beauty (1970)/
      01 - Box of Rain.flac
      02 - Morning Dew (1972-05-04).flac  # Live bonus track
```

**Official Releases:**
```
{officialReleasesPath}/
  Dave's Picks/
    Dave's Picks Volume 28/
      01 - Song Title.flac
```

**Business Logic:**

1. **Validate library root** (line 23-31)
2. **Official Release handling** (line 37-64):
   - Extract series name: `ExtractSeriesName(albumInfo.OfficialRelease)`
   - Create folder: `{officialReleasesPath}/{Series}/{Release Name}`
   - Import all tracks (no date in filename)
3. **Studio Album handling** (line 66-85):
   - Folder: `{libraryRoot}/Studio Albums/{AlbumName} ({Year})`
   - Import all tracks
4. **Live Recording handling** (line 87-122):
   - Group tracks by performance date
   - For each date group:
     - Extract year from date
     - Build folder name: `{Date} - {Venue} - {City}, {State}`
     - Create: `{libraryRoot}/{Year}/{FolderName}`
   - Default to "Unknown Venue/City" if missing

**Error Handling:** Throws `ArgumentException` if paths not set, creates directories if missing.

---

### ShowExistsInLibrary

**Signature:**
```csharp
public bool ShowExistsInLibrary(string libraryRoot, AlbumInfo albumInfo,
    string? officialReleasesPath = null)
```

**Purpose:** Check if album already exists before import (duplicate detection).

**Return Value:** `true` if folder exists, `false` otherwise

**Detection Logic:**
- **Official Release:** Check `{officialReleasesPath}/{Series}/{Release}*` wildcard match
- **Studio Album:** Check exact folder `{libraryRoot}/Studio Albums/{AlbumName} ({Year})`
- **Live Recording:** Check `{libraryRoot}/{Year}/{Date}*` wildcard match

**Business Rule:** Uses wildcard matching for live/official (allows venue variations).

---

## Private Helper Methods

### ExtractSeriesName

**Purpose:** Extract series name from full official release name.

**Regex Patterns (priority order):**
1. `^(Dave's Picks)` → "Dave's Picks"
2. `^(Dick's Picks)` → "Dick's Picks"
3. `^(Road Trips)` → "Road Trips"
4. `^(Download Series)` → "Download Series"
5. `^(Spring \d{4})` → "Spring 1990"
6. `^(Here Comes Sunshine)` → "Here Comes Sunshine"

**Fallback:** Extract text before "Vol", "Volume", or "No." (line 351-359)
**Ultimate Fallback:** Return full string (line 362)

**Examples:**
- "Dave's Picks Volume 28" → "Dave's Picks"
- "Road Trips Vol. 3 No. 4" → "Road Trips"

---

### SanitizeFolderName / SanitizeFileName

**Purpose:** Remove invalid Windows path/filename characters.

**Processing:**
1. Replace invalid chars with underscore: `<>:"|?*` etc. (line 371-375, 396-400)
2. Collapse multiple spaces: `\s+` → single space (line 378, 403)
3. Trim periods and spaces (line 381, 406)
4. Return "Unknown" if empty (line 383, 409)

**Difference:**
- `SanitizeFolderName`: Uses `Path.GetInvalidPathChars()`
- `SanitizeFileName`: Uses `Path.GetInvalidFileNameChars()`, preserves extension

---

### ImportTracksToFolder

**Purpose:** Copy tracks to specific folder, write metadata.

**Filename Formats:**
- **Studio Album:** `{TrackNum:D2} - {Title}.flac`
- **Official Release:** `{TrackNum:D2} - {Title}.flac`
- **Live Recording:** `{TrackNum:D2} - {Title} ({Date}).flac`

**Metadata Preservation (line 174-195):**
Reads original file's:
- Genre, Comment, Copyright, Publisher, Composer
Writes back after updating core fields (line 262-281)

**Title Writing Logic (line 214-217):**
All album types now use centralized `BuildFinalTitle()` method:
- Resolves date: `dateForTitle` param → `track.TrackDate` → `albumInfo.Date`
- Calls `BuildFinalTitle(songName, hasSegue, trackDate, albumDate)`
- Returns final title with date suffix and segue marker in correct order
- Includes double-date prevention (see BuildFinalTitle section below)

**Progress Reporting:** Updates per-track: `(current, total, "Importing track N of M: Title")` (line 138)

**Error Handling:** Try/catch on original metadata read (line 181-195), continues if fails.

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

**1. Date Resolution (line 509):**
```csharp
var finalDate = !string.IsNullOrEmpty(trackDate) ? trackDate : albumDate;
```
- trackDate wins if non-empty
- Falls back to albumDate
- May be null/empty if both are empty

**2. Double-Date Prevention (lines 514-541):**
Checks if `songName` already contains date suffix `(yyyy-MM-dd)`:

**Case A: Embedded date MATCHES finalDate**
- Use songName as-is (don't append duplicate date)
- Add segue marker if needed (before the existing date)
- Example: `"Song (1978-02-01)"` + finalDate `"1978-02-01"` → `"Song (1978-02-01)"` (no change)
- Example with segue: `"Song (1978-02-01)"` + segue → `"Song > (1978-02-01)"`

**Case B: Embedded date DIFFERS from finalDate**
- **Data integrity issue** - log warning to Debug output
- Strip embedded date from songName
- Proceed to append finalDate (trackDate/albumDate wins)
- Example: `"Song (1970-01-01)"` + finalDate `"1978-02-01"` → `"Song (1978-02-01)"`
- **Rationale:** Explicit TrackDate/AlbumDate from import workflow is authoritative

**Case C: No embedded date**
- Proceed to append finalDate normally

**3. Segue Marker Placement (lines 543-547):**
```csharp
if (hasSegue && !title.EndsWith(">"))
{
    title = title.TrimEnd() + " >";
}
```
- **Critical:** Segue marker MUST come BEFORE date suffix
- Format: `"Song Name > (yyyy-MM-dd)"`

**4. Date Suffix Append (lines 549-553):**
```csharp
if (!string.IsNullOrEmpty(finalDate))
{
    title = $"{title} ({finalDate})";
}
```
- Only appends if finalDate is non-empty
- If both trackDate and albumDate are empty, returns song name without date

**Output Examples:**

| Input | Output |
|-------|--------|
| `songName = "Bertha"`, `hasSegue = false`, `finalDate = "1978-02-01"` | `"Bertha (1978-02-01)"` |
| `songName = "China Cat Sunflower"`, `hasSegue = true`, `finalDate = "1977-05-08"` | `"China Cat Sunflower > (1977-05-08)"` |
| `songName = "Bertha"`, `hasSegue = false`, `finalDate = null` | `"Bertha"` (no date) |
| `songName = "Bertha (1978-02-01)"`, `hasSegue = false`, `finalDate = "1978-02-01"` | `"Bertha (1978-02-01)"` (no duplicate) |
| `songName = "Bertha (1970-01-01)"`, `hasSegue = false`, `finalDate = "1978-02-01"` | `"Bertha (1978-02-01)"` (corrected) |
| `songName = "China Cat (1977-05-08)"`, `hasSegue = true`, `finalDate = "1977-05-08"` | `"China Cat > (1977-05-08)"` (segue added) |

**Error Handling:**
- **Missing dates:** Returns song name without date suffix (graceful degradation)
- **Date mismatch:** Logs warning, uses finalDate (explicit value wins)
- **Null songName:** Treats as empty string

**Critical Invariant:**
**The TITLE tag written to disk MUST always follow the format: `"Song Name > (yyyy-MM-dd)"` for segue tracks or `"Song Name (yyyy-MM-dd)"` for non-segue tracks. This is the core requirement for display in car audio systems (Apple Music/Plex).**

---

## Business Rules

### 1. Two-Path System
- **Library Root:** Audience recordings + studio albums
- **Official Releases Path:** Separate path for official release series
- **Rule:** User can configure same or different paths

### 2. Year Folder Organization (Live)
- **Structure:** `{libraryRoot}/{YYYY}/{Date - Venue - City, State}/`
- **Rationale:** Scalability (thousands of concerts organized chronologically)

### 3. Series Folder Organization (Official)
- **Structure:** `{officialReleasesPath}/{Series Name}/{Release Name}/`
- **Example:** `D:/Official/Dave's Picks/Dave's Picks Volume 28/`
- **Rationale:** Group related releases (all Dave's Picks together)

### 4. Studio Albums Flat Structure
- **Structure:** `{libraryRoot}/Studio Albums/{Album} ({Year})/`
- **Rationale:** Limited number of studio albums, no deep nesting needed

### 5. Date in Filename (Live Only)
- **Live:** Filename includes date: `01 - Song (1972-05-04).flac`
- **Studio/Official:** No date in filename: `01 - Song.flac`
- **Rationale:** Live tracks need date context, studio tracks don't

### 6. Metadata Field Preservation
- **Preserved:** Genre, Comment, Copyright, Publisher, Composer
- **Overwritten:** Title, Album, Performers, AlbumArtists, Track, Disc, Year, Pictures
- **Rationale:** Preserve original metadata not managed by DeadEditor

### 7. Segue Marker in Filename and Tag
- **Filename:** `01 - Song Title >.flac`
- **ID3 Tag:** `Song Title > (Date)`
- **Placement:** Before date suffix in tag, at end of filename

### 8. Unknown Venue/City Fallback
- **Rule:** If venue/city empty, use "Unknown Venue" / "Unknown City"
- **Prevents:** Empty folder names like "1972-05-04 - - "

### 9. Wildcard Duplicate Detection
- **Live/Official:** Use wildcard matching (`{Date}*`)
- **Studio:** Exact folder name match
- **Rationale:** Live recordings may have varying venue spellings

### 10. File Overwrite Policy
- **Rule:** `File.Copy(..., overwrite: true)` (line 172)
- **Behavior:** Silently overwrites existing files
- **Rationale:** Re-import should update files with new metadata

### 11. Multi-Folder Album Grouping
- **Rule:** Multiple folders with the same ALBUM tag are grouped into a single library entry
- **Implementation:** LibraryBrowserWindow.GroupMultiFolderAlbums() (line 532-603)
- **Identity Key:** Album tag value + AlbumType (groups within same type only)
- **Behavior:**
  - Folders with matching Album tags → Merged into ONE LibraryShow
  - `FolderPaths` property stores ALL folder paths (List<string>)
  - `TrackCount` = sum of all folders' track counts
  - `ContainsDates` and `ContainsVenues` aggregated from all folders
  - Track loading reads from ALL folders, sorted by Disc/Track number
- **Example:**
  - Import 3 folders: `1971-04-25`, `1971-04-26`, `1971-04-27`
  - All have Album tag = "Enjoying the Ride"
  - Result: ONE library entry with 3 folder paths, combined track count
- **Rationale:** Multi-night concerts often span multiple folders but should appear as single album

---

## Critical File Operations

### File Copying
```csharp
File.Copy(track.FilePath, targetPath, overwrite: true);  // Line 172
```

**Source:** Original folder (not modified)
**Destination:** Library organized structure
**Overwrite:** Yes (re-imports replace existing)

### Metadata Writing Sequence
1. Copy file to destination
2. Read original file's preserved fields
3. Temporarily change `track.FilePath` to destination (line 199)
4. Open destination file with TagLib-Sharp
5. Write all metadata (core + preserved fields)
6. Save file
7. Restore `track.FilePath` to original (line 307)

**Critical:** Path restoration in `finally` block ensures no state corruption (line 304-308).

---

## Error Scenarios

| Scenario | Behavior |
|----------|----------|
| Library root not set | Throw `ArgumentException` (line 25) |
| Library root doesn't exist | Create directory (line 30) |
| Official releases path not set (for official album) | Throw `ArgumentException` (line 41) |
| Source file locked/missing | `File.Copy()` throws (unhandled) |
| Destination file locked | `File.Copy()` throws (unhandled) |
| Disk full | IOException thrown (unhandled) |
| Invalid folder name chars | Sanitized to underscores |
| Original metadata unreadable | Continue without preserved fields (line 192-195) |
| Metadata write fails | TagLib exception (unhandled) |

**Gracefully Handled:**
- Missing directories (created automatically)
- Invalid path characters (sanitized)
- Original metadata read errors (continue without)

**Unhandled:**
- File copy errors (locked files, permissions, disk full)
- Metadata write errors

---

## File Path

**Source:** [Services/LibraryImportService.cs](Services/LibraryImportService.cs)
**Lines of Code:** 483

---

## Related Documentation

- [11-metadata-service.md](documentation/11-metadata-service.md) - Used for track metadata
- [01-main-window.md](documentation/01-main-window.md) - Calls ImportToLibrary
- [02-library-browser.md](documentation/02-library-browser.md) - Displays imported library

---

**Last Updated:** 2026-03-01
**Status:** Complete service documentation
