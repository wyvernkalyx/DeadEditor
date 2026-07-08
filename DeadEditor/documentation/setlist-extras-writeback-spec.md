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
([Data/concerts/1971-02-21.json](../Data/concerts/1971-02-21.json)) records the concert's **23 songs**
across two sets and — as the committed worked example of this arc — now also carries **two typed
extras**: one `tuning` entry (flattened position 1) and one `false-start` entry labelled **"Ripple
false start"** (flattened position 9), preceding the real Ripple. The concert remains
**`verified:false`**. Before those extras were authored into canon, a common recording of this date —
the 23 songs plus a tuning passage and a second "Ripple" that is a false start — left two tracks
unmatchable under the song-only model:

- The tuning track normalizes to the canonical song "Tuning" (it exists in `songs.json`) but there was
  **no setlist position** for it → off-list (reference-side-panel-spec.md §7.5.2 class 2).
- The false-start "Ripple" normalizes to canonical "Ripple" (`IsMatched == true`) but the one Ripple
  setlist position was already claimed by the real Ripple → **recognized-but-unclaimed**
  (reference-side-panel-spec.md §7.5.2 class 1).

So the album sat permanently at 23/25 matched and could not reach a "complete" match or a verified
state, no matter how correct it was. Authoring the tuning and false-start entries into the canonical
setlist — exactly the two typed extras the committed fixture above now carries — is what gives those
two tracks a home.

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
`"Crowd/Stage Talk"`, `"Ripple false start"` for the false start). A descriptive label is **not fabricated data** —
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

  - **Carve-out — combine-alias appends are exempt (D14, §11).** The combine-alias write path —
    `ConcertLookupService.PersistAliasSetlist` ([ConcertLookupService.cs:362](../Services/ConcertLookupService.cs)),
    the additive append that records a combine against a concert — is **exempt** from P1's "never
    silently change a verified setlist," carries **no verified guard and no unverify**, and none will be
    added. Rationale: `aliasSetlists[]` is **additive, idempotent, diff-excluded provenance** — excluded
    from `ConcertSnapshot.Project` and therefore from the verification diff baseline by design
    ([ConcertReference.cs:46](../Models/ConcertReference.cs), §2.1/§2.3) — so an append changes no
    verified content and cannot trip diff-at-save. It is **match-derived metadata, not setlist
    content**; gating it would add friction to every combine-confirm against a verified concert for a
    write the verification model already treats as invisible. This is **documented, not guarded** (D14):
    P1 governs edits to setlist *content* (entries, types, segues, positions), which still route through
    explicit review; combine provenance sits outside that surface, so path B is unchanged.

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

**D12 accept-path — build reality + interim costs (amended 2026-07-08, Q4/Q5/Q6).** The
route-through-the-editor decision stands; delivering it in slice 7a (§10) has concrete costs, now
documented:

1. **A staged-extras entry parameter on `EditSetlistView` will be built** — it does **not** exist
   today. Staged extras arrive as pre-populated **unsaved** rows, subject to the editor's normal
   diff-at-save review and the slice-4 atomic remap; they are **never auto-saved**. The banked
   direct-append seam (`PersistExtraEntry`-style, above) **stays banked** — 7a adds no second write
   seam.
2. **Import needs a new direct-to-editor accept navigation.** Import's existing setlist deep-link stops
   at `ConcertDetailView` and is **one-way by ratified decision** — that decision **stands for the
   non-offer path** (§8); the offer-accept path gets **its own editor navigation**, delivered with the
   Import inline banner (§10 slice 7a).
3. **Documented interim cost — the offer-accept round trip on Edit rides the slice-6 return guard**,
   which **unconditionally invalidates match/claim state (including on Cancel)** and forces a
   re-**Match Setlist** (§10 slice 6). This is **accepted for 7a**; the **claim-preserving return
   upgrade remains banked** (available only once the slice-4 remap can preserve claims across a shifted
   setlist, §10).
