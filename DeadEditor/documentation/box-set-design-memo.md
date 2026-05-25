# Box-Set Design Memo

**Status:** Design memo. No code yet. Sits alongside `audio-as-archive-design-memo.md` and `curation-layer-design-memo.md` as a sibling conceptual document.

**What this is for:** Establishing the data model, workflow, and surfacing rules for box sets before any implementation. The conversation that produced this memo started from three Wikipedia examples — *Listen to the River*, *Lyceum '72*, and *Enjoying the Ride* — which between them break every simple model of "box set = parent folder with concert subfolders." The memo captures what we decided, parking the things we deferred.

---

## What a box set actually is

A box set is a **commercial release** that contains audio from one or more concerts, packaged across multiple discs. The relationship between physical containers (discs) and real-world events (concerts) is **not regular**:

- *Listen to the River* (20 CDs, 7 concerts): concerts span disc boundaries. Disc 5 contains the tail of Dec 10, 1971 *and* the head of Oct 17, 1972.
- *Enjoying the Ride* (60 CDs, 21+ venues over 25 years): heterogeneous. 17 complete concerts, 3 compiled from multiple-night runs, bonus tracks from 4 entirely separate concerts inserted at various points, plus a cassette of partial-concert selections.

*Lyceum '72* was discussed but is a vinyl box set with side-level granularity and is not a realistic import target for this app. The data model still needs to be able to express "this slot has no audio" (for any future case where a disc/track in a definition has no corresponding file) but the vinyl-side specifics are not driving the design.

**The model has to handle the heterogeneous cases above.** Any design that requires "disc N = concert N" is wrong from the start.

The right abstraction is to keep **disc** and **concert** as orthogonal axes, linked by a many-to-many mapping of track ranges. A disc has tracks numbered sequentially within the box-set-wide numbering scheme. A concert is a real-world event with a date, venue, and setlist. A box-set definition says "tracks 101-107 plus 201-202 are the December 9, 1971 Fox Theatre concert."

---

## Conceptual positions (banked, do not re-litigate)

1. **A box set produces multiple Library rows.** N+1 rows, where N is the number of concerts in the box: one row for the box set itself (album name = box-set name) and one row per concert (album name = box-set name + concert identifier). The same audio files underlie both projections; the difference is which manifest the row binds to.

2. **The same concert can legitimately appear in multiple Library rows.** If you own both *Fox Theatre, St. Louis, MO 12-10-71* (standalone release) and *Listen to the River* (which includes that concert), you have two rows on the Dec 10, 1971 date. That's correct — they are two distinct products. Deduplication is not the user's expectation here; the data shows what was bought.

3. **`BoxSetDefinition` is a curation artifact, not derived from audio.** It exists independently of having the audio files. The wizard builds it from authoritative sources (Wikipedia, dead.net, liner notes) with manual validation. Once defined, the definition is reusable forever — the next person who imports the same box set picks from the validated list rather than re-defining it.

4. **Curation precedes audio.** The MVP workflow is curate-then-import: define the box set in the wizard, then on import check "this is a box set" and pick from the validated list. The opposite direction (import-then-curate) is not in MVP.

5. **Date normalization is mandatory and the wizard's job.** Box-set sources will give dates in many formats ("Dec. 9, 1971", "12/9/71", "9 December 1971"). The wizard enforces `yyyy-MM-dd`. Whatever the source format was, that format does not survive into DeadEditor's data.

6. **Audio fingerprints are the matching primitive on import.** Per the curation-layer memo, fingerprint is the per-track identity. When audio is imported and matched against a `BoxSetDefinition`, fingerprint matches each FLAC to a slot in the definition. Unmatched tracks fall back to manual slot assignment in the import UI.

7. **Pt. 1 / Pt. 2 and other taper conventions are normalized out.** A sequence like "Not Fade Away Pt. 1 → Goin' Down the Road Feeling Bad → Not Fade Away Pt. 2" is taper-side notation for a segue chain. In DeadEditor it becomes three separate song entries with segue flags. The Pt. 1 / Pt. 2 markers do not survive into our metadata.

