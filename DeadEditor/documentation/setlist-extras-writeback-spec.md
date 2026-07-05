# Canonical Setlist Extras + Recording-to-Canon Write-Back — Design Spec

_Status: **ratified design** — decisions D1–D9 resolved 2026-07-03 (§11); the body reads as settled
design and is ready to slice. Document-before-implement per the handbook. This spec introduces
**typed setlist entries** (songs vs extras such as tuning / false starts) to the
`Data/concerts/*.json` canonical model, defines matching and verification against a typed setlist,
and specifies a **recording-to-canon write-back** policy (P1–P3) so a discovery on one recording
(e.g. a Ripple false start) can flow back to the shared canonical setlist that every recording of the
date reads. It also **unbanks the Edit-side "Edit setlist" deep-link** as a design requirement. This
spec is the **canonical home** of the write-back policy (formerly a chat-side "P1–P3" / "KnownExtras"
handoff that never lived in the repo). Companion to
[reference-side-panel-spec.md](reference-side-panel-spec.md) (esp. §7.5 allow-assign-onto-resolved,
§7.5.2 recognized-but-unclaimed / off-list track classes, §9 Edit-reconstruction fact). File:line
citations verified against HEAD `911247d`; **reconciled 2026-07-05 against `dfa5673`** after the
side-panel 5b click-assign (`7b2d635`) and slice-6 DnD (`dfa5673`) commits landed — see the
Phase-A reconciliation amendments in §2.2, §4, §5, §6, §10, and D10–D12 (§11). D1–D9 are unchanged._

---

## 1. Problem & motivating case

A live recording routinely contains **events the setlist does not list as songs**: tuning passages,
crowd/banter, applause, and **false starts** (a song begun, aborted, then restarted). These are real
historical events of the *concert*, but the canonical setlist — sourced from setlist.fm via
`tools/SetlistFetcher` — records only the *songs*. The result: a faithful recording can never fully
match or verify against its own concert's setlist, because some of its tracks have no canonical home.

**Motivating case — 1971-02-21 (Capitol Theatre).** The canonical file
([Data/concerts/1971-02-21.json](../Data/concerts/1971-02-21.json)) has **23 song entries** (12 in
Set 1, 11 in Set 2; "Ripple" is one song entry at flattened position 8). A common recording of this
date has **26 tracks**: the 23 songs **plus two tuning passages and a second "Ripple" that is a false
start** preceding the real Ripple. Under today's model those three tracks are unmatchable:

- The tuning tracks normalize to the canonical song "Tuning" (it exists in `songs.json`) but there is
  **no setlist position** for them → off-list (reference-side-panel-spec.md §7.5.2 class 2).
- The false-start "Ripple" normalizes to canonical "Ripple" (`IsMatched == true`) but the one Ripple
  setlist position is already claimed by the real Ripple → **recognized-but-unclaimed**
  (reference-side-panel-spec.md §7.5.2 class 1).

So the album sits permanently at 23/26 matched and cannot reach a "complete" match or a verified
state, no matter how correct it is.

**Principle.** The false start and the tuning **belong to the concert, not to any one recording** —
they are historical facts about what happened on stage that night. There is **one canonical setlist
per concert**, and its entries **carry a type**. Once the concert's canonical setlist records the
false start and the tuning as typed extras, *every* recording of that date can place those tracks,
and the album can complete and verify. The workflow that gets a discovery from a recording into the
canonical setlist is the **write-back** policy (§6).

---

## 2. Current model (Phase A — read-only findings)

### 2.1 Concert JSON schema
[Models/ConcertReference.cs](../Models/ConcertReference.cs). A concert file carries identity/venue
fields (`date`, `venue`, `city`, `state`, `country`, `setlistFmId`, `setlistFmUrl`, `lastUpdated`),
flags (`hasSetlist`, `multiShow`, `verified`), and **two parallel setlist representations**:

- **`sets[]`** (`ConcertSet` → `ConcertSong { Name, Date, Segue, Info }`) — the nested, set-grouped
  form. **This is the read path** for setlist projection (§2.2).
- **`tracks[]`** (`ConcertTrack { Position, SongName, Date, Segue, Set }`) — a **flattened, redundant
  projection** of the same songs with an explicit 1-based `Position` and set label. `SongCount =>
  Tracks.Count` ([ConcertReference.cs:85](../Models/ConcertReference.cs)). Written alongside `sets[]`
  by the editor's save projection.
