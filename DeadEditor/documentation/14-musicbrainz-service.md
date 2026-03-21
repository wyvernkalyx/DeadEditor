# Service Documentation: MusicBrainzService

## Purpose

The **MusicBrainzService** integrates with external music metadata APIs (AcoustID and MusicBrainz) to automatically identify and retrieve album information for official releases. It supports both **audio fingerprinting** (automatic identification) and **manual search by name**. The service handles rate limiting, fpcalc.exe dependency management, release-group matching across multiple recordings, and cover art retrieval from the Cover Art Archive.

---

## Dependencies

- **AcoustID API** - Audio fingerprinting service (requires API key)
- **MusicBrainz API** - Music metadata database (no key required)
- **Cover Art Archive** - Album artwork retrieval
- **fpcalc.exe** (Chromaprint) - External tool for generating audio fingerprints (user-configured path)
- **LibrarySettings** - Settings object containing fpcalc.exe path configuration
- **TagLib-Sharp** - Reading audio file duration
- **Newtonsoft.Json** - JSON parsing for API responses
- **System.Net.Http** - HTTP requests to APIs

---

## Public Methods

### LookupAlbumAsync

**Signature:**
```csharp
public async Task<AlbumLookupResult?> LookupAlbumAsync(List<TrackInfo> tracks)
```

**Purpose:** Automatically identify album using audio fingerprinting (first 3 tracks).

**Parameters:**
- `tracks` (List<TrackInfo>) - Tracks from the album to identify

**Return Value:** `AlbumLookupResult?` - Album metadata (name, year, artist, artwork) or null if not found

**Business Logic:**

1. **Take first 3 tracks** for fingerprinting (line 35) - Faster and more reliable than all tracks
2. **For each track:**
   - Generate fingerprint using fpcalc.exe → `GetFingerprintAsync()`
   - Query AcoustID API with fingerprint → `QueryAcoustIdAsync()`
   - Extract MusicBrainz recording ID from response
   - Collect recording IDs (line 46-81)
3. **Query MusicBrainz** with first recording ID → `QueryMusicBrainzAsync()` (line 92)
4. **Return single album result** (first match, auto-selected)

**Error Handling:**
- Returns `null` if no tracks provided (line 40)
- Skips problematic tracks (try/catch per track, line 75-80)
- Returns `null` if no recordings found (line 88)
- Catches top-level exceptions and returns `null` (line 95-99)

**Known Limitations:**
- Only uses first recording ID (ignores multi-track matching)
- Does not show release selector dialog (auto-selects first match)
- Relies on fpcalc.exe availability (fails silently if missing)

**Console Logging:** Extensive logging at every step (lines 31-88)

---

### SearchReleasesByNameAsync

**Signature:**
```csharp
public async Task<List<ReleaseOption>?> SearchReleasesByNameAsync(string albumName, string artistName, string? year = null)
```

**Purpose:** Manual search for releases by album name and artist (no fingerprinting).

**Parameters:**
- `albumName` (string) - Album name to search for
- `artistName` (string) - Artist name to search for
- `year` (string?, optional) - Year filter (added to query if provided)

**Return Value:** `List<ReleaseOption>?` - All matching releases or null if none found

**Business Logic:**

1. **Build MusicBrainz query** (line 118-124):
   ```
   releasegroup:{albumName} AND artist:{artistName}
   ```
   - Add year filter if provided: `AND date:{year}`
2. **Query MusicBrainz release-groups** (line 126-139)
   - Limit: 10 results
   - Format: JSON
3. **Filter to Albums only** (line 152-157):
   - Skip non-Album types (Singles, EPs, Compilations)
4. **Get releases for top 3 release-groups** (line 146-172):
   - Call `GetReleasesForReleaseGroupAsync()` for each
   - Rate limit: 1 second delay between calls (line 171)
5. **Remove duplicates** (line 175-179):
   - Group by `{Title, Year}` combination
   - Take first from each group
   - Sort by year

**Error Handling:**
- Returns `null` if no release-groups found (line 138)
- Returns `null` if no releases after filtering (line 183)
- Catches exceptions and returns `null` (line 185-189)

