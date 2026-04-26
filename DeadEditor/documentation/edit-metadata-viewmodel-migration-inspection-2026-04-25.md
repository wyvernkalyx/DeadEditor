# EditMetadataView ViewModel Migration Inspection — 2026-04-25

## 1. TrackInfoViewModel today

**File:** `Models/TrackInfoViewModel.cs` (185 lines)

### Properties

| Property | Type | Category | Notes |
|----------|------|----------|-------|
| `Track` | `TrackInfo` (read-only) | Underlying model | Public getter exposes raw model |
| `DisplayTitle` | `string` | Computed (backed by `_displayTitle`) | Recomputed by `UpdateDisplayTitle()` |
| `InheritedDate` | `string` | Computed (backed by `_inheritedDate`) | Shows track date or falls back to album date |
| `TrackNumber` | `int` | Passthrough proxy | Read/write, fires INPC + `DisplayTrackNumber` |
| `DisplayTrackNumber` | `string` | Passthrough (read-only) | Delegates to `Track.DisplayTrackNumber` |
| `DiscNumber` | `int` | Passthrough proxy | Read/write, sets `Track.IsModified`, fires INPC + `DisplayTrackNumber` |
| `SongName` | `string` | Passthrough proxy | Read/write, fires INPC, calls `UpdateDisplayTitle()` |
| `Segue` | `bool` | Passthrough proxy | Read/write, fires INPC, calls `UpdateDisplayTitle()` |
| `TrackDate` | `string` | Passthrough proxy | Read/write, fires INPC, calls `UpdateDisplayTitle()` |
| `Duration` | `string` | Passthrough (read-only) | Delegates to `Track.Duration` |

**Not proxied** (must access via `.Track`): `FilePath`, `FileName`, `RawTitle`, `IsModified`, `IsMatched`, `AlbumDate`, `Title`, `HeadyIcon`, `HeadyTooltip`, `HeadyUrl`

### Methods

| Method | Lines | Purpose |
|--------|-------|---------|
| `UpdateDisplayTitle()` | 134-161 | Recomputes `DisplayTitle` from SongName + Segue + date. Has `_isEditMode` branch. |
| `UpdateInheritedDate()` | 163-176 | Recomputes `InheritedDate` from TrackDate with album-date fallback |
| `Track_PropertyChanged()` | 33-43 | Event handler: relays TrackInfo.SongName/Segue/TrackDate changes to UpdateDisplayTitle/UpdateInheritedDate |

No `Refresh*` or `Recompute*` methods beyond the above two.

### `_isEditMode` branch (lines 136-139)

```csharp
if (_isEditMode)
{
    // EDIT MODE: Display the raw SongName as-is from FLAC tags
    DisplayTitle = Track.SongName ?? Track.Title ?? "";
}
```

In edit mode, `DisplayTitle` = raw `SongName` with **no date suffix, no segue marker**. This was intentional for Import's edit-in-cell experience but does **not** match what EditMetadataView needs. EditMetadataView wants the aggregated `"Song > (date)"` format.

**For Edit Metadata's needs:** Either set `isEditMode: false` when constructing ViewModels, or change the edit-mode branch to include segue + date (matching the import-mode branch). The simplest approach is `isEditMode: false`.

### PropertyChanged plumbing

- TrackInfoViewModel implements `INotifyPropertyChanged` directly (line 11)
- Each proxy property setter calls `OnPropertyChanged()` with `[CallerMemberName]`
- `SongName`, `Segue`, `TrackDate` setters also call `UpdateDisplayTitle()` which sets `DisplayTitle` → fires INPC for `DisplayTitle`
- Constructor subscribes to `Track.PropertyChanged` (line 30) — catches changes made directly to the `Track` object (e.g., from batch operations like NormalizeAll that modify `Track.SongName` directly)
- This subscription means even if code modifies `Track.SongName` directly (not through the ViewModel proxy), `DisplayTitle` still updates

### Constructor

```csharp
public TrackInfoViewModel(TrackInfo track, AlbumInfo albumInfo, bool isEditMode = false)
```

- Requires a `TrackInfo` and `AlbumInfo`
- `isEditMode` defaults to `false` (import mode)
- Calls `UpdateDisplayTitle()` and `UpdateInheritedDate()` during construction
- Subscribes to `Track.PropertyChanged`

