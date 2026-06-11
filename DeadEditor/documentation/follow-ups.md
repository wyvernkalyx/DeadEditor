# Follow-Ups

A single tracked home for known deferred items, so they survive session
boundaries rather than depending on memory. Keep entries dry and factual —
this is a reference list, not a narrative.

## Known Deferred Items

Issues identified but not yet fixed. Each entry: brief description, where it surfaces, when noticed.

### In-app alert system (replace native MessageBox dialogs)
- **What:** The app uses native Windows `MessageBox` dialogs for the unsaved-changes prompt, delete confirmations, save-error reports, the setlist-editor save validations (invalid date, empty setlist), and now the duplicate-date refusal. These break visual consistency with the dark in-app UI and cannot be styled or positioned.
- **The sound, not just the dialog:** the `MessageBoxImage.Warning` icon plays the Windows warning chime. Gregg (2026-06-10): the **system alert sound interrupts his state of mind** as much as the dialog interrupts the screen — the audio startle is a distinct pain point from the visual inconsistency. Any in-app replacement should be **silent or use a non-startling cue**, not just a re-skinned modal that still chimes.
- **Call-site inventory this will sweep (grows as features land):** unsaved-changes prompt, delete confirmations, save-error reports; `EditSetlistView.SaveChangesAsync` invalid-date + empty-setlist validations; and the duplicate-date refusal added in commit `a6a652b` (`EditSetlistView.SaveChangesAsync`, `MessageBoxImage.Warning`).
- **Proposed fix:** Replace them with an in-app alert/dialog surface for visual consistency and control — silent (or softly cued) by default.
- **Surfaced:** Preference surfaced 2026-06-10; second lived-demand data point the **same day** during the Add Concert duplicate-date gate (the refusal chime, hit repeatedly while exercising collisions, is what surfaced the sound detail).

### Cold-start concert load (~17s cold, ~0.6s warm)
- **What:** The first `ConcertLookupService` load after a reboot/cache flush took **16,758ms** for 2,293 files; the immediate second run took **583ms** (measured 2026-06-10). Steady-state is fine — this is **NOT** a reopening of the closed Settings-perf item.
- **Possible mitigation (only if it ever bites):** kick the load on a background thread shortly after startup so the cache is warm before first Concerts use.
- **Surfaced:** 2026-06-10.

### ~~SetlistFetcher arg-parser guard~~ (DONE 2026-06-11)
- **Done:** Inline parse loop (`Program.cs:11-22`, `args.Length - 1` bound, positional next-token,
  no validation) replaced by the pure `tools/SetlistFetcher/ArgParser.Parse` (side-effect-free,
  throws `ArgParserException`). Guard rejects a missing value, a value that is itself a flag
  (the swallow), and any unknown token; `Program.cs` writes the message to stderr and exits
  non-zero before any fetch or write. Eight xUnit cases in `DeadEditor.Tests/ArgParserTests.cs`
  (new `SetlistFetcher` project reference); test baseline 338 -> 346.
- **What:** The arg parser accepted an option token as a value — `--concerts --output` silently set
  the concerts dir to the literal string `"--output"` during the 2026-06-10 gate, writing 2,292 files
  into a stray directory.
- **Surfaced:** 2026-06-10 implementation gate.

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

### ~~Add Concert capability (no in-app concert record creation)~~ (DONE 2026-06-10)
- **Done:** Shipped per [add-concert-spec.md](add-concert-spec.md) across four commits — `67be225`
  (spec), `7362cee` (grid-header **+ New Concert** entry point → blank editor), `a6a652b`
  (duplicate-date **refuse at save**, which also fixed a pre-existing mid-edit silent-overwrite via
  the `_originalDate` exclusion), `e182ec3` (pure `DuplicateDateRule` + five xUnit cases). Save floor
  left unchanged; hand-created concerts are born unverified. Phase A confirmed the substrate already
  handled blank `ConcertReference` instances end-to-end.
- **What:** There is no way to create a new concert record in-app — the concert store is fetcher-populated and `EditSetlistView` only edits existing records. Needed for actively touring artists (the app is multi-band by design; new shows happen and have no setlist.fm-sourced file yet).
- **Why low-friction:** `ConcertLookupService.NotifySaved` (commit `4c560a0`) already inserts an absent date key into the live cache, so the cache side is done. The work is the **UI entry point** — likely a "New Concert" action in `ConcertDatabaseView` that routes to `EditSetlistView` with a blank `ConcertReference` — plus first-save handling (a record with no prior file: the existing atomic write + `NotifySaved` path should cover it; confirm the blank-instance flow).
- **Verification tie-in:** a hand-entered concert is **born unverified** and uses the same `ConcertVerifyGate` to be marked verified (see `concert-verification-spec.md`). Record creation is its own feature, separate from verification.
- **Surfaced:** Concert-verification spec (2026-06-10).

### Unverify control on album + box-set surfaces (consistency with concerts)
- **What:** Concerts gained an explicit manual **Unverify** control (`concert-verification-spec.md`
  decision 6, amended 2026-06-10) so trust can be withdrawn without a fake edit. The album
  (`EditMetadataView`) and box-set (`BoxSetWizardView` Step 3) verify surfaces have **no** such
  control — they only unverify via the diff-at-save edit path.
- **Proposed fix:** Decide per-surface, in those surfaces' own sessions, whether to add the same
  low-emphasis Unverify control mirroring the concert one. Not implemented as part of the concert
  work. See amended decision 6 for the rationale and the persist-on-save symmetry.
- **Surfaced:** Concert-verification Unverify amendment (2026-06-10).

### ~~ConcertLookupService cache: editing a concert date desyncs the dictionary key~~ (FIXED)
- **Fixed:** rekey-on-save. `EditSetlistView` snapshots the concert's original date at
  construction and, after a successful save, calls `ConcertLookupService.NotifySaved(oldDate,
  concert)` — which evicts the old-date key, re-inserts the concert under its new date, and
  maintains `_sortedDates`. The orphaned `{oldDate}.json` is recycled with the same mechanism
  the delete path uses. (Shared-live-instance model retained; surgical fix, no reload system.)

### ~~ConcertLookupService cache: deleting a concert does not evict it~~ (FIXED)
- **Fixed:** evict-on-delete. The `ShellWindow` delete path now calls
  `ConcertLookupService.Evict(date)` after recycling `{date}.json`, removing the entry from
  both `_concerts` and `_sortedDates`. Both `Evict` and `NotifySaved` are idempotent (no throw
  on an absent key).
