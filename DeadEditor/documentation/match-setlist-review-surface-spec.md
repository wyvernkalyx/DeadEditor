# Match Setlist Review Surface — Design Spec

_Status: approved design. Document-before-implement per the handbook. Implementation phases B1 (seam) and B2 (surface) follow._

## 1. Problem

Clicking Match Setlist silently mutates the grid. The user cannot see what changed, and the change can be destructive: the matcher does a blind, unconditional `track.Segue = NewSegue` (post-B1: `SetlistMatcher.Apply`, ~SetlistMatcher.cs:188), so when the canonical source under-captured segues (common for this dataset; confirmed on 1977-05-11, where all 23 entries carry `segue: false`), a run of correct hand/import-derived segues is silently cleared. Five real segues were wiped in one click. The pre-B1 matcher also fired `track.IsModified = true` on every match (the blind apply); B1 already replaced that with the on-real-change guard at `SetlistMatcher.Apply` (~SetlistMatcher.cs:194-198) — this spec's review surface narrows *which* fields apply, on top of that fix.

The name-matching itself is safe (name-gated, never mis-names). The defect is the silent, unconditional apply.

## 2. Goals / non-goals

Goals:
- Show, before anything is written, the per-track old-to-new changes Match Setlist proposes (SongName and Segue).
- Let the user resolve each change per field; apply only the resolved subset.
- Fix the no-op-flags-modified bug.
- Build the proposal model so combined-track matching (a later arc) slots in without reshaping it.

