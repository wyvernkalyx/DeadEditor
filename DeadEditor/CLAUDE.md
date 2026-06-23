# DeadEditor - AI Assistant Context Document

## Project Overview

**DeadEditor** is a digital music metadata editor and library manager specifically designed for live concert recordings. Unlike generic metadata editors, DeadEditor provides specialized features for managing, searching, and playing live concert collections with consistent naming conventions and rich metadata.

### Target Users
People who collect live music recordings (tapers, collectors, archivists).

**Note:** While development/testing uses Grateful Dead as sample data, the application is **artist-agnostic by design**. The codebase makes no hardcoded assumptions about specific artists and can manage any live music collection.

### Core Problem Solved
Live music metadata suffers from:
- Inconsistent date formatting across sources
- Typos in song titles and venue information
- Lack of standardization in album/track naming conventions
- Difficulty searching across large collections for specific performances or song sequences
- Multiple editions of official releases treated as identical

**DeadEditor's Solution:** Enforce strict yyyy-MM-dd date format conventions and provide intelligent fuzzy matching to handle real-world metadata inconsistencies.

---

## Ground Rules

These are invariants. They apply to every feature, every commit, every session. If a proposed change appears to require violating one, stop and raise it explicitly — do not work around it.

### Source files are read-only

DeadEditor never writes to source folders. The user's source archive — the folder they Browse to in the Import view, or any path outside `LibrarySettings.LibraryRootPath` — is treated as read-only. We copy from it, never to it.

All tag writes, file renames, artwork updates, and fingerprint persistence operate exclusively on copies inside the managed library. If a feature needs to mutate audio files, the change must land on the managed copy.

This is enforced two ways:

1. **Structurally.** Every tag-writing entry point in the app is reachable only from a `LibraryShow` (managed-by-construction) or from the Library Import target path (computed from `LibraryRoot`). There is no UI surface that opens a non-managed folder for write.
2. **At runtime.** `PathGuard.EnsureWithinLibrary` is called at the top of `MetadataService.WriteMetadata`, `FingerprintService.WriteFingerprintToTrackFile`, and `ManifestService.WriteManifest`. Any path outside the managed library throws `InvalidOperationException` immediately, before any file handle is opened.

The single legitimate exception is the import copy step itself, which reads the source. Reads are unconstrained; writes are guarded.

**If we destroy data, it is always our copy of the data — never the user's source archive.**

When adding a new feature that writes to disk:

- Confirm the write target is under `LibraryRoot`. Verify it, don't assume it.
- If you add a new write service, call `PathGuard.EnsureWithinLibrary` at the top of it.
- Do not add an `allowSourceWrite` escape hatch or any equivalent bypass. There is no legitimate source-write in DeadEditor; an escape hatch is a foot-gun.
- If a workflow seems to require writing to source, stop and raise it. There is almost certainly a managed-side equivalent (or one we should build).

---

## Documentation Handbook

**RULE:** Before modifying any file, check this table and read the relevant documentation file first. **The documentation is the spec — code must match the doc.**

### Documentation Lookup Table

