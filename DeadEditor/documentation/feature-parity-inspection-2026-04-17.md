# Import vs Edit Metadata Feature Parity Inspection — 2026-04-17

## 1. Import View Feature Inventory

**File:** `Views/ImportView.xaml.cs` — **1,653 lines**

### Action Bar Buttons

| # | Feature | Trigger | Handler (file:line) | Services Called | Dialogs Opened | Side Effects |
|---|---------|---------|---------------------|----------------|----------------|--------------|
| 1 | **Read** (folder selector) | Button click | `ImportView.xaml.cs:254` ReadButton_Click | MetadataService.ReadFolder, MetadataService.ReadAlbumInfo, ShowLookupService.GetShowByDate | FolderBrowserDialog | Populates tracks + album info, auto-fills venue |
| 2 | **🔎 MusicBrainz** (fingerprint lookup) | Button click | `ImportView.xaml.cs:1218` MusicBrainzButton_Click | MusicBrainzService.LookupAllReleasesAsync, .GetReleaseTracksAsync, HttpClient (artwork) | ReleaseSelectorDialog | Sets Artist/Album/Year/tracks from MB data, downloads artwork |
| 3 | **✨ Normalize** | Button click | `ImportView.xaml.cs:959` NormalizeButton_Click | NormalizationService.NormalizeAll | UnmatchedSongsDialog (if unmatched) | Sets SongName/IsMatched on all tracks |
| 4 | **Renumber** | Button click | `ImportView.xaml.cs:1013` RenumberButton_Click | None (inline logic) | None | Sets TrackNumber using disc×100+track convention |
| 5 | **Match Setlist** | Button click | `ImportView.xaml.cs:1064` MatchSetlistButton_Click | ShowLookupService.GetSetlist, .GetDiscTrack, .GetSegue; NormalizationService.Normalize, .GetOfficialTitle | None | Assigns disc/track/segue from setlist; creates overflow disc for unmatched |
| 6 | **View Info** | Button click | `ImportView.xaml.cs:1511` ViewInfoButton_Click | None | MessageBox | Displays info file content |
| 7 | **Write to Files** | Button click | `ImportView.xaml.cs:1355` WriteButton_Click | MetadataService.WriteMetadata | Confirmation dialog | Writes all tags to FLAC/MP3 files on disk |
| 8 | **Import to Library** | Button click (green) | `ImportView.xaml.cs:1390` ImportButton_Click | LibraryImportService.ImportToLibrary, .ShowExistsInLibrary | Confirm + success notifications | Copies files to library, creates metadata.json, fires ImportCompleted |

### Album Info Bar (6 editable fields)

| # | Feature | Trigger | Handler (file:line) | Services Called |
|---|---------|---------|---------------------|----------------|
| 9 | **Artist** TextBox | TextChanged | `ImportView.xaml.cs:322` AlbumInfo_Changed | None |
| 10 | **Date** TextBox (auto-lookup) | TextChanged + LostFocus | `ImportView.xaml.cs:361` AlbumDateTextBox_LostFocus | ShowLookupService.GetShowByDate (auto-fills venue/city if empty) |
| 11 | **Venue** TextBox | TextChanged | `ImportView.xaml.cs:322` AlbumInfo_Changed | None |
| 12 | **City, State** TextBox | TextChanged | `ImportView.xaml.cs:322` AlbumInfo_Changed | None |
| 13 | **Album/Release** TextBox (autocomplete) | TextChanged + KeyDown + MouseClick | `ImportView.xaml.cs:402` AlbumNameTextBox_PreviewKeyDown | ReleaseLookupService.GetSuggestions |
| 14 | **Year** TextBox | TextChanged | `ImportView.xaml.cs:322` AlbumInfo_Changed | None |
| 15 | **Album Type** ComboBox | SelectionChanged | `ImportView.xaml.cs:458` AlbumTypeComboBox_SelectionChanged | None (inline auto-detect) |

### Track Grid