8. **"First set, part 1" / "First set, part 2" annotations are dropped on import.** Some box-set liner notes label tracks this way when a set spans a disc boundary. That is a packaging artifact. The set is the set; the disc split is metadata of the physical product, not of the performance. Tracks join set 1 (or whichever set) and the part-1/part-2 distinction does not survive.

9. **Filenames are preserved; metadata is owned.** Whatever filenames the source provides remain on disk. DeadEditor's normalization applies to FLAC tags (and to per-track display in the app), not to filenames. The `BoxSetDefinition` drives FLAC tag values via the import flow. This matches the existing principle that DeadEditor never rewrites source files.

10. **Track numbering uses disc-prefixed encoding, box-set-wide, starting at 101.** Track 3 of disc 5 is `503`. Track 7 of disc 12 is `1207`. Track 4 of disc 20 is `2004`. This matches DeadEditor's existing convention for multi-disc albums (Dave's Picks Vol. 43: 101-107, 201-210, 301-304, 401-404). No special continuous-1-to-N numbering for box sets. Box sets that span 20 discs go up to `20XX` in the hundreds-disc encoding. (Hypothetical 100+ disc box sets would need a wider encoding; not a near-term concern.)

11. **Per-concert manifests preserve the box-set-wide track numbers.** The Oct 17, 1972 concert from *Listen to the River* spans tracks (say) `503` through `707`. The per-concert manifest for that row carries those exact numbers — not a renumbered `101`-and-up. This avoids tag-vs-manifest drift: the FLAC tag and the manifest entry refer to the track by the same number always. The per-concert row's first track shown will be `503` rather than `101`, which is mildly unusual visually but factually accurate.

12. **Concerts and releases are conceptually separate** but in MVP they are tracked together through manifests and album names. An explicit `Concert` entity (real-world event, distinct from any specific release of it) is a future concern, not an MVP one. Flagged here because some later feature ("show me everything I own from Dec 10, 1971") will eventually want it.

---

## Verification: two distinct states

Box sets have two independent trust questions, both worth surfacing:

**Definition completeness.** "Have I audited this `BoxSetDefinition` itself? Are all the discs, tracks, concert dates, venues, and setlists confirmed correct?" This is the analog of the existing `Verified` flag on `AlbumManifest`. It's a manual flip — the user reviews the definition and marks it verified. Verified definitions resist automated overwrites (consistent with the existing verification model).

**Audio completeness.** "Do I have all the FLAC files this definition expects?" This is *computed*, not manually flipped. The definition declares N tracks. The library has M of them. The completeness state is `M/N` — derivable, always current, requires no user maintenance.

These are independent. You can have:

- Verified definition + complete audio: green-light everything.
- Verified definition + 0% audio: the wizard works fine as a standalone curation tool, no files yet.
- Verified definition + 50% audio: you own half the box (legitimate — some collectors buy partial releases, some have lost files, etc.).
- Unverified definition + complete audio: you have all the files but haven't audited the definition yet.

**UI implications (not MVP, but designed for):**