---

## 2. Every TrackInfo usage in EditMetadataView

### XAML bindings (EditMetadataView.xaml)

| Line | Binding | Property | Operation | Model-change impact |
|------|---------|----------|-----------|-------------------|
| 281 | `{Binding TrackNumber}` | TrackNumber | Read (IsReadOnly) | **Works unchanged** — ViewModel has TrackNumber proxy |
| 286 | `{Binding DiscNumber, Mode=TwoWay}` | DiscNumber | Read/Write | **Works unchanged** — ViewModel has DiscNumber proxy |
| 290 | `{Binding RawTitle, Mode=TwoWay}` | RawTitle | Read/Write | **MUST CHANGE** — ViewModel does not proxy RawTitle. Must switch to DisplayTitle (display) + SongName (edit), matching ImportView pattern |
| 305 | `{Binding Segue, Mode=TwoWay}` | Segue | Read/Write | **Works unchanged** — ViewModel has Segue proxy |
| 323 | `{Binding TrackDate, Mode=TwoWay}` | TrackDate | Read/Write | **Works unchanged** — ViewModel has TrackDate proxy |
| 338 | `{Binding Duration}` | Duration | Read (IsReadOnly) | **Works unchanged** — ViewModel has Duration proxy |

### Code-behind (EditMetadataView.xaml.cs)

