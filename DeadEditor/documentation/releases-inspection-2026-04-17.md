# Releases / Concerts / Library Linkage Inspection — 2026-04-17

## 1. releases.json Schema

### Top-level structure

`Data/releases.json` is a JSON object with two top-level keys:
- `"series"` — array of series objects (templated volume expansions)
- `"standalone"` — array of plain strings (album names only)

**No per-release metadata exists.** Each entry is either a volume number in a series template or a bare string name. There are no fields for dates, MBIDs, catalog numbers, disc counts, track lists, or any other metadata.

Source: [Data/releases.json](Data/releases.json)

### Series breakdown

| Series | Count | Template |
|---|---|---|
| Dave's Picks | 58 | `Dave's Picks Vol. {N}` |
| Dick's Picks | 36 | `Dick's Picks Volume {N}` |
| Road Trips | 17 | `Road Trips Vol. {N} No. {M}` |
| Download Series | 12 | `Download Series Vol. {N}` |
| **Standalone** | **46** | *(plain strings)* |
| **Total** | **169** | |

### Example entries

**Dave's Picks (series):** Expanded from template — no per-entry object. Volumes are bare integers `[1, 2, 3, ..., 58]`. The service expands `"Dave's Picks Vol. {N}"` → `"Dave's Picks Vol. 1"`, etc.

**Dick's Picks (series):** Same pattern. Volumes `[1..36]`, template `"Dick's Picks Volume {N}"`.

**Road Trips (series):** Uses compound volume objects:
```json
{"vol": 1, "num": 1}
```
Template: `"Road Trips Vol. {N} No. {M}"` → `"Road Trips Vol. 1 No. 1"`

**Standalone (string only):**
```json
"Enjoying the Ride"
```
No metadata — just a name string.

### Field inventory

| Field | Location | Always / Sometimes | Notes |
|---|---|---|---|
| `name` | series[] | Always (in series) | Series display name |
| `template` | series[] | Always (in series) | Name template with `{N}` / `{M}` placeholders |
| `volumes` | series[] | Always (in series) | Array of ints or `{vol, num}` objects |
| `vol` | Road Trips volumes | Always (in Road Trips) | Volume number |
| `num` | Road Trips volumes | Always (in Road Trips) | Issue number within volume |
| *(string)* | standalone[] | Always | Bare album name, no wrapping object |

### Disc/track data present?

**No.** No entry in `releases.json` has disc-level or track-level data. The file is purely a name registry for autocomplete and the Releases editor view.

### Identifiers present?

**No.** No MBIDs, catalog numbers, UPCs, Discogs IDs, or any other stable identifiers exist anywhere in the file.

### Date fields?

**No.** No release dates, recording dates, or date ranges are stored.

---

## 2. ReleaseLookupService

Source: [Services/ReleaseLookupService.cs](Services/ReleaseLookupService.cs)

### Loading strategy

Lazy singleton (`Lazy<ReleaseLookupService>`). Loads `releases.json` once on first access. Parses the raw JSON into an in-memory `JObject` and expands all series templates into a flat `List<string> _allNames` + `HashSet<string> _knownNames`.

### Public methods

| Method | Signature | Purpose |
|---|---|---|
| `GetSuggestions` | `List<string> GetSuggestions(string searchText)` | Returns up to 20 release names matching search (prefix matches first). Used for autocomplete. |
| `IsKnownRelease` | `bool IsKnownRelease(string name)` | Case-insensitive exact match check. |
| `AddRelease` | `void AddRelease(string name)` | Adds a standalone release name, persists to file. |
| `RemoveStandaloneRelease` | `bool RemoveStandaloneRelease(string name)` | Removes a standalone release. |
| `RenameStandaloneRelease` | `bool RenameStandaloneRelease(string oldName, string newName)` | Renames in-place. |
| `AddVolumeToSeries` | `string? AddVolumeToSeries(string seriesName)` | Appends next integer volume. Not supported for Road Trips (complex `{M}` template). |
| `RemoveLastVolumeFromSeries` | `bool RemoveLastVolumeFromSeries(string seriesName)` | Removes the last volume entry. |
| `GetSeriesInfo` | `List<SeriesInfo> GetSeriesInfo()` | Returns structured series data for the Releases view. |
| `GetStandaloneReleases` | `List<string> GetStandaloneReleases()` | Returns standalone names. |
| `TotalCount` | `int` (property) | Count of all expanded names. |
| `SaveToFile` | `void SaveToFile()` | Atomic write via temp file + rename. |

