# Verification and Manifest Wiring: Design Memo

## Status

This is a design memo, not a spec. It captures the conceptual decisions
made during the session of 2026-05-18, banks the resolved positions for
future implementation work, and names what remains deferred.

Sibling to:
- [audio-as-archive-design-memo.md](audio-as-archive-design-memo.md)
- [curation-layer-design-memo.md](curation-layer-design-memo.md)

The memo is a banking document. It authorizes the implementation prompts
that follow this session but does not authorize any work beyond what is
named here. Each "Deferred" item below is its own future conversation.

## Background

The session of 2026-05-11 produced
[curation-layer-design-memo.md](curation-layer-design-memo.md), which
named the curation layer as foundational to the verification model and
identified five "Pending design" questions:

1. Verification granularity and storage
2. Structure-complete gate
3. Stepper-as-buttons UX
4. Manifest spec reconciliation
5. Migration

The session of 2026-05-18 ran the Phase A diagnostic that the previous
session deferred, then took up the design conversation those findings
enabled. The diagnostic surfaced one major finding that reshaped the
conversation: `Services/ManifestService.cs` and `Models/AlbumManifest.cs`
are fully implemented (commit `9b36b4f`, 2026-04-04, "Implements the
manifest spec from documentation/19"). Zero callers exist. The dormancy
is intentional scaffolding, not abandoned work — the spec's Step 3
(manifest-aware import) was the explicit next step, and the curation-
layer memo's framing in May reframed Step 3's requirements (per-track
fingerprint, verification fields, fingerprint-keyed lookup) before the
wiring landed.

This memo banks the resolved positions from the 2026-05-18 design
conversation. It does not answer every question in front of the
verification model — it answers the ones MVP needs.

## Resolved positions

### 1. Verification is page-level

A single boolean `verified` on the manifest. The whole album is either
verified or not. No per-field state. No per-track state.

This is a deliberate reversal of an initial in-session lean toward
per-field verification. The reversal was made on consideration of:

- The implementation surface area of per-field state (verification
  block listing field names, rules for which fields are user-curatable
  vs. derived, unverify-on-edit logic per field, UI per field, key
  stability across rename/Renumber/song-name-edit operations)
- The recovery of the "bank partial progress" benefit via the Archivist
  Note field (§ 2 below), which models *why* a field is uncertain rather
  than just *that* it is
- The asymmetry of future migration: page-level → per-field is additive;
  per-field → page-level is a reduction with unclear semantics

The verification-model stub's "initial lean: record-level for v1"
(verification-model.md, open question 1) is now the resolved position.

**Implementation status:** ❌ Schema change pending. The existing
`AlbumManifest.VerifiedAt: DateTime` field is being repurposed (see § 4
below); the new `verified: bool` field is additive.

### 2. Archivist Note

A free-text manifest-only field carrying curator context — sources
consulted, lineage information, open questions about the recording,
decisions made and their justification.

Properties:

- **Manifest-only.** Never written to FLAC tags.
- **Free-text, no length cap.** No structured sub-fields imposed by
  schema; the user can adopt internal conventions (e.g. "Sources: ..." /
  "Open questions: ..." prefixes) and the schema can be promoted in a
  future revision if conventions stabilize.
- **Edits do not unverify.** Editing the note is editing context about
  the recording, not editing the recording's metadata. This is the one
  carve-out to the unverify-on-edit rule (§ 5 below).
- **Edit surface: right sidebar, below album artwork** in
  EditMetadataView.

This field also recovers the principal cost of page-level verification:
the user can record "venue uncertain — possibly Fillmore or Winterland"
in the note, banking the uncertainty without modeling it in the
verification data. Arguably better than per-field unverified, because it
carries *why*, not just *that*.

**Implementation status:** ❌ Schema and UI change pending.

### 3. Required fields for verification

Verification requires:

- **Date present and parseable as a valid date.** "1971-02-19" passes;
  "circa 1971" or empty fails.
- **Artist non-empty.**
- **Album Type set** (already enforced by the combobox; no new
  enforcement needed).

All other fields may be empty and the album can still be verified —
empty is sometimes the correct value (a studio album has no venue).

The audit did not probe enforcement, so existing field requirements
elsewhere in the code may differ. This is a verification-action
requirement, not a manifest schema requirement; the schema permits any
state to be persisted.

**Implementation status:** ❌ Verification action does not exist yet.

### 4. Manifest schema additions

Version bumps from 1 to 2. The amended schema:

```json
{
  "version": 2,
  "folderName": "...",
  "albumName": "...",
  "albumType": "AudienceRecording",
  "artist": "...",
  "date": "1971-02-19",
  "venue": "...",
  "city": "...",
  "state": "...",
  "edition": "",
  "officialRelease": "",
  "verified": false,
  "archivistNote": "",
  "manifestSavedAt": "2026-05-12T12:00:00Z",
  "tracks": [
    {
      "filename": "01 - Morning Dew (1971-02-19).flac",
      "trackNumber": 1,
      "discNumber": 1,
      "title": "Morning Dew (1971-02-19)",
      "songName": "Morning Dew",
      "trackDate": "1971-02-19",
      "segue": false,
      "acoustIdFingerprint": "AQADtMmSRJGS..."
    }
  ]
}
```

Changes from version 1:

- **`verified: bool`** added (page-level verification flag)
- **`archivistNote: string`** added (curator context)
- **`acoustIdFingerprint: string`** added per `ManifestTrack`
  (curation-layer memo § 5: fingerprint as the lookup key, has to live
  in the manifest body for fingerprint-keyed reverse lookup)
- **`VerifiedAt: DateTime` → `manifestSavedAt: DateTime`** rename. The
  semantics change: this field is now "timestamp the manifest file was
  last written," not "timestamp of verification." Under page-level
  verification with single boolean state, a separate verification
  timestamp is not modeled. If staleness detection becomes a need
  later, a `verifiedAt` field can be re-added then.

Decision notes:

- The `albumName` field is kept as-is. The conversational Album Title vs
  Song Title distinction is application vocabulary, not a schema
  requirement; standard metadata uses `ALBUM` / `TALB` and the existing
  `AlbumInfo.AlbumName` property establishes the in-memory naming. The
  schema follows the established naming rather than introducing a third
  variant.

- The audit found `Edition` and `OfficialRelease` fields in
  `AlbumManifest` that are not in the spec's example JSON
  (19-folder-import-and-manifests.md). The amended schema keeps these
  as-is.
- The spec's existing manifest format is not contradicted — the
  additions are additive and the rename is mechanical. Reading a
  version-1 manifest should be handled gracefully (default `verified` to
  false, default `archivistNote` to empty, treat `VerifiedAt` as
  `manifestSavedAt`).

**Implementation status:** ❌ All additions pending.

### 5. Unverify-on-edit

Any edit to a verified album's metadata fields (other than the Archivist
Note) drops the `verified` flag back to `false`. The album returns to
unverified state and the user can re-verify with an explicit action once
satisfied with the new state.

