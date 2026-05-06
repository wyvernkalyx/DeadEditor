# Verification Model (Stub — Future Design)

**Status:** Placeholder. Not implemented. Captures a design principle that emerged during Commit 1.5 of the MBID/fingerprint work. Intended to anchor the curation-layer design conversation in a future session.

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

## Required Fields (Concept)

A record cannot be marked verified unless certain required fields are populated and valid. The exact set is per-record-type, but the principle is that **verified means something specific** rather than being a sticker the user can apply arbitrarily.

Example for a concert/release record: a date is required, and must parse as a valid date.

## Open Questions for Future Design Session

These are not answered yet. Listing them so the future design conversation has a starting point.

1. **Granularity.** Single record-level `Verified` flag, or per-field provenance/verification? (Initial lean: record-level for v1.)
2. **State machine.** Are there intermediate states (`In Progress`, `Needs Review`)? (Initial lean: no — keep it binary for v1.)
3. **Required-fields enforcement.** Where does the validation live? On the record? On a per-record-type schema?
4. **Unlock UX.** Is unlocking destructive (clears `Verified`), or reversible (re-lock without re-confirming)? (Initial lean: unlocking clears `Verified`; user must re-confirm to re-verify.)
5. **External data interaction.** When MB/AcoustID/gdshowsdb has new data for a verified record, does the app surface a "suggested change" affordance, or stay silent? (Initial lean: silent for v1; "suggested changes" is a v2 feature.)
6. **Storage.** Where does the `Verified` flag live for each record type? (Concerts in `concerts/*.json`, releases in `release-details/*.json`, songs in `songs.json`, etc.)
7. **Track-title overwrite case (MBID re-match).** Today the MBID-driven 🔎 path overwrites local track titles via `ApplyMusicBrainzData`. Once the verification model exists, this becomes "verified records don't get auto-overwritten." Until then, this is a known papercut flagged in the Commit 1.5 conversation.

## Relationship to Other Future Work

- **Curation layer (per-release JSON files in `Data/release-details/`)** — verification status is a field on each release record
- **Fingerprint persistence (Commit 2 of the MBID chain)** — fingerprints attached to a verified release are the highest-trust identity signal in the system; this shapes how Commit 2's persistence layer is designed
- **GD-hardcoding cleanup** — unrelated; mentioned only because it's another deferred design item

## Do Not

- Implement this without a focused design session
- Add a `Verified` field to any record type ahead of that session
- Reference this stub as if it were a spec