| Line(s) | Code | Operation | Impact |
|---------|------|-----------|--------|
| 32 | `private List<TrackInfo> _tracks = new();` | Field declaration | **MUST CHANGE** to `ObservableCollection<TrackInfoViewModel>` |
| 49 | `private TrackInfo? _draggedItem;` | Drag state | **MUST CHANGE** to `TrackInfoViewModel?` |
| 91-99 | `_tracks.Clear(); _tracks.AddRange(folderTracks)` | Collection mutation | **MUST CHANGE** — wrap each TrackInfo in ViewModel before adding |
| 143-154 | `foreach (var track in _tracks)` — date extraction from SongName | Iterate + write `track.TrackDate` | Can operate on `.Track` — or move into ViewModel construction |
| 157-180 | Sorting `_tracks` by date/disc/track | Iterate + reorder | **MUST CHANGE** — sort by `t.Track.TrackDate`, `t.Track.DiscNumber`, etc., or sort before wrapping |
| 183-186 | `track.PropertyChanged += Track_PropertyChanged` | Subscribe INPC | **May remove** — ViewModel already subscribes to Track.PropertyChanged. Change tracking could subscribe to ViewModel instead |
| 189 | `TracksDataGrid.ItemsSource = _tracks` | UI bind | **Works if _tracks is ObservableCollection<TrackInfoViewModel>** |
| 246 | `_tracks.Count` | Read count | Works unchanged |
| 474 | `_tracks.Count == 0` | Read count | Works unchanged |
| 502 | `_metadataService.WriteMetadata(_albumInfo, _tracks)` | Save — passes track list | **MUST CHANGE** — `WriteMetadata` takes `List<TrackInfo>`, must unwrap: `_tracks.Select(t => t.Track).ToList()` |
| 587-590 | `_tracks.Where(t => !string.IsNullOrEmpty(t.SongName)).Select(t => t.SongName!)` | Read SongName for search cache | **MUST CHANGE** — `t.Track.SongName` or `t.SongName` (ViewModel proxies SongName) |
| 658-675 | `Track_PropertyChanged` handler, casts `sender` to `TrackInfo` | Change tracking | **MUST CHANGE** — if subscribed to ViewModel, sender is ViewModel. Or keep subscribing to Track directly. |
| 682-694 | `CellEditEnding`, casts `e.Row.Item` to `TrackInfo` | Cell edit handler | **MUST CHANGE** — cast to `TrackInfoViewModel`, call `UpdateDisplayTitle()` after commit |
| 701-718 | `ReconstructRawTitles()` — iterates `_tracks`, reads/writes `track.SongName`, `track.Segue`, `track.TrackDate`, `track.RawTitle` | Batch write | **MUST CHANGE** — access via `.Track` property. Alternatively, eliminate this method if ViewModel handles display reactively |
| 738-745 | Normalize — reads `track.RawTitle`, writes `track.SongName`, `track.TrackDate` | Batch write | **MUST CHANGE** — access via `.Track` |
| 746 | `_normalizationService.NormalizeAll(_tracks)` | Passes tracks to service | **MUST CHANGE** — NormalizeAll takes `List<TrackInfo>`, must unwrap |
| 763-766 | `_tracks.Where(t => t.IsMatched == false)` | Filter unmatched | **MUST CHANGE** — `t.Track.IsMatched` (IsMatched not proxied on ViewModel) |
| 793-810 | Renumber — writes `track.TrackNumber`, `track.DiscNumber`, `track.IsModified` | Batch write | **MUST CHANGE** — access via `.Track` or ViewModel proxies |
| 884 | `row.Item is not TrackInfo clickedTrack` | Context menu cast | **MUST CHANGE** to `TrackInfoViewModel` |
| 993 | `MatchToSong_Click(TrackInfo track)` | Method signature | **MUST CHANGE** to take `TrackInfoViewModel` (or keep TrackInfo, unwrapping at call site) |
| 1001 | `track.SongName` | Read | Access via `.Track` or change method signature |
| 1012-1018 | `track.SongName = ...; track.IsMatched = ...; track.IsModified = ...; track.Segue = ...` | Write multiple props | Access via `.Track` — but Segue/SongName writes through ViewModel would auto-update DisplayTitle |
| 1075-1109 | MatchSetlist — iterates `_tracks`, reads/writes `track.SongName`, `track.IsMatched`, `track.IsModified`, `track.Segue` | Batch read/write | **MUST CHANGE** — access via `.Track` |
| 1323-1339 | `ApplyMbTrackTitles` — writes `localTrack.SongName`, `localTrack.IsMatched`, `localTrack.IsModified` | Batch write | **MUST CHANGE** — access via `.Track` |
| 1359 | `_draggedItem = row.Item as TrackInfo` | Drag start | **MUST CHANGE** — cast to `TrackInfoViewModel` |
| 1384 | `e.Data.GetDataPresent(typeof(TrackInfo))` | Drag over check | **MUST CHANGE** — `typeof(TrackInfoViewModel)` |
| 1390 | `targetRow.Item as TrackInfo` | Drag target | **MUST CHANGE** — `TrackInfoViewModel` |
| 1404-1407 | `e.Data.GetDataPresent(typeof(TrackInfo))`, casts to `TrackInfo` | Drop handler | **MUST CHANGE** — `TrackInfoViewModel` |
| 1410 | `_draggedItem.DiscNumber != targetItem.DiscNumber` | Cross-disc check | Works unchanged — ViewModel proxies DiscNumber |
| 1417-1424 | `_tracks.IndexOf`, `_tracks.RemoveAt`, `_tracks.Insert` | Collection reorder | Works if _tracks is `ObservableCollection<TrackInfoViewModel>` |
| 1428 | `RenumberDisc` — reads/writes `track.DiscNumber`, `track.TrackNumber`, `track.IsModified` | Batch write | **MUST CHANGE** — access via `.Track` |
| 1476-1509 | `WriteMbidToTracks` — reads `track.FilePath` | Read FilePath | **MUST CHANGE** — `.Track.FilePath` (FilePath not proxied) |

**Total touch points: ~30+ locations** in the code-behind that reference TrackInfo directly.

---

## 3. Feature-by-feature impact

### Save (`SaveChangesAsync`, line 472-625)

**Current:** Passes `_tracks` (List<TrackInfo>) directly to `_metadataService.WriteMetadata()`. Also iterates `_tracks` for MBID write, search-cache rebuild, and save verification.

**Change needed:** Unwrap ViewModels before passing to WriteMetadata:
```csharp
var trackList = _tracks.Select(t => t.Track).ToList();
_metadataService.WriteMetadata(_albumInfo, trackList);
```
Same pattern for `WriteMbidToTracks()` and the search-cache LINQ at line 587. This is the **exact same pattern ImportView uses** (see ImportView.xaml.cs lines 1009, 1271, 1411, 1491).

**Risk:** Low. Mechanical unwrap. ImportView already validates this pattern.

### Match Setlist (`MatchSetlistButton_Click`, line 1040-1126)

**Current:** Iterates `_tracks` as `TrackInfo`, reads/writes `SongName`, `IsMatched`, `IsModified`, `Segue`. Calls `ReconstructRawTitles()` and `TracksDataGrid.Items.Refresh()`.