- **`aliasSetlists[]`** (`AliasSetlist` → `AliasEntry.CoveredOfficialIndices`) — reference-based
  combine aliases; each entry stores **covered official entry indices over flattened positions**, not
  names (alias-setlists-spec.md §3.2). Empty-omitted on write
  ([ConcertReference.cs:49](../Models/ConcertReference.cs)); **not** copied into the verification diff
  baseline (§2.3).

Serialization is camelCase via `CanonicalJson`. **Finding:** `sets[]` and `tracks[]` duplicate the
same song sequence in two shapes that must stay in lockstep — a `type` field is represented
consistently across **both** shapes (D8a, §3.2).

### 2.2 `SetlistProjection.Build` output + consumers
The read chain is: `ConcertLookupService` → `ConcertSetlistAdapter.ToSetInfoList` (reads **`Sets`**,
not `Tracks`; [ConcertSetlistAdapter.cs:40-63](../Helpers/ConcertSetlistAdapter.cs)) →
`ShowLookupService.GetSetlist` → `SetlistProjection.Build` → `IReadOnlyList<SetlistEntryVm>`.
`SetlistEntryVm { Name, Canonical, Position, Segue, SetLabel }`
([Models/SetlistEntryVm.cs](../Models/SetlistEntryVm.cs)) carries **no type**; `Position` is a single
global 0-based index across all sets — **the claim axis**
([SetlistProjection.cs:37-51](../Helpers/SetlistProjection.cs)).

Consumers of the projection / flattened setlist:
- **Panel** — `SetlistReferencePanel.SetSetlist(entries, claimedPositions)` renders one row per entry;
  dims claimed positions ([SetlistReferencePanel.xaml.cs](../Views/SetlistReferencePanel.xaml.cs)).
- **Matcher** — `SetlistMatcher.ComputeProposals` / `MatchAndDecorate`: **name-gated, first-unclaimed**
  (`trackCanonical == entry.Canonical`), producing `ClaimedPositions : HashSet<int>` of flattened
  positions ([SetlistMatcher.cs:151-247](../Services/SetlistMatcher.cs) `ComputeProposals`;
  `MatchAndDecorate` at :124). No positional fallback; a track that matches no unclaimed entry is
  left untouched (never mis-named — follow-ups.md "setlist/media divergence is data-safe"). Since
  `7b2d635` the result also carries `MatchResult.ClaimsByTrack` ([SetlistMatcher.cs:53, 299-302](../Services/SetlistMatcher.cs))
  — the per-track primary claimed index (first covered index) the host stamps onto the VM
  back-reference below.
- **Review dialog** — `MatchReviewViewModel` / `ReviewRowViewModel` present per-track proposals.
- **Match-to-Song list** — `BuildUnmatchedSetlistSongList` enumerates **unclaimed** setlist songs for
  the right-click dialog (both surfaces).
- **`GetDiscTrack`** — maps a flattened position back to (set, track-within-set) for set labels.
- **Per-track claim back-reference (post-5b claim-axis machinery, `7b2d635`).** The claim axis is no
  longer just the matcher's `ClaimedPositions` set: each track VM now carries a transient
  `TrackInfoViewModel.ClaimedSetlistPosition` ([TrackInfoViewModel.cs:184](../Models/TrackInfoViewModel.cs))
  — the flattened position that track currently claims, or null — stamped by **every** claim path
  (Match-Setlist run via `MatchResult.ClaimsByTrack`, right-click Match-to-Song, and panel/DnD assign),
  reset before each run and cleared when the track becomes unmatched. Placement availability is gated
  on the `_matchSetlistHasRun` run-state signal only for the amber unplaced-row highlight
  ([EditMetadataView.xaml.cs:59](../Views/EditMetadataView.xaml.cs); [ImportView.xaml.cs:75](../Views/ImportView.xaml.cs));
  **panel assignment itself is available whenever the panel is populated, not gated on a match run**
  (reference-side-panel-spec.md §7.5). **D9a obligation:** because extras occupy flattened `Position`
  slots, the slice-4 atomic remap MUST keep every live `ClaimedSetlistPosition` coherent when an extra
  insert / removal / reorder shifts positions — a stamped back-reference left pointing at a shifted
  index silently un-dims or mis-frees the wrong entry. This is slice 4's highest-risk obligation (§3.3).

