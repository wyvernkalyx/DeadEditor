# Verification Model (Status Doc)

**Box-set extension: see box-set-verification-spec.md.**

**Status:** Partially formalized. The page-level verification model and its manifest storage shipped in the MVP wiring chain (commits `2415bfb` through `0d56b90`, 2026-05-18 through 2026-05-20). This stub retains the open architectural questions that the MVP did not resolve.

**Authoritative reference:** [verification-and-manifest-wiring-design-memo.md](verification-and-manifest-wiring-design-memo.md) banks the resolved decisions. Read it for the page-level decision, the unverify-on-edit rule, the manifest-as-storage decision, and the diff-via-preview pattern for external-data interactions.

**See also:** [audio-as-archive-design-memo.md](audio-as-archive-design-memo.md) § 2 (Trust hierarchy as workflow) and § 4 (Structure as verification stage) for the design framing this stub doc will eventually formalize.

## The Principle

Curated records (releases, concerts, songs, anything we maintain canonical truth for) carry a verification status. The minimum viable shape is two states:

- **Unverified** — data came from external sources (MusicBrainz, AcoustID, gdshowsdb) or is incomplete
- **Verified** — the user has inspected the record, confirmed it is correct, and locked it

A verified record is the highest-trust signal in the system. External data sources may suggest changes, but cannot silently overwrite verified data. Editing verified data requires an explicit unlock action by the user.

## Why This Matters

The app's existence-test is "faster and more accurate than a spreadsheet for maintaining a deep live-recording library." Verification status is the mechanism by which the app's data quality compounds over time:

- Every record the user touches and confirms moves from unverified to verified
- Verified records resist drift from automated re-imports, re-scans, and external-source pulls
- Verified records can be shared (or compared) with confidence — they represent real human curation work

Without an explicit verification model, the app cannot tell the difference between "this data is correct because the user confirmed it" and "this data is correct because MusicBrainz said so the last time we asked."

## Library Grid Surface (shipped 2026-05-24)

The first user-facing surface for verification state outside the Edit Metadata view. A narrow leading icon column in the Library grid shows each row's verification at a glance.

**Data model (shipped, unchanged).** Verification is a single boolean `Verified` on `AlbumManifest` — page-level and binary per album folder. No per-field, no per-track verification. (Resolved Q1 / Q6 below.)

**Row-level summarization for the grid.** A Library row (`LibraryShow`) can represent one folder or — for box sets / multi-folder official releases collapsed by `MergeOfficialReleasesByAlbumName` — several. The grid summarizes the underlying manifest booleans into a tri-state `VerificationState` (`Unverified` / `Partial` / `Verified`):

- Single-folder rows are binary: `Verified` iff the manifest's `Verified` is true, else `Unverified`.
- Merged multi-folder rows are `Verified` when all underlying folders are verified, `Unverified` when none are, and `Partial` when some but not all are.

The summarization runs at grid-population time in `LibraryGridView.PopulateVerificationState` (eager manifest reads, parallelized). `LibraryShow.VerifiedFolderCount` carries the underlying verified count so tooltips can say "N of M folders verified" without re-reading manifests.

**Visual vocabulary.** Leading column, glyph-only, reusing the existing App.xaml brushes:

- `Verified` → ✓ in the verified-badge green (`BadgeVerifiedBg`, `#0E7A0D`)
- `Partial` → ◐ in amber (`MarkerAmberAccent`, `#F0AD4E`)
- `Unverified` → empty cell (absence is the indicator)

The column is present only in the album (Library) view; it is absent in "Shows I Don't Have" mode (no owned manifest) and By-Date mode (rows are `DateRow`, not `LibraryShow`), where verification is not meaningful.

**Still open.**

- The Album Detail view badge is a separate follow-up (not shipped here).
- A Library filter by verification state (All / Verified / Partial / Unverified) is the next step.
- The tri-state summarization rule (none/some/all → Unverified/Partial/Verified) is settled for the grid; whether `Partial` should surface anywhere beyond a derived row summary is a v2 question, related to Q2's state-machine discussion.

## Required Fields (Concept)

A record cannot be marked verified unless certain required fields are populated and valid. The exact set is per-record-type, but the principle is that **verified means something specific** rather than being a sticker the user can apply arbitrarily.

Example for a concert/release record: a date is required, and must parse as a valid date.

## Open Questions for Future Design Session

These are not answered yet. Listing them so the future design conversation has a starting point.

1. **Granularity.** ✅ Resolved. MVP shipped page-level verification (single boolean per album manifest). The initial lean toward record-level for v1 became the settled position; per-field provenance is not modeled. See wiring memo § 1.

2. **State machine.** ❌ Still open. MVP shipped binary `verified: true | false`. Whether v2 should add intermediate states (e.g. `In Progress`, `Needs Review`) is undecided — the v1 binary is settled, the v2 question is not.