This rule is uniform: it does not matter which field, which view, or
whether the edit came from typing or from Enrich (Match Setlist /
MusicBrainz). Any change to a verified manifest's content unverifies it.

The one carve-out: editing the Archivist Note does not unverify, per
§ 2 above.

**Implementation status:** ❌ Logic pending.

### 6. UI for verification state changes

Two distinct scenarios with distinct UI treatments:

**Scenario A — manual edit drops verification.** The user typed the
change; they know what they changed. UI surfaces:

- The verified/unverified badge in the sidebar flips state
- A status-line message ("Verification cleared by edit") below the
  toolbar

**Scenario B — Enrich (Match Setlist or MusicBrainz) overwrites
fields.** The user did not type the changes; they need to see what
happened. UI surfaces:

- **Pre-apply preview** before any change is written: a dialog showing
  the diff per-field and per-track, with the user accepting or rejecting
  changes. Builds on the existing `MbidCandidateDialog` (which already
  has per-field opt-in checkboxes) for MusicBrainz. Match Setlist needs
  a new dialog with the same shape. The dialog supports per-track
  selection (accept this row, reject that one), not all-or-nothing.
- **Post-apply amber left-edge marker** on changed cells/inputs, until
  next save. Color: amber, drawing from the existing palette's
  `PillSkippedAccent` tone. Marker is session-only — cleared on save or
  on navigation away (which discards unsaved changes per the existing
  Cancel flow).