**Change needed:** Change `foreach (var track in _tracks)` to iterate ViewModels, access properties via `.Track`:
```csharp
foreach (var vm in _tracks)
{
    var track = vm.Track;
    // ... existing logic using track.SongName, track.IsMatched, etc.
}
```
Can eliminate `ReconstructRawTitles()` call — the `Track.PropertyChanged` subscription on each ViewModel will auto-update `DisplayTitle` when `SongName`/`Segue` change on the Track. Still need `TracksDataGrid.Items.Refresh()` for row coloring (unmatched gold).

**Risk:** Low. Batch operations modify Track directly, which fires `Track.PropertyChanged`, which ViewModel listens to and updates `DisplayTitle`.

### MB Lookup (`MusicBrainzButton_Click` + `ApplyMbTrackTitles`, lines 1130-1350)

**Current:** `ApplyMbTrackTitles` iterates `_tracks` as TrackInfo, writes `localTrack.SongName`, `localTrack.IsMatched`, `localTrack.IsModified`, calls `ReconstructRawTitles()`.

**Change needed:** Same as Match Setlist — iterate ViewModels, access `.Track`. The `ShowMbidCandidateDialog` method also does `_isUpdating = true; AlbumNameTextBox.Text = ...; _isUpdating = false;` which is independent of the track model.

`LookupAllReleasesAsync` and `SearchReleasesByNameAsync` both take `List<TrackInfo>` — need unwrap at call site (line 1173: `_tracks` → `_tracks.Select(t => t.Track).ToList()`).

**Risk:** Low. Same mechanical transformation.

### Match to Song (`MatchToSong_Click`, line 993-1036)

**Current:** Takes `TrackInfo track` parameter. Reads `track.SongName`, writes `track.SongName`, `track.IsMatched`, `track.IsModified`, `track.Segue`. Calls `ReconstructRawTitles()`.

**Change needed:** Change method signature to take `TrackInfoViewModel` (or extract `.Track` at call site). The context menu build at line 884 casts `row.Item` to `TrackInfo` — change to `TrackInfoViewModel`, pass ViewModel to `MatchToSong_Click`.

Writing through ViewModel proxy for `SongName` and `Segue` would auto-fire `UpdateDisplayTitle()`, potentially eliminating the need for `ReconstructRawTitles()` + `Items.Refresh()`. However, writing `IsMatched` and `IsModified` must go through `.Track` since they're not proxied.

**Risk:** Low. The context menu cast change is the main thing.

### Drag-to-reorder (lines 1354-1467)

**Current:** All drag state uses `TrackInfo`. `_draggedItem` is `TrackInfo?`. `GetDataPresent(typeof(TrackInfo))`, casts to `TrackInfo`. Collection operations on `_tracks` (IndexOf, RemoveAt, Insert). `RenumberDisc` iterates tracks directly.

**Change needed:**
1. `_draggedItem` → `TrackInfoViewModel?`
2. All `typeof(TrackInfo)` → `typeof(TrackInfoViewModel)` in drag/drop
3. All `as TrackInfo` casts → `as TrackInfoViewModel`
4. `RenumberDisc` accesses DiscNumber/TrackNumber/IsModified via `.Track`
5. Cross-disc check at line 1410 works as-is (ViewModel proxies DiscNumber)

**Risk:** Medium. Drag-and-drop type checks are finicky — if the `GetDataPresent` type doesn't match the `DoDragDrop` data type, drops silently fail. Must be consistent across all 4 handlers.

### Track Info context menu (line 925-935)

**Current:** 
```csharp
var dialog = new TrackInfoDialog(clickedTrack, artist: _albumInfo?.Artist, album: _albumInfo?.AlbumName);
```
`TrackInfoDialog` constructor takes `TrackInfo`, not `TrackInfoViewModel`.

**Change needed:** After casting `row.Item` to `TrackInfoViewModel`, pass `.Track` to TrackInfoDialog:
```csharp
var dialog = new TrackInfoDialog(clickedTrack.Track, ...);
```

**Risk:** Negligible. One-line change.

### Date auto-lookup (`AlbumDateTextBox_LostFocus`, line 818-840)

**Current:** Operates entirely on album-level fields (TextBoxes + `_albumInfo`). Does not touch per-track state.

**Change needed:** None. This feature is album-level only.

**Risk:** None.

