# Service Documentation: MetadataService

## Purpose

The **MetadataService** is the core business logic layer for reading and writing ID3 metadata tags in audio files (FLAC/MP3). It handles the critical task of parsing complex album/folder naming conventions to extract structured data (date, venue, city, state, official release names, box set names), detecting segues between songs, extracting embedded dates from track titles, and writing standardized metadata back to audio files. This service enforces the strict **yyyy-MM-dd date format** convention and implements the **space-vs-no-space colon distinction** to differentiate box sets from official releases.

---

## Dependencies

### External Libraries
- **TagLib-Sharp (TagLib.File)** - ID3 tag reading/writing for FLAC and MP3 files
- **System.IO** - File system operations (Directory.GetFiles, Path operations)
- **System.Text.RegularExpressions** - Pattern matching for parsing folder names, album titles, and dates

### Models
- **TrackInfo** - Track-level metadata (FilePath, Title, TrackNumber, DiscNumber, Duration, HasSegue, PerformanceDate, NormalizedTitle, IsModified)
- **AlbumInfo** - Album-level metadata (Date, Venue, City, State, OfficialRelease, BoxSetName, Type, Artist, AlbumTitle, ArtworkData, InfoFileContent)
- **AlbumType** enum - Live, Studio, OfficialRelease, BoxSet

### File System Access
- Reads: `.flac`, `.mp3` (audio files), `.txt` (info files)
- Writes: ID3 tags in audio files (modifies files in-place)

---

## Public Methods

### 1. ReadFolder

**Signature:**
```csharp
public List<TrackInfo> ReadFolder(string folderPath, bool editMode = false)
```

**Purpose:** Reads metadata from all audio files (FLAC and MP3) in a folder, extracts track information, detects segues, and returns ordered track list. Supports two modes: import mode (full transformation pipeline) and edit mode (read-only, no transformations).

**Parameters:**
- `folderPath` (string) - Absolute path to folder containing audio files
- `editMode` (bool, optional, default = false) - If true, reads tags as-is without parsing/transforming (for editing already-imported files). If false, runs full import pipeline with ParseTitleAndDate and DetectSegues.

**Return Value:** `List<TrackInfo>` - Track metadata ordered by track number (1, 2, 3...)

**Mode Behaviors:**

**Import Mode (editMode = false):**
- Runs `ParseTitleAndDate()` to extract song name and date from titles like "Bertha (1971-04-27)"
- Strips segue markers from SongName (segue state captured in HasSegue boolean)
- Runs `DetectSegues()` to auto-identify common segue pairs
- Used when importing new concerts from raw files

**Edit Mode (editMode = true):**
- Reads TITLE tag as-is into SongName (no parsing or transformation)
- FLAC file tags ARE the source of truth (already contain final formatted metadata from previous import)
- Skips ParseTitleAndDate and DetectSegues (transformations already applied during import)
- Used when opening already-imported concerts for editing in MainWindow

**Business Logic:**

1. **File Discovery** (line 19-23):
   - Find all `.flac` files: `Directory.GetFiles(folderPath, "*.flac")`
   - Find all `.mp3` files: `Directory.GetFiles(folderPath, "*.mp3")`
   - Concatenate both lists
   - Sort alphabetically by filename (`OrderBy(f => f)`)

2. **Per-File Processing** (line 25-63):
   - Open file with TagLib-Sharp: `TagLib.File.Create(filePath)`
   - Extract raw title: `file.Tag.Title` (or filename if tag missing)
   - **Parse title and date:** Call `ParseTitleAndDate(rawTitle)` (line 34) to separate song name from trailing date
     - Returns tuple: `(songName, extractedDate)`
     - Handles formats like "Bertha (1971-04-27)" or "Song (1971-04-27 - Venue, City)"
     - Returns null for date if no yyyy-MM-dd pattern found
   - Extract remaining ID3 tag values:
     - **Track Number:** `file.Tag.Track` (cast to int)
     - **Disc Number:** `file.Tag.Disc` (default to 1 if 0 or missing)
     - **Duration:** `file.Properties.Duration` (formatted as "mm:ss")
   - Store parsed values in TrackInfo:
     - **SongName:** Parsed song name from ParseTitleAndDate (line 42)
     - **RawTitle:** Original unmodified title for display (line 43)
     - **TrackDate:** Extracted date or empty string (line 44)
   - Detect segue markers in title: `HasSegueMarker(rawTitle)` (line 50)
   - If no date extracted from title, try album-level date: `ExtractDateFromAlbum(file.Tag.Album)` (lines 52-56)
   - **Important:** Title NOT cleaned here (preserves original metadata for display/library browser)

3. **Track Number Auto-Assignment** (line 66-72):
   - If ALL tracks have track number 0 (no ID3 track numbers)
   - Assign sequential numbers 1, 2, 3... based on alphabetical order

4. **Ordering** (line 74):
   - Sort tracks by track number: `tracks.OrderBy(t => t.TrackNumber)`

5. **Segue Auto-Detection** (line 77):
   - Call `DetectSegues(orderedTracks)` to identify known segue pairs

