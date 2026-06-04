# Box-Set Design Memo

**Status:** Design memo. No code yet. Sits alongside `audio-as-archive-design-memo.md` and `curation-layer-design-memo.md` as a sibling conceptual document.

**What this is for:** Establishing the data model, workflow, and surfacing rules for box sets before any implementation. The conversation that produced this memo started from three Wikipedia examples — *Listen to the River*, *Lyceum '72*, and *Enjoying the Ride* — which between them break every simple model of "box set = parent folder with concert subfolders." The memo captures what we decided, parking the things we deferred.

---

## Major decision: 2026-05-28 — concerts collapsed; date moves to the track

A box set is, by this app's definition, a bundle of multiple distinct shows or cross-show selections. *Europe '72: The Complete Recordings* is 22 complete shows; *So Many Roads* is 42 selections, each track from a different date across thirty years. A single-date concert parent object is therefore the wrong shape for a box set — the format is multi-date by nature.

The date is genuinely a property of the recording: the track. So the `BoxSetConcert` object collapses away. The model is now flat — `BoxSetDefinition → List<BoxSetTrack>` — with each `BoxSetTrack` carrying `{ TrackNumber, SongName, Date, SegueOut }`. Venue, city, and state are derivable from the date via `ShowLookupService` and are not stored; this stays derivable until real friction in use argues otherwise.

This supersedes the "two levels deep: definition → concerts → tracks" framing in the 2026-05-29 (disc removal) and earlier 2026-05-27 (set removal) entries. The model is one level: definition → tracks.

The "pull setlist for date" workflow (built in commit G) is the fast path: enter a date, pull that show's setlist from the show database, append tracks stamped with that date, then verify and prune. For a complete-shows box set, repeat per show date; for a cross-show compilation, dates are entered per track. The flat list serves both.

## Major decision: 2026-05-29 — disc abstraction removed

*(Superseded in part — the 2026-05-28 concert collapse flattened the model to definition → tracks; the "two levels deep" framing below is historical. See the top decision entry.)*

A late conversation during wizard implementation surfaced that the disc abstraction does not match DeadEditor's actual workflow. The app only ever sees audio as files in folders, not as discs. The clarifying example was *Enjoying the Ride* sold as a continuous digital download via dead.net — no discs to manage, just an ordered stream of tracks per concert.

The data model is now **two levels deep**: definition → concerts → tracks. Tracks live directly on concerts. Disc-prefixed track numbering (`101`, `503`, `1207`) is preserved as a *display and tagging convention only*, not a data structure. The hundreds digit is informational, not structural.

The rest of this memo has been edited surgically to reflect that decision. The "What a box set actually is" framing below describes the heterogeneous packaging that motivated the original design; the structural conclusions ("disc and concert as orthogonal axes") no longer apply.

## Major decision: 2026-05-27 — sets are not modeled in box-set definitions

*(Superseded in part — the 2026-05-28 concert collapse flattened concerts → tracks into one flat track list; the two-level framing below is historical. Sets stay unmodeled — `SetLabel` becomes a flat field on `BoxSetTrack` if ever needed. See the top decision entry.)*

The 2026-05-29 disc-removal entry collapsed sets into "no setlist sub-structure on concerts" without standalone reasoning. The standalone reasoning, captured here:

DeadEditor's job is mapping audio files to dates and song names. Set structure (Set 1 / Set 2 / encore) is a real musical concept, but other databases — gdshowsdb, setlist.fm — already record it definitively and with more research than DeadEditor will ever do. Replicating set structure here would be authoring downstream curation that adds no value to the core "songs with dates" goal.

In practice: the curator has never used set information as a filter, a search axis, or a listening-choice driver. Sets are decorative for DeadEditor's purpose.

Implication: box-set definitions are tagging templates, not curatorial records. Their job is to map (track in a box set) → (concert date, song name). Per-set structure is not part of that mapping.

If a future need surfaces — a real, concrete "I'd actually use this to do X" — sets can be added back as a string property on `BoxSetTrack` (e.g. `SetLabel`) without restructuring. The two-level model (concerts → tracks) accommodates it as a flat field; no nested type needed.

---

## What counts as a box set (vs. an official release)

The box-set feature exists to handle a shape a normal album does not. The distinction:

- **Official release** — one show, or one continuous run treated as a single listening unit. Dave's Picks, Dick's Picks, and Road Trips volumes all live here. A single volume is structurally a normal live album: a tracklist whose per-track dates all happen to be the same. Series identity ("Dave's Picks Vol. 43") currently lives in the release *name* string, not in structured fields. A series being complete (Dick's Picks, Road Trips) versus in-progress (Dave's Picks) does not change the classification — series status is a publishing fact, not a structural boundary. The tell: reclassifying a volume when its series finishes would be a no-op data change.
- **Box set** — a bundle of multiple distinct shows or cross-show selections, with its own identity as a collection. *Europe '72: The Complete Recordings*, *Pacific Northwest '73-'74*, *So Many Roads*.

Both shapes share the same atom: every track maps to (a date, a song name). In a complete-shows box set, all tracks of one show share a date. In a cross-show compilation, every track carries its own date. Either way, the track is where the date lives — which is exactly why concerts collapsed (see the decision entry above).

---

## What a box set actually is

A box set is a **commercial release** that contains audio from one or more concerts. The relationship between physical packaging and real-world events is **not regular**:

- *Listen to the River* (20 CDs, 7 concerts): concerts span disc boundaries. Disc 5 contains the tail of Dec 10, 1971 *and* the head of Oct 17, 1972.
- *Enjoying the Ride* (60 CDs, 21+ venues over 25 years): heterogeneous. 17 complete concerts, 3 compiled from multiple-night runs, bonus tracks from 4 entirely separate concerts inserted at various points, plus a cassette of partial-concert selections. Also distributed as a continuous digital download with no disc structure at all.

*Lyceum '72* was discussed but is a vinyl box set with side-level granularity and is not a realistic import target for this app. The data model still needs to be able to express "this slot has no audio" (for any future case where a track in a definition has no corresponding file) but the vinyl-side specifics are not driving the design.

**The model has to handle the heterogeneous cases above.** What we need is a list of concerts, each with an ordered list of tracks. The curator records whatever track numbers the source uses (disc-prefixed or continuous); DeadEditor stores them as opaque integers.

---

## Conceptual positions (banked, do not re-litigate)

1. **A box set produces multiple Library rows.** N+1 rows, where N is the number of concerts in the box: one row for the box set itself (album name = box-set name) and one row per concert (album name = box-set name + concert identifier). The same audio files underlie both projections; the difference is which manifest the row binds to.

2. **The same concert can legitimately appear in multiple Library rows.** If you own both *Fox Theatre, St. Louis, MO 12-10-71* (standalone release) and *Listen to the River* (which includes that concert), you have two rows on the Dec 10, 1971 date. That's correct — they are two distinct products. Deduplication is not the user's expectation here; the data shows what was bought.

3. **`BoxSetDefinition` is a curation artifact, not derived from audio.** It exists independently of having the audio files. The wizard builds it from authoritative sources (Wikipedia, dead.net, liner notes) with manual validation. Once defined, the definition is reusable forever — the next person who imports the same box set picks from the validated list rather than re-defining it.

4. **Curation precedes audio, and curation is bundled with the app.** Box-set definitions are a deliverable: they ship with DeadEditor in `Data/box-sets/`, validated by the maintainer. End users install the app and inherit the full set of canonical definitions without doing research themselves. The wizard exists for two purposes: (a) the maintainer uses it to author bundled definitions; (b) end users can create their own definitions for box sets the bundle doesn't yet cover. User-created definitions live in their AppData only and do not propagate. The MVP workflow is still curate-then-import: pick a definition (bundled or user-authored), then on import check "this is a box set" and pick from the validated list. The opposite direction (import-then-curate) is not in MVP.

5. **Date normalization is mandatory and the wizard's job.** Box-set sources will give dates in many formats ("Dec. 9, 1971", "12/9/71", "9 December 1971"). The wizard enforces `yyyy-MM-dd`. Whatever the source format was, that format does not survive into DeadEditor's data.

6. **Audio fingerprints are the matching primitive on import.** Per the curation-layer memo, fingerprint is the per-track identity. When audio is imported and matched against a `BoxSetDefinition`, fingerprint matches each FLAC to a slot in the definition. Unmatched tracks fall back to manual slot assignment in the import UI.

7. **Pt. 1 / Pt. 2 and other taper conventions are normalized out.** A sequence like "Not Fade Away Pt. 1 → Goin' Down the Road Feeling Bad → Not Fade Away Pt. 2" is taper-side notation for a segue chain. In DeadEditor it becomes three separate song entries with segue flags. The Pt. 1 / Pt. 2 markers do not survive into our metadata.

