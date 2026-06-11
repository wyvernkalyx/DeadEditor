# Alert System Spec

**Status:** Proposed
**Date:** 2026-06-11
**Branch:** `feature/library-verification-surface`

**Cross-references**
- `documentation/follow-ups.md` — the In-app alert system entry (points here as the authoritative inventory).
- `documentation/18-shell-redesign-spec.md` — the single-window `ShellWindow` + `NavigationService`
  architecture the alert surfaces live inside.
- `documentation/add-concert-spec.md` § Decision 1 — the duplicate-date refusal (commit `a6a652b`),
  the repeatedly-hit site that surfaced the sound complaint and motivates slice 1 here.
- `Views/PullCollisionDialog.xaml(.cs)` — the in-codebase proof-of-silence precedent (a dark-themed
  WPF dialog that passes no `MessageBoxImage` and therefore never chimes).
- `ShellWindow.xaml(.cs)`, and the 15 call-site files enumerated in the Inventory table.

---

## BLUF
`MessageBox.Show` is the app's only alert mechanism — **45 live sites across 15 files**, every one
capable of playing a Windows system sound. The driving pain (two lived-demand points on 2026-06-10) is
the **system alert sound**: `MessageBoxImage.Warning` fires the Exclamation chime on ~18 sites,
including the duplicate-date refusal hit repeatedly during the Add Concert gate — plus the modal
focus-steal of native dialogs. The replacement is two in-window WPF surfaces resolved from a shared
`IAlertService`: a **silent banner strip** for fire-and-forget notifications and a **silent dimmed
confirm host** for blocking decisions, both **silent by construction** (in-window WPF never calls
`MessageBeep`), no third-party packages, rolled out as five increments that never interleave the two
surfaces in one commit.

---

## Motivation
The app alerts the user exclusively through native `MessageBox.Show` (one call goes through the
`WpfMessageBox` alias in `ImportView.xaml.cs`). There is no `SystemSounds`, `TaskDialog`, or direct
sound API anywhere — every chime the app emits comes from a `MessageBox` icon flag.

**Lived demand.** Gregg surfaced this twice on 2026-06-10. The general preference came first; the
**sound** detail crystallized the same day while exercising the Add Concert duplicate-date gate —
hitting the refusal repeatedly across collision cases meant the Exclamation chime fired over and over.
His framing: the **system alert sound interrupts his state of mind** as much as the modal interrupts
the screen. The audio startle is a distinct pain from the visual inconsistency.

**Two distinct problems, one mechanism.**
1. **Sound.** `MessageBoxImage.Warning` (the most common value in the inventory, ~18 of 45 sites)
   plays the Windows Exclamation chime. `Error` and `Information` are also audible by default. Native
   dialogs cannot be made silent short of dropping the icon entirely.
2. **Focus-steal + visual inconsistency.** Native `MessageBox` is a separate top-level OS window with
   light OS chrome; it grabs focus from whatever the user was doing and breaks the dark in-app theme.

**Hard requirements for any replacement:**
- **Silent and non-startling** — no system chime by default.
- **In-window** — renders inside `ShellWindow`, dark-themed, no OS chrome.
- **No focus theft** — notifications must not yank focus from the active control.

---

## Binding rulings

### Ruling 1 — Bucket A (notifications) → in-window banner strip
The **30 fire-and-forget notification sites** (OK-only; code does not branch on the result) convert to
a **banner strip anchored at the top of the content area inside `ShellWindow`**:
- Color-coded by severity (info / warn / error).
- **Severity-dependent dismissal** (refined in slice 1): **Info auto-dismisses** after ~5s (manual
  close ✕ also available); **Warning and Error persist** until manually closed — a refusal or error
  must be read, never timed out from under the user.
- **Silent** and **never steals focus** — it is passive chrome above the active view; showing a banner
  never calls `Focus()`.
- **Queued** if multiple notifications fire — **one visible at a time**, the next surfaces on dismiss
  (FIFO). The queue + dismissal policy is the pure, tested `Services/AlertQueue`.