| Working on... | Read first | Lines | Purpose |
|--------------|------------|-------|---------|
| **Shell** | | | |
| `ShellWindow.xaml/.cs` | [documentation/18-shell-redesign-spec.md](documentation/18-shell-redesign-spec.md) | ~430 lines | Single-window shell, sidebar, navigation, view switching |
| `Services/NavigationService.cs` | [documentation/18-shell-redesign-spec.md](documentation/18-shell-redesign-spec.md) § Navigation | ~130 lines | View stack, forward/back/root navigation |
| **Views (UserControls in Views/)** | | | |
| `Views/LibraryGridView.xaml/.cs` | [documentation/02-library-browser.md](documentation/02-library-browser.md) | - | Library grid, quick search, concert list |
| `Views/AlbumDetailView.xaml/.cs` | [documentation/18-shell-redesign-spec.md](documentation/18-shell-redesign-spec.md) § Album Detail | - | Album art, track list, date grouping, playback |
| `Views/ImportView.xaml/.cs` | [documentation/01-main-window.md](documentation/01-main-window.md) | - | Import workflow, folder selection, normalize, renumber |
| `Views/EditMetadataView.xaml/.cs` | [documentation/18-shell-redesign-spec.md](documentation/18-shell-redesign-spec.md) § Edit Metadata | - | Edit FLAC tags in-place, save changes |
| `Views/SettingsView.xaml/.cs` | [documentation/06-settings-window.md](documentation/06-settings-window.md) | - | Library paths, fpcalc, primary artist, reset data |
| `Views/HeaderBar.xaml/.cs` | [documentation/18-shell-redesign-spec.md](documentation/18-shell-redesign-spec.md) § Header Bar | - | Context-sensitive header with nav + action buttons |
| `Views/SidebarPanel.xaml/.cs` | [documentation/18-shell-redesign-spec.md](documentation/18-shell-redesign-spec.md) § Sidebar | - | Sidebar icon navigation: Library, Import, Songs, Releases, Concerts, Box Sets, Settings |
| `Views/PlayerBar.xaml/.cs` | [documentation/18-shell-redesign-spec.md](documentation/18-shell-redesign-spec.md) § Player Bar | - | Transport controls, progress, volume |
| `Views/PlaylistPanel.xaml/.cs` | [documentation/18-shell-redesign-spec.md](documentation/18-shell-redesign-spec.md) § Playlist | - | Compact track list, always visible |
| `Views/SongsView.xaml/.cs` | (inline — no separate doc) | - | Songs editor: browse/search/add/edit/remove songs in songs.json, grouped alphabetically |
| `Views/ReleasesView.xaml/.cs` | (inline — no separate doc) | - | Releases editor: two-panel Series + Standalone management over releases.json |
| `Views/ConcertDatabaseView.xaml/.cs` | (inline — no separate doc) | - | Browse the Data/concerts/ reference database via ConcertLookupService |
| `Views/ConcertDetailView.xaml/.cs` | (inline — no separate doc) | - | Per-concert detail: setlist + venue, setlist.fm link, and which owned library copies match the date |
| `Views/EditSetlistView.xaml/.cs` | (inline — no separate doc) | - | Edit a concert's setlist; raises SaveCompleted so the shell refreshes |
| `Views/MbidMigrationView.xaml/.cs` | [documentation/mbid-foundation-spec.md](documentation/mbid-foundation-spec.md) | - | MBID migration UI: album counts (total / already-tagged / needs-migration) and start/resume the run |
| **Dialogs (modal, overlay shell)** | | | |
| `AdvancedSearchDialog.xaml/.cs` | [documentation/03-advanced-search-dialog.md](documentation/03-advanced-search-dialog.md) | ~9,500 words | 3-tab search (Contains/Exclude/Sequence) |
| `AddSongDialog.xaml/.cs` | [documentation/04-add-song-dialog.md](documentation/04-add-song-dialog.md) | ~7,600 words | Add songs to database with artist support |
| `ManageSongsDialog.xaml/.cs` | [documentation/05-manage-songs-dialog.md](documentation/05-manage-songs-dialog.md) | ~8,200 words | Browse songs, filter, export to text |
| `ReleaseSelectorDialog.xaml/.cs` | [documentation/07-release-selector-dialog.md](documentation/07-release-selector-dialog.md) | ~7,400 words | Select from multiple MusicBrainz releases |
| `AlbumSearchDialog.xaml/.cs` | [documentation/08-album-search-dialog.md](documentation/08-album-search-dialog.md) | ~7,000 words | Manual MusicBrainz search by name |
| `MatchToSongDialog.xaml/.cs` | [documentation/01-main-window.md](documentation/01-main-window.md) § Match to Song | - | Manual match unmatched track to setlist song with auto-alias |
| `MbidCandidateDialog.xaml/.cs` | [documentation/mbid-foundation-spec.md](documentation/mbid-foundation-spec.md) | - | Pick a MusicBrainz release candidate and choose which fields (title/artist/year/track titles) to apply during MBID migration |
| `TrackInfoDialog.xaml/.cs` | (inline — no separate doc) | - | Inspect one track: on-disk FLAC/MP3 tags plus in-memory import status (raw title, match, modified) |
| `UnmatchedSongsDialog.xaml/.cs` | (inline — no separate doc) | - | Resolve tracks the normalizer left unmatched: per-track dropdown to assign the canonical song title |
| **Services** | | | |
| `MetadataService.cs` | [documentation/11-metadata-service.md](documentation/11-metadata-service.md) | ~8,000 words | ID3 tags, ParseAlbumTitle regex, box set vs official release |
| `NormalizationService.cs` | [documentation/12-normalization-service.md](documentation/12-normalization-service.md) | ~7,000 words | 14-stage normalization, fuzzy matching, Levenshtein distance |
| `LibraryImportService.cs` | [documentation/13-library-import-service.md](documentation/13-library-import-service.md) | ~5,000 words | Universal single-path library system, folder creation, metadata preservation |
| `MusicBrainzService.cs` | [documentation/14-musicbrainz-service.md](documentation/14-musicbrainz-service.md) | ~9,500 words | AcoustID fingerprinting, fpcalc.exe, MusicBrainz API, rate limiting |
| `ShowLookupService.cs` | (inline — no separate doc) | - | Loads Data/shows.json; setlist + venue lookup by yyyy-MM-dd (GetSetlist, GetSegue, GetDiscTrack, SuggestTrackNumber, GetSetlistSongCount, GetShowByDate, FormattedVenueLocation) |
| `ReleaseLookupService.cs` | (inline — no separate doc) | - | Loads Data/releases.json, autocomplete for album/release names |
| `ConcertLookupService.cs` | (inline — no separate doc) | - | O(1) per-date lookup over Data/concerts/; AppData-first with bundled fallback + first-run copy |
| `ManifestService.cs` | [documentation/19-folder-import-and-manifests.md](documentation/19-folder-import-and-manifests.md) | - | Read/write sidecar JSON manifests capturing verified metadata for re-import |
| `FingerprintService.cs` | [documentation/fingerprint-persistence-spec.md](documentation/fingerprint-persistence-spec.md) | - | AcoustID/Chromaprint fingerprint precompute + persistence to track files |
| `MbidMigrationService.cs` | [documentation/mbid-foundation-spec.md](documentation/mbid-foundation-spec.md) | - | Orchestrate MBID migration: lookup, candidate confirmation, tag write, state persistence |
| **Models** | | | |
| `AlbumInfo.cs` | [documentation/15-data-model.md](documentation/15-data-model.md) § AlbumInfo | ~11,000 words | Album metadata, type-based polymorphism, AlbumTitle format |
| `TrackInfo.cs` | [documentation/15-data-model.md](documentation/15-data-model.md) § TrackInfo | ~11,000 words | Track metadata, segue notation, GetFinalMetadataTitle |
| `LibrarySettings.cs` | [documentation/15-data-model.md](documentation/15-data-model.md) § LibrarySettings | ~11,000 words | User settings, single library root, window positions |
| `SongDatabase.cs` | [documentation/15-data-model.md](documentation/15-data-model.md) § SongDatabase | ~11,000 words | Song database structure, artist-based organization |
| **Data Files** | | | |
| `Data/songs.json` | [documentation/15-data-model.md](documentation/15-data-model.md) § songs.json | ~11,000 words | JSON schema, examples, 598 songs across 2 artists |
| `Data/shows.json` | (inline — no separate doc) | - | Full gdshowsdb setlists keyed by yyyy-MM-dd (~1,797 shows with per-song segues); venue + setlist lookup via ShowLookupService |
| `Data/releases.json` | (inline — no separate doc) | - | Series templates + standalone album names for autocomplete |
| `%APPDATA%/DeadEditor/settings.json` | [documentation/15-data-model.md](documentation/15-data-model.md) § settings.json | ~11,000 words | Settings JSON schema, all 12 keys with defaults |
| **Box Sets** | | | |
| `Views/BoxSetsView.xaml/.cs`, `BoxSetWizardView.xaml/.cs` | [documentation/box-set-design-memo.md](documentation/box-set-design-memo.md) | ~316 lines | Box Sets list + 3-step authoring wizard (flat track grid, pull-setlist, accordion) |
| `Services/BoxSetService.cs` | [documentation/box-set-design-memo.md](documentation/box-set-design-memo.md) § Data model | - | Read/write/list/delete BoxSetDefinition files; AppData vs Data/box-sets + DEADEDITOR_DEV dev mode |
| `Models/BoxSetDefinition.cs`, `BoxSetTrack.cs` | [documentation/box-set-design-memo.md](documentation/box-set-design-memo.md) § Data model | - | Flat definition -> List<BoxSetTrack> ({trackNumber, songName, date, segueOut}) |
| **Other shipped docs** | | | |
| Import redesign | [documentation/16-import-redesign-spec.md](documentation/16-import-redesign-spec.md) | - | Import screen redesign spec (live in ImportView) |
| Player | [documentation/17-player-window.md](documentation/17-player-window.md) | - | Global playback design (PlaybackService, player/playlist windows) |
| Folder import & manifests | [documentation/19-folder-import-and-manifests.md](documentation/19-folder-import-and-manifests.md) | - | Universal single-path import + sidecar manifest design |

### Future Design Documents

Stubs and banking memos for design conversations not yet implemented:

- [verification-model.md](documentation/verification-model.md) — verification status for curated records (populated status doc; album model shipped, box-set extension specced in box-set-verification-spec.md)
- [concert-verification-spec.md](documentation/concert-verification-spec.md) — concert verification surface (third shipped surface, 2026-06-10): ConcertReference.Verified, ConcertVerifyGate, diff-at-save unverify + explicit Unverify control, SetlistFetcher skip-verified guard, ConcertDatabaseView glyph column
- [add-concert-spec.md](documentation/add-concert-spec.md) — in-app concert creation (shipped 2026-06-10): New Concert button on the Concerts header → blank ConcertReference opened directly in EditSetlistView (no intermediate detail view); duplicate-date refusal on save guarded by the `_originalDate` exclusion (also fixes a pre-existing mid-edit silent-overwrite), extracted as the pure `Services/DuplicateDateRule`; save floor unchanged; hand-created concerts born unverified
- [curation-layer-design-memo.md](documentation/curation-layer-design-memo.md) — Layer A setlist authority vs Layer B per-recording manifest, fingerprints as curation lookup key, curation layer as foundational to verification (banking memo)

The `documentation/` folder now holds 41 files. Beyond the numbered specs above, the design memos and spec/inspection set includes: [box-set-design-memo.md](documentation/box-set-design-memo.md) (authoritative for Box Sets), [verification-and-manifest-wiring-design-memo.md](documentation/verification-and-manifest-wiring-design-memo.md), [audio-as-archive-design-memo.md](documentation/audio-as-archive-design-memo.md), [mbid-foundation-spec.md](documentation/mbid-foundation-spec.md), [fingerprint-persistence-spec.md](documentation/fingerprint-persistence-spec.md), [source-folder-preservation-spec.md](documentation/source-folder-preservation-spec.md), [feature-parity-spec.md](documentation/feature-parity-spec.md), [title-structure-parser-spec.md](documentation/title-structure-parser-spec.md), and [concerts-owned-missing-spec.md](documentation/concerts-owned-missing-spec.md), plus dated audit/inspection/diagnostic notes. Consult the folder directly rather than assuming this list is exhaustive.

### Documentation-First Development Workflow

**ALWAYS follow this workflow when making changes:**

1. **DOCUMENT** → Update the doc first if requirements change
   - If changing behavior, update the relevant documentation file BEFORE writing code
   - Document the new business rules, method signatures, parameters, error handling
   - Include examples showing the new behavior

2. **IMPLEMENT** → Write code to match the doc
   - Code must implement EXACTLY what the documentation specifies
   - Method signatures, parameters, return values must match documented spec
   - Business rules in code must match documented business rules

3. **TEST** → Verify the doc's spec is met
   - Test that implementation matches all documented behavior
   - Verify all documented edge cases are handled
   - Check that error handling matches documented error scenarios

4. **VERIFY** → If implementation forced doc changes, update the doc
   - If you discover the doc was wrong during implementation, update the doc
   - If you find new edge cases, document them
   - If error handling differs, update the documentation to match reality

5. **COMMIT** → Code + docs ship together
   - Never commit code without updating relevant documentation
   - Commit message should reference both code and doc changes
   - Documentation and code must stay in sync

### Quick Reference: Finding Documentation

**By Feature:**
- Import workflow → [01-main-window.md](documentation/01-main-window.md)
- Library browsing → [02-library-browser.md](documentation/02-library-browser.md)
- Advanced search → [03-advanced-search-dialog.md](documentation/03-advanced-search-dialog.md)
- Song database management → [04-add-song-dialog.md](documentation/04-add-song-dialog.md), [05-manage-songs-dialog.md](documentation/05-manage-songs-dialog.md)
- MusicBrainz integration → [14-musicbrainz-service.md](documentation/14-musicbrainz-service.md), [07-release-selector-dialog.md](documentation/07-release-selector-dialog.md), [08-album-search-dialog.md](documentation/08-album-search-dialog.md)

**By Business Logic:**
- Album title format (box set vs official release) → [15-data-model.md](documentation/15-data-model.md) § AlbumInfo, [11-metadata-service.md](documentation/11-metadata-service.md) § ParseAlbumTitle
- Song normalization & fuzzy matching → [12-normalization-service.md](documentation/12-normalization-service.md)
- Folder structure & universal single-path system → [13-library-import-service.md](documentation/13-library-import-service.md)
- Segue notation → [15-data-model.md](documentation/15-data-model.md) § TrackInfo
- Track numbering scheme → [15-data-model.md](documentation/15-data-model.md) § TrackInfo

**By Data Structure:**
- JSON schemas → [15-data-model.md](documentation/15-data-model.md) (songs.json + settings.json)
- All model classes → [15-data-model.md](documentation/15-data-model.md)

---

## Technology Stack

### Platform
- **Framework:** .NET 8.0 (WPF on Windows)
- **UI Framework:** WPF (Windows Presentation Foundation)
- **Language:** C# with nullable reference types enabled
- **Target Platform:** Windows-only (WinExe)

### Dependencies
- **NAudio 2.2.1** - Audio playback
- **Newtonsoft.Json 13.0.3** - JSON serialization/deserialization
- **TagLibSharp 2.3.0** - ID3 tag reading/writing

### Architecture
- **Pattern:** MVVM-like with code-behind (pragmatic WPF approach)
- **Data Storage:**
  - Song database: `Data/songs.json` (598 songs, artist-organized)
  - User settings: `%APPDATA%/DeadEditor/settings.json`
  - Concert metadata: Embedded in audio file ID3 tags + folder structure
- **Services:**
  - `MetadataService` - ID3 tag reading/writing
  - `NormalizationService` - Song title normalization with fuzzy matching
  - `LibraryImportService` - Import concerts from folder structure
  - `MusicBrainzService` - MusicBrainz API integration for official releases

---

## Library Structure

### Universal Single-Path System
DeadEditor uses a single configurable library root (`LibraryRootPath`) for all albums regardless of type. The folder layout under that root is universal:

```
{LibraryRoot}/{Artist}/{AlbumFolder}/
```

Audience recordings, studio albums, official live releases, and box sets all live side by side under each artist folder. Album type is a metadata tag — not a folder decision. See [documentation/13-library-import-service.md](documentation/13-library-import-service.md) and [documentation/19-folder-import-and-manifests.md](documentation/19-folder-import-and-manifests.md) for the full rationale.