### 2.3 What "verified" means today
`ConcertReference.Verified` is a **bare user attestation** that the concert's data is correct
([ConcertReference.cs:29](../Models/ConcertReference.cs); concert-verification-spec.md). It gates:
- **`SetlistFetcher` skip-verified** — the fetcher will not overwrite a verified concert.
- **`ConcertDatabaseView`** — leading verified glyph column.
- **Diff-at-save unverify** — `EditSetlistView` captures a `_baselineJson` via
  `ConcertSnapshot.Project` → `Serialize` at load, and on save re-projects and compares; **any content
  change unverifies** (concert-verification-spec.md decision 4). `ConcertSnapshot.Project` populates
  **only editor-controlled content** — Date, Venue, City/State, Sets, Tracks, HasSetlist — and
  deliberately **excludes** `LastUpdated`, `Verified`, and `AliasSetlists`
  ([ConcertSnapshot.cs:25-90](../Helpers/ConcertSnapshot.cs)).

Separately, the **album/Edit** surface has its own `Verified` flow (`EditMetadataView`, the
`_isVerified` badge + Verify button), governed by album-level verification, distinct from the
concert-record `Verified`. This spec must specify how a typed-setlist "complete match" relates to
**both** (§5).

### 2.4 Where a per-entry `type` field flows (resolved routing)
- **Serialization** — a new field on **both** `ConcertSong` (nested) and `ConcertTrack` (flattened);
  default `"song"`; empty-omit when `"song"` to keep legacy files byte-clean (D8a, §3.2).
- **Adapter** — `ConcertSetlistAdapter` maps `ConcertSong` → `SetlistSong`; carries `type` through so
  the projection can expose it (`SetInfo`/`SetlistSong` gain a type).
- **Projection** — `SetlistEntryVm` gains `Type`; extras **consume flattened `Position` slots on one
  axis, exactly like songs** (D9a, §3.3), so `Position` accounting is unchanged in shape.
- **Matcher** — knows which positions are song-typed to compute "complete" (§4); extras are claimable
  by **manual / panel assignment only, never auto-matched** (D2, §4).
- **Panel / review dialog** — render type (badge / muted style) and treat unclaimed extras as *not*
  incomplete.
- **Claim accounting** — `ClaimedPositions` stays position-indexed; "complete" is redefined over the
  song-typed subset (§4).
- **Verification diff** — `type` lands in `ConcertSnapshot.Project`'s serialization, so a type edit
  unverifies (D3, §5).

---

## 3. Typed entries

### 3.1 Type vocabulary (D1 → ratified)
Ratified vocabulary (a single lowercase enum stored per entry). The "Auto-matched?" column reflects
D2 — auto-match only ever considers `song` entries (§4):

| Type | Meaning | Auto-matched? |
|---|---|---|
| `song` | a performed song (default) | yes (existing behavior) |
| `tuning` | tuning / noodling between songs | no — manual / panel only |
| `false-start` | a song begun then aborted, usually before the real take | no — manual / panel only |
| `banter` | stage talk / crowd interaction with no song | no — manual / panel only |
| `other-extra` | catch-all non-song event (soundcheck fragment, technical stop) | no — manual / panel only |

**Default = `song`.** Absent field reads as `song`, so every existing concert file is valid unchanged
(§3.2). The vocabulary is **ratified** (D1, §11) and deliberately **extensible**: as importing reveals
new kinds, add a type rather than forcing a bad fit — but with **minimal extension bias**, preferring
`other-extra` over coining a new label unless the new kind recurs and earns its own handling.

### 3.2 Serialization & backward compatibility
- New field `type` on the entry, default `"song"`, **empty-omitted when `"song"`** via a
  `ShouldSerialize` guard (mirroring `ShouldSerializeAliasSetlists`,
  [ConcertReference.cs:49](../Models/ConcertReference.cs)) so untouched legacy files stay byte-clean.
- A missing `type` deserializes to `song` (the model default) — **no migration required to read old
  files** (§8).
- **`type` lands in both shapes (D8, §11 → 8a).** `type` is written to **both** `sets[]`
  (`ConcertSong`) and `tracks[]` (`ConcertTrack`) and kept in lockstep by `ConcertSnapshot.Project`,
  matching today's dual write with no consumer churn. Collapsing the `sets[]`/`tracks[]` redundancy
  (making `tracks[]` a derived projection) is banked as a separate cleanup (§12).

### 3.3 Interleaving, position & claim accounting (D9 → 9a)
Extras are **positionally interleaved** with songs in performance order (a false start immediately
precedes its real take; tuning sits between the songs it separated). The claim axis is a **single
flattened `Position` including extras** — extras occupy `Position` slots exactly like songs, so the
panel renders in order and the matcher claims by position on one integer axis (the panel, matcher, and
`GetDiscTrack` are unchanged in shape).