- The Box Sets view shows each definition with its verification status and its audio-completeness ratio (e.g., "✓ Verified · 287/287 tracks" or "Unverified · 18/287 tracks").
- The box-set drill-in view shows a **color-coded track list**: tracks present in the library render normally, tracks not yet imported render dimmed/marked. This makes "what's missing" legible at a glance — useful even when the user owns 0 tracks (the wizard's review is essentially this view).
- The Library grid's existing `VerificationState` indicator on a box-set collection row reflects definition completeness only. Audio completeness gets its own surface (probably in the drill-in, possibly a secondary indicator in the grid — TBD at implementation time).

---

## Updating definitions after import

When a `BoxSetDefinition` is edited (typo fix, venue correction, song-name normalization), existing per-concert and collection manifests for that box set may be out of date. The handling:

- **Unverified manifests auto-update.** The new definition values get written to the manifest and to the FLAC tags. The user did not commit to the prior values, so silent correction is appropriate.
- **Verified manifests prompt.** A verified manifest represents a state the user explicitly confirmed. Changing a definition does not silently re-rewrite it. Instead, the user gets a "definition changed for [Listen to the River]. Re-apply changes to verified records? [Re-apply] [Skip]" prompt the next time those manifests are read (or on a one-shot "audit definitions" action — exact trigger TBD).

Auto-update writes require the `WithFileReleased` plumbing built for the file-lock bug fix (commits 8dff255 / 63a87c7). That infrastructure is already in place.

---

## Data model

### `BoxSetDefinition`

One file per box set, in `%APPDATA%/DeadEditor/box-sets/<slug>.json`. This follows the live-write convention concerts already use: the directory is created on first save if it doesn't exist, and user-created definitions live in AppData so they survive upgrades and reinstalls. (A bundled seed under `Data/box-sets/` — the way concerts ship their first-run seed — may be added later; not in MVP.) The slug is derived from the box-set name (algorithm TBD; see open questions).

Conceptual shape (final field names settled during implementation):

```json
{
  "version": 1,
  "name": "Listen to the River: St. Louis '71 '72 '73",
  "releaseDate": "2021-10-08",
  "label": "Rhino",
  "catalogNumber": "...",
  "notes": "Limited edition of 13,000 numbered copies. 84-page hardcover book.",
  "discCount": 20,
  "verified": false,
  "concerts": [
    {
      "id": "1971-12-09",
      "date": "1971-12-09",
      "venue": "Fox Theatre",
      "city": "St. Louis",
      "state": "MO",
      "country": "USA",
      "setlist": [
        { "set": 1, "songs": [
          { "name": "Truckin'", "segueOut": false },
          { "name": "Brown-Eyed Women", "segueOut": false }
        ]},
        { "set": 2, "songs": [] },
        { "set": "encore", "songs": [] }
      ]
    },
    { "id": "1971-12-10" }
  ],
  "discs": [
    {
      "discNumber": 1,
      "discName": null,
      "tracks": [
        {
          "trackNumber": 101,
          "title": "Truckin'",
          "duration": "12:47",
          "concertId": "1971-12-09",
          "songName": "Truckin'",
          "segueOut": false,
          "fingerprint": null
        }
      ]
    }
  ]
}
```

A few design notes on this shape:

- **`verified` is on the definition itself.** This is the "definition completeness" flag — has the user audited that the whole structure is correct. Default false.
- **`trackNumber` follows the disc-prefixed convention.** Disc 1 tracks are `101, 102, 103...`. Disc 5 tracks are `501, 502, 503...`. Disc 20 tracks are `2001, 2002...`. Same convention DeadEditor already uses for multi-disc albums.
- **Concerts have their own `id`.** Using the date as the ID works for a band that played at most once a day. If a box set contains two performances on the same date (early/late shows), the ID can become `1971-12-09-early` / `1971-12-09-late`.
- **Setlists live on concerts, not on discs.** The setlist is the canonical statement of "what was played that night." Disc tracks reference the concert and the song; the concert's setlist is the authoritative source.
- **Disc tracks carry their own `songName` and `segueOut`.** Even though the concert's setlist has the same info, the disc-track copy is what gets written to FLAC tags on import. Keeping it explicit avoids a lookup at every read.
- **`fingerprint` is null until first import.** Once a successful match happens (or a manual assignment is confirmed), the fingerprint is persisted on the disc track. The next time someone imports the same box set from a different source, fingerprint matches against this.
- **No-audio slots are representable but rare.** A disc track with no expected audio (decorative side, gap in the source, etc.) can be flagged. The data shape allows it; UI support comes when needed.
- **External-concert bonus tracks** (e.g., *Enjoying the Ride*'s bonus tracks from a different concert than the rest of the disc) can be represented by giving them a `concertId` referring to a one-off concert added to the `concerts` array. The data shape handles this without special-casing; UI support comes when needed.

### Per-concert manifest

When a box set is imported, in addition to the box-set's collection manifest, the app generates per-concert manifests. Each carries:

- The standard `AlbumManifest` fields, populated from the definition (date, venue, city, state).
- A reference to the parent box-set's definition slug, so the per-concert row can show provenance.
- The track list filtered to just the concert's tracks, with `trackNumber` values preserved from the definition (per position 11 above).

One set of audio bytes on disk. Multiple manifests referencing them.

---

## Workflow

### Curation (wizard) — MVP

A new top-level **Box Sets** view in the sidebar, sibling to Library / Import / Settings. The Box Sets view lists existing definitions. "+ New Box Set" opens the wizard.

The wizard is a multi-step form:

1. **Top-level info** — name, release date, label, catalog number, notes, total disc count.
2. **Concerts** — for each concert in the box: date (`yyyy-MM-dd` enforced), venue, city, state, country, setlist. Setlist entry follows the existing `EditSetlistView` pattern: the user types song names freely, then a Normalize action fuzzy-matches against `songs.json` (scoped by `LibrarySettings.PrimaryArtistName`) to canonicalize them. Live autocomplete does not exist in the project today; it is a future enhancement (tracked in `follow-ups.md`).
3. **Discs** — for each disc: number, optional name, list of tracks. Each track gets a number (auto-filled per disc-prefixed convention, user-editable), title, duration, and a dropdown to assign which concert it belongs to.
4. **Review** — full view of the definition. Highlight any tracks not assigned to a concert. Show the future "color-coded track list" surface (initially all dimmed since no audio is matched yet). Save writes the JSON.

The wizard can also be entered in edit mode for an existing definition.

The wizard never touches audio. It is pure curation data entry.

### Import (post-MVP, but designing the seam now)

The import flow gains a "**This is a box set**" checkbox. When checked, the user picks from the list of existing `BoxSetDefinition`s. The import then:

1. Reads each FLAC file's fingerprint (computing if absent).
2. Matches each fingerprint against the disc-track entries in the chosen definition. Most will match by position if the imported folder structure mirrors the box set (one folder per disc, sequential tracks). Fingerprint disambiguates when ordering is uncertain.
3. For any track that does not match, the user gets a manual slot picker — pick which disc and track number this file is, confirming the assignment.
4. On successful import, writes:
   - The box-set parent manifest (collection row).
   - Per-concert manifests, one per concert defined.
   - Track-level FLAC tags per the definition (song name, segue, date, disc number, track number).
   - The matched fingerprint back to the `BoxSetDefinition` (so future imports of the same box set from other sources auto-match).
   - Filenames are not changed.

Import flow is **not in the MVP commit slice.** The MVP ships the wizard and the data model. Import wiring is a follow-up.

### Library surfacing

After import, a box set produces N+1 rows in Library:

- One **collection row** (album name = box-set name, e.g., "Listen to the River"). When clicked, drills into a view of the whole box: 20 discs, their tracks, the concerts they map to, with the color-coded "have it / don't have it" indicator.
- One **per-concert row** for each of the N concerts in the box (album name = `<box-set name>: <concert date short form>` or similar — exact format TBD). These rows appear in the date-sorted Library view alongside audience recordings and standalone official releases of the same date.

The existing `MergeOfficialReleasesByAlbumName` does not apply to box-set rows — they have distinct identities by design.

---

## Multi-band considerations

Nothing in `BoxSetDefinition` is band-specific. The wizard's song autocomplete pulls from `songs.json`, which is scoped by `LibrarySettings.PrimaryArtistName` — same gating used elsewhere. Concert dates, venues, and setlists are data, not code. Grateful Dead box sets and Phish box sets share the same data model and the same wizard UI; only the autocomplete source differs.

**Hard rule (per project conventions):** no hardcoded artist data, no Grateful Dead specifics. Grateful Dead is part of the data, not the code.

---

## Performance

A 60-CD box like *Enjoying the Ride* has ~287 tracks across 21+ concerts. Storage is trivial (one JSON file). Three real performance concerns:

1. **Wizard responsiveness during data entry.** Filling out 287 tracks via a single monolithic form will not be pleasant. The wizard should be broken into per-disc views (one disc visible at a time) rather than a flat 287-row table. This is an implementation decision but worth flagging at design time.
2. **Fingerprint matching at import.** 287 fingerprints to compute and match. fpcalc is single-threaded per process; this needs the parallelization already on the horizon (per `userMemories`).
3. **Color-coded track list rendering.** A 287-row visual indicator must render without UI stutter. Existing virtualization patterns should handle this; flag for measurement.

Measure as we go. The 60-disc case is the stress test; the typical 3-6 disc case will not surface these.

---

## What's in MVP (the first commit slice)

1. **Data model.** `BoxSetDefinition` C# class + serialization. The `Data/box-sets/` directory convention. JSON read/write with atomic temp-and-rename.
2. **Box Sets view in the sidebar.** A new top-level view, navigable from the existing sidebar pattern. Lists existing definitions (read from disk). "+ New Box Set" button.
3. **Wizard for creating and editing.** Multi-step form per the spec above, with per-disc paging to handle large box sets. Saves to disk on completion. Validates date format. Surfaces unassigned tracks before save. Shows the color-coded track list in the review step (initially all dimmed; no audio yet).
4. **Persistence.** Atomic write per the existing file conventions. The write uses the inline temp-and-rename pattern that already appears at six call sites in the project (canonical example: `EditSetlistView.xaml.cs:270-284`). Extracting it to a shared `Json.WriteAtomic(path, obj)` helper is deferred to its own commit chain (tracked in `follow-ups.md`).
5. **Verification flag.** The `verified` boolean on the definition is settable from the Box Sets view (or wizard review step). Unverified by default.

That is the whole MVP. It is sized to one commit chain (probably 4-6 surgical commits).

## What's NOT in MVP

- Import flow ("this is a box set" checkbox, fingerprint matching, per-concert manifest generation).
- Library grid changes (the N+1 row generation).
- Box-set drill-in view (the collection row's detail view, with live color-coded "have it / don't have it" against imported audio).
- The "definition changed; re-apply to verified manifests?" prompt.
- Any change to existing `AlbumManifest` schema.
- Auto-population from external sources (Wikipedia scraping, dead.net API).
- The explicit `Concert` entity (Concerts-as-distinct-from-Releases).
- Decorative-slot and external-concert-bonus-track UI support (the data shape allows for them; UI comes when needed).

Each non-MVP item is a separate future commit chain.

---

## Open questions (parked, not blocking MVP)

These do not need answers to ship MVP.

1. **Slug derivation.** What's the algorithm for turning "Listen to the River: St. Louis '71 '72 '73" into a filename slug? Lowercase, alphanumeric + hyphens, strip apostrophes and colons? Need a deterministic rule that won't collide.

2. **Triggering the "re-apply to verified manifests?" prompt.** On next read of an affected manifest? On a one-shot "audit my definitions" action from the Box Sets view? Both? Decided at implementation time for the relevant follow-up commit chain.

3. **Box-set verification in the Library grid indicator.** The existing `VerificationState` column shows `Verified / Partial / Unverified`. For box-set collection rows, definition-completeness drives this. Audio-completeness is a separate signal — where it surfaces (drill-in only? secondary indicator in the grid? tooltip enhancement?) is open.

4. **Naming convention for per-concert rows from a box set.** "Listen to the River: 1971-12-09" vs. "1971-12-09 - Fox Theatre - Listen to the River" vs. something else. Affects how rows sort and how they read in the grid. Decided at the Library-surfacing commit chain.

5. **Two performances on the same date.** Concert `id` becomes `1971-12-09-early` / `1971-12-09-late` (or similar). Confirm the format when we hit a real case.

6. **Versioning of `BoxSetDefinition`.** The shape will evolve. `version: 1` is a forward marker. When (not if) the shape changes, we need a migration path. Mirror the `AlbumManifest` v1→v2 pattern when it comes.

---

## What this memo does not establish

Anything not in "Conceptual positions" or "What's in MVP" is undecided. Specifically:

- Wizard visual design (layout, step transitions, validation messaging).
- Whether the wizard is modal, full-page, or a separate window.
- The drill-in view from a box-set collection row.
- The exact slug algorithm.
- Whether fingerprint matching uses Chromaprint or AcoustID's tighter matching mode for box-set tracks.
- The exact per-concert row naming format.

Those become design conversations or implementation calls during the relevant commit chains.

---

## Next step

Phase A audit: read the existing `Data/` file conventions (`release-details/`, `concerts/`), `LibrarySettings`, the existing sidebar navigation pattern, the existing wizard-style UI (if any), and the existing autocomplete patterns. Confirm the data model and view-addition fit the existing architecture without surprise. Then a Phase B implementation chain in 4-6 surgical commits.