| # | Feature | Trigger | Handler (file:line) | Notes |
|---|---------|---------|---------------------|-------|
| 16 | **Cell editing** (Title, Disc, Date) | Double-click cell | `ImportView.xaml.cs:549` TracksDataGrid_CellEditEnding | Direct inline editing |
| 17 | **Segue checkbox** (→ column) | Click toggle | XAML binding | Sets Track.Segue boolean |
| 18 | **Drag-to-reorder** | Click+drag row | `ImportView.xaml.cs:857` Row_PreviewMouseLeftButtonDown → Row_Drop | Reorders tracks in ObservableCollection |
| 19 | **Column sorting** (Disc header) | Click header | `ImportView.xaml.cs:584` TracksDataGrid_Sorting | Custom disc+track sort |

### Right-Click Context Menu (on track rows)

| # | Feature | Handler (file:line) | Services Called | Dialog |
|---|---------|---------------------|----------------|--------|
| 20 | **▶ Play Now** | `ImportView.xaml.cs:647` | App.PlaybackService.Play | None |
| 21 | **＋ Add to Playlist** | `ImportView.xaml.cs:656` | App.PlaybackService.Playlist.Add | None |
| 22 | **📄 Track Info** | `ImportView.xaml.cs:670` | None | TrackInfoDialog |
| 23 | **🎵 Match to Song…** (conditional) | `ImportView.xaml.cs:746` MatchToSong_Click | NormalizationService.AddAlias, ShowLookupService.GetDiscTrack | MatchToSongDialog |

Match to Song is only shown when: (a) Match Setlist was run, (b) track is on overflow disc, (c) unclaimed setlist positions exist.

### Artwork Panel

| # | Feature | Handler (file:line) |
|---|---------|---------------------|
| 24 | **Change** artwork | `ImportView.xaml.cs:1525` ChangeArtworkButton_Click (opens OpenFileDialog) |
| 25 | **Remove** artwork | `ImportView.xaml.cs:1559` RemoveArtworkButton_Click |

**Total distinct user-facing features: 25**

---

## 2. Edit Metadata View Feature Inventory

**File:** `Views/EditMetadataView.xaml.cs` — **771 lines**

### Action Bar Buttons

| # | Feature | Trigger | Handler (file:line) | Services Called | Dialog |
|---|---------|---------|---------------------|----------------|--------|
| 1 | **Normalize** | Button click | `EditMetadataView.xaml.cs:678` NormalizeButton_Click | MetadataService.ParseTitleAndDate, NormalizationService.NormalizeAll | UnmatchedSongsDialog (if unmatched) |
| 2 | **Renumber** | Button click | `EditMetadataView.xaml.cs:745` RenumberButton_Click | None (inline logic) | None |

### Album Info Bar (6 editable fields + type dropdown)

| # | Feature | Trigger | Handler (file:line) | Services Called |
|---|---------|---------|---------------------|----------------|
| 3 | **Artist** TextBox | TextChanged | `EditMetadataView.xaml.cs:266` AlbumInfo_Changed | None |
| 4 | **Date** TextBox | TextChanged | `EditMetadataView.xaml.cs:266` AlbumInfo_Changed | None |
| 5 | **Venue** TextBox | TextChanged | `EditMetadataView.xaml.cs:266` AlbumInfo_Changed | None |
| 6 | **City, State** TextBox | TextChanged | `EditMetadataView.xaml.cs:266` AlbumInfo_Changed | None |
| 7 | **Album/Release** TextBox (autocomplete) | TextChanged + KeyDown + MouseClick | `EditMetadataView.xaml.cs:303` UpdateAlbumNameSuggestions | ReleaseLookupService.GetSuggestions |
| 8 | **Year** TextBox | TextChanged | `EditMetadataView.xaml.cs:266` AlbumInfo_Changed | None |
| 9 | **Album Type** ComboBox | SelectionChanged | `EditMetadataView.xaml.cs:288` AlbumTypeComboBox_SelectionChanged | None |

### Track Grid