**Mandatory atomic alias remap.** Because `AliasEntry.CoveredOfficialIndices` are positional
(§2.1), any extra **insertion, removal, or reorder** that shifts a covered index MUST remap the
affected `aliasSetlists[]` indices **in the same atomic operation** — a save projection that renumbers
positions and remaps covered indices together, never one without the other. A leftover un-remapped
alias index silently points at the wrong entry, so the remap is a first-class, separately tested
component (§10, slice 4) and **no extra-editing path may ship before it**. This is the load-bearing
structural choice of the arc, resolved by explicit deferral to the architect recommendation (D9, §11).

**Position is internal bookkeeping only.** A flattened canonical `Position` (song or extra) is setlist
bookkeeping and is **never** written to a media file's track number or filename — recording-side
track numbering is a hard non-goal of this arc (§12).

---

## 4. Matching policy for extras

Extras are **optionally claimable by manual or panel assignment only**, never *required*:

- **Auto-match NEVER claims extras (D2, §11).** The name-gated matcher considers only `song`-typed
  entries; an extra entry is never auto-claimed, even when a track's canonical title matches it (e.g.
  a "Tuning" track does **not** auto-claim a `tuning` entry). This preserves the matcher's
  never-place-wrongly guarantee — several like-named extras could otherwise bind the wrong one.
