# Edit Metadata Feature Parity Spec (Phase 1 + Phase 2)

## 1. Goals

- Edit Metadata becomes a superset of Import's capabilities for already-imported albums
- Track Info is accessible from every view where tracks are visible (Edit Metadata, Album Detail, Playlist panel)
- MusicBrainz Lookup works in Edit Metadata with user-controlled per-field application
- Match Setlist works in Edit Metadata as a non-destructive suggestion engine (no overflow disc, no renumbering)
- Drag-to-reorder works post-import with save-deferred auto-renumbering
- Right-click context menus reach parity (Play Now, Add to Playlist, Track Info, Match to Song)

## 2. Non-Goals

| Item | Rationale |
|------|-----------|
| Phase 3 ImportOrchestrator extraction | Deferred — ~200 lines of orchestration duplication is acceptable until Edit Metadata is stable |
| MB Lookup in Album Detail | Album Detail is a read-only view; only Edit Metadata gets MB Lookup |
| Match Setlist overflow disc creation post-import | Tracks are already on disk with stable numbering; creating a virtual overflow disc would require renumbering files on Save |
| Automatic track renumbering (standalone) | Only happens as a side-effect of drag-to-reorder |
| Cross-disc track moves in drag-to-reorder | Disc boundary is preserved; moving a track from Disc 1 to Disc 2 is out of scope |
| Any changes to ImportView | This phase is about Edit Metadata parity, not Import changes |
| MB Lookup in Playlist panel | Playlist is a playback queue, not an editing surface |
| Bulk operations across multiple albums | Single-album editing only |

## 3. Feature Inventory and Gap Closures

Each entry corresponds to a row from the gap matrix in [feature-parity-inspection-2026-04-17.md](feature-parity-inspection-2026-04-17.md) Section 3.

### 3.1 Track Info Context Menu (Phase 1)

