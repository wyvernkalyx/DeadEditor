# Follow-Ups

A single tracked home for known deferred items, so they survive session
boundaries rather than depending on memory. Keep entries dry and factual —
this is a reference list, not a narrative.

## Known Deferred Items

Issues identified but not yet fixed. Each entry: brief description, where it surfaces, when noticed.

### Alias-learning double-write
- **Symptom:** Import view's right-click "Match to Song" writes the new alias to `songs.json` twice on a single match event.
- **Surfaced:** Commit `1840a44` deduped one such occurrence ("The Monkey and the Engineer" written twice for "Monkey And The Engineer").
- **Impact:** Dirty data in `songs.json`; harmless to matching (lookups tolerate dupes) but accumulates over time.
- **Likely location:** The code path that appends to a song's `Aliases` list after a user matches an unmatched track.

### Autocomplete control duplicated; no song-name autocomplete
- **What:** `AlbumNameSuggestions` (the TextBox + Popup + ListBox pattern) is duplicated between `EditMetadataView.xaml` and `ImportView.xaml`. Song names have no autocomplete at all — the setlist editor uses a type-then-Normalize pattern instead.
- **Proposed fix:** Extract the album-name pattern into a reusable `AutocompleteTextBox` user control, add a song-name autocomplete variant scoped by `LibrarySettings.PrimaryArtistName`, and adopt it across `EditMetadataView`, `ImportView`, and the box-set wizard.
- **Surfaced:** Phase A audit for box-set MVP.

### Atomic-write temp-and-rename pattern duplicated across call sites
- **What:** The temp-write-and-rename idiom is duplicated at 6+ call sites: `EditSetlistView.xaml.cs:270-284`, `MbidMigrationService.cs:405-408`, `NormalizationService.cs:414-418`, `ReleaseLookupService.cs:388-390`, `ShowLookupService.cs:292-294`, `SongsView.xaml.cs:391-393` (plus the box-set MVP's new copy).
- **Proposed fix:** Extract a shared `Json.WriteAtomic(path, obj)` helper. `ManifestService.WriteManifest` notably uses a non-atomic `File.WriteAllText` and would benefit from the same helper.
- **Surfaced:** Phase A audit for box-set MVP.

### Series identity lives in the release-name string, not structured fields
- **What:** Series identity (e.g. "Dave's Picks Vol. 43") currently lives in the release name string, not structured fields.
- **Proposed fix:** Extract `Series` + `SeriesNumber` into structured fields to enable sort/filter by series and numeric ordering (Vol. 2 vs Vol. 10). Non-blocking; do only if a real sort/filter need surfaces. Unrelated to box-set work.
- **Surfaced:** Concert-collapse doc commit (box-set taxonomy discussion).

### Box-set memo body describes the superseded two-level model
- **What:** The `## What a box set actually is` and `## Data model` sections of `box-set-design-memo.md` still describe the concerts → tracks two-level model. The 2026-05-28 concert-collapse decision entry supersedes them, but the section bodies were not rewritten (the doc commit captured the decision only).
- **Proposed fix:** Rewrite both sections to the flat `definition → tracks` model so the memo body matches its own top decision entry. Documentation-only.
- **Surfaced:** Concert-collapse doc commit.

### ConcertLookupService cache: editing a concert date desyncs the dictionary key
- **What:** `EditSetlistView` save writes `{newDate}.json`, but the in-memory `ConcertLookupService._concerts` dict stays keyed by the old date. Lookups by the new date miss the cache until restart; the old date still resolves to the now-mutated `ConcertReference`.
- **Why latent:** Today's coherence relies on in-place mutation of the shared cached instance; there is no invalidation/reload path. A date edit is the one case in-place mutation can't cover — the dictionary key, not just the value, changes.
- **Proposed fix:** Any redesign that stops sharing the live cached instance must add explicit cache invalidation (covers both this and the delete-no-evict item below). Pre-existing; restart-resolved.
- **Surfaced:** Concert-load perf audit.

### ConcertLookupService cache: deleting a concert does not evict it
- **What:** The `ShellWindow` delete path recycles `{date}.json` but leaves the entry in `ConcertLookupService._concerts`, so the deleted concert still resolves from the cache until restart.
- **Impact:** Stale cache hit for a deleted concert until app restart. Pre-existing; restart-resolved.
- **Surfaced:** Concert-load perf audit.