- **Manual / panel assignment** may claim an extra exactly as it claims a song. Since `7b2d635`
  (click) and `dfa5673` (DnD) the manual / panel / DnD path routes through a **per-surface
  `AssignEntryToTrack(vm, position)`** ([EditMetadataView.xaml.cs:1809](../Views/EditMetadataView.xaml.cs);
  [ImportView.xaml.cs:1033](../Views/ImportView.xaml.cs)) → **`ManualMatchApply.Reassign`** (free the
  track's prior claimed position, claim the new one) → **`ManualMatchApply.Apply`** as the inner write
  ([ManualMatchApply.cs:59-71](../Helpers/ManualMatchApply.cs)). The design intent is **unchanged and
  now stronger**: one unified, unit-tested seam that writes over `TrackInfo` + the claimed set with
  **no song/extra distinction** — an extra claims through the identical code path a song does
  (reference-side-panel-spec.md §7.5).
- **Unclaimed extras do NOT count against a complete match.** They are optional placement targets, not
  obligations.

**Definition — "complete match" = every `song`-typed entry is claimed.** Extra-typed entries are
excluded from the completeness denominator. The matcher's `ClaimedPositions` stays as-is; completeness
is computed as: `songPositions ⊆ ClaimedPositions` where `songPositions` is the set of flattened
positions whose entry type is `song`.

**Interaction with the recognized-but-unclaimed / off-list classes.** Once the canonical setlist
carries a `false-start` Ripple entry and two `tuning` entries, the motivating 1971-02-21 tracks stop
being structurally unreachable: the false-start Ripple has its own (extra-typed) position to claim,
and the tuning tracks have theirs. Panel assignment (reference-side-panel-spec.md §7.5.1, ALLOW onto
resolved tracks) is the placement gesture.

---

## 5. Verification semantics against a typed setlist

Two related notions, kept distinct:

- **Verified setlist (concert record).** The concert's `Verified` attestation (§2.3) is unchanged in
  meaning — "this canonical setlist is correct" — but its *content* now includes typed extras. Adding
  or typing an entry is a content edit and therefore participates in the diff-at-save unverify
  (D3, §11).
- **Verified match (a recording/album).** A recording is **fully matched when all `song`-typed entries
  are claimed**; extras are verified where claimed and **ignored where the recording does not contain
  them** — a soundboard that cut the tuning is still complete. Completeness is **surfaced** on the
  album Verify surface (`EditMetadataView`) as guidance — e.g. a "23/23 songs placed, 3 extras"
  readout — but does **not gate** album Verify, which stays an independent user attestation (parity
  with today) (D4, §11). This lets an archivist verify an album whose recording legitimately lacks an
  extra.

**Type edits unverify (D3, §11).** A `type` edit is a real change to the canonical record, so `type`
enters the `ConcertSnapshot.Project` serialization on its projected `ConcertSong`/`ConcertTrack`:
retyping an entry participates in diff-at-save unverify exactly as any content change does. Silently
keeping a concert "verified" across a structural retype would violate the P1 principle (§6).

**Every entry carries a non-empty label — including extras (D10, §11).** Every setlist entry, songs
and non-song extras alike, carries a non-empty `SongName` label (e.g. `"Tuning"`, `"Banter"`,
`"Crowd/Stage Talk"`, `"Ripple"` for the false start). A descriptive label is **not fabricated data** —
it is the display string the panel, editor grid, and Concerts grid already require for every row, and
naming the event ("Tuning") is a truthful description of what the extra *is*. This keeps
`ConcertVerifyGate` ([ConcertVerifyGate.cs:29-50](../Helpers/ConcertVerifyGate.cs)) **pure and
untouched**: its existing "every track has a `SongName`" rule composes with typed extras as-is, and a
`banter`/`other-extra` entry left nameless can no longer wedge a concert between "has an extra" and
"can be verified". _Considered, rejected: a type-aware gate exemption that lets non-`song` entries have
an empty name — adds a type branch to a pure gate to buy the ability to store a label-less row nothing
can display._

---

## 6. Recording-to-canon write-back policy (P1–P3)

The write-back is how a discovery on a **recording** (Import or Edit) reaches the **canonical
setlist** so all recordings of the date benefit. The three policies below are **ratified** (D5/D6/D7,
§11).

- **P1 — never silently change a VERIFIED setlist (invariant, D5).** A verified concert setlist is a
  ratified fact; a recording may *disagree* with it (extra tracks, different segues) but must **never
  silently rewrite it**. Any change to a verified setlist requires **explicit review** — the write-back
  offer is shown, the user opts in, and the change routes through the setlist editor (which then
  re-runs its own diff-at-save, unverifying pending re-attestation). This mirrors the `SetlistFetcher`
  skip-verified guard already in place.

- **P2 — unverified setlists accept write-back VISIBLY, never silently (D6).** When the canonical
  setlist is *unverified*, a recording that reveals a missing extra (or a segue correction) surfaces a
  non-blocking **offer** ("this recording has 2 tracks not in the setlist — add as extras?") that the
  user accepts/dismisses; on accept, apply and stamp `lastUpdated`. Write-back is **never silent** —
  every canon change is user-intended and reviewable, which matches the app's confirm-before-mutate
  grain and keeps the canon trustworthy against crowd-sourced recording metadata.

- **P3 — import against an unverified setlist whose concert details were updated → prompt "update and
  verify?" (D7).** When an import both (a) matches against an unverified setlist and (b) the user has
  supplied corrected concert details (venue, a segue, an extra), offer a single combined **"update the
  canonical setlist and mark it verified?"** prompt, so the act of carefully importing a recording is
  also the act of ratifying the canon. Diff-gated: it fires only when there is a real diff to apply
  (no empty prompts).

**The two-Ripples flow through this policy.** User imports the 1971-02-21 recording → the false-start
Ripple and two tuning tracks are unplaceable (§1). User recognizes the false start → **adds a
`false-start` "Ripple" entry** (and two `tuning` entries) to the canonical setlist, either directly in
the setlist editor (§7) or by accepting a P2-visible / P3 write-back offer. Because there is one
canonical setlist per concert, **every** recording of 1971-02-21 now places those tracks and can
complete. If the concert was already verified, P1 forces the change through explicit review before it
lands.

### 6.1 Scope of what write-back propagates

**Segue write-back (recording → canon) is BANKED (D11, §11).** This arc's write-back propagates
**typed extras** (missing tuning / false-start / banter entries), not segue corrections sourced from a
recording. There is **no designated recording-side source of truth for segue**: the assign seam is
**set-true-only** (`ManualMatchApply.Apply` only ever sets `track.Segue = true`, never clears —
[ManualMatchApply.cs:42-43](../Helpers/ManualMatchApply.cs)), so a recording could never propagate a
segue *removal*, and canon→track is the only direction wired today. The lived-demand case — the
1971-03-03 Set 2 segues entered this week after hearing the recording's continuous flow — was **editor
authoring** on the canonical `ConcertSong.Segue` / `ConcertTrack.Segue` fields ([EditSetlistView.xaml.cs:701-707](../Views/EditSetlistView.xaml.cs)),
which **is in scope and supported** via §7. Slice 7 is therefore **not** assumed to deliver
recording-sourced segue write-back; if concrete demand for it surfaces, it earns its own decision
(source of truth, un-segue semantics) before slicing. _Considered, rejected: folding segue propagation
into the P2/P3 offer now — underspecified source of truth and a set-true-only seam that can't express
the corrections users actually make._

**P2 / P3 accept routes through the setlist editor (D12, §11).** When the user accepts a P2-visible or
P3 write-back offer, the app **opens the setlist editor pre-navigated to the concert's date** with the
proposed extras staged, and the change lands via the editor's **existing atomic save + diff-at-save
review** ([EditSetlistView.xaml.cs:448-534](../Views/EditSetlistView.xaml.cs)): one write seam, the
verified→unverify P1 machinery (§5) applied for free, and the user sees exactly what canon will become
before it persists. A **direct `PersistExtraEntry`-style append** (write an extra straight into the
concert file without opening the editor) is **BANKED**: `ConcertLookupService.PersistAliasSetlist`
([ConcertLookupService.cs:362](../Services/ConcertLookupService.cs)) is the transactional, dev-aware,
idempotent template for it, but any such seam is **gated on the slice-4 atomic alias remap** exactly as
the editor path is (an extra inserted ahead of a covered alias index corrupts stored combines with or
without the editor). _Considered, rejected: a direct-append accept path now — a second canon write seam
to build and test, no review-for-free, and the same slice-4 dependency, for a round-trip the editor
already makes cheap._

---

## 7. Setlist-editor changes

`EditSetlistView` ([Views/EditSetlistView.xaml.cs](../Views/EditSetlistView.xaml.cs)) gains typed-entry
authoring:

- **Type column / control** per row — a dropdown bound to the D1 vocabulary (§3.1); default `song`.
- **Add-extra** — add a row of a chosen type at a chosen position (interleaved, §3.3). The existing
  Add/Remove/Normalize/cell-edit change tracking (`OnEditorChanged` → `_structuralEditsSinceSave`)
  extends to type edits; a type change is a content edit that unverifies on save (§5, D3).
- **Reordering** extras with their neighbours uses the existing row ordering; the save projection
  (`ConcertSnapshot.Project`) reassigns contiguous positions
  ([ConcertSnapshot.cs:50-83](../Helpers/ConcertSnapshot.cs)) and, per D9a, performs the **mandatory
  atomic alias index-remap** (§3.3, slice 4) when an extra insert/removal/reorder shifts covered
  indices.
- **Type surfacing** — extras render visually distinct (muted row, a type chip) so the editor reads as
  "songs + annotations", not a flat list. Set assignment still uses the fixed 5-label axis
  (`SetChoices`, [EditSetlistView.cs:64-65](../Views/EditSetlistView.xaml.cs)); extras belong to the
  set they occur within.

The combine-alias controls' existing dirty-block (`CombineControlsEnabled` refuses combine edits while
structural edits are unsaved, [EditSetlistView.cs:70-78](../Views/EditSetlistView.xaml.cs)) already
guards the positional-index hazard extras introduce; type/extra edits must set
`_structuralEditsSinceSave` for the same reason.

