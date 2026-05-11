# Curation Layer: Design Memo

## Status

This is a design memo, not a spec. It captures conceptual decisions
made during the session of 2026-05-11, what is already documented
elsewhere, and what remains to be designed. Implementation commits
should reference this memo. Specs that emerge from this thinking
should supersede the relevant sections here when they land.

The memo is a banking document. It is *not* an authorization to
implement anything. Each "Pending design" item below is its own
future conversation.

## Background

The session that produced
[audio-as-archive-design-memo.md](audio-as-archive-design-memo.md)
established that audio is the archive and the workflow is the trust
hierarchy made concrete. That memo deferred several questions to
future design conversations, including:

- How verification is recorded and persisted
- What "Structure complete" actually means
- How re-running Enrich interacts with verified data
- The migration story for albums imported before verification existed

The session of 2026-05-11 took up "Stepper-as-buttons +
Structure-as-checklist" from that queue. The conversation surfaced
that the questions in front of it could not be answered without
first naming the data layer where verified state would live. That
data layer is the **curation layer**, and naming it changes how
several of the audio-as-archive memo's pending questions get
answered.

This memo banks the conceptual framing that emerged. It does not
answer the design questions; it sharpens them.

## Five conceptual positions

### 1. The workflow is a property of the album, not the view

ImportView and EditMetadataView are two surfaces onto the same
workflow. The workflow's stages — Load, Enrich, Clean, Structure,
Import — describe states an album moves through, not steps a UI
guides the user through. Either view can be the surface where a
given stage is acted on.

Practically, this means design proposals for the workflow must be
evaluated against both views. A proposal that makes sense in
ImportView but breaks EditMetadataView is wrong; a proposal that
works only because EditMetadataView is exempted is also wrong.
Import and EditMetadata are ~99% the same workflow with a real but
narrow 1% difference (source-folder selection and copy are
Import-only).

This position is consistent with the convergence work already
landed in commit `3ad318e` (ImportView and EditMetadataView Match
Setlist now share the same Model B framing) and with the project's
multi-band-by-design principle (the workflow doesn't know about
specific artists; the album does).

**Implementation status:** 🟡 The framing is settled in
conversational record. Code-level enforcement is partial — Match
Setlist has converged across views (3ad318e); the rest of the
workflow has not.

### 2. Edit Metadata is the Structure stage, replayed post-import

(Working position, derived from § 1, not yet stress-tested in a
design conversation.)

If the workflow is a property of the album, then an
already-imported album that needs editing has already passed
through Load, Enrich, Clean, and Import. What it re-enters is
Structure — the verification surface. Edit Metadata is therefore
not a separate workflow; it is the same Structure stage running on
an album that has already been imported.

Practical implications, if this framing holds:

- The verification model and the Structure-as-checklist UI must
  work in EditMetadataView, not just ImportView.
- "Save Changes" in EditMetadataView is a re-completion of the
  Structure stage. Editing a previously-verified field re-opens
  that checklist item; other items stay verified.
- Editing fields that map to other stages (e.g., re-running
  MusicBrainz from EditMetadataView) is a re-entry into those
  stages, with the same verification consequences as the first
  time through.

The four open questions in
[audio-as-archive-design-memo.md](audio-as-archive-design-memo.md)
§ Edit Metadata view's relationship to this workflow get cleaner
answers under this framing, but the framing itself needs design-
conversation review before being treated as settled.

**Implementation status:** ❌ Framing-only; no code reflects it.

### 3. The curation layer is foundational, not derived

The project's existence-test — "faster and more accurate than a
spreadsheet for maintaining a deep live-recording library" — turns
on the user's curation work persisting and not needing to be
repeated. Without a place for verified state to live, "verified"
is a sticker the user applies that doesn't survive a re-import or
a database reset. With a persistence layer for verified state,
"verified" is the input to the next import, and the library's data
quality compounds over time.

The goal state, in the user's words: **import once, verify once,
never again.** A future import of the same recording should match a
curation record and auto-populate every field correctly without
asking the user to redo song-name normalization, track-date
verification, segue confirmation, or any other Structure-stage
work.

This makes the curation layer foundational to the verification
model — it is the layer where verified state lives. Without it,
the verification model is an in-memory flag with no persistence
strategy.

**Implementation status:** ❌ Conceptually foundational, not yet
designed as a unified layer. Some pieces exist (see § 4); the
unification does not.

