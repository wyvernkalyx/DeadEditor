# Alias Setlists — Spec (for review) — v4

Status: DRAFT under active implementation (slices 1-2 committed). Schema additive only.
Revision history:
- v2: Gregg's audit Q2-Q6 answers.
- v3: slice-0 probe folded (atomicity guard 5.1; persist seam (b) 4; PathGuard does not gate concert
  writes; alias write does not unverify the concert 8.1).
- v4: slice-3 Phase A folded. Verification is NOT hard-gated on matching (corrects P2 and 8); the
  real harm is persistent amber + P1 numbering drift, and the fix is clearing amber. 11.3 resolved
  (derive NewSongName, join covered official names with " > ", always). Two-pass matcher structure
  (5.3). Combined review rows are always shown, exempt from no-op hiding (6.1).
Arc type: generative. Grounding: Phase A audit + slice-0 probe + slice-3 Phase A at `ca873c7`.
Repo authoritative; each slice opens with a narrow read-only check before edits.

---

## 1. Problem

Combined tracks are a legitimate, recurring representation, not an error. Official releases use them
deliberately: *One From The Vault* lists 4 combined tracks of 16. Tapers use them too, with `>`.

The matcher is name-gated with no positional fallback (`SetlistMatcher.cs:136-137`). A combined media
track resolves to one canonical key that equals no single official entry, so it stays
unmatched/amber. Two concrete harms:

- **P1 - numbering drift.** A 15-track combined album maps against a 16-entry official setlist;
  numbers misalign and skip.
- **P2 - persistent spurious warning (corrected v4).** A faithful combined pressing carries a
  permanent amber unmatched warning and the P1 drift, and the combine is never recorded. NOTE: this
  is NOT a verify block - `VerifyButton_Click` checks only Artist + Date
  (`EditMetadataView.xaml.cs:1356-1373`), so such an album can already be verified today. The harm is
  the standing warning on a correct album, undermining "import once, verify once" in spirit.

## 2. Core idea (resolves the audit's split-vs-collapse fork)

Split and collapse are the same operation at two times. Decomposition (the split engine) runs at
match time -> `CoveredEntryIndices = [i..j]`. The alias (the collapse record) persists that confirmed
run. Same field, same semantics, same direction.

## 3. Reference-based by decision

An alias entry stores only the official entry indices it covers - nothing flattened. Display name and
match key are derived from the covered official entries.

### 3.1 Internal segue
A combined entry that is one media file has no track boundary inside it, so no internal segue to
preserve. Internal segue is informational only (derivable for display). The B2b guard governs only
the trailing/boundary segue of the combined entry.

### 3.2 Schema (additive on `ConcertReference`) - SHIPPED (slice 1, `ab15b1c`)

```jsonc
"aliasSetlists": [
  { "id": "string", "label": "string",
    "entries": [ { "coveredOfficialIndices": [1, 2] } ] }
]
```
Sparse overlay; singleton runs implicit. `ShouldSerializeAliasSetlists` omits an empty list. Plain
mutable class matching `ConcertReference` convention.

## 4. Persistence - seam (b), AppData atomic write

`ConcertLookupService.PersistAliasSetlist(date, aliasEntry)`: `GetConcertByDate` -> append ->
`CanonicalJson.Serialize` -> atomic temp+rename into AppData concerts path -> `NotifySaved`. Reachable
from both consumers (import has only the date; Edit has `_concert.Date`); the editor-writer seam is
unreachable from import. **PathGuard does NOT gate concert writes** (it gates library audio only);
discipline is AppData-first atomic temp+rename.

## 5. Match predicate - "official OR any registered alias"

An album is fully matched when every media track matches and every official entry is covered exactly
once, by either a direct single-name match (existing) or an alias/combine match (decomposition to a
contiguous official run `[i..j]`).

