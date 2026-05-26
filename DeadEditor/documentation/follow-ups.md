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

### Settings menu slow to open after launch
- **Symptom:** ~26 seconds to open Settings after app launch.
- **Suspected cause:** UI-blocking startup work, likely concert load.
- **Impact:** Poor first-use experience; settings unreachable during startup window.

### Library view doesn't refresh after database reset in Settings
- **Symptom:** After resetting the database via Settings, the Library view continues to show stale state until the app is closed and reopened.
- **Workaround:** Close and reopen the app.
- **Impact:** Confusing UX; users may assume the reset failed.

### Autocomplete control duplicated; no song-name autocomplete
- **What:** `AlbumNameSuggestions` (the TextBox + Popup + ListBox pattern) is duplicated between `EditMetadataView.xaml` and `ImportView.xaml`. Song names have no autocomplete at all — the setlist editor uses a type-then-Normalize pattern instead.
- **Proposed fix:** Extract the album-name pattern into a reusable `AutocompleteTextBox` user control, add a song-name autocomplete variant scoped by `LibrarySettings.PrimaryArtistName`, and adopt it across `EditMetadataView`, `ImportView`, and the box-set wizard.
- **Surfaced:** Phase A audit for box-set MVP.

### Atomic-write temp-and-rename pattern duplicated across call sites
- **What:** The temp-write-and-rename idiom is duplicated at 6+ call sites: `EditSetlistView.xaml.cs:270-284`, `MbidMigrationService.cs:405-408`, `NormalizationService.cs:414-418`, `ReleaseLookupService.cs:388-390`, `ShowLookupService.cs:292-294`, `SongsView.xaml.cs:391-393` (plus the box-set MVP's new copy).
- **Proposed fix:** Extract a shared `Json.WriteAtomic(path, obj)` helper. `ManifestService.WriteManifest` notably uses a non-atomic `File.WriteAllText` and would benefit from the same helper.
- **Surfaced:** Phase A audit for box-set MVP.

### Box-set wizard review step: click-to-jump
- **What:** In `BoxSetWizardView`'s step-4 review surface, individual track rows and disc headers in the color-coded list are not clickable. A user inspecting a red (dangling) or amber (incomplete) track has to navigate manually back to step 2 or 3 and find the offender.
- **Proposed fix:** Wire `MouseLeftButtonUp` on track row Borders to call `ShowStep(3)` plus set `_selectedDiscIndex` to the matching disc and scroll the `TrackGrid` to bring the row into view. Same idea for clicking a disc header. For concert references, jump to step 2 with `ConcertListBox.SelectedItem` set.
- **Surfaced:** Phase B commit 4c (stretch goal in the brief, banked here per the brief's "if more than ~30 minutes, skip" guidance).