8. *(Resolved by removal — 2026-05-29.)* The original position addressed "First set, part 1 / Part 2" annotations that appear in liner notes when a set spans a disc boundary. With sets and discs both removed from the data model, this no longer applies. If the source labels tracks as "Set 1, Part 1 / Set 1, Part 2," the curator enters them as ordered tracks with appropriate segue flags; the labels do not survive into our metadata.

9. **Filenames are preserved; metadata is owned.** Whatever filenames the source provides remain on disk. DeadEditor's normalization applies to FLAC tags (and to per-track display in the app), not to filenames. The `BoxSetDefinition` drives FLAC tag values via the import flow. This matches the existing principle that DeadEditor never rewrites source files.

10. **Track numbering preserves the source's convention; the data model is agnostic.** When the source uses disc-prefixed encoding, track 3 of disc 5 is recorded as `503`, track 7 of disc 12 as `1207`. This matches DeadEditor's existing convention for multi-disc albums (Dave's Picks Vol. 43: 101-107, 201-210, 301-304, 401-404) and lets the disc identity survive into FLAC tags for source fidelity. But the encoding is a *display/tagging convention*, not a data structure — DeadEditor has no `Disc` entity. Box sets distributed as continuous digital downloads (e.g., the dead.net edition of *Enjoying the Ride*) carry whatever numbering scheme the source provides, and DeadEditor does not impose disc-prefixed numbering on them. The hundreds digit is informational, not structural.

11. **Per-concert manifests preserve the source track numbers.** A track recorded in the box-set definition as `503` stays `503` in the per-concert manifest for that track's concert. The number is the canonical identifier; concerts simply collect their tracks. This avoids tag-vs-manifest drift: the FLAC tag and the manifest entry refer to the track by the same number always. The per-concert row's first track shown may be `503` rather than `101`, which is mildly unusual visually but factually accurate.

12. **Concerts and releases are conceptually separate** but in MVP they are tracked together through manifests and album names. An explicit `Concert` entity (real-world event, distinct from any specific release of it) is a future concern, not an MVP one. Flagged here because some later feature ("show me everything I own from Dec 10, 1971") will eventually want it.

---

## Verification: two distinct states

Box sets have two independent trust questions, both worth surfacing:

**Definition completeness.** "Have I audited this `BoxSetDefinition` itself? Are all the concerts, tracks, dates, venues, and song names confirmed correct?" This is the analog of the existing `Verified` flag on `AlbumManifest`. It's a manual flip — the user reviews the definition and marks it verified. Verified definitions resist automated overwrites (consistent with the existing verification model).

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

One file per box set. Two locations are involved:

- **`Data/box-sets/<slug>.json`** (project-relative, in the repo) — the bundled location. Shipped with the app. The maintainer's wizard writes here when running in dev mode.
- **`%APPDATA%/DeadEditor/box-sets/<slug>.json`** — the user-runtime location. On first launch, bundled files are copied here. The end-user wizard writes here. All reads happen from here in production.

Dev mode (the `DEADEDITOR_DEV=1` environment variable) collapses both into the project-relative path: dev-mode reads and writes go straight to `Data/box-sets/`, bypassing AppData entirely. This means definitions authored by the maintainer in dev mode are immediately ready to commit, and the dev-mode running app sees those changes without any sync step.

Both paths use the same camelCase JSON shape (`ContractResolver = new CamelCasePropertyNamesContractResolver()`) and the same atomic temp-and-rename write pattern.

The slug is derived from the box-set name (algorithm TBD; see open questions).

A `BoxSetDefinition` carries top-level metadata (`version`, `name`, `releaseDate`, `label`, `catalogNumber`, `notes`, `verified`) and a single flat `tracks` array. Each track carries `trackNumber`, `songName`, `date`, and `segueOut`. The date lives on the track, not on an intervening concert object — the format is multi-date by nature (see the 2026-05-28 decision entry). Venue, city, and state (country for non-US shows) are derived from the date via `ShowLookupService` (`GetShowByDate(date)?.FormattedVenueLocation`) and are not stored.

No `concerts` array. No `discs` array. No `discCount` field. No `setlist` sub-structure. The data shape is one level deep: definition → tracks.

Serialized shape (camelCase; matches what the wizard writes):

```json
{
  "version": 1,
  "name": "Listen to the River: St. Louis '71 '72 '73",
  "releaseDate": "2021-10-08",
  "label": "Rhino",
  "catalogNumber": "...",
  "notes": "Limited edition of 13,000 numbered copies. 84-page hardcover book.",
  "verified": false,
  "tracks": [
    { "trackNumber": 101, "songName": "Truckin'", "date": "1971-12-09", "segueOut": false },
    { "trackNumber": 102, "songName": "Brown-Eyed Women", "date": "1971-12-09", "segueOut": false }
  ]
}
```

A few design notes on this shape:

- **`verified` is on the definition itself.** This is the "definition completeness" flag — has the user audited that the whole structure is correct. Default false.
- **`trackNumber` is an opaque integer.** When the source uses disc-prefixed encoding, the curator records `101, 102 ... 501, 502 ...` and so on. When the source uses continuous 1-to-N numbering, the curator records it that way. DeadEditor stores whatever the curator entered.
- **`date` is the per-track performance date** in `yyyy-MM-dd` (empty permitted while unknown). It is the grouping key in the wizard and the basis for the derived venue/city; a cross-show compilation carries a different date per track, a complete-shows box repeats one date across a run.
- **No concert object and no `id`.** Tracks for the same date are simply the tracks whose `date` matches; the wizard groups on that. Two performances on one date (early/late shows) are a parked open question (see below) — there is no per-concert identifier to disambiguate them in the flat shape today.
- **Fingerprints, no-audio slots, and external-concert bonus tracks** were design considerations for an earlier shape. When import wiring lands, equivalent affordances will be added to track entries. The data shape today is the curation-only MVP shape and intentionally minimal.

### Per-concert manifest

*(Uses the superseded two-level "concert" vocabulary — per-concert grouping is a future Layer-B import concern and will be revisited under the flat `List<BoxSetTrack>` model when import wiring lands. No manifest design is settled here.)*

When a box set is imported, in addition to the box-set's collection manifest, the app generates per-concert manifests. Each carries:

- The standard `AlbumManifest` fields, populated from the definition (date, venue, city, state).
- A reference to the parent box-set's definition slug, so the per-concert row can show provenance.
- The track list filtered to just the concert's tracks, with `trackNumber` values preserved from the definition (per position 11 above).

One set of audio bytes on disk. Multiple manifests referencing them.

---

## Workflow

### Curation (wizard) — MVP

A new top-level **Box Sets** view in the sidebar, sibling to Library / Import / Settings. The Box Sets view lists existing definitions. "+ New Box Set" opens the wizard.

The wizard has three steps:

1. **Top-Level Info** — name, release date, label, catalog number, notes.
2. **Concerts and Tracks** — a flat, editable track grid bound to `BoxSetDefinition.Tracks`. Columns: **TrackNumber** (editable int; Add defaults to the next sequential — max existing + 1, first row 1), **SongName** (free text), **Date** (`yyyy-MM-dd`, validated per track; empty permitted for not-yet-known dates), **SegueOut** (checkbox). Add appends a row; Delete (Del key, when not mid-edit) removes selected rows without renumbering. Edits flow into `BoxSetDefinition.Tracks` and persist on Save. Additionally, a **Pull setlist for date** action reads the gdshowsdb reference setlist (`ShowLookupService.GetSetlist`) for an entered `yyyy-MM-dd`, flattens it (set labels discarded, per-song segue preserved), and appends one track per song stamped with that date and numbered sequentially from the current max. Pull is no longer unconditionally append-only: if the target date already has rows in the box, a modal prompts **Replace / Append / Cancel** (Replace removes that date's existing rows then adds the pulled setlist; Append keeps the prior add-alongside behavior, duplicates included by design; Cancel, like window-close/Esc, does nothing). A date with no existing rows pulls with no prompt, exactly as before. Numbering is not reclaimed on Replace — the removed rows' numbers are not reused; the pull resumes from the remaining max+1, then the curator prunes/renumbers. Same-date collision is still purely a view/service interaction over the flat `List<BoxSetTrack>` (the predicate is the pure `BoxSetPullCollision.HasTracksForDate`); serialization is unchanged. Per-song dedup remains rejected (intentional setlist repeats). Song-name normalization against `songs.json` is a deferred follow-up, not part of this grid. The grid can be **grouped by date** (a "Group by date" toggle in the step-2 action row, default on) with collapsible per-date headers showing the derived venue (`ShowLookupService.GetShowByDate`) and track count. The headers behave as an **accordion**: exactly one date group is expanded at a time, and the expanded group is always the last-touched date — the date just pulled, added to, deleted from, or whose Date cell was edited. The active date is held in a transient `ActiveGroupDate` (set before each view refresh so a regenerated header realizes already in the right state), and each header's expand state is driven from it through `GroupActiveConverter`. The authority for the match — including the null-vs-empty rule: `null` means nothing is expanded (a fresh box is all-collapsed) while a deliberate `""` matches the no-date group — lives in the pure `BoxSetGroupHeader.IsActiveGroup`. A whole date can be removed in one action via a ✕ control on each group header (commit H3): it prompts a Yes/No confirm with the track count, then drops every row for that date through the pure `BoxSetTrackMutations.RemoveTracksForDate` helper (same ordinal/empty-date identity as the pull-collision predicate, so the `(no date)` group is removable too). Removing a date collapses the accordion to all-collapsed *only* when the removed date was the open group — deleting a different, collapsed group leaves the open group expanded (`ActiveGroupDate` is nulled only on a match). TrackNumbers on surviving rows are not renumbered, matching the per-row delete. Grouping (and which group is open) is purely a view concern — the model stays a flat `List<BoxSetTrack>` and serialization is unchanged. A **Renumber** action (a third bulk button in the step-2 action row, after Pull) re-sequences every track's `TrackNumber` to a contiguous `1..N` ordered by **`(Date ascending, then existing TrackNumber ascending)`**, with no-date (`""`) tracks sorted **last**, through the pure `BoxSetTrackMutations.RenumberByDate` helper. This is the standing fix for two symptoms of the global `max+1` numbering: out-of-order pulls (a date pulled later otherwise keeps lower numbers than an earlier date, so its block renders above — group display order is a side effect of TrackNumber) and the gaps left by whole-date / per-row deletes — neither of which previously had a remedy short of delete-and-reimport-in-order. Renumber is **non-destructive** (no row added or removed), so unlike the whole-date delete it carries **no confirm prompt**; and because dates are untouched the accordion's active group stays valid (no `ActiveGroupDate` change — a single `_tracksView.Refresh()` re-renders the groups in the new order). Unlike grouping, Renumber is the one step-2 action that **does change serialization**: the helper physically reorders the backing list so the persisted `tracks` array order matches the new numbering (array order == TrackNumber order == chronological). The renumber is **flat and unconditional** — any hand-entered disc-prefixed numbering (`101`, `503`; positions 10/11) is overwritten. Segues are unaffected: each show's within-date order is preserved, and there is no live "next track" computation over `BoxSetTrack` today. A **disc-prefixed-preserving** renumber variant is banked until import wiring lands and gives those numbers a downstream consumer (positions 10/11).
3. **Review** — read-only summary of the definition with validation, color-coded track display (initially all dimmed since no audio is matched yet), and the Save action.

The wizard can also be entered in edit mode for an existing definition (**implemented**:
double-click / Enter a saved row in the Box Sets list). The edit copy is a **fresh read from
disk** by slug (`BoxSetService.Read`) — the deserialized object is the isolated editable buffer,
so there is **no in-memory clone**; edits never touch the file until Save and Cancel discards by
dropping the wizard. Edit-mode prefills the step-1 fields, lands on step 2, and titles the header
"Edit Box Set". Saving resolves
to one of four outcomes via the pure `BoxSetSaveResolution.Resolve(originalSlug, newSlug,
targetSlugExists)` — **SaveNew** (new box, free name), **Overwrite** (editing, slug
unchanged — overwriting the box's own file is not a collision), **MoveRename** (editing,
name changed to a free slug), and **NameCollision** (the target slug belongs to a
*different* box; refuse and ask for a new name). A new box is the no-original case. A
rename is handled **write-new-then-delete-old**, deliberately in that order: a failed
delete leaves a recoverable orphan, whereas delete-first would risk data loss. The
resolver shipped first (with the new-box guard rewired onto it, no behavior change); the
edit-mode load and the `Overwrite`/`MoveRename` file I/O landed in the follow-up commit.

The wizard never touches audio. It is pure curation data entry.

### Import (post-MVP, but designing the seam now)

The import flow gains a "**This is a box set**" checkbox. When checked, the user picks from the list of existing `BoxSetDefinition`s. The import then:

1. Reads each FLAC file's fingerprint (computing if absent).
2. Matches each fingerprint against the track entries in the chosen definition. Most will match by position if the imported folder structure mirrors the source ordering. Fingerprint disambiguates when ordering is uncertain.
3. For any track that does not match, the user gets a manual slot picker — pick which concert and track number this file is, confirming the assignment.
4. On successful import, writes:
   - The box-set parent manifest (collection row).
   - Per-concert manifests, one per concert defined.
   - Track-level FLAC tags per the definition (song name, segue, date, track number).
   - The matched fingerprint back to the `BoxSetDefinition` (so future imports of the same box set from other sources auto-match).
   - Filenames are not changed.

Import flow is **not in the MVP commit slice.** The MVP ships the wizard and the data model. Import wiring is a follow-up.

### Library surfacing

After import, a box set produces N+1 rows in Library:

- One **collection row** (album name = box-set name, e.g., "Listen to the River"). When clicked, drills into a view of the whole box: all concerts and their tracks, with the color-coded "have it / don't have it" indicator.
- One **per-concert row** for each of the N concerts in the box (album name = `<box-set name>: <concert date short form>` or similar — exact format TBD). These rows appear in the date-sorted Library view alongside audience recordings and standalone official releases of the same date.

The existing `MergeOfficialReleasesByAlbumName` does not apply to box-set rows — they have distinct identities by design.

---

## Multi-band considerations

Nothing in `BoxSetDefinition` is band-specific. The wizard's song autocomplete pulls from `songs.json`, which is scoped by `LibrarySettings.PrimaryArtistName` — same gating used elsewhere. Concert dates, venues, and setlists are data, not code. Grateful Dead box sets and Phish box sets share the same data model and the same wizard UI; only the autocomplete source differs.

**Hard rule (per project conventions):** no hardcoded artist data, no Grateful Dead specifics. Grateful Dead is part of the data, not the code.

---

## Performance

A box set like *Enjoying the Ride* has ~287 tracks across 21+ concerts. Storage is trivial (one JSON file). Three real performance concerns:

1. **Wizard responsiveness during data entry.** Filling out 287 tracks will stress the grid. The wizard's step 2 is a single flat track grid grouped by date, with the per-date headers collapsible as an accordion (one group expanded at a time). Collapsing keeps the realized row count bounded to the open date's tracks even when the box spans 21+ dates. (An earlier design paginated by concert via a left-hand concert pane; that did not ship — the flat date-grouped grid replaced it.)
2. **Fingerprint matching at import.** 287 fingerprints to compute and match. fpcalc is single-threaded per process; this needs the parallelization already on the horizon (per `userMemories`).
3. **Color-coded track list rendering.** A 287-row visual indicator must render without UI stutter. Existing virtualization patterns should handle this; flag for measurement.

Measure as we go. The 287-track case is the stress test; the typical small box set will not surface these.

---

## What's in MVP (the first commit slice)

1. **Data model.** `BoxSetDefinition` C# class + serialization. The `Data/box-sets/` directory convention. JSON read/write with atomic temp-and-rename.
2. **Box Sets view in the sidebar.** A new top-level view, navigable from the existing sidebar pattern. Lists existing definitions (read from disk). "+ New Box Set" button.
3. **Wizard for creating and editing.** Three-step form per the spec above, with per-concert paging to handle large box sets. Saves to disk on completion. Validates date format. Shows the color-coded track list in the review step (initially all dimmed; no audio yet).
4. **Persistence.** Atomic write per the existing file conventions. The write uses the inline temp-and-rename pattern that already appears at six call sites in the project (canonical example: `EditSetlistView.xaml.cs:270-284`). Extracting it to a shared `Json.WriteAtomic(path, obj)` helper is deferred to its own commit chain (tracked in `follow-ups.md`). The target path is `%APPDATA%/DeadEditor/box-sets/` in distributed builds and `Data/box-sets/` when `DEADEDITOR_DEV=1` is set; both share the same write code path.
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

7. **Update model for shipped definitions.** When the app ships an update that changes a previously-shipped definition (typo fix, song-name correction, added concert), what happens to the user's existing AppData copy? Possibilities: (i) silently overwritten on first run after update; (ii) merged with the user's version, preserving user edits; (iii) prompts the user; (iv) only overwritten if the user hasn't marked the definition `Verified`. This needs a decision before the first real update ships, but is not on the MVP critical path.

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