### 5.1 Separator safety - normalization-based atomicity guard - SHIPPED (slice 2, `ca873c7`)
Decomposition splits only when the full combined string does NOT resolve via
`NormalizationService.GetOfficialTitle` (titles AND aliases) to a known song. A resolvable whole
string is atomic and never split. Protects the medley title `Devil With the Blue
Dress/Good Golly Miss Molly/Devil With the Blue Dress` (`songs.json:543`) and the alias
`I Know You Rider> High Time tease` (`songs.json:1121`). Separators after the guard: `/`, `>`, `->`.

### 5.2 Validation gate (data integrity, non-negotiable) - SHIPPED (slice 2)
Accepted only if every component normalizes to a real official song and the components form a
contiguous in-order run. Else unmatched. Never fabricate a match or a song name.

### 5.3 Two-pass matcher structure (slice 3) - from slice-3 Phase A
`ComputeProposals` runs two passes, sharing one `claimed` set and one proposal list:
- **Pass 1 (existing): direct greedy.** Each track claims the first unclaimed official entry whose
  `Canonical` equals the track's canonical (`SetlistMatcher.cs:124-156`). High-confidence exact
  matches establish all claims first.
- **Pass 2 (new): claim-aware decomposition.** For each still-unmatched track, decompose against the
  still-unclaimed official positions; on a valid contiguous unclaimed run, claim the WHOLE run and
  build a combined proposal.

Two-pass (not inline) is required: inline makes the outcome depend on media track order (a combine
could claim a position a later direct track wanted, or be blocked by an earlier one). Two-pass
encodes the right priority - direct exact matches always win; combines fill gaps - and is
order-independent.

Combined proposal fields:
- `NewSongName` = the covered official `Canonical` names joined with `" > "` (11.3; always `>`).
- `NewSegue` = the LAST covered entry's `Segue` (the boundary segue to the next media track; internal
  segues are informational only, 3.1).
- `CoveredEntryIndices = [i..j]`.
- **Claim the whole run.** All covered indices are added to `ClaimedPositions` (pass-1 adds one;
  combines add the run). This is load-bearing: the manual Match-to-Song dialog builds its
  "still-unclaimed" list from `ClaimedPositions` (`ImportView.xaml.cs:818`,
  `EditMetadataView.xaml.cs:1563`), so a combine that fails to claim its full run would let the dialog
  re-offer an entry the combine already covers.

Claim-aware run selection lives in `CombinedTrackDecomposer` (slice 3 adds a claim-aware overload
that returns the first contiguous run with no claimed positions; slice 2's claim-blind version stays
for pure tests).

Dual-count note (do not conflate): `MatchResult.MatchedCount` counts tracks (a combine = 1);
`ClaimedPositions.Count` counts official entries covered (a combine = 2). Both are correct in their
own contexts (`ImportView.xaml.cs:887`).

### 5.4 Amber clearing (slice 3) - Gregg Q3
`Apply` sets `IsMatched = true` on a matched track (`SetlistMatcher.cs:185-200`), including combined
tracks, so the spurious amber unmatched warning clears. `NewSongName` (derived `>` form) typically
differs from the source string, so `IsModified` is also set. Verification itself is unchanged
(Artist + Date only); there is no match-completeness gate to add (corrects v3's "match -> eligible"
overstatement - the value is clearing amber and recording the combine, not unblocking a gate).

## 6. Authoring (Q3: both paths) - no silent auto-create

### 6.1 Promote-from-media (primary)
Matcher detects+decomposes+validates -> a **combined review row** in the B2b review surface -> human
confirms once -> alias persisted via seam (b). **Combined rows are ALWAYS shown, exempt from the
review surface's no-op hiding** (`ReviewRowViewModel.cs:149`): a combine where the source already used
`>` would derive to an identical string (a name no-op) yet still must be confirmed, because the human
is confirming the *combine* (coverage of multiple official entries), not a text diff. Confirmation is
gated on `CoveredEntryIndices.Count > 1`, not on a name change. (Slice 4.)

Why not auto-persist: deriving an alias from media then verifying against it is circular. The
confirmation is the human gate.