### 4. Two layers: setlist authority and per-recording manifest

The data files DeadEditor maintains about concerts and recordings
fall into two distinct categories that should not be conflated:

**Layer A — Setlist authority: `Data/concerts/*.json`**

Per-date files describing **what was performed**. Date, venue,
city, state, country, setlistFmId, sets/songs. These are *about
the concert as a historical event*, independent of any audio. They
exist for dates the user does not own. They are shared reference
data, the audience-recording equivalent of MusicBrainz, assembled
within the project. The audio-as-archive memo's § 3 names this
correctly:

> `shows.json` IS DeadEditor's curated external metadata source
> for audience recordings.

(The on-disk representation is per-date files under
`Data/concerts/`; the in-memory aggregate is `shows.json` as
loaded by `ShowLookupService`. This memo treats them as one layer.)

**Layer B — Per-recording manifest:
`{LibraryRoot}/{Artist}/{AlbumFolder}.json`**

Per-album sidecar files describing **what is on tape**. Folder
name, album type, full album fields, tracks array with filename,
disc/track numbers, song name, track date, segue, and (proposed
addition) per-track AcoustID fingerprint and verification state.
These are *about a specific copy of a recording* and are sidecar
to it. Two different audience tapes of 1977-05-08 are two
different Layer B files; both reference the same Layer A concert.

Layer B is specified in
[19-folder-import-and-manifests.md](19-folder-import-and-manifests.md)
and is currently in "Step 1 complete, Step 2 pending" status. The
spec was written before the audio-as-archive memo's framing landed
and before fingerprint persistence shipped; it does not yet include
the additions implied by this memo (see § 5 below).

**The two layers are not interchangeable.** Layer A is shared
reference data; Layer B is per-copy curation. Extending Layer A
files to carry per-recording state would conflate the two, force
multiple copies of the same date to share or fight over one file,
and would not address official releases (which have no Layer A
concert file). The right move is to land Layer B as the curation
home and let Layer A stay focused on setlist authority.

**Implementation status:**
- Layer A: ✅ Exists (`Data/concerts/*.json`,
  loaded by `ShowLookupService`).
- Layer B: 🟡 Spec exists; Step 2 (manifest generation) pending
  implementation.

### 5. Fingerprints are the curation lookup key

The "import once, verify once" goal requires that a future import
of an already-curated recording be recognized as that recording,
unambiguously, without depending on folder names, filenames, or
tag content (all of which are malleable). The AcoustID Chromaprint
fingerprint, which is deterministic from audio bytes, is the
correct identity primitive at the track level.

Fingerprint persistence already exists: per the
[fingerprint-persistence-spec.md](fingerprint-persistence-spec.md),
every imported track carries `ACOUSTID_FINGERPRINT` as a tag and
that value is read back into `TrackInfo.AcoustIdFingerprint` at
folder load. What does not yet exist is **fingerprint-keyed reverse
lookup** — given an incoming folder of tracks, find the Layer B
manifest whose tracks fingerprint-match.

Once that lookup exists, the curation workflow on import becomes:

1. Compute fingerprints for incoming tracks (already done at
   import time).
2. For each incoming track, look up its fingerprint across
   existing Layer B manifests.
3. If a quorum of incoming tracks point to the same manifest, that
   manifest is the curation record for this recording.
4. Apply the manifest's verified state to the incoming tracks.
   What "apply" means in detail depends on the verification model
   decisions still pending.

This is the "compare media to curation file" workflow the user
asked about. The manifest spec's § 4 (When Manifests Are Used —
Import Matching) already describes a compare view for this; what's
missing is the fingerprint-keyed match logic underneath it. The
spec's current matching is by album date, which is ambiguous when
multiple copies of the same date exist.

A consequence worth naming: a verified Layer B manifest becomes a
**fourth source in the Enrich trust hierarchy**, alongside
MusicBrainz and `shows.json`. It is external-to-the-current-import
but internal-to-the-library, and it incorporates accumulated
verification work — making it the highest-trust external source
available for a recording the library has seen before.

**Implementation status:**
- Per-track fingerprint tags: ✅ Landed (fingerprint-persistence
  spec, Commit 2a and follow-ons).
- Fingerprint in the Layer B manifest body: ❌ Not in current
  manifest spec; addition required.
- Fingerprint-keyed reverse lookup: ❌ Not designed or implemented.

## Pending design