### Consumers (call sites)

| File | Usage |
|---|---|
| [Views/ReleasesView.xaml.cs](Views/ReleasesView.xaml.cs) | Primary consumer — browse, add, remove, rename releases |
| [Views/ImportView.xaml.cs](Views/ImportView.xaml.cs) | `GetSuggestions()` for album name autocomplete during import |
| [Views/EditMetadataView.xaml.cs](Views/EditMetadataView.xaml.cs) | `GetSuggestions()` for album name autocomplete during edit |

**No lookup/match method exists** — the service cannot match a library album to a release entry. It only does string autocomplete and existence checks.

---

## 3. Releases View

Source: [Views/ReleasesView.xaml](Views/ReleasesView.xaml), [Views/ReleasesView.xaml.cs](Views/ReleasesView.xaml.cs)

### Data loading

`LoadReleases()` is called by ShellWindow on navigation. Fetches data via `ReleaseLookupService.Instance.GetSeriesInfo()` and `.GetStandaloneReleases()`. All UI is built programmatically in code-behind (no data binding/MVVM).

### Layout

Two-panel grid:
- **Left panel:** Series with collapsible headers. Each series shows `▶ Series Name (N volumes)` that expands to list individual volume names. Non-complex series (all except Road Trips) have Add/Remove Volume buttons.
- **Right panel:** Standalone releases as a flat scrollable list with Add Release button.

### Item template