---

## 8. Unbank the Edit-side "Edit setlist" deep-link (RATIFIED)

reference-side-panel-spec.md §9 banked the Edit-side "Edit setlist ↗" affordance because
`EditMetadataView` is reconstructed per entry ([ShellWindow.xaml.cs:469]) — a deep-link round-trip
from Edit to the setlist editor and back would drop in-flight album edits. **This arc ratifies
unbanking it**, turning the old blocker into a design **requirement**:

- The Edit → setlist-editor → back round-trip must **preserve the Edit session**. The design uses a
  **refresh-on-return hook** that re-reads the concert and re-projects the panel while keeping the
  album grid's in-memory edits intact (mirroring the Concerts grid's `RefreshFromCache`,
  reference-side-panel-spec.md §9 banked note): the setlist editor mutates the shared
  `ConcertReference` live, so on return only the panel projection is stale, and a scoped re-project
  (not a full view rebuild) preserves the album edit buffer. _Considered, rejected: guarding the
  deep-link behind an unsaved-changes check that saves/stashes album edits before navigating — heavier
  and interrupts the edit flow._
- **Import's convention stays as-is** — the user re-**Reads** after fixing a setlist and Read already
  re-invokes `RefreshSetlistPanelAsync` (reference-side-panel-spec.md §9); no new hook there.

This is the surface that makes the two-Ripples fix reachable from the Edit side, not just Import.

## 9. Migration / compatibility

- **No read migration needed.** Absent `type` deserializes to `song`; every existing
  `Data/concerts/*.json` is valid unchanged, and empty-omit-on-write keeps untouched files byte-clean
  (§3.2). A concert only gains `type` keys when a user actually types an extra.
- **Alias index-remap (D9a).** The one real migration hazard is inserting an extra ahead of a
  covered alias index on an alias-bearing concert (§3.3). This arc **requires** the editor's save
  projection to remap `AliasEntry.CoveredOfficialIndices` when an insert shifts them, with a
  characterization test; it does **not** require a batch pass over existing files.