| # | Feature | Trigger | Handler (file:line) |
|---|---------|---------|---------------------|
| 10 | **Cell editing** (Title/RawTitle, Disc, Date) | Double-click cell | `EditMetadataView.xaml.cs:637` TracksDataGrid_CellEditEnding |
| 11 | **Segue checkbox** (→ column) | Click toggle | `EditMetadataView.xaml.cs:613` Track_PropertyChanged |
| 12 | **Duration** column | Read-only display | N/A |
| 13 | **Track #** column | Read-only display | N/A |

### Artwork Panel

| # | Feature | Handler (file:line) |
|---|---------|---------------------|
| 14 | **Change** artwork | `EditMetadataView.xaml.cs:371` ChangeArtworkButton_Click |
| 15 | **Remove** artwork | `EditMetadataView.xaml.cs:410` RemoveArtworkButton_Click |

### Save / Cancel (triggered externally from HeaderBar)

| # | Feature | Handler (file:line) | Services Called |
|---|---------|---------------------|----------------|
| 16 | **Save Changes** | `EditMetadataView.xaml.cs:430` SaveChangesAsync | MetadataService.WriteMetadata, TagLib (artwork) |
| 17 | **Cancel / Back** | `EditMetadataView.xaml.cs:586` CancelEdit | Navigation.GoBack |

**Total distinct user-facing features: 17**

**Notable absences:** No right-click context menu. No MusicBrainz button. No Match Setlist button. No Track Info dialog. No Match to Song. No drag-to-reorder. No View Info. No date auto-lookup.

---

## 3. Gap Matrix

| Feature | Import | Edit Metadata | Shared Service? | Effort to Add to Edit Metadata |
|---------|--------|---------------|-----------------|-------------------------------|
| **🔎 MusicBrainz fingerprint lookup** | ✓ | ✗ | Yes — `MusicBrainzService.LookupAllReleasesAsync` | **M** — service is shared but orchestration (fingerprint → selector → apply) is 50 lines in ImportView code-behind |
| **Release selector dialog** | ✓ | ✗ | Yes — `ReleaseSelectorDialog` is generic (takes string + List\<ReleaseOption\>) | **S** — reuse dialog as-is |
| **Match Setlist** | ✓ | ✗ | Partly — `ShowLookupService.GetSetlist/GetDiscTrack` are shared; matching + overflow logic is 150 lines in ImportView | **M** — service calls are shared but the match-and-assign orchestration needs extraction |
| **Match to Song… (right-click)** | ✓ (conditional) | ✗ | Partly — `MatchToSongDialog` is generic; `NormalizationService.AddAlias` is shared; overflow-disc state management is ImportView-specific | **M** — dialog and alias service are reusable; need to replicate or extract overflow tracking |
| **📄 Track Info (right-click)** | ✓ | ✗ | Yes — `TrackInfoDialog` takes (TrackInfo, string?, string?), zero dependencies | **S** — add context menu, instantiate dialog |
| **Right-click context menu (any)** | ✓ (5 items) | ✗ (none) | N/A — each view builds its own menu | **S** — ~30 lines of menu construction |
| **▶ Play Now (right-click)** | ✓ | ✗ | Yes — `App.PlaybackService.Play` | **S** — one-liner in context menu |
| **＋ Add to Playlist (right-click)** | ✓ | ✗ | Yes — `App.PlaybackService.Playlist.Add` | **S** — one-liner in context menu |
| **Date auto-lookup on blur** | ✓ | ✗ | Yes — `ShowLookupService.GetShowByDate` | **S** — add LostFocus handler, 10 lines |
| **Drag-to-reorder tracks** | ✓ | ✗ | No — 90 lines of ImportView-specific drag/drop code | **M** — could be copy-pasted but would duplicate; extract to helper or accept duplication |
| **View Info** | ✓ | ✗ | N/A — info file only exists pre-import | **N/A** — not applicable post-import |
| **Write to Files** | ✓ | ≈ (Save Changes) | Yes — `MetadataService.WriteMetadata` | **Already present** — Save Changes serves same purpose |
| **Import to Library** | ✓ | N/A | N/A — already in library | **N/A** — not applicable post-import |
| **Normalize** | ✓ | ✓ | Yes — `NormalizationService.NormalizeAll` | **Already present** |
| **Renumber** | ✓ | ✓ | Inline logic (identical in both views) | **Already present** |
| **Album/Release autocomplete** | ✓ | ✓ | Yes — `ReleaseLookupService.GetSuggestions` | **Already present** |
| **Artwork Change/Remove** | ✓ | ✓ | Inline logic | **Already present** |
| **Album info editing** | ✓ | ✓ | Inline binding | **Already present** |
| **Track cell editing** | ✓ | ✓ | Inline binding | **Already present** |