**URL Encoding:** Uses `Uri.EscapeDataString()` for album/artist names (line 118)

---

### LookupAllReleasesAsync

**Signature:**
```csharp
public async Task<List<ReleaseOption>?> LookupAllReleasesAsync(List<TrackInfo> tracks)
```

**Purpose:** Fingerprint-based lookup returning ALL matching releases (for release selector dialog).

**Parameters:**
- `tracks` (List<TrackInfo>) - Tracks to fingerprint

**Return Value:** `List<ReleaseOption>?` - All matching releases or null if none found

**Business Logic:**

1. **Take first 3 tracks** for fingerprinting (line 203)
2. **For each track:**
   - Generate fingerprint → `GetFingerprintAsync()`
   - Query AcoustID → `QueryAcoustIdAsync()`
   - Collect recording IDs (line 216-240)
3. **Find common releases** across all recordings → `FindCommonReleasesAsync()` (line 251)
   - Uses multi-track matching to identify albums (not compilations)
4. **Return all release options** (line 252)

**Difference from LookupAlbumAsync:**
- Returns `List<ReleaseOption>` (multiple releases) instead of single `AlbumLookupResult`
- Uses `FindCommonReleasesAsync()` to find releases containing multiple matched recordings
- Designed to trigger release selector dialog (if multiple releases found)

**Error Handling:**
- Returns `null` if no tracks provided (line 208)
- Skips problematic tracks (continue on exception, line 235-239)
- Returns `null` if no recordings found (line 246)
- Catches top-level exceptions and returns `null` (line 254-258)

---

### GetAllReleasesAsync

**Signature:**
```csharp
public async Task<List<ReleaseOption>?> GetAllReleasesAsync(string recordingId)
```

**Purpose:** Get all official album releases containing a specific MusicBrainz recording.

**Parameters:**
- `recordingId` (string) - MusicBrainz recording ID (MBID)

**Return Value:** `List<ReleaseOption>?` - All official album releases or null if none found

**Business Logic:**

1. **Query MusicBrainz** for recording details (line 598):
   ```
   /ws/2/recording/{recordingId}?inc=releases+release-groups+artists&fmt=json
   ```
   - Does NOT include `recordings` (invalid parameter for recording endpoint)
   - Returns release summaries only (no track data)
2. **Extract artist credit** from recording (line 611)
3. **Filter releases** (line 619-639):
   - **Status = "Official"** (line 629-633)
   - **Primary type = "Album"** or null (line 635-639)
   - Skip compilations, singles, bootlegs
4. **Extract metadata for each release** (line 641-715):
   - Title, year (from date), country, label, format (CD/Vinyl/etc.)
   - Release ID (for later track data fetching)
   - Get cover art URL → `GetCoverArtUrlAsync()` (line 701)
   - **Track data NOT included** (must be fetched separately per release)
5. **Remove duplicates** (line 718-723):
   - Group by `{Title, Year}`
   - Sort by year

**Note:** Track listings are NOT available from this endpoint. After the user selects a release, call `GetReleaseTracksAsync()` to fetch track data for that specific release.

**Error Handling:**
- Returns `null` if no releases found (line 608)
- Catches exceptions and returns `null` (line 729-733)

**Console Logging:** Logs artist, release count, and filtering decisions (lines 613-639)

---

## Private Helper Methods

### GetFingerprintAsync

**Signature:**
```csharp
private async Task<string?> GetFingerprintAsync(string filePath)
```

**Purpose:** Generate AcoustID fingerprint using fpcalc.exe (Chromaprint library).

**Business Logic:**

