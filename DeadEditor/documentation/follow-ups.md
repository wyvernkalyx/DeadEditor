# Follow-Ups

A single tracked home for known deferred items, so they survive session
boundaries rather than depending on memory. Keep entries dry and factual —
this is a reference list, not a narrative.

## Known Deferred Items

Issues identified but not yet fixed. Each entry: brief description, where it surfaces, when noticed.

### Fingerprint timing vs. match-before-import (Surfaced 2026-06-24)
Fingerprinting currently runs at import (`PrecomputeFingerprints`), aligned with the
source -> library boundary (read operates on un-owned source files; import is where a file
is committed to the managed library). Stored fingerprints are presently display-only — no
fingerprint-keyed matcher consumes them yet. Open question for the future
fingerprint-matching arc: once a fingerprint-keyed identify/match consumer exists, should
fingerprinting move earlier (read/scan time) to enable match-before-import? Do not act until
that consumer is designed — acting now would build for a non-existent consumer. Surfaced
during the fpcalc-runner sever (relocating the runner into `FingerprintService`).

### Edit Metadata save-lock (Surfaced + fixed 2026-06-22)
**DONE (this commit):** Saving could fail with "the process cannot access the file ... because it is being used by another process," losing the edit. Root cause was concurrency, not a held handle: the save path had no re-entrancy guard and `AudioPlayerService.WithFileReleased` did not serialize, so a second save (or a fingerprint / MBID write) could enter its write `action()` while the first still held the FLAC open for exclusive write (TagLib `Flac.File.Save` opens write-exclusive). Fix: a static `SemaphoreSlim(1,1)` inside `WithFileReleased` serializes every guarded write window app-wide (closing the whole save+save / save+fingerprint / save+MBID class), plus an `_isSaving` re-entrancy guard on `EditMetadataView.SaveChangesAsync` (covers both entry points — HeaderBar Save and `VerifyButton_Click`) with the HeaderBar Save button disabled during the save. Regression gate: `AudioPlayerServiceWriteGateTests.WithFileReleased_ConcurrentWritersOnSameFile_DoNotCollide` (fails pre-fix, passes after). `MetadataService.WriteMetadata` per-track disposal was already correct and is unchanged.

**REMAINING — now ACTIVE (was deferred):** the **UI-thread load freeze** is the enabler and is the natural next commit. `EditMetadataView_Loaded -> LoadData` runs synchronously on the UI thread (folder reads + `RefreshUI -> UpdateMatchSetlistButton -> GetSetlist` concert load), freezing the view while the Save button is already live, so frustration-clicks queue and then fire. Fix direction: move `LoadData`'s blocking work off the UI thread (async load) with a progress indicator so the view never presents an interactive-but-frozen Save button. Promoted from "deferred unless it bites" — it bit.

### Import action-bar UI pass (Surfaced 2026-06-20)
The Import view's top action bar needs a visual/UX cleanup. Three issues:
1. ~~**MusicBrainz button** — label text is vertically clipped (bottom of "MusicBrainz" cut off). Button height/padding/line-height doesn't accommodate the label.~~ **DONE 2026-06-20:** the button set `Height="28"` (too short for `FontSize="16"` + AccentButton padding) while every sibling is `Height="32"`; set to 32 to restore label headroom and row-align the cluster. Manual WPF gate PASS.
2. **Workflow stepper** (Load → Enrich → Clean → Structure → Import) is styled almost identically to the action buttons below it, so it reads as a row of interactive controls rather than a non-interactive process-flow indicator. Restyle as a clearly non-clickable stepper/breadcrumb that maps to the workflow the action buttons drive.
3. **Match Setlist button** — styling inconsistent with the blue sibling action buttons (light/washed-out). Refines/absorbs the existing **Match Setlist button styling** follow-up (banked 2026-06-12, alert-system inc-9 gate) — fold both into this pass. **DONE 2026-06-20:** both roots fixed. ImportView root (`05b7c8f`): `AccentButton` gained a `ControlTemplate` whose disabled state renders an intentional muted-badge treatment instead of system washed-out gray. EditMetadataView root (this commit): its Renumber/Match Setlist/MusicBrainz were one-off inline gray; harmonized to the centralized shared styles matching ImportView's role convention (Renumber -> TertiaryButton ghost; Match Setlist & MusicBrainz -> AccentButton; Normalize already AccentButton), so the disabled Match Setlist now inherits the same muted-badge treatment. Sub-item (2) stepper remains open; (1) was closed in `813cde6`.

Two are quick XAML sizing/style fixes (1, 3); (2) is a genuine UX distinction. Good cohesive next-session candidate.

