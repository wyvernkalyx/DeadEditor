# Data Model Documentation

## Purpose

This document provides comprehensive coverage of DeadEditor's data model layer, including all model classes, their properties, business logic, JSON schemas, and real-world examples. The data model supports three distinct album types (Live, Studio, BoxSet) with type-specific properties and computed fields.

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

The `AlbumInfo` class represents metadata for a concert, studio album, or box set. It uses a **type-based polymorphic design** where different properties are populated based on the `Type` field. The `AlbumTitle` computed property adapts its format based on album type.

---

### AlbumType Enum

**Definition:**
```csharp
public enum AlbumType
{
    Live,            // Live concert recording (audience/taper recording)
    Studio,          // Studio album release
    OfficialRelease, // Official live release (Dave's Picks, Road Trips, etc.)
    BoxSet           // Box set collection (digital or physical multi-show sets)
}
```

**Values:**

| Value | Description | Folder Location |
|-------|-------------|-----------------|
| `Live` | Audience/taper recording of single concert | `{LibraryRoot}/{Year}/{Date} - {Venue} - {City}, {State}` |
| `Studio` | Studio album release | `{OfficialReleasesPath}/Studio Albums/{Album} ({Year})` |
| `OfficialRelease` | Official live release (Dave's Picks, Dick's Picks, etc.) | `{OfficialReleasesPath}/{Series}/{Release Name}` |
| `BoxSet` | Multi-show collection (digital box sets, physical box sets) | `{LibraryRoot}/{Year}/{Date} - {Venue} - {City}, {State}:{BoxSetName}` |

**Default:** `AlbumType.Live` (line 17) - For backward compatibility with legacy imports.

---

### Properties

#### Common Properties (All Types)

| Property | Type | Description | Example |
|----------|------|-------------|---------|
| `FolderPath` | `string` | Full path to album folder | `"D:\library\1977\1977-05-08 - Barton Hall - Ithaca, NY"` |
| `Artist` | `string` | Artist name | `"Grateful Dead"` |
| `Type` | `AlbumType` | Album type (Live/Studio/OfficialRelease/BoxSet) | `AlbumType.Live` |
| `IsModified` | `bool` | Has user made changes? | `false` |
| `ArtworkData` | `byte[]?` | Album artwork image bytes | `[JPEG/PNG binary data]` |
| `ArtworkMimeType` | `string?` | MIME type of artwork | `"image/jpeg"` or `"image/png"` |
| `InfoFileContent` | `string?` | Content of .txt info file | `"Source: AUD Charlie Miller\nLineage: ..."` |
| `InfoFileName` | `string?` | Name of info file | `"gd77-05-08.txt"` |

---

#### Live Recording Properties (Type = Live or BoxSet)

| Property | Type | Description | Example |
|----------|------|-------------|---------|
| `Date` | `string` | Performance date (yyyy-MM-dd) | `"1977-05-08"` |
| `Venue` | `string` | Venue name | `"Barton Hall, Cornell University"` |
| `City` | `string` | City name | `"Ithaca"` |
| `State` | `string` | State abbreviation | `"NY"` |
| `OfficialRelease` | `string` | Optional official release name (if concert later released officially) | `"Dave's Picks Vol. 29"` |

**Used For:** Live concerts, box sets
**Not Used For:** Studio albums

---

#### Studio Album Properties (Type = Studio)

| Property | Type | Description | Example |
|----------|------|-------------|---------|
| `AlbumName` | `string` | Studio album title | `"Workingman's Dead"` |
| `ReleaseYear` | `int?` | Year of release | `1970` |
| `Edition` | `string?` | Edition/remaster info (optional) | `"2025 Remaster"`, `"Deluxe Edition"` |

**Used For:** Studio albums only
**Not Used For:** Live concerts, box sets

---

#### Box Set Properties (Type = BoxSet)

| Property | Type | Description | Example |
|----------|------|-------------|---------|
| `BoxSetName` | `string?` | Name of box set collection | `"Enjoying the Ride"`, `"Digital Album Live"` |

**Used For:** Box sets only
**Not Used For:** Live concerts, studio albums

**Note:** Box sets also populate live recording properties (`Date`, `Venue`, `City`, `State`) for each concert in the set.