### Ruling 2 — Bucket B (blocking decisions) → in-window modal dialog host
The **14 blocking-decision sites** (Yes/No, OK/Cancel, Yes/No/Cancel; code branches on the answer)
convert to an **in-window modal dialog host**: a full-shell **dimmed scrim + centered card** rendered
inside `ShellWindow`, no OS chrome, silent. The host exposes `await ConfirmAsync(...)` returning a
result via `TaskCompletionSource`, which fits the existing `async` save paths
(`SaveChangesAsync` etc.).

**Branch semantics must be preserved exactly per site** — the same buttons (Yes/No / OK/Cancel /
Yes/No/Cancel), the same **default button**, and **Esc**/cancel mapping each site relies on today. A
bucket-B conversion that changes which branch a button takes is a regression, not a re-skin.

### Ruling 3 — Bucket C (info-file viewer) → scrollable read panel, last
The **single bucket-C site** (#33, `ImportView` info-`.txt` viewer) is not an alert — it dumps an
arbitrarily long text file into a `MessageBox`. It converts to a **scrollable in-window read panel**,
designed in **its own increment, last**. A banner or toast is the wrong target for long content.

### Ruling 4 — No third-party packages; silence is by construction
Every surface is built from stock WPF (`Border`/`Grid`/`ContentControl`/`Adorner`/`Storyboard` +
`TaskCompletionSource`). **No new dependency.** In-window WPF surfaces **never call `MessageBeep`**, so
silence is structural, not a suppressed flag. Any future audio cue would have to be **added
deliberately** — the default is no sound.

### Ruling 5 — A and B land as separate increments
Bucket A (`Notify`) and bucket B (`ConfirmAsync`) are different surfaces with different risk profiles
(cosmetic notification swap vs branch-preserving decision swap). They are **never interleaved in one
commit**. Build and verify each surface independently.

### Ruling 6 — Modal-dialog sites are not served by the shell banner
The shell banner **cannot serve bucket-A sites that fire inside a modal `Window` opened via
`ShowDialog()`** (confirmed by the 2026-06-11 dialog-architecture audit). A modal window sits above
`ShellWindow` and runs a nested message loop, so the content-cell banner is **occluded** behind it and
**undismissable** while the modal is up. Those sites get per-classification handling instead:
- **Input-validation refusals** — dialog stays open, user must fix a field (#7, #8, #12): **inline
  validation text adjacent to the offending field** (no banner; reads where the eye already is).
- **Terminal notifications where the dialog closes immediately** (#13, #14): **return the result to
  the caller** and `App.Alerts.Notify` on the shell **after `ShowDialog` returns**, landing on the
  now-visible banner.
- **Terminal notifications where the dialog persists as a workspace** (#10, #11 `ManageSongsDialog`,
  which is not closed by the export): an **embedded `AlertBannerHost` instance inside the dialog** —
  the control is self-contained and reusable, so the same silent surface works in-dialog.

This is why the bucket-A sweep (increment 2) takes **shell-hosted views first** (#38/#43/#45);
dialog-hosted sites are deferred to a later increment with the handling above.

---

## Inventory

The authoritative MessageBox inventory. **45 live call sites across 15 files**, all chime-capable
(none use `MessageBoxImage.None`). Excludes dead/stale references (see Banked cleanup, and
`follow-ups.md`): the 4 phantom sites in `LibraryBrowserWindow.xaml.cs.bak` (a backup of a window the
shell redesign deleted) and the stale `MainWindow` reference in `01-main-window.md:281`.

| # | File:line | Scenario | Image / Buttons | Bucket |
|---|-----------|----------|-----------------|--------|
| 1 | `ShellWindow.xaml.cs:344` | Confirm delete album → Recycle Bin | Warning / YesNo | B |
| 2 | `ShellWindow.xaml.cs:376` | Album delete failed — **converted (inc 3)** | Error / OK | A |
| 3 | `ShellWindow.xaml.cs:423` | Leave Import with unimported tracks | Question / YesNo | B |
| 4 | `ShellWindow.xaml.cs:607` | Box set gone since list loaded | Information / OK | A |
| 5 | `ShellWindow.xaml.cs:770` | Confirm delete concert JSON | Warning / YesNo | B |
| 6 | `ShellWindow.xaml.cs:800` | Concert delete failed — **converted (inc 3)** | Error / OK | A |
| 7 | `AdvancedSearchDialog.xaml.cs:294` | No search criteria (songs) | Warning / OK | A |
| 8 | `AdvancedSearchDialog.xaml.cs:317` | No search criteria (date/venue) | Warning / OK | A |
| 9 | `AlbumSearchDialog.xaml.cs:40` | Missing album/artist — **ORPHANED: no live caller, site unreachable** (excluded from conversion) | Warning / OK | A |
| 10 | `ManageSongsDialog.xaml.cs:199` | Export succeeded | Information / OK | A |
| 11 | `ManageSongsDialog.xaml.cs:204` | Export failed | Error / OK | A |
| 12 | `ReleaseSelectorDialog.xaml.cs:34` | No release selected | Warning / OK | A |
| 13 | `UnmatchedSongsDialog.xaml.cs:118` | N songs added | Information / OK | A |
| 14 | `UnmatchedSongsDialog.xaml.cs:126` | Apply corrections failed | Error / OK | A |
| 15 | `Views/AlbumDetailView.xaml.cs:730` | Confirm delete track → Recycle Bin | Warning / OKCancel | B |
| 16 | `Views/AlbumDetailView.xaml.cs:747` | Could not delete file — **converted (inc 3)** | Error / OK | A |
| 17 | `Views/BoxSetWizardView.xaml.cs:198` | Invalid track date(s) on save — **converted (inc 4)** | Warning / OK | A |
| 18 | `Views/BoxSetWizardView.xaml.cs:255` | Box set save failed — **converted (inc 4)** | Error / OK | A |
| 19 | `Views/BoxSetWizardView.xaml.cs:566` | Pull: invalid date entry — **converted (inc 4)** | Warning / OK | A |
| 20 | `Views/BoxSetWizardView.xaml.cs:574` | Pull: no setlist for date — **converted (inc 4, first Info)** | Information / OK | A |
| 21 | `Views/BoxSetWizardView.xaml.cs:666` | Confirm remove all tracks for date | Warning / YesNo | B |
| 22 | `Views/EditMetadataView.xaml.cs:469` | Manifest sidecar write failed | Warning / OK | A |
| 23 | `Views/EditMetadataView.xaml.cs:976` | Album type changed (move-files notice) | Information / OK | A |
| 24 | `Views/EditMetadataView.xaml.cs:994` | Save changes failed — **converted (inc 3)** | Error / OK | A |
| 25 | `Views/EditMetadataView.xaml.cs:1007` | Cancel with unsaved changes | Question / YesNo | B |
| 26 | `Views/EditMetadataView.xaml.cs:1317` | Cannot verify — required fields missing | Warning / OK | A |
| 27 | `Views/EditMetadataView.xaml.cs:1703` | Existing MBID — refresh / search / cancel | Question / YesNoCancel | B |
| 28 | `Views/EditSetlistView.xaml.cs:244` | Invalid date format on save — **converted (slice 1)** | Warning / OK | A |
| 29 | `Views/EditSetlistView.xaml.cs:261` | **Duplicate-date refusal (commit `a6a652b`)** — **converted (slice 1)** | Warning / OK | A |
| 30 | `Views/EditSetlistView.xaml.cs:271` | Empty setlist on save — **converted (slice 1)** | Warning / OK | A |
| 31 | `Views/EditSetlistView.xaml.cs:359` | Setlist save failed — **converted (inc 3)** | Error / OK | A |
| 32 | `Views/EditSetlistView.xaml.cs:369` | Cancel with unsaved changes | Question / YesNo | B |
| 33 | `Views/ImportView.xaml.cs:1577` | Display info `.txt` file content | Information / OK | C |
| 34 | `Views/MbidMigrationView.xaml.cs:80` | Confirm start migration fresh | Question / YesNo | B |
| 35 | `Views/MbidMigrationView.xaml.cs:130` | Migration error — **converted (inc 3)** | Error / OK | A |
| 36 | `Views/ReleasesView.xaml.cs:326` | Confirm remove standalone release | Question / YesNo | B |
| 37 | `Views/ReleasesView.xaml.cs:359` | Confirm remove last volume | Question / YesNo | B |
| 38 | `Views/ReleasesView.xaml.cs:419` | Duplicate release name — **converted (inc 2)** | Warning / OK | A |
| 39 | `Views/SettingsView.xaml.cs:135` | Confirm re-enrich library | Question / YesNo | B |
| 40 | `Views/SettingsView.xaml.cs:346` | Confirm reset library data | Warning / YesNo | B |
| 41 | `Views/SettingsView.xaml.cs:393` | Reset complete | Information / OK | A |
| 42 | `Views/SettingsView.xaml.cs:404` | Reset error | Error / OK | A |
| 43 | `Views/SongsView.xaml.cs:227` | Duplicate song name (rename) — **converted (inc 2)** | Warning / OK | A |
| 44 | `Views/SongsView.xaml.cs:262` | Confirm remove song | Question / YesNo | B |
| 45 | `Views/SongsView.xaml.cs:327` | Duplicate song name (add) — **converted (inc 2)** | Warning / OK | A |

**Bucket counts:** A = 30, B = 14, C = 1 (total 45). Site **#9 is orphaned** (`AlbumSearchDialog` has
no live caller), so the **live actionable count is effectively 44** — #9 is excluded from conversion
until that dialog is deleted or revived (see `follow-ups.md`).

### Sound mapping (Windows 11)
On Windows, `MessageBox.Show` calls the Win32 `MessageBox`, which calls `MessageBeep(uType)` for the
icon flag. The icon → sound-scheme-event mapping:

| `MessageBoxImage` | Sound scheme event | Win11 default |
|-------------------|--------------------|---------------|
| `Error` / `Hand` / `Stop` (16) | Critical Stop (`SystemSounds.Hand`) | audible |
| `Warning` / `Exclamation` (48) | Exclamation (`SystemSounds.Exclamation`) | **audible — the chime Gregg hears** |
| `Information` / `Asterisk` (64) | Asterisk (`SystemSounds.Asterisk`) | audible |
| `Question` (32) | Question (`SystemSounds.Question`) | **usually silent** — the default scheme maps the Question event to "(None)"; the beep call still fires but plays nothing |
| `None` (0) | none | silent — no icon, no beep |

So the bulk of the annoyance is `Warning` (most frequent) plus `Error`/`Information`. The
`Question`-icon sites are silent **only by accident** of the stock Win11 scheme — not something to
rely on, and per-machine. **No live site uses `None`**, so every one is chime-capable. An in-window
WPF surface produces no sound by default.

### Relationship to follow-ups.md
`follow-ups.md` previously named ~a dozen sites accurately (unsaved-changes prompts, delete
confirmations, save-error reports, the EditSetlist validations, the duplicate-date refusal) but
**undercounted the real sweep roughly 4×**. **This spec's table is now the authoritative inventory**;
the follow-ups entry points here.

---

## Service API
A shell-wide `IAlertService`, resolved as a singleton mirroring the `App.PlaybackService` pattern
(one instance owned by / reachable from the single `ShellWindow`, which hosts both surfaces above its
swappable `ContentPresenter` so they survive view switches):

```csharp
public enum AlertSeverity { Info, Warning, Error }

public interface IAlertService
{
    // Bucket A — silent, non-focus-stealing banner; returns immediately, queued if one is showing.
    void Notify(string message, AlertSeverity severity = AlertSeverity.Info, string? title = null);

    // Bucket B — silent in-window dimmed confirm host; result via TaskCompletionSource.
    // Two-way (Yes/No, OK/Cancel):
    Task<bool> ConfirmAsync(string message, string title,
                            string confirmText = "OK", string cancelText = "Cancel");

    // Three-way (Yes/No/Cancel):
    Task<ConfirmResult> ConfirmAsync(string message, string title,
                                     string yesText, string noText, string cancelText);
}

public enum ConfirmResult { Yes, No, Cancel }
```

**Proof-of-silence precedent.** `Views/PullCollisionDialog` is already a dark-themed WPF dialog
(Replace / Append / Cancel) that passes **no `MessageBoxImage`** and therefore **chimes not at all** —
demonstrating the no-sound requirement is achievable in hand-rolled WPF today. The confirm host
generalizes that precedent into a reusable in-window overlay (replacing the separate-OS-window aspect
that the shell redesign set out to eliminate).

---

## Rollout plan
Five increments, lowest-risk / highest-annoyance first. A and B never interleave (Ruling 5).

1. **`IAlertService` + banner surface + EditSetlistView cluster.** Build the service and the bucket-A
   banner strip; convert the EditSetlistView validation cluster: **#28** (invalid-date), **#29**
   (duplicate-date refusal), **#30** (empty-setlist). This is where the sound complaint was surfaced —
   the duplicate-date refusal hit repeatedly during the Add Concert gate.
2. **Bucket-A sweep**, one or two views per commit (search dialogs → songs/releases duplicate-name →
   save-error reports → settings/manage notifications → album-type/manifest notices). Pure
   notification swaps, no branching.
3. **Confirm host (`ConfirmAsync`) + the unsaved-changes prompts** (**#25**, **#32**, **#3**) — the
   most-traversed decisions, well-understood Yes/No semantics.
4. **Bucket-B sweep** — deletes (**#1**, **#5**, **#15**, **#21**, **#44**, **#36**, **#37**),
   reset / re-enrich (**#39**, **#40**), MBID flows (**#27**, **#34**). Each must preserve exact branch
   semantics (button mapping, default button, Esc).
5. **Bucket C** — convert the ImportView info-file viewer (**#33**) to a scrollable in-window read
   panel. Standalone, lowest priority.

**Estimated ~8–12 commits total** (could compress to ~6 if views are batched within a bucket).

### Per-increment manual WPF gate
Each increment is gated by a build + run of the app, triggering **every** converted site, confirming
by eye and ear:
- **(a)** no system chime;
- **(b)** in-window, dark-themed rendering (no OS chrome, no separate window);
- **(c)** for bucket B, every branch fires correctly per button — including **Cancel** and **Esc** —
  with the documented default button;
- **(d)** no focus theft from the active control.

Structural/test evidence (e.g. tests on a pure result-mapper) is necessary but **not sufficient** to
clear the gate — a human clears the manual WPF gate before each commit.

---

## Out of scope
- Any change to the branch *logic* of a converted site — conversions preserve behavior, not redefine
  it. (The duplicate-date refusal rule itself is owned by `add-concert-spec.md`.)
- A soft audio cue — silence is the default; an opt-in cue is a future, deliberate addition only.
- Replacing the OS **file/folder pickers** (`OpenFileDialog` / `FolderBrowserDialog`) — those are not
  alerts and are out of this sweep.

---

## Commit plan (ledger)
1. **[Proposed `<this commit>`]** Spec doc (`alert-system-spec.md`) + `follow-ups.md` update pointing
   the alert entry here as the authoritative inventory and banking two cleanup items (delete
   `LibraryBrowserWindow.xaml.cs.bak`; fix the stale `01-main-window.md:281` MessageBox reference).
   Docs-only.
2. **[Implemented — pending WPF gate]** Increment 1 — `IAlertService.Notify` + the in-window banner
   surface + the EditSetlistView validation cluster. New: `Services/IAlertService.cs` (`AlertSeverity`,
   `AlertItem`, `IAlertService` with `Notify` only — `ConfirmAsync` deferred to increment 3, not
   stubbed), `Services/AlertQueue.cs` (pure FIFO queue + severity dismissal policy), `Services/AlertService.cs`
   (shell-wide singleton mirroring `AudioPlayerService.Instance`, reached via `App.Alerts`),
   `Views/AlertBannerHost.xaml(.cs)` (`IAlertSink` banner, overlaid at the top of the content cell in
   `ShellWindow`, registered by the shell at construction). Converted sites **#28** (invalid date),
   **#29** (duplicate-date refusal), **#30** (empty setlist) in `EditSetlistView.SaveChangesAsync` from
   `MessageBox.Show(..., Warning)` to `App.Alerts.Notify(..., AlertSeverity.Warning, title)` — control
   flow unchanged (each still returns/aborts the save). Sites #31/#32 in the same file left for later
   increments. Tests: 9 `AlertQueueTests` (baseline **346 → 355**). Build clean (51 unique warnings,
   zero from new files). **Manual WPF gate is Gregg's, separate.**
3. **[Implemented — pending WPF gate]** Increment 2 — bucket-A sweep, shell-hosted views first.
   Converted the three duplicate-name validation refusals in shell-hosted views to
   `App.Alerts.Notify(..., AlertSeverity.Warning, "Duplicate")`, surface only (every abort/return flow
   unchanged): **#38** (`ReleasesView` duplicate release name), **#43** (`SongsView` duplicate song
   name on rename), **#45** (`SongsView` duplicate song name on add). Sites #36/#37 (ReleasesView
   Yes/No remove confirms), #44 (SongsView remove confirm), and #41/#42 (SettingsView) untouched.
   No new pure logic, no new tests (baseline holds at **355**); build clean (51 unique warnings).
   **Reorder vs the rollout-plan ordering:** the plan listed *search dialogs* as the first sweep
   batch, but those eight sites (#7–#14) live inside **modal `Window` dialogs** (`AdvancedSearchDialog`,
   `AlbumSearchDialog`, `ReleaseSelectorDialog`, `ManageSongsDialog`, `UnmatchedSongsDialog`) opened
   via `ShowDialog()`. A modal dialog sits above the shell, so the shell's content-cell banner is
   occluded and would not be visible while the dialog is open — those sites are **not servable by the
   shell banner as-is** and need per-dialog handling (inline field validation, an embedded banner, or
   deferring result notifications until after close). That design is captured in the increment-2
   dialog-architecture audit and deferred to a later increment; this increment took the shell-hosted
   duplicate-name refusals (#38/#43/#45), which the banner serves directly, first. **Manual WPF gate
   is Gregg's, separate.**
4. **[Implemented — pending WPF gate]** Bucket-A sweep, batch 2 — error reports (the second commit of
   the increment-2 bucket-A sweep; the "increment 3" work session). Converted the six
   `MessageBoxImage.Error` catch-block reports to `App.Alerts.Notify(..., AlertSeverity.Error, title)`,
   full exception detail preserved (Error banners persist until closed): **#2** (`ShellWindow` album
   delete failed), **#6** (`ShellWindow` concert delete failed), **#16** (`AlbumDetailView` could not
   delete file), **#24** (`EditMetadataView` save changes failed), **#31** (`EditSetlistView` setlist
   save failed), **#35** (`MbidMigrationView` migration error). Surface only — every catch/abort flow
   unchanged; no other sites in those files touched (confirm/branching sites #1/#3/#5, #15,
   #22/#23/#25/#26/#27, #32, #34 left as-is). `using MessageBox` aliases retained (those files still
   host unconverted sites) to minimize diff. No new tests (baseline holds at **355**); build clean (51
   unique warnings). **Manual WPF gate is Gregg's, separate.**
5. **[Implemented — pending WPF gate]** Bucket-A sweep, batch 3 — `BoxSetWizardView` cluster (the
   "increment 4" work session). Converted four sites to `App.Alerts.Notify`, surface only (abort/return
   flows unchanged): **#17** (invalid track date(s) on save, Warning), **#18** (box set save failed,
   Error — full exception detail preserved), **#19** (pull: invalid date entry, Warning), **#20**
   (pull: no setlist for date, **Info — the first Info-severity conversion**, which exercises the ~5s
   auto-dismiss path live for the first time). Severities map 1:1 from the code's existing
   `MessageBoxImage` (Warning/Error/Warning/Information). Untouched: #21 (Yes/No remove-all-tracks
   confirm) and the `PullCollisionDialog` interaction; `using MessageBox` alias retained (file still
   hosts #21). No new tests (baseline holds at **355**); build clean (51 unique warnings). **Manual WPF
   gate is Gregg's, separate.**
6. _(future)_ Increment 3 — confirm host + unsaved-changes prompts.
7. _(future)_ Increment 4 — bucket-B sweep.
8. _(future)_ Increment 5 — bucket-C scrollable read panel.

Status flips to **Implemented** at close-out once the sweep lands.