### PrimaryButton key collision (Surfaced 2026-06-20)
Two distinct styles share `x:Key="PrimaryButton"`: ImportView's green terminal-action
button vs. the blue one in `ConfirmHost`/`PullCollisionDialog`. Currently runtime-safe
(the dialog-local declarations shadow any other — `ConfirmHost` reads it via the local
`Resources["PrimaryButton"]` indexer; `PullCollisionDialog` via local `StaticResource`),
but the key carries two meanings. Disambiguate by renaming one (e.g. the dialog blue ->
`DialogPrimaryButton`) so the key is single-meaning before any future centralization.
Out of scope for the import UI pass; own concern. Surfaced during the AccentButton
centralization (Commit 2 of the import action-bar pass), which left PrimaryButton local
for exactly this reason.

### Button label vertical centering (Tertiary/Primary) (Surfaced 2026-06-20)
`AccentButton` labels were re-centered in `05b7c8f` (Padding `12,6` -> `12,4`) when it
gained a `ControlTemplate` — the 16pt label was overflowing low within the consumers'
fixed `Height=32`. `TertiaryButton` and `PrimaryButton` share `Height=32` / `FontSize=16`
/ `Padding 12,6` and likely show the same slight off-centering — NOT confirmed visually.
Confirm in the running app; if labels look off, apply the same adjustment (`TertiaryButton`
via its template padding, `PrimaryButton` via its style). Lived-demand: don't fix unless
observed.

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
- **Match Setlist button styling:** the Match Setlist button in the `EditMetadataView` toolbar renders washed-out / unreadable (owner screenshots 2026-06-12). Restyle for contrast against the dark toolbar. **Folded into "Import action-bar UI pass" (2026-06-20) — address there.**
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