Non-goals (this arc):
- Combined-track matching (splitting "Scarlet Begonias > Fire on the Mountain" onto two entries) is a separate arc; this spec only ensures the model can represent it.
- Match-to-Song stays an immediate single-track action (the user is already explicitly choosing the value).
- Upstream segue-data repair (the Data/concerts/*.json under-capture) is scoped separately.

## 3. Architecture

### 3.1 ComputeProposals / Apply split (inside SetlistMatcher)
- `ComputeProposals(tracks, setlist, resolveCanonical)` runs the existing matching but writes nothing. For each matched track it captures the old values (track.SongName, track.Segue) and the would-be new values (entry canonical, entry segue), plus the entry indices it would claim. Returns a list of proposals plus the claimed set.
- `Apply(proposals)` performs the SongName/Segue/IsMatched writes (post-B1 at ~SetlistMatcher.cs:187-189, with the IsModified-on-real-change guard at ~:194-198), for the resolved subset.
- `MatchAndDecorate` becomes `Apply(ComputeProposals(...))` and returns the existing MatchResult unchanged, so both current callers (Import, Edit) are untouched.

This is additive: a new method and a new proposal type alongside MatchResult. No old-to-new capture exists today (the loop is a blind write), so this adds the first one.

### 3.2 The proposal model (the one addition that serves both designs)
Each proposal is one row per track, carrying:
- OldSongName to NewSongName (variant title to canonical setlist name)
- OldSegue to NewSegue
- CoveredEntryIndices, a list of the setlist indices this track claims

For single-song tracks the list is length 1. Building it as a list now (not an implicit single index) is what lets a future combined track carry [i, i+1, ...] with no model change. This is the single addition both the review surface and combined-track matching require; built once here.

## 4. Review dialog (preview-before-apply)

Invoked by Match Setlist in both Import and Edit (both call the converged matcher). Flow: ComputeProposals -> dialog -> Apply(resolved subset).

- One row per track that has a real change.
- Resolution is PER FIELD, not per row. Each row carries two independently-resolvable decisions: one for SongName (variant to canonical) and one for Segue (add or remove). A track can have its name normalized while its segue is left alone; this is exactly the 1977-05-11 case (canonicalize the title AND propose clearing a real segue in the same track). A single row-level toggle would force losing one or the other, so per-field accept/ignore is required for the Sec. 5 defaults to protect segues on mixed rows.
- For each field: Accept (take new) or Ignore (keep existing). The SongName field is presented as an editable value seeded with the proposed canonical name, so the user can take it, ignore it, or hand-tune it (covers any concatenation/alias need without a separate mode).
- No-op rows hidden, by conjunction: a row is hidden only when BOTH SongName Old == New AND Segue Old == New. A row whose name already matches but whose segue would change is NOT a no-op and stays visible. Hidden rows show as a collapsed count ("N tracks already match"); they are never written and never flagged modified.
- Combined rows (CoveredEntryIndices.Count > 1) are exempt from this conjunction hiding per alias-setlists-spec.md section 6.1 (shipped slice 4 Piece 1).
- Apply writes only the accepted/edited fields. Ignored fields are left exactly as they were.

### 4.1 Claiming is fixed at compute (invariant; do not "fix")
The greedy "first unclaimed entry with equal canonical name" claim is decided during ComputeProposals and is independent of the user's later accept/ignore. Ignoring a row or a field does NOT re-open its claimed entry for a later track. This is deliberate: letting accept/ignore re-open claims would reshuffle which track matched which entry mid-review. IsMatched keys off claim-at-compute (Sec. 6). A future reader must not optimize ignored rows into released claims.

## 5. Default selection policy (protective defaults, per field)

Each field's default (pre-accepted vs pre-ignored) is set per change type. On a mixed row the two fields default independently:

| Field | Direction | Default |
|---|---|---|
| SongName | variant to canonical | Accept |
| Segue | add (false to true) | Accept |
| Segue | remove (true to false) | Ignore |

The asymmetry is the point: the dataset's documented failure mode is segue under-capture (false negatives), so a sparse source clearing a real segue is the one direction that must never be silent. With per-field resolution, a mixed row defaults to Accept the name and Ignore the segue removal, so 1977-05-11 keeps its normalized names AND keeps its real segues on a blind "apply." This same table becomes the trailing-segue rule for combined tracks later (policy, not a hard-coded matcher branch).

## 6. Flag derivation (IsMatched / IsModified)

- IsModified is set only when an accepted field's resolved value actually differs from current (fixes today's bug where every match flags modified). Set if any accepted field really changed; never cleared by matching (won't wipe a pre-existing edit).
- IsMatched reflects "a setlist entry was claimed for this track" (claim-at-compute, Sec. 4.1), independent of accept/ignore.

## 7. Staged, not immediate

Unchanged from today: Match Setlist (and now its review/apply) mutates in-memory model/VM state only. Persistence to FLAC happens at Save (Edit) / Import-to-Library (Import). Cancel discards. The review surface gates the in-memory apply; it does not change when disk writes happen.

## 8. Implementation phasing

- B1, the seam: split SetlistMatcher into ComputeProposals + Apply; MatchAndDecorate becomes the thin compose. Add the proposal model with CoveredEntryIndices as a list. Behavior-preserving (compose still applies everything). Unit-gated, no UI change.
- B2, the surface: the review dialog with per-field resolution (the row VM carries an independent decision for SongName and for Segue); wire both call sites to compute -> review -> apply-subset; the Sec. 5 defaults; conjunction no-op hiding; the IsModified-only-on-real-change fix; claim-at-compute preserved. WPF manual gate.

Each its own concern, committed between layers.

## 9. Out of scope / future arcs

- Combined-track matching: a pre-check ahead of the single-song scan that splits on ">", canonicalizes each piece, and claims a consecutive in-order unclaimed run, declining as a unit on any partial / non-consecutive / unresolvable / already-claimed case. Produces a combined proposal row (multiple CoveredEntryIndices, kept combined name, one trailing-segue old-to-new governed by Sec. 5). Slots into this model with no reshaping.
- Upstream segue-data repair: fixing the under-capture in Data/concerts/*.json / the fetcher is the root cause for the dataset; the matcher only reflects what it is given.

## 10. Settled decisions and remaining UX details

Settled: protective per-field defaults (Sec. 5); per-field resolution (Sec. 4); claim-at-compute invariant (Sec. 4.1); conjunction no-op hiding (Sec. 4).

Remaining UX details for B2 (not blockers): exact wording/placement of the collapsed no-op count; whether the editable SongName field is always visible or revealed on demand.
