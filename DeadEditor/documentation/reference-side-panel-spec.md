# Reference Side-Panel (Setlist + Info File) — Design Spec

_Status: approved design, revised after lived use. Slice 1 (read-only Setlist tab, Import) is **implemented** (uncommitted at time of this amendment; manual WPF gate in progress). This revision **supersedes the original §8 (in-panel Info tab)** with an external info-file open (§8) and **adds the setlist-editor deep-link** (§9), a pivot driven by the lived workflow of comparing the info file against the setlist editor. Document-before-implement per the handbook; implementation follows in the six slices of §11. All file:line citations are Phase-A-verified (slice-1 citations against HEAD `aa9b5da`; the amendment citations against the working tree at amendment time); they are cited, not re-derived. A later amendment adds §7.5 (panel-assignment availability rules) and §7.6 (slice-5 diagnostic work items); its citations are re-verified against HEAD `dc110ad`._

## 1. Problem

The canonical setlist for a date and the folder's info `.txt` are both reachable today only through transient or modal surfaces, neither usable beside the track grid while editing:

- The setlist exists in memory only as `_lastSetlistSongs`, built **solely inside `MatchSetlistButton_Click`** (`ImportView.xaml.cs:1190-1208`, Edit twin around `EditMetadataView.xaml.cs:1669`). There is no way to *see* the setlist before running Match Setlist, and once run it is consumed for matching, not displayed as reference.
- The info `.txt` renders only in `ReadPanelHost`, a full-screen scrim overlay (`ReadPanelHost.xaml`, mounted once in the shell at ZIndex 50), so it cannot sit open next to the grid, and it exists on the Import surface only.