### Effort Key
- **S** (small) = existing service + dialog, just wire a button/menu item; < 50 lines
- **M** (medium) = orchestration logic lives in ImportView.xaml.cs and needs extraction or duplication; 50–150 lines
- **L** (large) = deeply tangled feature requiring significant refactor
- **N/A** = not applicable to post-import context

---

## 4. Architectural Coupling

### 4.1 ImportView.xaml.cs — Size and Composition

- **1,653 lines** total
- **~75% orchestration logic** (~1,240 lines): MusicBrainz lookup + apply, Match Setlist matching + overflow, Match to Song + alias, Import to Library workflow, drag-to-reorder
- **~25% pure UI wiring** (~410 lines): TextChanged handlers, DataGrid binding, artwork display, status bar, notifications

ImportView is not quite a "god file" but it is the single largest code-behind in the project. It bundles several distinct workflows that share UI state (the track list, album info bar, overflow disc tracking) which makes extraction non-trivial but not impossible.

### 4.2 Shared Services Already in Place

| Service | Description | Reusable? |
|---------|-------------|-----------|
| `MusicBrainzService` | Audio fingerprinting via fpcalc + AcoustID, MusicBrainz release/track lookup, artwork URL retrieval | Yes — instantiated with API key, no UI coupling |
| `ShowLookupService` (singleton) | O(1) venue lookup by date, setlist data, disc/track position mapping, segue data | Yes — global singleton, zero UI coupling |
| `NormalizationService` | Fuzzy song matching (Levenshtein), batch normalize, alias management, songs.json I/O | Yes — instantiated, no UI coupling |
| `MetadataService` | FLAC/MP3 tag read/write, folder scanning, title+date parsing, segue-pair detection | Yes — instantiated, no UI coupling |
| `ReleaseLookupService` (singleton) | Album name autocomplete from releases.json | Yes — global singleton |
| `LibraryImportService` | File copy to library, folder naming, duplicate detection | Yes — but only relevant to import workflow |
| `MbidMigrationService` (uncommitted) | Batch MBID lookup + tag writing via events | Yes — event-driven, decoupled from specific views |

### 4.3 Logic Stuck in ImportView Code-Behind

These blocks contain orchestration logic that would need extraction to be reused from Edit Metadata:

| Logic Block | Lines | What It Does | Extraction Difficulty |
|-------------|-------|--------------|----------------------|
| **MusicBrainz orchestration** | `ImportView.xaml.cs:1218-1269` | Fingerprint → LookupAllReleases → show ReleaseSelectorDialog → ApplyMusicBrainzData (sets Artist/Album/Year/tracks/artwork) | Medium — 50 lines, clean service boundary but `ApplyMusicBrainzData` writes to ImportView-specific `_albumInfo` and `_tracks` |
| **ApplyMusicBrainzData** | `ImportView.xaml.cs:1271+` (estimated) | Maps MB release tracks to local tracks by position, downloads artwork | Medium — needs to be parameterized for different track sources |
| **Match Setlist orchestration** | `ImportView.xaml.cs:1064-1216` | Canonical matching → disc/track assignment → overflow disc creation → status reporting | Medium-Hard — 150 lines with internal state (`_lastSetlistSongs`, `_lastClaimedPositions`, `_overflowDiscNumber`) |
| **Match to Song handler** | `ImportView.xaml.cs:746-823` | Dialog → alias learning → disc/track reassignment → overflow renumbering | Medium — depends on Match Setlist state variables |
| **Drag-to-reorder** | `ImportView.xaml.cs:857-945` | Mouse event → drag → drop indicator → reorder | Easy to extract — self-contained 90-line block, no service deps |

