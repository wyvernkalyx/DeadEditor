# Audio-as-Archive: Design Memo

## Status

This is a design memo, not a spec. It captures the conceptual model
DeadEditor is moving toward, what has been implemented so far, and
what remains to be designed. Implementation commits should reference
this memo. Specs that emerge from this thinking should supersede the
relevant sections here when they land.

The memo is a banking document. It is *not* an authorization to
implement anything beyond what is already cited as landed in code.
Each "Pending design" item below is its own future conversation.

## Background

DeadEditor's purpose is managing a personal live-recording library —
primarily Grateful Dead, expanding to other artists. The data model
distinguishes between **what was performed** (the setlist, documented
in `Data/shows.json` and adjacent curated sources) and **what is on
tape** (the audio files the user owns).

Earlier behavior treated the setlist as canonical. Match Setlist
forced audio tracks to align with setlist positions, with unmatched
tracks (tuning, banter, applause) pushed to an "overflow disc" with
high track numbers (e.g., 401, 402, ...). This destroyed the natural
audio sequence of archival recordings: a 14-track audience tape with
two tuning passages between St Stephen and The Eleven would arrive in
the library as eleven matched tracks plus a disc-3 orphan section,
with the original audio sequence permanently lost from disc/track
metadata.

The session of 2026-05-06 reframed this and is captured below.

## Four conceptual shifts

### 1. Audio is the archive

The audio files are the truth. They document what was *recorded*. The
setlist documents what was *performed*. They overlap heavily but are
not identical:

- A recording may include tuning, banter, false starts, audience
  applause — none of which appear on a setlist.
- A recording may be incomplete (cut tape, missing songs the band
  played).
- A recording's track boundaries may not match setlist song
  boundaries (tape flips mid-song; one audio file holding two segued
  songs).
- A recording may have *more* discs than the setlist has sets, when
  the recording was split for physical-medium reasons (CD-length
  splits) rather than performance-structure reasons.

DeadEditor preserves the audio as it is. Match Setlist no longer
forces alignment; it decorates matched tracks with canonical names
and segue flags from the setlist and leaves unmatched tracks
untouched. Disc and track numbers reflect the source recording's
structure, not the setlist's.

A small but important corollary: `shows.json` already includes
performed-segment entries like Drums, Space, and various Jam
variants. These match audio tracks correctly under the new behavior.
The Model B framing is specifically about *non-performed* tracks
(tuning, banter, applause) that no setlist would document — *not*
about reclassifying material the band performed.

**Implementation status:** ✅ Landed in commit `3ad318e` (Model B
Commit 0). ImportView's `MatchSetlistButton_Click` and
`MatchToSong_Click` are now both decoration-only. The matching
algorithm is extracted to `Services/SetlistMatcher.cs` as a pure
static method, parameterized on a canonical-resolver callback so it
has no service or WPF dependencies. EditMetadataView's Match Setlist
was already decoration-only (see `feature-parity-spec.md` § 6); the
two views now converge on the same Model B framing. See
`01-main-window.md` § Match Setlist Workflow for the user-facing
description.

### 2. Trust hierarchy is a workflow, not just a doctrine

The principle "user-verified data > existing tags > external sources"
has been stated as project doctrine for a while. This session
operationalized it: the *workflow* the user moves through *is* the
trust hierarchy made concrete.

- **External sources produce suggestions.** MusicBrainz and the
  curated `shows.json` setlist database are both external sources.
  Both produce suggestions about what the audio is. Neither is
  authoritative on its own.
- **The user verifies suggestions.** Until the user has reviewed and
  confirmed, nothing is verified. Verification is an explicit,
  per-field action (or per-stage, or per-album — see open questions),
  not a vibes-based "looks good."
- **Verified data is durable.** Once the user has verified a field,
  re-running an external-source action should not silently overwrite
  it. (Today, MBID-driven re-match does silently overwrite track
  titles via `ApplyMusicBrainzData`. This is a known papercut flagged
  in `verification-model.md` § Open Questions, item 7.)

The verification model
([documentation/verification-model.md](verification-model.md)) is the
stub doc where this gets formalized. That stub already enumerates the
hard mechanical questions (granularity, state machine, storage,
external-data interaction). This memo adds the conceptual framing the
stub will eventually formalize — that verification is *part of the
import flow*, not a sticker the user applies after the fact.

**Implementation status:** 🟡 Doctrine present in CLAUDE.md and
adjacent docs; workflow-level enforcement not yet implemented. No
field on any record carries a verification flag today.