- **Relationship to the AppData-vs-repo reconciliation / materialization-noise sweep.** The concert
  store is AppData-first with a bundled fallback and first-run copy (`ConcertLookupService`), and there
  is a known maintenance concern about materialization noise / AppData-vs-repo drift. **This arc asks
  nothing new of that pass beyond one thing:** the empty-omit `ShouldSerialize` guard on `type` (§3.2)
  must hold so the sweep does not see spurious `"type":"song"` keys appear on files a user merely
  opened. If that sweep normalizes concert files, it should treat absent-`type` and `type=="song"` as
  identical. Flagged as a coordination note, not a dependency.

## 10. Slice plan

One concern per commit; WPF manual gate on every UI-visible slice; pure helpers unit-tested.

1. **Typed model + serialization.** Add `type` (default `song`, empty-omit) to `ConcertSong` **and**
   `ConcertTrack` (D8a); extend `ConcertSetlistAdapter` + `SetlistProjection` + `SetlistEntryVm` to
   carry it; extend `ConcertSnapshot.Project` (D3). Unit tests: round-trip serialization, absent→song
   default, projection carries type, snapshot includes type. No UI yet.
2. **Matching + completeness.** Redefine "complete" over the song-typed subset (§4); extras are
   manual/panel-claimable only, never auto-claimed (D2). Unit tests on the completeness predicate +
   extra-claim exclusion from auto-match. Panel/matcher read-through.
3. **Panel + review-dialog typing.** Render type distinctly; unclaimed extras don't read as incomplete.
   WPF gate.
4. **Alias index-remap component (D9a — MANDATORY PREREQUISITE).** The pure, atomic alias-remap
   (§3.3) as a **first-class, separately unit-tested** helper: given a canonical-`Position`
   insertion / removal / reorder, remap every affected `AliasEntry.CoveredOfficialIndices` in one
   operation. This slice **owns** the remap. Unit tests: insert-before-covered shifts indices,
   insert-after leaves them, removal, reorder, multi-run alias, and no-alias no-op. **Gate on this
   slice: no extra-editing path (slice 5) may ship before it lands** — an extra insert without an
   atomic remap silently corrupts stored combines.
5. **Setlist-editor extras authoring** (§7). Type control + add/reorder extras, routing every
   position-shifting save through the slice-4 remap **atomically** (renumber + remap together, never
   one alone). **Depends on:** slice 4 (hard). WPF gate = add a `false-start` Ripple + two `tuning`
   entries to 1971-02-21, save, re-open, verify persistence **and** that an alias-bearing concert's
   combines survive the extra insert unshifted.
6. **Edit-side deep-link unbank** (§8) — refresh-on-return. WPF gate = Edit → edit setlist → back,
   album edits intact, panel refreshed.
7. **Write-back offers** (§6) — P2-visible + P3 prompt + P1 explicit-review routing. WPF gate = the
   full two-Ripples round-trip from a recording.

**Sequence reference — reference-side-panel arc slice 5b + 6 (LANDED).** Panel **click-to-assign**
(5b, `7b2d635`) and **drag-and-drop** (slice 6, `dfa5673`) are specified in
[reference-side-panel-spec.md](reference-side-panel-spec.md) §7.5/§10/§11.5 and are **NOT** part of
this arc's slices. They are the placement gesture this arc's typed extras make useful, and **both have
now landed** — so the sequencing precondition (a placement gesture must exist before extras are typed
and displayed) is **satisfied**, not pending. **This arc therefore starts cleanly at its own slice 1**;
the unified `AssignEntryToTrack → Reassign → Apply` seam (§4) is already in place for extras to claim
through. Global order from here: extras slice 1 → 2 → 3 → 4 (remap) → 5 (editor authoring, gated on 4)
→ 6 (Edit deep-link unbank) → 7 (write-back).

## 11. Decision record (RESOLVED 2026-07-03; addendum D10–D12 2026-07-05)

All nine core decisions were ratified 2026-07-03; the spec body above is written as settled design
against them. Three sub-decisions (D10–D12) were ratified 2026-07-05 during the Phase-A reconciliation
against `dfa5673` — they refine scope without re-opening D1–D9. Each rejected alternative is retained
here as a one-line "considered, rejected" note.

1. **D1 — type vocabulary — RESOLVED.** Ratified: `song` / `tuning` / `false-start` / `banter` /
   `other-extra` (§3.1). **Rider:** the vocabulary is explicitly **extensible** as importing reveals
   new kinds, with **minimal extension bias** — prefer reusing `other-extra` over coining a new type.
