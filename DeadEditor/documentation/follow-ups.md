# Follow-Ups

A single tracked home for known deferred items, so they survive session
boundaries rather than depending on memory. Keep entries dry and factual —
this is a reference list, not a narrative.

## Known Deferred Items

Issues identified but not yet fixed. Each entry: brief description, where it surfaces, when noticed.

### ~~Banked 2026-06-12 (import normalization)~~ (DONE 2026-06-17)
- **Strip leading track-number prefix before canonical song matching:**
  - **Done:** Shipped as the pure `TrackNumberPrefix.Strip` helper + contract (`21ecfa8`) and its match-key wiring into `NormalizationService.Normalize` (`0d6381f`, manual import gate PASS). Hypothesis confirmed (Phase A): the regression was the leading-track-number strip lost when matching moved from the now-dead `MetadataService.CleanTitle` to `TitleStructureParser`. Option B (match-key only) — the stored FLAC tag/`RawTitle` are untouched; a false strip merely misses the lookup and the original survives in the dialog. Open question resolved: stripped+matched titles **auto-resolve** (skip the dialog). Intended boundary: bare-space auto-resolve (`01 Bertha`) fires only on the **Normalize** path (which carries a track number to gate it), not the Match Setlist path — by design; separator/disc-token forms auto-resolve everywhere. `songs.json` verified clean (no prefix pollution, no digit-leading titles). Spec: [track-number-prefix-normalization.md](track-number-prefix-normalization.md).
  - **Symptom:** importing a concert whose track titles carry an embedded number prefix (e.g. `01 - Bertha`, `02 - Good Lovin'`) sends every track to the Unmatched Songs dialog even though the canonical name exists in `songs.json`; the dropdown pre-fills the raw prefixed string instead of the matched canonical name. Likely common given taper/ripper folder-naming conventions.
  - **Hypothesis (untested):** the matcher compares the full raw title against canonical names without normalizing away a leading `<digits><separator>` prefix.
  - **Direction (not designed; needs Phase A):** normalize only the comparison key used for matching — never mutate the stored FLAC tag (source-of-truth rule). Strip a leading track-number prefix across separator variants (` - `, `-`, `. `, `) `). Safety: no supported artist's canonical title legitimately begins with `NN -` (multi-band by design); the disc-prefixed track number (e.g. `Track 101`) is distinct from an in-title `01` and must not be conflated.
  - **Open question:** should a stripped+matched title auto-resolve and skip the Unmatched dialog, or only pre-select the correct dropdown entry for user confirmation?
  - **Status:** Resolved — see **Done** above.

### Banked 2026-06-12 (alert-system increment-9 gate)
- **Advanced Search song criteria not wired to any result:** the Contains / Exclude / Sequence tabs collect `SelectedSongs`/`ExcludedSongs`/`SongSequence` into public properties, but the caller (`HeaderBar_AdvancedSearchRequested`, `ShellWindow.xaml.cs:316`) calls `dialog.ShowDialog()` and discards the result — nothing reads the criteria, so a song search closes the dialog with no visible effect. Only the Track Search tab (its own in-dialog grid) works. Pre-existing (predates the alert sweep). Fix: consume the criteria after `ShowDialog()` and filter the library grid / present results.
- **Match Setlist button styling:** the Match Setlist button in the `EditMetadataView` toolbar renders washed-out / unreadable (owner screenshots 2026-06-12). Restyle for contrast against the dark toolbar.
- **OS SaveFileDialog OverwritePrompt chimes:** the Manage Songs export (and other `SaveFileDialog` pickers) raise the native overwrite-confirm, which chimes — outside the in-window alert sweep (Ruling: file/folder pickers are out of scope). Consider an in-window confirm after the picker returns if the chime bothers. Surfaced 2026-06-12 (inc-9 gate).