1. **Get configured fpcalc.exe path** from `LibrarySettings.FpcalcPath`
2. **Validate path** (throws exception if not configured or file doesn't exist):
   - If `FpcalcPath` is empty or null → Throw `InvalidOperationException` with message:
     ```
     "fpcalc.exe path not configured. Please set the path in Settings."
     ```
   - If file doesn't exist at configured path → Throw `FileNotFoundException` with message:
     ```
     "fpcalc.exe not found at configured path: {FpcalcPath}. Please verify the path in Settings."
     ```
3. **Execute fpcalc.exe**:
   - Arguments: `"<filePath>"`
   - Redirect stdout, no window
   - Wait for process completion
4. **Parse output** for `FINGERPRINT=<value>` line
5. **Return fingerprint string** or null if no fingerprint in output

**Error Handling:**
- **Throws `InvalidOperationException`** if fpcalc.exe path not configured (user action required)
- **Throws `FileNotFoundException`** if configured path doesn't exist (user action required)
- Catches process execution exceptions and returns `null` (logs to console)
- Returns `null` if no fingerprint found in output

**Console Logging:** Logs fpcalc execution, output parsing

**Changed from Previous Version:** Previously searched for fpcalc.exe in multiple locations and silently returned null if not found. Now uses user-configured path from Settings and throws clear exceptions if not configured.

---

### QueryAcoustIdAsync

**Signature:**
```csharp
private async Task<string?> QueryAcoustIdAsync(string fingerprint, string filePath)
```

**Purpose:** Query AcoustID API with fingerprint to get MusicBrainz recording ID.

**Business Logic:**

1. **Get duration** from audio file → `GetDurationInSeconds()` (line 366)
2. **Build AcoustID API URL** (line 369):
   ```
   https://api.acoustid.org/v2/lookup?client={apiKey}&duration={duration}&fingerprint={fingerprint}&meta=recordings+releasegroups
   ```
   - `client` - AcoustID API key (from constructor)
   - `duration` - File duration in seconds
   - `fingerprint` - Audio fingerprint from fpcalc.exe
   - `meta` - Request recordings and release-groups metadata
3. **Query AcoustID API** (line 371-375)
4. **Parse JSON response** (line 378-391):
   - Get first result from `results` array
   - Get first recording from `recordings` array
   - Extract recording `id` (MusicBrainz recording ID)
5. **Return recording ID** or null

**Error Handling:**
- Returns `null` if no results (line 380)
- Returns `null` if no recordings in result (line 385-391)
- Catches exceptions and returns `null` (line 393-397)

**API Key:** Stored in `_acoustIdApiKey` field (set in constructor, line 369)

---

### FindCommonReleasesAsync

**Signature:**
```csharp
private async Task<List<ReleaseOption>?> FindCommonReleasesAsync(List<string> recordingIds)
```

**Purpose:** Find release-groups that contain multiple matched recordings (identifies albums vs compilations).

**Business Logic:**

1. **Initialize counters** (line 410-411):
   - `releaseGroupCounts` - How many matched recordings in each release-group
   - `releaseGroupInfo` - Metadata for each release-group
2. **For each recording ID** (line 413-461):
   - Query MusicBrainz for recording's releases (line 416)
   - Extract all release-groups from releases
   - **Filter to official albums** (line 437-442):
     - `status == "Official"`
     - `primaryType == "Album"`
   - Increment count for each release-group (line 455)
   - **Rate limit: 1 second delay** between requests (line 460)
3. **Calculate threshold** for matching (line 470-479):
   - Threshold = `max(2, recordingIds.Count / 2)`
   - Example: 3 recordings → threshold = 2 (need 2+ matches)
   - Example: 1 recording → threshold = 2 (need 2+ matches)
4. **Select candidate release-groups** (line 475-479):
   - Where count >= threshold
   - Order by match count (descending)
5. **Get all releases** for top 3 release-groups (line 492-505):
   - Call `GetReleasesForReleaseGroupAsync()` for each
   - Rate limit: 1 second delay (line 504)
6. **Remove duplicates** (line 508-512):
   - Group by `{Title, Year}`
   - Sort by year

**Error Handling:**
- Returns `null` if no release-groups found (line 467)
- Returns `null` if no candidates meet threshold (line 486)
- Catches exceptions and returns `null` (line 518-522)

**Console Logging:** Logs recording queries, match counts, threshold logic (lines 407-514)

**Business Rule:** Multi-track matching helps identify the correct album when a track appears on multiple releases (original album, compilations, reissues).

---

### GetReleasesForReleaseGroupAsync

**Signature:**
```csharp
private async Task<List<ReleaseOption>?> GetReleasesForReleaseGroupAsync(string releaseGroupId, string artist)
```

**Purpose:** Get all official releases for a specific release-group (different editions/countries).

**Business Logic:**

1. **Query MusicBrainz** for release-group details (line 488):
   ```
   /ws/2/release-group/{releaseGroupId}?inc=releases+artists&fmt=json
   ```
   - Does NOT include `recordings` (invalid parameter for release-group endpoint)
   - Returns release summaries only (no track data)
2. **Extract album title** from release-group (line 501)
3. **For each release** (line 503-579):
   - **Filter to official releases** (line 549-550)
   - Extract metadata: title, date, country, release ID
   - Extract year from date (line 557-561)
   - Extract label from `label-info` array (line 563-568)
   - Extract format from `media` array (CD/Vinyl/Digital) (line 570-575)
   - Get cover art URL → `GetCoverArtUrlAsync()` (line 577-582)
   - Create `ReleaseOption` object (line 584-594)
   - **Track data NOT included** (must be fetched separately per release)
4. **Return release options** or null

**Note:** Track listings are NOT available from this endpoint. After the user selects a release, call `GetReleaseTracksAsync()` to fetch track data for that specific release.

**Error Handling:**
- Returns `null` if no releases found (line 542)
- Catches exceptions and returns `null` (line 599-603)

**Metadata Extraction:**
- **Label:** First item in `label-info` array (line 567)
- **Format:** First media format (line 574)
- **Year:** First 4 characters of date string (line 560)

---

### QueryMusicBrainzAsync

**Signature:**
```csharp
private async Task<AlbumLookupResult?> QueryMusicBrainzAsync(string recordingId)
```

**Purpose:** Get single album result from recording ID (used by `LookupAlbumAsync`).

**Business Logic:**

1. **Query MusicBrainz** for recording (line 729)
2. **Extract releases** from response (line 738-740)
3. **Find primary release** (line 743-748):
   - Prefer: `primary-type == "Album"` AND `status == "Official"`
   - Fallback: First release if no primary found
4. **Extract metadata** (line 750-762):
   - Album name, release date, artist credit, release-group ID
   - Parse year from date string
5. **Get cover art** → `GetCoverArtUrlAsync()` (line 765-769)
6. **Return AlbumLookupResult** with hardcoded confidence 0.85 (line 771-778)

**Error Handling:**
- Returns `null` if no releases found (line 740)
- Catches exceptions and returns `null` (line 780-784)

**Confidence Score:** Hardcoded to 0.85 (line 777) - **NOT calculated from match quality**

**Difference from GetAllReleasesAsync:**
- Returns `AlbumLookupResult` (single album) instead of `List<ReleaseOption>`
- Auto-selects "primary" release (original album, usually)
- No filtering or duplicate removal

---

### GetReleaseTracksAsync

**Signature:**
```csharp
public async Task<List<MusicBrainzTrack>?> GetReleaseTracksAsync(string releaseId)
```

**Purpose:** Get track listings for a specific release ID.

**Parameters:**
- `releaseId` (string) - MusicBrainz release ID (MBID)

**Return Value:** `List<MusicBrainzTrack>?` - Track listings or null if none found

**Business Logic:**

1. **Query MusicBrainz** for release details:
   ```
   /ws/2/release/{releaseId}?inc=recordings+artists&fmt=json
   ```
   - `inc=recordings` requests full track data for this specific release
   - This is the ONLY correct endpoint for fetching track listings
2. **Extract media array** from response
3. **For each medium** (disc):
   - Track disc number (1-based, increments for each medium)
   - Extract `tracks[]` array
   - For each track:
     * Extract disc number (from medium position in media array)
     * Extract position (track number within disc, 1-based)
     * Extract title
     * Extract length (duration in milliseconds, optional)
4. **Return List<MusicBrainzTrack>** with all tracks from all media, each tagged with disc number

**Usage Pattern:**
1. User performs fingerprint or manual search → gets list of `ReleaseOption` objects
2. User selects a release from `ReleaseSelectorDialog`
3. **Call `GetReleaseTracksAsync(selectedRelease.ReleaseId)`** to fetch track data
4. Update `selectedRelease.Tracks` with the result
5. Pass to `MusicBrainzConfirmationDialog` for display

**Why This Approach:**
- `/ws/2/recording/{id}` and `/ws/2/release-group/{id}` return **release summaries** (no track data)
- `/ws/2/release/{id}?inc=recordings` returns **full release** with complete track listings
- Only fetch tracks for the release the user actually selects (more efficient)

**Error Handling:**
- Returns `null` if release not found
- Returns `null` if no media/tracks in response
- Catches exceptions and returns `null`

**Rate Limiting:** This is a MusicBrainz API call - observe 1 request per second limit

---

### GetCoverArtUrlAsync

**Signature:**
```csharp
private async Task<string?> GetCoverArtUrlAsync(string releaseGroupId)
```

**Purpose:** Retrieve album artwork URL from Cover Art Archive.

**Business Logic:**

1. **Query Cover Art Archive** (line 791):
   ```
   https://coverartarchive.org/release-group/{releaseGroupId}
   ```
2. **Return null if 404** (line 794-795) - No cover art available
3. **Parse JSON response** (line 797-802)
4. **Find front cover** (line 805-809):
   - Prefer: Image with `front == true`
   - Fallback: First image if no front cover
5. **Return image URL** or null

**Error Handling:**
- Returns `null` on HTTP error (line 794-795)
- Returns `null` if no images found (line 801-802)
- Catches all exceptions and returns `null` (line 814-817)

**API:** Cover Art Archive is a MusicBrainz project, no API key required

---

### GetDurationInSeconds

**Signature:**
```csharp
private int GetDurationInSeconds(string filePath)
```

**Purpose:** Get audio file duration for AcoustID API (required parameter).

**Business Logic:**

1. **Open file with TagLib-Sharp** (line 824)
2. **Extract duration** from `file.Properties.Duration` (line 825)
3. **Convert to seconds** (TotalSeconds)
4. **Return integer seconds**

**Error Handling:**
- Returns `0` on any exception (line 827-830)
- Used as fallback if file unreadable

---

## Business Rules

### 1. Rate Limiting
- **MusicBrainz API:** 1 request per second (line 460, 504, 171)
- **Implementation:** `await Task.Delay(1000)` after each request
- **Rationale:** MusicBrainz requires rate limiting to avoid IP bans
- **No rate limit for AcoustID or Cover Art Archive**

### 2. First 3 Tracks for Fingerprinting
- **Rule:** Only fingerprint first 3 tracks (line 35, 203)
- **Rationale:** Faster, more reliable, reduces API calls
- **Trade-off:** May miss correct match if first 3 tracks not representative

### 3. fpcalc.exe Configuration
- **Storage:** User-configured path stored in `LibrarySettings.FpcalcPath`
- **Configuration:** Set via Settings window (Browse button for .exe file picker)
- **Validation:** Path validated before use - throws exception if not configured or file missing
- **No Auto-Search:** Does NOT search system PATH or parent directories (explicit configuration required)

### 4. Official Albums Only
- **Filter:** `status == "Official"` AND `primaryType == "Album"` (line 441, 645, 651)
- **Excluded:** Bootlegs, compilations, singles, EPs, promotional releases
- **Rationale:** User only wants official studio/live album releases

### 5. Duplicate Removal
- **Grouping:** By `{Title, Year}` combination
- **Selection:** First release in each group (arbitrary)
- **Sorting:** By year (oldest first)
- **Applied to:** All methods returning `List<ReleaseOption>`

### 6. Multi-Track Matching Threshold
- **Formula:** `max(2, recordingIds.Count / 2)` (line 474)
- **Examples:**
  - 1 recording → threshold 2 (need 2+ matches)
  - 3 recordings → threshold 2 (need 2+ matches - 50%+)
  - 5 recordings → threshold 3 (need 3+ matches - 60%+)
- **Rationale:** Avoid false positives from compilations with 1 track matching

### 7. Primary Release Selection
- **Preference:** First release where `primary-type == "Album"` AND `status == "Official"` (line 743-745)
- **Fallback:** First release in list (line 748)
- **Used by:** `QueryMusicBrainzAsync()` for auto-selecting single result

### 8. Cover Art Priority
- **Preference:** Image with `front == true` (line 805-809)
- **Fallback:** First image if no front cover (line 812)
- **Rationale:** Front cover most relevant for display

### 9. Exception Handling for Missing fpcalc.exe
- **Behavior:** Throw exception if fpcalc.exe path not configured or file missing
- **Exceptions thrown:**
  - `InvalidOperationException` - Path not configured in Settings
  - `FileNotFoundException` - Configured path doesn't exist
- **User Experience:** Clear error message directs user to Settings to configure path
- **Startup Check:** MainWindow shows one-time warning on startup if path not configured

---

## API Integration Details

### AcoustID API

**Endpoint:** `https://api.acoustid.org/v2/lookup`

**Parameters:**
- `client` - API key (stored in `_acoustIdApiKey`)
- `duration` - Audio file duration in seconds
- `fingerprint` - Chromaprint fingerprint from fpcalc.exe
- `meta` - Requested metadata (`recordings+releasegroups`)

**Response:** JSON with `results` array containing `recordings` with MusicBrainz IDs

**Authentication:** API key required (line 369)

**Rate Limiting:** No explicit rate limit (not implemented)

---

### MusicBrainz API

**Base URL:** `https://musicbrainz.org/ws/2/`

**Endpoints Used:**
1. `/recording/{id}?inc=releases+release-groups+artists&fmt=json` (line 416, 614, 729)
2. `/release-group/?query={query}&fmt=json&limit=10` (line 126)
3. `/release-group/{id}?inc=releases+artists&fmt=json` (line 532)

**Query Format:**
```
releasegroup:{albumName} AND artist:{artistName} AND date:{year}
```

**Response Format:** JSON with nested `releases`, `release-groups`, `artist-credit`

**Authentication:** No API key required (open API)

**Rate Limiting:** 1 request per second (enforced by DeadEditor, line 460)

**User-Agent:** `DeadEditor/1.0 (https://github.com/yourrepo)` (line 21)

---

### Cover Art Archive

**Endpoint:** `https://coverartarchive.org/release-group/{id}`

**Response:** JSON with `images` array containing `image` URLs

**Image Selection:** First image with `front == true`, fallback to first image

**Authentication:** No API key required

**Error Handling:** Returns `null` on 404 (no cover art available)

---

## External Dependencies

### fpcalc.exe (Chromaprint)

**Purpose:** Generate audio fingerprints for AcoustID API

**Required Version:** Compatible with AcoustID v2 API

**Configuration:** User must set path in Settings window (`LibrarySettings.FpcalcPath`)

**Download Source:** https://acoustid.org/chromaprint

**Command-Line Usage:**
```bash
fpcalc.exe "path/to/audio.flac"
# Output:
# DURATION=285
# FINGERPRINT=AQADtMqUJEqSJkeR5Njx...
```

**Configuration Workflow:**
1. Download fpcalc.exe from https://acoustid.org/chromaprint
2. Extract to any location (e.g., `C:\Tools\fpcalc.exe`)
3. Open Settings in DeadEditor
4. Browse to fpcalc.exe file
5. Path saved to `settings.json` (`FpcalcPath` property)

**Failure Mode:** Throws clear exception if path not configured or file missing

**Startup Behavior:** One-time warning shown if path not configured (can be dismissed)

---

## Error Scenarios

| Scenario | Behavior |
|----------|----------|
| fpcalc.exe path not configured | Throw `InvalidOperationException` with clear message |
| fpcalc.exe file not found at configured path | Throw `FileNotFoundException` with path details |
| fpcalc.exe execution error | Return `null`, catch exception, log to console |
| AcoustID API error | Return `null`, catch exception (line 393-397) |
| MusicBrainz API error | Return `null`, catch exception (varies by method) |
| Cover Art Archive 404 | Return `null` (no exception) (line 794-795) |
| No releases found | Return `null` (line 138, 246, 467, 542, 625, 740) |
| Invalid recording ID | Return `null`, MusicBrainz API error |
| Network timeout | Throw `HttpRequestException` (unhandled) |
| Invalid API key | AcoustID returns error response (parsed as null) |
| File duration unreadable | Return `0` duration (line 829) |
| No fingerprint in fpcalc output | Return `null` |

**Gracefully Handled:**
- fpcalc.exe execution errors (skip track, continue with others)
- Individual track fingerprinting errors (skip track)
- API errors (return null)
- Missing cover art (return null)

**Thrown as Exceptions (User Action Required):**
- fpcalc.exe path not configured → `InvalidOperationException`
- fpcalc.exe file missing at configured path → `FileNotFoundException`
- Network timeouts → `HttpRequestException`
- Invalid JSON responses → `JsonException` from `JObject.Parse()`

---

## Known Limitations

### 1. Release Selector Dialog Never Triggered
**Issue:** `LookupAllReleasesAsync()` implemented but never called by UI workflow.

**Current Behavior:**
- `LookupAlbumAsync()` returns single result, auto-selected
- No dialog shown even if multiple releases exist

**Expected Behavior:**
- Query all releases with `LookupAllReleasesAsync()`
- Show `ReleaseSelectorDialog` if 2+ releases found
- Auto-select if 1 release found

**Related Files:**
- [ReleaseSelectorDialog.xaml](ReleaseSelectorDialog.xaml) - Dialog UI exists but not triggered
- [MainWindow.xaml.cs](MainWindow.xaml.cs) - Import workflow should call `LookupAllReleasesAsync()`

---

### 2. Confidence Score Hardcoded
**Issue:** `AlbumLookupResult.Confidence` always set to 0.85 (line 777).

**Not Calculated From:**
- Match quality
- Number of matched recordings
- Fingerprint score

**Unused:** Confidence value not displayed or used in UI.

---

### 3. First Recording ID Only
**Issue:** `LookupAlbumAsync()` only uses first recording ID (line 92), ignores multi-track matching.

**Consequence:** May return wrong album if first track appears on compilation.

**Better Approach:** Use `FindCommonReleasesAsync()` like `LookupAllReleasesAsync()` does.

---

### 4. No Rate Limiting for AcoustID
**Issue:** No rate limiting implemented for AcoustID API calls.

**Risk:** Potential API throttling if many tracks fingerprinted quickly.

**Current Usage:** Only fingerprints 3 tracks, so low risk.

---

### 5. User-Agent URL Placeholder
**Issue:** User-Agent contains placeholder URL `https://github.com/yourrepo` (line 21).

**Impact:** Violates MusicBrainz API guidelines (should be real contact URL).

**Recommendation:** Update to actual repository URL or contact email.

---

### 6. Duplicate Removal Arbitrary
**Issue:** When multiple releases have same title+year, first one selected arbitrarily (line 709).

**No Preference For:**
- Original release country
- CD vs vinyl
- Label

**Consequence:** User may see "wrong" edition if multiple with same year.

---

## Track Matching Strategy

MusicBrainz track data is applied to local tracks using **position-based matching** (as of 2026-03-21). This strategy is reliable for official releases where track order is fixed.

### Position-Based Matching (MainWindow.xaml.cs)

**When:** After user selects a release and track data is fetched via `GetReleaseTracksAsync()`

**How:**
1. For each local track in `_tracks`:
   - Find MusicBrainz track where `DiscNumber` and `Position` match local track's `DiscNumber` and `TrackNumber`
   - If match found:
     * Set `Track.SongName` to MusicBrainz title
     * Mark as matched (`IsMatched = true`)
     * Mark as modified (`IsModified = true`)
2. Log each match/unmatch to console
3. Calculate confidence indicator:
   - **High confidence:** All local tracks matched AND track count equals MB track count
   - **Medium confidence:** Otherwise (missing matches or count mismatch)
4. Update status bar with match count and confidence

**Example Console Output:**
```
=== Applying MusicBrainz track data (position-based matching) ===
Local tracks: 85, MusicBrainz tracks: 85
  Matched: Disc 1 Track 1 → Alabama Getaway
  Matched: Disc 1 Track 2 → Promised Land
  ...
=== Match confidence: High (85/85 matched) ===
```

**Advantages:**
- **Reliable for official releases:** Fixed track order ensures correct matching
- **Handles multi-disc albums:** Matches by disc+position instead of global index
- **No fingerprint errors:** Doesn't rely on audio fingerprinting for individual tracks
- **Transparent:** Console logs show exactly what matched

**Limitations:**
- **Requires exact disc/track structure:** Fails if local files have different disc organization
- **No fuzzy matching:** Track 1 on Disc 2 won't match Track 11 on Disc 1 even if same song
- **Medium confidence flagged:** User must manually review when counts differ

**Why Not Fingerprint Matching for Official Releases:**

AcoustID fingerprinting is designed for identifying *unknown* recordings. For official releases:
- Track order is known and fixed
- Position-based matching is deterministic
- Fingerprinting live bonus tracks often returns weak/wrong matches (e.g., "Dark Star" from different dates)

---

## File Path

**Source:** [Services/MusicBrainzService.cs](Services/MusicBrainzService.cs)
**Lines of Code:** 843

---

## Related Documentation

- [07-release-selector-dialog.md](07-release-selector-dialog.md) - Dialog for selecting from multiple releases (implemented but not triggered)
- [08-album-search-dialog.md](08-album-search-dialog.md) - Manual search input dialog (triggers `SearchReleasesByNameAsync`)
- [01-main-window.md](01-main-window.md) - Import workflow that calls MusicBrainzService
- [11-metadata-service.md](11-metadata-service.md) - Writes metadata retrieved from MusicBrainz

---

## Testing Notes

### How to Test Fingerprinting

1. **Configure fpcalc.exe:**
   - Download from https://acoustid.org/chromaprint
   - Extract to any location (e.g., `C:\Tools\fpcalc.exe`)
   - Open DeadEditor → Settings
   - Click Browse next to "fpcalc.exe Path"
   - Select fpcalc.exe file
   - Verify path saved to settings.json

2. **Test Startup Warning:**
   - Clear fpcalc path in settings.json (`"FpcalcPath": ""`)
   - Start DeadEditor
   - Verify: Warning shown on startup about fpcalc.exe
   - Dismiss warning
   - Restart DeadEditor
   - Verify: Warning not shown again (dismissed flag set)

3. **Test Fingerprinting:**
   - Configure fpcalc.exe path in Settings
   - Import studio album with 3+ tracks
   - Check console for "STARTING ALBUM LOOKUP" messages
   - Verify fingerprinting succeeds
   - Verify AcoustID API calls succeed
   - Verify MusicBrainz recording ID found

4. **Test Missing fpcalc.exe:**
   - Set invalid path in Settings (e.g., `C:\doesnotexist\fpcalc.exe`)
   - Import studio album
   - Verify: Clear error message shown (not silent failure)
   - Verify: Error directs user to Settings

5. **Test Manual Search:**
   - Import studio album
   - Click "Manual Search" button
   - Enter album name, artist, year
   - Verify release options returned

6. **Test Cover Art:**
   - Verify cover art displays in UI
   - Check console for Cover Art Archive URL
   - Test with album without cover art (404 handling)

### How to Test Known Issue

**Issue #1 (Release selector not appearing):**
1. Import "Workingman's Dead" (has multiple editions)
2. Verify: Only single result auto-selected
3. Verify: `ReleaseSelectorDialog` never shown
4. Check: Which method called - `LookupAlbumAsync()` or `LookupAllReleasesAsync()`?

---

**Last Updated:** 2026-03-02
**Status:** Updated to reflect configured fpcalc.exe path approach (no auto-search, throws exceptions if not configured)