4. **Affordance (Q6) — the P2 offer is actionable on both surfaces.** Edit reuses the existing
   `ValidationBanner` inline notice; Import gets **new inline-banner XAML delivered alongside its
   accept navigation, not before**.

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
- **Import's convention stays as-is for the non-offer path** — the user re-**Reads** after fixing a
  setlist and Read already re-invokes `RefreshSetlistPanelAsync` (reference-side-panel-spec.md §9); no
  new hook there. **Exception (2026-07-08, §6.1/§10 slice 7a):** the **P2 offer-accept** path is
  distinct — Import's setlist deep-link stops one-way at `ConcertDetailView`, so accepting a write-back
  offer gets its **own new direct-to-editor accept navigation** (delivered with the Import inline
  banner), not the Read round-trip.

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
4. ✅ **Alias index-remap component (D9a — MANDATORY PREREQUISITE) — LANDED.** The pure, atomic
   remap (§3.3) as a **first-class, separately unit-tested** helper `Helpers/SetlistPositionRemap`:
   given a canonical-`Position` insertion / removal / reorder over the flattened 0-based axis, remap
   every position-indexed consumer in one operation. Per the §2.2 D9a obligation this covers **not just
   `AliasEntry.CoveredOfficialIndices` but also `ClaimedPositions` and per-track `ClaimedSetlistPosition`**
   (broader than this line's original blurb; the spec body governs). Atomic by purity — inputs are never
   mutated, so a caller adopts a result only after it is produced exception-free. The D13 removed-covered
   case throws `SetlistRemapUnsupportedException` as a backstop (see D13). 35 unit tests: insert/remove
   at head/middle/tail, insert-before/after/spanning a covered run, reorder, multi-run alias, claims on
   removed positions, sequential composition, round-trip logical-entry coherence, cross-consumer
   atomicity, and out-of-range robustness. **Gate held: no extra-editing path (slice 5) shipped before
   this landed.**
5. **Setlist-editor extras authoring** (§7) — **expanded per the owner's lived-demand requirements
   (2026-07-06 session on 1971-04-08); split into 5a/5b, each with its own WPF gate.** The editor as it
   stands is **append-only** (`AddSongButton_Click` appends with `Position = _tracks.Count + 1`,
   EditSetlistView.xaml.cs:184-207), removes with a full renumber (`RemoveSelectedTracks`, :218-231),
   and stages combines applied at Save (`CombineSelectedButton_Click`, :249) — with **no
   insert-at-position, no move/reorder, no song-name autocomplete**, and it constructs
   `ConcertTrackInput` **without a Type** so every saved entry rides the `"song"` default
   (`BuildTrackInputs`, :579-580 — the slice-1 banked note, even though `ConcertTrackInput.Type` and
   `ConcertSnapshot.Project` already carry a type through). Slice 5 closes all of that. **Depends on:**
   slice 4 (hard — every position shift routes through `SetlistPositionRemap`).

   **Remap model — remap-at-save, not remap-per-operation (chosen).** Positions are assigned in exactly
   one place today: `ConcertSnapshot.Project`'s contiguous renumber at Save (ConcertSnapshot.cs:50-83);
   nothing tracks position mid-session. The remap therefore lands **at Save, beside that renumber**
   (§3.3's ratified "a save projection that renumbers positions and remaps covered indices together,
   never one without the other"): the Save path derives the old→new mapping from **surviving-row
   identity** (each loaded `EditableTrack` keeps its origin; inserted rows have none; removed rows map to
   null), builds a `SetlistPositionRemap`, and applies it to the persisted `_concert.AliasSetlists` (and
   any live claim state) inside the same whole-object write that already applies staged combines. One
   place changes positions, the same breath remaps the indices — atomic by construction — and **Cancel
   stays trivial** (drop the in-memory edits; no alias mutation to unwind). Deriving the composite
   old→new map at Save may add a small row-identity factory (e.g. `FromSurvivingOrder(oldCount,
   newRowOrigins)`) to the pure `SetlistPositionRemap`, unit-tested in 5a and staying **inside the one
   atomic component** — not a new write seam. _Considered, rejected: remap-per-operation on live editor
   state — it can call the discrete `ForInsert`/`ForRemove`/`ForReorder` factories directly, but it must
   continuously mutate `_concert.AliasSetlists` in memory, unwind those mutations on Cancel, and fight
   the assign-positions-only-at-save model; the dirty-block that already forbids combine authoring during
   unsaved structural edits (alias-setlists-spec.md §6) exists precisely because live positional indices
   are unstable mid-session._ The existing combine dirty-block **stays** — the at-save remap keeps
   already-persisted aliases coherent across a structural-edit Save; relaxing the dirty-block so new
   combines can be authored amid unsaved structural edits is out of scope (banked).

   - **5a — structural authoring + atomic remap + D13 block + visual parity.** Insert-at-position and
     move/reorder of rows (alongside the existing add/remove); the Save projection routes every position
     shift through `SetlistPositionRemap`, remapping persisted `aliasSetlists` covered indices and any
     live claim state (§2.2 D9a). **D13 lives here, with the remove path** (moved from the original
     slice-5 blurb: the remove+remap path is exactly where a combine-covered entry can be hit, so the
     user-facing block must ship *with* it, not a sub-slice later) — removing a combine-covered entry is
     **refused with a message naming the covering combine and stating dissolve-first** (and that
     combine-dissolve authoring is not yet built, alias-setlists-spec.md §6); `SetlistRemapUnsupportedException`
     is the backstop, never the user-facing failure. **Song-name entry gains autocomplete** from
     `songs.json`'s canonical list as the user types, with **free text always allowed** (extras like
     "Tuning" / banter are off-list; D10 requires only a non-empty label). **Visual parity with Edit
     Metadata** — the owner's finding, verbatim: *"the Edit Setlist view should look like the Edit
     Metadata view. Instead, it looks like a different program."* Parity is a requirement (grid styling,
     header/action layout, control chrome), pixels unpinned. Segue editing is **unchanged** (already
     works — the 1971-03-03 Set 2 curation precedent). **WPF gate:** on a date that HAS a combine, insert
     a row ahead of the combine's covered run, Save, reopen → the combine's covered song names are
     unchanged (survived the shift); attempt to remove a combine-covered entry → blocked with the D13
     message; the view reads as a sibling of Edit Metadata.
   - **5b — typed-extra authoring.** A per-row **type selector** (D1 vocabulary; default `song`);
     `EditableTrack` gains `Type`; **`BuildTrackInputs` passes `Type` explicitly** (closing the slice-1
     default-ride at EditSetlistView.xaml.cs:579-580 so a retyped row actually persists its type);
     **D10a non-empty-label enforcement** for all rows of any type (widened 2026-07-07 from the
     extras-only D10 — see the decision record; a fresh row defaults to `song`, so an unnamed song row
     otherwise slips through); extras render visually distinct (muted row / type chip, §7). A type edit is a content edit that unverifies on Save (D3). **Depends on:** 5a (the remap
     + D13 block must exist before extras can be authored). **WPF gate:** the two-Ripples round-trip — add
     a `false-start` "Ripple" + two `tuning` entries to 1971-02-21, Save, reopen → types persist, and the
     recording's false-start + tuning tracks now have canonical positions to claim (§1, §4).