The user wants both, docked beside the grid on both edit surfaces, plus the ability to assign a setlist entry to a track directly from the panel (write-identical to today's right-click Match-to-Song).

## 2. Goals / non-goals

Goals:
- One shared collapsible `SetlistReferencePanel` UserControl, docked between the track DataGrid and the right sidebar on BOTH `ImportView` and `EditMetadataView`.
- **Setlist view**: canonical setlist for the entered date (position, song, set label, segue), populated on date entry.
- **External info-file open**: open the folder's info `.txt` in the OS default editor (replaces the dropped in-panel Info tab; §8), so it can sit in its own window beside the setlist editor.
- **Setlist-editor deep-link**: jump from the panel to the concert's setlist editor to fix source-data gaps, returning to a fresh panel via re-Read (§9).
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
- **No GridSplitter in v1** (banked as polish; §13). The existing views have no GridSplitter, so a fixed-width collapsible column is purely additive.

The current 10px gutter (`Margin="0,0,10,0"` on the DataGrid Border on both views, `ImportView.xaml:251` / `EditMetadataView.xaml:196`) is preserved as the seam between grid and panel.

## 4. Shared control API

`SetlistReferencePanel` is a UserControl hosting the canonical setlist under a single **Setlist** panel header (the in-panel Info File tab is dropped, §8; the former 2-tab `TabControl` collapses cosmetically). It **never writes `TrackInfo`, never touches dirty state, never reads a service singleton** — the hosting view owns all data-fetching, all writes, and all navigation. The control is a pure display + event surface.

API surface:
- `SetSetlist(IReadOnlyList<SetlistEntryVm> entries, ISet<int>? claimedPositions)` — populate/refresh the Setlist view. `claimedPositions` is `null` pre-match (nothing dimmed); a set post-match (dim members). Calling again with an updated set is the re-dim refresh path (used after a match run or a manual/panel assign).
- `event Action<int> AssignRequested` — raised with the flattened 0-based `Position` of the setlist entry the user chose to assign to the currently-selected track. The hosting view handles the write (§7). The panel does not know which track is selected.
- `event Action EditSetlistRequested` — raised when the user activates the header's **"Edit setlist ↗"** affordance. The host performs the concert-detail deep-link (§9); the panel holds no date and does no navigation.

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

**Decision:** guard the panel-population path with `ConcertDataGate.EnsureLoadedAsync` (`ConcertDataGate.cs:29-53`), awaited **asynchronously** — the population routine is `async`, awaits the gate (which short-circuits instantly when warm and, when cold, forces the load off the UI thread behind the existing shared status overlay), then calls `SetSetlist(...)`. The UI thread stays responsive; the cold load surfaces the **existing** overlay idiom rather than a bespoke panel spinner (this keeps one cold-load treatment app-wide). This deliberately reverses follow-ups.md:300's "don't await LostFocus," scoped narrowly to the new panel-population path (see §12 and report note).

## 6. Claimed / dimmed lifecycle

- **Pre-match**: every entry un-dimmed. The panel is pure reference until a Match Setlist run exists.
- **Post-match**: dimming mirrors `_lastClaimedPositions` **exactly** — the `HashSet<int>` assigned from `result.ClaimedPositions` after a match (`ImportView.xaml.cs:64,1251`; `EditMetadataView.xaml.cs:61,1735`). Members render dimmed (the same "claimed → skip/dim" semantics `BuildUnmatchedSetlistSongList` uses at `ImportView.xaml.cs:842`). The host passes this set into `SetSetlist`.
- **On a manual or panel assign**: the position is added to `_lastClaimedPositions` (the existing manual increment, `ImportView.xaml.cs:900` / `EditMetadataView.xaml.cs:1627`) and the host re-calls `SetSetlist(entries, _lastClaimedPositions)` to re-dim.
- **Explicit non-goal**: no invented pre-match claim state. Per-track `TrackInfo.IsMatched` (`TrackInfo.cs:157-169`, set at normalize `NormalizationService.cs:247,252`) says "this track resolved to *a* canonical song," not "*this setlist position* is claimed," so it cannot drive setlist-row dimming and is not used for it.

## 7. Assignment write contract (load-bearing)

Today the manual right-click Match-to-Song writes are **duplicated inline** in each code-behind (`ImportView.xaml.cs:893-949`, write set at `916-920`; `EditMetadataView.xaml.cs:1699-1752`, write set at `1719-1725`) with no shared entry point; `MatchToSongDialog` returns only the selected index and writes nothing.

### 7.1 Shared apply core (resolved decision)
Extract a shared apply core, **pure over `TrackInfo`** (implemented as `Helpers/ManualMatchApply.Apply`, returning a `ManualMatchResult`), that performs exactly the current manual write set plus the pure claimed-position add:
- `SongName = entry.Canonical`
- `IsMatched = true`
- `IsModified = true`
- `if (entry.Segue) Segue = true`
- `claimedPositions.Add(position)` — a plain `ISet<int>` mutation (no view/service touch), folded into the core so the whole "this track now claims this position" transition is one unit-tested call

Per-surface side effects attach in the hosting view **after** the core call, mirroring today exactly:
- **Both surfaces**: panel re-dim via `SetlistPanel.SetSetlist(_lastSetlistProjection, _lastClaimedPositions)`, `AddAlias(entry.Canonical, oldSongName.Trim())` (driven off `ManualMatchResult.PreviousSongName`), status text, `TracksDataGrid.Items.Refresh()` (Import: re-dim `ImportView.xaml.cs:941`, alias `:948`, status `:958`, refresh `:960`; Edit: re-dim `EditMetadataView.xaml.cs:1727`, alias `:1733`, refresh `:1737`, status `:1745`).
- **Edit only** (no Import counterpart): `ReconstructRawTitles()`, `_hasUnsavedChanges = true`, `RefreshValidation()`, `RecomputeUnmatchedHighlights()` (`EditMetadataView.xaml.cs:1736-1742`).

### 7.2 Retrofit the existing handlers (resolved decision)
The two existing right-click Match-to-Song handlers are retrofitted onto this same core **in the same slice** that introduces it (slice 5). The panel's click/DnD assign path calls the identical core + the identical per-surface hooks. There must be **no third inline copy** of the write set.

### 7.3 Segue is set-true-only, never cleared (resolved decision)
The core writes `Segue = true` only when the entry carries a segue; it never clears a segue — **write-identical to the current manual path**. This is deliberately different from the batch `SetlistMatcher.Apply`, which writes `Segue = NewSegue` (clear-capable, `SetlistMatcher.cs:248-281`). The "should a manual assign be able to clear a wrong segue" question is banked, not solved here.

### 7.4 No unverify on assign (resolved decision)
Edit-side assignment does **not** call `MaybeUnverifyAlbumEdit()` — parity with the existing manual handlers, which also don't (known gap, follow-ups.md:318). This feature neither fixes nor widens that gap; it stays banked as its own concern.

### 7.5 Availability rules — panel assignment vs the right-click menu (resolved decision)

Panel-driven assignment and the existing right-click **Match to Song** menu have **different availability gates**, deliberately:

- **Panel assignment gate = the panel is populated.** The panel's click/DnD assign is offered whenever the setlist projection exists (`_lastSetlistProjection != null` — Import `ImportView.xaml.cs:72,465,1366`; Edit `EditMetadataView.xaml.cs:65,1448,1840`). It is **NOT** gated on a completed Match Setlist run. Motivating case: **position-only imports where auto-match resolves nothing** — no run ever completes with claims, yet the setlist is populated on date entry (§5.2), so the panel is the *only* path to assign. A track's `IsMatched` state does not gate panel assignment (§7.5.1).

- **Right-click menu gate = UNCHANGED.** The existing menu keeps its current, stricter gate: a completed **applied** Match Setlist run **and** the clicked track's `IsMatched != true` **and** at least one unclaimed setlist song. Ground truth: Import `ImportView.xaml.cs:827-844` (condition at `828-829`: `_lastSetlistSongs != null && _lastClaimedPositions != null && _lastMatchDate != null && vm.Track.IsMatched != true`, plus the `BuildUnmatchedSetlistSongList().Count > 0` check at `832-833`); Edit `EditMetadataView.xaml.cs:1643-1659` (condition at `1644-1645`: `_matchSetlistHasRun && clickedTrack.IsMatched != true && _lastSetlistSongs != null && _lastClaimedPositions != null`, plus the unmatched-count check at `1647-1648`). Slice 5 does not change this gate; it only retrofits the handler body onto the shared core (§7.2).

#### 7.5.1 Assignment onto an already-resolved track is ALLOWED
Panel assignment works on a track **regardless of its current `IsMatched` state** — including a track auto-match already resolved. Rationale: a panel assign is an **explicit per-track user action**, and explicit action is explicit intent; re-assignment is the **correction path** when auto-match resolved a track to the wrong song. (This is exactly what the right-click menu cannot do — its `IsMatched != true` gate structurally excludes resolved tracks, §7.5.2.)

**Consequence the host must implement:** re-assigning a track that already claimed a setlist position must **free the previously claimed position** (remove it from `_lastClaimedPositions` so the old entry un-dims) and **claim the new one**, then re-call `SetSetlist(projection, _lastClaimedPositions)` to refresh dimming. This is a **new** host hook: today's manual path only ever *adds* to the claimed set (Import `:923`, Edit `:1728`) and never frees, because its gate guarantees the clicked track was unclaimed.

**Implementation note (flag).** `_lastClaimedPositions` is position-indexed; `TrackInfo` carries **no back-reference** to the setlist position it claimed, so "free the previously claimed position" has no O(1) lookup today. Slice 5 must introduce a track→claimed-position association (a side map, or recompute the freed position from the track's prior `SongName`/segue) to un-dim correctly. Absent this, a re-assign would leave the stale old position dimmed. Called out as slice-5 design work, not solved here.

#### 7.5.2 Structural consequence — the menu can never serve two track classes
Because the right-click gate requires `IsMatched != true`, the menu **can never appear** for:
1. **recognized-but-unclaimed tracks** — a track whose title normalized to a canonical song (so `IsMatched == true`) but whose setlist *position* the matcher did not claim (e.g. a **second "Ripple"** when the setlist lists one, or any repeated song); and
2. **extra / non-setlist tracks** that still resolved to a canonical title (tuning, banter, or a song not in this date's setlist).

Both classes are unreachable from the menu by construction. **Panel assignment is the structural fix for both** — its populated-panel gate (§7.5) plus its allow-on-resolved rule (§7.5.1) let the user assign these tracks directly, which the menu path cannot.

### 7.6 Slice-5 work items from diagnostic findings (implementation notes)

Findings surfaced while specifying the availability rules; each is slice-5 implementation work, recorded here, not yet coded:

a. **Run-state signal unification.** The two surfaces track "a match run completed" differently — Import overloads `_lastMatchDate != null` (`ImportView.xaml.cs:66,1361`), Edit uses a dedicated `_matchSetlistHasRun` bool (`EditMetadataView.xaml.cs:59,1836`). Unify on **one** mechanism during the shared-core extraction (§7.1) so both surfaces reason about run-state identically. Note the panel gate (§7.5) is *not* this signal — it keys on `_lastSetlistProjection`; unification concerns the right-click gate and the status/guidance copy only.

b. **`ClearView` stale-state leak (latent).** Import's `ClearView` (`ImportView.xaml.cs:452-473`) resets `_lastSetlistProjection` (`:465`) and `_lastConfirmedCombines` (`:463`) but does **NOT** reset `_lastSetlistSongs`, `_lastClaimedPositions`, or `_lastMatchDate` (the in-code comment at `:460-462` acknowledges the broader `_lastMatch*` cleanup is banked). Stale match state can leak onto a newly loaded album. Sweep the full reset into slice 5 — it becomes load-bearing once panel assignment reads/writes that state outside a fresh run.

c. **Misleading guidance copy.** Copy in the review dialog and the Edit status bar tells the user to "match manually in the grid" / "right-click to match manually" (Import status at `ImportView.xaml.cs:944`) in states where the menu item **cannot** appear — the cancel path and a fully-claimed setlist (menu gate false). When the availability rules land, rewrite the guidance so copy and gates agree: point at the **panel** (always available when populated), not the sometimes-absent menu.

d. **Unclaimed-indication asymmetry.** Import has **no** post-match unclaimed indication; Edit tints unclaimed rows amber via `RecomputeUnmatchedHighlights` (`EditMetadataView.xaml.cs:507-511`, `vm.ShowUnmatchedWarning = _matchSetlistHasRun && vm.Track.IsMatched != true`). The surfaces also disagree on what "unmatched" *means* visually: Import gold = title-unresolved **always**; Edit amber = **gated on a run**. Slice 5 should either reconcile the two indications or explicitly document why they differ — and ensure panel assignment updates whichever indication a surface uses.

## 8. External info-file open

The in-panel Info tab is **dropped**. The info `.txt` is no longer hosted inside the panel; it opens in the OS default editor, so it can sit in its own window beside the setlist **editor** — the real comparison workflow (§9), which an in-panel tab could never serve.

- **Import**: retarget the existing **View Info** button (`ViewInfoButton_Click`, `ImportView.xaml.cs:1582`) from opening the `ReadPanelHost` scrim to opening the info `.txt` in the OS default editor via the codebase's shell-open idiom — `Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })`, wrapped in try/catch-to-status (precedent: `OpenFolderButton_Click`, `ImportView.xaml.cs:1672-1686`). The path is `Path.Combine(_albumInfo.FolderPath, _albumInfo.InfoFileName)` (`AlbumInfo.cs:13,39`). The button stays disabled when there is no info file, mirroring the existing disable at `ImportView.xaml.cs:429`.
- **Edit**: net-new — no info-file plumbing exists on this surface (`InfoFileContent` is `null`; `EditMetadataView` builds `_albumInfo` by hand and never calls `ReadAlbumInfo`). A net-new host affordance performs first-`*.txt` discovery over `_show.FolderPath` (`EditMetadataView.xaml.cs:29,122`) and the same shell-open. It inherits Import's **"first `.txt` wins"** behavior (still banked; multi-`.txt` disambiguation out of scope). A2 does not fix this affordance's placement — the now-single-tab Setlist panel header or a control in the existing right-column StackPanel are both viable; implementer's call, flagged (§13).
- **Panel header**: with the Info tab gone, the panel's 2-tab `TabControl` collapses to a plain **Setlist** panel header (cosmetic). This may ride with this slice or bank if churny — implementer's call, flagged (§13).
- **Rationale (recorded)**: the user's real workflow is side-by-side comparison of the info file with the setlist **editor** (a different view, §9), not the import grid. An in-panel tab cannot serve that; an external editor window can.

## 9. Setlist-editor deep-link

The panel exposes an explicit **"Edit setlist ↗"** affordance in its Setlist header — **not** a tab-click or row-click side effect (the panel stays a pure display + event surface, §4). Activating it raises `EditSetlistRequested`; the **host** performs the deep-link:

1. Resolve the concert: `ConcertLookupService.Instance.GetConcertByDate(date)` (`ConcertLookupService.cs:266-273`, O(1) warm).
2. **Null-guard**: no concert for the date → status message, no navigation.
3. Navigate: `NavigateToConcertDetail(concert, ...)` (`ShellWindow.xaml.cs:742-746`), reached via the existing `Window.GetWindow(this) as ShellWindow` pattern.

From the concert-detail view the user enters the existing **Edit Setlist** flow, saves, verifies, and navigates back.

**Freshness on return needs no invalidation plumbing.** `ConcertLookupService` is a shared-live-instance cache: `EditSetlistView.SaveChangesAsync` mutates the same object that `GetSetlist` reads through (`EditSetlistView.xaml.cs:409-550`; `ShowLookupService.cs:202-209`). A fresh write is current immediately.

**Resolved decision — no refresh-on-return hook.** The panel does not auto-refresh when Import becomes current again. The user's stated workflow is to hit **Read** and restart the import after fixing the setlist, and Read already re-invokes `RefreshSetlistPanelAsync` (`ImportView.xaml.cs:328`). A refresh-on-return hook (mirroring the Concerts grid's `RefreshFromCache`, `ShellWindow.xaml.cs:278-281`) is **banked** (§13) as the known small fix if a stale-panel-after-return ever bites. Lived-demand rule.

**Kept-alive singleton is incidental, not load-bearing.** ImportView happens to be a kept-alive singleton (`ShellWindow.xaml.cs:29,553-572`), so in-progress import state survives the round-trip — but the design does not depend on it (the user re-Reads regardless). Note the asymmetry: `EditMetadataView` is reconstructed per entry (`ShellWindow.xaml.cs:469`), so an Edit-side round-trip would lose edits. The deep-link affordance is therefore **Import-first; the Edit-side affordance is banked** (§13).

**Motivating case (segue ground truth).** Panel segue markers render correctly end-to-end (`ConcertSetlistAdapter.cs:54` → `SetlistProjection.cs:49` → `SetlistReferencePanel.xaml.cs:41`, `.xaml:67`). Observed missing markers — e.g. 1971-02-23, China Cat `>`, and Truckin' `>` Drums `>` The Other One `>` Wharf Rat — are gaps in the `Data/concerts/*.json` **source data**, not rendering bugs. Fixing them is exactly the deep-link → edit → save round-trip above; this is the feature's motivating validation case.

## 10. Drag-and-drop assignment

- Drop-side machinery already exists on **both** grids (drag-to-reorder), so drop-onto-a-track-row is established, not greenfield: `ImportView.xaml.cs:923-998` (`DoDragDrop` at `:951`, `Row_DragOver`/`Row_Drop` type-gated at `:959,:979`); `EditMetadataView.xaml:242,286-292` (`AllowDrop="True"` + row `EventSetter`s).
- The setlist drag carries a **distinct payload type** (the `SetlistEntryVm`, not `TrackInfoViewModel`), so its `DragOver`/`Drop` branch never cross-triggers the reorder branch. `GetDataPresent` type-gating must match the payload type exactly — a mismatch is a **silent-fail** mode (documented in edit-metadata-viewmodel-migration-inspection-2026-04-25.md); keep the payloads distinct.
- Net-new work is only the **drag source** on setlist rows (a ListBox/ItemsControl item calling `DoDragDrop`). The drop handler calls the §7 core + the same per-surface hooks as click-assign.
- Ships **after** click-assign (slice 6, following click-assign in slice 5), so assignment does not depend on DnD mechanics.

## 11. Slice plan

One concern per commit; WPF manual gate on every UI-visible slice; pure helpers unit-tested.

1. ✅ **Read-only Setlist tab (Import).** Implemented (uncommitted at time of amendment; manual gate in progress). Extract pure `BuildSetlistProjection` (§5.1) + retrofit `MatchSetlistButton_Click`'s inline flatten onto it; build `SetlistReferencePanel` with the read-only Setlist tab; host on Import; populate on date entry (§5.2) with the cold-load guard (§5.3); post-match dimming mirroring `_lastClaimedPositions` (§6). Unit tests on the projection. Unchanged.
2. **Host the same control on Edit.** Nothing else — per-view host wiring only (§3). WPF gate.
3. **Setlist-editor deep-link** (NEW). "Edit setlist ↗" event + host deep-link with null-guard; no refresh hook (§9). WPF gate = the manual round-trip: deep-link, fix a setlist (the 1971-02-23 segues are the fixture), save, back, re-Read, verify the panel + matcher see the fix.
4. **External info-file open** (replaces the old Info-tab slice). Import: retarget the View Info button to shell-open (§8). Edit: net-new first-`.txt` discovery + shell-open. Optional: collapse the vestigial TabControl to a plain Setlist panel header. WPF gate.
5. **Click-to-assign** (formerly slice 4, content unchanged). Extract the shared apply core (§7.1), retrofit BOTH existing Match-to-Song handlers onto it (§7.2), wire select-track → double-click-entry → `AssignRequested` → core + per-surface hooks. Honor the **availability rules** (§7.5: panel gate = populated, not run-gated; allow-on-resolved with previous-position freeing, §7.5.1) and sweep the four diagnostic work items (§7.6: run-state unification, `ClearView` reset, guidance copy, unclaimed-indication asymmetry). Unit tests on the core. WPF gate.
6. **DnD assign** (formerly slice 5, content unchanged). Drag source on setlist rows + payload-typed drop branch calling the slice-5 core (§10). WPF gate.

## 12. Decision record

Resolved decisions carried into implementation, each with its one-line rationale:

1. **Cold-load guard = async `ConcertDataGate.EnsureLoadedAsync` on the panel-population path** — keeps the UI responsive on the fresh-Import pre-Read cold hit (follow-ups.md:300) and reuses the one existing app-wide cold-load overlay; scoped reversal of "don't await LostFocus," limited to panel population (§5.3).
2. **Shared apply core, pure over `TrackInfo`** — eliminates the inline write-set duplication so click, DnD, and right-click all write identically; per-surface side effects stay in the host where dirty/verify/validation state lives (§7.1).
3. **Retrofit both existing Match-to-Song handlers onto the core in slice 5** — prevents a third inline copy of the write set and keeps all four assignment entry points convergent (§7.2).
4. **Segue set-true-only, never cleared** — write-identical to today's manual path; the segue-clear question is a separate, banked concern, not silently changed by this feature (§7.3).
5. **No `MaybeUnverifyAlbumEdit` on assign** — parity with the existing manual handlers (follow-ups.md:318); the feature neither fixes nor widens that banked gap (§7.4).
6. **In-panel Info tab replaced by external shell-open** — the comparison target is the setlist editor, not the import grid; an external editor window serves side-by-side compare, an in-panel tab cannot (§8).
7. **Deep-link via an explicit affordance, never a tab/row-click side effect** — the panel stays a pure display + event surface (§4, §9).
8. **No refresh-on-return hook** — the user re-Reads by stated preference and Read already refreshes the panel; banked as the known small fix (§9, §13).
9. **Two-shows-one-date banked as a separate data-model arc** — the panel and round-trip work within the one-show model (§13).

## 13. Out of scope / banked

- GridSplitter / user-resizable panel width (§3).
- Manual-assign segue-clear semantics (§7.3).
- `MaybeUnverifyAlbumEdit`-on-manual-assign (§7.4, follow-ups.md:318).
- Multi-`.txt` info-file disambiguation (§8).
- Combined/alias read-side matching — the panel shows the plain flattened setlist and is orthogonal to the aliasSetlists read-side arc (follow-ups.md "Combines: the matcher never reads aliasSetlists back").
- **Refresh-on-return hook** for the Import panel (§9) — dropped by lived-demand; the known small fix (mirror the Concerts grid's `RefreshFromCache`) if a stale panel after a deep-link return ever bites.
- **Two-shows-one-date model limitation**: `ConcertReference.MultiShow` is a bare marker (`ConcertReference.cs:21`); the flat `Sets`/`Tracks` with a fixed 5-label set axis (`EditSetlistView.xaml.cs:64-65`) cannot represent early + late shows as distinct shows (fixture: `Data/concerts/1970-01-03.json` line 215, one 15-song "Set"). A separate future concert-data arc; the panel and round-trip work within the one-show model. The user's working convention folds multiple shows on one date into the sets of a single concert record (within the existing five-label set axis), so this model-limitation arc is deprioritized, not blocking.
- **Edit-side deep-link affordance** (§9 asymmetry) — Import-first because `EditMetadataView` is reconstructed per entry, so an Edit-side round-trip would lose in-progress edits.
- **Vestigial-TabControl cleanup** if not taken in the external-open slice (§8).