### Album Types
The `AlbumType` enum is two values (see [Models/AlbumInfo.cs](Models/AlbumInfo.cs)):
- **AudienceRecording** - Audience/taper/soundboard recordings of a single concert
- **OfficialRelease** - Any official release: studio albums, official live releases (Dave's Picks, Road Trips, etc.), and box sets

### AlbumFolder Naming Convention
`LibraryImportService.BuildLibraryFolderName()` composes the folder from `AlbumInfo` fields:
- **Live recordings** (date + venue present): `{Artist} - {Date} - {Venue} - {City}, {State} - {AlbumName}` (each segment omitted if empty)
- **Studio-style releases** (no date or no venue): `{Artist} - {Year} - {AlbumName}`

All dates use strict **yyyy-MM-dd** format for consistent sorting.

### Hybrid Albums
**Important Design Decision:** Official releases increasingly include live bonus material (e.g., "Europe '72 50th Anniversary Edition" with extra live tracks from different dates). These remain a single `OfficialRelease` with an `Edition` field to keep them as one cohesive unit, rather than splitting studio/live content.

---

## Song Database Design

### Structure
- **File:** `Data/songs.json`
- **Format:** Artist-based organization (supports multiple artists)
- **Current Size:** 598 songs (594 Grateful Dead, 4 NRPS)

### Artist Organization
```json
{
  "Artists": [
    {
      "Name": "Grateful Dead",
      "Songs": [
        {
          "OfficialTitle": "Alabama Getaway",
          "Aliases": ["Alabama"]
        }
      ]
    }
  ]
}
```

### Features
- **Official Titles:** Primary/official song title (the `OfficialTitle` field)
- **Aliases:** Common variations and typos
- **Fuzzy Matching:** Levenshtein distance algorithm (max 2 character difference or 20% of string length)
- **Runtime Addition:** Songs can be added via UI without recompiling
- **Backward Compatibility:** Reads both new artist-based and legacy flat structures

### Why Fuzzy Matching?
Real-world metadata from tapers and internet sources contains frequent typos:
- "Dnacing in the Street" instead of "Dancing in the Street"
- "Wkae of the Flood" instead of "Wake of the Flood"
- "Monkey &" instead of "Monkey & the Engineer"

Fuzzy matching (up to 2 character typos) automatically handles these without requiring manual alias entry for every variation.

---

## Box Sets

Box sets are a distinct feature from the `OfficialRelease` album type. A single
Dave's Picks / Dick's Picks / Road Trips volume is one show or one continuous
run — a normal live album, `AlbumType.OfficialRelease`. A **box set** is a
multi-show bundle or cross-show compilation with its own identity (*Europe '72:
The Complete Recordings*, *So Many Roads*, *Listen to the River*). The
authoritative design is [documentation/box-set-design-memo.md](documentation/box-set-design-memo.md);
this is the summary.

### Data model (flat: definition -> tracks)
A box set is a **curation artifact**, defined independently of owning any audio.
The model is one level deep — no concert or disc objects:

- `BoxSetDefinition` ([Models/BoxSetDefinition.cs](Models/BoxSetDefinition.cs)):
  `Version`, `Name`, `ReleaseDate` (yyyy-MM-dd), `Label`, `CatalogNumber`,
  `Notes`, `Verified`, and `List<BoxSetTrack> Tracks`.
- `BoxSetTrack` ([Models/BoxSetTrack.cs](Models/BoxSetTrack.cs)): `TrackNumber`
  (opaque int — disc-prefixed `101`/`1207` or continuous), `SongName`, `Date`
  (yyyy-MM-dd; the date lives on the track because a box set is multi-date by
  nature), `SegueOut`. Implements `INotifyPropertyChanged` for two-way grid
  binding; the four properties serialize as camelCase.

Venue/city/state are NOT stored — they are derived from each track's date via
`ShowLookupService.GetShowByDate(date)?.FormattedVenueLocation`.

### Storage
One JSON file per box set (camelCase, atomic temp-and-rename write), via
[Services/BoxSetService.cs](Services/BoxSetService.cs):
- `%APPDATA%\DeadEditor\box-sets\<slug>.json` — user-runtime location; seeded
  from the bundled copy on first run. All production reads/writes here.
- `Data\box-sets\<slug>.json` — bundled location shipped with the app.
- When `DEADEDITOR_DEV=1`, AppData is bypassed: reads and writes go straight to
  `Data\box-sets\` (maintainer authoring mode).

### Box Sets view + 3-step wizard
A top-level **Box Sets** sidebar item ([Views/BoxSetsView.xaml](Views/BoxSetsView.xaml))
lists existing definitions (re-read from disk on each navigation); "+ New Box
Set" opens the wizard ([Views/BoxSetWizardView.xaml](Views/BoxSetWizardView.xaml)),
a single UserControl that toggles three panels:
1. **Top-Level Info** — name, release date, label, catalog number, notes.
2. **Concerts and Tracks** — a flat, editable track grid bound to
   `BoxSetDefinition.Tracks` (TrackNumber / SongName / Date / SegueOut).
3. **Review** — read-only summary, validation, and Save.

The wizard never touches audio; it is pure curation data entry. Import wiring
(fingerprint matching, per-concert manifests) is a documented post-MVP follow-up.

### Pull setlist + collision
In step 2, **Pull setlist for date** reads the gdshowsdb reference setlist
(`ShowLookupService.GetSetlist`) for a date, flattens it (set labels dropped,
per-song segue preserved), and appends one track per song stamped with that date
([Helpers/SetlistTrackBuilder.cs](Helpers/SetlistTrackBuilder.cs)). If the date
already has rows, a modal prompts **Replace / Append / Cancel**
([Views/PullCollisionDialog.xaml](Views/PullCollisionDialog.xaml)); the pure
predicate is `BoxSetPullCollision.HasTracksForDate`
([Helpers/BoxSetPullCollision.cs](Helpers/BoxSetPullCollision.cs)). A date with
no existing rows pulls with no prompt.

### Accordion date-grouping
The grid groups by date (toggle, default on) with collapsible per-date headers
showing the derived venue and track count
([Helpers/BoxSetGroupHeader.cs](Helpers/BoxSetGroupHeader.cs) `FormatGroupHeader`).
Headers behave as an **accordion** — exactly one group expanded, always the
last-touched date. The active date lives in a transient `ActiveGroupDate`; each
header's expand state is driven through
[Converters/GroupActiveConverter.cs](Converters/GroupActiveConverter.cs), with
the null-vs-empty rule (`null` = all-collapsed; `""` = the no-date group) owned
by the pure `BoxSetGroupHeader.IsActiveGroup`. Each header also carries a ✕
"remove this date" action ([Helpers/BoxSetTrackMutations.cs](Helpers/BoxSetTrackMutations.cs)
`RemoveTracksForDate`): it prompts a Yes/No confirm with the track count, then
drops every row for that date in one step. The accordion collapses to
all-collapsed only when the removed date was the open group — deleting a
different, collapsed group leaves the open one expanded (`ActiveGroupDate` is
nulled only when it equals the removed date). TrackNumbers are not renumbered,
matching the per-row delete. Grouping is purely a view concern — the model stays
a flat `List<BoxSetTrack>` and serialization is unchanged.

### Renumber action
A **Renumber** button (third bulk action in the step-2 row, after Pull) re-sequences
every track's `TrackNumber` to a contiguous `1..N` ordered by **`(Date asc, then
existing TrackNumber asc)`**, no-date (`""`) tracks **last**, via the pure
`BoxSetTrackMutations.RenumberByDate`. It fixes out-of-order pulls (group display
order is a side effect of TrackNumber, so a later-pulled date otherwise renders its
block above an earlier one) and closes delete gaps — without delete-and-reimport.
Non-destructive, so **no confirm prompt**; dates are untouched so the accordion's
active group stays valid (no `ActiveGroupDate` change — a single `_tracksView.Refresh()`
re-renders groups in the new order). Unlike grouping, Renumber **does change
serialization**: the helper physically reorders the backing list so the persisted
`tracks` array matches the numbering (array order == TrackNumber order ==
chronological). The renumber is **flat and unconditional** — hand-entered
disc-prefixed numbers (`101`, `503`) are overwritten; a disc-prefixed-preserving
variant is banked until import wiring lands (box-set-design-memo positions 10/11).
Segues are unaffected (within-date order preserved; no live next-track computation
over `BoxSetTrack`).

### Save identity
A box set's identity is its **slug** — one `<slug>.json` file per box, with the slug
derived from the name via `BoxSetService.DeriveSlug`. The save-overwrite/rename/collision
decision is centralized in the pure
[Helpers/BoxSetSaveResolution.cs](Helpers/BoxSetSaveResolution.cs)
`Resolve(originalSlug, newSlug, targetSlugExists)`, which returns one of four
`BoxSetSaveOutcome`s: **SaveNew** (new box, free name), **Overwrite** (editing, name
unchanged — slug == original, so the existing file is the box itself, not a collision),
**MoveRename** (editing, name changed to a free slug), and **NameCollision** (the target
slug already belongs to a *different* box). A brand-new box is simply the "edit with no
original" case (`originalSlug` null/empty).

### Edit-mode entry
Double-clicking (or pressing Enter on) a saved row in the Box Sets list opens the wizard
**pre-populated** for editing. `BoxSetsView` raises `EditBoxSetRequested` with the box's
slug; `ShellWindow.OpenBoxSetForEdit` reads a **fresh copy from disk** by slug
(`BoxSetService.Read`) — the deserialized object is itself the isolated editable buffer, so
edits never touch the saved file until Save and Cancel/"← Box Sets" discards by dropping the
wizard (no in-memory `DeepCopy` needed). If the box is gone since the list loaded, the list
refreshes and a notice shows instead of opening an empty wizard. Edit-mode reuses the wizard
via a shared ctor `BoxSetWizardView(svc, definition, originalSlug)` (the new-box ctor chains
to it with a fresh definition and null slug); the loaded definition is injected **before** the
grid/`_tracksView` wiring, step-1 fields are prefilled (`LoadStep1FieldsFromDefinition`, the
inverse of `SyncStep1FieldsToDefinition`), and the wizard lands on **step 2** (the shell calls
`GoToStep(2)` after subscribing `StepChanged`). The header reads **"Edit Box Set"** (driven by
`IsEditingExisting`). On Save the resolver's outcome decides the I/O: **Overwrite** and
**SaveNew** write in place; **MoveRename** writes the new slug **then** deletes the old
(write-then-delete: a failed delete leaves a recoverable orphan, delete-first would risk data
loss); **NameCollision** refuses. `Verified` and all other fields ride through because the
read-fresh definition is serialized whole.

---

## Reference Data Layer

Beyond `songs.json` and `releases.json`, the app ships read-only reference data
keyed by yyyy-MM-dd.

### Data/concerts/*.json — Layer-A authority files
Per-concert reference files (one per date), loaded by
[Services/ConcertLookupService.cs](Services/ConcertLookupService.cs). Each file
([Models/ConcertReference.cs](Models/ConcertReference.cs)) carries: `date`,
`venue`, `city`, `state`, `country`, `setlistFmId`, `setlistFmUrl`,
`lastUpdated`, `hasSetlist`, `multiShow`, `sets[]` (name + songs), and
`tracks[]` (position, songName, date, segue, set). Sourced from setlist.fm via
the `tools/SetlistFetcher` console tool. Loaded **AppData-first**
(`%APPDATA%\DeadEditor\concerts\`) with a bundled `Data\concerts\` fallback and a
first-run copy from bundle to AppData, so the dataset survives upgrades.

### Data/heady.json
Best-version rankings sourced from headyversion.com: a `versions[]` array of
`{ song, date, venue, city, state, rank, votes, url }`. Crowd-ranked "best
performance" pointers per song.

### Note: two overlapping concert-data systems
DeadEditor currently has two reference systems that overlap:
- **`shows.json`** (via `ShowLookupService`) — a **venue index** keyed by date. The
  venue/city/state index is (re)generated from setlist.fm by `tools/SetlistFetcher`
  (which writes only venue fields to shows.json — it does not write `sets`, see
  `tools/SetlistFetcher/Program.cs:164-171`). `shows.json` still carries legacy
  gdshowsdb `sets` for ~1,797 dates, but those are **no longer read** — see the
  setlist-source note below. Used for **venue lookup** at import (`GetShowByDate`)
  and venue write-back (`UpdateShow`/`SaveToFile`).
- **`Data/concerts/*.json`** (via `ConcertLookupService`) — richer per-concert
  files (above), generated entirely from setlist.fm, and the **sole source of
  setlist data**.

**Setlist source (since the concerts/ redirect):** `ShowLookupService.GetSetlist`
sources its `Sets` from `ConcertLookupService` (`Data/concerts/`) through the pure
[Helpers/ConcertSetlistAdapter.cs](Helpers/ConcertSetlistAdapter.cs), **not** from
`shows.json`. All setlist-derived methods (`GetDiscTrack`, `GetSegue`,
`GetSetlistSongCount`, `SuggestTrackNumber`) call `GetSetlist`, so they read
`concerts/` too. This fixed the box-set pull (loud "No setlist found") and the
Import/Edit "Match Setlist" surfaces (quiet button-disable) for the ~495 dates that
are venue-only stubs in `shows.json`. Only **venue** lookup still reads `shows.json`.

**FLAG:** the long-term division of labor between these two systems is still not
fully settled — `shows.json` is retained for the venue index + venue write-back,
which `ConcertLookupService` (read-only) does not yet cover.
[documentation/releases-inspection-2026-04-17.md](documentation/releases-inspection-2026-04-17.md)
(open questions, §2) records the systems as candidates for full unification. This
documents the current state, not an endorsed end-state.

---

## Core Features

### 1. Import Workflow
- Drag-and-drop folder or browse for concert directory
- Auto-parse folder name for date, venue, city, state
- Auto-import `.txt` info files (if present)
- Parse track listing from audio files
- Normalize song titles using fuzzy matching
- Auto-detect segues (e.g., "China Cat Sunflower > I Know You Rider")
- Write standardized ID3 tags to files
- MusicBrainz integration for official releases (select from multiple releases)

### 2. Library Browser
- Grid view of all imported concerts
- Display: Album artwork, date, venue, location, show type
- Quick search by date, venue, or location
- Advanced search (3 tabs):
  - **Contains Songs** - Find shows with ALL selected songs (in any order)
  - **Exclude Songs** - Find shows WITHOUT specific songs (NOT queries)
  - **Song Sequence** - Find shows with songs in specific order
- Song filter for handling 600+ songs in search dialogs
- Play concerts directly from library
- Edit metadata of already-imported concerts

### 3. MusicBrainz Integration (Official Releases)
- Query MusicBrainz API for official release metadata via audio fingerprinting
- **Release Selector Dialog** - Choose from multiple releases/editions of same album
  - Example: "Workingman's Dead" (1970 original, 2003 remaster, 2020 deluxe edition)
  - Each edition is treated as a separate album
  - **Status:** Dialog UI implemented but not triggering (see Known Issues)
- Pre-fill metadata fields for user validation
- User corrects/validates before final save
- **Dependencies:** Requires `fpcalc.exe` (Chromaprint) for audio fingerprinting

### 4. Audio Playback
- Play full concerts or individual tracks
- Track navigation (next/previous)
- Media key support (keyboard play/pause/stop/next/previous buttons)
- Display current track with segue markers and performance date

### 5. Song Database Management
- **Add Songs Dialog** - Add new songs on-the-fly with artist support
- **Manage Songs Dialog** - Browse all songs, view aliases, filter by artist, export to text
- Songs persist in `Data/songs.json` immediately
- **Artist-agnostic design** - No hardcoded artist assumptions in code

### 6. Metadata Editing
- Edit concert metadata after import
- Real-time preview updates
- Batch operations on track listings

---

## Key Design Decisions

### 1. Date Format Standardization
**Decision:** Always use yyyy-MM-dd format everywhere
**Reasoning:** Eliminates confusion from MM/dd/yyyy vs dd/MM/yyyy formats, ensures consistent sorting

### 2. Segue Detection
**Notation:** "China Cat Sunflower > I Know You Rider"
**Storage:** Tracks stored separately but linked with ">" marker
**Display:** Show both song names with segue marker in UI

### 3. Track Title Normalization
**Process:**
1. Strip leading track numbers ("01 ", "02 ")
2. Remove tape markers ("//")
3. Remove box-drawing characters (─, —, –)
4. Strip embedded dates from titles ("(1972-05-04)")
5. Normalize to canonical song name via fuzzy matching
6. Preserve segue markers

### 4. Universal Single-Path Library System
**Decision:** One library root for all album types; `AlbumType` is a metadata tag, not a folder decision
**Reasoning:**
- Folder selected for import is the atomic unit — files imported together stay together in one library folder
- Multi-date official releases (Road Trips, etc.) no longer scatter across date folders
- Single configuration to manage; same folder convention for every import path
- See [documentation/19-folder-import-and-manifests.md](documentation/19-folder-import-and-manifests.md) for the full design memo

### 5. In-Memory Concert Storage
**Decision:** No separate database file for concerts; read from file system + ID3 tags
**Reasoning:**
- Audio files are source of truth
- Avoids sync issues between database and files
- Simplifies backup (just backup audio files)

---

## Critical Bug Fixes (Historical Context)

### LINQ Lazy Evaluation Bug (Session 2026-01-08)
**Problem:** Song searches returned 0 results inconsistently
**Root Cause:** `Where()` clause was re-evaluated multiple times with captured variables changing between evaluations
**Solution:** Replace lazy LINQ chains with immediate `foreach` evaluation and explicit list building

### TextChanged Recursion Bug (Session 2026-01-08)
**Problem:** Duplicate searches triggered when updating search box programmatically
**Root Cause:** Setting `TextBox.Text` triggered `TextChanged` event handler recursively
**Solution:** Added `_isUpdatingSearchBox` flag to prevent recursive calls

### Multi-song Collection Bug (Session 2026-01-08)
**Problem:** Advanced search only collected visible filtered songs instead of selected songs
**Root Cause:** Collecting from `SongCheckListPanel.Children` (filtered view) instead of full list
**Solution:** Maintain separate `_allSongCheckBoxes` collection and collect from that

---

## File Organization

### Key Files by Category

#### Models
- `AlbumInfo.cs` - Concert/album metadata model
- `TrackInfo.cs` - Individual track metadata
- `LibrarySettings.cs` - User settings (paths, window positions)
- `SongDatabase.cs` - Song database structure with artist support

#### Services
- `MetadataService.cs` - ID3 tag reading/writing, info file import
- `NormalizationService.cs` - Song title normalization, fuzzy matching, Levenshtein distance
- `LibraryImportService.cs` - Concert import from folder structure
- `MusicBrainzService.cs` - MusicBrainz API integration
- `ShowLookupService.cs` - Setlist + venue lookup by date from Data/shows.json (GetSetlist / GetSegue / GetDiscTrack / SuggestTrackNumber / GetShowByDate / FormattedVenueLocation)
- `ReleaseLookupService.cs` - Album name autocomplete from Data/releases.json
- `ConcertLookupService.cs` - Per-date lookup over Data/concerts/ reference files
- `ManifestService.cs` - Sidecar metadata manifests (verified state for re-import)
- `FingerprintService.cs` - AcoustID/Chromaprint fingerprint precompute + persistence
- `MbidMigrationService.cs` - MBID migration orchestration
- `BoxSetService.cs` - BoxSetDefinition read/write/list/delete (AppData vs Data/box-sets)

#### Shell + Views
- `ShellWindow.xaml/.cs` - Single-window shell, sidebar, navigation
- `Views/LibraryGridView.xaml/.cs` - Library grid, search, concert list
- `Views/AlbumDetailView.xaml/.cs` - Album art, track list, date grouping
- `Views/ImportView.xaml/.cs` - Import workflow, normalize, renumber
- `Views/EditMetadataView.xaml/.cs` - Edit FLAC tags in-place
- `Views/SettingsView.xaml/.cs` - Library paths, fpcalc, primary artist
- `Views/HeaderBar.xaml/.cs` - Context-sensitive header bar
- `Views/SidebarPanel.xaml/.cs` - Sidebar navigation icons (Library, Import, Songs, Releases, Concerts, Box Sets, Settings)
- `Views/PlayerBar.xaml/.cs` - Transport controls, progress, volume
- `Views/PlaylistPanel.xaml/.cs` - Compact playlist panel
- `Views/SongsView`, `ReleasesView` - songs.json / releases.json editors
- `Views/ConcertDatabaseView`, `ConcertDetailView` - browse Data/concerts/
- `Views/EditSetlistView` - edit a concert's setlist
- `Views/MbidMigrationView` - MBID migration UI
- `Views/BoxSetsView`, `BoxSetWizardView` - box-set list + authoring wizard

#### Dialogs (modal)
- `AdvancedSearchDialog.xaml/.cs` - 3-tab search (Contains/Exclude/Sequence)
- `AddSongDialog.xaml/.cs` - Add songs on-the-fly
- `ManageSongsDialog.xaml/.cs` - Browse/export song database
- `ReleaseSelectorDialog.xaml/.cs` - Select from multiple MusicBrainz releases
- `MatchToSongDialog.xaml/.cs` - Manual match unmatched track to setlist song with auto-alias
- `MbidCandidateDialog.xaml/.cs` - Confirm a MusicBrainz release candidate (MBID migration)
- `TrackInfoDialog.xaml/.cs` - Inspect one track's on-disk tags + import status
- `UnmatchedSongsDialog.xaml/.cs` - Assign canonical titles to normalizer-unmatched tracks
- `Views/PullCollisionDialog.xaml/.cs` - Replace/Append/Cancel for box-set pull-setlist

#### Helpers / Converters
- `Helpers/SetlistTrackBuilder.cs` - gdshowsdb setlist -> flat BoxSetTrack rows
- `Helpers/BoxSetPullCollision.cs`, `BoxSetGroupHeader.cs`, `BoxSetTrackMutations.cs` - pure wizard predicates/mutations
- `Converters/GroupActiveConverter.cs` - drives the wizard accordion's IsExpanded

#### Tools
- `tools/SetlistFetcher/` - console tool: fetches GD shows from the setlist.fm API,
  (re)writes shows.json's venue index and the Data/concerts/ files

#### Data
- `Data/songs.json` - Song database (598 songs, artist-organized)
- `Data/shows.json` - Full gdshowsdb setlists by date (venue/location + per-song segues), via ShowLookupService
- `Data/releases.json` - Series templates + standalone release names for autocomplete
- `Data/concerts/*.json` - Layer-A per-concert authority files (setlist.fm, via SetlistFetcher)
- `Data/heady.json` - headyversion.com best-version rankings
- `Data/box-sets/*.json` - bundled box-set definitions (also %APPDATA%\DeadEditor\box-sets\)

---

## Development Workflow

### Building and Running
```bash
# Kill any background processes
taskkill //F //IM DeadEditor.exe //T
taskkill //F //IM dotnet.exe //T

# Build
dotnet build DeadEditor/DeadEditor.csproj

# Run
dotnet run --project DeadEditor/DeadEditor.csproj
```

### Running Tests
Automated tests live in `DeadEditor.Tests/` (xUnit, .NET 8). Run from repo root:

```bash
dotnet test DeadEditor.sln
```

- The repo root contains the canonical `.sln`; do not create additional `.sln` files inside subdirectories.
- New regression tests for feature commits go in `DeadEditor.Tests/`, not in the main project.
- Tests should not require WPF runtime initialization. Reference plain models, services, and helpers from the main project; do not instantiate `Window`/`UserControl` types in test code.

### Testing with Real Data
- Primary test library: User's personal Grateful Dead taper collection
- Add songs/aliases as unmatched tracks are encountered
- Verify fuzzy matching against real-world typos
- Test both album types: AudienceRecording and OfficialRelease (the OfficialRelease type covers studio albums, official live releases, and box sets)

---

## Historical Development Focus (2026-01-25)

*(Historical context, captured 2026-01-25. Retained as background; no longer the current focus.)*

### Multiple Official Release Editions
**Goal:** Support importing different editions of the same studio album as separate entities

**Problem:** Official releases often have multiple editions over time:
- Original release (e.g., "Workingman's Dead" 1970)
- Remastered edition (e.g., "Workingman's Dead" 2003)
- Deluxe/Anniversary edition (e.g., "Workingman's Dead" 2020 with bonus live tracks)

Each edition should be treated as a distinct album with its own metadata, even though they share the same base album name.

**MusicBrainz Integration:** Use `ReleaseSelectorDialog` to let user choose which specific release/edition they're importing, then treat each as separate in the library.

### Track-Level Search with Album Context (Related Feature)
**Goal:** Enable searching for specific performance dates within official releases

**Use Case:** If "Workingman's Dead 2003 Edition" includes live bonus tracks like "Morning Dew (1972-05-04)", searching for "1972-05-04" should find this track within the studio album context.

**Solution:**
1. Parse embedded dates from track titles in official releases
2. Show search results with album context (track name + containing album)
3. Open containing album folder when clicked
4. Preserve all existing search functionality

**Design Decision:** Import hybrid albums (studio + live bonus material) as Studio Albums with Edition field to keep them as one cohesive unit.

---

## Known Issues

### MusicBrainz Release Selector Not Appearing
**Status:** Dialog UI is implemented but never shows up during official release import

**Possible Causes:**
1. **fpcalc.exe missing** - Audio fingerprinting fails silently
   - MusicBrainzService searches for fpcalc.exe in: current dir, parent dirs, system PATH
   - Returns null if not found, causing lookup to fail
   - Check: Is fpcalc.exe in `D:\Projects\fpcalc.exe`?

2. **MusicBrainz API not returning multiple releases** - Only finds 1 release
   - Code auto-selects single release without showing dialog
   - May need to adjust filtering logic in `GetAllReleasesAsync()`
   - Current filter: Only includes "Official" status and "Album" type

3. **AcoustID API key issue** - Service initialization may be failing
   - Check how `_musicBrainzService` is initialized in `ImportView` (ImportView.xaml.cs)
   - Verify API key is valid

**Expected Behavior:**
- If 0 releases found → Error message
- If 1 release found → Auto-select (no dialog)
- If 2+ releases found → Show `ReleaseSelectorDialog`

**Debug Steps:**
1. Check console output for MusicBrainz logging (extensive Console.WriteLine statements exist)
2. Verify fpcalc.exe location
3. Test with known album that has multiple editions

### Minor UI Issue
- The shell window (`ShellWindow`) sometimes opens in background at startup (requires Alt+Tab to bring forward)

---

## Future Enhancement Ideas

### High Priority
- Complete testing with both album types (AudienceRecording, OfficialRelease)

### Low Priority
- Batch import multiple concerts at once
- Export concert metadata to CSV/JSON
- Dark/light theme toggle
- Keyboard shortcuts for common actions
- Duplicate concert detection (same date/venue)
- Statistics dashboard (most played songs, venue counts, etc.)
- Integration with online databases (archive.org, etree.org)
- Automated backup/sync functionality

---

## Questions for AI Assistants Resuming Sessions

When resuming development:

1. **Check git status** - What files have been modified?
2. **Read TODO.md** - What's currently in progress?
3. **Review recent commits** - What was done in the last session?
4. **Ask about priorities** - What does the user want to focus on?

### Common Session Workflows

#### Import Testing
- User provides path to concert folder
- Import and verify song matching
- Add missing songs/aliases as needed
- Commit song database updates

#### Bug Fixing
- User reports unexpected behavior
- Reproduce issue
- Identify root cause
- Implement fix
- Test thoroughly

#### Feature Development
- Clarify requirements
- Review existing code
- Implement incrementally
- Test with real data
- Update TODO.md

---

## Useful Context for AI Assistants

### The "Grateful Dead Problem"
Grateful Dead concerts have unique challenges:
- 2,300+ concerts over 30 years
- Songs played in different arrangements
- Segues between songs are musically significant
- Multiple recordings of same show (different tapers)
- Extensive live bonus material in official releases
- Fan community has strong conventions (setlist formats, venue naming)

### Song Title Variations
Common patterns requiring normalization:
- Segue arrows: "->", ">", "→"
- Parenthetical info: "(Live at...)", "(1972-05-04)", "(Acoustic)"
- Track numbers: "01 ", "1-01 ", "d1t01 "
- Tape flip markers: "//", "/ /"
- Typos: "Dnacing", "Wkae", "Monkey &"
- Character encoding: "Peggy─O", "Peggy-O", "Peggy-o"

### Why Fuzzy Matching Matters
A typical taper collection has metadata from:
- Original taper notes (hand-typed)
- Archive.org uploads (crowd-sourced)
- CD rips with OCR errors
- Automated tools with bugs
- Multiple editors over decades

Without fuzzy matching, you'd need hundreds of aliases per song. With 2-character tolerance, most typos are automatically handled.

---

## Conventions for AI Assistants

### Interacting with the User

Ask decisions and clarifications as **numbered questions in your reply text** — the user answers in chat. Do **not** use interactive popup/dialog question prompts for planning or clarification. This keeps the decision trail in the conversation and matches how the user works.

### When Modifying Code

**CRITICAL:** Before modifying ANY file, read the relevant documentation from the [Documentation Handbook](#documentation-handbook) first.

**Code Standards:**
- Preserve existing patterns (MVVM-like with code-behind)
- Use `Newtonsoft.Json` for JSON operations (already in project)
- Follow nullable reference type conventions
- Avoid LINQ lazy evaluation in search logic (see bug fix notes)
- Use guard clauses to prevent recursion in event handlers
- **NEVER hardcode artist names** - Code must be artist-agnostic
  - Bad: `if (artist == "Grateful Dead")`
  - Good: Use `PrimaryArtistName` from settings or artist-agnostic logic
- **Shared button styles:** `AccentButton` and `TertiaryButton` are centralized in `App.xaml` as shared resources (Tertiary was promoted on its second consumer, EditMetadataView's Renumber); new views consume them rather than re-declaring. `PrimaryButton` is NOT centralized - the key is overloaded (ImportView green vs. dialog blue) and must be disambiguated first (see follow-ups).
- **Long/blocking operations:** wrap them in `await App.Alerts.RunWithStatusAsync(title, work, message?)` — a reusable modal "please wait" status overlay that rides the AlertService host stack at `Panel.ZIndex=75` (between the read panel at 50 and the confirm host at 100, so a confirm can still surface above it). It owns the show, an `IProgress<StatusUpdate>` for mid-operation message updates, the overlapping-scope refcount (last-write-wins on the title; hides only when the last scope completes), and a guaranteed Hide in `finally` (success or exception; the exception rethrows). Indeterminate spinner only — a determinate bar is deferred. The overlay is non-cancelable and swallows all keys while showing (`Views/StatusHost`, see `documentation/alert-system-spec.md` § Status overlay).

**Documentation-First Workflow:**
1. **Check [Documentation Lookup Table](#documentation-lookup-table)** - Find the relevant doc file
2. **Read the documentation** - Understand the current spec
3. **Update documentation FIRST** if changing behavior
4. **Write code to match the doc** - Code implements the spec
5. **Update doc if implementation reveals issues** - Keep docs in sync
6. **Commit code + docs together** - Never commit one without the other

### When Adding Features

**CRITICAL:** Follow the [Documentation-First Development Workflow](#documentation-first-development-workflow) for all new features.

**Check lived demand, not abstract desire.** Before adding a feature, ask whether the user has ever used the equivalent capability in real life. "Of course I want X" is a trap when X *would* be nice in the abstract but doesn't connect to the actual goal. Skip features that fail this check; revisit if concrete need surfaces later.

**Feature Design:**
- Consider both Live and Official Release workflows
- Test with real concert data
- Update song database if new songs discovered
- Add to TODO.md with session notes
- Consider impact on existing imports (backward compatibility)
- Ensure feature works for any artist, not just Grateful Dead

**Documentation Requirements:**
- Document new methods in relevant doc file (signature, parameters, return value, business logic)
- Document new business rules and validation logic
- Include examples showing the new behavior
- Document error handling and edge cases
- Update related doc sections if feature affects existing behavior

### When Writing Commits
- Reference specific issues/features
- Include session date in TODO.md updates
- Note file counts and line changes for major work
- **Co-Authored-By trailers.** Every commit carries exactly two `Co-Authored-By` trailers:
  1. `Co-Authored-By: Claude <noreply@anthropic.com>` — the constant generic attribution.
  2. `Co-Authored-By: <model> <noreply@anthropic.com>` — identifies the specific model that authored the work (e.g. `Claude Opus 4.8 (1M context)`), updated to whatever model is in use.

  The example string is illustrative, not fixed — the second trailer tracks the current model. When in doubt, match the two trailers on the branch's last commit.
- **One concern per commit.** Each commit addresses a single concern, with related documentation and code committed together — no broken or half-finished intermediate states between commits.
- **Never push without authorization.** Commits accumulate locally on the feature branch; pushing to origin happens only on explicit authorization, never automatically.

---

## End of Document

**Last Updated:** 2026-06-11
**Architecture:** Single-window shell (`ShellWindow` + `NavigationService`); Box Sets feature shipped (curation wizard + flat model)
**Song Database:** 598 songs (594 Grateful Dead, 4 NRPS)
**Documentation:** 41 files in `documentation/` (specs, design memos, audits/inspections)
**Build/Test Baseline (2026-06-19):** `dotnet build DeadEditor/DeadEditor.csproj` (clean) -> 0 errors, 51 unique warnings (MSBuild reports 204; the WPF markup + main multi-pass compile re-emits warnings across the main and `_wpftmp` projects, so the raw count exceeds unique): 25 CA1416 platform-compat + 22 CS8618 uninitialized-non-nullable + 4 other nullable CS86xx; `dotnet test DeadEditor.sln` -> 401 passed, 0 failed, 0 skipped (was 355; +30 `TrackNumberPrefix` helper tests, +7 `NormalizationService` track-prefix wiring tests, +6 alias-idempotency tests, +3 AddSong clobber tests). Note (51 -> 49 -> 51 round trip): commit 2eec71e changed this to 49 believing 51 was stale, but miscounted CS8618 as 20; the tree has always emitted 22. Building 2eec71e in an isolated git worktree on 2026-06-20 still produced 22 CS8618 / 51 unique with an identical site list to HEAD, so the 49 never existed in any build and there was no regression. CA1416 (25), other CS86xx (4), and raw (204) were always correct.

## Development Environment
- OS: Windows 10.0.26200
- Shell: Git Bash
- Path format: Windows (use forward slashes in Git Bash)
- File system: Case-insensitive
- Line endings: CRLF (configure Git autocrlf)

## Playwright MCP Guide

File paths:
- Screenshots: `./CCimages/screenshots/`
- PDFs: `./CCimages/pdfs/`

Browser version fix:
- Error: "Executable doesn't exist at chromium-XXXX" → Version mismatch
- v1.0.12+ uses Playwright 1.57.0, requires chromium-1200 with `chrome-win64/` structure
- Quick fix: `npx playwright@latest install chromium`
- Manual symlink (if needed): `cd ~/AppData/Local/ms-playwright && cmd //c "mklink /J chromium-1200 chromium-1181"`
