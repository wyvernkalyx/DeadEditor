# Concert Verification Spec

**Status:** Implemented (2026-06-10)
**Date:** 2026-06-10
**Branch:** `feature/library-verification-surface`

**Shipped commits**
- `4c560a0` — prerequisite: cache coherence (rekey on date-change save, evict on delete)
- `581eac5` — layer 1: `ConcertReference.Verified` + `ConcertVerifyGate`
- `845b316` — layer 2: `EditSetlistView` verify wiring (`ConcertSnapshot`, diff-at-save)
- `8258116` — layer 3: `SetlistFetcher` skip-verified guard
- `2cfe5e2` — layer 4: `ConcertDatabaseView` glyph column + navigation refresh
- `1c41de4` — amendment: explicit Unverify control (decision 6 revised)

**Cross-references**
- `documentation/verification-model.md` — verification status doc (the principle: "import once,
  verify once, never again"; external sources may suggest, never silently overwrite).
- `documentation/box-set-verification-spec.md` — the structural template. Concert verification is
  the **third** verification surface, after album manifests (shipped 2026-05-20) and box sets
  (shipped 2026-06-06).
- `documentation/verification-and-manifest-wiring-design-memo.md` — the original album pattern both
  later surfaces adopt (page-level bool, unverify-on-edit, no provenance).
- `Models/ConcertReference.cs`, `Services/ConcertLookupService.cs`, `Views/EditSetlistView.xaml.cs`,
  `Helpers/BoxSetVerifyGate.cs`, `Services/EditUnverifyRule.cs`, `tools/SetlistFetcher/Program.cs`.

---

## BLUF
The concert store (`Data/concerts/*.json` via `ConcertLookupService` — the canonical setlist
authority since the concerts/ redirect) gets the same verification model already shipped for albums
and box sets. Add a bare `bool Verified` to `ConcertReference`, a pure `ConcertVerifyGate`, an
in-body verify surface in `EditSetlistView` with **diff-at-save** unverify, a **skip-verified** guard
in the `SetlistFetcher` so re-fetches cannot clobber curated setlists, and a verified glyph column in
`ConcertDatabaseView`. No new verification model; this is the box-set pattern applied to a third
record type, with two concert-specific divergences (no per-track-date gate rule; the fetcher guard is
a new mechanism with no box-set analog).

---

## Prerequisite (already shipped)
Cache coherence was fixed in commit `4c560a0` — `ConcertLookupService.NotifySaved(oldDate, concert)`
(rekey on date-change save) and `Evict(date)` (evict on delete), both idempotent and `_sortedDates`-
maintaining. **Verification builds on this:** save and delete coherence no longer rests on accidental
shared-instance mutation. A verified concert whose date is edited rekeys cleanly under the new date
(and the verify state, being a field on the same live instance, rides along); a deleted verified
concert leaves no stale cache hit. Without `4c560a0` a date-change on a verified concert would have
left the verified record resolvable under the stale key — verification would have inherited the same
desync it is meant to prevent.

---

## Mapping (box-set decision → concert analog)
| Box-set model (shipped) | Concert analog | Status |
|---|---|---|
| Bare bool `BoxSetDefinition.Verified`, no provenance | `ConcertReference.Verified`, bare bool | Adopt |
| Pure `BoxSetVerifyGate.Evaluate` → `(bool, reason)` | Pure `ConcertVerifyGate.Evaluate` → `(bool, reason)` | Adopt |
| Gate requires per-track Date present + parseable | **No per-track-date rule** (tracks default date from concert) | Diverge |
| Unverify via diff-at-save (serialized baseline) | Same: serialized baseline at ctor, `EditUnverifyRule.IsDirty` | Adopt |
| Archivist-Note carve-out: none (box sets) | No carve-out (concerts have no note field) | Adopt |
| Verify surface in built Step 3 Review panel | Verify surface **in-body in EditSetlistView** (not HeaderBar) | Adopt (different host) |
| Honest badge: `Verified && !IsDirty`, recomputed on state change | Same | Adopt |
| Persist-on-save (terminal Save persists; Cancel discards) | Same (the editor's existing Save writes the file) | Adopt |
| Glyph in Box Sets list | Glyph column in `ConcertDatabaseView` | Adopt |
| — (no analog) | **Fetcher skip-verified guard** | New mechanism |

---

## Settled decisions

1. **Scope.** Whole-record bool (`ConcertReference.Verified`). Not per-track, not per-set, not
   per-field. Matches `BoxSetDefinition.Verified` and `AlbumManifest.Verified`.

2. **Model.** Add `public bool Verified { get; set; }` to `ConcertReference`. Bare bool — **no
   provenance** (no `VerifiedAt`, no verifier identity, no source field). `LastUpdated` keeps its
   existing overwrite-on-save semantics (`EditSetlistView` stamps it to `DateTime.UtcNow` on every
   save; the fetcher stamps it on every write) and is **NOT** a verification timestamp — do not
   repurpose it. Additive and camelCase via `CanonicalJson` (the serializer `EditSetlistView`
   already uses). Existing `concerts/*.json` files have no `verified` key and deserialize with
   `Verified = false` — the correct default (everything currently in the store is fetcher-sourced,
   unverified by definition).

3. **Verify gate.** New pure, WPF-free, I/O-free helper `ConcertVerifyGate.Evaluate(ConcertReference)`
   returning `(bool CanVerify, string? Reason)` with the **first failing reason**, mirroring
   `BoxSetVerifyGate`. Conditions, checked in this order:
   1. **Date** parses as `yyyy-MM-dd` (shape regex `^\d{4}-\d{2}-\d{2}$`, consistent with
      `BoxSetVerifyGate.DateRegex` and the `EditSetlistView` save validation — a shape check, not a
      calendar parse).
   2. **Venue** non-empty (trimmed).
   3. **At least one track** (`Tracks.Count > 0`).
   4. **Every track** has a non-empty song name (trimmed).

   **Explicitly NOT in the gate, and why:**
   - **No per-track-date rule.** Box-set tracks require a present, parseable per-track date because a
     box set is multi-date by nature and the date lives on the track. A concert is single-date: its
     tracks default their date from the concert date (`EditSetlistView` save sets
     `trackDate = !string.IsNullOrEmpty(track.Date) ? track.Date : date`, and `ConcertSong.Date`
     falls back to the concert date on load). Importing the box-set per-track-date strictness here
     would be wrong — it would block verification of correct single-date concerts whose track dates
     are simply inherited. The concert-level Date check (condition 1) already covers date validity.
   - **No city/state/country requirement.** Location is venue metadata, frequently partial in
     setlist.fm source data (non-US shows, missing state codes), and not load-bearing for the setlist
     authority the concert store represents. Venue name (condition 2) is the meaningful "this record
     identifies a real show" signal; requiring city/state would block verification of otherwise-good
     setlists over incidental gaps.

   The gate is **self-contained** — evaluated against the current in-memory `ConcertReference`, not
   assuming the editor's Save validation ran. (As with box sets, the gate is stricter than the Save
   validation, which only requires a well-formed date and ≥1 track and does not require a non-empty
   venue or non-empty song names.)

4. **Unverify-on-edit via diff-at-save.** Same mechanism as box sets — one serialized baseline at
   view construction, one dirty-check at Save. This covers every edit path uniformly (cell edits,
   Add/Remove song, Normalize, venue/city/date field edits) with no per-mutation hooks.
   - **Baseline MUST be a serialized JSON string, never an object/list reference (the box-set
     correctness landmine).** `EditSetlistView`'s grid mutates the live `EditableTrack` instances and
     ultimately the live `ConcertReference` (`_concert`). A baseline of `_concert` (or `_concert.Tracks`,
     or a shallow copy) would equal current at save *always* — the diff would silently never fire and a
     verified concert would never unverify, with no error. Capture the baseline as a serialized
     string at view construction, before any edit — specifically the **normalized + serialized** form
     pinned below, not a raw serialize.
   - **Diff = serialized compare.** Reuse `EditUnverifyRule.IsDirty(baselineJson, currentJson)` — the
     existing pure ordinal two-string compare (no trim, no case-fold). Both sides are the serialized
     `ConcertReference`. Do not reach for a structural comparer.
   - **Shared serializer.** Baseline, diff, and persisted bytes all go through `CanonicalJson.Serialize`
     (the same call `EditSetlistView.SaveChangesAsync` already uses to write the file), so they cannot
     drift.
   - **Baseline = normalized + serialized (PINNED).** At view construction, apply the **same
     rebuild/normalization that Save applies** to the loaded concert — track-date fill-in from the
     concert date, set regrouping — *then* `CanonicalJson.Serialize`. A raw serialize of the
     as-deserialized object is **NOT acceptable**. Concrete hazard: a file whose tracks omit explicit
     dates deserializes with empty track dates, and a zero-edit open-and-save rebuilds them as explicit
     dates (`trackDate = !string.IsNullOrEmpty(track.Date) ? track.Date : date`); a raw baseline would
     therefore differ from the rebuilt current and **spuriously unverify**. `LastUpdated` restamping is
     the second, better-known instance of the same class.
   - **`LastUpdated` excluded from the compare (PINNED).** Clear/zero `LastUpdated` on **both**
     serialized sides (or serialize copies with it cleared) before the diff. Do **NOT** rely on call
     ordering inside `SaveChangesAsync` — "snapshot before restamp" is **rejected as fragile**.
   - The invariant stands: a no-edit open-and-save must **NOT** unverify.
   - **Tracked fields** (any difference unverifies): Date, Venue, City/State (the editor's location
     fields), and the track list (per track: SongName, Date, Segue, Set; add/remove/reorder). Set
     assignment and segue are setlist content and are tracked.
   - **Not tracked** (pure view state): grid selection, scroll position, the transient `_hasUnsavedChanges`
     flag.
   - **Verify rebaselines.** "Mark Verified" re-snapshots the current `_concert` as the new baseline, so
     the same-pass diff does not undo the verify; a *subsequent* edit re-dirties and unverifies at the
     next save.

5. **No carve-out.** Concerts have no Archivist-Note equivalent. Every tracked-field edit unverifies;
   there is nothing to exempt.

6. **Explicit Unverify control** (amended 2026-06-10 — supersedes the original "no unlock gesture").
   Concerts gain a manual **Unverify** control in the `EditSetlistView` verify slot, alongside Mark
   Verified.
   - **Rationale.** Verification can be withdrawn purely for *trust* reasons, independent of having a
     correction in hand ("I no longer trust this record as-is"). Forcing a throwaway edit to express
     that — the only way under the original decision — is a workaround. Surfaced by lived demand
     during the 2026-06-10 implementation gates; the edit-and-revert workaround is rejected.
   - **Additive, not a replacement.** The edit-driven unverify (diff-at-save, decision 4) is
     **unchanged**. The manual control is a second, independent way to drop `Verified`; the first
     edit still dirties → unverifies at save exactly as before. Re-verify via the same in-body Mark
     Verified control.
   - **Behavior.** Visible **only** when the badge shows verified (the honest `effectiveVerified`
     state), mirroring how Mark Verified hides in that state. Click sets `_concert.Verified = false`
     in-memory and calls `RefreshVerifyControls()` — the badge drops to Unverified and Mark Verified
     reappears (enabled, since the content still passes the gate). **No rebaseline** — `Verified` is
     not part of the content snapshot and the content has not changed, so the baseline stays valid.
   - **Persist-on-save symmetry.** Like Mark Verified, Unverify sets the in-memory flag only; the
     change reaches disk through the editor's normal Save. **Implication:** unverifying and then
     navigating away *without saving* leaves the concert **verified on disk** — exactly as Mark
     Verified without saving does not persist the verify.
   - **Precedent divergence.** This diverges from the album/box-set surfaces, which have no manual
     unverify. Whether they adopt the same control is **banked as a follow-up** (`follow-ups.md`) — do
     **not** implement it there as part of this work.

7. **Verify UI — in-body in `EditSetlistView`, not HeaderBar.** Unlike the concert delete/edit actions
   (HeaderBar-driven), the verify surface lives in the view body alongside the setlist, mirroring the
   box-set Step 3 Review panel's locality. Three elements:
   - **Verified badge** — shown verified only when `Verified && !IsDirty(baseline, current)` (honest
     `effectiveVerified`), recomputed when state changes (the box-set `RefreshVerifyControls` pattern:
     re-evaluate on load, after Mark Verified, and after edits that flip the dirty state). A verified
     concert that has been edited shows as not-verified, which is the truth.
   - **Mark Verified button** — enabled only when `ConcertVerifyGate.Evaluate` passes; on click,
     re-check the gate, set `_concert.Verified = true` in-memory, rebaseline (decision 4), and refresh
     the controls.
   - **Disabled-reason text** — when the gate fails, surface its first failing reason (wrapping text,
     box-set pattern).
   - **Persistence is persist-on-save.** Mark Verified sets the in-memory flag only; the flag reaches
     disk through the editor's normal Save (which serializes the whole `_concert`, `Verified` included).
     **Implication to document for the user-facing behavior:** marking verified and then navigating away
     *without saving* does not persist the verify — the flag is discarded with the unsaved view, exactly
     as box-set Cancel discards.

8. **Fetcher guard — skip verified concerts.** `tools/SetlistFetcher` must not clobber curated setlists.
   Before writing `{dateKey}.json` (`Program.cs` ~`:298-300`), if the target file already exists, parse
   it as **raw JSON** (`JObject` / `JsonConvert` — the tool is a standalone console project with no
   reference to the app model `ConcertReference`, and must stay that way) and **skip the write when the
   parsed `verified` field is `true`**. On skip, log a per-file line (e.g. `[SKIP verified] {dateKey}`)
   and increment a counter surfaced in the end-of-run summary (e.g. `Verified concerts skipped: N`).
   Non-verified and absent files are written as today.
   - **Rejected alternatives:** (a) sidecar/diff files recording what the fetcher *would* have written —
     no consumer exists, so it is write-only noise; (b) overwrite-and-clear (write fresh data, reset
     `verified` to false) — this is the exact silent-destruction-of-curation failure mode the feature
     exists to prevent.

9. **Surfacing — glyph column in `ConcertDatabaseView`.** Add a narrow leading glyph column to the
   concerts DataGrid (which binds to `List<ConcertReference>`), bound to `Verified`, reusing the
   Library-grid glyph vocabulary (✓ in `BadgeVerifiedBg` green for verified; empty cell otherwise —
   absence is the indicator, per `verification-model.md` § Library Grid Surface). Concerts are
   single-record (no merged/tri-state `Partial` case — that was a multi-folder album concern only).

---

## Out of scope / deferred (stated, not designed here)
- **Second-source compare** (diffing JerryBase / setlist.fm / other sources against the curated
  record). The verify model is single-source attestation; cross-source reconciliation is a separate
  feature. Surface-and-offer at the consume surfaces is deferred *with* this.
- **`ConcertSetlistAdapter` widening / verify-state at consume surfaces.** In v1 the adapter seam is
  **not** widened: box-set pull-setlist and Import/Edit "Match Setlist" do **not** see concert
  verify-state. Surfacing "this setlist is verified" at those consume points is deferred (couples
  naturally with second-source compare).
- **Force-verify-on-pull** (auto-marking a concert verified because something pulled from it). No.
- **Add Concert capability.** Creating a new concert record in-app is a related but separate feature
  (see follow-ups). A hand-entered concert is **born unverified** and uses this same gate to become
  verified, but record *creation* (the UI entry point + first-save handling) is its own work.
- Intermediate verification states (`In Progress` / `Needs Review`) — gated on the still-open
  `verification-model.md` Q2; v1 stays binary.

---

## Implementation plan (layered, one concern per commit; WPF gate before each UI commit)

1. **Model + gate (no UI).** Add `ConcertReference.Verified`; add pure `ConcertVerifyGate.Evaluate`
   (`Helpers/ConcertVerifyGate.cs`); add xUnit tests mirroring the `BoxSetVerifyGate` test shape (pass
   case + one test per failing condition, asserting the first-failing reason). No WPF gate (no UI).

2. **`EditSetlistView` wiring.** Baseline at construction is **normalized + serialized, `LastUpdated`
   excluded** (decision 4); diff-at-save unverify (`EditUnverifyRule.IsDirty`) wired into
   `SaveChangesAsync` so an open-and-save with no edit does not false-unverify; in-body badge + Mark
   Verified button + disabled-reason; `effectiveVerified` recompute on state change; verify rebaselines.
   **Manual WPF gate before commit.**

3. **`SetlistFetcher` skip-verified guard.** Raw-JSON existence/`verified` check before write; per-file
   skip log + summary count. **Note:** the tool currently has no test project; **manual verification is
   acceptable** for this commit (run the fetcher against a directory containing one hand-marked
   `verified: true` file and confirm it is skipped while others rewrite) — state this in the commit.

4. **`ConcertDatabaseView` glyph column.** Leading `Verified` glyph column, Library-grid vocabulary.
   **Manual WPF gate before commit.**

---

## Phase A findings (recorded for the implementer)
- `ConcertReference` fields: Date, Venue, City, State, Country, SetlistFmId, SetlistFmUrl, LastUpdated,
  HasSetlist, MultiShow, `List<ConcertSet> Sets`, `List<ConcertTrack> Tracks`. `FormattedLocation` and
  `SongCount` are `[JsonIgnore]` computed views — never persisted.
- `EditSetlistView` editable fields: Venue, City/State (one text box, split on first comma), Date, and
  the `EditableTrack` grid (Position, SongName, Date, Segue, Set). Save rebuilds `Sets` + `Tracks` from
  the grid, stamps `LastUpdated = UtcNow`, and writes via `CanonicalJson.Serialize` + atomic temp-rename
  to `AppDataConcertsPath/{date}.json`. Save already calls `ConcertLookupService.NotifySaved` (`4c560a0`).
- `EditSetlistView` Save validation (existing, looser than verify): date matches `^\d{4}-\d{2}-\d{2}$`,
  and `_tracks.Count > 0`. No venue or song-name requirement — so "venue non-empty" and "every track has
  a song name" are genuinely new verify requirements.
- `ConcertVerifyGate` template: `Helpers/BoxSetVerifyGate.cs` (`(bool, string?)`, first-failing reason,
  `DateRegex` shape check).
- Unverify rule: `Services/EditUnverifyRule.IsDirty` (ordinal string compare). `WouldUnverify` carries an
  `isArchivistNote` carve-out param that concerts do not need (pass the no-carve-out path or just use
  `IsDirty` directly behind an `if (_concert.Verified)` guard).
- Serializer: `CanonicalJson.Serialize` (camelCase, computed keys dropped) — already used by
  `EditSetlistView` and shared with `BoxSetService`.
- Fetcher write site: `tools/SetlistFetcher/Program.cs` builds a `Dictionary<string,object>` concert and
  `File.WriteAllTextAsync(concertPath, ...)` at ~`:298-300`; summary block at ~`:303-313`. The tool does
  **not** currently write a `verified` field (additive default-false is correct) and references no app
  model — the guard must parse raw JSON.
- `ConcertDatabaseView` binds `ConcertsDataGrid.ItemsSource` to a `List<ConcertReference>` (`:48-50`);
  glyph column binds to `Verified`. Existing owned/missing row coloring is separate and unaffected.

---

## Doc hygiene (commit 1)
- `verification-model.md`: after the Box-Set Surface section, the concert surface becomes the third
  shipped surface — add a "Concert Surface" subsection (or pointer) when commit 1 lands, and a pointer
  to this spec.
- `CLAUDE.md` Documentation Handbook: `concert-verification-spec.md` joins the verification spec set
  alongside `box-set-verification-spec.md`.
