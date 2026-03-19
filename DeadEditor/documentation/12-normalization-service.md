# Service Documentation: NormalizationService

## Purpose

The **NormalizationService** implements fuzzy title matching using **Levenshtein distance algorithm** to normalize messy track titles from taper recordings and MusicBrainz into canonical song names. It handles typos, alternative spellings, date suffixes, venue info, artist suffixes, segue markers, special characters, and remaster tags through a multi-stage cleaning and matching pipeline. The service loads `Data/songs.json` into memory, builds a case-insensitive alias lookup dictionary, and provides song database management (add songs, get all titles/artists).

---

## Dependencies

### External Libraries
- **Newtonsoft.Json** - JSON serialization/deserialization for `songs.json`
- **System.IO** - File system operations

### Data Models
- **SongDatabase** - Root structure: `{ Songs: List<SongEntry>, Artists: List<ArtistEntry> }`
- **SongEntry** - `{ OfficialTitle: string, Aliases: List<string> }`
- **ArtistEntry** - `{ Name: string, Songs: List<SongEntry> }`
- **TrackInfo** - Track model with `Title`, `NormalizedTitle` properties

### File System Access
- **Reads:** `Data/songs.json` (on initialization and after AddSong)
- **Writes:** `Data/songs.json` (when AddSong called)

---

## Public Methods

### 1. Constructor

**Signature:**
```csharp
public NormalizationService()
```

**Purpose:** Initializes service by loading song database and building alias lookup dictionary.

**Business Logic:**
1. Calls `LoadDatabase()` immediately (line 17)
2. Loads `Data/songs.json` from `{AppDirectory}/Data/songs.json` (line 22)
3. If file doesn't exist, creates empty database structure (line 30)
4. Builds `_aliasLookup` dictionary (case-insensitive) from:
   - **Artist-based structure** (new format): `database.Artists[].Songs[]` (line 37-53)
   - **Legacy structure**: `database.Songs[]` (backward compatibility, line 56-69)
5. Each official title maps to itself (line 44, 61)
6. Each alias maps to official title (line 49, 66)

**Dictionary Structure:**
```
{
  "Dark Star" → "Dark Star",
  "Darkstar" → "Dark Star",
  "Dark Star ->" → "Dark Star",
  "Morning Dew" → "Morning Dew",
  "Morning Doo" → "Morning Dew" (typo alias)
}
```

**Case Insensitivity:** Uses `StringComparer.OrdinalIgnoreCase` (line 34)

---

### 2. Normalize

**Signature:**
```csharp
public string? Normalize(string title)
```

**Purpose:** Normalize single track title to canonical song name using multi-stage cleaning and fuzzy matching.

**Parameters:**
- `title` (string) - Raw track title from ID3 tag or filename

**Return Value:**
- `string` - Canonical song name if match found
- `null` - No match found (unknown song)

**Business Logic (15-stage pipeline):**

#### Stage 1: Basic Cleaning (line 80-82)
- Remove tape splice markers: `//`
- Trim whitespace