---

### Computed Properties

#### AlbumTitle (Read-Only)

**Property:**
```csharp
public string AlbumTitle { get; }
```

**Purpose:** Returns formatted album title based on `Type`. Adapts format for Live/Studio/BoxSet albums.

**Business Logic:**

**1. Studio Album Format** (Type = Studio, lines 49-63):
```
"{AlbumName} ({ReleaseYear}) [{Edition}]"
```

**Examples:**
- `AlbumName = "Workingman's Dead"`, `ReleaseYear = 1970`, `Edition = null`
  - **Returns:** `"Workingman's Dead (1970)"`
- `AlbumName = "American Beauty"`, `ReleaseYear = 1970`, `Edition = "2025 Remaster"`
  - **Returns:** `"American Beauty (1970) [2025 Remaster]"`
- `AlbumName = "Europe '72"`, `ReleaseYear = null`, `Edition = null`
  - **Returns:** `"Europe '72"`
- `AlbumName = null`
  - **Returns:** `"Unknown Album"`

**2. Box Set Format** (Type = BoxSet, lines 65-71):
```
"{Date} - {Venue} - {City}, {State}: {BoxSetName}"
```

**Note:** NO space before colon (`:`) - distinguishes box sets from official releases.

**Examples:**
- `Date = "1972-05-04"`, `Venue = "Olympia Theatre"`, `City = "Paris"`, `State = "France"`, `BoxSetName = "Enjoying the Ride"`
  - **Returns:** `"1972-05-04 - Olympia Theatre - Paris, France: Enjoying the Ride"`
- `BoxSetName = null`
  - **Returns:** `"1972-05-04 - Olympia Theatre - Paris, France"` (base title only)

**3. Live Recording Format** (Type = Live or OfficialRelease, lines 73-80):
```
"{Date} - {Venue} - {City}, {State} [- {AlbumName}] [: {OfficialRelease}]"
```

**Note:** SPACE before colon (` :`) - distinguishes official releases from box sets. Album Name is appended with dash separator (if populated). Official Release name is appended with colon separator (if populated).

**Examples:**
- `Date = "1977-05-08"`, `Venue = "Barton Hall"`, `City = "Ithaca"`, `State = "NY"`, `AlbumName = null`, `OfficialRelease = null`
  - **Returns:** `"1977-05-08 - Barton Hall - Ithaca, NY"`
- `Date = "1971-04-25"`, `Venue = "Fillmore East"`, `City = "New York"`, `State = "NY"`, `AlbumName = "Enjoying the Ride"`, `OfficialRelease = null`
  - **Returns:** `"1971-04-25 - Fillmore East - New York, NY - Enjoying the Ride"`
- `Date = "1977-05-08"`, `Venue = "Barton Hall"`, `City = "Ithaca"`, `State = "NY"`, `AlbumName = null`, `OfficialRelease = "Dave's Picks Vol. 29"`
  - **Returns:** `"1977-05-08 - Barton Hall - Ithaca, NY : Dave's Picks Vol. 29"`

**Critical Business Rule:** Colon spacing distinguishes box sets from official releases:
- **Box Set:** `State:{BoxSetName}` (NO space before `:`)
- **Official Release:** `State : {OfficialRelease}` (space before `:`)

This distinction is used by `MetadataService.ParseAlbumTitle()` regex patterns (see [11-metadata-service.md](11-metadata-service.md)).

---

#### IsStudioAlbum (Read-Only)

**Property:**
```csharp
public bool IsStudioAlbum => Type == AlbumType.Studio;
```

**Purpose:** Helper property to check if album is a studio album.

**Used For:** UI logic, import workflow branching

---

### Type-Based Property Usage Matrix