2. **D2 — auto-match extras? — RESOLVED: never.** Auto-match **never** claims extras — manual and
   panel assignment only, not even opt-in (§4). _Considered, rejected: auto-attempting extras by title
   (even opt-in) — risks binding the wrong one of several like-named extras and erodes the matcher's
   never-place-wrongly guarantee._
3. **D3 — type edits unverify? — RESOLVED: yes.** `type` enters `ConcertSnapshot.Project`, so a retype
   participates in diff-at-save unverify (§5).
4. **D4 — verified-match definition + album-Verify interaction — RESOLVED.** Complete/verified match =
   all `song`-typed entries claimed, extras optional; completeness is **surfaced** on album Verify but
   does **not gate** it (§5). _Considered, rejected: gating album Verify on completeness — would block
   verifying a recording that legitimately lacks an extra._
5. **D5 — P1 — RESOLVED: invariant.** Never silently change a verified setlist; explicit review
   required (§6).
6. **D6 — P2 — RESOLVED: VISIBLE.** Write-back to an unverified setlist is **offered**, never silent
   (§6). _Considered, rejected: P2-silent auto-apply — opaque provenance and a foot-gun on
   crowd-sourced recording metadata._
7. **D7 — P3 — RESOLVED.** Ratified as drafted: import against an updated unverified setlist prompts
   "update and verify?", diff-gated (§6).
8. **D8 — `sets[]`/`tracks[]` `type` placement — RESOLVED: 8a.** `type` in **both** `sets[]` and
   `tracks[]`, kept in lockstep by `ConcertSnapshot.Project` (§3.2). _Considered, rejected (banked
   §12): 8b sets-authoritative / tracks-derived — larger consumer churn, deferred as a standalone
   cleanup._
9. **D9 — claim-axis model — RESOLVED: 9a.** Ratified by explicit deferral to the architect
   recommendation: a **single flattened claim axis including extras**, with **mandatory, atomic** alias
   index-remap on any extra insertion / removal / reorder (§3.3, owned by slice 4 §10). _Considered,
   rejected: 9b separate song-position sub-sequence — two-axis complexity across
   panel / matcher / `GetDiscTrack`._
10. **D10 — extras carry a non-empty label — RESOLVED: yes (2026-07-05).** Every entry, extras
    included, has a non-empty `SongName` label; `ConcertVerifyGate` stays pure and untouched (§5).
    _Considered, rejected: a type-aware gate exemption allowing nameless non-`song` entries — adds a
    type branch to a pure gate to store a row nothing can display._
11. **D11 — recording→canon segue write-back — RESOLVED: banked (2026-07-05).** Write-back propagates
    typed extras, not recording-sourced segue corrections; no recording-side segue source of truth
    exists and the assign seam is set-true-only. The 1971-03-03 case was editor authoring, which is in
    scope (§6.1). _Considered, rejected: folding segue propagation into the P2/P3 offer now —
    underspecified source of truth and a seam that can't express un-segue._
12. **D12 — P2/P3 accept path — RESOLVED: route through the editor (2026-07-05).** Accepting a
    write-back offer opens the setlist editor pre-navigated to the date and lands via the existing
    atomic save + diff-at-save review (one seam, P1 unverify for free); a direct `PersistExtraEntry`
    append is banked (`PersistAliasSetlist` template, gated on the slice-4 remap) (§6.1). _Considered,
    rejected: a direct-append accept path now — a second canon write seam with no review-for-free and
    the same slice-4 dependency._

## 12. Out of scope / banked

- **NON-GOAL — recording-side track numbering is never altered by this arc.** No letter-suffixed
  (e.g. `8a`) or otherwise non-integer track numbers may **ever** be written to media file tags or
  filenames. Recording tracks keep ordinary **sequential integer** numbering; compatibility with
  mainstream players is **inviolable**. Canonical entry positions (song or extra) are **internal
  setlist bookkeeping** and never surface in media files (referenced from §3.3). Typed extras change
  the canonical setlist only, not any recording's on-disk numbering.
- Auto-detecting extras from audio (silence/length heuristics) — extras are user-authored here.
- `tracks[]`-derivation cleanup (the rejected D8b alternative) as a standalone refactor.
- `IsMatched` semantics split (title-known vs setlist-claimed) — a separate refactor noted in
  audio-as-archive-design-memo.md; typed extras narrow but do not close it.
- Multi-show-one-date model limitation (reference-side-panel-spec.md §13) — orthogonal.
- Batch migration of existing files to materialize extras — not required; extras accrue per user edit
  (§9).