#### Stage 2: Apostrophe Normalization (line 85-88)
```csharp
.Replace("'", "'")  // Curly apostrophe → straight
.Replace("'", "'")  // Another variant
.Replace("`", "'")  // Backtick → apostrophe
```

**Rationale:** Different text sources use different apostrophe characters.

#### Stage 3: Date Normalization and Metadata Stripping (line 93-157)

**Date Convention:** All dates normalized to `(yyyy-MM-dd)` format. Venue info stripped, dates preserved and normalized.

**Patterns Processed (in order):**

1. **Filler Pattern** (line 94-97):
   - Regex: `@"\s*\(Filler:\s*\d{4}-\d{2}-\d{2}\s*-\s*[^)]+\)\s*$"`
   - Example: "Song (Filler: 1972-05-04 - Olympia Theatre)"
   - **Action:** Strip entirely (filler metadata, not part of title)

2. **Slash Date with Venue in Parentheses** (line 100-115):
   - Patterns matched:
     * `(yyyy/MM/dd Venue)` → `(yyyy-MM-dd)`
     * `(M/d/yy Venue)` → `(yyyy-MM-dd)`
     * `(MM/DD/YYYY Venue)` → `(yyyy-MM-dd)`
   - Example: "Drums (1971/07/02 Filmore West)" → "Drums (1971-07-02)"
   - Example: "Not Fade Away (5/7/77 Barton Hall)" → "Not Fade Away (1977-05-07)"
   - Example: "Good Loving' (1971/07/02)" → "Good Loving' (1971-07-02)"
   - **Action:** Parse date, strip venue, convert to yyyy-MM-dd, re-add to title

3. **Dash Date with Location** (line 117-120):
   - Regex: `@"\s*\(\d{4}-\d{2}-\d{2}\s*-\s*[^)]+\)\s*$"`
   - Example: "Song (1972-05-04 - Boston)" → "Song (1972-05-04)"
   - **Action:** Already yyyy-MM-dd format, just strip venue suffix

4. **Dash Date Only** (line 123-126):
   - Regex: `@"\s*\(\d{4}-\d{2}-\d{2}\)\s*$"`
   - Example: "Song (1972-05-04)"
   - **Action:** Already normalized, no change needed

5. **Remaster Tags in Parentheses** (line 118-121):
   - Regex: `@"\s*\((?:\d{4}\s+)?Remastere?d?\)\s*$"`
   - Case-insensitive
   - Examples: "(Remaster)", "(2003 Remastered)", "(Remastered)"

6. **Remaster Tags in Brackets** (line 124-127):
   - Regex: `@"\s*\[(?:\d{4}\s+)?Remastere?d?\]\s*$"`
   - Example: "[2003 Remaster]"

7. **US Date/Venue in Brackets** (line 120-124):
   - Regex: `@"\s*\[\d{1,2}/\d{1,2}/\d{2,4}[,\s].*$"`
   - Example: "[5/7/72, Bickershaw Festival]"

8. **Artist Suffix Removal** (line 126-132):
   - Regex: `@"\s*-\s*[^-]+_{0,2}\s*$"`
   - Examples: " - Grateful Dead__", " - Grateful Dead", " - Artist Name"
   - **Critical:** MUST come BEFORE "(Live at...)" removal
   - **Rationale:** MusicBrainz titles have format "Song (Live at Venue) - Artist__" where artist suffix prevents (Live...) regex from matching end-of-string

9. **Live At/In Brackets** (line 134-138):
   - Regex: `@"\s*\[Live (?:at|in) [^\]]+\]\s*$"`
   - Example: "[Live at Fillmore East]"

10. **Live At/In Parentheses** (line 140-144):
   - Regex: `@"\s*\(Live (?:at|in) [^)]+\)\s*$"`
   - Example: "(Live at Winterland)"

11. **Venue/Date in Brackets** (line 146-150):
    - Regex: `@"\s*\[[^\]]*\d{1,2}/\d{1,2}/\d{2,4}\]\s*$"`
    - Example: "[Kiel Opera House, St. Louis, MO 10/24/70]"

12. **Segue Markers** (line 152-156):
    - Regex: `@"\s*(\[?>?\]?|[-–]?\s*>\s*)\s*$"`
    - Matches: `>`, `->`, `–>`, `→`, `[>]`

#### Stage 4: Direct Lookup (line 160-163)
- Check `_aliasLookup` for exact match (case-insensitive)
- **Fast path:** Most matches found here after cleaning

#### Stages 5-8: Progressive Stripping (line 167-218)

Each stage strips additional patterns, then checks lookup:

5. **Strip Date/Venue Again** (line 167-175): `[M/D/YY, Venue` pattern
6. **Strip Live Info Again** (line 178-186): `[Live at...]` pattern
7. **Strip Date+Location+Segue** (line 189-202): Combined removal
8. **Strip Date+Segue** (line 205-218): Final date/segue removal

**Rationale:** Some titles have nested or repeated patterns not caught by initial cleaning.

#### Stage 9: Dash Normalization (line 221-230)
```csharp
.Replace("–", "-")  // en-dash → hyphen
.Replace("—", "-")  // em-dash → hyphen
.Replace("−", "-")  // minus sign → hyphen
.Replace("─", "-")  // box-drawing → hyphen
```

**Lookup:** Check `_aliasLookup` after normalization

**Rationale:** Different sources use different dash characters (Unicode variants, box-drawing characters from ASCII art).

#### Stage 10: Suffix Removal (line 233-243)
```csharp
.Replace(" (1)", "")
.Replace(" (2)", "")
.Replace(" Reprise", "")
.Replace(" reprise", "")
```

**Lookup:** Check `_aliasLookup`

**Rationale:** Handles "Song (1)", "Song (2)", "Song Reprise" variants.

#### Stage 11: Normalized Dashes + Suffixes (line 246-256)
- Combine dash normalization and suffix removal
- **Lookup:** Final exact match attempt

#### Stage 12: Fuzzy Matching (line 259-263)
- Call `FindFuzzyMatch(cleaned)` as **last resort**
- Uses Levenshtein distance algorithm
- Handles typos (max 2 characters or 20% of string length)

#### Stage 13: No Match (line 265)
- Return `null` if all stages fail

**Total Stages:** 14 (1-3 cleaning, 4-13 progressive matching, 14 failure)

**Return:** First match found in pipeline order

---

### 3. FindFuzzyMatch

**Signature:**
```csharp
private string? FindFuzzyMatch(string input)
```

**Purpose:** Find best match using Levenshtein distance when exact matching fails.

**Algorithm:**

1. **Initialize** (line 276-277):
   - `bestDistance = int.MaxValue` (no match yet)
   - `bestMatch = null`

2. **Iterate All Aliases** (line 280):
   - Loop through `_aliasLookup` dictionary (all official titles + aliases)

3. **Calculate Distance** (line 282):
   - `LevenshteinDistance(input, kvp.Key)` - Edit distance

4. **Distance Threshold** (line 287):
   ```csharp
   maxAllowedDistance = Math.Min(2, (int)(kvp.Key.Length * 0.2))
   ```
   - **Max 2 characters different** OR
   - **20% of string length** (whichever is smaller)

5. **Track Best Match** (line 289-293):
   - If distance ≤ threshold AND better than current best
   - Update `bestDistance` and `bestMatch`

**Return:** Official title with smallest distance, or `null` if none within threshold

**Example Matching:**
- Input: "Dnacing in the Street" (typo: "Dnacing")
- Distance to "Dancing in the Street": 1 (replace 'n' with 'a')
- Threshold: min(2, 23 * 0.2) = min(2, 4.6) = 2
- Match: Distance 1 ≤ 2 → **Match found**

---

### 4. LevenshteinDistance

**Signature:**
```csharp
private int LevenshteinDistance(string s1, string s2)
```

**Purpose:** Calculate minimum edit operations (insert, delete, replace) to transform s1 into s2.

**Algorithm: Dynamic Programming**

```csharp
s1 = s1.ToLowerInvariant();  // Case-insensitive
s2 = s2.ToLowerInvariant();

int n = s1.Length;
int m = s2.Length;
int[,] d = new int[n + 1, m + 1];  // 2D array

// Base cases: empty string to string
if (n == 0) return m;  // Insert all chars from s2
if (m == 0) return n;  // Delete all chars from s1

// Initialize first column/row
for (int i = 0; i <= n; i++)
    d[i, 0] = i;  // Delete i characters
for (int j = 0; j <= m; j++)
    d[0, j] = j;  // Insert j characters

// Fill matrix
for (int i = 1; i <= n; i++)
{
    for (int j = 1; j <= m; j++)
    {
        int cost = (s2[j - 1] == s1[i - 1]) ? 0 : 1;  // Match vs replace
        d[i, j] = Math.Min(
            Math.Min(d[i - 1, j] + 1,      // Delete from s1
                     d[i, j - 1] + 1),     // Insert into s1
            d[i - 1, j - 1] + cost);       // Replace (or match if cost=0)
    }
}

return d[n, m];  // Bottom-right cell = edit distance
```

**Example: "cat" → "cart"**
```
    ""  c  a  r  t
""   0  1  2  3  4
c    1  0  1  2  3
a    2  1  0  1  2
t    3  2  1  1  1
```
Distance = 1 (insert 'r')

**Complexity:**
- **Time:** O(n × m) where n, m are string lengths
- **Space:** O(n × m) for matrix

**Case Insensitivity:** Strings converted to lowercase before comparison (line 304-305)

---

### 5. NormalizeAll

**Signature:**
```csharp
public int NormalizeAll(List<TrackInfo> tracks)
```

**Purpose:** Normalize all tracks in a list, count successful matches.

**Parameters:**
- `tracks` (List<TrackInfo>) - Track list to normalize

**Return Value:** `int` - Count of tracks matched

**Business Logic:**
```csharp
int matched = 0;
foreach (var track in tracks)
{
    // First, normalize any slash-formatted dates in the title
    // This ensures dates like "(1971/07/02 Filmore West)" become "(1971-07-02)"
    var dateNormalizedTitle = NormalizeDateInTitle(track.Title);
    if (dateNormalizedTitle != track.Title)
    {
        track.Title = dateNormalizedTitle;  // Update the title with normalized date
    }

    // Then normalize the song name for matching
    var normalized = Normalize(track.Title);
    if (normalized != null)
    {
        track.SongName = normalized;   // Set normalized song name
        track.IsMatched = true;        // Mark as matched for UI highlighting
        matched++;
    }
    else
    {
        track.IsMatched = false;       // Mark as unmatched for UI highlighting (gold color)
    }
}
return matched;
```

**Side Effects:**
- Modifies `track.Title` to normalize slash-formatted dates to yyyy-MM-dd format
- Modifies `track.SongName` for each successful match with the canonical song name from the database
- Sets `track.IsMatched = true` for matched songs (displayed in white in import grid)
- Sets `track.IsMatched = false` for unmatched songs (displayed in gold #D7BA7D in import grid to indicate they need attention)
- Tracks start with `IsMatched = null` (not yet normalized), so only tracks that have been through normalization will have true/false values

**Use Case:** MainWindow calls this after loading folder to normalize all tracks at once. Also called when user clicks "Normalize All Songs" button.

**Post-Normalization Flow (UnmatchedSongsDialog):**
After normalization completes, if there are unmatched songs (IsMatched == false):
1. MainWindow shows `UnmatchedSongsDialog` instead of the generic notification
2. Dialog displays each unmatched track with:
   - Track number and raw title (read-only, for context)
   - Editable ComboBox populated with all songs from database (via `GetAllTitles()`)
   - User can select from dropdown or type a new name
3. When user clicks "Apply All":
   - For each corrected track:
     - Updates `track.SongName` to the corrected value
     - Sets `track.IsMatched = true` (removes gold highlighting)
     - If the correction is NOT in database, calls `AddSong()` to add it to songs.json
   - Shows summary: "Added N new song(s) to songs.json: ..."
   - Returns to MainWindow with grid refreshed
4. User can click "Skip" to close dialog without applying corrections

**Benefits:**
- Guided correction workflow instead of relying only on gold highlighting
- New songs automatically added to database for future imports
- Corrections persist across re-imports of the same concert

---

### 6. GetAllTitles

**Signature:**
```csharp
public List<string> GetAllTitles()
```

**Purpose:** Get all canonical song titles for autocomplete/search UI.

**Return Value:** Sorted, distinct list of official titles

**Business Logic:**
1. Collect from artist-based structure: `database.Artists[].Songs[].OfficialTitle` (line 359-365)
2. Collect from legacy structure: `database.Songs[].OfficialTitle` (line 368-371)
3. Remove duplicates: `Distinct()` (line 373)
4. Sort alphabetically: `OrderBy(t => t)` (line 373)

**Use Case:** AdvancedSearchDialog uses this to populate song checklists (600+ songs).

---

### 7. GetAllArtists

**Signature:**
```csharp
public List<string> GetAllArtists()
```

**Purpose:** Get all artist names from database.

**Return Value:** Sorted list of artist names

**Business Logic:**
```csharp
return _database?.Artists?.Select(a => a.Name).OrderBy(n => n).ToList()
       ?? new List<string>();
```

**Null Safety:** Returns empty list if `_database` or `Artists` is null

**Use Case:** AddSongDialog uses this to populate artist dropdown.

---

### 8. AddSong (Legacy Overload)

**Signature:**
```csharp
public void AddSong(string officialTitle, List<string>? aliases = null)
```

**Purpose:** Add song using default "Grateful Dead" artist (backward compatibility).

**Implementation:**
```csharp
AddSong(officialTitle, aliases, "Grateful Dead");
```

**Delegates to:** 3-parameter overload (line 389)

---

### 9. AddSong (With Artist)

**Signature:**
```csharp
public void AddSong(string officialTitle, List<string>? aliases, string artistName)
```

**Purpose:** Add new song to database for specific artist, persist to disk, reload lookup.

**Parameters:**
- `officialTitle` (string) - Canonical song name
- `aliases` (List<string>?) - Optional list of alternate spellings/typos
- `artistName` (string) - Artist to add song under

**Business Logic:**

1. **Validate Database** (line 397):
   - Return early if `_database` is null

2. **Ensure Artists List Exists** (line 400-403):
   - Create empty list if null

3. **Find or Create Artist** (line 406-415):
   - Case-insensitive search: `StringComparison.OrdinalIgnoreCase`
   - If artist doesn't exist, create new `ArtistEntry` with empty Songs list
   - Add to `_database.Artists`

4. **Duplicate Check** (line 418-419):
   - Check if song already exists for this artist (case-insensitive)
   - Return early if duplicate (no error thrown)

5. **Add Song** (line 422-426):
   - Create `SongEntry` with title and aliases
   - Add to artist's Songs list

6. **Persist and Reload** (line 428-429):
   - Call `SaveDatabase()` - Write to `Data/songs.json`
   - Call `LoadDatabase()` - Rebuild `_aliasLookup` dictionary

**Side Effects:**
- Modifies `Data/songs.json` on disk
- Reloads entire database into memory
- Rebuilds alias lookup dictionary

**Error Handling:** No exceptions thrown:
- Duplicate song → Silent return (no-op)
- Null database → Silent return
- File write errors → Unhandled (SaveDatabase may throw IOException)

---

## Private Helper Methods

### LoadDatabase

**Signature:**
```csharp
private void LoadDatabase()
```

**Purpose:** Load `songs.json`, parse JSON, build alias lookup dictionary.

**File Path:** `{AppDomain.CurrentDomain.BaseDirectory}/Data/songs.json`

**Business Logic:**

1. **Load JSON** (line 22-26):
   - Check file exists
   - Read all text
   - Deserialize to `SongDatabase` object

2. **Fallback** (line 30):
   - If file missing, create empty database with empty Songs and Artists lists

3. **Build Lookup** (line 34):
   - Initialize case-insensitive dictionary

4. **Load from Artist Structure** (line 37-53):
   - Iterate `database.Artists[].Songs[]`
   - Add official title → official title mapping
   - Add each alias → official title mapping

5. **Load from Legacy Structure** (line 56-69):
   - Iterate `database.Songs[]`
   - Same mapping logic (backward compatibility)

**Dual Loading:** Supports both old flat structure and new artist-based structure.

**Case Insensitivity:** All lookups ignore case (line 34)

---

### SaveDatabase

**Signature:**
```csharp
private void SaveDatabase()
```

**Purpose:** Serialize database to JSON, write to disk.

**Business Logic:**
```csharp
if (_database == null) return;

var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "songs.json");
var json = JsonConvert.SerializeObject(_database, Formatting.Indented);
File.WriteAllText(path, json);
```

**Formatting:** `Formatting.Indented` - Human-readable JSON with indentation

**Error Handling:** Unhandled (caller must catch IOException, UnauthorizedAccessException, etc.)

---

### NormalizeDateInTitle

**Signature:**
```csharp
private string NormalizeDateInTitle(string title)
```

**Purpose:** Parse slash-formatted dates in parenthetical suffixes, strip venue info, convert to yyyy-MM-dd format.

**Parameters:**
- `title` (string) - Track title with potential date suffix

**Return Value:** `string` - Title with normalized date or unchanged if no date found

**Business Logic:**

1. **Match slash date pattern** (regex):
   ```regex
   \s*\((\d{1,2}/\d{1,2}/\d{2,4})(?:\s+[^)]+)?\)\s*$
   ```
   - Captures: `(M/d/yy)`, `(MM/DD/YYYY)`, `(yyyy/MM/dd)`
   - Optional venue info after date: `(?:\s+[^)]+)?`

2. **Parse date components**:
   - Split on `/` → 3 parts: part1, part2, part3
   - Detect format:
     * If part1 length == 4 → `yyyy/MM/dd` format
     * Else → `M/d/yy` or `MM/DD/YYYY` format

3. **Convert to yyyy-MM-dd**:
   - `yyyy/MM/dd` format:
     * year = part1, month = part2, day = part3
   - `M/d/yy` format:
     * month = part1, day = part2, year = part3
     * If year < 100, add 1900 or 2000 (assumes 1970-2069 range)

4. **Reconstruct title**:
   - Extract base title (before date parentheses)
   - Append normalized date: `{baseTitle} ({yyyy-MM-dd})`

**Examples:**
- `"Drums (1971/07/02 Filmore West)"` → `"Drums (1971-07-02)"`
- `"Not Fade Away (5/7/77 Barton Hall)"` → `"Not Fade Away (1977-05-07)"`
- `"Good Loving' (1971/07/02)"` → `"Good Loving' (1971-07-02)"`
- `"Song Title"` → `"Song Title"` (no change)

**Error Handling:**
- Invalid date parsing → return original title unchanged
- Date components out of range → return original title unchanged

**Year Inference Rules (2-digit years):**
- 70-99 → 1970-1999
- 00-69 → 2000-2069

---

## Business Rules

### 1. Fuzzy Match Threshold

**Rule:** Maximum edit distance is **min(2, 20% of string length)**

**Examples:**
- "Dark Star" (9 chars): max distance = min(2, 1.8) = **2 edits**
- "I Know You Rider" (17 chars): max distance = min(2, 3.4) = **2 edits**
- "A" (1 char): max distance = min(2, 0.2) = **0 edits** (exact match only)

**Rationale:**
- Short songs (< 10 chars): Allow up to 2 typos
- Long songs (> 10 chars): Allow up to 20% different (but cap at 2)

**Implementation:** Line 287

---

### 2. Case Insensitivity

**Rule:** All matching is case-insensitive

**Enforcement:**
- Alias lookup dictionary: `StringComparer.OrdinalIgnoreCase` (line 34)
- Levenshtein distance: `ToLowerInvariant()` before comparison (line 304-305)

**Rationale:** Taper metadata has inconsistent capitalization.

---

### 3. Multi-Stage Cleaning Order

**Rule:** Clean in specific order from most specific to least specific

**Order:**
1. Special patterns (Filler, dates with venues)
2. Generic dates (ISO format, US format)
3. Remaster tags
4. Artist suffix (MUST come before Live venue info)
5. Live venue info
6. Segue markers
7. Character normalization (dashes, apostrophes)
8. Suffix removal
9. Fuzzy matching (last resort)

**Rationale:** Specific patterns prevent false matches (e.g., "Song (1)" could match " (1972-01-01)" if date pattern checked first).

---

### 4. Backward Compatibility

**Rule:** Support both legacy flat structure and new artist-based structure

**Legacy Format:**
```json
{
  "Songs": [
    { "OfficialTitle": "Dark Star", "Aliases": ["Darkstar"] }
  ]
}
```

**New Format:**
```json
{
  "Artists": [
    {
      "Name": "Grateful Dead",
      "Songs": [
        { "OfficialTitle": "Dark Star", "Aliases": ["Darkstar"] }
      ]
    }
  ]
}
```

**Loading:** Both structures loaded, merged into single alias lookup (line 37-69)

---

### 5. Duplicate Song Prevention

**Rule:** Cannot add duplicate song to same artist (case-insensitive check)

**Check:** `artist.Songs.Any(s => s.OfficialTitle.Equals(officialTitle, StringComparison.OrdinalIgnoreCase))` (line 418)

**Behavior:** Silent return (no exception thrown)

---

### 6. Alias Reflexivity

**Rule:** Official title maps to itself in lookup dictionary

**Implementation:** `_aliasLookup[song.OfficialTitle] = song.OfficialTitle` (line 44, 61)

**Rationale:** Allows direct lookup of already-correct titles without alias indirection.

---

### 7. Segue Marker Handling

**Rule:** Segue markers (>, ->, →, [>]) stripped during normalization

**Patterns Matched:**
- Simple: `>`, `>`
- ASCII arrow: `->`
- En-dash arrow: `–>`
- Unicode arrow: `→`
- Bracketed: `[>]`

**Regex:** `@"\s*(\[?>?\]?|[-–]?\s*>\s*)\s*$"` (line 156, 196, 212)

**Rationale:** Segue marker indicates track connection, not part of song name.

---

### 8. Reload After Add

**Rule:** Reload entire database and rebuild lookup after adding song

**Implementation:**
```csharp
SaveDatabase();   // Write to disk
LoadDatabase();   // Reload from disk, rebuild lookup
```

**Rationale:**
- Ensures alias lookup includes new song immediately
- Rebuilds optimized case-insensitive dictionary

**Performance Impact:** Small delay on AddSong (acceptable for infrequent operation).

---

## Regex Patterns Explained

### Date with Location (ISO)
```regex
\s*\(\d{4}-\d{2}-\d{2}\s*-\s*[^)]+\)\s*$
```
- `\s*` - Optional whitespace
- `\(` - Literal open paren
- `\d{4}-\d{2}-\d{2}` - yyyy-MM-dd date
- `\s*-\s*` - Dash separator (optional spaces)
- `[^)]+` - Anything except close paren (location info)
- `\)` - Literal close paren
- `\s*$` - Optional trailing whitespace, end of string

**Matches:** "Song (1972-05-04 - Olympia Theatre, Paris)"

---

### US Date with Venue
```regex
\s*\(\d{1,2}/\d{1,2}/\d{2,4}\s+[^)]+\)\s*$
```
- `\d{1,2}/\d{1,2}/\d{2,4}` - M/D/YY or MM/DD/YYYY
- `\s+` - Required space (separator from venue)
- `[^)]+` - Venue name

**Matches:** "Song (5/7/77 Barton Hall)"

---

### Remaster Tags
```regex
\s*\((?:\d{4}\s+)?Remastere?d?\)\s*$
```
- `(?:\d{4}\s+)?` - Optional year (non-capturing group)
- `Remastere?d?` - "Remaster", "Remastered", "Remastere" (typo tolerance)
- Case-insensitive flag

**Matches:** "(Remaster)", "(2003 Remastered)", "(Remastered)", "(2003 Remaster)"

---

### Segue Markers
```regex
\s*(\[?>?\]?|[-–]?\s*>\s*)\s*$
```
- `\[?>?\]?` - Optional bracketed: `>`, `[>]`, `[]`
- `|` - OR
- `[-–]?\s*>\s*` - Optional dash (hyphen or en-dash) + spaces + `>`

**Matches:** " >", " ->", " –>", " → ", " [>]"

---

### Dash Normalization Characters
```csharp
.Replace("–", "-")  // U+2013 EN DASH
.Replace("—", "-")  // U+2014 EM DASH
.Replace("−", "-")  // U+2212 MINUS SIGN
.Replace("─", "-")  // U+2500 BOX DRAWINGS LIGHT HORIZONTAL
```

**Rationale:** Different sources use different Unicode dash characters. Normalize all to ASCII hyphen for matching.

---

## Error Handling Summary

| Scenario | Behavior |
|----------|----------|
| `songs.json` missing | Create empty database (no error) |
| `songs.json` corrupted | JsonConvert throws exception (unhandled) |
| Empty title to Normalize() | Return null (line 77) |
| No match found | Return null (line 265) |
| Duplicate song in AddSong() | Silent return, no-op (line 418-419) |
| File write error in SaveDatabase() | IOException unhandled (caller must catch) |
| Null database in AddSong() | Silent return (line 397) |

**Gracefully Handled:**
- Missing database file
- Empty/null titles
- Duplicate songs

**Unhandled:**
- JSON parsing errors (corrupted file)
- File write errors (disk full, permissions)

---

## Performance Characteristics

### Normalization Pipeline

**Best Case:** O(1) - Direct lookup after basic cleaning (line 160)

**Worst Case:** O(n × m²) where:
- n = number of songs in database (600+)
- m = average song title length

**Fuzzy Matching Complexity:**
- For each song: O(m²) for Levenshtein distance
- For all songs: O(n × m²)

**Optimization:** Direct lookup stages (4-11) checked before fuzzy matching

**Typical Performance:**
- 600 songs: ~1-2ms per title (exact match)
- 600 songs: ~50-100ms per title (fuzzy match required)

### Memory Usage

- `_aliasLookup` dictionary: ~2-3 entries per song (1 official + 1-2 aliases average)
- 600 songs × 3 entries × ~20 bytes/entry = ~36 KB
- Negligible memory impact

---

## File Path

**Source:** [Services/NormalizationService.cs](Services/NormalizationService.cs)

**Lines of Code:** 442

---

## Related Documentation

- [11-metadata-service.md](documentation/11-metadata-service.md) - MetadataService calls Normalize() during import
- [04-add-song-dialog.md](documentation/04-add-song-dialog.md) - UI for AddSong() method
- [05-manage-songs-dialog.md](documentation/05-manage-songs-dialog.md) - UI for GetAllTitles() method

---

**Last Updated:** 2026-03-01
**Status:** Complete service documentation