6. **Error Handling** (line 58-62):
   - Catch exceptions per file (corrupted files, permission errors)
   - Log to Debug output: `Debug.WriteLine($"Error reading {filePath}: {ex.Message}")`
   - Continue processing remaining files (don't fail entire folder)

**What Can Fail:**
- **File locked by another process:** IOException → Skipped, logged to Debug
- **Corrupted audio file:** TagLib exception → Skipped, logged to Debug
- **Missing permissions:** UnauthorizedAccessException → Skipped, logged to Debug
- **Empty folder:** Returns empty list (no error)

**Business Rules:**
- Supports FLAC and MP3 only (other formats ignored)
- Alphabetical filename sort used if no track numbers
- Original titles preserved (cleaning happens during normalization, not here)
- Segue detection runs after all files loaded (needs full track list)

---

### 2. ReadAlbumInfo

**Signature:**
```csharp
public AlbumInfo ReadAlbumInfo(string folderPath, List<TrackInfo> tracks)
```

**Purpose:** Reads album-level metadata from folder name, first audio file's ID3 tags, and any `.txt` info files in the folder.

**Parameters:**
- `folderPath` (string) - Absolute path to album folder
- `tracks` (List<TrackInfo>) - Tracks from `ReadFolder()` (used to get first file)

**Return Value:** `AlbumInfo` - Album metadata with parsed date, venue, official release, box set name, artwork, etc.

**Business Logic:**

1. **Initialize Defaults** (line 123-127):
   - FolderPath = provided path
   - Artist = "Grateful Dead" (default, overridden from ID3 tags)

2. **Parse Folder Name** (line 134):
   - Extract folder name: `Path.GetFileName(folderPath)`
   - Call `ParseFolderName(folderName, albumInfo)` to extract official release, date, venue

3. **Read First File's ID3 Tags** (line 171-241):
   - Get first track's file path: `tracks.FirstOrDefault()?.FilePath`
   - If file exists, open with TagLib-Sharp
   - **Artist:** `file.Tag.FirstPerformer` (fallback to "Grateful Dead")

   **3a. Try Custom FLAC Vorbis Comment Fields First** (line 178-206):
   - For FLAC files, reads custom fields written by WriteMetadata():
     - `ALBUMDATE` → `AlbumInfo.AlbumDate`
     - `VENUE` → `AlbumInfo.Venue`
     - `CITYSTATE` → `AlbumInfo.CityState`
     - `ALBUMNAME` → `AlbumInfo.AlbumName`
     - `ALBUMTYPE` → `AlbumInfo.Type`
   - If `ALBUMDATE` is populated, treats custom fields as authoritative (skips step 3b)

   **3b. Fallback: Parse Standard Tags** (line 208-241):
   - Only runs if no custom fields found
   - **Album Title:** Parse via `ParseAlbumTitle(album, albumInfo)` (line 214)
     - Tries 5 regex patterns to extract date/venue/city/state from ALBUM tag
     - If no patterns match, the ALBUM tag value is lost
   - **FIX 1 (NEW):** Fallback to store raw ALBUM tag in AlbumName (line 216-223)
     - If `ParseAlbumTitle()` didn't set AlbumName, use raw ALBUM tag value
     - Handles official releases like "Blues for Allah 50th Anniversary (2025)"
     - Preserves the album name including year suffix
   - **Date (FIX 2):**
     - If `file.Tag.Year > 0`, attempt to extract full date from album string via `ExtractDateFromAlbum()`
     - **Only set AlbumDate if full yyyy-MM-dd date is found** (no fabrication)
     - Store Year separately in `albumInfo.Year` for display purposes
     - **REMOVED:** No longer falls back to "{Year}-01-01" fake date
     - **Rationale:** Year tag is publication/release year (e.g., 2025), NOT concert date (e.g., 1975-08-12). For official releases, concert dates come from individual track titles.

   **3c. Artwork** (line 243-248):
   - Read `file.Tag.Pictures[0]` if exists
   - Store raw bytes: `albumInfo.ArtworkData = pictures[0].Data.Data`
   - Store MIME type: `albumInfo.ArtworkMimeType = pictures[0].MimeType`

4. **Read Info File** (line 168-183):
   - Find all `.txt` files: `Directory.GetFiles(folderPath, "*.txt")`
   - Take first `.txt` file found (if multiple exist)
   - Read full content: `File.ReadAllText(infoFile)`
   - Store filename and content in `albumInfo` (line 176-177)
   - **Error Handling:** Catch all exceptions, silently ignore (line 180-182)

**What Can Fail:**
- **No tracks provided:** Uses default "Grateful Dead" artist, no year extraction (safe degradation)
- **First file corrupted:** TagLib exception → Caught at caller level (not in this method)
- **Info file unreadable:** Silently ignored (empty InfoFileContent)
- **Info file encoding issues:** May read garbled text (no encoding detection)

**Business Rules:**
- Artist defaults to "Grateful Dead" (hardcoded, line 126, 141)
- First `.txt` file wins (if multiple exist in folder)
- Year-only date expanded to "{Year}-01-01" (conservative default)
- Artwork from first file's first picture (no multi-artwork support)

---

### 3. WriteMetadata

**Signature:**
```csharp
public void WriteMetadata(AlbumInfo album, List<TrackInfo> tracks)
```

**Purpose:** Writes standardized ID3 metadata to all audio files, including title with date suffix, segue markers, track/disc numbers, album, artist, year, and embedded artwork.

**Parameters:**
- `album` (AlbumInfo) - Album-level metadata to write
- `tracks` (List<TrackInfo>) - Track-level metadata to write

**Return Value:** void (modifies files in-place)

**Business Logic:**

1. **Per-Track Processing** (line 193-244):
   - Open file: `TagLib.File.Create(track.FilePath)`
   - **Build Title** (line 198-223):
     - **Date Priority Logic** (line 237-243):
       - 1st priority: `track.PerformanceDate` (track-specific concert date override)
       - 2nd priority: `album.AlbumDate` (album-level concert date)
       - 3rd priority: `album.Year` (release year for studio tracks without concert date)
       - 4th priority: No date suffix (if all three are empty)
     - Use normalized title if exists, else original title (line 245)
     - **Strip existing date suffixes** (line 249):
       - Regex: `@"\s*\(\d{4}-\d{2}-\d{2}\)(\s*\(\d{4}-\d{2}-\d{2}\))*\s*$"`
       - Matches: " (yyyy-MM-dd)" or " (yyyy-MM-dd) (yyyy-MM-dd)" (prevents duplicate dates)
     - **Add segue marker** (line 252-255):
       - If `track.HasSegue == true`, append " >" to title
       - Segue marker comes BEFORE date suffix
     - **Final title format** (line 258-266):
       - If date is available: `"{title} ({date})"`
         - Example: "Dark Star (1972-05-04)" (concert date)
         - Example: "Help on the Way (2025)" (release year for studio track)
         - Example: "China Cat Sunflower > (1972-05-04)" (with segue)
       - If no date: `"{title}"`
         - Example: "Help on the Way" (no date, no year)

2. **Write ID3 Tags** (line 212-222):
   - `file.Tag.Title` = title with date and optional segue
   - `file.Tag.Album` = album.AlbumTitle
   - `file.Tag.Performers` = [album.Artist] (array with single artist)
   - `file.Tag.AlbumArtists` = [album.Artist]
   - `file.Tag.Track` = track.TrackNumber (cast to uint)
   - `file.Tag.Disc` = track.DiscNumber (cast to uint)
   - `file.Tag.Year` = parsed from album.AlbumDate if valid date, else from album.Year if numeric (supports Official Releases with year but no concert date)

3. **Embed Artwork** (line 224-240):
   - If `album.ArtworkData` and `album.ArtworkMimeType` exist:
     - Create `TagLib.Picture` object (line 227-234)
     - Set Type = FrontCover
     - Set MimeType (e.g., "image/jpeg")
     - Set Data (raw byte array)
     - Set Description = "Front Cover"
     - Write: `file.Tag.Pictures = new[] { picture }` (single artwork)
   - If no artwork data:
     - Clear artwork: `file.Tag.Pictures = new TagLib.IPicture[0]` (line 239)

4. **Save File** (line 242):
   - `file.Save()` - Writes all changes to disk

**What Can Fail:**
- **File locked by another process:** IOException → Unhandled (caller must catch)
- **Read-only file:** UnauthorizedAccessException → Unhandled (caller must catch)
- **Disk full:** IOException → Unhandled (caller must catch)
- **Invalid date format:** DateTime.TryParse() returns false → Year tag not written (safe degradation, line 219)

**Business Rules:**
- **Date suffix priority:** Track date > Album date > Release year > No suffix
- **Release year fallback:** Studio tracks with no concert date use `album.Year` for suffix (e.g., "Song (2025)")
- **Duplicate date prevention:** Regex strips existing dates before adding new one
- **Segue marker placement:** Comes BEFORE date suffix (e.g., " > (date)" not "(date) >")
- **Single artwork only:** First picture replaces all existing pictures
- **Artist written to both Performers and AlbumArtists** (standard practice for consistency)

**Important Note:** This method is destructive - modifies audio files in-place with no undo. Caller should confirm with user before calling.

---

## Private Helper Methods

### HasSegueMarker

**Signature:**
```csharp
private bool HasSegueMarker(string title)
```

**Purpose:** Detects if title contains segue marker characters.

**Regex Pattern:** `@"[-–]?>|→|\[>\]"`

**Matches:**
- `>` - Simple greater-than
- `->` - ASCII arrow
- `–>` - En dash arrow
- `→` - Unicode arrow
- `[>]` - Bracketed segue marker

**Returns:** `true` if any marker found, `false` otherwise

**Business Rule:** Multiple marker formats supported for compatibility with various tapers' conventions.

---

### ParseTitleAndDate

**Signature:**
```csharp
public (string songName, string? date) ParseTitleAndDate(string title)
```

**Purpose:** Parse track title to separate song name from trailing date suffix, handling both MusicBrainz format (M/D/YYYY in "Live at..." parentheses) and DeadEditor format (yyyy-MM-dd). This is the primary date extraction method called during ReadFolder to populate SongName and TrackDate fields.

**Parameters:**
- `title` (string) - Full title from ID3 tag (e.g., "Jack Straw (Live at Uptown Theatre, Chicago, IL, 2/1/1978) - Grateful Dead__")

**Return Value:** Tuple of (songName, date)
- `songName` - Song name with date/venue stripped (e.g., "Jack Straw")
- `date` - Date in yyyy-MM-dd format, or null if no date found

**Regex Patterns (checked in order):**

**Pattern 1: MusicBrainz format with M/D/YYYY**
```csharp
@"^(.+?)\s*\(Live (?:at|in) .+?,\s*(\d{1,2})/(\d{1,2})/(\d{4})\)(?:\s*-\s*.+)?$"
```

**Pattern Explanation:**
- `^(.+?)` - Capture group 1: Song name (non-greedy, before opening paren)
- `\s*\(Live (?:at|in) ` - "(Live at..." or "(Live in..." (case-insensitive)
- `.+?,\s*` - Venue/location text ending with comma
- `(\d{1,2})/(\d{1,2})/(\d{4})` - Groups 2-4: M/D/YYYY date
- `\)` - Closing paren
- `(?:\s*-\s*.+)?$` - Optional artist suffix (e.g., " - Grateful Dead__")

**Pattern 2: yyyy-MM-dd date with any suffix**
```csharp
@"^(.+?)\s*\((\d{4}-\d{2}-\d{2})[^)]*\)\s*$"
```

**Pattern Explanation:**
- `^(.+?)` - Capture group 1: Song name (non-greedy). Because it's non-greedy, the regex engine skips parentheticals that don't start with a date (e.g., "(Two Souls In Communion)") and only matches the LAST parenthetical that contains a yyyy-MM-dd date.
- `\s*` - Optional whitespace before paren
- `\(` - Opening parenthesis (escaped)
- `(\d{4}-\d{2}-\d{2})` - Capture group 2: Date in yyyy-MM-dd format
- `[^)]*` - Any remaining text inside parentheses (venue, tour name, location — all discarded)
- `\)` - Closing parenthesis (escaped)
- `\s*$` - Optional trailing whitespace

**Pattern 3: Year-only + tour/album name**
```csharp
@"^(.+?)\s*\((\d{4})\s*[-–]\s*[^)]+\)\s*$"
```

**Pattern Explanation:**
- `^(.+?)` - Capture group 1: Song name (non-greedy, same subtitle-preserving behavior as Pattern 2)
- `\s*\(` - Opening parenthesis
- `(\d{4})` - Capture group 2: 4-digit year (not used — year alone isn't a concert date)
- `\s*[-–]\s*` - Dash separator (hyphen or en-dash) with optional spaces
- `[^)]+` - Tour/album name text (e.g., "Europe '72")
- `\)\s*$` - Closing parenthesis, end of string

**Note:** Pattern 3 strips the year+tour suffix but returns `null` for date because a year alone is not a full concert date.

**Supported Formats:**

1. **MusicBrainz with artist suffix:** `"Jack Straw (Live at Uptown Theatre, Chicago, IL, 2/1/1978) - Grateful Dead__"`
   - Returns: `("Jack Straw", "1978-02-01")`

2. **MusicBrainz without artist suffix:** `"Terrapin Station (Live in Chicago, 1/31/1978)"`
   - Returns: `("Terrapin Station", "1978-01-31")`

3. **Simple yyyy-MM-dd date:** `"Bertha (1971-04-27)"`
   - Returns: `("Bertha", "1971-04-27")`

4. **Date with venue (dash-separated):** `"Mama Tried (1971-04-26 - New York, NY - Fillmore East)"`
   - Returns: `("Mama Tried", "1971-04-26")`

5. **Date with full metadata:** `"Johnny B. Goode (1971-03-24 - San Francisco, CA - Winterland - Skull & Roses)"`
   - Returns: `("Johnny B. Goode", "1971-03-24")`

6. **Full date + tour name (space-separated):** `"He's Gone (1972-05-10 Europe '72)"`
   - Returns: `("He's Gone", "1972-05-10")`

7. **Song with subtitle + full date + tour:** `"The Stranger (Two Souls In Communion) (1972-05-10 Europe '72)"`
   - Returns: `("The Stranger (Two Souls In Communion)", "1972-05-10")`

8. **Year-only + tour name:** `"Good Lovin' (1972 - Europe '72)"`
   - Returns: `("Good Lovin'", null)` — suffix stripped, no full date extracted

9. **Song with subtitle + year-only + tour:** `"The Stranger (Two Souls In Communion) (1972 - Europe '72)"`
   - Returns: `("The Stranger (Two Souls In Communion)", null)`

10. **No date:** `"Radio AD"`
    - Returns: `("Radio AD", null)`

**Business Logic:**
1. Return immediately if title is null/whitespace
2. Try Pattern 1 (MusicBrainz M/D/YYYY format):
   - Extract song name from group 1, trim whitespace
   - **Strip trailing segue markers** from song name (Regex: `@"(\s*[-–]?\s*>)+\s*$"`)
   - Extract month/day/year from groups 2-4
   - Convert to yyyy-MM-dd format
   - Return tuple
3. If no match, try Pattern 2 (yyyy-MM-dd with any suffix):
   - Extract song name from group 1, trim whitespace
   - **Strip trailing segue markers** from song name (Regex: `@"(\s*[-–]?\s*>)+\s*$"`)
   - Extract date from group 2 (already yyyy-MM-dd)
   - Return tuple
4. If no match, try Pattern 3 (year-only + tour name):
   - Extract song name from group 1, trim whitespace
   - **Strip trailing segue markers** from song name
   - Return (songName, null) — suffix stripped but no full date to extract
5. If no match from any pattern:
   - **Strip trailing segue markers** from title
   - Return (cleaned title, null)

**Business Rules:**
- **MusicBrainz priority:** Pattern 1 (M/D/YYYY) checked first to handle MusicBrainz titles
- **Date format conversion:** M/D/YYYY converted to yyyy-MM-dd for consistency
- **Venue stripping:** MusicBrainz "(Live at...)" parenthetical completely removed from song name
- **Artist suffix handling:** " - Artist__" suffix stripped automatically
- **Date-first extraction:** Pulls yyyy-MM-dd date and discards venue/location/album metadata
- **Whitespace handling:** Trims trailing spaces from song name
- **Graceful fallback:** Returns original title unchanged if no date pattern found
- **Prevents date doubling:** ParseTitleAndDate strips existing dates during ReadFolder, preventing duplicate dates when re-importing already-formatted files
- **FIX 1: Segue marker stripping:** All four return paths strip trailing segue markers (" >", " ->", " - >", " –>") from songName using greedy regex `@"(\s*[-–]?\s*>)+\s*$"`. The `+` quantifier handles multiple/doubled segue markers (e.g., "Dark Star > >" → "Dark Star"). The segue state is captured by the HasSegue boolean flag — it should not also live in the SongName string.
- **Public visibility:** ParseTitleAndDate is public so EditMetadataView can call it to clean raw FLAC titles before normalization, giving the normalizer the same clean input it gets in import mode.

**Usage in ReadFolder:**
```csharp
// Example 1: MusicBrainz title
var rawTitle = file.Tag.Title;  // "Jack Straw (Live at Uptown Theatre, Chicago, IL, 2/1/1978) - Grateful Dead__"
var (songName, extractedDate) = ParseTitleAndDate(rawTitle);
// songName = "Jack Straw"
// extractedDate = "1978-02-01"

// Example 2: DeadEditor formatted title
var rawTitle2 = file.Tag.Title;  // "Bertha (1971-04-27 - Fillmore East)"
var (songName2, extractedDate2) = ParseTitleAndDate(rawTitle2);
// songName2 = "Bertha"
// extractedDate2 = "1971-04-27"

// Example 3: Title with segue marker (FIX 1)
var rawTitle3 = file.Tag.Title;  // "Help on the Way > (1975-08-12)"
var (songName3, extractedDate3) = ParseTitleAndDate(rawTitle3);
// songName3 = "Help on the Way" (segue marker stripped!)
// extractedDate3 = "1975-08-12"
// HasSegue flag is set separately by HasSegueMarker(rawTitle3) = true
// DisplayTitle will later append " >" based on HasSegue flag

track.SongName = songName;       // Clean song name (no segue marker)
track.RawTitle = rawTitle;       // Original title preserved for display
track.TrackDate = extractedDate ?? "";  // Date or empty string

// Example 4: Double segue marker (greedy regex fix)
var rawTitle4 = file.Tag.Title;  // "Dark Star > > (1968-02-23)"
var (songName4, extractedDate4) = ParseTitleAndDate(rawTitle4);
// songName4 = "Dark Star" (both segue markers stripped!)
// extractedDate4 = "1968-02-23"
```

**Why This Matters:**
When importing MusicBrainz-tagged files or reimporting already-formatted files (e.g., from a previous DeadEditor export), ParseTitleAndDate prevents the user from having to manually strip dates and venue info. The method automatically extracts the song name and date into separate fields, allowing the import workflow to proceed without manual editing.

---

### ExtractDateFromTitle

**Signature:**
```csharp
private string? ExtractDateFromTitle(string title)
```

**Purpose:** Extracts yyyy-MM-dd date from track title using multiple pattern attempts.

**Return Value:** Date string in "yyyy-MM-dd" format, or null if no date found

**Regex Patterns (in priority order):**

1. **"Filler" Pattern** (line 257-258):
   - Regex: `@"\(Filler:\s*(\d{4}-\d{2}-\d{2})\s*-"`
   - Matches: "Song (Filler: 1972-05-04 - Venue, City)"
   - **Purpose:** Archive.org format for bonus tracks
   - **Example:** "Morning Dew (Filler: 1972-05-04 - Olympia Theatre, Paris, France)"

2. **Trailing Date Pattern** (line 261-262):
   - Regex: `@"\((\d{4}-\d{2}-\d{2})\)\s*$"`
   - Matches: "Song (yyyy-MM-dd)" at end of title
   - **Example:** "Dark Star (1972-05-04)"

3. **Date with Location Pattern** (line 265-266):
   - Regex: `@"\((\d{4}-\d{2}-\d{2})\s*-"`
   - Matches: "Song (yyyy-MM-dd - Location info)"
   - **Example:** "Eyes of the World (1973-11-11 - Winterland)"

4. **US Date in Parentheses** (line 269-277):
   - Regex: `@"\((\d{1,2}/\d{1,2}/\d{2,4})\s+"`
   - Matches: "Song (M/D/YY Venue)" or "(MM/DD/YYYY Venue)"
   - **Conversion:** Parse with `DateTime.TryParse()`, convert to "yyyy-MM-dd"
   - **Example:** "Bertha (5/7/77 Barton Hall)" → "1977-05-07"

5. **US Date in Brackets** (line 280-289):
   - Regex: `@"\[.*?(\d{1,2}/\d{1,2}/\d{2,4}).*?\]"`
   - Matches: "[Venue M/D/YY]" or "[M/D/YY, Venue]"
   - **Non-greedy:** `.*?` captures minimal text before/after date
   - **Example:** "[Bickershaw Festival 5/7/72]" → "1972-05-07"

6. **Trailing US Date in Parentheses** (line 292-300):
   - Regex: `@"(\d{1,2}/\d{1,2}/\d{4})\)"`
   - Matches: "Song (Live at Venue, M/D/YYYY)"
   - **Example:** "(Live at Fillmore East, 5/2/1970)" → "1970-05-02"

**Business Rules:**
- **Priority order matters:** First match wins (filler format checked before generic formats)
- **US date auto-conversion:** M/D/YY and MM/DD/YYYY parsed as DateTime, converted to yyyy-MM-dd
- **Returns null if no match:** Safe fallback (caller uses album-level date)
- **Case-insensitive:** "Filler" pattern uses `RegexOptions.IgnoreCase`

**Error Handling:**
- If `DateTime.TryParse()` fails (invalid US date), returns null (pattern skipped)

---

### ExtractDateFromAlbum

**Signature:**
```csharp
private string? ExtractDateFromAlbum(string? album)
```

**Purpose:** Extracts date from album tag if it starts with yyyy-MM-dd format.

**Regex Pattern:** `@"^(\d{4}-\d{2}-\d{2})"`

**Matches:** Album tags starting with date:
- "1972-05-04 - Olympia Theatre..."
- "1977-05-08 - Barton Hall, Cornell University..."

**Returns:** Date string if match found, null otherwise

**Business Rule:** Album tag often contains full show info starting with date (standard taper convention).

---

### DetectSegues

**Signature:**
```csharp
private void DetectSegues(List<TrackInfo> tracks)
```

**Purpose:** Auto-detects segues based on known consecutive song pairs in Grateful Dead repertoire.

**Parameters:**
- `tracks` (List<TrackInfo>) - Ordered track list (must be in performance order)

**Business Logic:**

1. **Known Segue Pairs** (line 85-98):
   - Dictionary<string, string> with case-insensitive comparison
   - **Key:** First song in pair
   - **Value:** Second song in pair

   **Pairs Defined:**
   ```csharp
   "China Cat Sunflower" → "I Know You Rider"
   "China Cat" → "I Know You Rider"
   "Scarlet Begonias" → "Fire on the Mountain"
   "Scarlet" → "Fire on the Mountain"
   "Help on the Way" → "Slipknot!"
   "Slipknot!" → "Franklin's Tower"
   "Lost Sailor" → "Saint of Circumstance"
   "Playing in the Band" → "Uncle John's Band"
   "Estimated Prophet" → "Eyes of the World"
   "Drums" → "Space"
   "Space" → "The Other One"
   ```

2. **Pair Matching** (line 100-115):
   - Loop through tracks (i = 0 to count - 2)
   - Compare current track title with next track title
   - Case-insensitive match: `StringComparison.OrdinalIgnoreCase`
   - If match found, set `tracks[i].HasSegue = true`

**Side Effects:** Modifies `HasSegue` property on track objects

**Business Rules:**
- Only sets segue on FIRST song of pair (not both)
- Case-insensitive matching (handles "china cat" vs "China Cat")
- Requires exact title match (no fuzzy matching here)
- Matches both full title and abbreviated versions (e.g., "Scarlet" or "Scarlet Begonias")

**Limitation:** Hardcoded Grateful Dead segues. For other artists, this method does nothing useful (no pairs match).

---

### CleanTitle

**Signature:**
```csharp
private string CleanTitle(string title)
```

**Purpose:** Strips track numbers, dates, segue markers, and location info from title to get base song name.

**Return Value:** Cleaned title string

**Regex Operations (in sequence):**

1. **Remove Track Numbers** (line 317):
   - Regex: `@"^\d+[\s\.\-_]+"`
   - Matches: Leading numbers followed by space, dot, dash, or underscore
   - **Examples:** "01 ", "1. ", "001-", "1_", "12 "

2. **Remove Date Suffix with Optional Segue** (line 320):
   - Regex: `@"\s*→?\s*\(\d{4}-\d{2}-\d{2}\)\s*$"`
   - Matches: Optional arrow + " (yyyy-MM-dd)" at end
   - **Examples:** " (1972-05-04)", " → (1972-05-04)"

3. **Remove Date with Location** (line 323):
   - Regex: `@"\s*\(\d{4}-\d{2}-\d{2}\s*-\s*[^)]+\)\s*$"`
   - Matches: " (yyyy-MM-dd - Location info)" at end
   - **Example:** " (1972-05-04 - Olympia Theatre)"

4. **Remove Live At/In Parentheses** (line 326):
   - Regex: `@"\s*\(Live (?:at|in) [^)]+\)\s*$"`
   - Case-insensitive: `RegexOptions.IgnoreCase`
   - **Examples:** " (Live at Fillmore East)", " (live in San Francisco)"

5. **Remove Live At/In Brackets** (line 329):
   - Regex: `@"\s*\[Live (?:at|in) [^\]]+\]\s*$"`
   - **Examples:** " [Live at Winterland]", " [LIVE IN NYC]"

6. **Remove Segue Markers** (line 332):
   - Regex: `@"\s*(\[?>?\]?|[-–]?\s*>\s*)\s*$"`
   - **Matches:**
     - " >", " -> ", " [>]", " →"
     - En dash variants: " –>"
     - With or without spaces

7. **Remove Reprise Tag** (line 335):
   - Regex: `@"\s*[\[(]Reprise[\])]\s*$"`
   - Case-insensitive
   - **Examples:** " (Reprise)", " [reprise]", " [REPRISE]"

8. **Trim Trailing Characters** (line 337):
   - `TrimEnd('→', ' ', '-', '–', '>', '[', ']')`
   - Removes any leftover punctuation/whitespace

**Return:** Cleaned title

**Use Case:** Called by normalization service to prepare title for fuzzy matching (not called during ReadFolder anymore per line 51-53 comment).

---

### ParseFolderName

**Signature:**
```csharp
private void ParseFolderName(string folderName, AlbumInfo info)
```

**Purpose:** Extracts date, venue, city, state, and official release name from folder name using generic multi-pattern approach.

**Parameters:**
- `folderName` (string) - Just the folder name (not full path)
- `info` (AlbumInfo) - Object to populate with extracted data (modified in-place)

**Business Logic (4-step process):**

**Step 1: Extract Official Release Name** (line 352-366)

Regex:
```regex
(?:Dave's Picks|Dick's Picks|Road Trips|Download Series|Spring \d{4}|Here Comes Sunshine)
\s*,?\s*Vol(?:ume|\.)?\s+\d+(?:\s+No\.\s+\d+)?
```

**Matches:**
- "Dave's Picks Vol. 1"
- "Road Trips Vol. 3 No. 4"
- "Road Trips, Vol. 3 No. 4" (with comma)
- "Spring 1990 Vol. 2"

**Action:**
- Set `info.OfficialRelease` to matched string
- Remove release name from `remainingText` for further parsing
- Strip trailing " - " separators

**Step 2: Extract Date(s)** (line 368-383)

Regex: `@"(\d{4}-\d{2}-\d{2}(?:\s*[,\-]\s*\d{4}-\d{2}-\d{2})*)"`

**Matches:**
- Single date: "1973-11-30"
- Multiple dates: "1973-11-30, 1973-12-02"
- Date range: "1973-11-30 - 1973-12-02"

**Action:**
- If multiple dates found, take FIRST date for `info.Date`
- Remove entire date string from `remainingText`

**Step 3: Remove Artist Name** (line 385-391)

**Patterns:**
- "Grateful Dead - " (case-insensitive)
- "New Riders of the Purple Sage - " (case-insensitive)
- Generic: "{any text} - " (removes first dash-separated segment)

**Action:** Strip artist prefix from `remainingText`

**Step 4: Parse Venue/City/State** (line 393-440)

Three pattern attempts (in priority order):

**Pattern A: "Venue - City, State"** (line 400-406)
- Regex: `@"^(.+?)\s*-\s*([^,]+),\s*(.+)$"`
- **Example:** "Boston Music Hall - Boston, MA"
- **Extracts:** Venue, City, State

**Pattern B: "City, State - Venue"** (line 410-416)
- Regex: `@"^([^,]+),\s*([^-]+?)\s*-\s*(.+)$"`
- **Example:** "Boston, MA - Music Hall"
- **Extracts:** City, State, Venue

**Pattern C: Fallback Splitting** (line 420-438)
- Split on " - " or ", "
- **1 part:** Assume it's venue only
- **2 parts:** Assume "City, State"
- **3+ parts:** Assume "Venue, City, State"

**Side Effects:** Populates `info.Date`, `info.Venue`, `info.City`, `info.State`, `info.OfficialRelease`

**Business Rules:**
- Official release extracted FIRST (most distinctive pattern, prevents confusion with dashes in venue names)
- First date wins if multiple dates present (primary concert date)
- Artist name removal supports both Grateful Dead and NRPS explicitly
- Generic artist removal as fallback (any text before first dash)
- Venue/City/State parsing tries multiple patterns (handles inconsistent taper naming)

---

### ParseAlbumTitle

**Signature:**
```csharp
private void ParseAlbumTitle(string album, AlbumInfo info)
```

**Purpose:** Parses Album ID3 tag to extract date, venue, city, state, and detect album type (Live, BoxSet, OfficialRelease) based on **colon format**.

**Parameters:**
- `album` (string) - Album tag value from ID3
- `info` (AlbumInfo) - Object to populate (modified in-place)

**Business Logic (5 patterns, checked in priority order):**

---

#### Pattern 1: Box Set Format (line 448-462)

**Regex:**
```regex
^(\d{4}-\d{2}-\d{2})\s*-\s*([^-]+)\s*-\s*([^,]+),\s*([^:\s]+)(?<!\s):\s*(.+)$
```

**Key Feature:** `(?<!\s):` - Negative lookbehind ensures NO space before colon

**Matches:** "1972-09-15 - Boston Music Hall - Boston, MA: Enjoying the Ride"

**Breakdown:**
- Group 1: `\d{4}-\d{2}-\d{2}` - Date (yyyy-MM-dd)
- Group 2: `[^-]+` - Venue (everything up to next dash)
- Group 3: `[^,]+` - City (everything up to comma)
- Group 4: `[^:\s]+` - State (everything up to colon, no space allowed before colon)
- `(?<!\s):` - Colon with NO space before it (critical distinction)
- Group 5: `.+` - Box set name (everything after colon)

**Sets:**
- `info.Date`, `info.Venue`, `info.City`, `info.State`, `info.BoxSetName`
- `info.Type = AlbumType.BoxSet`

**Returns:** Early return (pattern match successful)

---

#### Pattern 2: Official Release Format (line 466-480)

**Regex:**
```regex
^(\d{4}-\d{2}-\d{2})\s*-\s*([^-]+)\s*-\s*([^,]+),\s*([^:]+?)\s:\s*(.+)$
```

**Key Feature:** `\s:` - Space BEFORE colon (distinguishes from box set)

**Matches:** "1972-09-15 - Boston Music Hall - Boston, MA : Dave's Picks Vol. 1"

**Breakdown:**
- Groups 1-4: Same as Pattern 1 (Date, Venue, City, State)
- `\s:` - Space before colon (official release marker)
- Group 5: `.+` - Official release name

**Sets:**
- `info.Date`, `info.Venue`, `info.City`, `info.State`, `info.OfficialRelease`
- `info.Type = AlbumType.Live`

**Returns:** Early return

---

#### Pattern 3: Basic Live Recording (line 484-496)

**Regex:**
```regex
^(\d{4}-\d{2}-\d{2})\s*-\s*([^-]+)\s*-\s*([^,]+),\s*(.+)$
```

**No Colon:** Plain live recording format

**Matches:** "1972-09-15 - Boston Music Hall - Boston, MA"

**Breakdown:**
- Group 1: Date
- Group 2: Venue
- Group 3: City
- Group 4: State (everything after comma)

**Sets:**
- `info.Date`, `info.Venue`, `info.City`, `info.State`
- `info.Type = AlbumType.Live`

**Returns:** Early return

---

#### Pattern 4: Alternative Format (line 499-523)

**Regex:**
```regex
^([^,]+),\s*([^,]+),\s*([A-Z]{2})\s*\(([^)]+)\)
```

**Matches:** "Venue, City, State (M/D/YY & M/D/YY) [Live]"

**Breakdown:**
- Group 1: Venue
- Group 2: City
- Group 3: State (2-letter uppercase: `[A-Z]{2}`)
- Group 4: Date string in parentheses (may contain multiple dates)

**Date Extraction:**
- Regex within parentheses: `@"(\d{1,2}/\d{1,2}/\d{2,4})"`
- Matches first US-format date (M/D/YY or MM/DD/YYYY)
- Converts to yyyy-MM-dd via `DateTime.TryParse()`

**Official Release Detection:**
- Looks for `[text]` after venue/city/state
- Ignores generic `[Live]` tag
- Extracts other bracketed text as official release name

**Sets:**
- `info.Venue`, `info.City`, `info.State`
- `info.Date` (converted from US format)
- `info.OfficialRelease` (if bracketed text exists and not "Live")

---

#### Pattern 5: Space-Separated Audience Recording

**Regex:**
```regex
^(\d{4}-\d{2}-\d{2})\s+(.+?),\s+([^,]+),\s*([A-Z]{2})$
```

**No Dashes:** Date followed by space (not " - "), comma-separated venue/city/state

**Matches:**
- `"1968-08-21 Fillmore West, San Francisco, CA"`
- `"1977-05-08 Barton Hall, Cornell University, Ithaca, NY"`

**Breakdown:**
- Group 1: `\d{4}-\d{2}-\d{2}` - Date (yyyy-MM-dd)
- `\s+` - Space separator (not dash)
- Group 2: `.+?` - Venue (non-greedy, up to first comma that leads to a valid City, ST ending)
- Group 3: `[^,]+` - City
- Group 4: `[A-Z]{2}` - State (2-letter uppercase)

**Sets:**
- `info.AlbumDate`, `info.Venue`, `info.CityState`
- `info.Type = AlbumType.AudienceRecording`

**Returns:** Early return

---

## Critical Business Rules

### 1. Box Set vs Official Release Colon Format

**Rule:** Colon spacing distinguishes album types

**Box Set:**
- Format: `{Date} - {Venue} - {City}, {State}:{BoxSetName}`
- **NO space before colon:** "MA:Enjoying the Ride"
- Sets `Type = AlbumType.BoxSet`
- Stores name in `BoxSetName` property

**Official Release:**
- Format: `{Date} - {Venue} - {City}, {State} : {ReleaseName}`
- **Space before colon:** "MA : Dave's Picks Vol. 1"
- Sets `Type = AlbumType.Live` (official releases are a subtype of live)
- Stores name in `OfficialRelease` property

**Rationale:** Single-character distinction enables dual use of colon separator without ambiguity. User must format album tags carefully.

**Implementation:** Negative lookbehind `(?<!\s):` vs explicit `\s:` in regex patterns (line 450 vs 468)

---

### 2. Date Format Standardization

**Rule:** All dates must be yyyy-MM-dd (ISO 8601) format

**Enforcement:**
- Folder names expected in yyyy-MM-dd
- Album tags expected in yyyy-MM-dd
- US dates (M/D/YY, MM/DD/YYYY) auto-converted via DateTime.TryParse()
- Written to ID3 tags as yyyy-MM-dd suffix: "Song Title (1972-05-04)"

**Rationale:**
- Eliminates MM/DD/YYYY vs DD/MM/YYYY ambiguity
- Consistent alphabetical sorting
- International compatibility

---

### 3. Segue Marker Placement

**Rule:** Segue marker " >" appears BEFORE date suffix in final title

**Format:** `"{Title} > ({Date})"`

**Examples:**
- "China Cat Sunflower > (1972-05-04)"
- "Scarlet Begonias > (1977-05-08)"

**Rationale:** Date is always last element, segue marker indicates connection to next track.

**Implementation:** Line 206-211 in WriteMetadata()

---

### 4. Duplicate Date Prevention

**Rule:** Strip ALL existing date suffixes before adding new one

**Regex:** `@"\s*\(\d{4}-\d{2}-\d{2}\)(\s*\(\d{4}-\d{2}-\d{2}\))*\s*$"`

**Matches:** One or more consecutive date suffixes

**Prevents:**
- "Song (1972-05-04) (1972-05-04)" (double dates)
- "Song (1970-01-01) (1972-05-04)" (old + new dates)

**Implementation:** Line 203 in WriteMetadata()

---

### 5. File Type Support

**Rule:** Only FLAC and MP3 files processed

**Reasoning:**
- FLAC: Lossless format preferred by tapers
- MP3: Widely compatible lossy format
- Other formats (WAV, ALAC, OGG) not supported by TagLib-Sharp in this context or not commonly used

**Implementation:** Line 20-21 in ReadFolder()

---

### 6. Track Number Auto-Assignment

**Rule:** If ALL tracks have no track number (0), assign sequential numbers 1, 2, 3...

**Trigger:** `tracks.All(t => t.TrackNumber == 0)`

**Assignment:** Based on alphabetical filename order

**Rationale:** Untagged files common in older taper collections; alphabetical order usually matches performance order.

**Implementation:** Line 66-72 in ReadFolder()

---

### 7. Error Isolation

**Rule:** File-level errors don't fail entire folder

**Behavior:**
- Corrupted file → Skip, log to Debug, continue
- Locked file → Skip, log to Debug, continue
- Invalid tags → Use filename as fallback for title

**Rationale:** Large concert folders may have one bad file; don't block entire import.

**Implementation:** Try/catch in ReadFolder() line 58-62

---

### 8. Artwork Handling

**Rule:** Single artwork per album (first picture from first file)

**Read:** First picture from `file.Tag.Pictures[0]` (line 159-164)

**Write:** Replaces ALL existing pictures with single front cover (line 227-234)

**Clear:** If no artwork data, sets empty array (line 239)

**Rationale:** Multiple artwork per track wastes space; one album art sufficient.

---

### 9. Info File Discovery

**Rule:** First .txt file found in folder becomes info file

**Behavior:**
- `Directory.GetFiles(folderPath, "*.txt")` finds all
- Takes `txtFiles[0]` (first alphabetically)
- Reads full content (no size limit)
- Errors silently ignored (empty content if unreadable)

**Rationale:** Most folders have 0 or 1 .txt file; if multiple exist, first is arbitrary but deterministic.

**Implementation:** Line 169-183 in ReadAlbumInfo()

---

### 10. Artist Default

**Rule:** "Grateful Dead" is hardcoded default artist

**Locations:**
- ReadAlbumInfo() line 126: Initial default
- ReadAlbumInfo() line 141: Fallback if FirstPerformer is null

**Override:** ID3 tag `FirstPerformer` overrides default (line 141)

**Limitation:** Not artist-agnostic at service layer (default assumes Grateful Dead). For other artists, user must ensure ID3 tags have correct performer.

---

## Error Handling Summary

### What Can Fail (and what happens)

| Failure Scenario | Method | Behavior |
|------------------|--------|----------|
| Corrupted audio file | ReadFolder | Caught, logged to Debug, file skipped |
| File locked by another process | ReadFolder, WriteMetadata | ReadFolder: skip; WriteMetadata: unhandled exception |
| Missing file permissions | ReadFolder, WriteMetadata | ReadFolder: skip; WriteMetadata: unhandled exception |
| Invalid date in title | ExtractDateFromTitle | DateTime.TryParse fails → returns null, fallback to album date |
| Invalid date in album | ExtractDateFromAlbum | No match → returns null, fallback to {Year}-01-01 |
| No .txt files in folder | ReadAlbumInfo | Silently ignored, InfoFileContent remains empty |
| Unreadable .txt file | ReadAlbumInfo | Exception caught, silently ignored |
| No track numbers in files | ReadFolder | Auto-assign sequential 1, 2, 3... |
| Empty folder (no audio files) | ReadFolder | Returns empty list (not an error) |
| Folder name doesn't match patterns | ParseFolderName | Partial extraction (some fields remain default/empty) |
| Album tag doesn't match patterns | ParseAlbumTitle | No fields populated (silently fails to parse) |
| Disk full during write | WriteMetadata | IOException unhandled (caller must catch) |

**Handled Gracefully:**
- Corrupted files
- Missing info files
- Missing track numbers
- Invalid dates (fallback to album-level or year-only dates)

**Unhandled (caller must handle):**
- Write failures (disk full, read-only files, locked files)
- Folder doesn't exist (Directory.GetFiles throws)

---

## Known Limitations

1. **Hardcoded Grateful Dead Artist Default** - Service not fully artist-agnostic at initialization level

2. **Hardcoded Segue Pairs** - DetectSegues() only knows Grateful Dead song pairs (useless for other artists)

3. **Single Artwork Only** - No support for multiple pictures per album

4. **No Undo** - WriteMetadata() modifies files in-place with no backup

5. **No Multi-Disc Album Title Parsing** - ParseAlbumTitle() doesn't handle disc numbers (relies on ID3 Disc tag)

6. **First .txt File Wins** - No prioritization logic if multiple info files exist

7. **US Date Format Only** - Non-US date formats (DD/MM/YYYY) not detected

8. **Case-Sensitive Colon Format** - Space-before-colon distinction requires exact formatting (no fuzzy detection)

9. **No Encoding Detection** - Info files assumed to be UTF-8 or system default (may read garbled text for other encodings)

10. **No Validation of Folder Structure** - Accepts any folder path (doesn't check if it's actually a concert folder)

---

## File Path

**Source:** [Services/MetadataService.cs](Services/MetadataService.cs)

**Lines of Code:** 527

---

## Related Documentation

- [01-main-window.md](documentation/01-main-window.md) - Main window uses MetadataService for import workflow
- [12-normalization-service.md](documentation/12-normalization-service.md) - Uses CleanTitle() for fuzzy matching (service separated)
- [13-library-import-service.md](documentation/13-library-import-service.md) - Uses ReadFolder/ReadAlbumInfo for library scanning

---

**Last Updated:** 2026-03-01
**Status:** Complete service documentation
