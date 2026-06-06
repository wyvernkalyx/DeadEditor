# Box-Set Verification Spec

**Status:** Shipped (2026-06-06)
Implemented by 3d1965b (Step 3 Review surface) and 1c24a27 (verification behavior); spec landed in 1b69c9b.
**Date:** 2026-06-06
**Branch:** `feature/library-verification-surface`

**Cross-references**
- `documentation/verification-and-manifest-wiring-design-memo.md` - the shipped album verification
  pattern this spec adopts.
- `documentation/verification-model.md` - verification status doc.
- `documentation/box-set-design-memo.md` - box-set data model (`definition -> List<BoxSetTrack>`).

---

## BLUF
Box sets adopt the shipped album verification pattern. `BoxSetDefinition.Verified` exists, persists,
and renders a glyph in the Box Sets list, but no code path sets, gates, or clears it - a passive
sticker (Phase A confirmed). This spec wires it up. Two findings shape the work: (1) the wizard's
Step 3 Review is a placeholder and must be **built**, not just wired; (2) the unverify mechanism is
**diff-at-save** (not the album's live field handlers), because the wizard's step-1 fields are
read-on-save, not live-bound. No new verification model; new Review UI required.

---

## Mapping (album decision -> box-set analog)
| Album model (shipped) | Box-set analog | Status |
|---|---|---|
| Page-level bool on `AlbumManifest` | `BoxSetDefinition.Verified` (exists) | Adopt |
| Verify action in EditMetadataView, gated on required fields | "Mark Verified" in the built Step 3 (Review), gated | Adopt + build UI |
| Unverify fired from live field handlers | Unverify via **diff-at-save** (serialized baseline vs current) | Diverge (mechanism) |
| Archivist Note carve-out | **No carve-out** (editing `Notes` unverifies) | Diverge |
| Enrich on verified -> unverify on apply; preview deferred | Pull-setlist is an edit -> unverifies via the same diff | Adopt |
| Bare bool, no provenance | Bare bool, no provenance | Adopt |
| Unlock-to-edit: rejected | No unlock; first edit dirties -> unverifies at save | Adopt |

---

## Settled decisions

1. **Scope.** Whole-definition bool (`BoxSetDefinition.Verified`). Not per-track, not per-field.

2. **Mark Verified action + Review display.** A control in the **newly built** Step 3 (Review) panel.
   Today Step 3 is a placeholder TextBlock (`BoxSetWizardView.xaml:326-331`); Save/Next is HeaderBar-
   driven. The Review panel (a summary of the box + a validation/verify control) must be built. The
   verify control is local to the Review panel.
   - **Honest badge (refinement 5).** Because unverify is deferred to save with no live feedback,
     the Review panel
     must display the *projected* state, not the stale stored bool. Compute, on panel-show,
     `effectiveVerified = _definition.Verified && !IsDirty(baselineSnapshot, currentSnapshot)` (the
     same diff used at save). A verified box that has been edited shows as not-verified on Review,
     which is the truth. This is a one-shot evaluation on panel-show, **not** live tracking - it
     stays inside the "no amber markers" deferral.
   - **Mark Verified** sets `Verified = true` and rebaselines (see decision 4), which immediately
     makes `effectiveVerified` true with no markers.

3. **Verify gate (required fields).** Mark-Verified is enabled only when ALL hold on the current
   in-memory definition, evaluated self-contained (not assuming Save validation ran):
   - Name non-empty (trimmed)
   - ReleaseDate parses as `yyyy-MM-dd`
   - At least one track
   - Every track: SongName non-empty (trimmed)
   - Every track: Date non-empty AND parseable
   When disabled, surface the first failing reason. The verify gate is strictly stricter than the
   Save gate. **Confirmed (Phase A):** Save validates per-track dates as *empty-allowed, non-empty-
   must-parse* (`BoxSetWizardView.xaml.cs:176-187`) and does not require >=1 track or non-blank
   SongNames - so all of "every track has a date", ">=1 track", and "all SongName non-empty" are
   genuinely new verify requirements. Dateless tracks remain a supported *working* state (Renumber
   sorts them last); they are blocked only at verify time. Editing-time tolerance, attestation-time
   strictness.

4. **Unverify-on-edit via diff-at-save.** One serialized snapshot captured as a baseline at wizard
   ctor; one dirty-check at Save. This single check covers every edit path uniformly - step-1 fields,
   cell edits, Add/Remove/Renumber/H3-date-delete/pull-setlist - so **no** TextChanged handlers, no
   broadened `CellEditEnding`, no per-mutation hooks.
   - **Baseline must be a serialized string, never an object/list reference (refinement 1, the
     correctness landmine).** The grid binds live to `_definition.Tracks` and edits mutate those exact
     `BoxSetTrack` instances (`BoxSetWizardView.xaml.cs:108-121`). A baseline of `_definition` or
     `_definition.Tracks` (or a shallow copy) equals current at save *always* - the diff silently
     never fires and verified boxes would never unverify, with no error. Capture baseline as a
     serialized JSON string (or a deep clone).
   - **Diff = serialized compare, not field-by-field (refinement, IsDirty semantics).**
     `EditUnverifyRule.IsDirty` is a pure ordinal
     two-string compare (`EditUnverifyRule.cs:18-21`), no trim/no case-fold. So the mechanism is
     `IsDirty(baselineJson, currentJson)` where both are the serialized definition. Do not reach for
     a structural comparer.
   - **Shared serializer (refinement 2).** `BoxSetService._jsonSettings` is private static (camelCase,
     `NullValueHandling.Ignore`, `:110-115`); `Write` serializes via it (`:261`). Expose a
     `public static string Serialize(BoxSetDefinition)` (or `Snapshot`) on `BoxSetService` that
     `Write` also uses, so baseline, diff, and persisted bytes share one serializer and cannot drift.
     Including `Verified`/`Version` in the snapshot is harmless (equal on both sides at diff time).
   - **Normalize before snapshotting (refinement 3).** Because `IsDirty` is ordinal and `Sync`
     trims, capture the baseline from the *normalized* definition (run `SyncStep1FieldsToDefinition`
     once at ctor before the snapshot), so an untrimmed on-disk box does not false-unverify on
     open-and-save with no user edit.
   - **Position in Save() (refinement 4).** Run the dirty-check after `ValidateStep1()`/Sync (which
     trims step-1 into `_definition`) and after the CommitEdit flush (`:171`), immediately before the
     `Write` block (`:216`). If `Verified && IsDirty(baseline, current)` -> `Verified = false`, then
     Write (which already carries `Verified` through whole-object serialization).
   - **Verify rebaselines.** "Mark Verified" re-snapshots the current definition as the new baseline,
     so the same-pass diff does not undo the verify, and a *subsequent* edit re-dirties and unverifies.
   - **Tracked fields** (any difference unverifies): Name, ReleaseDate, Label, CatalogNumber, Notes,
     Tracks (TrackNumber/SongName/Date/SegueOut per track; add/remove/reorder).
   - **Not tracked** (pure view state): accordion expand/collapse, group-by-date toggle,
     selection/scroll.

5. **No carve-out.** `Notes` exists; editing it unverifies like any other field.

6. **No unlock gesture.** Double-click -> edit-mode entry (`56d47fb`) stays; first edit dirties ->
   unverifies at save; re-verify in Step 3.

7. **Persistence / provenance.** Bare bool only. No timestamp, source list, or verifier identity.

8. **Pull-setlist.** An edit: applying it dirties the definition and unverifies via the diff. Pre-
   apply preview deferred.

---

## Likely commit shape (doc-first, one concern each; WPF gate on UI commits)
1. **doc** - this spec + stale-label fixes. (No code.)
1b. **doc** - reconcile recorded test baseline with an actual `dotnet test` count.
2. **build Step 3 Review surface** - replace the placeholder with a real summary panel; show
   `effectiveVerified`; fix the stray "Step N of 4" comment vs the "of 3" code.
3. **verification behavior** - Mark-Verified control + self-contained gate; expose
   `BoxSetService.Serialize`; baseline capture (normalized, serialized) at ctor; diff-at-save
   unverify; verify-rebaseline. May split; baseline-capture placement firms up at prompt time.

---

## Phase A findings (recorded for the implementer)
- `BoxSetDefinition`: Version, Name, ReleaseDate, Label, CatalogNumber, Notes, Verified, Tracks.
  Step-1 editable: Name, ReleaseDate, Label, CatalogNumber, Notes. Per-track: TrackNumber, SongName,
  Date, SegueOut.
- Step-1 fields are read-on-save (`SyncStep1FieldsToDefinition`/`LoadStep1FieldsFromDefinition`), not
  live-bound - the reason for diff-at-save.
- Step 3 Review is a placeholder; Save is HeaderBar-driven.
- Save per-track date rule: empty allowed, non-empty must parse (`:176-187`).
- `EditUnverifyRule.IsDirty`: ordinal string compare, no trim/case-fold (`:18-21`).
- `BoxSetService.Write` serializes whole object via private `_jsonSettings` (camelCase,
  NullValueHandling.Ignore; `:110-115`, `:261`).
- Mutation sites (informational; diff-at-save needs none individually): AddTrack `:323`, PullSetlist
  `:358`, Del remove `:423`, RemoveDate/H3 `:453`, Renumber `:486`, cell commits via two-way bindings.
- No baseline snapshot exists; capture at the edit-mode ctor (which already injects the loaded def).
- Album template: `EditMetadataView.VerifyButton_Click`, `MaybeUnverifyAlbumEdit`,
  `Services/EditUnverifyRule.cs`.

---

## Out of scope / deferred
Per-track/field verification; provenance fields; pre-apply pull-setlist preview; per-release curation
+ fingerprint identity; community curation; live unverify feedback (amber markers) on box sets.

---

## Doc hygiene (commit 1 / 1b)
- `verification-model.md`: drop the "(Stub - Future Design)" title; it is a populated status doc.
- `CLAUDE.md` (~line 123): `verification-model.md` is no longer "(placeholder)".
- Add a pointer from `verification-model.md` to this spec for the box-set extension.
- Reconcile the test baseline (handoff 279 vs `CLAUDE.md:862` 258) with one clean
  `dotnet test DeadEditor.sln`; correct `CLAUDE.md` to the real count.