### 3. Match Setlist and MusicBrainz are the same conceptual action

Both look up authoritative metadata from an external source and
decorate the audio with it. They differ in *which* source — official
discography curated by MusicBrainz, or live-show database curated
within DeadEditor itself (`shows.json`). The trust hierarchy applies
the same way to both: external suggestion → user verification →
durable record.

Treating them as the same conceptual action ("Enrich") under the
canonical workflow unifies the architecture. The implementation is
polymorphic on album type:

- **Official Release / Studio Album:** Enrich = MusicBrainz lookup.
  Source is the public MusicBrainz database keyed by AcoustID
  fingerprint or text search.
- **Audience Recording:** Enrich = Match Setlist against `shows.json`.
  Source is the project-internal setlist database keyed by
  `yyyy-MM-dd`.
- **Box Set:** Both apply, depending on context (the setlist for the
  underlying show date *and* the MusicBrainz release for the boxed
  edition).

`shows.json` IS DeadEditor's curated external metadata source for
audience recordings. It is not "internal data" in any meaningful
sense — it is the audience-recording equivalent of MusicBrainz,
assembled within the project. Treating it as such places it correctly
in the trust hierarchy and clarifies what "Enrich" means for any
album type.

**Implementation status:** ❌ Not implemented as a unified action.
The current Import toolbar exposes "MusicBrainz" and "Match Setlist"
as separate buttons. The workflow stepper's Enrich stage is
MusicBrainz-only — `ImportWorkflowState.MbidPresent` is the sole
input ([Services/ImportWorkflowState.cs:23](../Services/ImportWorkflowState.cs#L23)).
Future work will unify these under album-type-driven Enrich behavior.

### 4. Structure is a verification stage, not an automated action

The canonical Import workflow has five stages:

| Stage     | What it is                                | How it's done                                       |
|-----------|-------------------------------------------|-----------------------------------------------------|
| Load      | Get tracks into the app                   | Pick a folder, run Read                             |
| Enrich    | Decorate with external-source suggestions | MusicBrainz (Official) or Match Setlist (Audience)  |
| Clean     | Apply title normalization                 | Run Normalize                                       |
| Structure | **Human review and verification**         | User walks a checklist, edits the grid, confirms    |
| Import    | Commit verified album to managed library  | Run Import to Library                               |

Three stages are programmatic actions: Load, Enrich, Clean. One stage
is a terminal action: Import. **Structure is a verification surface.**
The user does the work; the app provides the structure for the user
to work through.

The verification checklist (preliminary; subject to design):

- [ ] Album fields correct (artist, date, venue, city/state, album/release name, year, type)
- [ ] Song names normalized correctly
- [ ] Segue markers correct
- [ ] Album-level date correct
- [ ] Track-level dates correct (per-track date for multi-night box sets)
- [ ] Disc/track numbering reflects audio sequence (not setlist)

When all checklist items are confirmed, Structure is complete and the
album is ready for Import. The mechanics — how confirmation is
recorded, how it persists, how it interacts with re-running Enrich,
what happens when a user adds a new track or edits a previously
confirmed field — are open design questions. See § Open design
questions below.

**Implementation status:** ❌ Not implemented. The current stepper's
Structure stage uses an 80% canonical-name heuristic
([Services/ImportWorkflowState.cs:108-123](../Services/ImportWorkflowState.cs#L108-L123))
that is a proxy for "Match Setlist or Normalize has run," not for
"user has verified." The threshold catches the common case but does
not represent verification.

## Implementation status summary

| Conceptual shift                                        | Status                                          |
|---------------------------------------------------------|-------------------------------------------------|
| Audio is the archive                                    | ✅ Landed in commit `3ad318e` (Match Setlist)   |
| Trust hierarchy as workflow                             | 🟡 Doctrine present; workflow not enforced     |
| Match Setlist and MusicBrainz unified as Enrich         | ❌ Pending design                              |
| Structure as verification stage                         | ❌ Pending design                              |

## Open design questions (queued for future sessions)

These are not for this memo to answer. They are the agenda for the
next design conversation(s). Each is a separate Phase A diagnostic
or design memo waiting to happen — the order in which they get picked
up is itself a design decision.

### Stepper-as-buttons redesign

- Should the workflow stepper become the action surface, replacing
  the current toolbar?
- How does Enrich express its polymorphism — same pill, behavior
  switches on album type, or visible variation in label/icon?
- What does Structure look like as a UI affordance — a pill with
  click-to-mark-complete, an inline checklist, an expandable panel,
  something else?
- The current toolbar has accreted clusters (Source ops / Enrichment
  / Grid ops / Terminal action — see commit `b23b4bb`). The stepper
  is currently informational only, not action-bearing. Merging them
  is a real UX redesign, not a relabeling.

### Verification model

This is its own large topic, already stubbed in
[verification-model.md](verification-model.md). The questions there
remain open. Adding workflow-framing context from this memo:

- How is verification recorded — per-field, per-stage, per-album?
  (verification-model.md § Open Questions item 1.)
- Where does it persist — sidecar JSON, FLAC tags, both?
  (verification-model.md § Open Questions item 6.)
- How does verified data interact with re-running Enrich? Block?
  Prompt? Diff and let user accept/reject per-field?
  (verification-model.md § Open Questions item 5.)
- What is the migration story for albums imported before
  verification existed? (Adjacent to the Model B migration question
  for albums imported under Model A — see below.)

### Album type field audit

The album-type field (now collapsed to `AudienceRecording` /
`OfficialRelease`) has one clear use (library filtering under the
single `LibraryRoot`) and several questionable ones (driving Enrich
behavior, default values, hybrid album handling like "Studio +
bonus live"). A small focused audit is queued to determine which
uses are real and which are vestigial.

### Match Setlist's behavior on missing/extra tracks

Currently silent on:

- Setlist songs with no matching audio (recording missing songs the
  band played). Today this surfaces only via the right-click "Match
  to Song..." dialog populating its list of unclaimed setlist
  positions — never as a banner or count.
- Audio tracks with no matching setlist entry (tuning, banter, etc.).
  Today these are flagged visually (gold row tint via
  `IsMatched=false`), but not summarized.

Should these be surfaced as banners, columns, optional warnings? The
data is collected in `_lastClaimedPositions`; it's just not
displayed. Tied to the Structure-as-checklist question — the
checklist is the natural place for these signals.

### Edit Metadata view's relationship to this workflow

The Edit Metadata view edits an already-imported album. Open
questions:

- Does the verification stage apply post-import? (Probably yes — a
  re-edit can produce new unverified state.)
- What about the rest of the workflow stages — does an
  already-imported album have a "Load" state? Or is post-import
  editing a separate flow that shares only the Structure surface?
- EditMetadataView already differs from ImportView on Match Setlist
  (it never created the overflow disc; see `feature-parity-spec.md`
  § 6). After Model B Commit 0, the two converge on Match Setlist.
  Convergence on the rest of the workflow is a separate question.

### Model B follow-on commits

These were enumerated in the Model B Phase A audit (held in
conversation context, not committed to `documentation/`):

- **Track-kind field on `TrackInfo`** for distinguishing
  non-performed tracks (Tuning, Crowd, Banter) from performed
  tracks. The shape is undecided: enum on `TrackInfo`, membership
  in a curated list in `songs.json`, a custom FLAC tag, or some
  combination. Today there is no such distinction; songs.json has
  "Tuning" as a canonical entry but cannot tell it apart from "Dark
  Star."
- **Migration for albums imported under Model A.** Existing imports
  may have Model-A-induced overflow-disc structures (e.g., Disc 3
  with track 301-303 holding tuning tracks). Decision required:
  leave as-is, opportunistic migration on Edit Metadata visit, or
  batch tool. Commit `3ad318e` deliberately did not migrate.
- **`IsMatched` semantics cleanup.** Today `IsMatched` overloads two
  signals: "the title is a known canonical in `songs.json`" (set by
  `NormalizationService.NormalizeAll`) and "the track was matched to
  a setlist song for this date" (set by Match Setlist). These are
  orthogonal. Splitting or renaming is a future refactor.
- **MetadataValidator under Model B.** The validator's per-disc
  contiguity checks have stronger meaning under Model B (no more
  legitimate overflow disc justifying Disc 1 → Disc 4 jumps). A
  sweep of test data and real library data is queued to confirm
  nothing fails under stricter Model B assumptions.

## Cross-references

- [01-main-window.md § Match Setlist Workflow](01-main-window.md) — user-facing description of the post-`3ad318e` behavior
- [verification-model.md](verification-model.md) — verification stub doc; this memo expands its conceptual framing
- [feature-parity-spec.md § 6](feature-parity-spec.md) — historical record of why ImportView and EditMetadataView Match Setlist diverged, and the narrower file-rename rationale that the audio-as-archive framing supersedes
- Commit `3ad318e` — Model B Commit 0 implementation
- Commit `4ddeadd` — Workflow stepper proof-of-concept (current Enrich/Structure heuristics)