6. **Edit-side deep-link unbank** (§8) — **pulled forward on lived demand, ahead of slices 4–5.**
   Direct-to-editor navigation (`EditMetadataView.OnEditSetlistRequested` →
   `ShellWindow.NavigateToSetlistEditor`, not Import's ConcertDetail hop) + a guarded refresh-on-return.
   **Guard, not remap:** because the slice-4 atomic position remap does not exist yet, the return path
   does not try to keep claims coherent across a shifted setlist — it **invalidates** match state
   (`_matchSetlistHasRun=false`, clears `_lastClaimedPositions`/`_lastSetlistSongs`/every
   `ClaimedSetlistPosition`, clears amber) and rebuilds the panel as pure reference, so a stale claim
   can never dim/free the wrong entry (the §3.3 hazard). A re-run of Match Setlist re-establishes claims
   against the new positions. The same return also guards the retained-instance `Loaded` re-fire so the
   tag re-read cannot drop unsaved album edits (reference-side-panel-spec.md §9). When slice 4 lands, the
   invalidate-on-return MAY be upgraded to a position-preserving remap (optional; invalidation stays
   correct). WPF gate = Edit → edit setlist → back, album edits intact, panel refreshed, claims cleared.
7. **Write-back offers** (§6) — **split into 7a/7b per the owner's 2026-07-08 rulings, each with its
   own WPF gate.**

   - **7a — P1 carve-out documentation + P2 write-back offers.** P1's combine-alias carve-out (D14,
     §6) needs **no code enforcement** — it is a documented exemption, not a guard. P2-visible offers
     ship on **both surfaces, Edit-surface first, then Import** (§6.1/§8): Edit reuses the existing
     `ValidationBanner` inline notice; Import gets **new inline-banner XAML delivered alongside its
     accept navigation** (§6.1 affordance, Q6). Accept routes through the setlist editor via a **new
     staged-extras entry parameter** on `EditSetlistView` (D12, §6.1) — staged extras arrive as
     pre-populated **unsaved** rows subject to the editor's normal diff-at-save review and the slice-4
     atomic remap, **never auto-saved**; the banked direct-append seam stays banked. **Interim cost
     accepted (§6.1):** the Edit offer-accept round trip rides the slice-6 return guard, which
     unconditionally invalidates match/claim state (even on Cancel) and forces a re-Match — the
     claim-preserving upgrade stays banked. **WPF gate:** the two-Ripples P2 round-trip on Edit, then
     Import, each landing staged extras through the editor's Save.
   - **7b — P3 update-and-verify prompt — gated on a precursor.** P3 (§6) offers the combined "update
     the canonical setlist and mark it verified?" prompt on import against an updated unverified
     setlist. It **must render the diff it proposes**, which a change-detecting **hash cannot supply**;
     7b therefore **depends on a precursor**: persisting an import-time setlist **snapshot** (not a
     hash) in the **Layer B per-recording manifest**. **7b does not start until that precursor is
     specced and landed.** The precursor's shape is **7b Phase-A work**, not specified here beyond
     naming it. **WPF gate:** the full two-Ripples P3 round-trip from a recording — import against an
     unverified, detail-corrected setlist → the combined update-and-verify prompt shows the snapshot
     diff → accept updates canon and verifies.