### ~~Cold-start concert load (~17s cold, ~0.6s warm) — UI-thread freeze~~ (DONE 2026-06-23)
- **What:** The first `ConcertLookupService` load after a reboot/cache flush took **16,758ms** for 2,293 files; the immediate second run took **583ms** (measured 2026-06-10). Steady-state is fine. The pain was that this cold load ran **synchronously on the UI thread** the first time Edit Metadata opened (reached via `LoadData -> RefreshUI -> UpdateMatchSetlistButton -> ShowLookupService.GetSetlist`, which sources from `ConcertLookupService`), freezing the app for seconds with no feedback — and the frozen-but-interactive-looking window let queued clicks pile up, which enabled the concurrent-save file lock (fixed separately in `2912161`).
- **Done:** Added `ConcertLookupService.IsLoaded` (reads the `Lazy`'s `IsValueCreated` without forcing the load) and a cold-gate in `EditMetadataView_Loaded`: on a cold open it pre-warms the cache on a background thread (`Task.Run`) behind the modal status overlay (`App.Alerts.RunWithStatusAsync`, the Commit-A service) before running the synchronous `LoadData`. Cold opens now show an indeterminate "Loading concert database…" dialog while the UI thread stays responsive; warm opens skip it and stay silent on the fast (~0.6s) path. The modal scrim also blocks interaction during the load, closing the click-into-not-ready-view window. WPF gate passed 2026-06-23 (cold shows the animating dialog without freezing; warm silent; interaction blocked behind the scrim; no save regression).
- **Residual (accepted):** the sub-second per-file TagLib read inside `LoadData` (the 25-file folder read) stays synchronous. Backgrounding all of `LoadData` is a possible future item only if that residual ever bites (bigger blast radius — touches `RefreshUI`/`UpdateMatchSetlistButton` direct-UI writes).
- **Surfaced:** 2026-06-10. **Resolved:** 2026-06-23.

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

### `AddSong` blind-overwrite clobbered cross-instance `songs.json` writes — **RESOLVED (2026-06-19)**
- **Symptom:** `NormalizationService.AddSong` mutated its in-memory `_database` and serialized the **whole graph** to disk via the private `SaveDatabase` (`File.WriteAllText`, no re-read). Each view holds its **own** `NormalizationService` whose `_database` is loaded once at construction and is **not** refreshed on re-navigation (the cached SettingsView instance never reloads at all — `SettingsView_Loaded` reloads settings.json only). So a song added through a stale instance overwrote any alias or song another instance had written to `songs.json` since the stale instance loaded.
- **Concrete repro (confirmed, Phase A):** (1) open Settings → its NormalizationService loads snapshot S0; (2) go to Import, Match-to-Song learns alias A (`AddAlias`, read-fresh-merge) → disk now has A; (3) return to the cached Settings view (not reloaded) → Add Song C → `AddSong` blind-writes S0+C → **alias A is lost**. The victim can be any post-S0 write (an `AddAlias`, or a SongsView add/edit/remove). `AddAlias` was already safe (read-fresh-merge-atomic-reload); `AddSong` was the asymmetric blind path.
- **Resolution:** `AddSong` rewritten to mirror `AddAlias` — re-read `songs.json` into a fresh `SongDatabase`, find-or-create the artist and dedup the `OfficialTitle` (`OrdinalIgnoreCase`) on that fresh graph, append, atomic temp+rename write, then `LoadDatabase()`. The in-memory `_database` is no longer trusted as the write source. Covered by `NormalizationServiceAddSongTests` (cross-instance no-clobber / dedup / basic-add-resolves). The private `SaveDatabase` is now **orphaned** within `NormalizationService` (AddSong was its only caller); left in place this commit, removable in a follow-up.
- **Scope note:** fix is scoped to `AddSong` only. SongsView edit/remove hardening is banked separately (next entry). The shared `Json.WriteAtomic` extraction remains banked (see "Atomic-write temp-and-rename pattern duplicated" below).
- **Surfaced:** during the #13 alias-learning investigation (2026-06-19).

### SongsView (and other non-append) edits should read-fresh-merge before writing `songs.json` — **OPEN**
- **What:** `SongsView.SaveDatabase` (inline edit, RemoveSong, AddSongButton) and any future non-append edit serialize the view's in-memory `_database` whole. Today this is **safe only incidentally** — `SongsView.LoadSongs()` reloads from disk on every navigation, and no other writer runs while SongsView is the visible view (single-threaded UI). It is **not a current repro**, but it is the same latent blind-overwrite shape the `AddSong` fix removed.
- **Why deferred / harder than `AddSong`:** append (AddSong/AddAlias) has trivial merge semantics — add one item to the fresh graph. Edit/remove carry **real** merge semantics: a rename or delete must be reconciled by stable key (`OfficialTitle`) against a concurrently-changed disk graph (e.g. what if the renamed song was also edited elsewhere?). Needs a deliberate reconcile design, not a mechanical mirror of `AddAlias`.
- **Surfaced:** Phase A of the `AddSong` clobber investigation (2026-06-19).

### Autocomplete control duplicated; no song-name autocomplete
- **What:** `AlbumNameSuggestions` (the TextBox + Popup + ListBox pattern) is duplicated between `EditMetadataView.xaml` and `ImportView.xaml`. Song names have no autocomplete at all — the setlist editor uses a type-then-Normalize pattern instead.
- **Proposed fix:** Extract the album-name pattern into a reusable `AutocompleteTextBox` user control, add a song-name autocomplete variant scoped by `LibrarySettings.PrimaryArtistName`, and adopt it across `EditMetadataView`, `ImportView`, and the box-set wizard.
- **Surfaced:** Phase A audit for box-set MVP.

### Atomic-write temp-and-rename pattern duplicated across call sites
- **What:** The temp-write-and-rename idiom is duplicated at 7+ call sites: `EditSetlistView.xaml.cs:270-284`, `MbidMigrationService.cs:405-408`, `NormalizationService.cs` (both `AddAlias` and now `AddSong` after the 2026-06-19 clobber fix), `ReleaseLookupService.cs:388-390`, `ShowLookupService.cs:292-294`, `SongsView.xaml.cs:391-393` (plus the box-set MVP's new copy).
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

### Import action bar: "Import to Library" section and styling (Surfaced 2026-06-25)
The "Import to Library" action should be its own section in the import action bar (alongside the View Info / Import to Library grouping) and styled as a blue button consistent with the other primary actions. Surfaced during the commit-3 (MB UI removal) manual gate. Belongs to the deferred stepper / action-bar review arc.

### Import action bar: button order (Surfaced 2026-06-25)
Proposed order, as stated by Gregg: Read - Open Folder - Match Setlist - Normalize - Import | Tools section: Open Folder - Renumber - View Info. NOTE: "Open Folder" appears in both the main row and the Tools section as written — clarify whether intentional or a typo when this arc is picked up; recorded verbatim, not deduplicated. The post-MB-removal action bar is simpler, which makes the ordering cleaner to settle. Belongs to the stepper / action-bar review arc.

### Canonical song naming: Weather Report Suite variants (Surfaced 2026-06-25)
Observation (tentative — Gregg's lean, not a locked rule): "Weather Report Suite" appears in multiple title variants (e.g. "Weather Report Suite: Prelude/Part 1/Part 2 (Let It Grow)"). Lean is to collapse to just "Weather Report Suite" and "Let It Grow", treating Prelude/Part 1/Part 2 as always part of the Weather Report Suite. NOT a precise rule yet — needs its own read-only diagnosis to define the canonical names in songs.json, pin the normalizer/alias behavior, and assess impact on already-matched tracks before any change. Unrelated to MB removal; recorded here only because it surfaced during the commit-3 gate. Ties to the "own canonical song names" direction.

### Import folder-Read cold-load overlay (Surfaced 2026-06-25)
Reading a folder for import on the first action after app launch stalls for several seconds with no feedback — the same cold-load behavior already fixed for Edit Metadata's cold open. Cause: the Read step warms `ConcertLookupService` (Match Setlist needs the concert authority), and the cache is per-process, so it only colds once per launch. Import's Read path was never wrapped in the status overlay. Fix: apply the existing shipped pattern — `ConcertLookupService.IsLoaded` gate + `App.Alerts.RunWithStatusAsync` — to the Import folder-Read entry point, using the Edit Metadata cold-open fix as the template. Own concern, separate from the MB-removal arc; wants its own small read-only Phase A (confirm exactly where Read touches the concert DB cold and that `RunWithStatusAsync` is reachable there) and its own commit.