- The verified/unverified badge flips state, same as Scenario A.

**Match Setlist specifically:** today's behavior (immediate rewrite
without preview) is acceptable for unverified albums. For verified
albums, the preview dialog gates the changes. The unverify trigger fires
only if any change is accepted — rejecting all changes leaves
verification intact.

**`MbidCandidateDialog` parity:** today the dialog has album-level
per-field opt-in (apply Title? apply Artist? apply Year? apply Track
Titles?). For verified albums, the "apply track titles" section should
surface track-by-track diffs with per-track selection, matching the
Match Setlist dialog. Track-level granularity within the existing
album-level structure.

**Implementation status:** ❌ All UI work pending. The Match Setlist
dialog does not exist. The `MbidCandidateDialog` per-track parity does
not exist. The badge UI does not exist. The amber-marker UI does not
exist.

### 7. Manifest read and write triggers

**Write triggers (when `WriteManifest` is called):**

- End of `LibraryImportService.ImportToLibrary` on success — every
  successful import writes a fresh manifest with `verified: false` and
  empty `archivistNote`
- `EditMetadataView.SaveChangesButton_Click` — every save rewrites the
  manifest with current state, including the current `verified` flag
- The verify action triggers a save — clicking Verify in EditMetadataView
  is functionally equivalent to clicking Save Changes, with `verified`
  set to true before the save runs. One write path, not two. Any pending
  in-flight edits are persisted as part of the verify action.

**Read triggers (when `ReadManifest` is called):**

- `EditMetadataView` open — reads the manifest if present, falls back to
  reading from FLAC tags if not (opportunistic migration, § 9 below)
- `ImportView.LoadFolderAsync` — fingerprint-keyed lookup against all
  existing manifests (§ 8 below)

**Implementation status:** ❌ All wiring pending. The service is fully
implemented (commit `9b36b4f`); only call sites are missing.

### 8. Fingerprint-keyed lookup at folder load

The mechanic that delivers on "import once, verify once, never again."

**Trigger point:** at folder load (`ImportView.LoadFolderAsync`), not at
import click. The user knows immediately whether the recording is
already in their library.