- **Source:** Inspection gap matrix row "📄 Track Info (right-click)" — S effort
- **Trigger:** Right-click track row → "📄 Track Info" menu item
- **Service reused:** None — `TrackInfoDialog` at [TrackInfoDialog.xaml.cs:12](../TrackInfoDialog.xaml.cs#L12) takes `(TrackInfo, string?, string?)` with zero service dependencies
- **Dialog:** `TrackInfoDialog` — reused as-is
- **In-memory state affected:** None (read-only display)
- **Save-time behavior:** N/A
- **Scope:** Also adds Play Now and Add to Playlist menu items (see 3.5)

### 3.2 Date Auto-Lookup on ALBUMDATE Field (Phase 1)

- **Source:** Inspection gap matrix row "Date auto-lookup on blur" — S effort
- **Trigger:** `LostFocus` event on `AlbumDateTextBox`
- **Service reused:** `ShowLookupService.Instance.GetShowByDate(date)` at [ShowLookupService.cs:140](../Services/ShowLookupService.cs#L140) — same pattern as [ImportView.xaml.cs:361-380](../Views/ImportView.xaml.cs#L361-L380)
- **Dialog:** None
- **In-memory state affected:** `_albumInfo.Venue`, `_albumInfo.CityState` (only if currently empty), `_hasUnsavedChanges` set to `true`
- **Save-time behavior:** Venue/CityState written to tags like any other album info field change
- **Scope:** Only auto-fills empty fields; never overwrites user-entered venue/city data. Also calls `ShowLookupService.GetSetlist()` at [ShowLookupService.cs:193](../Services/ShowLookupService.cs#L193) to enable/disable Match Setlist button (Phase 2).

### 3.3 Right-Click "Match to Song…" in Track Grid (Phase 1)

- **Source:** Inspection gap matrix row "Match to Song… (right-click)" — M effort (reduced: no overflow disc in Edit Metadata)
- **Trigger:** Right-click track row → "🎵 Match to Song…" menu item. Shown **conditionally**: only after Match Setlist has been run (Phase 2), and only on unmatched tracks.
- **Service reused:**
  - `MatchToSongDialog` at [MatchToSongDialog.xaml.cs](../MatchToSongDialog.xaml.cs) — reused as-is, constructor `(string trackTitle, List<(int, string)>)`
  - `NormalizationService.AddAlias()` at [NormalizationService.cs:585](../Services/NormalizationService.cs#L585) — auto-learns alias
  - `NormalizationService.GetOfficialTitle()` at [NormalizationService.cs:389](../Services/NormalizationService.cs#L389) — resolves canonical name
- **Dialog:** `MatchToSongDialog` — reused as-is
- **In-memory state affected:** `track.SongName` updated to canonical title from setlist, `track.IsMatched` set to `true`, `_hasUnsavedChanges` set to `true`
- **Save-time behavior:** Updated SongName written to TITLE tag on Save
- **Scope:** Does NOT reassign disc/track numbers. Does NOT create overflow disc. Does NOT renumber any tracks. Only updates the song title and learns the alias. This is the key simplification vs Import's Match to Song at [ImportView.xaml.cs:746-823](../Views/ImportView.xaml.cs#L746-L823).

### 3.4 MBID Display Field (Phase 1)

- **Source:** Inspection Section 6.1 item 2 — "Display existing MBID"
- **Trigger:** Always visible in album info bar when Edit Metadata loads
- **Service reused:** None — reads `_show.MusicBrainzReleaseId` (populated by library scanner, from MBID foundation uncommitted code at [LibraryGridView.xaml.cs:767-789](../Views/LibraryGridView.xaml.cs#L767-L789))
- **Dialog:** None
- **In-memory state affected:** None (read-only display; editable only via MB Lookup in Phase 2)
- **Save-time behavior:** N/A for display; if MB Lookup (Phase 2) changes the MBID, Save writes it
- **UI detail:** Read-only TextBlock below the Album Type dropdown. Shows MBID value or "—" if not set. Includes a small "Copy" button (same pattern as TrackInfoDialog's copy buttons).

### 3.5 Right-Click Context Menu (Phase 1, prerequisite for all menu items)

- **Source:** Inspection gap matrix row "Right-click context menu (any)" — S effort
- **Trigger:** Right-click on any track row in EditMetadataView's `TracksDataGrid`
- **Services reused:**
  - `App.PlaybackService.Play()` — Play Now
  - `App.PlaybackService.Playlist.Add()` — Add to Playlist
- **Dialog:** None (menu construction only)
- **In-memory state affected:** None (playback is external)
- **Save-time behavior:** N/A
- **Scope:** Menu items: ▶ Play Now, ＋ Add to Playlist, 📄 Track Info, separator, 🎵 Match to Song… (conditional). Dark-themed menu follows the existing pattern from [AlbumDetailView.xaml.cs:644-652](../Views/AlbumDetailView.xaml.cs#L644-L652).

### 3.6 MusicBrainz Lookup Button with Per-Field Checkbox Dialog (Phase 2)

- **Source:** Inspection gap matrix row "🔎 MusicBrainz fingerprint lookup" — M effort
- **Trigger:** Button click — "🔎 MusicBrainz" button in Edit Metadata action bar
- **Services reused:**
  - `MusicBrainzService.LookupAllReleasesAsync()` at [MusicBrainzService.cs:218](../Services/MusicBrainzService.cs#L218) — fingerprint path
  - `MusicBrainzService.SearchReleasesByNameAsync()` at [MusicBrainzService.cs:127](../Services/MusicBrainzService.cs#L127) — name search fallback
  - `MusicBrainzService.GetReleaseTracksAsync()` at [MusicBrainzService.cs:790](../Services/MusicBrainzService.cs#L790) — fetch track data for checked "Track titles" field
- **Dialog:** `MbidCandidateDialog` at [MbidCandidateDialog.xaml.cs:18](../Views/MbidCandidateDialog.xaml.cs#L18) — **extended** with per-field checkbox panel (see Section 4)
- **In-memory state affected:** See Section 4 data flow
- **Save-time behavior:** All MB-derived values written to tags via existing `MetadataService.WriteMetadata()` at [EditMetadataView.xaml.cs:460](../Views/EditMetadataView.xaml.cs#L460) and MBID written via Xiph/TXXX pattern from MBID foundation spec Section 4
- **Scope:** Does NOT download artwork (artwork changes use existing Change/Remove buttons). Does NOT auto-apply any field except MBID — all other fields require explicit checkbox opt-in.

**Lookup flow:**

1. If `_show.MusicBrainzReleaseId` is already set → offer two paths via a small confirmation: "Use existing MBID to refresh metadata?" (Yes → skip to step 4 with existing MBID) / "Search for a different release" (→ step 2)
2. Fingerprint path: `LookupAllReleasesAsync(_tracks, _tracks.Count)` — same as [ImportView.xaml.cs:1234](../Views/ImportView.xaml.cs#L1234)
3. If fingerprint returns nothing AND fpcalc is unavailable: fallback to `SearchReleasesByNameAsync(_albumInfo.AlbumName, _albumInfo.Artist, _albumInfo.Year)` — reuses dead code at [MusicBrainzService.cs:127](../Services/MusicBrainzService.cs#L127)
4. Show `MbidCandidateDialog` (extended) with candidates + per-field checkboxes
5. Apply result to in-memory state (Section 4 data flow)

### 3.7 Match Setlist Button with Suggestions-Only Behavior (Phase 2)

- **Source:** Inspection gap matrix row "Match Setlist" — M effort
- **Trigger:** Button click — "Match Setlist" button in Edit Metadata action bar. Enabled/disabled based on `ShowLookupService.GetSetlist(date)` returning non-null.
- **Services reused:**
  - `ShowLookupService.Instance.GetSetlist(date)` at [ShowLookupService.cs:193](../Services/ShowLookupService.cs#L193)
  - `ShowLookupService.Instance.GetDiscTrack(date, position)` at [ShowLookupService.cs:205](../Services/ShowLookupService.cs#L205) — **not used for disc/track assignment** in Edit Metadata; only used for ordering reference
  - `NormalizationService.Normalize()` at [NormalizationService.cs:60](../Services/NormalizationService.cs#L60)
  - `NormalizationService.GetOfficialTitle()` at [NormalizationService.cs:389](../Services/NormalizationService.cs#L389)
- **Dialog:** None (inline operation with status feedback)
- **In-memory state affected:** Matched tracks get `SongName` updated to canonical setlist title, `IsMatched` set to `true`, `_hasUnsavedChanges` set to `true`. Unmatched tracks are flagged visually but **not moved or renumbered**.
- **Save-time behavior:** Updated SongNames written to TITLE tags via `MetadataService.WriteMetadata()`
- **Scope:** See Section 6 for full behavior spec

### 3.8 Drag-to-Reorder with Auto-Renumber (Phase 2)

- **Source:** Inspection gap matrix row "Drag-to-reorder tracks" — M effort
- **Trigger:** Click+drag track row to new position in `TracksDataGrid`
- **Service reused:** None — inline UI logic. Pattern from [ImportView.xaml.cs:857-945](../Views/ImportView.xaml.cs#L857-L945) (90 lines, self-contained, no service deps)
- **Dialog:** None
- **In-memory state affected:** Track order in `_tracks` list changes. `TrackNumber` recomputed for all tracks within the affected disc. `_hasUnsavedChanges` set to `true`.
- **Save-time behavior:** Updated TrackNumbers written to tags via `MetadataService.WriteMetadata()`
- **Scope:** See Section 7 for full behavior spec

### 3.9 Features Intentionally Not Closed

| Gap | Reason |
|-----|--------|
| **View Info** | Info files (.txt) exist only pre-import; not preserved post-import. N/A. |
| **Write to Files** | Already present as "Save Changes" at [EditMetadataView.xaml.cs:430](../Views/EditMetadataView.xaml.cs#L430). No gap. |
| **Import to Library** | Album is already in library. N/A. |
| **Column sorting (Disc header)** | EditMetadataView tracks are pre-sorted by disc+track on load. Adding click-to-sort is trivial but not requested. Deferred. |

## 4. Per-Field Checkbox Dialog (extends MbidCandidateDialog)

### UI

Extends `MbidCandidateDialog` at [MbidCandidateDialog.xaml](../Views/MbidCandidateDialog.xaml) + [MbidCandidateDialog.xaml.cs](../Views/MbidCandidateDialog.xaml.cs).

Below the selected candidate (and below the manual MBID entry field), add a **"Fields to apply"** panel. This panel is only visible when a candidate is selected (radio or manual MBID) and is hidden when no selection is active.

```
┌─ Fields to apply ──────────────────────────────────────┐
│ ☑ Release MBID (always applied)          [disabled]    │
│ ☐ Album title: "Europe '72" → "Europe '72 (Vol. 2)"   │
│ ☐ Album artist: "Grateful Dead" → "Grateful Dead"     │
│ ☐ Release year: "1972" → "1990"                        │
│ ☐ Track titles (applies to matching positions)         │
│                                                        │
│ Only checked fields will be applied.                   │
│ Click Save in Edit Metadata to commit changes to disk. │
└────────────────────────────────────────────────────────┘
```

**Checkbox rules:**
- `Release MBID` — always checked, checkbox disabled (non-interactive). MBID is the primary purpose of this dialog.
- All other checkboxes — unchecked by default. Each shows the current value → candidate value for comparison (except "Track titles" which just describes the operation).
- Checkboxes only appear when the candidate provides that field. E.g., if the candidate has no year, the year checkbox is omitted.

**Context sensitivity:** The "Fields to apply" panel needs to know the current album state to show "current → candidate" comparisons. The dialog constructor gains two new optional parameters:

```csharp
public MbidCandidateDialog(
    LibraryShow album,
    List<ReleaseOption> candidates,
    string? dryRunPreSelectedMbid,
    string? warning,
    AlbumInfo? currentAlbumInfo = null,    // NEW: for field comparison display
    bool showFieldCheckboxes = false)       // NEW: false for migration, true for Edit Metadata
```

When `showFieldCheckboxes` is `false` (migration tool usage), the panel is hidden and behavior is unchanged from the MBID foundation implementation.

### Data flow

1. User clicks "🔎 MusicBrainz" in Edit Metadata
2. Fingerprint + optional name-search runs (using `MusicBrainzService` methods)
3. `MbidCandidateDialog` opens with `showFieldCheckboxes: true` and `currentAlbumInfo` populated
4. User selects candidate (radio or manual), checks desired fields, clicks Confirm
5. Dialog returns an `MbidApplyResult`:

```csharp
public class MbidApplyResult
{
    public string Mbid { get; set; } = "";              // Always populated
    public bool ApplyAlbumTitle { get; set; }
    public bool ApplyAlbumArtist { get; set; }
    public bool ApplyReleaseYear { get; set; }
    public bool ApplyTrackTitles { get; set; }
    public ReleaseOption? SelectedRelease { get; set; }  // Full candidate data for field values
}
```

6. Edit Metadata handler receives the result and applies to in-memory state:
   - `_show.MusicBrainzReleaseId = result.Mbid` (always)
   - If `ApplyAlbumTitle`: `_albumInfo.AlbumName = result.SelectedRelease.Title`, update `AlbumNameTextBox.Text`
   - If `ApplyAlbumArtist`: `_albumInfo.Artist = result.SelectedRelease.Artist`, update `ArtistTextBox.Text`
   - If `ApplyReleaseYear`: `_albumInfo.Year = result.SelectedRelease.Year`, update `YearTextBox.Text`
   - If `ApplyTrackTitles`: call `GetReleaseTracksAsync(result.Mbid)`, then match by disc+position (same logic as [ImportView.xaml.cs:1312-1331](../Views/ImportView.xaml.cs#L1312-L1331))
7. `_hasUnsavedChanges = true`
8. UI reflects proposed values immediately
9. Save button writes everything to tags

### State tracking

Edit Metadata already tracks dirty state via `_hasUnsavedChanges` (boolean) at [EditMetadataView.xaml.cs:25](../Views/EditMetadataView.xaml.cs#L25). Individual field-level dirty tracking is **not needed** — the existing pattern writes all fields on Save regardless of which ones changed (see [EditMetadataView.xaml.cs:460](../Views/EditMetadataView.xaml.cs#L460): `_metadataService.WriteMetadata(_albumInfo, _tracks)` writes everything).

The MBID specifically needs to be persisted on `_show.MusicBrainzReleaseId` (in memory) and then written to tags during `SaveChangesAsync()`. The Save method at [EditMetadataView.xaml.cs:430](../Views/EditMetadataView.xaml.cs#L430) will need ~6 lines added after the existing `WriteMetadata()` call to write the MBID tag using the same Xiph/TXXX pattern from MBID foundation spec Section 4.

### Where in code

- **Extend:** [Views/MbidCandidateDialog.xaml](../Views/MbidCandidateDialog.xaml) — add "Fields to apply" panel below manual entry
- **Extend:** [Views/MbidCandidateDialog.xaml.cs](../Views/MbidCandidateDialog.xaml.cs) — add `MbidApplyResult` return type, constructor parameters, checkbox logic
- **Reuse:** `MusicBrainzService.LookupAllReleasesAsync()` at [MusicBrainzService.cs:218](../Services/MusicBrainzService.cs#L218)
- **Reuse:** `MusicBrainzService.SearchReleasesByNameAsync()` at [MusicBrainzService.cs:127](../Services/MusicBrainzService.cs#L127)
- **Reuse:** `MusicBrainzService.GetReleaseTracksAsync()` at [MusicBrainzService.cs:790](../Services/MusicBrainzService.cs#L790)
- **Integrate at:** [Views/EditMetadataView.xaml.cs](../Views/EditMetadataView.xaml.cs) — add MB Lookup button handler

## 5. Track Info Placement

### 5.1 EditMetadataView

- **File:** [Views/EditMetadataView.xaml.cs](../Views/EditMetadataView.xaml.cs)
- **Current state:** No context menu exists (inspection confirmed at Section 2, line 111: "Notable absences: No right-click context menu")
- **Change:** Add `TracksDataGrid_MouseRightButtonUp` handler. Build dark-themed context menu with: ▶ Play Now, ＋ Add to Playlist, 📄 Track Info, separator, 🎵 Match to Song… (conditional on Match Setlist having been run).
- **Track Info handler:** `new TrackInfoDialog(clickedTrack, _albumInfo?.Artist, _albumInfo?.AlbumName)` — `LibraryShow` available via `_show` field at [EditMetadataView.xaml.cs:18](../Views/EditMetadataView.xaml.cs#L18)
- **Data model:** `_tracks` is `List<TrackInfo>` at [EditMetadataView.xaml.cs:23](../Views/EditMetadataView.xaml.cs#L23), so `clickedTrack` resolves directly from `row.Item as TrackInfo`.

### 5.2 AlbumDetailView

- **File:** [Views/AlbumDetailView.xaml.cs](../Views/AlbumDetailView.xaml.cs)
- **Current state:** 4-item context menu at [AlbumDetailView.xaml.cs:617](../Views/AlbumDetailView.xaml.cs#L617): Play Now, Add to Playlist, Add Selected to Playlist, Delete Track
- **Change:** Add "📄 Track Info" menu item **before** the separator (after Add Selected, before Delete). ~10 lines.
- **Track Info handler:** Same constructor. `clickedTrack` resolves from `row.Item` via the existing `TrackInfo`/`TrackViewItem` pattern at [AlbumDetailView.xaml.cs:636-641](../Views/AlbumDetailView.xaml.cs#L636-L641).
- **Artist/album context:** Available from `_show.AlbumName` and artist from the first track or `_show` metadata.

### 5.3 PlaylistPanel

- **File:** [Views/PlaylistPanel.xaml.cs](../Views/PlaylistPanel.xaml.cs)
- **Current state:** No context menu. Double-click plays track at [PlaylistPanel.xaml.cs:93](../Views/PlaylistPanel.xaml.cs#L93). Delete via keyboard shortcut (`TryRemoveSelectedTrack` at [PlaylistPanel.xaml.cs:116](../Views/PlaylistPanel.xaml.cs#L116)).
- **Change:** Add `PlaylistDataGrid_MouseRightButtonUp` handler. Context menu items: ▶ Play Now, 📄 Track Info, separator, 🗑 Remove from Playlist.
- **Track Info handler:** `row.Item` is `PlaylistTrackViewModel` at [PlaylistPanel.xaml.cs:15](../Views/PlaylistPanel.xaml.cs#L15). Access underlying `TrackInfo` via `vm.Track`. Artist/album: `vm.Track.Artist` / `vm.Track.Album` (standard TagLib fields on TrackInfo).
- **Data model note:** `PlaylistTrackViewModel` wraps `TrackInfo`. The `.Track` property provides direct access.

## 6. Match Setlist in Edit Metadata

### Behavior

1. User clicks "Match Setlist" button (enabled only when `ShowLookupService.GetSetlist(date)` at [ShowLookupService.cs:193](../Services/ShowLookupService.cs#L193) returns non-null for the current album date)
2. Flatten setlist into ordered list with canonical names — same logic as [ImportView.xaml.cs:1082-1094](../Views/ImportView.xaml.cs#L1082-L1094):
   - For each set, for each song: `_normalizationService.GetOfficialTitle(song.Name) ?? song.Name`
3. For each track in `_tracks`:
   - Normalize track title: `_normalizationService.Normalize(track.SongName)`
   - Resolve to canonical: `_normalizationService.GetOfficialTitle(nameToMatch) ?? nameToMatch`
   - Find first unclaimed setlist position with matching canonical title (case-insensitive)
4. **Confident matches only:**
   - Update `track.SongName` to canonical setlist title
   - Set `track.IsMatched = true`
   - Apply segue from setlist data
   - Mark position as claimed
5. **Unmatched tracks:** Flag visually. Do NOT change their disc number, track number, or song name.

### What Match Setlist does NOT do (vs Import)

- No overflow disc creation (Import: [ImportView.xaml.cs:1161-1190](../Views/ImportView.xaml.cs#L1161-L1190))
- No disc/track reassignment from setlist positions (Import: [ImportView.xaml.cs:1143-1148](../Views/ImportView.xaml.cs#L1143-L1148))
- No renumbering of any tracks

### State stored for Match to Song

After Match Setlist runs, store:
- `_lastSetlistSongs` — the flattened setlist with canonical names
- `_lastClaimedPositions` — which positions were matched
- `_matchSetlistHasRun` — boolean flag for conditional context menu

These enable the "Match to Song…" context menu item (Section 3.3) for unmatched tracks.

### UI feedback

- **Status bar message after run:** "Matched X of Y tracks to setlist, Z segues. N unmatched — right-click to match manually."
- **Unmatched track row styling:** Amber/yellow left border (4px `#D7A74A` border on the row's left edge via DataGrid `LoadingRow` event or row style DataTrigger on `IsMatched == false` when `_matchSetlistHasRun == true`).
- **Tooltip on unmatched rows:** "Unmatched — right-click → Match to Song…"

### Where in code

- **Reuse:** `ShowLookupService.Instance.GetSetlist()` at [ShowLookupService.cs:193](../Services/ShowLookupService.cs#L193)
- **Reuse:** `NormalizationService.Normalize()` at [NormalizationService.cs:60](../Services/NormalizationService.cs#L60)
- **Reuse:** `NormalizationService.GetOfficialTitle()` at [NormalizationService.cs:389](../Services/NormalizationService.cs#L389)
- **Implement at:** [Views/EditMetadataView.xaml.cs](../Views/EditMetadataView.xaml.cs) — new `MatchSetlistButton_Click` handler (~60 lines, duplicating the matching loop from Import minus the overflow/renumber logic)

## 7. Drag-to-Reorder

### Behavior

- Track grid rows become draggable via mouse events (same WPF native pattern as Import)
- On drop: target position inserts the dragged track; intervening tracks shift (not swap)
- `TrackNumber` recomputed for all tracks within the affected disc using the existing `disc×100+track` convention
- Row styling shows `_hasUnsavedChanges` indicator (existing header bar "Save Changes" button already reflects this)
- Save commits renumbered TrackNumbers to disk

### Auto-renumber on drop

After the insert operation (same as [ImportView.xaml.cs:912-919](../Views/ImportView.xaml.cs#L912-L919)):

```
For each disc D in the album:
  trackNum = 1
  For each track in _tracks where track.DiscNumber == D (in list order):
    track.TrackNumber = D * 100 + trackNum
    trackNum++
```

This runs in memory immediately after the drop. The grid refreshes to show updated track numbers.

### Cross-disc considerations

- Reordering within a disc: shifts only that disc's track numbers
- Moving a track from Disc 1 to Disc 2: **out of scope** for this phase. The drag operation only reorders within the flat `_tracks` list; disc boundaries are determined by the existing `DiscNumber` on each track and are not changed by drag. If a user drags a Disc 1 track past a Disc 2 track, the track keeps its original disc number and only its position within that disc changes.

### Where in code

- **Pattern from:** [ImportView.xaml.cs:857-945](../Views/ImportView.xaml.cs#L857-L945) — 90-line self-contained block with: `Row_PreviewMouseLeftButtonDown`, `Row_MouseMove`, `Row_DragOver`, `Row_Drop`, `UpdateDropIndicator`, `HideDropIndicator`
- **Implement at:** [Views/EditMetadataView.xaml](../Views/EditMetadataView.xaml) — add `DropIndicator` Border element, add event handlers to DataGrid row style
- **Implement at:** [Views/EditMetadataView.xaml.cs](../Views/EditMetadataView.xaml.cs) — copy drag/drop handlers, add auto-renumber after drop
- **Data model note:** ImportView uses `List<TrackInfoViewModel>` for `_tracks`; EditMetadataView uses `List<TrackInfo>`. The drag/drop code needs to operate on `TrackInfo` directly. The `RemoveAt`/`Insert` pattern is identical — only the type changes.

## 8. Architecture

### Files to modify

| File | Change | Phase |
|------|--------|-------|
| [Views/EditMetadataView.xaml](../Views/EditMetadataView.xaml) | Add: MB Lookup button, Match Setlist button, MBID display field, DropIndicator element, drag event handlers on row style | Phase 1 (MBID field) + Phase 2 (buttons, drag) |
| [Views/EditMetadataView.xaml.cs](../Views/EditMetadataView.xaml.cs) | Add: context menu handler, Track Info/Play/Playlist menu items, Match to Song handler, MB Lookup handler, Match Setlist handler, drag-to-reorder handlers, date auto-lookup LostFocus handler, MBID write in SaveChangesAsync | Phase 1 + Phase 2 |
| [Views/MbidCandidateDialog.xaml](../Views/MbidCandidateDialog.xaml) | Add: "Fields to apply" checkbox panel below manual entry section | Phase 2 |
| [Views/MbidCandidateDialog.xaml.cs](../Views/MbidCandidateDialog.xaml.cs) | Add: `MbidApplyResult` class, `showFieldCheckboxes` constructor parameter, `currentAlbumInfo` parameter, checkbox state management, extended return value | Phase 2 |
| [Views/AlbumDetailView.xaml.cs](../Views/AlbumDetailView.xaml.cs) | Add: "📄 Track Info" menu item to existing context menu (before Delete separator, after Add Selected) | Phase 1 |
| [Views/PlaylistPanel.xaml.cs](../Views/PlaylistPanel.xaml.cs) | Add: `MouseRightButtonUp` handler, context menu with Play Now / Track Info / Remove | Phase 1 |
| [Views/PlaylistPanel.xaml](../Views/PlaylistPanel.xaml) | Add: `MouseRightButtonUp` event on DataGrid | Phase 1 |

### Files NOT to modify

- `Views/ImportView.xaml` / `.cs` — this phase is about Edit Metadata parity, not Import changes
- `Services/MusicBrainzService.cs` — no new methods needed; MBID foundation already added rate limiting and User-Agent
- `Services/ShowLookupService.cs` — used read-only, no changes
- `Services/NormalizationService.cs` — used read-only, no changes
- `Services/MetadataService.cs` — `WriteMetadata()` already handles all standard fields; MBID write is handled separately in Save
- `Services/MbidMigrationService.cs` — migration tool is a separate workflow; Edit Metadata calls `MusicBrainzService` directly
- All MBID foundation files in the uncommitted working tree — those land as-is

### New files

**None expected.** All changes fit within existing files. The `MbidApplyResult` class is small enough to live inside `MbidCandidateDialog.xaml.cs` (same pattern as `CandidateAction` enum living in [MbidMigrationService.cs:473](../Services/MbidMigrationService.cs#L473)).

If implementation reveals a need for a new file (e.g., an extracted MB lookup orchestrator), stop and flag rather than improvising.

### Services reused (complete list)

| Service Method | File:Line | Used By |
|----------------|-----------|---------|
| `ShowLookupService.Instance.GetShowByDate()` | [ShowLookupService.cs:140](../Services/ShowLookupService.cs#L140) | Date auto-lookup (3.2) |
| `ShowLookupService.Instance.GetSetlist()` | [ShowLookupService.cs:193](../Services/ShowLookupService.cs#L193) | Match Setlist (3.7), enable/disable button (3.2) |
| `NormalizationService.Normalize()` | [NormalizationService.cs:60](../Services/NormalizationService.cs#L60) | Match Setlist (3.7) |
| `NormalizationService.GetOfficialTitle()` | [NormalizationService.cs:389](../Services/NormalizationService.cs#L389) | Match Setlist (3.7), Match to Song (3.3) |
| `NormalizationService.AddAlias()` | [NormalizationService.cs:585](../Services/NormalizationService.cs#L585) | Match to Song (3.3) |
| `MusicBrainzService.LookupAllReleasesAsync()` | [MusicBrainzService.cs:218](../Services/MusicBrainzService.cs#L218) | MB Lookup (3.6) |
| `MusicBrainzService.SearchReleasesByNameAsync()` | [MusicBrainzService.cs:127](../Services/MusicBrainzService.cs#L127) | MB Lookup fallback (3.6) |
| `MusicBrainzService.GetReleaseTracksAsync()` | [MusicBrainzService.cs:790](../Services/MusicBrainzService.cs#L790) | MB Lookup track title apply (3.6) |
| `MetadataService.WriteMetadata()` | [MetadataService.cs](../Services/MetadataService.cs) (called at [EditMetadataView.xaml.cs:460](../Views/EditMetadataView.xaml.cs#L460)) | Save (existing) |
| `ReleaseLookupService.Instance.GetSuggestions()` | Already wired at [EditMetadataView.xaml.cs:306](../Views/EditMetadataView.xaml.cs#L306) | Album name autocomplete (existing) |
| `App.PlaybackService.Play()` | Global singleton | Play Now context menu (3.5) |
| `App.PlaybackService.Playlist.Add()` | Global singleton | Add to Playlist context menu (3.5) |

## 9. Data Model Additions

### LibraryShow (no changes beyond MBID foundation)

`MusicBrainzReleaseId` property is already added by the uncommitted MBID foundation work at [Models/LibraryShow.cs](../Models/LibraryShow.cs). No additional properties needed.

### EditMetadataView internal state (new fields)

| Field | Type | Purpose |
|-------|------|---------|
| `_matchSetlistHasRun` | `bool` | Controls visibility of "Match to Song…" context menu item and unmatched row styling |
| `_lastSetlistSongs` | `List<(string Name, string Canonical, int Position, bool Segue)>?` | Stored after Match Setlist for use by Match to Song |
| `_lastClaimedPositions` | `HashSet<int>?` | Tracks which setlist positions have been claimed |
| `_dragStartPoint` | `Point` | Drag-to-reorder state |
| `_draggedItem` | `TrackInfo?` | Drag-to-reorder state |
| `_isDragging` | `bool` | Drag-to-reorder state |

These are all private fields on `EditMetadataView`, not model changes. They follow the same pattern as ImportView's equivalent fields at [ImportView.xaml.cs:853-855](../Views/ImportView.xaml.cs#L853-L855).

### MbidApplyResult (new class)

Defined in `MbidCandidateDialog.xaml.cs`. See Section 4 for structure.

### No per-field dirty tracking needed

`_hasUnsavedChanges` (existing boolean at [EditMetadataView.xaml.cs:25](../Views/EditMetadataView.xaml.cs#L25)) is sufficient. The Save method writes all fields unconditionally. No need for per-field "applied-from-MB" markers or individual dirty flags.

## 10. Edge Cases

| Scenario | Handling |
|----------|----------|
| User runs MB Lookup, applies fields, then runs MB Lookup again with different candidate | Second run overwrites first run's pending fields. All changes are in memory — nothing is saved yet. MBID and any checked fields from the second run replace the first. |
| User runs Match Setlist, then edits a matched TITLE manually | Manual edit wins. The in-memory `SongName` is whatever the user last typed. |
| User drags tracks around, then runs Match Setlist | Match Setlist operates on current in-memory `SongName` values regardless of track order. It does not change track order or disc/track numbers. |
| User cancels Edit Metadata (navigates away without Save) | All pending changes discarded via existing cancel path at [EditMetadataView.xaml.cs:586](../Views/EditMetadataView.xaml.cs#L586). Disk untouched. Confirmation dialog shown if `_hasUnsavedChanges` is true. |
| MBID already present on album | MB Lookup still works. Step 1 of the lookup flow (Section 3.6) asks whether to refresh from existing MBID or search for a different release. |
| Multi-disc album | Per-disc track numbering preserved during drag-to-reorder. Match Setlist matches by song name regardless of disc. Cross-disc drag explicitly out of scope. |
| fpcalc.exe not configured | MB Lookup falls back to name search via `SearchReleasesByNameAsync()`. Status bar shows "fpcalc not configured — using name search." |
| No setlist data for album date | Match Setlist button is disabled. Tooltip: "No setlist data for this date." |
| Match to Song clicked before Match Setlist has run | Menu item is hidden (gated on `_matchSetlistHasRun`). |
| Album has no tracks | All buttons (MB Lookup, Match Setlist, Normalize, Renumber) show "No tracks" status message and return early. Existing pattern at [EditMetadataView.xaml.cs:678-684](../Views/EditMetadataView.xaml.cs#L678-L684). |
| MusicBrainz API returns zero candidates | `MbidCandidateDialog` shows only the manual MBID entry field (existing behavior at [MbidCandidateDialog.xaml.cs:64-72](../Views/MbidCandidateDialog.xaml.cs#L64-L72)). Per-field checkboxes are hidden since there's no candidate to compare against. |
| Track title apply from MB when local tracks don't match by disc+position | Unmatched tracks are left unchanged. Status message reports "X of Y tracks matched." Same behavior as [ImportView.xaml.cs:1317-1331](../Views/ImportView.xaml.cs#L1317-L1331). |

## 11. Testing Plan

### Manual Verification Checklist (for Gregg post-implementation)

#### Phase 1 Features

1. **Track Info — Edit Metadata:** Open any album → Edit Metadata → right-click a track → "📄 Track Info." Confirm dialog shows correct file metadata (path, format, tags, MBID if present).

2. **Track Info — Album Detail:** Open any album → right-click a track → "📄 Track Info." Confirm same dialog appears with correct data.

3. **Track Info — Playlist:** Add tracks to playlist → expand playlist → right-click a track → "📄 Track Info." Confirm dialog shows correct data.

4. **Context menu — Edit Metadata:** Right-click a track in Edit Metadata. Confirm menu shows: Play Now, Add to Playlist, Track Info. Confirm Play Now starts playback. Confirm Add to Playlist adds to queue.

5. **Date auto-lookup:** In Edit Metadata, clear Venue and City/State fields, then change date to a known date (e.g., `1977-05-08`), then click away from the date field. Confirm Venue and City/State auto-fill from shows.json.

6. **Date auto-lookup — no overwrite:** With Venue already populated, change date. Confirm Venue is NOT overwritten.

7. **MBID display:** Open Edit Metadata for an album with an MBID tag. Confirm MBID is displayed in the album info section. Open Edit Metadata for an album without MBID. Confirm "—" is shown.

#### Phase 2 Features

8. **MB Lookup — fingerprint path:** In Edit Metadata for an official release, click "🔎 MusicBrainz." Confirm candidate dialog appears with candidates. Select one, check "Track titles" and "Album title." Confirm in-memory fields update. Click Save. Reopen Track Info — confirm MBID is written to file tag.

9. **MB Lookup — name search fallback:** Temporarily remove fpcalc path. Run MB Lookup. Confirm it falls back to name search (status bar message). Confirm candidates still appear.

10. **MB Lookup — per-field checkboxes:** Run MB Lookup, select a candidate. Confirm MBID checkbox is checked and disabled. Confirm other checkboxes are unchecked. Check "Album title" only. Confirm only album title and MBID are applied; artist and year unchanged.

11. **MB Lookup — existing MBID:** Run MB Lookup on an album that already has an MBID. Confirm the "Use existing MBID to refresh?" prompt appears.

12. **Match Setlist:** In Edit Metadata for a concert with setlist data, click "Match Setlist." Confirm matched tracks get canonical titles. Confirm unmatched tracks have amber left border. Confirm disc/track numbers are NOT changed.

13. **Match to Song (after Match Setlist):** Right-click an unmatched (amber) track → "🎵 Match to Song…" Confirm dialog shows unclaimed setlist positions. Select one. Confirm track title updates to canonical name. Confirm alias was added to songs.json.

14. **Match to Song — hidden before Match Setlist:** Right-click a track before running Match Setlist. Confirm "Match to Song…" is NOT in the context menu.

15. **Drag-to-reorder:** In Edit Metadata, drag a track to a new position. Confirm track numbers auto-update. Confirm `_hasUnsavedChanges` is reflected (Save button enabled). Click Save. Reopen album — confirm new track order persists in file tags.

16. **Cancel discards all changes:** Run MB Lookup + Match Setlist + drag-reorder. Click Cancel. Confirm "Discard changes?" dialog. Click Yes. Reopen album — confirm no changes were written.

#### MBID End-to-End Integration

17. **MBID round-trip:** Import an album → MB Lookup assigns MBID → Save → close → reopen in Edit Metadata → confirm MBID display shows correct value → Track Info confirms MBID in file tags.

18. **Migration tool + Edit Metadata:** Run MBID migration on a few albums → open one in Edit Metadata → confirm MBID displays → run MB Lookup → confirm existing MBID prompt appears.

### Unit tests

No test project exists. Flagged in MBID foundation inspection, still deferred.

## 12. Out of Scope / Future Work

- **Phase 3:** ImportOrchestrator service extraction — eliminate ~200 lines of orchestration duplication between ImportView and EditMetadataView by extracting shared matching/MB logic into a service
- **Cross-disc track moves** in drag-to-reorder
- **Match Setlist overflow disc creation** post-import
- **MB Lookup in Album Detail** — Album Detail is read-only; edit operations belong in Edit Metadata
- **Bulk operations** across multiple albums
- **Artwork download from MB** in Edit Metadata — artwork changes already have dedicated Change/Remove buttons
- **Column sorting** in EditMetadataView track grid
- **Playlist panel: Remove from Playlist** via context menu — low priority, keyboard Delete already works