### 4.4 Reusable Dialogs

| Dialog | Reusable As-Is? | Constructor | Notes |
|--------|-----------------|-------------|-------|
| **TrackInfoDialog** | ✓ Yes | `(TrackInfo, string?, string?)` | Zero service deps; displays tag data read-only |
| **ReleaseSelectorDialog** | ✓ Yes | `(string albumInfo, List<ReleaseOption>)` | Generic selection; returns `SelectedRelease` |
| **MatchToSongDialog** | ✓ Yes | `(string trackTitle, List<(int, string)>)` | Generic list picker; returns `SelectedSetlistIndex` |
| **MbidCandidateDialog** (uncommitted) | Partially | `(LibraryShow, List<ReleaseOption>, string?, string?)` | Coupled to `LibraryShow` model — usable from Edit Metadata but not from Import |
| **AlbumSearchDialog** | ✓ Yes | `(string, string)` | Pure text input, zero deps |
| **AddSongDialog** | Partially | `(NormalizationService)` | Needs NormalizationService injected |
| **UnmatchedSongsDialog** | ✓ Yes | Used by both Import and Edit Metadata already | Already shared |

### 4.5 ImportView-Specific UI Elements That Don't Generalize

| Element | Why It's Import-Specific |
|---------|-------------------------|
| **Read / Browse folder** button | Edit Metadata operates on already-imported library albums, not raw disk folders |
| **Import to Library** button | Album is already in the library |
| **View Info** button | Info files (.txt) are consumed during import; they aren't preserved post-import |
| **Folder preview** | Shows where files will be imported to; irrelevant post-import |
| **Progress bar** (indeterminate) | Used during MusicBrainz lookup and file copy; Edit Metadata's Save is fast |
| **Drag-drop zone** (folder drop target) | Initial folder selection mechanism |

---

## 5. Track Info Placement Analysis

| View | Should Have Track Info? | Existing Context Menu? | Data Model | Complications |
|------|------------------------|------------------------|------------|---------------|
| **ImportView** | ✓ Has it | ✓ 5-item menu (`ImportView.xaml.cs:614`) | `TrackInfo` built from disk scan | None — working today |
| **AlbumDetailView** | ✓ Yes | ✓ 4-item menu (`AlbumDetailView.xaml.cs:617`) — Play, Add, Add Selected, Delete | `TrackInfo` built from library files via `LoadTracksFromDisk()` | **S** — add one menu item to existing menu; TrackInfo model is identical |
| **EditMetadataView** | ✓ Yes | ✗ No context menu at all | `TrackInfo` built from library files via `MetadataService.ReadFolder()` | **S** — need to add both a context menu and the Track Info item; TrackInfo model is identical |
| **LibraryGridView** | Maybe (album-level, not track-level) | ✗ No context menu | `LibraryShow` (album-level, no individual tracks) | **N/A** — this view shows albums not tracks; Track Info doesn't apply at album level |
| **PlaylistPanel** | Possible but low priority | ✗ No context menu | `TrackInfo` from PlaybackService.Playlist | **S** — add context menu; data model is same TrackInfo |

**Key insight:** All views that display tracks use the same `TrackInfo` model. The `TrackInfoDialog` constructor signature `(TrackInfo, string?, string?)` works universally. The only effort is adding/extending context menus.

---

## 6. MBID Integration with Edit Metadata

### 6.1 Most Needed MBID Features in Edit Metadata

1. **Assign MBID to an album that wasn't tagged during import** — This is the primary use case. User imports an audience recording, later wants to link it to a MusicBrainz release. Edit Metadata should offer an "MB Lookup" button that fingerprints the first track(s), finds candidates, and writes the selected MBID to tags.

