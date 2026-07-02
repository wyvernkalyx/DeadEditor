# Reference Side-Panel (Setlist + Info File) — Design Spec

_Status: approved design (Gregg-approved; grounded by a completed read-only Phase A). Document-before-implement per the handbook. Implementation follows in the five slices of §10. All file:line citations below are Phase-A-verified against HEAD `aa9b5da`; they are cited, not re-derived._

## 1. Problem

The canonical setlist for a date and the folder's info `.txt` are both reachable today only through transient or modal surfaces, neither usable beside the track grid while editing:

- The setlist exists in memory only as `_lastSetlistSongs`, built **solely inside `MatchSetlistButton_Click`** (`ImportView.xaml.cs:1190-1208`, Edit twin around `EditMetadataView.xaml.cs:1669`). There is no way to *see* the setlist before running Match Setlist, and once run it is consumed for matching, not displayed as reference.
- The info `.txt` renders only in `ReadPanelHost`, a full-screen scrim overlay (`ReadPanelHost.xaml`, mounted once in the shell at ZIndex 50), so it cannot sit open next to the grid, and it exists on the Import surface only.

The user wants both, docked beside the grid on both edit surfaces, plus the ability to assign a setlist entry to a track directly from the panel (write-identical to today's right-click Match-to-Song).

## 2. Goals / non-goals

Goals:
- One shared collapsible `SetlistReferencePanel` UserControl, docked between the track DataGrid and the right sidebar on BOTH `ImportView` and `EditMetadataView`.
- **Setlist tab**: canonical setlist for the entered date (position, song, set label, segue), populated on date entry.
- **Info File tab**: the folder's info `.txt`, read-only + copyable, no scrim.
- Click-to-assign a setlist entry to the selected track, write-identical to the existing manual match.

Non-goals (this arc):
- **Pre-match dimming.** Matched-entry dimming happens only after a Match Setlist run; there is no pre-match track→setlist-position mapping and inventing one is out of scope (§6).
- **Positional bulk-apply.** Assignment is always one entry → one explicitly-selected track.
- **Fixing the segue-clear question, the `MaybeUnverifyAlbumEdit` gap, or the "first .txt wins" fragility.** Each is inherited from the existing manual path deliberately and stays banked (§7, §8).
- **A GridSplitter / user-resizable panel.** Fixed width in v1 (§3).
- **Combined/alias read-side matching.** Untouched; the panel shows the plain flattened setlist (§5).

## 3. Placement & host wiring

The panel inserts as a **new middle column** between the track-DataGrid column and the right-sidebar column, inside each view's `*`-sized main content grid. The seam is the same structural position on both views, but the surrounding XAML differs, so **host wiring is per-view; only the panel control is shared** (explicit non-goal: not copy-paste-identical host XAML).

- **ImportView**: the nested Grid at `ImportView.xaml:244` — col0 `*` (DataGrid wrapped in a Border, `:251-252`), col1 `Auto` sized by a 450px sidebar Border (`:500-501`). Insert a new fixed-width column between them.
- **EditMetadataView**: the nested Grid at `EditMetadataView.xaml:189` — col0 `*` (a Border wrapping an inner 2-row validation-banner + grid, `:196-201`), col1 fixed `Width="200"` StackPanel (`:190-193`, `:408`). Insert a new fixed-width column between them.

Collapse behavior:
- Expanded width **~320px**; collapses to **0**.
- Mechanism: an imperative `bool` toggle + chevron glyph swap, following the PlaylistPanel idiom (`PlaylistPanel.xaml.cs:184-199`) **adapted to horizontal column-Width collapse** (PlaylistPanel collapses a row `Height`; this collapses a column `Width`). No data binding, no animation, no DependencyProperty — parity with the existing idiom.
- **No GridSplitter in v1** (banked as polish; §11). The existing views have no GridSplitter, so a fixed-width collapsible column is purely additive.

The current 10px gutter (`Margin="0,0,10,0"` on the DataGrid Border on both views, `ImportView.xaml:251` / `EditMetadataView.xaml:196`) is preserved as the seam between grid and panel.

## 4. Shared control API

`SetlistReferencePanel` is a UserControl hosting a 2-tab `TabControl` (Setlist, Info File). It **never writes `TrackInfo`, never touches dirty state, never reads a service singleton** — the hosting view owns all data-fetching and all writes. The control is a pure display + event surface.

API surface:
- `SetSetlist(IReadOnlyList<SetlistEntryVm> entries, ISet<int>? claimedPositions)` — populate/refresh the Setlist tab. `claimedPositions` is `null` pre-match (nothing dimmed); a set post-match (dim members). Calling again with an updated set is the re-dim refresh path (used after a match run or a manual/panel assign).
- `SetInfo(string fileName, string? content)` — populate the Info File tab (`content` null/empty → an empty-state message).
- `event Action<int> AssignRequested` — raised with the flattened 0-based `Position` of the setlist entry the user chose to assign to the currently-selected track. The hosting view handles the write (§7). The panel does not know which track is selected.

`SetlistEntryVm` shape (one per flattened setlist entry):

| Field | Type | Meaning |
|---|---|---|
| `Name` | string | Setlist display name (as stored) |
| `Canonical` | string | Canonical song title (via `GetOfficialTitle`) |
| `Position` | int | Flattened, 0-based position across all sets (the claim axis) |
| `Segue` | bool | Segue-out marker for this entry |
| `SetLabel` | string | Human set label, e.g. "Set 2, #3" (folded in at projection time; §5) |

## 5. Setlist projection & population

### 5.1 The projection helper (pure, extracted)
The flatten / canonicalize / position logic currently inline in `MatchSetlistButton_Click` (`ImportView.xaml.cs:1190-1208`) is extracted into a pure, WPF-free, unit-testable helper `BuildSetlistProjection` producing `IReadOnlyList<SetlistEntryVm>` (the §4 shape). It:
- flattens `ShowLookupService.GetSetlist(date)`'s `List<SetInfo>` across all sets into a global 0-based `Position`,
- runs each name through the caller-supplied canonical resolver (`GetOfficialTitle`) for `Canonical`,
- carries `Segue` verbatim,
- **folds in `SetLabel`**, which is today re-derived per position via `GetDiscTrack` inside `BuildUnmatchedSetlistSongList` (`ImportView.xaml.cs:831-861`; `EditMetadataView.xaml.cs:1566-1596`) — computed once during projection instead of re-derived at render.

After this extraction the matcher and the panel share **one** projection. `MatchSetlistButton_Click`'s inline flatten is retrofitted onto the helper in the same slice that introduces it, so `_lastSetlistSongs` and the panel derive from a single source (no second copy of the projection).

### 5.2 Population trigger
Populate on the date field's **`LostFocus`** with a well-formed `yyyy-MM-dd` (`AlbumDateTextBox_LostFocus`, `ImportView.xaml.cs:498-517` / `EditMetadataView.xaml.cs:1316-1338`), **not** on `TextChanged` (which fires per keystroke and does not touch the setlist). This rides the `GetSetlist(date)` call `UpdateMatchSetlistButton` already makes (`ImportView.xaml.cs:1131-1149`; `EditMetadataView.xaml.cs:1340-1354`) — no new lookup on the warm path; the result, previously discarded except for the enable-flag/tooltip, now also feeds `SetSetlist(...)`. A malformed or empty date clears the tab.

### 5.3 Cold-load guard (resolved decision)
On a fresh Import view the Album-Info date field is editable **before** any folder Read, so `LostFocus → UpdateMatchSetlistButton → GetSetlist` can trigger the ~17s / 2,293-file cold `ConcertLookupService` load (`ConcertLookupService.cs:45-46`, `237-263`; residual noted at follow-ups.md:300). On the normal flow the cache is already warm — `ConcertDataGate.EnsureLoadedAsync()` is awaited before the date is editable on both surfaces (Import after folder Read, `ImportView.xaml.cs:310-317`; Edit in `_Loaded`, `EditMetadataView.xaml.cs:129-142`).

**Decision:** guard the panel-population path with `ConcertDataGate.EnsureLoadedAsync` (`ConcertDataGate.cs:29-53`), awaited **asynchronously** — the population routine is `async`, awaits the gate (which short-circuits instantly when warm and, when cold, forces the load off the UI thread behind the existing shared status overlay), then calls `SetSetlist(...)`. The UI thread stays responsive; the cold load surfaces the **existing** overlay idiom rather than a bespoke panel spinner (this keeps one cold-load treatment app-wide). This deliberately reverses follow-ups.md:300's "don't await LostFocus," scoped narrowly to the new panel-population path (see §11 and report note).

## 6. Claimed / dimmed lifecycle

- **Pre-match**: every entry un-dimmed. The panel is pure reference until a Match Setlist run exists.
- **Post-match**: dimming mirrors `_lastClaimedPositions` **exactly** — the `HashSet<int>` assigned from `result.ClaimedPositions` after a match (`ImportView.xaml.cs:64,1251`; `EditMetadataView.xaml.cs:61,1735`). Members render dimmed (the same "claimed → skip/dim" semantics `BuildUnmatchedSetlistSongList` uses at `ImportView.xaml.cs:842`). The host passes this set into `SetSetlist`.
- **On a manual or panel assign**: the position is added to `_lastClaimedPositions` (the existing manual increment, `ImportView.xaml.cs:900` / `EditMetadataView.xaml.cs:1627`) and the host re-calls `SetSetlist(entries, _lastClaimedPositions)` to re-dim.
- **Explicit non-goal**: no invented pre-match claim state. Per-track `TrackInfo.IsMatched` (`TrackInfo.cs:157-169`, set at normalize `NormalizationService.cs:247,252`) says "this track resolved to *a* canonical song," not "*this setlist position* is claimed," so it cannot drive setlist-row dimming and is not used for it.

## 7. Assignment write contract (load-bearing)

Today the manual right-click Match-to-Song writes are **duplicated inline** in each code-behind (`ImportView.xaml.cs:870,893-908`; `EditMetadataView.xaml.cs:1598,1618-1642`) with no shared entry point; `MatchToSongDialog` returns only the selected index and writes nothing.

### 7.1 Shared apply core (resolved decision)
Extract a shared apply core, **pure over `TrackInfo`**, that performs exactly the current manual write set:
- `SongName = entry.Canonical`
- `IsMatched = true`
- `IsModified = true`
- `if (entry.Segue) Segue = true`

Per-surface side effects attach in the hosting view **after** the core call, mirroring today exactly:
- **Both surfaces**: `_lastClaimedPositions.Add(position)`, `AddAlias(entry.Canonical, oldSongName.Trim())`, status text, `TracksDataGrid.Items.Refresh()` (`ImportView.xaml.cs:900,908`; `EditMetadataView.xaml.cs:1627,1633`).
- **Edit only** (no Import counterpart): `ReconstructRawTitles()`, `_hasUnsavedChanges = true`, `RefreshValidation()`, `RecomputeUnmatchedHighlights()` (`EditMetadataView.xaml.cs:1636-1642`).

### 7.2 Retrofit the existing handlers (resolved decision)
The two existing right-click Match-to-Song handlers are retrofitted onto this same core **in the same slice** that introduces it (slice 4). The panel's click/DnD assign path calls the identical core + the identical per-surface hooks. There must be **no third inline copy** of the write set.

### 7.3 Segue is set-true-only, never cleared (resolved decision)
The core writes `Segue = true` only when the entry carries a segue; it never clears a segue — **write-identical to the current manual path**. This is deliberately different from the batch `SetlistMatcher.Apply`, which writes `Segue = NewSegue` (clear-capable, `SetlistMatcher.cs:248-281`). The "should a manual assign be able to clear a wrong segue" question is banked, not solved here.

### 7.4 No unverify on assign (resolved decision)
Edit-side assignment does **not** call `MaybeUnverifyAlbumEdit()` — parity with the existing manual handlers, which also don't (known gap, follow-ups.md:318). This feature neither fixes nor widens that gap; it stays banked as its own concern.

## 8. Info tab

- Re-host the `ReadPanelHost` text recipe — a read-only, selectable, `IsReadOnlyCaretVisible`, `TextWrapping="NoWrap"`, Consolas `TextBox` (`ReadPanelHost.xaml:54-64`) — inside the second tab. The scrim **host** itself is not reusable (it is a full-shell overlay with Close/Esc modal semantics); only the TextBox is.
- **Import**: feed `_albumInfo.InfoFileContent`, already populated by `MetadataService.ReadAlbumInfo` (`MetadataService.cs:339-340`). The existing View Info button (`ViewInfoButton_Click`, `ImportView.xaml.cs:1484-1495`) **retargets** from opening the scrim to expanding the panel + selecting the Info tab.
- **Edit**: net-new disk read of the folder's first `.txt` at `_show.FolderPath` — `InfoFileContent` is `null` on this surface today (`EditMetadataView` builds `_albumInfo` by hand and never reads the `.txt`, `EditMetadataView.xaml.cs:176-201`). The tab is Edit's **only** entry point; no new button is added.
- **Inherited fragility (banked, not fixed)**: the Edit read reproduces Import's "first `*.txt` wins" behavior. Inherited deliberately for parity; a multi-`.txt` disambiguation is out of scope.

## 9. Drag-and-drop assignment

- Drop-side machinery already exists on **both** grids (drag-to-reorder), so drop-onto-a-track-row is established, not greenfield: `ImportView.xaml.cs:923-998` (`DoDragDrop` at `:951`, `Row_DragOver`/`Row_Drop` type-gated at `:959,:979`); `EditMetadataView.xaml:242,286-292` (`AllowDrop="True"` + row `EventSetter`s).
- The setlist drag carries a **distinct payload type** (the `SetlistEntryVm`, not `TrackInfoViewModel`), so its `DragOver`/`Drop` branch never cross-triggers the reorder branch. `GetDataPresent` type-gating must match the payload type exactly — a mismatch is a **silent-fail** mode (documented in edit-metadata-viewmodel-migration-inspection-2026-04-25.md); keep the payloads distinct.
- Net-new work is only the **drag source** on setlist rows (a ListBox/ItemsControl item calling `DoDragDrop`). The drop handler calls the §7 core + the same per-surface hooks as click-assign.
- Ships **after** click-assign (slice 5), so assignment does not depend on DnD mechanics.

## 10. Slice plan

One concern per commit; WPF manual gate on every UI-visible slice; pure helpers unit-tested.

1. **Read-only Setlist tab (Import).** Extract pure `BuildSetlistProjection` (§5.1) + retrofit `MatchSetlistButton_Click`'s inline flatten onto it; build `SetlistReferencePanel` with the read-only Setlist tab; host on Import; populate on date entry (§5.2) with the cold-load guard (§5.3); post-match dimming mirroring `_lastClaimedPositions` (§6). Unit tests on the projection. WPF gate.
2. **Host the same control on Edit.** Nothing else — per-view host wiring only (§3). WPF gate.
3. **Info tab.** TextBox recipe (§8); Import content feed + View Info retarget; Edit disk read. WPF gate.
4. **Click-to-assign.** Extract the shared apply core (§7.1), retrofit BOTH existing Match-to-Song handlers onto it (§7.2), wire select-track → double-click-entry → `AssignRequested` → core + per-surface hooks. Unit tests on the core. WPF gate.
5. **DnD assign.** Drag source on setlist rows + payload-typed drop branch calling the slice-4 core (§9). WPF gate.

## 11. Decision record

Resolved decisions carried into implementation, each with its one-line rationale:

1. **Cold-load guard = async `ConcertDataGate.EnsureLoadedAsync` on the panel-population path** — keeps the UI responsive on the fresh-Import pre-Read cold hit (follow-ups.md:300) and reuses the one existing app-wide cold-load overlay; scoped reversal of "don't await LostFocus," limited to panel population (§5.3).
2. **Shared apply core, pure over `TrackInfo`** — eliminates the inline write-set duplication so click, DnD, and right-click all write identically; per-surface side effects stay in the host where dirty/verify/validation state lives (§7.1).
3. **Retrofit both existing Match-to-Song handlers onto the core in slice 4** — prevents a third inline copy of the write set and keeps all four assignment entry points convergent (§7.2).
4. **Segue set-true-only, never cleared** — write-identical to today's manual path; the segue-clear question is a separate, banked concern, not silently changed by this feature (§7.3).
5. **No `MaybeUnverifyAlbumEdit` on assign** — parity with the existing manual handlers (follow-ups.md:318); the feature neither fixes nor widens that banked gap (§7.4).

## 12. Out of scope / banked

- GridSplitter / user-resizable panel width (§3).
- Manual-assign segue-clear semantics (§7.3).
- `MaybeUnverifyAlbumEdit`-on-manual-assign (§7.4, follow-ups.md:318).
- Multi-`.txt` info-file disambiguation (§8).
- Combined/alias read-side matching — the panel shows the plain flattened setlist and is orthogonal to the aliasSetlists read-side arc (follow-ups.md "Combines: the matcher never reads aliasSetlists back").
