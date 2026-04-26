# MBID Foundation Inspection — 2026-04-17

## 1. Existing MusicBrainz Code

### Summary

**Yes — substantial, active integration exists.** `MusicBrainzService.cs` (884 lines) provides fingerprint-based album lookup via AcoustID + MusicBrainz APIs, with a release selector dialog for multiple editions. The service is actively called from the import workflow.

### Call sites

| File:Line | Method | Status | Purpose | Result destination |
|---|---|---|---|---|
| `MusicBrainzService.cs:29` | `LookupAlbumAsync()` | **DEAD** (never called) | Single-album auto-select via fingerprint | Returns `AlbumLookupResult` (unused) |
| `MusicBrainzService.cs:109` | `SearchReleasesByNameAsync()` | **DEAD** (never called) | Manual search by album name + artist + year | Returns `List<ReleaseOption>` (unused) |
| `MusicBrainzService.cs:200` | `LookupAllReleasesAsync()` | **LIVE** | Multi-track fingerprint → release candidates | Returns `List<ReleaseOption>` → `ReleaseSelectorDialog` |
| `MusicBrainzService.cs:279` | `GetFingerprintAsync()` | **LIVE** (helper) | Run fpcalc.exe, extract fingerprint string | Used by lookup methods |
| `MusicBrainzService.cs:333` | `QueryAcoustIdAsync()` | **LIVE** (helper) | AcoustID API → recording MBID | Used by lookup methods |
| `MusicBrainzService.cs:375` | `FindCommonReleasesAsync()` | **LIVE** (helper) | Multi-track matching, filters to albums (not compilations) | Used by `LookupAllReleasesAsync` |
| `MusicBrainzService.cs:500` | `GetReleasesForReleaseGroupAsync()` | **LIVE** (helper) | Get all editions of a release-group | Used by `FindCommonReleasesAsync` |
| `MusicBrainzService.cs:585` | `GetAllReleasesAsync()` | **DEAD** (never called) | Get releases for a single recording ID | Returns `List<ReleaseOption>` (unused) |
| `MusicBrainzService.cs:701` | `QueryMusicBrainzAsync()` | **DEAD** (never called) | Single result from recording | Returns `AlbumLookupResult` (unused) |
| `MusicBrainzService.cs:767` | `GetReleaseTracksAsync()` | **LIVE** | Fetch track listings for selected release | Returns `List<MusicBrainzTrack>` → applied to import tracks |
| `MusicBrainzService.cs:828` | `GetCoverArtUrlAsync()` | **LIVE** (helper) | Cover Art Archive lookup | URL stored in `ReleaseOption.ArtworkUrl` |
| `ImportView.xaml.cs:84` | Service instantiation | **LIVE** | `new MusicBrainzService("asa4wLQhwJ", _librarySettings)` | — |
| `ImportView.xaml.cs:1218` | `MusicBrainzButton_Click()` | **LIVE** | UI handler → calls `LookupAllReleasesAsync` | Shows `ReleaseSelectorDialog` |
| `ImportView.xaml.cs:1271` | `ApplyMusicBrainzData()` | **LIVE** | Apply selected release to import tracks | Writes to `TrackInfo.SongName`, downloads artwork |

**Active call chain:**
```
MusicBrainzButton_Click (ImportView:1218)
  → LookupAllReleasesAsync (MBService:200)
    → GetFingerprintAsync (last 4 tracks)
    → QueryAcoustIdAsync (AcoustID API)
    → FindCommonReleasesAsync
      → MB /recording/{id}?inc=releases+release-groups+artists
      → GetReleasesForReleaseGroupAsync
        → MB /release?release-group={id}&inc=media+labels
        → GetCoverArtUrlAsync
  → ReleaseSelectorDialog (if 2+ results)
  → ApplyMusicBrainzData
    → GetReleaseTracksAsync (MB /release/{id}?inc=recordings+artists)
    → Position-based track matching
    → Artwork download
```

### Fields extracted from MB responses

`ReleaseOption` class (ReleaseSelectorDialog.xaml.cs:55-67):
- `Title`, `Year`, `Label`, `Country`, `Format`, `ReleaseId` (MBID), `ArtworkUrl`, `Artist`, `TotalTrackCount`, `Tracks`

`MusicBrainzTrack` class (ReleaseSelectorDialog.xaml.cs:69-75):
- `DiscNumber`, `Position`, `Title`, `Length`

