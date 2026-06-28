# Alias Setlists — Spec (for review) — v3

Status: DRAFT, ready to commit pending Gregg's glance. Schema additive only.
Revision history:
- v2: Gregg's audit Q2-Q6 answers (additive storage; both authoring paths; internal segue not
  required for a single combined file; match -> verifiable, prerequisite arc dropped; per-concert).
- v3: slice-0 probe folded. Separators are NOT safe bare splitters -> normalization-based atomicity
  guard (5.1). Write seam (a) is unreachable from import -> seam (b) on ConcertLookupService is
  mandatory (4). PathGuard does not gate concert writes; AppData atomic temp+rename is the
  discipline. Alias write does not unverify the concert (8.1). Open questions 11.1 (one-click) and
  the probe's Q1/Q2 resolved.
Arc type: generative. Grounding: Phase A audit + slice-0 probe at `bd56b19`. Repo authoritative;
slice 1 reopens a narrow read-only Phase A before any edit.

---

## 1. Problem

Combined tracks are a legitimate, recurring representation, not an error. Official releases use
them deliberately: *One From The Vault* (ASIN B000002VK3) lists 4 combined tracks of 16
("Help On The Way/Slipknot!", "King Solomon's Marbles/Stronger Than Dirt", "Crazy Fingers/Drums",
"Blues For Allah/Sand Castles"). Tapers use them too, with `>` instead of `/`.

The matcher is name-gated with no positional fallback (`SetlistMatcher.cs:136-137`, confirmed
`:144`). A combined media track resolves to one canonical key that equals no single official
entry, so it stays unmatched/amber and the album can't verify. Two concrete harms:

- **P1 - numbering drift.** A 15-track combined album maps against a 16-entry official setlist;
  numbers misalign and skip (visible in the *One From The Vault* screen). A human comparing
  "both verified" sees mismatched counts.
- **P2 - can't verify a faithful combine.** A correct combined pressing can never be marked
  verified, defeating "import once, verify once, never again."

## 2. Core idea (resolves the audit's split-vs-collapse fork)

Split and collapse are the same operation at two times, not opposites:

- **Decomposition (the "split" engine)** runs at match time: split the combined media name on a
  separator, normalize each component to an official title, check the components form a
  **contiguous run** of official entries `[i..j]`. Output: `CoveredEntryIndices = [i..j]`.
- **The alias (the "collapse" record)** persists that confirmed run so re-imports match instantly
  and verification is durable. Output: a stored alias entry = the same `[i..j]`.

Same field, same semantics, same direction. The fork dissolves.

## 3. Reference-based by decision

An alias entry stores **only the official entry indices it covers** - nothing flattened. Display
name and match key are **derived** from the covered official entries, never duplicated.

### 3.1 Internal segue (simplified per Q4)