### Banked 2026-06-12 (alert-system increment-7 gate)
- **Delete-affordance discoverability:** song delete (#44 trigger, SongsView row right-click → "Remove") and track delete (#15 trigger, AlbumDetailView track-grid right-click → "Delete Track") not findable by the owner; surface or relabel, then review #15's OK/Cancel labels.
- **Setlist editor song autocomplete:** realtime lookup against `songs.json` while typing (replaces free-type-then-Normalize flow). Lived demand 2026-06-12.
- **Concert grid sort:** persist sort state across navigation within a session; add per-column sort-direction indicators. Lived demand 2026-06-12.
- **SongsView grid layout:** alias column placement/justification inconsistent with other grids.
- **Releases view redesign:** owner flag 2026-06-12 — "needs a lot of attention".

### In-app alert system (replace native MessageBox dialogs)
- **Authoritative spec + inventory:** [alert-system-spec.md](alert-system-spec.md) (Status: Proposed). That spec's 45-site table is now the authoritative MessageBox inventory and supersedes the partial list this entry used to carry; the binding rulings (banner for notifications, in-window confirm host for decisions, no third-party packages, A/B never interleaved) and the five-increment rollout live there.
- **What:** The app uses native Windows `MessageBox` dialogs for the unsaved-changes prompt, delete confirmations, save-error reports, the setlist-editor save validations (invalid date, empty setlist), the duplicate-date refusal, and ~40 more sites. These break visual consistency with the dark in-app UI, steal focus, and cannot be styled or positioned.
- **The sound, not just the dialog:** the `MessageBoxImage.Warning` icon plays the Windows warning chime (~18 sites). Gregg (2026-06-10): the **system alert sound interrupts his state of mind** as much as the dialog interrupts the screen — the audio startle is a distinct pain point from the visual inconsistency. The replacement is **silent by construction** (in-window WPF never calls `MessageBeep`); a non-startling cue would be an opt-in future addition.
- **Scope:** 45 live sites across 15 files (30 fire-and-forget notifications, 14 blocking decisions, 1 info-file viewer). Slice 1 converts the `EditSetlistView` validation cluster led by the duplicate-date refusal (`a6a652b`) that surfaced the sound complaint. See the spec for the full table and rollout.
- **Surfaced:** Preference surfaced 2026-06-10; second lived-demand data point the **same day** during the Add Concert duplicate-date gate (the refusal chime, hit repeatedly while exercising collisions, is what surfaced the sound detail). Spec written 2026-06-11.

### Delete `LibraryBrowserWindow.xaml.cs.bak` (dead file) — **RESOLVED (2026-06-19)**
- **What:** `DeadEditor/LibraryBrowserWindow.xaml.cs.bak` was a backup of a window the shell redesign already deleted ([18-shell-redesign-spec.md](18-shell-redesign-spec.md) § Removed Components). It carried 4 phantom `MessageBox.Show` sites that polluted alert-inventory greps but were unreachable dead code.
- **Resolution:** **Deleted** the `.bak` file. Its 4 phantom sites are gone, so the unfiltered `MessageBox.Show` grep (no `--include=*.cs`) is now clean — the residual the alert-system true-zero close-out had to carve out no longer exists.
- **Surfaced:** Alert-system inventory (2026-06-11).

### `AlbumSearchDialog` has no live caller — decide delete vs revive — **RESOLVED (2026-06-19)**
- **What:** `AlbumSearchDialog` (a modal `Window`) had a constructor but **no `new AlbumSearchDialog(` anywhere in source** — nothing opened it. Its `MessageBox` site #9 (missing album/artist validation) in [alert-system-spec.md](alert-system-spec.md) was therefore **unreachable**, and was excluded from the alert-system conversion until resolved.
- **Resolution:** **Deleted** as dead code. `AlbumSearchDialog.xaml(.cs)` removed (dead since the March shell cutover; no live caller, no lived demand for a manual MusicBrainz-search entry point), and site #9 retired from the inventory. This dropped the **last live `MessageBox.Show`** in the app — a `grep -rn "MessageBox.Show" --include=*.cs` now returns zero hits in compiled source (alert migration at true zero). Recoverable from git `006d170` if a manual album-search feature is ever scoped.
- **Surfaced:** Alert-system dialog-architecture audit (2026-06-11).

### Fix stale MainWindow MessageBox reference in `01-main-window.md`
- **What:** [01-main-window.md:281](01-main-window.md) documents a deprecated "Settings menu item" `MessageBox.Show(...)` in `MainWindow` (marked "Partial (deprecated, should be removed)"). `MainWindow.xaml.cs` no longer exists — the shell redesign removed it — so the reference describes nonexistent code.
- **Proposed fix:** Update or drop that doc row so the inventory reflects reality. Documentation-only.
- **Surfaced:** Alert-system inventory (2026-06-11).

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

### Alias-learning double-write — **RESOLVED (2026-06-19)**
- **Symptom (as banked):** Import view's right-click "Match to Song" writes the new alias to `songs.json` twice on a single match event.
- **Surfaced:** Commit `1840a44` deduped one such occurrence ("The Monkey and the Engineer" written twice for "Monkey And The Engineer").
- **Investigation:** The literal "double-write per match event" was **not reproducible**. `AddAlias` has been dedup-guarded and idempotent since `d92d219` (official-title equality guard + existing-alias `.Any` guard), and each match call site calls it exactly once. `1840a44` was a one-time cleanup of a pre-feature working-copy artifact, not a recurring double-write.
- **Real residual:** variant titles the cascade canonicalizes (typographic dashes, NBSP/Unicode whitespace, case) could still be stored as **dead alias keys** — the parser folds them to the canonical form *before* lookup, so the stored variant key is never reached at match time (e.g. `Peggy-O` carries a stored `Peggy–O` en-dash alias that no lookup ever hits).
- **Resolution:** `AddAlias` now short-circuits when `Normalize(candidate)` already resolves to the OfficialTitle (approach (a), reusing the existing cascade — no second normalizer to diverge). Safe because the manual Match-to-Song flow only reaches `AddAlias` for tracks `Normalize` left unmatched (the menu is gated on `IsMatched != true`, set by `NormalizeAll`/`Normalize` incl. L9 fuzzy), so the guard cannot suppress a legitimate first add. Covered by `NormalizationServiceAliasIdempotencyTests` (variant rejected / novel accepted / idempotent re-add). A test path-seam ctor (`internal NormalizationService(string songsPath)`) lets the write path run against a temp `songs.json`.
- **Optional follow-up (separate concern, NOT done here):** the existing dead artifacts in `songs.json` — `Peggy-O`'s stored `Peggy–O` (en-dash) alias plus the two case-variant seed entries — are harmless to matching but could be removed in a one-time data cleanup.

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