**Critical finding:** `ReleaseOption.ReleaseId` holds the MB release MBID during the import flow, but it is **never persisted** — not written to tags, not stored on `AlbumInfo`, not saved anywhere.

### HTTP client / library

- **Client:** `System.Net.Http.HttpClient` (MusicBrainzService.cs:22)
- **No third-party MB library** — all API calls are raw HTTP + JSON parsing via `JObject`

### User-Agent compliance

```csharp
_httpClient.DefaultRequestHeaders.Add("User-Agent", "DeadEditor/1.0 (https://github.com/yourrepo)");
```
(MusicBrainzService.cs:23)

**Non-compliant.** Contains placeholder URL. MusicBrainz requires a real contact URL or email. Should be updated before any increased API usage (migration tool).

### Rate limiting

**MusicBrainz:** Enforced via `await Task.Delay(1000)` at lines 175, 432, 476, 504. Meets the 1 req/sec requirement.

**AcoustID:** No explicit rate limiting. Currently sends up to 4 requests in quick succession (one per fingerprinted track). Low risk for small batches but would need throttling for a migration tool processing hundreds of albums.

**Cover Art Archive:** No explicit rate limiting.

### Error handling

| Scenario | Handling |
|---|---|
| fpcalc.exe not configured | Throws `InvalidOperationException` (MBService:284-286) |
| fpcalc.exe file missing | Throws `FileNotFoundException` (MBService:290-292) |
| fpcalc.exe execution error | Throws (re-thrown) (MBService:326-329) |
| AcoustID API error | Catches all exceptions, returns `null` |
| MusicBrainz API error | Catches all exceptions, returns `null` |
| Cover Art Archive 404 | Returns `null` (no exception) |
| Network timeout | **Not caught** — propagates as `HttpRequestException` |
| Invalid JSON response | **Not caught** — propagates as `JsonException` |

**UI layer:** ImportView shows user-friendly status notifications (ImportView.xaml.cs:1241-1247).

---

## 2. TagLib# MBID Support

### TagLib# version

**TagLibSharp 2.3.0** (DeadEditor.csproj:18). This version **does support** `Tag.MusicBrainzReleaseId` as a built-in property.

### FLAC Vorbis comments: read/write patterns

**Read pattern** (used in 4 files):
```csharp
var xiph = (TagLib.Ogg.XiphComment)file.GetTag(TagLib.TagTypes.Xiph);
var value = xiph.GetFirstField("FIELDNAME");
```
Locations: MetadataService.cs:216-223, TrackInfoDialog.xaml.cs:57-65, LibraryGridView.xaml.cs:764-770, SettingsView.xaml.cs:307-308.

**Write pattern** (used in 3 files):
```csharp
var xiph = (TagLib.Ogg.XiphComment)flacFile.GetTag(TagLib.TagTypes.Xiph);
xiph.SetField("FIELDNAME", value);
```
Locations: MetadataService.cs:406-413, LibraryImportService.cs:277-284, SettingsView.xaml.cs:237-243.

### MP3 ID3 TXXX: read/write patterns

**Read pattern** (two variants in use):
```csharp
// Variant 1 — frame enumeration (TrackInfoDialog.xaml.cs:89)
foreach (var frame in tag.GetFrames<TagLib.Id3v2.UserTextInformationFrame>())
    if (frame.Description == fieldId) return frame.Text[0];

// Variant 2 — direct Get (LibraryGridView.xaml.cs:779)
var frame = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2, fieldName, false);
var value = frame?.Text.Length > 0 ? frame.Text[0] : null;
```

**Write pattern:**
```csharp
var frame = TagLib.Id3v2.UserTextInformationFrame.Get(tag, description, true);
frame.Text = new[] { value ?? "" };
```
Locations: MetadataService.cs:456-466, LibraryImportService.cs:152-160, SettingsView.xaml.cs:322-329.

### Shared abstraction?

**No.** Each file duplicates the read/write logic independently. MetadataService.cs has private helpers `GetId3v2TextField()` (line 468) and `SetId3v2TextField()` (line 456), but these are not shared — TrackInfoDialog, LibraryGridView, and SettingsView each have their own copies.

### Existing custom tag fields in the codebase

Five custom fields are consistently read/written across the codebase:

