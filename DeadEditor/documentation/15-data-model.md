# Data Model Documentation

## Purpose

This document provides comprehensive coverage of DeadEditor's data model layer, including all model classes, their properties, business logic, JSON schemas, and real-world examples. The data model supports two album types (`AudienceRecording`, `OfficialRelease`) and exposes all album-info fields on a single unified shape; the `AlbumTitle` computed property adapts based on which fields are populated.

---

## Table of Contents

1. [AlbumInfo.cs](#albuminfocs) - Concert/album metadata model
2. [TrackInfo.cs](#trackinfocs) - Individual track metadata model
3. [LibrarySettings.cs](#librarysettingscs) - User settings/preferences
4. [SongDatabase.cs](#songdatabasecs) - Song database structure
5. [songs.json Schema](#songsjson-schema) - Song database JSON format
6. [settings.json Schema](#settingsjson-schema) - Settings file JSON format
7. [Real-World Examples](#real-world-examples) - Actual data from imported concerts

---

## AlbumInfo.cs

**File:** [Models/AlbumInfo.cs](Models/AlbumInfo.cs)
**Lines:** 88

### Purpose

The `AlbumInfo` class represents metadata for a concert, studio album, official release, or box set. All fields are present on every instance regardless of `Type`; field visibility in the UI is no longer type-gated. The `AlbumTitle` computed property adapts its format based on which fields are populated.

---

### AlbumType Enum

**Definition:**
```csharp
public enum AlbumType
{
    AudienceRecording,  // Audience/taper recordings (was named Live)
    OfficialRelease     // Official releases (studio albums, live albums, box sets, series)
}
```

**Values:**

| Value | Description | Folder Location |
|-------|-------------|-----------------|
| `AudienceRecording` | Audience/taper/soundboard recording of a single concert | `{LibraryRoot}/{Artist}/{AlbumFolder}/` |
| `OfficialRelease` | Any official release — studio albums, official live releases (Dave's Picks, Road Trips, etc.), and box sets | `{LibraryRoot}/{Artist}/{AlbumFolder}/` |

All album types use the same universal folder layout — see [13-library-import-service.md](13-library-import-service.md) for how `AlbumFolder` is composed.

**Default:** `AlbumType.AudienceRecording`.

---

### Properties

All fields are present on every `AlbumInfo` instance regardless of `Type`. Whether a field is populated (vs. empty) depends on the import path and what the user enters.

| Property | Type | Description | Example |
|----------|------|-------------|---------|
| `FolderPath` | `string` | Full path to album folder | `"D:\library\Grateful Dead\Grateful Dead - 1977-05-08 - Barton Hall - Ithaca, NY"` |
| `Artist` | `string` | Artist name | `"Grateful Dead"` |
| `Type` | `AlbumType` | Album type (`AudienceRecording` or `OfficialRelease`) | `AlbumType.AudienceRecording` (default) |
| `AlbumDate` | `string` | Performance/release date (yyyy-MM-dd); may be empty for studio-style releases | `"1977-05-08"` |
| `Venue` | `string` | Venue name; may be empty for studio-style releases | `"Barton Hall, Cornell University"` |
| `CityState` | `string` | "City, State" combined | `"Ithaca, NY"` |
| `AlbumName` | `string` | Album or official-release name | `"Workingman's Dead"`, `"Dave's Picks Vol. 29"` |
| `CollectionName` | `string` | Optional collection/box-set name; applies to either type | `"Enjoying the Ride"` |
| `Year` | `string` | Release year (string; may be empty) | `"1970"` |
| `Edition` | `string` | Optional edition/remaster info | `"2025 Remaster"`, `"Deluxe Edition"` |
| `FolderNameOverride` | `bool` | True when user manually edited the folder-name preview | `false` |
| `CustomFolderName` | `string` | User's manual folder-name override (active when `FolderNameOverride` is true) | `"Custom Folder"` |
| `IsModified` | `bool` | Has user made changes? | `false` |
| `ArtworkData` | `byte[]?` | Album artwork image bytes | `[JPEG/PNG binary data]` |
| `ArtworkMimeType` | `string?` | MIME type of artwork | `"image/jpeg"` or `"image/png"` |
| `InfoFileContent` | `string?` | Content of .txt info file | `"Source: AUD Charlie Miller\nLineage: ..."` |
| `InfoFileName` | `string?` | Name of info file | `"gd77-05-08.txt"` |
| `MusicBrainzReleaseId` | `string?` | MBID read from the `MUSICBRAINZ_ALBUMID` tag at library scan. **Inert orphan field — see note below.** | `"abcd-1234-…"` |

> **`MusicBrainzReleaseId` is a deliberately-preserved inert orphan (MusicBrainz-removal arc, 2026-06).** MusicBrainz was removed from DeadEditor; there is **no acquisition path** left that can newly populate this field or the `MUSICBRAINZ_ALBUMID` tag (the lookup, apply, MBID-migration, and manual-entry surfaces are all gone). Under **orphan policy (a) — leave the tag in place**, the field and tag are intentionally kept, not stripped:
> - **Read:** `LibraryGridView` reads the existing `MUSICBRAINZ_ALBUMID` tag into `LibraryShow.MusicBrainzReleaseId` at scan.
> - **Preserve-on-write:** `EditMetadataView.WriteMbidToTracks`, `LibraryImportService`, and `MetadataService.WriteMetadata` re-emit the value only when it is already non-empty (`if (IsNullOrEmpty) …` guards) — they never originate one. So an existing tag round-trips harmlessly through a save; a file without one stays without one.
> - **Not displayed as a curated field:** the Edit-sidebar MBID display was removed with the MusicBrainz UI. `TrackInfoDialog` still lists `Release MBID` as one row of its **raw on-disk tag dump** (alongside the fingerprint) — that is a diagnostic read-out of disk state, not an actionable field.
> - **Pinned by tests:** `MetadataServiceMbidTests` and `LibraryImportServiceMbidTests` assert the supplied-write and null-preserve round-trip — the guarantee that policy (a) holds.
>
> The dormant field is intentional; do not "clean it up" as dead code.

**Compatibility aliases.** `Date`, `City`, `State`, `ReleaseYear`, `OfficialRelease`, and `BoxSetName` survive as read/write aliases on `AlbumInfo` and all map onto the unified fields above (e.g., `BoxSetName` is an alias for `AlbumName`, `Date` for `AlbumDate`, `City`/`State` parse `CityState`). New code should use the unified field names directly.

---

### Computed Properties

#### AlbumTitle (Read-Only)

**Property:**
```csharp
public string AlbumTitle { get; }
```

**Purpose:** Returns the formatted album title based on which fields are populated. Source of truth: [Models/AlbumInfo.cs:105-167](Models/AlbumInfo.cs#L105-L167).

**Business Logic:**

**1. Folder-name override (any type).** If `FolderNameOverride == true` and `CustomFolderName` is non-empty, return `CustomFolderName` verbatim.

**2. `OfficialRelease` with Date + Venue (live flavor).**
```
"{AlbumDate} - {Venue} - {CityState}[ - {AlbumName}][ : {CollectionName}]"
```
Examples:
- `AlbumDate="1977-05-08"`, `Venue="Barton Hall"`, `CityState="Ithaca, NY"`, `AlbumName="Dave's Picks Vol. 29"` → `"1977-05-08 - Barton Hall - Ithaca, NY - Dave's Picks Vol. 29"`
- Same + `CollectionName="Enjoying the Ride"` → `"1977-05-08 - Barton Hall - Ithaca, NY - Dave's Picks Vol. 29 : Enjoying the Ride"`

**3. `OfficialRelease` without Date + Venue (studio flavor).**
```
"{AlbumName} ({Year})[ : {CollectionName}]"
```
The `(Year)` suffix is omitted when `AlbumName` already contains a parenthesized year (so `"Europe '72 (2003 Reissue)"` does not become `"Europe '72 (2003 Reissue) (1972)"`).

Examples:
- `AlbumName="Workingman's Dead"`, `Year="1970"` → `"Workingman's Dead (1970)"`
- `AlbumName="Europe '72 (2003 Reissue)"`, `Year="1972"` → `"Europe '72 (2003 Reissue)"`
- `AlbumName=""` → `"Unknown Release"`

**4. `AudienceRecording` with Date + Venue.**
```
"{AlbumDate} - {Venue} - {CityState}[ - {AlbumName}][ : {CollectionName}]"
```
Same shape as the live-flavor `OfficialRelease`. Example:
- `AlbumDate="1977-05-08"`, `Venue="Barton Hall"`, `CityState="Ithaca, NY"` → `"1977-05-08 - Barton Hall - Ithaca, NY"`

**5. `AudienceRecording` fallback (no Date, no Venue).** Falls back to the studio-flavor `"{AlbumName} ({Year})[ : {CollectionName}]"` shape. This handles folders parsed before the user assigns a meaningful `Type`.

**Colon spacing note:** The legacy " : " vs ":" colon distinction is no longer used by `AlbumTitle` to encode album type. The colon between `AlbumName` and `CollectionName` is " : " (space before colon) in every case. The legacy "no-space colon = box set" / "space-colon = official release" rule survives only inside `MetadataService.ParseAlbumTitle` as two regex parse paths; see [11-metadata-service.md](11-metadata-service.md) for details.

---

#### IsOfficialRelease (Read-Only)

**Property:**
```csharp
public bool IsOfficialRelease => Type == AlbumType.OfficialRelease;
```

**Purpose:** Helper property to check whether this album is the `OfficialRelease` flavor.

**Used For:** UI logic, import-workflow branching.

---

### Type-Based Property Usage

The current model does **not** gate property availability by `Type`. Every field listed in the Properties table above is settable and readable for both `AudienceRecording` and `OfficialRelease` instances. Different fields tend to be populated for different flavors (e.g., studio-style `OfficialRelease` instances usually have empty `Venue`/`CityState`/`AlbumDate`), but this is a data convention — not enforced by the model.

(Earlier versions of the model used a four-value `AlbumType` with per-type field-visibility rules. That model was retired when the enum was collapsed to two values; see [16-import-redesign-spec.md](16-import-redesign-spec.md) for the redesign context.)

---

### Business Rules

1. **Field Population by Flavor**
   - `OfficialRelease` instances populated as live-flavor: have `AlbumDate`, `Venue`, `CityState`
   - `OfficialRelease` instances populated as studio-flavor: have `AlbumName` and (optionally) `Year`; `AlbumDate`/`Venue` are typically empty
   - `AudienceRecording` instances: have `AlbumDate`, `Venue`, `CityState`; `AlbumName`/`CollectionName` optional

2. **Date Format Enforcement**
   - `AlbumDate` MUST use yyyy-MM-dd (ISO 8601) format
   - No MM/dd/yyyy or dd/MM/yyyy formats allowed
   - Enforced by import workflow and metadata service

3. **Edition Field Usage**
   - `Edition` field distinguishes different releases of the same official album
   - Examples: `"2003 Remaster"`, `"Deluxe Edition"`, `"50th Anniversary"`, `"Expanded & Remastered"`
   - Each edition is treated as a separate album in the library

4. **Artwork Storage**
   - `ArtworkData` stores raw image bytes (JPEG or PNG)
   - `ArtworkMimeType` MUST be `"image/jpeg"` or `"image/png"`
   - Artwork embedded in ID3 tags via `MetadataService.WriteMetadata()`

5. **Info File Storage**
   - `InfoFileContent` stores full text of `.txt` info files
   - `InfoFileName` stores filename for reference
   - Common in taper recordings (lineage, source info, taper notes)

---

## TrackInfo.cs

**File:** [Models/TrackInfo.cs](Models/TrackInfo.cs)
**Lines:** 61

### Purpose

The `TrackInfo` class represents metadata for a single audio track within an album. Tracks have both original metadata (read from file) and normalized metadata (after song matching). The class handles segue notation, performance dates, and metadata preview generation.

**Implements:** `INotifyPropertyChanged` - Raises PropertyChanged events when IsMatched changes to trigger WPF DataTrigger re-evaluation for gold highlighting of unmatched songs.

---

### Properties

| Property | Type | Description | Example |
|----------|------|-------------|---------|
| `FilePath` | `string` | Full path to audio file | `"D:\library\1977\...\01 - Minglewood.flac"` |
| `FileName` | `string` | Filename only | `"01 - All New Minglewood Blues.flac"` |
| `TrackNumber` | `int` | Track number within disc (user-editable in import grid) | `1` (first track), `12` (twelfth track), `101` (disc 1 using 101/201 convention) |
| `DiscNumber` | `int` | Disc number (defaults to 1) | `1` (single-disc), `2` (disc 2 of multi-disc) |
| `SongName` | `string` | Just the song name (without date suffix or segue) | `"Dancing in the Street"` (normalized or user-edited) |
| `RawTitle` | `string` | Original title as stored in file (includes date/segue) | `"Dancing in the Street (1977-05-08)"` |
| `TrackDate` | `string` | Track-specific date override (yyyy-MM-dd format, empty = inherit from album) | `"1977-05-08"` or `""` (inherits from album date) |
| `Segue` | `bool` | Transitions to next track (segue marker) | `true` (track flows into next), `false` (ends cleanly) |
| `Duration` | `string` | Track duration (MM:SS format, read-only) | `"07:42"`, `"12:15"` |
| `IsModified` | `bool` | Has user made changes? | `true` if edited, `false` otherwise |
| `IsMatched` | `bool?` | Song normalization status (three-state for UI highlighting) | `null` = not yet normalized (no highlighting), `true` = matched in database (white text), `false` = unmatched (gold #D7BA7D text) |

---

### Track Numbering Scheme

**Standard Single-Disc Numbering:**
- Track 1, 2, 3, 4, ... (sequential)
- `DiscNumber = 1` (default)

**Multi-Disc Set Numbering (101/201/301 Convention):**

DeadEditor uses the **101/201/301 convention** for multi-disc sets to make disc boundaries visually obvious:
- **Disc 1:** Track 101, 102, 103, ... (DiscNumber = 1, TrackNumber = 101, 102, 103, ...)
- **Disc 2:** Track 201, 202, 203, ... (DiscNumber = 2, TrackNumber = 201, 202, 203, ...)
- **Disc 3:** Track 301, 302, 303, ... (DiscNumber = 3, TrackNumber = 301, 302, 303, ...)

**Benefits:**
- Disc boundaries are immediately visible in file browsers and media players
- Sorting by track number naturally groups tracks by disc
- Common convention in taper/collector communities

**How DeadEditor Handles This:**
- **On Import:** Reads `DISCNUMBER` tag from FLAC/ID3 metadata (defaults to 1 if missing)
- **Renumber Button:** Disc-aware renumbering using 101/201/301 format based on each track's `DiscNumber`
- **On Write:** Writes both `TRACKNUMBER` (e.g., 101) and `DISCNUMBER` (e.g., 1) tags to files
- **Graceful Fallback:** Tracks with missing/zero `DISCNUMBER` are treated as Disc 1

---

### Computed Properties

#### DisplayTitle (Read-Only)

**Property:**
```csharp
public string DisplayTitle => NormalizedTitle ?? Title;
```

**Purpose:** Returns normalized title if available, otherwise original title.

**Used For:** Displaying track title in UI (DataGrid, playback controls)

**Examples:**
- `Title = "Dnacing in the Street"`, `NormalizedTitle = "Dancing in the Street"`
  - **Returns:** `"Dancing in the Street"` (normalized)
- `Title = "Stranger"`, `NormalizedTitle = null`
  - **Returns:** `"Stranger"` (original, unmatched)

---

#### Segue (Read-Only)

**Property:**
```csharp
public string Segue => HasSegue ? ">" : "";
```

**Purpose:** Returns segue marker (`">"`) if track has segue, empty string otherwise.

**Used For:** Displaying segue marker in library browser DataGrid

**Examples:**
- `HasSegue = true` → **Returns:** `">"`
- `HasSegue = false` → **Returns:** `""`

**Display Format in UI:**
```
Track Name          Segue
─────────────────────────
China Cat Sunflower   >
I Know You Rider
```

---

### Methods

#### GetFinalMetadataTitle

**Signature:**
```csharp
public string GetFinalMetadataTitle(string fallbackDate)
```

**Purpose:** Returns final track title to be written to ID3 tags, including date suffix and segue marker.

**Parameters:**
- `fallbackDate` (string) - Date to use if `PerformanceDate` is null (album date)

**Return Value:** Final metadata title string

**Business Logic:**

**1. Select Date** (line 28):
- Use `PerformanceDate` if set (for bonus tracks with different dates)
- Otherwise use `fallbackDate` (album date)

**2. Select Title** (line 29):
- Use `NormalizedTitle` if available (matched song)
- Otherwise use `Title` (original title)

**3. Add Segue Marker** (lines 32-35):
- If `HasSegue == true` AND title doesn't already have segue marker
- Append ` >` to title
- **Regex check:** `@"[-–]?>|→|\[>\]\s*$"` (matches various segue notations)

**4. Check for Embedded Date/Venue** (lines 44-49):

Detect if title already contains date/venue info (common in bonus tracks):

**Patterns Detected:**
```regex
[\[\(]\d{1,2}/\d{1,2}/\d{2,4}[\s,]       # [M/D/YY, Venue or (MM/DD/YYYY Venue
\(Filler:\s*\d{4}-\d{2}-\d{2}            # (Filler: yyyy-MM-dd - Venue
\(\d{4}-\d{2}-\d{2}                      # (yyyy-MM-dd) or (yyyy-MM-dd - Location)
```

**Examples of Embedded Dates:**
- `"Morning Dew [5/4/72, Olympia Theatre"`
- `"Dark Star (Filler: 1972-05-04 - Paris, France)"`
- `"Truckin' (1977-05-08)"`
- `"Eyes of the World (1978-04-22 >)"` (with segue after date)

**5. Add Date Suffix** (lines 52-55):
- **Only if:** Title does NOT have embedded date/venue info
- **Format:** `"{title} ({date})"`

**Return Examples:**

| Input | Output |
|-------|--------|
| `Title = "Dark Star"`, `HasSegue = true`, `date = "1972-05-04"` | `"Dark Star > (1972-05-04)"` |
| `Title = "China Cat Sunflower"`, `HasSegue = true`, `date = "1977-05-08"` | `"China Cat Sunflower > (1977-05-08)"` |
| `Title = "I Know You Rider"`, `HasSegue = false`, `date = "1977-05-08"` | `"I Know You Rider (1977-05-08)"` |
| `Title = "Morning Dew [5/4/72, Olympia"`, `date = "1972-05-04"` | `"Morning Dew [5/4/72, Olympia"` (no date suffix added) |
| `Title = "Dark Star (Filler: 1972-05-04)"`, `date = "1972-05-04"` | `"Dark Star (Filler: 1972-05-04)"` (no date suffix added) |

---

### Segue Notation

**Purpose:** Indicate when a song transitions seamlessly into the next song without pause.

**Notation:** `>` character appended to track title

**Examples:**

**Single Segue:**
```
Track 1: "China Cat Sunflower >"
Track 2: "I Know You Rider"
```

**Multi-Song Segue Chain:**
```
Track 5: "Scarlet Begonias >"
Track 6: "Fire on the Mountain >"
Track 7: "Estimated Prophet"
```

**How Stored in ID3 Tags:**
- Segue marker appended to title: `"Dark Star > (1972-05-04)"`
- `HasSegue` field NOT stored in tags (derived from `>` marker when reading)

**How Displayed in Library Browser:**
- Separate "Segue" column shows `">"` if `HasSegue == true`
- Track title shown without `>` marker (stripped for display)

---

### Business Rules

1. **Performance Date Override**
   - Bonus tracks on studio albums may have different `PerformanceDate` than album `ReleaseYear`
   - Example: Studio album "Europe '72 (1972)" includes bonus track "Morning Dew (1977-05-08)"
   - Each track can have its own date

2. **Segue Marker Deduplication**
   - Before adding `>`, check if title already contains segue marker
   - Prevents double markers: `"Dark Star > > (1972-05-04)"`

3. **Embedded Date Detection**
   - Skip adding date suffix if title already contains date/venue info
   - Prevents duplication: `"Dark Star (1972-05-04) (1972-05-04)"`

4. **Normalized Title Priority**
   - Always use `NormalizedTitle` over `Title` when available
   - `NormalizedTitle = null` means song wasn't matched in database

5. **Duration Read-Only**
   - `Duration` property populated from audio file properties
   - Not editable by user (read from file metadata)

---

## LibrarySettings.cs

**File:** [Models/LibrarySettings.cs](Models/LibrarySettings.cs)
**Lines:** 67

### Purpose

The `LibrarySettings` class stores user preferences and configuration, including library paths, primary artist, window positions, and last used box set name. Settings are persisted to `%APPDATA%\DeadEditor\settings.json` and loaded on application startup.

---

### Properties

#### Library Configuration

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `LibraryRootPath` | `string` | `""` | Single library root for all album types |
| `FpcalcPath` | `string` | `""` | Path to fpcalc.exe (Chromaprint) for audio fingerprinting |
| `PrimaryArtistName` | `string` | `"Grateful Dead"` | Default artist for imports and new songs when none is otherwise set |
| `LastBoxSetName` | `string?` | `null` | Last used box-set/collection name (for faster imports) |
| `DismissedFpcalcWarning` | `bool` | `false` | User has dismissed the fpcalc.exe startup warning |

**Single Library Root:**
- All albums live under `LibraryRootPath` in the universal `{Artist}/{AlbumFolder}/` layout
- The previous separate `OfficialReleasesPath` setting was retired; see [13-library-import-service.md](13-library-import-service.md) and [19-folder-import-and-manifests.md](19-folder-import-and-manifests.md) for the rationale

**Primary Artist Usage:**
- Default artist applied at import and for new songs when none is otherwise set
- Default: `"Grateful Dead"` (artist-agnostic by design)
- User can change to any artist (e.g., `"Phish"`, `"Neil Young"`)

**Fpcalc Path:**
- Path to fpcalc.exe executable (Chromaprint library)
- Required for audio fingerprinting
- Configured via Settings window (Browse button)
- The fingerprint step (`FingerprintService.ComputeFingerprintAsync`) throws if not configured when fingerprinting attempted
- Download from: https://acoustid.org/chromaprint
- Example: `"C:\\Tools\\fpcalc.exe"`

**Last Box Set Name:**
- Remembers last box set name entered
- Pre-fills box set name field on next import
- Speeds up importing multiple concerts from same box set
- Example: User imports 3 concerts from "Enjoying the Ride" box set → name auto-filled for concerts 2 and 3

**Dismissed Fpcalc Warning:**
- Tracks whether user has dismissed the startup warning about missing fpcalc.exe
- Prevents warning from showing on every startup after dismissal
- Set to `true` when user dismisses warning
- Reset to `false` if fpcalc path is configured (so warning shows again if unconfigured later)

---

#### Main Window Position

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MainWindowLeft` | `double?` | `null` | X position of main window (pixels from left edge) |
| `MainWindowTop` | `double?` | `null` | Y position of main window (pixels from top edge) |
| `MainWindowWidth` | `double?` | `null` | Width of main window (pixels) |
| `MainWindowHeight` | `double?` | `null` | Height of main window (pixels) |

**Used For:** Restoring main window size/position on startup

**Default Behavior:** If `null`, window uses default size/position from XAML

---

#### Library Browser Window Position

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `LibraryWindowLeft` | `double?` | `null` | X position of library browser window |
| `LibraryWindowTop` | `double?` | `null` | Y position of library browser window |
| `LibraryWindowWidth` | `double?` | `null` | Width of library browser window |
| `LibraryWindowHeight` | `double?` | `null` | Height of library browser window |

**Used For:** Restoring library browser size/position when reopened

---

### File Storage Location

**Path Construction** (lines 24-27):
```csharp
private static readonly string SettingsPath = Path.Combine(
    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
    "DeadEditor",
    "settings.json"
);
```

**Resolved Path:**
- **Windows:** `%APPDATA%\DeadEditor\settings.json`
- **Full Path Example:** `C:\Users\YourName\AppData\Roaming\DeadEditor\settings.json`

**Directory Creation:**
- Directory `%APPDATA%\DeadEditor` created automatically if missing (line 52-54)
- Settings file created on first save

---

### Methods

#### Load (Static)

**Signature:**
```csharp
public static LibrarySettings Load()
```

**Purpose:** Load settings from `settings.json` file, or return defaults if file doesn't exist.

**Business Logic:**

1. **Check if file exists** (line 33)
2. **Read JSON** from file (line 35)
3. **Deserialize** to `LibrarySettings` object (line 36)
4. **Return default settings** if:
   - File doesn't exist
   - Deserialization returns `null`
   - Exception occurs (line 39-42)

**Error Handling:** Returns `new LibrarySettings()` with default values on any error.

**Default Values:**
- `LibraryRootPath = ""`
- `PrimaryArtistName = "Grateful Dead"`
- `LastBoxSetName = null`
- All window positions = `null`

---

#### Save (Instance)

**Signature:**
```csharp
public void Save()
```

**Purpose:** Save current settings to `settings.json` file.

**Business Logic:**

1. **Get directory path** from `SettingsPath` (line 51)
2. **Create directory** if it doesn't exist (line 52-54)
3. **Serialize** settings to JSON with indentation (line 57)
4. **Write JSON** to file (line 58)

**Error Handling:** Silently fails if save fails (line 60-63) - no exception thrown, no user notification.

**JSON Formatting:** Uses `Formatting.Indented` for human-readable JSON (line 57).

---

### Business Rules

1. **Immediate Save on Path Change**
   - When user changes `LibraryRootPath` or `FpcalcPath` in Settings, save immediately
   - Ensures paths persisted even if application crashes

2. **Deferred Save on Artist Change**
   - `PrimaryArtistName` saved when user clicks "Save" in Settings window
   - Not immediately persisted on every keystroke

3. **Window Position Persistence**
   - Window positions saved on window close
   - Restored on next window open
   - If `null`, uses XAML default position

4. **Silent Failure on Save Error**
   - If save fails (permissions, disk full, etc.), no exception thrown
   - Settings changes lost if save fails
   - **Known Issue:** User not notified of save failure

5. **Box Set Name Memory**
   - `LastBoxSetName` updated after successful box set import
   - Auto-fills box set name field on next import
   - Speeds up batch importing from same box set

6. **Fpcalc.exe Startup Warning**
   - On startup, check if `FpcalcPath` is empty/invalid AND `DismissedFpcalcWarning` is false
   - Show warning once directing user to Settings
   - Set `DismissedFpcalcWarning = true` when user dismisses
   - Warning not shown again until fpcalc path is configured (resets dismiss flag)

7. **Fpcalc.exe Validation**
   - `FpcalcPath` NOT validated on save (allows invalid paths to be saved)
   - Validation happens at use-time (when MusicBrainzService attempts fingerprinting)
   - Throws exception if empty or file doesn't exist

---

## SongDatabase.cs

**File:** [Models/SongDatabase.cs](Models/SongDatabase.cs)
**Lines:** 26

### Purpose

The `SongDatabase` class defines the structure for `songs.json` database. It supports **two formats** for backward compatibility: legacy flat structure (deprecated) and new artist-based structure (current).

---

### Class Hierarchy

```
SongDatabase
├── Artists (List<ArtistEntry>) - NEW artist-based structure
│   └── ArtistEntry
│       ├── Name (string) - Artist name
│       └── Songs (List<SongEntry>) - Songs by this artist
└── Songs (List<SongEntry>) - LEGACY flat structure (deprecated)

SongEntry
├── OfficialTitle (string) - Canonical song name
└── Aliases (List<string>) - Alternative spellings/variations
```

---

### SongEntry Class

**Properties:**

| Property | Type | Description | Example |
|----------|------|-------------|---------|
| `OfficialTitle` | `string` | Canonical/correct song title | `"Dancing in the Street"` |
| `Aliases` | `List<string>` | Alternative spellings, typos, abbreviations | `["Dnacing in the Street", "Dancing"]` |

**Purpose:** Represents a single song with its official title and known variations.

---

### ArtistEntry Class

**Properties:**

| Property | Type | Description | Example |
|----------|------|-------------|---------|
| `Name` | `string` | Artist name | `"Grateful Dead"`, `"NRPS"`, `"Jerry Garcia Band"` |
| `Songs` | `List<SongEntry>` | Songs performed by this artist | `[{OfficialTitle: "Dark Star", Aliases: [...]}, ...]` |

**Purpose:** Groups songs by artist for multi-artist support.

---

### SongDatabase Class

**Properties:**

| Property | Type | Description |
|----------|------|-------------|
| `Artists` | `List<ArtistEntry>` | **NEW:** Artist-based structure (current format) |
| `Songs` | `List<SongEntry>` | **LEGACY:** Flat song list (deprecated, backward compatibility only) |

**Dual Structure Design:**
- **New databases:** Use `Artists` list only
- **Legacy databases:** Use `Songs` list only
- **NormalizationService** reads both structures for backward compatibility
- **AddSongDialog** writes to `Artists` structure only

---

### Business Rules

1. **Artist-Based Structure (Current)**
   - All new songs added via `Artists` list
   - Each artist has separate song list
   - Supports multiple artists in same database
   - Current count: 2 artists (Grateful Dead: 594 songs, NRPS: 4 songs)

2. **Legacy Structure (Deprecated)**
   - Old databases use flat `Songs` list
   - No artist differentiation
   - Still readable by `NormalizationService` for backward compatibility
   - **Not written to** by current code

3. **Backward Compatibility**
   - `NormalizationService.LoadSongDatabase()` reads both `Artists` and `Songs`
   - If `Artists` exists, use that
   - If only `Songs` exists, load from legacy structure
   - Allows upgrading old databases without data loss

4. **Song Uniqueness**
   - Songs unique within artist (e.g., two artists can both have "Midnight Hour")
   - `OfficialTitle` must be unique within artist's song list
   - Aliases can overlap (handled by fuzzy matching priority)

---

## songs.json Schema

**File:** [Data/songs.json](Data/songs.json)
**Lines:** ~7,000+ (598 songs with aliases)

### JSON Structure

**Schema:**
```json
{
  "Artists": [
    {
      "Name": "string",
      "Songs": [
        {
          "OfficialTitle": "string",
          "Aliases": ["string", "string", ...]
        }
      ]
    }
  ],
  "Songs": []  // Legacy structure (empty in current databases)
}
```

---

### Real-World Examples

**Example 1: Song with Aliases**
```json
{
  "OfficialTitle": "Ain't It Crazy (The Rub)",
  "Aliases": [
    "Ain't It Crazy",
    "The Rub"
  ]
}
```

**Example 2: Song with No Aliases**
```json
{
  "OfficialTitle": "Althea",
  "Aliases": []
}
```

**Example 3: Song with Multiple Variations**
```json
{
  "OfficialTitle": "All New Minglewood Blues",
  "Aliases": [
    "New Minglewood Blues",
    "Minglewood Blues",
    "Minglewood"
  ]
}
```

**Example 4: Song with Segue Notation Aliases**
```json
{
  "OfficialTitle": "China Cat Sunflower",
  "Aliases": [
    "China Cat",
    "China Cat Sunflower >",
    "China Cat >",
    "China > Cat Sunflower"
  ]
}
```

**Example 5: Song with Punctuation Variations**
```json
{
  "OfficialTitle": "And We Bid You Goodnight",
  "Aliases": [
    "Bid You Goodnight Jam",
    "And We Bid You Goodnight Jam",
    "We Bid You Goodnight"
  ]
}
```

**Example 6: Song with Ampersand Variations**
```json
{
  "OfficialTitle": "Around and Around",
  "Aliases": [
    "Around & Around"
  ]
}
```

---

### Full Artist Entry Example

```json
{
  "Name": "Grateful Dead",
  "Songs": [
    {
      "OfficialTitle": "Dark Star",
      "Aliases": ["Darkstar", "Dark Star >", "-> Dark Star"]
    },
    {
      "OfficialTitle": "Dancing in the Street",
      "Aliases": ["Dnacing in the Street", "Dancing"]
    },
    {
      "OfficialTitle": "China Cat Sunflower",
      "Aliases": ["China Cat", "China Cat >"]
    }
  ]
}
```

---

### Database Statistics

**Current Database (as of 2026-01-25):**
- **Total Artists:** 2
  - Grateful Dead: 594 songs
  - NRPS (New Riders of the Purple Sage): 4 songs
- **Total Songs:** 598
- **Total Aliases:** ~1,200+ (varies per song)
- **File Size:** ~180 KB

**Common Alias Patterns:**
- **Typos:** `"Dnacing in the Street"` → `"Dancing in the Street"`
- **Abbreviations:** `"Minglewood"` → `"All New Minglewood Blues"`
- **Punctuation:** `"Around & Around"` → `"Around and Around"`
- **Segue markers:** `"Dark Star >"` → `"Dark Star"`
- **Parentheticals:** `"Ain't It Crazy"` → `"Ain't It Crazy (The Rub)"`

---

### Schema Notes

1. **Empty Aliases Array**
   - Valid: `"Aliases": []` (no known variations)
   - Common for well-spelled, unambiguous song titles

2. **Alias Ordering**
   - No guaranteed order (order doesn't affect matching)
   - Most common variations typically listed first

3. **Case Sensitivity**
   - JSON case-sensitive, but matching is case-**in**sensitive
   - `"Dark Star"` and `"dark star"` match the same song

4. **Apostrophe Normalization**
   - `'` (straight apostrophe) and `'` (curly apostrophe) treated as equivalent
   - Handled by `NormalizationService.NormalizeApostrophes()`

---

## settings.json Schema

**File:** `%APPDATA%\DeadEditor\settings.json`

### JSON Structure

**Schema:**
```json
{
  "LibraryRootPath": "string",
  "FpcalcPath": "string",
  "PrimaryArtistName": "string",
  "LastBoxSetName": "string | null",
  "DismissedFpcalcWarning": "bool",
  "MainWindowLeft": "double | null",
  "MainWindowTop": "double | null",
  "MainWindowWidth": "double | null",
  "MainWindowHeight": "double | null",
  "LibraryWindowLeft": "double | null",
  "LibraryWindowTop": "double | null",
  "LibraryWindowWidth": "double | null",
  "LibraryWindowHeight": "double | null"
}
```

The actual `LibrarySettings` model includes additional fields not shown above (volume, player-window positions, saved playlist paths, etc.). See [Models/LibrarySettings.cs](../Models/LibrarySettings.cs) for the full schema.

---

### Property Details

| Key | Type | Default | Example | Description |
|-----|------|---------|---------|-------------|
| `LibraryRootPath` | `string` | `""` | `"D:\\Projects\\library"` | Single library root for all album types |
| `FpcalcPath` | `string` | `""` | `"C:\\Tools\\fpcalc.exe"` | Path to fpcalc.exe for fingerprinting |
| `PrimaryArtistName` | `string` | `"Grateful Dead"` | `"Grateful Dead"` | Primary artist name |
| `LastBoxSetName` | `string?` | `null` | `"Enjoying the Ride"` | Last used box set name |
| `DismissedFpcalcWarning` | `bool` | `false` | `true` | User dismissed fpcalc startup warning |
| `MainWindowLeft` | `double?` | `null` | `3515.0` | Main window X position (pixels) |
| `MainWindowTop` | `double?` | `null` | `0.0` | Main window Y position (pixels) |
| `MainWindowWidth` | `double?` | `null` | `1100.0` | Main window width (pixels) |
| `MainWindowHeight` | `double?` | `null` | `1111.0` | Main window height (pixels) |
| `LibraryWindowLeft` | `double?` | `null` | `2561.0` | Library window X position |
| `LibraryWindowTop` | `double?` | `null` | `1.0` | Library window Y position |
| `LibraryWindowWidth` | `double?` | `null` | `2046.0` | Library window width |
| `LibraryWindowHeight` | `double?` | `null` | `1102.0` | Library window height |

---

### Real-World Example

**Actual `settings.json` from Development Environment:**
```json
{
  "LibraryRootPath": "D:\\Projects\\library",
  "FpcalcPath": "C:\\Tools\\fpcalc.exe",
  "PrimaryArtistName": "Grateful Dead",
  "LastBoxSetName": null,
  "DismissedFpcalcWarning": true,
  "MainWindowLeft": 3515.0,
  "MainWindowTop": 0.0,
  "MainWindowWidth": 1100.0,
  "MainWindowHeight": 1111.0,
  "LibraryWindowLeft": 2561.0,
  "LibraryWindowTop": 1.0,
  "LibraryWindowWidth": 2046.0,
  "LibraryWindowHeight": 1102.0
}
```

---

### Default Settings (First Run)

**When `settings.json` doesn't exist:**
```json
{
  "LibraryRootPath": "",
  "FpcalcPath": "",
  "PrimaryArtistName": "Grateful Dead",
  "LastBoxSetName": null,
  "DismissedFpcalcWarning": false,
  "MainWindowLeft": null,
  "MainWindowTop": null,
  "MainWindowWidth": null,
  "MainWindowHeight": null,
  "LibraryWindowLeft": null,
  "LibraryWindowTop": null,
  "LibraryWindowWidth": null,
  "LibraryWindowHeight": null
}
```

**Behavior with `null` Window Positions:**
- Windows use default size/position from XAML definitions
- Main window: Centered, 1024×768 (example)
- Library window: Centered, 1200×800 (example)

---

### Schema Notes

1. **Path Escaping**
   - Backslashes escaped in JSON: `"D:\\Projects\\library"`
   - Loaded as: `D:\Projects\library`

2. **Null vs Empty String**
   - Paths: Empty string `""` (not `null`) when unset
   - Box set name: `null` when never used
   - Window positions: `null` when never saved

3. **Double Precision**
   - Window positions stored as `double` (floating-point)
   - Supports sub-pixel positioning
   - Example: `3515.0` (not `3515`)

4. **Indented Formatting**
   - File written with `Formatting.Indented` for readability
   - 2-space indentation
   - Human-editable if needed

---

## Real-World Examples

### Example 1: Audience Recording

**AlbumInfo:**
```csharp
AlbumInfo {
  FolderPath = "D:\\Projects\\library\\Grateful Dead\\Grateful Dead - 1977-05-08 - Barton Hall - Ithaca, NY",
  Artist = "Grateful Dead",
  Type = AlbumType.AudienceRecording,
  AlbumDate = "1977-05-08",
  Venue = "Barton Hall, Cornell University",
  CityState = "Ithaca, NY",
  AlbumName = "",
  CollectionName = "",
  Year = "",
  Edition = "",
  ArtworkData = [JPEG bytes],
  ArtworkMimeType = "image/jpeg",
  InfoFileContent = "Source: AUD Charlie Miller\nLineage: Master Reel > ...",
  InfoFileName = "gd77-05-08.txt"
}
```

**AlbumTitle (Computed):**
```
"1977-05-08 - Barton Hall, Cornell University - Ithaca, NY"
```

**TrackInfo Examples:**
```csharp
TrackInfo {
  FilePath = "D:\\...\\01 - All New Minglewood Blues (1977-05-08).flac",
  FileName = "01 - All New Minglewood Blues (1977-05-08).flac",
  TrackNumber = 1,
  DiscNumber = 1,
  Title = "All New Minglewood Blues",
  NormalizedTitle = "All New Minglewood Blues",
  PerformanceDate = "1977-05-08",
  HasSegue = false,
  Duration = "04:42"
}

TrackInfo {
  FilePath = "D:\\...\\08 - Scarlet Begonias (1977-05-08).flac",
  FileName = "08 - Scarlet Begonias (1977-05-08).flac",
  TrackNumber = 8,
  DiscNumber = 1,
  Title = "Scarlet Begonias",
  NormalizedTitle = "Scarlet Begonias",
  PerformanceDate = "1977-05-08",
  HasSegue = true,  // ← Segues into next track
  Duration = "11:15"
}

TrackInfo {
  FilePath = "D:\\...\\09 - Fire on the Mountain (1977-05-08).flac",
  FileName = "09 - Fire on the Mountain (1977-05-08).flac",
  TrackNumber = 9,
  DiscNumber = 1,
  Title = "Fire on the Mountain",
  NormalizedTitle = "Fire on the Mountain",
  PerformanceDate = "1977-05-08",
  HasSegue = false,
  Duration = "12:03"
}
```

**Final Metadata Written to Tags:**
```
Track 8: "Scarlet Begonias > (1977-05-08)"
Track 9: "Fire on the Mountain (1977-05-08)"
```

---

### Example 2: Studio-Style Official Release with Edition

**AlbumInfo:**
```csharp
AlbumInfo {
  FolderPath = "D:\\Projects\\library\\Grateful Dead\\Grateful Dead - 1970 - Workingman's Dead",
  Artist = "Grateful Dead",
  Type = AlbumType.OfficialRelease,
  AlbumDate = "",
  Venue = "",
  CityState = "",
  AlbumName = "Workingman's Dead",
  CollectionName = "",
  Year = "1970",
  Edition = "2025 Remaster",
  ArtworkData = [JPEG bytes],
  ArtworkMimeType = "image/jpeg",
  InfoFileContent = null,
  InfoFileName = null
}
```

**AlbumTitle (Computed):**
```
"Workingman's Dead (1970)"
```

(`Edition` is preserved as metadata but is not appended to `AlbumTitle` by the current model. The UI displays edition information separately.)

**TrackInfo Examples:**
```csharp
TrackInfo {
  FilePath = "D:\\...\\01 - Uncle John's Band.flac",
  FileName = "01 - Uncle John's Band.flac",
  TrackNumber = 1,
  DiscNumber = 1,
  Title = "Uncle John's Band",
  NormalizedTitle = "Uncle John's Band",
  PerformanceDate = null,
  HasSegue = false,
  Duration = "04:42"
}

TrackInfo {
  FilePath = "D:\\...\\08 - Morning Dew (1977-05-08).flac",
  FileName = "08 - Morning Dew (1977-05-08).flac",
  TrackNumber = 8,
  DiscNumber = 1,
  Title = "Morning Dew (Bonus Track)",
  NormalizedTitle = "Morning Dew",
  PerformanceDate = "1977-05-08",  // ← Bonus track with different date
  HasSegue = false,
  Duration = "11:30"
}
```

**Final Metadata Written to Tags:**
```
Track 1: "Uncle John's Band"  (no date - studio album)
Track 8: "Morning Dew (1977-05-08)"  (bonus track with date)
```

---

### Example 3: Box Set (OfficialRelease with CollectionName)

**AlbumInfo:**
```csharp
AlbumInfo {
  FolderPath = "D:\\Projects\\library\\Grateful Dead\\Grateful Dead - 1972-05-04 - Olympia Theatre - Paris, France - Enjoying the Ride",
  Artist = "Grateful Dead",
  Type = AlbumType.OfficialRelease,
  AlbumDate = "1972-05-04",
  Venue = "Olympia Theatre",
  CityState = "Paris, France",
  AlbumName = "",
  CollectionName = "Enjoying the Ride",
  Year = "",
  Edition = "",
  ArtworkData = [JPEG bytes],
  ArtworkMimeType = "image/jpeg",
  InfoFileContent = "Digital box set download...",
  InfoFileName = "info.txt"
}
```

**AlbumTitle (Computed):**
```
"1972-05-04 - Olympia Theatre - Paris, France : Enjoying the Ride"
```

(Box sets are no longer a separate `AlbumType`; they are an `OfficialRelease` whose collection name is carried in `CollectionName`.)

**TrackInfo Examples:**
```csharp
TrackInfo {
  FilePath = "D:\\...\\d1t01 - Bertha (1972-05-04).flac",
  FileName = "d1t01 - Bertha (1972-05-04).flac",
  TrackNumber = 1,
  DiscNumber = 1,
  Title = "Bertha",
  NormalizedTitle = "Bertha",
  PerformanceDate = "1972-05-04",
  HasSegue = false,
  Duration = "06:15"
}
```

---

### Example 4: Live-Flavor Official Release

**AlbumInfo:**
```csharp
AlbumInfo {
  FolderPath = "D:\\Projects\\library\\Grateful Dead\\Grateful Dead - 1977-05-08 - Barton Hall - Ithaca, NY - Dave's Picks Vol. 29",
  Artist = "Grateful Dead",
  Type = AlbumType.OfficialRelease,
  AlbumDate = "1977-05-08",
  Venue = "Barton Hall, Cornell University",
  CityState = "Ithaca, NY",
  AlbumName = "Dave's Picks Vol. 29",
  CollectionName = "",
  Year = "",
  Edition = "",
  ArtworkData = [JPEG bytes],
  ArtworkMimeType = "image/jpeg"
}
```

**AlbumTitle (Computed):**
```
"1977-05-08 - Barton Hall, Cornell University - Ithaca, NY - Dave's Picks Vol. 29"
```

---

## Summary

### Key Takeaways

1. **Type-Based Design**
   - `AlbumInfo` uses `Type` field to determine which properties are populated
   - `AlbumTitle` computed property adapts format based on type
   - Three distinct formats: Live, Studio, BoxSet

2. **Colon Spacing Convention**
   - **Box Set:** `State:{BoxSetName}` (NO space before `:`)
   - **Official Release:** `State : {OfficialRelease}` (space before `:`)
   - Critical for regex parsing in `MetadataService`

3. **Track Metadata**
   - Tracks have both original (`Title`) and normalized (`NormalizedTitle`) metadata
   - Segue notation (`>`) indicates seamless transitions
   - Performance dates can differ from album date (bonus tracks)

4. **Settings Persistence**
   - Settings stored in `%APPDATA%\DeadEditor\settings.json`
   - Two-path library system (audience vs official releases)
   - Window positions saved/restored automatically

5. **Song Database**
   - Artist-based structure (current format)
   - Supports multiple artists
   - Backward compatible with legacy flat structure
   - 598 songs in current database (594 Grateful Dead, 4 NRPS)

6. **JSON Schemas**
   - `songs.json` - Artist → Songs → Aliases hierarchy
   - `settings.json` - Library paths + window positions

---

**Last Updated:** 2026-03-02
**Status:** Updated to include FpcalcPath and DismissedFpcalcWarning settings properties
