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
public string? Normalize(string title, string? albumDate = null)
```

**Purpose:** Normalize a single track title to a canonical OfficialTitle. Cleans the title via the structural parser, then resolves it against the alias table with fall-through cleanup stages and a final fuzzy-match step.

**Parameters:**
- `title` (string) - Raw track title from ID3 tag or filename
- `albumDate` (string?, optional) - Album date in `yyyy-MM-dd` or `yyyy` form. Forwarded to the parser for two-digit-year resolution. See [Two-Digit Year Resolution](#two-digit-year-resolution).

**Return Value:**
- `string` - Canonical song name if match found
- `null` - No match found (unknown song)

**Pipeline:**

#### Step A — Structural parse (delegation)

The legacy 13-stage strip stack (S1-S13) is replaced as of commit Y2K-2d by a single call to [`TitleStructureParser.Parse`](../Services/TitleStructureParser.cs):

```csharp
var parsed = TitleStructureParser.Parse(title, albumDate);
var cleaned = parsed.SongName;
```

Inside `Parse` the title is cosmetically cleaned (tape markers, apostrophes), the MusicBrainz artist-suffix tail is stripped, every `(...)` and `[...]` group is classified as metadata (date / state code / "Live at" / "Filler:" / "Remaster" / "Reprise" / standalone "Live") or content, trailing segue markers are removed, dashes are normalized, and whitespace is collapsed. **Canonical-paren song titles like `Ain't It Crazy (The Rub)` survive** because their inner text fires no metadata signal — a guarantee the position-based strip stack could not provide.

Full rules: see [`documentation/title-structure-parser-spec.md`](title-structure-parser-spec.md).

The parser's `TrackDate` and `Venue` fields are not used by `Normalize` — date plumbing for the `TrackInfo` record is handled separately in `NormalizeAll` via [`ExtractDateFromRawTitle`](#extractdatefromrawtitle).

#### Step B — Alias-lookup stack

After `cleaned = parsed.SongName`, the lookup stack runs against the alias table:

| Stage | Operation | Notes |
|------|-----------|-------|
| **L1** | Direct `_aliasLookup[cleaned]` lookup | The fast path — most inputs land here. |
| **L6** | Normalize dashes (en-dash, em-dash, minus, box-drawing) → hyphen, re-lookup | Covers U+2212 MINUS SIGN, which the parser does not normalize. |
| **L7** | Strip ` (1)`, ` (2)`, ` Reprise`, ` reprise` then re-lookup | Track-position markers are content parens to the parser, and the bare " Reprise" suffix has no paren/bracket boundary for the parser to detect. |
| **L9** | Fuzzy match via Levenshtein | Last-resort typo correction. |

Stage numbers preserve the historical labels — L2-L5 and L8 were retired in commit Y2K-2e once `TitleStructureParser` made them redundant.