| Field Name | Purpose | Type | Written by |
|---|---|---|---|
| `ALBUMDATE` | Concert/release date (yyyy-MM-dd) | Xiph + TXXX | MetadataService, LibraryImportService |
| `VENUE` | Venue name | Xiph + TXXX | MetadataService, LibraryImportService, SettingsView |
| `CITYSTATE` | "City, State" location | Xiph + TXXX | MetadataService, LibraryImportService, SettingsView |
| `ALBUMNAME` | Official release / album name | Xiph + TXXX | MetadataService, LibraryImportService |
| `ALBUMTYPE` | `AudienceRecording` or `OfficialRelease` | Xiph + TXXX | MetadataService, LibraryImportService |

Adding `MUSICBRAINZ_ALBUMID` follows the identical pattern.

### MBID support confirmation

TagLibSharp 2.3.0 exposes `Tag.MusicBrainzReleaseId` as a first-class property. However, the codebase does not use built-in MB tag properties — it uses raw Xiph `GetField`/`SetField` and ID3v2 TXXX frames for all custom fields. For consistency, the MBID field should follow the same raw-field approach:
- **FLAC:** Vorbis comment field `MUSICBRAINZ_ALBUMID` (standard Picard convention)
- **MP3:** TXXX frame with description `MusicBrainz Album Id` (standard Picard convention)

Alternatively, using `Tag.MusicBrainzReleaseId` would handle both formats automatically, but would diverge from the codebase's current explicit pattern.

---

## 3. LibraryShow Model

### Current properties

Source: [Models/LibraryShow.cs](Models/LibraryShow.cs) (222 lines)

| Property | Type | Populated from |
|---|---|---|
| `Type` | `AlbumType` | ALBUMTYPE tag or inferred from scan method |
| `TypeFromTag` | `bool` | Set `true` when Type came from tag |
| `Date` | `string` | ALBUMDATE tag or folder name parsing |
| `Venue` | `string` | VENUE tag |
| `City` | `string` | Parsed from folder name or CITYSTATE |
| `State` | `string` | Parsed from folder name or CITYSTATE |
| `Location` | `string` | Composed from City + State |
| `OfficialRelease` | `string` | ALBUMNAME tag |
| `ContainsDates` | `List<string>` | Lazy-loaded from track titles (date regex) |
| `ContainsVenues` | `List<string>` | Set during library scan |
| `AlbumName` | `string` | ALBUMNAME tag or folder name |
| `ReleaseYear` | `int?` | File Year tag |
| `Edition` | `string` | Parsed from album title |
| `TrackCount` | `int` | Count of audio files in folder |
| `FolderPaths` | `List<string>` | Folder path(s) for multi-disc albums |
| `TrackTitles` | `List<string>` | Lazy-loaded from TagLib Title tags |
| `HeadyIcon` | `string` (computed) | HeadyVersionService lookup |
| `HeadyTooltip` | `string` (computed) | HeadyVersionService lookup |
| `TypeIcon` | `string` (computed) | Based on Type |
| `PrimaryInfo` | `string` (computed) | Display: OfficialRelease or Date |
| `SecondaryInfo` | `string` (computed) | Display: dates/year or Venue |
| `TertiaryInfo` | `string` (computed) | Display: venues/edition or Location |

### Cost of adding MusicBrainzReleaseId property

**Trivial.** Add one auto-property:
```csharp
public string MusicBrainzReleaseId { get; set; } = "";
```

Then populate it during library scan in `LibraryGridView.xaml.cs` where the other custom fields (VENUE, CITYSTATE, ALBUMNAME, ALBUMTYPE) are read — same `xiph.GetFirstField()` / TXXX frame pattern at lines 764-789. The property is already read from a single audio file per album during scan, so adding one more field is zero-cost.

---

## 4. Track Info Dialog

### Current structure

Source: [TrackInfoDialog.xaml.cs](TrackInfoDialog.xaml.cs) (197 lines)

Two sections populated dynamically via `AddField(label, value)`:

**Section 1 — "File Metadata"** (line 17): Reads fresh from audio file tags via TagLib#
- Standard tags: Filename, Title, Track Number, Disc Number, Artist, Album Artist, Album, Year, Genre, Comment, Duration
- Custom FLAC Xiph fields (lines 55-65): ALBUMDATE, VENUE, CITYSTATE, ALBUMNAME, ALBUMTYPE
- Custom MP3 ID3v2 TXXX fields (lines 68-78): Same 5 fields via `GetId3v2TextField()`