**Sequence reference — reference-side-panel arc slice 5b + 6 (LANDED).** Panel **click-to-assign**
(5b, `7b2d635`) and **drag-and-drop** (slice 6, `dfa5673`) are specified in
[reference-side-panel-spec.md](reference-side-panel-spec.md) §7.5/§10/§11.5 and are **NOT** part of
this arc's slices. They are the placement gesture this arc's typed extras make useful, and **both have
now landed** — so the sequencing precondition (a placement gesture must exist before extras are typed
and displayed) is **satisfied**, not pending. **This arc therefore starts cleanly at its own slice 1**;
the unified `AssignEntryToTrack → Reassign → Apply` seam (§4) is already in place for extras to claim
through. Global order from here: extras slice 1 → 2 → 3 → **6 (Edit deep-link unbank — pulled forward on lived
demand; ships with the invalidate-on-return guard, not the slice-4 remap)** → 4 (remap) → 5a → 5b
(editor authoring, gated on 4) → **7a (P1 carve-out doc + P2 offers) → 7b (P3 prompt, gated on the
Layer B import-time snapshot precursor)**. Slice 6 moved ahead of 4/5 because the deep-link's return path
only needs to *invalidate* claims, which has no dependency on the remap; the remap is required only to
*preserve* claims across an edit, a later refinement.

## 11. Decision record (RESOLVED 2026-07-03; addenda D10–D12 2026-07-05, D13 2026-07-07, D14 2026-07-08)

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
    - **D10a — save-time name gate widened to ALL rows — RESOLVED (2026-07-07, supersedes-not-reopens
      D10).** The setlist editor's save-time name block covers **every entry of any type**, not just
      extras. A fresh row defaults to type `song`, so the extras-only gate left the front door open:
      the slice-5b gate authored an **unnamed `song` row that persisted to canon** (1971-02-21, the
      nameless "(Set 2, #12)" candidate). No row of any type may save with an empty/whitespace name,
      enforced by the pure `RowLabelRule.FirstUnnamedRow`; this closes the gap between save-legal and
      verify-legal for names (`ConcertVerifyGate` already refuses a nameless row). The one behavior
      change from D10's extras-only scope: a blank **song** name now blocks save too. _Considered,
      rejected: leaving song rows unblocked — the default-`song` front door is exactly what failed the
      gate._
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
13. **D13 — removed-covered-entry remap semantics — RESOLVED: forbid upstream (2026-07-07).** A
    position-shifting edit that would remove a setlist entry an alias combine covers is **blocked in the
    editor** until the combine is dissolved — the edit never reaches the remap, so no covered index is
    ever orphaned. The pure `SetlistPositionRemap` refuses this case with
    `SetlistRemapUnsupportedException` as the enforcement **backstop** if the editor check is ever
    bypassed. **Honest gap:** combine-dissolve authoring does not exist yet (aliases are write-only —
    alias-setlists-spec.md "Removal is deferred"), so a combine-covered entry is currently
    **un-removable** until it ships; the editor block message must say why. _Considered, rejected: (a)
    shrink the covered run — silently changes an authored combine's meaning; (b) drop the alias —
    destroys curation as a side effect of an unrelated edit._
14. **D14 — combine-alias appends under P1 — RESOLVED: document the carve-out, don't guard
    (2026-07-08).** Combine-alias appends via `ConcertLookupService.PersistAliasSetlist` are **exempt**
    from P1's "never silently change a verified setlist" and get **no verified guard and no unverify**
    (§6). `aliasSetlists[]` is additive, idempotent, diff-excluded provenance — outside
    `ConcertSnapshot.Project` and the verification baseline by design
    ([ConcertReference.cs:46](../Models/ConcertReference.cs), §2.1/§2.3) — so an append changes no
    verified content; it is match-derived metadata, not setlist content. Gating it would add friction to
    every combine-confirm against a verified concert for a write the verification model already treats
    as invisible. _Considered, rejected: a verified guard / unverify on the alias append — friction on
    an idempotent, diff-excluded write that can never alter verified setlist content._

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