**Return:** First successful lookup or fuzzy match wins; `null` if every stage misses.

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
    // Safety net: if ParseTitleAndDate missed the date during ReadFolder, scan the raw
    // title for a slash date so track.TrackDate is populated before lookup. Handles
    // bracket dates [Venue M/D/YY] and parenthetical dates (M/D/YY Venue) that the
    // initial file read didn't recognize.
    if (string.IsNullOrEmpty(track.TrackDate))
    {
        var rawTitle = !string.IsNullOrEmpty(track.RawTitle) ? track.RawTitle : track.SongName;
        var extractedDate = ExtractDateFromRawTitle(rawTitle, track.AlbumDate);
        if (extractedDate != null) track.TrackDate = extractedDate;
    }

    // Use SongName for matching — it's already been cleaned by ParseTitleAndDate
    // during ReadFolder. Fall back to Title (RawTitle) only if SongName is empty.
    var titleToNormalize = !string.IsNullOrEmpty(track.SongName) ? track.SongName : track.Title;

    // Normalize forwards albumDate to TitleStructureParser, which extracts and strips
    // slash-formatted dates internally.
    var normalized = Normalize(titleToNormalize, track.AlbumDate);
    if (normalized != null)
    {
        track.SongName = normalized;
        track.IsMatched = true;
        matched++;
    }
    else
    {
        track.IsMatched = false;
    }
}
return matched;
```

**Key Design Decision:** `NormalizeAll` uses `track.SongName` (not `track.Title`) for matching. During `ReadFolder`, `ParseTitleAndDate` strips date/tour suffixes from the raw title and stores the clean song name in `SongName`. The `Title` property returns `RawTitle` (the original tag value), which may still contain suffixes the structural parser would otherwise have to re-process. Using the already-cleaned `SongName` is the cheaper input.

**`track.TrackDate` population is independent of the alias-lookup path.** The early `ExtractDateFromRawTitle` branch writes `TrackDate` onto the track record so downstream UI / search code can read it; the alias-lookup pipeline that follows is a separate concern that produces a canonical `SongName`. `ExtractDateFromRawTitle` and its `ParseSlashDate` helper are exercised by [`TitleDateParsingY2KTests`](../../DeadEditor.Tests/TitleDateParsingY2KTests.cs) — both helpers stay because `Normalize`'s string-returning signature cannot write `TrackDate` itself.

**Side Effects:**
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

### AddAlias

**Signature:**
```csharp
public bool AddAlias(string officialTitle, string alias)
```

**Purpose:** Adds a new alias for an existing song's OfficialTitle in songs.json. Used by the "Match to Song" feature in ImportView to auto-learn title variants when a user manually matches an unmatched track to a setlist song.

**Parameters:**
- `officialTitle` (string) - The canonical song title to add the alias for
- `alias` (string) - The variant title to add as an alias

**Return Value:** `bool` - `true` if alias was added, `false` if not (already exists, song not found, or same as official title)

**Business Logic:**
1. **Validation:** Returns false if either parameter is empty, or if alias equals officialTitle (case-insensitive)
2. **Re-read from disk:** Loads a fresh copy of songs.json to avoid overwriting concurrent changes (does NOT use in-memory `_database`)
3. **Find song:** Searches artists-based structure first, then legacy Songs list
4. **Check duplicates:** Returns false if alias already exists (case-insensitive)
5. **Add alias:** Appends to the song's Aliases list
6. **Atomic write:** Writes to temp file (`.tmp`), deletes original, renames temp → prevents corruption on crash
7. **Reload:** Calls `LoadDatabase()` to rebuild `_aliasLookup` so the new alias is immediately available for subsequent normalization calls

**Thread Safety:** Not thread-safe. Caller should ensure single-threaded access (ImportView runs on UI thread).

**Error Handling:** IOException from file operations is unhandled (caller must catch).

---

## Two-Digit Year Resolution

**Status:** Bug fix — replaced a hard-coded `year >= 70 ? 1900 : 2000` pivot at four sites that previously misclassified pre-1970 dates as 21st-century. With no test coverage, the bug went unnoticed until an audit. This section documents the new strategy and the helper that backs it; the audit also added the first regression test coverage for these parsers.

**Helper:** [`TwoDigitYearResolver.ResolveTwoDigitYear(int twoDigitYear, string? albumDate, int? currentYear)`](../Services/TwoDigitYearResolver.cs) — a stateless static helper used by:

| Site | Method |
|------|--------|
| 1 | [`TitleStructureParser`](../Services/TitleStructureParser.cs) `ExtractDate` (covers all title-parsing paths since Y2K-2c/2d — `MetadataService.ParseTitleAndDate` and `NormalizationService.Normalize` both delegate to the parser) |
| 2 | [`NormalizationService.ParseSlashDate`](../Services/NormalizationService.cs) (private, via `ExtractDateFromRawTitle` from `NormalizeAll` — populates `track.TrackDate` from raw title scan) |

### Rules

The helper applies two rules in order:

**Rule 1 — Album-date preference.** If `albumDate` parses as a four-digit year, build a candidate four-digit year by combining the album's century with the two-digit input. If that candidate is within ±1 of the album year, use it. The ±1 fuzz handles year-boundary recordings (e.g., a track from `1972-01-01` on an album dated `1971-12-31`).

**Rule 2 — Pivot fallback.** Otherwise pivot at `currentYear + 5`:

- Years ≤ pivot → current century (e.g., `25` → `2025` in 2026)
- Years > pivot → previous century (e.g., `69` → `1969` in 2026)

The pivot tracks wall-clock time, so the strategy stays correct as years pass.

### Examples (currentYear = 2026, pivot = 31)

| Input | Album date | Result | Reason |
|-------|------------|--------|--------|
| `69` | `null` | `1969` | Pivot: 69 > 31 → 19XX |
| `69` | `"1969-12-26"` | `1969` | Album-date match |
| `10` | `null` | `2010` | Pivot: 10 ≤ 31 → 20XX |
| `10` | `"2010-05-15"` | `2010` | Album-date match |
| `72` | `"1971-12-31"` | `1972` | Album-date ±1 fuzz |
| `73` | `"1971-12-31"` | `1973` | Outside fuzz → pivot path (agrees) |
| `25` | `null` | `2025` | Pivot: 25 ≤ 31 → 20XX |
| `31` | `null` | `2031` | At pivot → 20XX |
| `32` | `null` | `1932` | Just above pivot → 19XX |

### Known limitation

A track recorded between **1900 and 1931** in an album with no album-date context will misroute to 20XX. We accept this as out-of-scope for the live-music collections this app targets.

### History

The previous implementation used `if (year < 100) year += (year >= 70) ? 1900 : 2000;` at all four sites. For year 69, this produced 2069 — corrupting every show in 1969 and earlier (which includes a substantial portion of the early Grateful Dead catalog) when its title contained a two-digit year. The fix replaces the four sites with a single shared helper and adds the first dedicated test coverage for these parsers ([`TwoDigitYearResolverTests`](../../DeadEditor.Tests/TwoDigitYearResolverTests.cs), [`TitleDateParsingY2KTests`](../../DeadEditor.Tests/TitleDateParsingY2KTests.cs)).

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

### 3. Cleaning + Lookup Order

**Rule:** Title is structurally parsed once, then resolved against the alias table with progressive fall-throughs.

**Order:**
1. **Structural parse** via [`TitleStructureParser.Parse`](../Services/TitleStructureParser.cs) — handles cosmetic prep, artist-suffix strip, paren/bracket classification, segue handling, dash normalization, whitespace collapse. Rules in [title-structure-parser-spec.md](title-structure-parser-spec.md).
2. **L1** direct alias lookup on the parser's `SongName`.
3. **L6** dash re-normalization (covers U+2212 minus, which the parser does not handle).
4. **L7** strip ` (1)`, ` (2)`, bare ` Reprise` / ` reprise` suffix (parser preserves these as content / has no boundary to detect them).
5. **L9** fuzzy match via Levenshtein.

**Rationale:** Structural parsing replaces the old "strip in increasingly-specific regex order" approach because content-paren classification (e.g. `Caution (Do Not Stop on Tracks)` vs `(11/2/69)`) cannot be done by position alone.

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

## Regex Patterns

As of Y2K-2d, the structural parsing rules (date shapes, "Live at"/"Filler:"/"Remaster"/"Reprise" markers, state codes, segue marker forms) live in [title-structure-parser-spec.md](title-structure-parser-spec.md) — see § 4 Classification rules and § 6 Segue detection. The L6 dash normalization in `Normalize` itself covers four Unicode dash variants:

```csharp
.Replace("–", "-")  // U+2013 EN DASH
.Replace("—", "-")  // U+2014 EM DASH
.Replace("−", "-")  // U+2212 MINUS SIGN  (parser does NOT handle this one)
.Replace("─", "-")  // U+2500 BOX DRAWINGS LIGHT HORIZONTAL
```

The parser handles the first, second, and fourth in its own dash-normalization step. L6 is retained for the U+2212 case.

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

**Last Updated:** 2026-05-01 (commit Y2K-2e — closing cleanup; redundant lookup stages and `NormalizeDateInTitle` removed)
**Status:** Complete service documentation