### Cell edits — the bug we're fixing (line 682-694)

**Current:** `CellEditEnding` casts `e.Row.Item` to `TrackInfo`, sets `track.IsModified = true`. Does NOT update display.

**Change needed:** Cast to `TrackInfoViewModel`. After commit, call `UpdateDisplayTitle()`:
```csharp
if (e.Row.Item is TrackInfoViewModel vm)
{
    vm.Track.IsModified = true;
    Dispatcher.BeginInvoke(new Action(() => vm.UpdateDisplayTitle()), DispatcherPriority.Background);
}
```
This is the **exact pattern ImportView uses** (ImportView.xaml.cs:591-598).

**Risk:** Low. This is the core bug fix. With the ViewModel in place, this is a ~5-line change.

---

## 4. ImportView pattern reference

### ViewModel construction at load time (ImportView.xaml.cs:253-257)
```csharp
_tracks.Clear();
foreach (var track in trackList)
{
    _tracks.Add(new TrackInfoViewModel(track, _albumInfo, isEditMode: false));
}
```
Track sorting happens **before** wrapping in ViewModels (lines 199-217), then ViewModels are created from the sorted list. This avoids sorting ViewModels.

### Cell edit handler (ImportView.xaml.cs:587-599)
```csharp
if (e.Row.Item is TrackInfoViewModel track)
{
    track.Track.IsModified = true;
    Dispatcher.BeginInvoke(new Action(() =>
    {
        track.UpdateDisplayTitle();
    }), DispatcherPriority.Background);
}
```
Deferred via Dispatcher so the DataGrid completes its edit cycle first.

### Save / write path (ImportView.xaml.cs:1411)
```csharp
var trackList = _tracks.Select(t => t.Track).ToList();
_metadataService.WriteMetadata(_albumInfo, trackList);
```
Unwraps ViewModels to raw TrackInfo for writing. Used in both Write and Import operations.

### Normalize (ImportView.xaml.cs:1009)
```csharp
var trackList = _tracks.Select(t => t.Track).ToList();
int matched = _normalizationService.NormalizeAll(trackList);
```
Same unwrap pattern.

### Patterns that DON'T transfer cleanly

1. **`isEditMode: false`** — ImportView uses `isEditMode: false`, which builds `"Song > (date)"` format. EditMetadataView should also use `isEditMode: false` since the diagnostic confirms we want the aggregated format. The `_isEditMode` branch that strips dates is not needed for Edit Metadata.

2. **XAML column template** — ImportView uses a `DataGridTemplateColumn` for Title with:
   - Display mode: read-only TextBlock bound to `{Binding DisplayTitle}`
   - Edit mode: editable TextBox bound to `{Binding SongName, UpdateSourceTrigger=PropertyChanged}`
   
   EditMetadataView currently uses a simple `DataGridTextColumn` bound to `RawTitle`. Must convert to a template column matching ImportView's pattern.

3. **`RawTitle` usage** — EditMetadataView has `ReconstructRawTitles()` which maintains `RawTitle` as the display+storage field. With ViewModel adoption, `DisplayTitle` handles display, and `RawTitle` becomes irrelevant for the grid. However, `RawTitle` is still used by `MetadataService.WriteMetadata` for FLAC tags. Must confirm: does WriteMetadata use `RawTitle` or `SongName` for the TITLE tag? If it uses `RawTitle`, we still need `ReconstructRawTitles()` before save (not for display, but for data integrity).

4. **`ObservableCollection` vs `List`** — ImportView uses `ObservableCollection<TrackInfoViewModel>` which auto-notifies the DataGrid of Add/Remove. EditMetadataView uses `List<TrackInfo>` and calls `Items.Refresh()` manually. Switching to `ObservableCollection` is cleaner but means replacing `_tracks = _tracks.OrderBy(...)` patterns (which create new lists) with in-place operations or re-population.

5. **Row coloring for unmatched tracks** — ImportView uses `LoadingRow` event with `TrackInfoViewModel` casts (line 602-608). EditMetadataView doesn't have `LoadingRow` — it doesn't color unmatched rows. If adding this, follow ImportView's pattern.

---

## 5. New ViewModel state needed for Edit Metadata