**Fingerprint source:** existing `ACOUSTID_FINGERPRINT` tags on the
source files. Per the fingerprint-persistence-spec, these are written
during import; tracks that have already been fingerprinted carry the
fingerprint as a Vorbis/ID3 tag. If most tracks in a source folder lack
fingerprints, the lookup surfaces a notification ("fingerprints missing
for this folder — click [Lookup] to fingerprint and match") rather than
auto-computing. fpcalc is expensive; the lookup does not block the user
on it.

**Match algorithm:** for each manifest in the library, count the
fingerprint overlap with the incoming track set. The denominator is the
smaller of (incoming track count, manifest track count) — i.e. "do most
of the tracks on the smaller side match?"

**Quorum:** majority (>50%) on the smaller-side denominator. Strict
("all tracks must match") is too fragile against real-world variation;
threshold (configurable percentage) adds tuning surface area no one will
tune.

**Match outcomes:**

- **No match:** standard import flow as today, manifest written at
  import end with `verified: false`
- **Single match:** show compare dialog. User reviews per-field and
  per-track diffs, accepts or rejects per row.
  - If most changes accepted: import proceeds with manifest values,
    verified state carries through, imported album lands verified
  - If most changes rejected: import proceeds with incoming values, the
    existing manifest is overwritten, verified flag resets to false
- **Multiple matches:** show a chooser dialog listing each candidate
  with overlap count and verified status. User picks one, then the
  single-match flow runs against that selection. Multiple matches
  usually mean something specific; auto-picking is fragile.

**Implementation status:** ❌ Not in MVP. Lookup logic, compare dialog,
chooser dialog all pending. This is post-MVP work scoped as a separate
implementation prompt sequence. The schema's `acoustIdFingerprint` per
track (§ 4) is the substrate for this; landing the schema in MVP enables
the lookup later.

### 9. Migration

Opportunistic. Pre-existing albums (those in the library today without a
manifest) get manifests written when the user next opens them in
EditMetadataView and saves. Until then, they read from FLAC tags as
today. No batch tool. No one-shot migration.

Pre-existing albums always land as `verified: false` with empty
`archivistNote` when their first manifest is written.

The implementation falls out of the read and write triggers (§ 7):
opening an album with no manifest reads from FLAC tags, saving writes
the manifest. No special-case migration code path is needed.

**Implementation status:** ❌ Pending, but pending only the read/write
triggers themselves.

## The workflow framing

The 2026-05-18 session also accepted the curation-layer memo § 2's
working position as the resolved framing for the workflow:

> Edit Metadata is the Structure stage, replayed post-import.

Specifically: **the workflow is a property of the album, not the view.**
ImportView and EditMetadataView are two surfaces onto the same workflow.
ImportView runs the workflow from Load through Import. EditMetadataView
runs the workflow at the Structure stage, on an album that has already
passed through Load, Enrich, Clean, and Import.

The stages name the **source of a field's current value**, not a
category the field belongs to. Enrich is "external source contributed
this." Structure is "user reviewed and confirmed." Any field can be
edited in either stage; editing in EditMetadataView is Structure work
regardless of whether the field's value originally came from Enrich or
from the user.

### Consequences for MVP

- **EditMetadataView is the primary surface for Structure work.**
  Sidebar gets the verified/unverified badge and Archivist Note.
- **No stepper in EditMetadataView for MVP.** The framing supports a
  stepper there (the user is in the Structure stage; a stepper could
  show that), but the existing stepper (per Phase A Item 2) is
  imperatively built and would need real work to be useful in
  EditMetadataView. The sidebar badge covers what the user needs to
  know. Adding a stepper later is open; not in MVP.
- **The current ImportView stepper's heuristics stay as MVP.** Phase A
  Item 1 found that Load = "files present", Enrich = "MBID present",
  Clean = "no date-tagged titles", Structure = "80% canonical names",
  Import = "source folder under library root." Under the new framing,
  several of these are conceptually wrong (e.g. Enrich now means
  "external sources consulted" which would include Match Setlist and
  fingerprint-matched manifests, not just MBID presence; Structure now
  means "verified state set", not an 80% heuristic). A stepper rewrite
  is on the runway, not blocking.
- **Match Setlist / MusicBrainz / Normalize / Renumber duplication
  between the views (per Phase A Items 3 and 4) is technical debt.**
  Future consolidation passes are expected, not optional. MVP wiring of
  `ManifestService` proceeds with the duplication intact — both inline
  implementations write the same things.

## MVP scope

The implementation work authorized by this memo, in commit-sized prompts:

1. **Schema + serialization.** Bump `AlbumManifest` version to 2; add
   `verified`, `archivistNote`, per-track `acoustIdFingerprint`; rename
   `VerifiedAt` → `manifestSavedAt`. Update `WriteManifest` and
   `ReadManifest` to handle the new fields and the version-1 →
   version-2 read path. Round-trip tests.
2. **Verification badge + Archivist Note UI in EditMetadataView
   sidebar.** Below album artwork. Wire data binding to the album's
   manifest state. Read-only for now (no write side yet).
3. **Wire `WriteManifest`.** Call from
   `EditMetadataView.SaveChangesButton_Click` and from
   `LibraryImportService.ImportToLibrary` post-success. Opportunistic
   migration falls out: opening an album with no manifest loads from
   tags, saving writes a new manifest. Add the verify action that
   triggers a save with the flag set.
4. **Unverify-on-edit and amber-marker UI.** Detect edits to manifest
   fields while the album is verified; drop the flag; surface the badge
   change and status-line message. Add the amber left-edge marker for
   fields changed since last save. Include the Archivist Note carve-out.
5. **Verification-model stub update (doc-only).** Update
   `documentation/verification-model.md` to close the questions this
   session resolved (granularity, unlock UX, external data interaction,
   storage, track-title overwrite), reduce the stub to actually-still-
   open questions (state machine binary-or-not, required-fields
   enforcement location), and point at this memo as the authoritative
   reference. No code changes. Lands after prompts 1–4 since the stub
   text will reference the just-shipped implementation.

Each prompt is independently testable, independently committable.
Out-of-scope lists in each prompt enforce that scope creep does not
absorb post-MVP work.

## Deferred

Out of MVP. Scoped as separate future prompts or future sessions:

- **Enrich preview dialogs.** Match Setlist preview dialog (new). Per-
  track parity in `MbidCandidateDialog`. The post-apply amber markers
  ship in MVP; the pre-apply preview gates ship after.
- **Fingerprint-keyed lookup at folder load.** Lookup logic, compare
  dialog, chooser dialog. The schema's `acoustIdFingerprint` per track
  ships in MVP as the substrate.
- **Consolidation work.** ImportView and EditMetadataView's inline
  duplication of Match Setlist, Normalize, Renumber. Extracting the
  inline implementations to shared services.
- **Stepper rewrite.** Aligning the ImportView stepper's stage
  computations with the resolved framing (stages name source-of-value,
  not field categories). The current heuristics stay in place during
  MVP.
- **Stepper in EditMetadataView.** The framing supports it; MVP does
  not include it.
- **Doc reconciliation.** Phase A Item 4 surfaced that the toolbar
  inventory at `01-main-window.md:159-168` is materially out of date.
  Not in MVP. Will be needed when the next design conversation touches
  the toolbar.
- **Manifest spec reconciliation.** `19-folder-import-and-manifests.md`
  predates this memo and the curation-layer memo. The spec needs
  revision (or replacement) to reflect: per-track fingerprints in the
  manifest body, the `verified` and `archivistNote` fields, fingerprint-
  keyed match replacing date-based match, the read/write triggers. The
  revision is a separate session.

## Cross-references

- [audio-as-archive-design-memo.md](audio-as-archive-design-memo.md)
  — upstream framing; particularly § 2 (Trust hierarchy as workflow) and
  § 4 (Structure as verification stage)
- [curation-layer-design-memo.md](curation-layer-design-memo.md) — the
  parent memo this one extends; particularly § 1 (workflow is a property
  of the album), § 2 (Edit Metadata as Structure replayed), § 4 (two
  layers), § 5 (fingerprints as lookup key)
- [verification-model.md](verification-model.md) — stub the
  verification mechanics will eventually formalize
- [19-folder-import-and-manifests.md](19-folder-import-and-manifests.md)
  — the manifest spec being extended; needs revision (see Deferred)
- [fingerprint-persistence-spec.md](fingerprint-persistence-spec.md) —
  the substrate for the curation lookup key
- Phase A diagnostic report (in-conversation, 2026-05-18) — the audit
  findings this memo's decisions reference
- Commit `9b36b4f` (2026-04-04) — introduced `ManifestService` and
  `AlbumManifest`; "Implements the manifest spec from documentation/19"
- Commit `5495b1f` (2026-05-05) — added `PathGuard.Validate` to
  `WriteManifest` as part of the source-protection 0a/0b/0c chain

## Do not

- Implement past MVP scope (§ MVP scope) without a new design
  conversation
- Treat the framing (§ The workflow framing) as license to consolidate
  inline duplications during MVP work — consolidation is its own future
  prompt
- Reference this memo as if it were a spec
- Re-derive the page-level verification decision; it is settled. If it
  needs revisiting, that is a new design conversation that reopens § 1.
- Extend the manifest schema beyond § 4 without a design conversation.
  Adding fields opportunistically during implementation is exactly the
  kind of accumulated drift the design conversations are meant to
  prevent.