3. **Required-fields enforcement.** 🟡 Partial. MVP listed three required fields — Date (parseable), Artist (non-empty), and Album Type (already enforced by ComboBox) — and validates them inline in the verify button handler (`EditMetadataView.VerifyButton_Click`). See wiring memo § 3. The architectural question — central validation framework? per-record-type schema? declarative vs. imperative? — was not addressed. Per-button inline validation is the MVP shape; whether that scales beyond the verify action is open.

4. **Unlock UX.** ✅ Resolved. MVP shipped unverify-on-edit (wiring memo § 5; commit `0d56b90`): any edit to a manifest-tracked field drops the `verified` flag. There is no separate unlock step. Re-verifying requires an explicit click of the Verify button. Implicitly destructive — but the new state is just "unverified," not "data discarded," so the destruction is bounded. The Archivist Note is the one carve-out (editing it does not unverify).

5. **External data interaction.** 🟡 Partial. Wiring memo § 6 settled "diff via preview" as the chosen approach for both Match Setlist and MusicBrainz Enrich paths. MVP shipped the **post-apply** side: amber left-edge markers on changed cells (commit `0d56b90`) plus unverify-on-Enrich-apply (same commit). MVP did **not** ship the **pre-apply** side: the diff-preview dialogs that gate changes before they are written. Those are deferred per memo § Deferred ("Enrich preview dialogs").

6. **Storage.** ✅ Resolved. The album manifest (Layer B, per-album JSON in each album folder) is the storage home for `verified`. See wiring memo § 4 and § 7. The earlier stub's enumeration of candidate stores (`concerts/*.json`, `release-details/*.json`, `songs.json`) is superseded — the manifest replaces all of them for the album-verification case.

7. **Track-title overwrite case (MBID re-match).** ✅ Resolved by Q5's resolution. Verified records' track titles cannot change silently in MVP — unverify fires on direct edits and on Enrich apply (both manual and MusicBrainz paths, commit `0d56b90`). The "verified records don't get auto-overwritten" guarantee is enforced for the apply-side. Full pre-apply preview dialogs (which would let the user see and reject specific track-title overwrites before they happen) are post-MVP per Q5.

## Relationship to Other Future Work

- **Curation layer** — now exists as the Layer B per-album manifest. See wiring memo § 4 (Two layers). The stub's original phrasing ("per-release JSON files in `Data/release-details/`") was superseded; the curation layer landed as `AlbumManifest` written to each album folder, not as a separate release-details tree.
- **Fingerprint persistence** — shipped per [fingerprint-persistence-spec.md](fingerprint-persistence-spec.md). `AcoustIdFingerprint` is now persisted per-track in `ManifestTrack` (wiring memo § 5) and is the substrate for the deferred fingerprint-keyed lookup at folder load (memo § 8).
- **GD-hardcoding cleanup** — unchanged. Still unrelated, still open.

## Do not (post-MVP)

- Treat this stub as a spec. The wiring memo and the implementation commits are authoritative; this stub documents the still-open architectural questions for future design conversations.
- Add intermediate verification states without a focused design session resolving Q2.
- Centralize required-fields enforcement without a focused design session resolving Q3.

## See also

- [verification-and-manifest-wiring-design-memo.md](verification-and-manifest-wiring-design-memo.md) — authoritative reference for the resolved decisions (page-level verification, manifest-as-storage, unverify-on-edit, diff-via-preview)
- [curation-layer-design-memo.md](curation-layer-design-memo.md) — parent memo (Layer A setlist authority vs Layer B per-recording manifest; fingerprints as curation lookup key)
- [audio-as-archive-design-memo.md](audio-as-archive-design-memo.md) — upstream framing (trust hierarchy as workflow; Structure as verification stage)
- [19-folder-import-and-manifests.md](19-folder-import-and-manifests.md) — manifest spec being extended; reconciliation deferred per wiring memo § Deferred
- [fingerprint-persistence-spec.md](fingerprint-persistence-spec.md) — substrate for the curation lookup key

Implementation chain (2026-05-18 → 2026-05-20):

- `2415bfb` — docs: add verification and manifest wiring design memo
- `4827b89` — feat: AlbumManifest schema v2 (verified, archivistNote, per-track fingerprint)
- `9e09a2f` — feat: verification badge + Archivist Note UI in EditMetadataView sidebar
- `50615e6` — feat: wire ManifestService — read on load, write on save, verify action
- `5f4cd73` — feat: write manifest at end of LibraryImportService.ImportToLibrary
- `22ca76e` — chore: promote stepper brushes to App.xaml, add badge + marker names
- `0d56b90` — feat: unverify-on-edit + amber left-edge markers in EditMetadataView