| Property | Live | Studio | OfficialRelease | BoxSet |
|----------|------|--------|-----------------|--------|
| `FolderPath` | ✓ | ✓ | ✓ | ✓ |
| `Artist` | ✓ | ✓ | ✓ | ✓ |
| `Type` | ✓ | ✓ | ✓ | ✓ |
| `ArtworkData` | ✓ | ✓ | ✓ | ✓ |
| `InfoFileContent` | ✓ | ✓ | ✓ | ✓ |
| **`Date`** | ✓ | ✗ | ✓ | ✓ |
| **`Venue`** | ✓ | ✗ | ✓ | ✓ |
| **`City`** | ✓ | ✗ | ✓ | ✓ |
| **`State`** | ✓ | ✗ | ✓ | ✓ |
| **`OfficialRelease`** | ✓ | ✗ | ✓ | ✗ |
| **`AlbumName`** | ✗ | ✓ | ✗ | ✗ |
| **`ReleaseYear`** | ✗ | ✓ | ✗ | ✗ |
| **`Edition`** | ✗ | ✓ | ✗ | ✗ |
| **`BoxSetName`** | ✗ | ✗ | ✗ | ✓ |

---

### Business Rules

1. **Type Determines Property Population**
   - Studio albums MUST have `AlbumName` and `ReleaseYear`
   - Live/BoxSet albums MUST have `Date`, `Venue`, `City`, `State`
   - Box sets SHOULD have `BoxSetName` (optional, but recommended)

2. **Date Format Enforcement**
   - `Date` property MUST use yyyy-MM-dd (ISO 8601) format
   - No MM/dd/yyyy or dd/MM/yyyy formats allowed
   - Enforced by import workflow and metadata service

3. **Colon Spacing Convention**
   - Box sets: NO space before colon (`:`) in `AlbumTitle`
   - Official releases: Space before colon (` :`) in `AlbumTitle`
   - Used by regex parsing in `MetadataService.ParseAlbumTitle()`

4. **Edition Field Usage**
   - `Edition` field distinguishes different releases of same studio album
   - Examples: `"2003 Remaster"`, `"Deluxe Edition"`, `"50th Anniversary"`, `"Expanded & Remastered"`
   - Each edition treated as separate album in library
   - Displayed in square brackets: `"Album (Year) [Edition]"`

5. **Artwork Storage**
   - `ArtworkData` stores raw image bytes (JPEG or PNG)
   - `ArtworkMimeType` MUST be `"image/jpeg"` or `"image/png"`
   - Artwork embedded in ID3 tags via `MetadataService.WriteMetadata()`

6. **Info File Storage**
   - `InfoFileContent` stores full text of `.txt` info files
   - `InfoFileName` stores filename for reference
   - Common in taper recordings (lineage, source info, taper notes)

---

## TrackInfo.cs

**File:** [Models/TrackInfo.cs](Models/TrackInfo.cs)
**Lines:** 61

### Purpose

The `TrackInfo` class represents metadata for a single audio track within an album. Tracks have both original metadata (read from file) and normalized metadata (after song matching). The class handles segue notation, performance dates, and metadata preview generation.

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
| `LibraryRootPath` | `string` | `""` | Path to audience recordings and box sets |
| `OfficialReleasesPath` | `string` | `""` | Path to official releases (studio albums, official live albums) |
| `FpcalcPath` | `string` | `""` | Path to fpcalc.exe (Chromaprint) for MusicBrainz fingerprinting |
| `PrimaryArtistName` | `string` | `"Grateful Dead"` | Primary artist for MusicBrainz filtering |
| `LastBoxSetName` | `string?` | `null` | Last used box set name (for faster imports) |
| `DismissedFpcalcWarning` | `bool` | `false` | User has dismissed the fpcalc.exe startup warning |