**Section 2 — "Import Status"** (line 21): In-memory pipeline state
- File Path, Original Title, Current Song Name, Is Matched, Is Modified

### Cost of adding MBID field

**Trivial.** Add one line after the existing custom fields in each branch:

For FLAC (after line 64):
```csharp
AddField("Release MBID", xiph.GetFirstField("MUSICBRAINZ_ALBUMID") ?? "");
```

For MP3 (after line 77):
```csharp
AddField("Release MBID", GetId3v2TextField(id3v2, "MusicBrainz Album Id"));
```

The `AddField()` method already handles display, em-dash for empty values, and copy-to-clipboard button. Zero layout changes needed.

---

## 5. LibrarySettings.PrimaryArtistName

### Current default

```csharp
public string PrimaryArtistName { get; set; } = "Grateful Dead";
```
(LibrarySettings.cs:10)

### Storage location

`%APPDATA%\DeadEditor\settings.json` (LibrarySettings.cs:41-44)

Loaded via `LibrarySettings.Load()` (line 46), saved via `Save()` (line 64). Uses `JsonConvert.DeserializeObject` / `SerializeObject` with `File.WriteAllText` (non-atomic).

### Consumers

| File:Line | Usage |
|---|---|
| MetadataService.cs:193 | Default artist for new albums: `Artist = LibrarySettings.Load().PrimaryArtistName` |
| MetadataService.cs:208 | Fallback if file has no performer: `file.Tag.FirstPerformer ?? LibrarySettings.Load().PrimaryArtistName` |
| AddSongDialog.xaml.cs:21 | Pre-populate artist dropdown |
| SettingsView.xaml.cs:32 | Display in settings UI |
| SettingsView.xaml.cs:81 | Save from settings UI |
| ImportView.xaml.cs:84 | Passed to `MusicBrainzService` constructor (via `_librarySettings`) |

For the migration tool, `PrimaryArtistName` can be used to filter MB results to the correct artist — the pattern is already established in `MusicBrainzService` where it builds queries with `AND artist:{artistName}`.

---

## 6. Persistence Patterns

### Existing atomic-write examples

The codebase has a well-established atomic write pattern: **temp file → delete original → move temp to target**:

| File:Line | Context |
|---|---|
| NormalizationService.cs:625-630 | `AddAlias()` — songs.json |
| ShowLookupService.cs:282-294 | `SaveToFile()` — shows.json |
| ReleaseLookupService.cs:388-390 | `SaveToFile()` — releases.json |
| EditSetlistView.xaml.cs:267-283 | Concert save — `{date}.json` |
| SongsView.xaml.cs:391-393 | Song DB save — songs.json |

Pattern:
```csharp
var tempPath = targetPath + ".tmp";
File.WriteAllText(tempPath, json);
File.Move(tempPath, targetPath, overwrite: true);
```

Some files use non-atomic `File.WriteAllText` directly (LibrarySettings.Save, NormalizationService.SaveDatabase). The migration state file should use the atomic pattern.

### Storage locations