### Already exists and sufficient
- `DisplayTitle` — the reactive property we need
- `InheritedDate` — useful for date display
- All editable proxy properties (SongName, Segue, TrackDate, DiscNumber, TrackNumber)
- `Track.PropertyChanged` subscription — catches batch modifications

### Not currently on ViewModel but may be needed

| Need | Exists? | Assessment |
|------|---------|------------|
| Dirty/pending-save indicator | No. `IsModified` exists only on `TrackInfo`. | **Not needed on ViewModel.** EditMetadataView tracks `_hasUnsavedChanges` at the view level and `track.IsModified` at the model level. No ViewModel-level dirty flag needed. |
| Original-vs-current tracking | No. | **Not needed.** EditMetadataView doesn't show "modified" markers per-cell. |
| `RawTitle` proxy | No. | **Possibly needed** if `MetadataService.WriteMetadata` writes from `RawTitle`. If so, either add a proxy or call `ReconstructRawTitles()` (on `.Track`) before save. |
| `IsMatched` proxy | No. | **Not strictly needed.** Accessed as `.Track.IsMatched` in filtering code. Could add for consistency but not required. |
| `FilePath` proxy | No. | **Not needed.** Only accessed during save (MBID write). Use `.Track.FilePath`. |

### Summary
**Zero new ViewModel properties are strictly required.** The existing `TrackInfoViewModel` is sufficient. The only question is whether to add a `RawTitle` proxy for symmetry, but it's simpler to keep calling `ReconstructRawTitles()` on the underlying tracks before save.

---

## 6. Risk surface

### Save
- **What works:** Verified working in current codebase (writes FLAC tags correctly)
- **What could break:** If unwrap (`_tracks.Select(t => t.Track)`) misses a track, or if `ReconstructRawTitles()` isn't called before save (RawTitle would be stale after SongName edits)
- **Retest:** Save after manual edit, save after Normalize, save after Match Setlist, save after MB Lookup, verify FLAC tags contain correct TITLE value

### Match Setlist
- **What works:** Matches tracks to setlist, applies segues, supports unmatched right-click
- **What could break:** If batch writes to `Track.SongName` don't trigger ViewModel updates (they should — constructor subscribes to `Track.PropertyChanged`)
- **Retest:** Run Match Setlist, verify titles update in grid, verify segue checkboxes, verify unmatched gold coloring (if added), verify Match to Song context menu

### MB Lookup
- **What works:** Fingerprint + name search, candidate dialog, apply track titles
- **What could break:** `LookupAllReleasesAsync` getting empty list if unwrap fails
- **Retest:** Full MB lookup flow, verify track titles update, verify album fields update

### Match to Song
- **What works:** Context menu → dialog → apply canonical title + alias
- **What could break:** Context menu cast from `DataGridRow.Item` to wrong type → silently fails (no match item in menu)
- **Retest:** After Match Setlist, right-click unmatched track, verify "Match to Song" appears, complete match, verify title updates

### Drag-to-reorder
- **What works:** Drag tracks within same disc, auto-renumber
- **What could break:** `GetDataPresent(typeof(TrackInfo))` returns false if DoDragDrop used `TrackInfoViewModel` → drops silently fail. This is the **highest-risk area** — silent failures are hard to debug.
- **Retest:** Drag a track up/down within same disc, verify it moves, verify track numbers update. Attempt cross-disc drag, verify it's blocked.

### Track Info context menu
- **What works:** Opens dialog with file metadata
- **What could break:** Only if cast fails → dialog doesn't appear
- **Retest:** Right-click → Track Info, verify dialog opens with correct data

### Cell edits (the fix)
- **What works currently:** Editing RawTitle in cell changes the value but display doesn't aggregate with date
- **What should work after migration:** Editing SongName in cell triggers UpdateDisplayTitle(), grid shows `"Song > (date)"` immediately
- **Retest:** Edit a song name in the grid, verify displayed title updates with date suffix. Edit date column, verify title updates. Toggle segue checkbox, verify ">" appears/disappears in title.

### Normalize
- **What works:** Cleans titles, fuzzy matches, shows unmatched dialog
- **What could break:** `NormalizeAll` needs `List<TrackInfo>`, unmatched filtering needs `.Track.IsMatched`
- **Retest:** Click Normalize, verify titles update, verify unmatched dialog appears, verify corrections apply

---

## 7. Phasing recommendation

### Recommended: Single-phase migration

**Rationale:**