**Two-Path System:**
- `LibraryRootPath` - Audience recordings, box sets (date-based organization)
- `OfficialReleasesPath` - Studio albums, official live releases (album-based organization)
- **Can be same directory or separate** (user's choice)

**Primary Artist Usage:**
- Used by MusicBrainz search to filter results
- Default: `"Grateful Dead"` (artist-agnostic by design)
- User can change to any artist (e.g., `"Phish"`, `"Neil Young"`)

**Fpcalc Path:**
- Path to fpcalc.exe executable (Chromaprint library)
- Required for MusicBrainz audio fingerprinting
- Configured via Settings window (Browse button)
- MusicBrainzService throws exception if not configured when fingerprinting attempted
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
- `OfficialReleasesPath = ""`
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
   - When user changes `LibraryRootPath`, `OfficialReleasesPath`, or `FpcalcPath` in Settings, save immediately
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
  "OfficialReleasesPath": "string",
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

---

### Property Details

| Key | Type | Default | Example | Description |
|-----|------|---------|---------|-------------|
| `LibraryRootPath` | `string` | `""` | `"D:\\Projects\\library\\Grateful Dead\\Concerts"` | Path to audience recordings |
| `OfficialReleasesPath` | `string` | `""` | `"D:\\Projects\\library\\Grateful Dead\\Releases"` | Path to official releases |
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
  "LibraryRootPath": "D:\\Projects\\library\\Grateful Dead\\Concerts",
  "OfficialReleasesPath": "D:\\Projects\\library\\Grateful Dead\\Releases",
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
  "OfficialReleasesPath": "",
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

### Example 1: Live Concert (Audience Recording)

**AlbumInfo:**
```csharp
AlbumInfo {
  FolderPath = "D:\\Projects\\library\\Grateful Dead\\Concerts\\1977\\1977-05-08 - Barton Hall - Ithaca, NY",
  Artist = "Grateful Dead",
  Type = AlbumType.Live,
  Date = "1977-05-08",
  Venue = "Barton Hall, Cornell University",
  City = "Ithaca",
  State = "NY",
  OfficialRelease = null,
  AlbumName = null,
  ReleaseYear = null,
  Edition = null,
  BoxSetName = null,
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

### Example 2: Studio Album with Edition

**AlbumInfo:**
```csharp
AlbumInfo {
  FolderPath = "D:\\Projects\\library\\Grateful Dead\\Releases\\Studio Albums\\Workingman's Dead (1970)",
  Artist = "Grateful Dead",
  Type = AlbumType.Studio,
  Date = null,
  Venue = null,
  City = null,
  State = null,
  OfficialRelease = null,
  AlbumName = "Workingman's Dead",
  ReleaseYear = 1970,
  Edition = "2025 Remaster",
  BoxSetName = null,
  ArtworkData = [JPEG bytes],
  ArtworkMimeType = "image/jpeg",
  InfoFileContent = null,
  InfoFileName = null
}
```

**AlbumTitle (Computed):**
```
"Workingman's Dead (1970) [2025 Remaster]"
```

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

### Example 3: Box Set

**AlbumInfo:**
```csharp
AlbumInfo {
  FolderPath = "D:\\Projects\\library\\Grateful Dead\\Concerts\\1972\\1972-05-04 - Olympia Theatre - Paris, France:Enjoying the Ride",
  Artist = "Grateful Dead",
  Type = AlbumType.BoxSet,
  Date = "1972-05-04",
  Venue = "Olympia Theatre",
  City = "Paris",
  State = "France",
  OfficialRelease = null,
  AlbumName = null,
  ReleaseYear = null,
  Edition = null,
  BoxSetName = "Enjoying the Ride",
  ArtworkData = [JPEG bytes],
  ArtworkMimeType = "image/jpeg",
  InfoFileContent = "Digital box set download...",
  InfoFileName = "info.txt"
}
```

**AlbumTitle (Computed):**
```
"1972-05-04 - Olympia Theatre - Paris, France: Enjoying the Ride"
```

**Note:** NO space before colon (`:`) - distinguishes from official releases.

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

### Example 4: Official Release (with Space Before Colon)

**AlbumInfo:**
```csharp
AlbumInfo {
  FolderPath = "D:\\Projects\\library\\Grateful Dead\\Concerts\\1977\\1977-05-08 - Barton Hall - Ithaca, NY",
  Artist = "Grateful Dead",
  Type = AlbumType.Live,
  Date = "1977-05-08",
  Venue = "Barton Hall, Cornell University",
  City = "Ithaca",
  State = "NY",
  OfficialRelease = "Dave's Picks Vol. 29",  // ← Official release
  AlbumName = null,
  ReleaseYear = null,
  Edition = null,
  BoxSetName = null,
  ArtworkData = [JPEG bytes],
  ArtworkMimeType = "image/jpeg"
}
```

**AlbumTitle (Computed):**
```
"1977-05-08 - Barton Hall, Cornell University - Ithaca, NY : Dave's Picks Vol. 29"
```

**Note:** SPACE before colon (` :`) - distinguishes from box sets.

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