These questions are not answered by this memo. They are the agenda
for the next design conversation(s). The list narrows the
audio-as-archive memo's pending questions by placing them in the
context of the curation layer.

### Verification model — granularity and storage

[verification-model.md](verification-model.md) remains the stub
where these get formalized. Open items relevant after this memo:

- **Granularity.** Per-album single flag, per-Structure-checklist-
  item, per-field? The Layer B manifest schema can support any of
  these; the choice constrains the Structure-as-checklist UI.
- **Storage.** With Layer B as the established curation home,
  storage of verification state defaults to the manifest. Whether
  FLAC tags also carry verification state is a separate question
  (probably no, but worth confirming).
- **Re-Enrich interaction.** When a verified field has new data
  from MusicBrainz, `shows.json`, or AcoustID, does the app
  silently keep the verified value, prompt, or surface a diff?
  Becomes tractable once granularity is settled.

### Structure-complete gate

What does it mean for the Structure stage to be complete?

- The audio-as-archive memo proposes a user-walked checklist.
- The current code uses an 80% canonical-name heuristic
  ([Services/ImportWorkflowState.cs:108-123](../Services/ImportWorkflowState.cs#L108-L123)).
- [verification-model.md](verification-model.md) frames it as
  "required fields populated."

These are three different gating models. The choice determines
whether Structure is a single boolean, a vector of booleans, or a
hybrid; whether the stepper's Structure pill can auto-complete
from heuristics or must be manually confirmed; and what gets
written into the Layer B manifest's verification fields.

### Stepper-as-buttons UX

Once verification mechanics are settled, the visual question
remains: how does the stepper become the action surface, what
happens to current toolbar actions (utility menu vs. retire), and
how does Enrich express its polymorphism on album type (one pill
with dispatching behavior, or visible variation)? The
audio-as-archive memo's § Stepper-as-buttons redesign frames
this; it stays open.

### Manifest spec reconciliation

[19-folder-import-and-manifests.md](19-folder-import-and-manifests.md)
predates this memo. The reconciliation needed:

- Add per-track fingerprint to the manifest body.
- Add verification fields (granularity TBD).
- Replace date-based match with fingerprint-keyed match.
- Update § 4 (Import Matching) for the fingerprint workflow.
- Clarify the manifest's position in the trust hierarchy as a
  fourth Enrich source.

Whether this happens as a spec revision or a successor spec is a
sequencing question for the next design session.

### Migration

Two migration questions, related but distinct:

- **Albums imported before manifests exist.** Today's library has
  albums with no Layer B file. When manifest generation lands, do
  pre-existing albums get manifests opportunistically (on next
  Edit Metadata visit), via a batch tool, or not at all?
- **Albums imported before verification exists.** Once verification
  is in the manifest schema, manifests written before that point
  carry no verification state. Migration path is probably trivial
  (no flag = unverified) but should be named.

### Edit Metadata's relationship to the workflow (continued)

§ 2 of this memo proposes Edit Metadata = Structure replayed. The
audio-as-archive memo's pending questions in this area
(does Edit Metadata have a Load state? does it share Enrich? does
post-import editing re-open Structure?) become tractable under
that framing but still require a focused design pass.

## Cross-references

- [audio-as-archive-design-memo.md](audio-as-archive-design-memo.md)
  — the conceptual framing this memo extends; particularly § 2
  (Trust hierarchy as workflow) and § 4 (Structure as verification
  stage)
- [verification-model.md](verification-model.md) — stub doc the
  verification mechanics will eventually formalize
- [19-folder-import-and-manifests.md](19-folder-import-and-manifests.md)
  — the existing manifest spec that this memo's Layer B builds on
- [fingerprint-persistence-spec.md](fingerprint-persistence-spec.md)
  — per-track fingerprint tag persistence, the substrate for the
  curation lookup key
- [feature-parity-spec.md](feature-parity-spec.md) — historical
  record of how ImportView and EditMetadataView came to be at
  feature parity
- Commit `3ad318e` — Model B Commit 0, the first convergence of
  ImportView and EditMetadataView under a shared framing

## Do not

- Implement the curation layer without a focused design session
  resolving § Pending design above
- Extend `Data/concerts/*.json` to carry per-recording state
- Reference this memo as if it were a spec
- Treat § 2 (Edit Metadata = Structure replayed) as settled until
  it has been stress-tested in a design conversation