### 6.2 Setlist editor (secondary)
A gesture in `EditSetlistView` to mark a contiguous run of official entries as a combined alias,
through the same seam (b). (Slice 6.)

## 7. Display - P1 fix (slice 4)

A media track matched via a combine drives matched/review numbering off the alias's coverage (N media
tracks against N collapsed positions), so numbering is 1:1. NOTE: nothing today consumes coverage for
numbering - `ReviewRowViewModel.TrackNumberDisplay` (:54) reads the media track's own number - so the
1:1 fix needs new display wiring driven off `CoveredEntryIndices`. (Slice 4.)

## 8. Verification (corrected v4)

There is no hard match-gate on verification (`VerifyButton_Click` checks Artist + Date only). Matching
precedes verification in the workflow, so no unverify-on-match. A combined match's contribution is
**clearing the amber warning** (5.4) and **recording the combine** as an alias, so a correct album
stops showing a spurious unmatched warning. The existing one-click Verify is unchanged. (Q11.1 about
auto-vs-one-click is therefore moot - there is no auto path and no gate; one-click stands.)

Out of scope / unchanged: the banked follow-up that editing an already-verified album skips
`MaybeUnverifyAlbumEdit()`.

### 8.1 Concert verification vs alias writes (probe Q1)
`ConcertReference.Verified` is the concert file's own flag. An alias write is additive provenance, not
a change to canonical setlist content (Sets/Tracks untouched), so it does NOT unverify the concert.
Seam (b) preserves `Verified` (whole-object write; the Edit diff-baseline ignores fields outside
`ConcertSnapshot.Project`, which `aliasSetlists` is).

## 9. Scope boundaries (v1)
- Per-concert only. - Contiguous in-order runs only (5.2). - No silent auto-create.
- Official releases and tapers share one mechanism (`/` vs `>` is just a separator).
- Cross-set-boundary runs: Section 11.

## 10. Implementation slices

- **Slice 0 (read-only probe): DONE.** Folded into 5.1 and 4.
- **Slice 1: DONE (`ab15b1c`).** Additive `AliasSetlists` model + round-trip + 5 tests.
- **Slice 2: DONE (`ca873c7`).** `CombinedTrackDecomposer` (atomicity guard, split, contiguity) + 12
  tests.
- **Slice 3 (next): matcher integration.** Two-pass `ComputeProposals` (5.3): claim-aware decomposer
  overload (pure + tests) + pass-2 wiring building combined proposals (derived `>` name, last-entry
  segue, whole-run claim) + amber clearing via `Apply` (5.4). Unit tests for the overload and the
  two-pass behavior; **WPF manual gate** (first behavior change visible on screen). Doc: this spec
  revision lands with it if not already committed.
- **Slice 4: review-surface wiring.** Combined rows always shown (6.1, exempt from no-op hiding) +
  1:1 coverage-driven numbering (7) + promote-from-media confirm-to-register. WPF gate.
- **Slice 5: alias persistence.** `PersistAliasSetlist` seam (b). WPF gate.
- **Slice 6: setlist-editor authoring** (6.2). WPF gate.

## 11. Open questions for Gregg

Resolved: 11.1 (one-click; moot - no gate); probe Q1 (no concert unverify); probe Q2 (guard via
normalization); 11.3 (derive `NewSongName`, join with `>`, always); combined rows always shown;
two-pass structure. Remaining (slice-local):

1. **Cross-set-boundary combines.** Allow a combine run to span a set boundary, or restrict v1 to
   within-set runs? Lean: within-set for v1; flag cross-set for review.

---

### Decision summary (settled)
Reference-based alias entries (covered indices only); stored in the concert file via seam (b);
combined tracks first-class; normalization-based atomicity guard before any split; two-pass matcher
(direct wins, combines fill gaps, claim whole run); combined `NewSongName` derived by joining covered
official names with " > "; combined match clears amber (no verify gate exists or is added); combined
review rows always shown for confirmation; two authoring paths; per-concert scope.