Each volume name is a plain `TextBlock` (no click handler, no navigation, no owned/missing indicator):
- [ReleasesView.xaml.cs:150-156](Views/ReleasesView.xaml.cs#L150-L156)

Each standalone release is a `Border` wrapping a `TextBlock`:
- Double-click → inline rename ([ReleasesView.xaml.cs:243-249](Views/ReleasesView.xaml.cs#L243-L249))
- Context menu → Edit / Remove ([ReleasesView.xaml.cs:252-268](Views/ReleasesView.xaml.cs#L252-L268))
- **No navigation to album detail or import workflow.**

### Search/filter

`SearchBox_TextChanged` → `RebuildUI()` filters both series names/volumes and standalone names by case-insensitive substring match ([ReleasesView.xaml.cs:455-459](Views/ReleasesView.xaml.cs#L455-L459)).

### Current state summary

The Releases view is a **data editor for the name registry** — it manages what release names exist in `releases.json`. It has **no concept of library ownership**, no album detail navigation, and no import triggering.

---

## 4. Concerts View

Source: [Views/ConcertDatabaseView.xaml](Views/ConcertDatabaseView.xaml), [Views/ConcertDatabaseView.xaml.cs](Views/ConcertDatabaseView.xaml.cs)

### Data source

The 2,292 shows come from `ConcertLookupService.Instance.GetAllConcerts()` ([ConcertDatabaseView.xaml.cs:37-38](Views/ConcertDatabaseView.xaml.cs#L37-L38)).

`ConcertLookupService` loads **individual JSON files from `%APPDATA%/DeadEditor/concerts/` (or `Data/concerts/` as fallback)**, not from `shows.json`. Each file is one concert, deserialized into `ConcertReference` objects. Source: [Services/ConcertLookupService.cs:97-123](Services/ConcertLookupService.cs#L97-L123)

### ConcertReference model

Defined in [Models/ConcertReference.cs](Models/ConcertReference.cs). Fields:

| Field | Type | Notes |
|---|---|---|
| `Date` | string | yyyy-MM-dd |
| `Venue` | string | |
| `City` | string | |
| `State` | string | |
| `Country` | string | "US" for domestic |
| `SetlistFmId` | string | setlist.fm identifier |
| `SetlistFmUrl` | string | |
| `LastUpdated` | string | |
| `HasSetlist` | bool | |
| `MultiShow` | bool | |
| `Sets` | List\<ConcertSet\> | Structured set/song data |
| `Tracks` | List\<ConcertTrack\> | Flat track list with position, song name, set, segue |

### Songs count

`SongCount` property = `Tracks.Count` ([ConcertReference.cs:54](Models/ConcertReference.cs#L54)).

### Columns

DataGrid with 4 columns: Date, Venue, City/State (`FormattedLocation`), Songs (`SongCount`). All read-only. Virtualized. User-sortable. ([ConcertDatabaseView.xaml:86-91](Views/ConcertDatabaseView.xaml#L86-L91))

### Search/filter

`ApplyFilter(string searchText)` — substring match across Date, Venue, City, State, and track song names ([ConcertDatabaseView.xaml.cs:56-74](Views/ConcertDatabaseView.xaml.cs#L56-L74)). Called by ShellWindow's search bar.

### Click handlers

Double-click → `NavigateToConcertDetail(concert)` on the ShellWindow ([ConcertDatabaseView.xaml.cs:76-84](Views/ConcertDatabaseView.xaml.cs#L76-L84)).

### Cross-reference stub

`SetLibraryDates(IEnumerable<string> dates)` stores a `HashSet<string> _libraryDates` ([ConcertDatabaseView.xaml.cs:47-50](Views/ConcertDatabaseView.xaml.cs#L47-L50)). **This is populated but not yet used in any UI rendering** — no "In Library" column or indicator exists.

---

## 5. Library-to-Reference Linkage

### What tags are read on import (MetadataService.ReadFolder)

Standard tags read from first audio file ([MetadataService.cs:76-78](Services/MetadataService.cs#L76-L78)):
- `Title`, `Track`, `Disc`, `Duration`

Album-level tags read from first file ([MetadataService.cs:213-236](Services/MetadataService.cs#L213-L236)):
- `FirstPerformer` (Artist)
- `Album`
- `Year`
- `Pictures` (artwork)

Custom FLAC Xiph / MP3 TXXX fields read:
- `ALBUMDATE`
- `VENUE`
- `CITYSTATE`
- `ALBUMNAME`
- `ALBUMTYPE`

### What tags are written on import (LibraryImportService + MetadataService)

Written to each track ([LibraryImportService.cs:199-300](Services/LibraryImportService.cs#L199-L300), [MetadataService.cs:334-451](Services/MetadataService.cs#L334-L451)):
- Standard: `Title`, `Album`, `Performers`, `AlbumArtists`, `Track`, `Disc`, `Year`, `Pictures`
- Preserved from original: `Genre`, `Comment`, `Copyright`, `Publisher`, `Composer`
- Custom: `ALBUMDATE`, `VENUE`, `CITYSTATE`, `ALBUMNAME`, `ALBUMTYPE`

### MBID: read? written?

**Neither.** A grep for `MUSICBRAINZ_ALBUMID`, `MusicBrainz Album Id`, `MBID`, and `musicbrainz_releaseid` across all `.cs` files returns **zero results** in service/model code. The MusicBrainz-related hits are only in documentation files (`CLAUDE.md`, `documentation/`) and dialog UI strings.

The `MusicBrainzService` exists for fingerprint lookup and release selection, but **it does not write any MBID back to audio file tags** during import. No MBID field exists on `AlbumInfo`, `TrackInfo`, or `LibraryShow`.

### Existing linkage to releases.json

**None.** No code path links a `LibraryShow` to a `releases.json` entry. The `ReleaseLookupService` provides autocomplete for the album name field during import, but the resulting `ALBUMNAME` tag is a free-text string — there is no foreign key, no ID, and no verified match.

The `AlbumName`/`OfficialRelease` field on `LibraryShow` *could* be compared against `_knownNames` in `ReleaseLookupService`, but this comparison is never performed. The two systems are completely disconnected.

### Existing linkage to shows.json

**Indirect, via date string only.** The `ShowLookupService` uses `shows.json` and is keyed by `yyyy-MM-dd` date. The library grid's "By Date" mode and "Shows I Don't Have" mode use `ShowLookupService` to cross-reference library dates against known show dates.

The `ConcertDatabaseView` also has `_libraryDates` (populated by ShellWindow), creating a **date-based linkage** between library and concert database. However, this is not yet surfaced in the UI.

Key linkage points:
- [LibraryGridView.xaml.cs](Views/LibraryGridView.xaml.cs) — `_isMissingShowsMode` uses `ShowLookupService.GetAllDates()` minus library dates
- [ConcertDatabaseView.xaml.cs:47-50](Views/ConcertDatabaseView.xaml.cs#L47-L50) — `SetLibraryDates()` stores dates but doesn't use them in rendering

### MBID bootstrap assessment

- **Current state:** Low confidence. MBID is **not stored anywhere** — not in tags, not in `releases.json`, not in any model class. Even when `MusicBrainzService` is used to look up releases, the MBID is used transiently during the API call and then discarded.

- **What retag/migration would involve:**
  1. Add an `MBID` field to `AlbumInfo` and `LibraryShow` models
  2. Modify `MusicBrainzService` to surface the release MBID to callers
  3. Write MBID as a custom Xiph/TXXX field (`MUSICBRAINZ_ALBUMID`) during import in both `MetadataService.WriteMetadata()` and `LibraryImportService.WriteMetadataWithRetry()`
  4. Read MBID back in `ReadCustomFieldsIntoShow()` and `MetadataService.ReadAlbumInfo()`
  5. For existing library albums: either re-import (destructive) or write a one-time migration tool that opens each album's first FLAC, does a MusicBrainz lookup, and writes the MBID tag
  6. For `releases.json`: enrich each entry with its MBID (could be automated via MusicBrainz API search by name)

---

## 6. Key Findings & Design Implications

### For the Official Release Match Service

- **`releases.json` is a name-only registry.** It has no disc/track/date/MBID data. Any release match service needs to either:
  - (a) Enrich `releases.json` with structured release metadata (disc counts, track lists, recording dates, MBIDs), or
  - (b) Create a parallel data store (e.g., `Data/release-details/` with per-release JSON files, similar to how `concerts/` works)
- **Option (b) is recommended** — it follows the established pattern (`concerts/` directory of per-entity files), avoids bloating the autocomplete registry, and allows incremental population.
- The `ConcertLookupService` pattern (per-file JSON, lazy singleton, O(1) key lookup) is a proven template to follow.

### For Owned/Missing indicators

- **Releases view → Library linkage:** Currently impossible. No code connects a `releases.json` name to a `LibraryShow`. The most viable short-term approach is **string matching** on `LibraryShow.AlbumName` against expanded release names from `ReleaseLookupService._knownNames`. This works because:
  - Import writes `ALBUMNAME` to tags (source of truth)
  - Library scan reads `ALBUMNAME` back into `LibraryShow.AlbumName`
  - Release names are deterministic (template expansion)
  - Caveat: name matching is fragile (typos, editions, user edits break it)

- **Concerts view → Library linkage:** The `_libraryDates` HashSet is already populated but unused. Adding an "In Library" column or row highlight based on `_libraryDates.Contains(concert.Date)` is straightforward — **this is the lowest-hanging fruit**.

- **Long-term:** MBID-based linkage is the correct solution for releases. Date-based linkage is sufficient for concerts (dates are unique identifiers for live shows).

### For click-to-import

- **From Concerts view:** Double-click already navigates to `ConcertDetailView`. Adding "Import this show" would require:
  - A way to locate the source audio (archive.org? user-specified folder?)
  - Pre-populating `ImportView` with concert metadata from `ConcertReference`
  - This is a UX design question more than a code question

- **From Releases view:** No click handler exists on volume names. Would need:
  - Navigation to an album detail or import view
  - Pre-population with release name
  - Similar source-audio question

### Blockers / open questions

1. **No MBID anywhere in the system.** This is the biggest gap for reliable release matching. String-based name matching is a pragmatic interim solution but will break for editions, remasters, and user-renamed albums.

2. **Two separate concert data systems.** `shows.json` (via `ShowLookupService`) and `Data/concerts/` (via `ConcertLookupService`) serve overlapping purposes. The concerts system is richer (has setlists, sets, setlist.fm IDs) but the shows system is still used for venue lookup during import. These should eventually be unified or their roles clearly delineated.

3. **`releases.json` has no per-release objects for series entries.** Series volumes are bare integers — there's no place to attach metadata without restructuring the schema (e.g., changing `"volumes": [1, 2, 3]` to `"volumes": [{"number": 1, "mbid": "...", "dates": [...]}]`). The recommended parallel-file approach avoids this schema migration.

4. **The Releases view is an editor, not a browser.** Adding Owned/Missing indicators and click-to-import would transform its purpose significantly. Consider whether this warrants a redesign or a separate "Release Browser" view.