2. **Display existing MBID** — If the album already has `MUSICBRAINZ_ALBUMID` in its FLAC tags (from Picard, from the migration tool, or from a prior Edit Metadata session), the Edit Metadata view should display it. Currently `LibraryShow.MusicBrainzReleaseId` is populated by the library scanner (uncommitted code), so the value is already available when entering Edit Metadata from Album Detail.

3. **Clear/change MBID** — If the wrong MBID was assigned, user needs a way to clear or replace it. Simple text field + clear button alongside MB Lookup.

4. **Re-fetch metadata from existing MBID** — If an album has an MBID, offer a "Refresh from MB" action that calls `GetReleaseTracksAsync(mbid)` and updates track titles/artist/year. This is a convenience feature, not critical for Phase 1.

### 6.2 MbidCandidateDialog vs ReleaseSelectorDialog — Overlap or Distinct?

**They serve distinct roles:**

| Aspect | ReleaseSelectorDialog | MbidCandidateDialog |
|--------|----------------------|---------------------|
| **Purpose** | Choose which release edition to import (track metadata) | Choose which MBID to assign (identity tagging) |
| **Constructor** | `(string, List<ReleaseOption>)` — generic | `(LibraryShow, List<ReleaseOption>, string?, string?)` — MBID-specific |
| **Returns** | `SelectedRelease` (full ReleaseOption) | `UserAction` (Confirm/Skip) + `SelectedMbid` (string) |
| **Special features** | None | Manual MBID entry field, "View on MusicBrainz" link, Skip button, dry-run pre-selection |
| **Used by** | ImportView (MusicBrainz lookup) | MbidMigrationView (batch migration) |

**Recommendation:** Keep both. ReleaseSelectorDialog is for "which edition's track data do I want?" during import. MbidCandidateDialog is for "which MBID identity do I want to stamp on this album?" They could theoretically be merged, but the extra features in MbidCandidateDialog (manual entry, skip, dry-run) make it purpose-built for MBID workflows.

For Edit Metadata's MB Lookup button, **MbidCandidateDialog is the better fit** — it returns an MBID string and supports the skip/manual-entry UX that a post-import workflow needs.

### 6.3 If Edit Metadata Had an MB Lookup Button, Which Flow?

**Recommended: Hybrid approach.**

1. **If the album already has an MBID** → Use `MusicBrainzService.GetReleaseTracksAsync(existingMbid)` to refresh metadata. No fingerprinting needed.

2. **If no MBID exists** → Two sub-paths:
   - **Fingerprint path** (preferred): Call `MusicBrainzService.LookupAllReleasesAsync(tracks)` — same as Import. If multiple candidates → show `MbidCandidateDialog`. Write selected MBID to tags.
   - **Name search fallback** (if fpcalc unavailable): Call `MusicBrainzService.SearchReleasesByNameAsync(albumName, artist, year)` — currently dead code, designed exactly for this. Show candidates via `MbidCandidateDialog`.

This does **not** require the migration service. The migration service (`MbidMigrationService`) orchestrates batch processing across the entire library. Edit Metadata's MB Lookup is a single-album operation that calls `MusicBrainzService` directly — same pattern as Import's existing button.

---

## 7. Recommended Phasing for Feature Parity

### Phase 1: Quick Wins (all S-effort, no refactoring)

1. **Add right-click context menu to EditMetadataView** with Play Now, Add to Playlist, and Track Info items. ~40 lines, copy pattern from AlbumDetailView. (`EditMetadataView.xaml.cs`)

2. **Add Track Info to AlbumDetailView context menu.** One new MenuItem in existing menu at `AlbumDetailView.xaml.cs:697` (before Delete). ~10 lines.

3. **Add date auto-lookup to EditMetadataView.** Add `LostFocus` handler on Date textbox that calls `ShowLookupService.GetShowByDate()` and fills Venue/City if empty. ~15 lines, same pattern as `ImportView.xaml.cs:361-380`.