| Category | Location | Examples |
|---|---|---|
| **Bundled data** (ships with app) | `{AppDomain.BaseDirectory}\Data\` | songs.json, releases.json, segue-pairs.json, heady.json |
| **User data** (per-user, survives updates) | `%APPDATA%\DeadEditor\` | settings.json |
| **Migrated data** (copied from bundled on first run) | `%APPDATA%\DeadEditor\concerts\` | Per-date JSON files (2292 files) |

### Recommended location for migration state file

**`%APPDATA%\DeadEditor\mbid-migration-state.json`**

Rationale:
- Migration state is user-specific (depends on which library folders the user has)
- Should survive app updates (rules out `Data/`)
- Already the home of `settings.json` and `concerts/` — established precedent
- The `ConcertLookupService` pattern of per-file storage in AppData is the closest analog
- Should use atomic write pattern (temp + rename)

---

## 7. Blockers & Open Questions

1. **User-Agent placeholder.** The current User-Agent (`DeadEditor/1.0 (https://github.com/yourrepo)`) needs a real URL or contact email before the migration tool makes potentially hundreds of API calls. MusicBrainz will block non-compliant agents. **Needs Gregg's input on the contact URL/email to use.**

2. **AcoustID API key exposure.** The key `asa4wLQhwJ` is hardcoded in ImportView.xaml.cs:84. For a migration tool making many more calls, consider whether this key has usage limits and whether it should be configurable. **Needs Gregg's input.**

3. **Dead code in MusicBrainzService.** Three public methods (`LookupAlbumAsync`, `SearchReleasesByNameAsync`, `GetAllReleasesAsync`) are implemented but never called. The migration tool might want `SearchReleasesByNameAsync` for non-fingerprint lookups (e.g., when fpcalc isn't available). **Decision: reuse or rewrite?**

4. **Tag field naming convention.** Two options for the MBID tag:
   - (a) Standard Picard convention: `MUSICBRAINZ_ALBUMID` (Vorbis) / `MusicBrainz Album Id` (TXXX) — interoperable with other tools
   - (b) Use TagLib#'s built-in `Tag.MusicBrainzReleaseId` property — simpler code but diverges from codebase pattern
   
   **Recommendation:** Option (a) for interoperability. Files tagged by Picard or other tools will already have these fields.

5. **Multi-disc album handling.** `LibraryShow.FolderPaths` can contain multiple paths for multi-disc albums. The migration tool needs to decide: write MBID to tracks in all disc folders, or just the first? **Recommendation: all folders — every track in the album gets the same MBID.**

6. **Existing tagged files.** Some files in the library may already have `MUSICBRAINZ_ALBUMID` tags (from ripping tools, Picard, etc.). The read path should check for these. The migration tool should detect and skip already-tagged albums. **No blocker — just a design consideration.**

7. **AcoustID rate limiting.** No throttling exists for AcoustID calls. The migration tool fingerprinting hundreds of albums needs throttling. **Not a blocker — easy to add `Task.Delay()` calls.**

---

## 8. Design Implications for MBID Foundation Spec

### Read path — **S (Small)**
- Add `MusicBrainzReleaseId` property to `LibraryShow` (1 line)
- Read from FLAC/MP3 tags during library scan in `LibraryGridView.xaml.cs` (3-4 lines, follows exact pattern of VENUE/ALBUMTYPE reads at lines 764-789)
- Display in Track Info dialog (2 lines — one per format branch)
- **Why S:** Exact patterns exist for every step. Copy-paste with field name change.

### Migration tool UI — **M (Medium)**
- New view (XAML + code-behind) following established `Views/` pattern
- Album list with status indicators (not tagged / tagged / skipped / error)
- Progress bar, resume button, dry-run toggle
- **Why M:** UI is straightforward (follows DataGrid patterns in ConcertDatabaseView), but needs careful UX for review/confirm workflow and resume state management.

### MB API integration — **S (Small)**
- The active `LookupAllReleasesAsync` → `ReleaseSelectorDialog` flow already does exactly what the migration tool needs: fingerprint → find releases → user selects → get release ID
- Dead method `SearchReleasesByNameAsync` provides a fallback for manual/name-based search
- **Why S:** Core API plumbing exists and works. May need minor refactoring to extract from ImportView dependency, but the service itself is self-contained.

### Write MBID to tags — **S (Small)**
- Follow exact pattern of existing custom field writes in MetadataService.cs:406-428 and LibraryImportService.cs:277-296
- Write `MUSICBRAINZ_ALBUMID` (Xiph) or `MusicBrainz Album Id` (TXXX) to every track in the album
- **Why S:** Pattern is copy-paste. The only complexity is iterating all files in all `FolderPaths`.

### Resumability — **S (Small)**
- Single JSON state file at `%APPDATA%\DeadEditor\mbid-migration-state.json`
- Map of `folderPath → { status, mbid, timestamp }`
- Atomic write (established pattern)
- On resume: skip folders already marked `complete`
- **Why S:** Simple key-value persistence. No complex state machine needed.

### Dry-run mode — **S (Small)**
- Flag that prevents `file.Save()` calls
- Log what would be written without writing
- Display results in UI same as normal mode
- **Why S:** Single boolean guard around the write calls. Everything else (lookup, matching, display) runs identically.

### Overall estimate

**Total: M (Medium).** The migration tool UI is the main work item. All API, tag I/O, and persistence infrastructure either exists or follows trivially from existing patterns. No architectural changes needed — this is additive code that plugs into well-established patterns.