1. **Scope is manageable.** ~30 touch points, but they fall into just 4 mechanical patterns:
   - Change `_tracks` type and populate with ViewModels (~5 lines)
   - Change casts from `TrackInfo` to `TrackInfoViewModel` (~12 occurrences, all mechanical)
   - Add `.Track.` prefix for non-proxied property access (~15 occurrences)
   - Unwrap for service calls with `_tracks.Select(t => t.Track).ToList()` (~4 occurrences)

2. **ImportView provides a complete reference implementation.** Every pattern needed in EditMetadataView already exists in ImportView. There's no design ambiguity.

3. **Two-phase risks incoherence.** A half-migrated state where `_tracks` is `ObservableCollection<TrackInfoViewModel>` but some code paths still cast to `TrackInfo` would be more error-prone than doing it all at once.

4. **The XAML change is atomic.** The Title column must switch from `{Binding RawTitle}` to `DisplayTitle`/`SongName` template column — this can't meaningfully be split across phases.

5. **`ReconstructRawTitles()` can stay** for now. It operates on `.Track` objects and is still needed to set `RawTitle` before save (for MetadataService). It just stops being needed for display updates. We don't need to remove it in the migration — removing it is a separate cleanup if/when save is refactored to use `GetFinalMetadataTitle()` instead.

### Migration checklist (single commit)

1. Change `_tracks` from `List<TrackInfo>` to `ObservableCollection<TrackInfoViewModel>`
2. Change `_draggedItem` from `TrackInfo?` to `TrackInfoViewModel?`
3. In `LoadData()`: sort TrackInfo list first, then wrap each in `new TrackInfoViewModel(track, _albumInfo, isEditMode: false)`
4. In XAML: convert Title column from `DataGridTextColumn` to `DataGridTemplateColumn` with DisplayTitle (display) / SongName (edit)
5. Update `CellEditEnding` to cast to ViewModel and call `UpdateDisplayTitle()`
6. Update all `e.Row.Item as TrackInfo` / `is TrackInfo` to `TrackInfoViewModel`
7. Update drag-drop: type checks, casts, `_draggedItem`
8. Update context menu: cast to ViewModel, pass `.Track` to TrackInfoDialog
9. Update `MatchToSong_Click` signature
10. Add `.Track.` prefix where needed for non-proxied properties
11. Add unwrap calls before service method calls (WriteMetadata, NormalizeAll, LookupAllReleasesAsync)
12. Keep `ReconstructRawTitles()` — change to iterate `.Track` objects for save preparation
13. Remove manual `Track_PropertyChanged` subscription in LoadData (ViewModel handles this)

**Estimated size:** ~100-150 lines changed across 2 files (XAML + code-behind). No new files. No changes to TrackInfoViewModel.cs if using `isEditMode: false`.

---

## 8. Open questions

1. **Does `MetadataService.WriteMetadata` use `RawTitle` or `SongName` for the FLAC TITLE tag?** If it uses `RawTitle`, we must call `ReconstructRawTitles()` before save to ensure `RawTitle` reflects the current `SongName + Segue + TrackDate`. If it uses `GetFinalMetadataTitle()`, we can skip `ReconstructRawTitles()` entirely. Needs inspection of `MetadataService.WriteMetadata`.

2. **Should `isEditMode` be `false` for EditMetadataView's ViewModels?** The diagnostic says yes (we want aggregated display). But the `_isEditMode` flag was explicitly added for edit scenarios. Setting it to `false` means `InheritedDate` computation also changes (includes album-date fallback). Confirm this is desired behavior for Edit Metadata.

3. **Should unmatched row coloring (gold text) be added to EditMetadataView?** ImportView has it (LoadingRow handler, line 602). EditMetadataView doesn't. This is not part of the migration itself but is a visual feature that would come naturally with the ViewModel pattern. Keep it out of scope for the migration commit.

4. **Should `ReconstructRawTitles()` be called automatically by the CellEditEnding handler?** Even with ViewModel, the save path may rely on `RawTitle`. If the user edits SongName in the grid, `RawTitle` won't update until Normalize or Match Setlist runs. Either: (a) call `ReconstructRawTitles()` in save just before WriteMetadata, or (b) eliminate `RawTitle` dependency in WriteMetadata. Option (a) is safer for the migration; (b) is a separate refactor.