4. **Display MBID in Edit Metadata** (once MBID foundation is committed). Add read-only text field showing `_show.MusicBrainzReleaseId` if present. ~5 lines XAML + binding.

### Phase 2: Service-Backed Features (M-effort, some extraction)

5. **MB Lookup button in EditMetadataView.** Extract the fingerprint → selector → apply sequence from `ImportView.xaml.cs:1218-1269` into a reusable helper method or lightweight service class (e.g., `MusicBrainzLookupOrchestrator`). Wire to a new button in Edit Metadata. Uses `MbidCandidateDialog` for selection. Writes MBID to tags via `MetadataService`. ~100 lines new code + ~50 lines extracted.

6. **Match Setlist button in EditMetadataView.** The core matching logic at `ImportView.xaml.cs:1064-1216` needs extraction into a service or static helper that takes `(List<TrackInfo>, string date)` and returns match results. EditMetadataView would consume the results and update its track list. ~150 lines to extract + ~30 lines to wire.

7. **Match to Song context menu in EditMetadataView.** Depends on Phase 2 item 6 (needs setlist state). Add conditional menu item that opens `MatchToSongDialog`, calls `NormalizationService.AddAlias`, and updates track disc/track. ~80 lines, depends on extracted setlist state.

8. **Drag-to-reorder in EditMetadataView.** Copy the 90-line drag/drop block from ImportView or extract to a shared behavior class. Low risk, self-contained.

### Phase 3: Larger Refactors (if warranted)

9. **Extract `ImportOrchestrator` service.** Move all orchestration logic (MB lookup + apply, Match Setlist + overflow, Match to Song + alias) out of ImportView code-behind into a reusable service. Both ImportView and EditMetadataView become thin UI shells. This is the "do it right" approach but is only justified if Phase 2 reveals excessive duplication.

10. **Unified context menu component.** If Track Info, Play, Add to Playlist, Match to Song appear in 3+ views, extract a shared `TrackContextMenuBuilder` that assembles the appropriate menu items based on context (pre-import vs. post-import vs. read-only). Avoids N copies of dark-themed menu construction code.

---

## 8. Open Questions

1. **Should Edit Metadata's MB Lookup write the MBID to tags immediately, or only on Save?** Import writes tags explicitly (Write to Files button). Edit Metadata batches all changes until Save. The MBID should follow Edit Metadata's existing pattern (deferred save), but this means the MBID needs to be stored in memory on `_albumInfo` or `_show` until Save is clicked.

2. **Should Album Detail also get a Track Info menu item, or only Edit Metadata?** Album Detail already has a 4-item context menu. Adding Track Info is trivial (Phase 1 item 2). But the user might also want it in the Playlist panel — scope decision needed.

3. **What happens if MB Lookup in Edit Metadata finds a different release than what was used during import?** The user might have imported with MB data from Release A, then later the MB Lookup finds Release B. Should Edit Metadata offer to re-apply track titles from the new release? Or just update the MBID? This affects whether `ApplyMusicBrainzData` logic is needed or just MBID assignment.

4. **Is drag-to-reorder useful post-import?** Reordering tracks in Edit Metadata means changing track numbers in already-written FLAC files. This is valid but raises the question: should renumbering happen automatically after drag, or require an explicit Renumber click?

5. **Should Match Setlist in Edit Metadata create an overflow disc?** In Import, unmatched tracks go to an overflow disc for later manual matching. Post-import, the tracks are already numbered and on disk. Creating a virtual overflow disc means renumbering files on Save — is this the desired behavior, or should post-import Match Setlist only suggest matches without moving tracks?

6. **MbidCandidateDialog takes a `LibraryShow` — will Edit Metadata always have one?** Edit Metadata is entered from Album Detail, which provides a `LibraryShow`. So yes, the dialog can be used. But if MB Lookup is ever added to Import, a different dialog (ReleaseSelectorDialog) or adapter would be needed since Import doesn't have a `LibraryShow`.