A combined entry that is **one media file** has no track boundary inside it, so there is no
internal segue to preserve or break. The internal segue is therefore **informational only**
(derivable from the covered official entries' `Segue` bools for display), never something the B2b
guard must act on. The guard governs only the **trailing/boundary** segue of the combined entry -
the transition to the *next* media track.

### 3.2 Schema (additive on `ConcertReference`)

```jsonc
// ConcertReference - new field; absent on legacy files => empty list on read.
"aliasSetlists": [
  {
    "id": "string",      // stable id, optional; provenance/debug
    "label": "string",   // optional source note, e.g. "One From The Vault"; default ""
    "entries": [
      { "coveredOfficialIndices": [1, 2] },   // contiguous run over FLATTENED official positions
      { "coveredOfficialIndices": [10, 11] }
    ]
  }
]
```

Sparse overlay: records only the combined runs. Effective alternate setlist = official with the
listed runs collapsed; singleton runs (`[i]`) are implicit and never stored. C# model mirrors the
existing `ConcertReference` convention (plain mutable class; not sealed; `{ get; set; }`).

## 4. Persistence - seam (b), AppData atomic write

Aliases live in the concert file alongside the official setlist (Q2: additive). Decision (slice-0
probe): a focused persist method on `ConcertLookupService`:

```
PersistAliasSetlist(date, aliasEntry):
  concert = GetConcertByDate(date)            // ConcertLookupService.cs:160 (cached ref)
  concert.AliasSetlists ... append entry
  json = CanonicalJson.Serialize(concert)     // CanonicalJson.cs:27, standalone, reusable
  atomic temp+rename into AppDataConcertsPath  // ConcertLookupService.cs:50
  NotifySaved(date, concert)                   // :184, keep cache coherent
```

Why seam (b) and not "reuse EditSetlistView's writer": the import path holds only a **date**
string (`ImportView.xaml.cs:1135`), no `ConcertReference`, so the editor-writer seam is
structurally unreachable from import. Seam (b) is the only one **both** consumers reach (import has
the date; Edit has `_concert.Date`), and `ConcertLookupService` already owns the whole
load -> mutate -> atomic-write -> recache loop.

Discipline: writes target the AppData-first writable copy (`%APPDATA%\DeadEditor\concerts\`), atomic
temp+rename, matching the existing concert writer. **PathGuard does NOT apply** - it gates library
audio writes (Metadata/Fingerprint/Manifest) only; the concert writer deliberately does not call
it. (Spec v2 was wrong on this; corrected.)

## 5. Match predicate - "official OR any registered alias"

An album is fully matched when every media track matches AND every official entry is covered
exactly once, where a track matches by either:

1. **Direct** - canonical name equals a single official entry (existing path, unchanged), or
2. **Alias/combine** - decomposition reduces to a contiguous official run `[i..j]` that is a
   registered alias entry (instant), or a **valid-but-unregistered** run proposed for confirmation
   (Section 6).

Reuses the existing seam: in `ComputeProposals`, when the single-name gate fails
(`SetlistMatcher.cs:144`), run decomposition before `continue`; a combined match populates the
existing `TrackProposal.CoveredEntryIndices` with `[i..j]` (the field's scaffolded purpose). No new
field on `TrackProposal`.

### 5.1 Separator safety - normalization-based atomicity guard (slice-0 probe)

Bare separator splitting is UNSAFE: confirmed blockers are the official medley title
`Devil With the Blue Dress/Good Golly Miss Molly/Devil With the Blue Dress` (`songs.json:543`,
a real ownable song whose canonical name embeds `/`) and the alias
`I Know You Rider> High Time tease` (`songs.json:1121`).

Guard: decomposition attempts a separator-split **only when the full combined media string does
NOT itself resolve**, via `NormalizationService.GetOfficialTitle` (which already considers official
titles AND aliases), to a known official song. A whole string that resolves is **atomic** and never
split. This one rule covers both blockers and answers the probe's Q2 (titles and aliases unified
through normalization - no separate decision needed). Splitting is attempted only on strings that
resolve to nothing.

Candidate separators after the atomicity guard: `/`, `>`, `->`. Components are normalized via
`GetOfficialTitle`; comparison is separator-agnostic.

### 5.2 Validation gate (data integrity, non-negotiable)

A decomposition is accepted only if every component normalizes to a real official song AND those
songs form a contiguous run in official order. Anything else - reordered, non-adjacent, a component
that doesn't normalize - stays unmatched/amber. **Never fabricate a match or a song name.**
Setlist-level analogue of the `AddAlias` short-circuit golden rule.

## 6. Authoring (Q3: both paths) - no silent auto-create

### 6.1 Promote-from-media (primary)
1. Matcher detects a combined media track, decomposes it, validates the contiguous run (5.2).
2. Valid-but-unregistered run -> a **proposed combine row** in the existing B2b review surface,
   showing covered official entries and the resulting 1:1 numbering (P1 fix).
3. Human confirms once. **Only then** is the alias persisted via seam (b).
4. Future imports of the same combine match instantly - verify once, never again.

Why not auto-persist: deriving an alias from media then verifying the media against the
just-derived alias is circular. Review-surface confirmation is the human gate, mirroring existing
review-before-apply discipline.

### 6.2 Setlist editor (secondary)
A gesture in `EditSetlistView` to mark a contiguous run of official entries as a combined alias.
Writes through the same seam (b). Useful when authoring ahead of import.

## 7. Display - P1 fix

When a media track matches via an alias/combine, the matched/review view drives numbering off the
alias's coverage (N media tracks against N collapsed positions), so numbering is 1:1 and counts
agree. The official view remains the canonical authority; the matched view uses coverage. This is
why coverage must be persisted independent of the matcher re-running.

## 8. Verification (simplified per Q5; prerequisite mini-arc dropped)

Matching precedes verification in the workflow, so there is no "verified-then-matched" case and no
unverify-on-match. An alias match is another way to reach fully-matched. A fully-matched album
(direct or via alias) becomes eligible for the existing **one-click** `VerifyButton_Click`
(Q11.1: one-click, not auto-flip - preserves "verify once" as a deliberate human act and avoids
changing the existing direct-match path).

Out of scope / unchanged: the banked follow-up that *editing* an already-verified album skips
`MaybeUnverifyAlbumEdit()` (post-verification edit case, orthogonal); stays banked.

### 8.1 Concert verification vs alias writes (probe Q1)

Distinct from the album manifest above, `ConcertReference.Verified` is the concert-file's own flag.
An alias write is **additive provenance**, not a change to canonical setlist content (Sets/Tracks
untouched), so it **does NOT unverify the concert**. Seam (b) preserves `Verified` naturally
(whole-object write, `NotifySaved` does not disturb it; the Edit diff-baseline ignores fields
outside `ConcertSnapshot.Project`, which `aliasSetlists` is).

## 9. Scope boundaries (v1)

- **Per-concert only** (Q6). Shared/templated aliases out of scope; promote-from-media auto-derives,
  so per-show cost is ~one click. Revisit only if tedious.
- **Contiguous in-order runs only.** Reordered/non-adjacent combines stay amber (5.2).
- **No silent auto-create.** Confirmation via review surface or explicit editor action (6).
- **Official releases and tapers share one mechanism** - `/` vs `>` is just a separator (5.1).
- Cross-set-boundary runs: see Section 11 open item.

## 10. Implementation slices (small, gated, one concern each)

- **Slice 0 (read-only probe): DONE.** Findings folded into 5.1 and 4.
- **Slice 1:** model - additive `AliasSetlists` on `ConcertReference` + serialize/deserialize
  round-trip + unit tests. Pure logic, unit-gated. Opens with narrow read-only Phase A confirming
  `ConcertSnapshot.Project` field list and that `aliasSetlists` sits outside it.
- **Slice 2:** decomposition engine - atomicity guard (5.1) + separator split + normalize +
  contiguity validation (5.2) -> candidate `[i..j]`. Pure, unit-tested. Reuses `NormalizationService`.
- **Slice 3:** matcher integration - consult registered aliases + run decomposition in
  `ComputeProposals` when the single-name gate fails; populate `CoveredEntryIndices = [i..j]`;
  album-level "fully matched = official OR alias" predicate. Pure logic + tests.
- **Slice 4:** review-surface wiring - combined proposal rows with coverage + 1:1 numbering (P1);
  promote-from-media confirm-to-register. WPF manual gate.
- **Slice 5:** alias persistence - `PersistAliasSetlist` on `ConcertLookupService` per seam (b)
  (AppData atomic write, no PathGuard). WPF manual gate (import -> confirm -> round-trip).
- **Slice 6:** setlist-editor authoring (6.2) - manual combine gesture in `EditSetlistView` reusing
  seam (b). WPF manual gate.

## 11. Open questions for Gregg

Resolved: 11.1 one-click verification; probe Q1 (no concert unverify on alias write); probe Q2
(guard checks titles+aliases via normalization). Remaining (both slice-local, decide later):

1. **Cross-set-boundary combines.** Allow a combine run to span a set boundary, or restrict v1 to
   within-set runs? Lean: within-set for v1; flag cross-set for review.
2. **Display separator for a derived combined name.** Join covered official names with `>` when the
   boundary segue is true and `,`/`/` otherwise, or mirror the source separator? Low stakes given
   3.1. Lean: segue-aware `>`/`,`.

---

### Decision summary (settled)
Reference-based alias entries (covered indices only); stored in the concert file via seam (b)
(`ConcertLookupService.PersistAliasSetlist`, AppData atomic write, no PathGuard); combined tracks
first-class; normalization-based atomicity guard before any separator split; two authoring paths
(promote-from-media primary, setlist editor secondary); validated-contiguous and confirmed before
persisting; internal segue informational only for single-file combines; P1 fixed by coverage-driven
display; match -> one-click verifiable, no unverify-on-match, no concert unverify on alias write;
per-concert scope for v1.
