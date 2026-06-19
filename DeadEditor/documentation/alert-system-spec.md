# Alert System Spec

**Status:** Implemented — close-out 2026-06-12 (buckets A/B/C converted); orphaned #9 dialog deleted 2026-06-19, zero `MessageBox.Show` remaining
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
- **Severity follows what the content requires, not the legacy `MessageBoxImage`.** An **actionable
  advisory** — anything that asks the user to do something or that they must read — maps to **Warning**
  so it **persists**, even if the old dialog used the Information icon. Reserve **Info** (auto-dismiss)
  for transient "done"/FYI confirmations the user need not act on. Gate finding (2026-06-11): site #23
  ("Album Type Changed", old icon Information) auto-dismissed before its *"you may need to move the
  folder"* advisory could be read, because it fires simultaneously with `GoBack` navigation — so it was
  **remapped Info → Warning**.

### Ruling 2 — Bucket B (blocking decisions) → in-window modal dialog host
The **14 blocking-decision sites** (Yes/No, OK/Cancel, Yes/No/Cancel; code branches on the answer)
convert to an **in-window modal dialog host**: a full-shell **dimmed scrim + centered card** rendered
inside `ShellWindow`, no OS chrome, silent. The host exposes `await ConfirmAsync(...)` returning a
result via `TaskCompletionSource`, which fits the existing `async` save paths
(`SaveChangesAsync` etc.).

**Branch semantics must be preserved exactly per site** — the same buttons (Yes/No / OK/Cancel /
Yes/No/Cancel), the same **default button**, and **Esc**/cancel mapping each site relies on today. A
bucket-B conversion that changes which branch a button takes is a regression, not a re-skin.

**Confirm host — implemented decisions (increment 6).** The two-way `ConfirmAsync` surface
(`Views/ConfirmHost.xaml(.cs)`, registered by `ShellWindow` via `AlertService.RegisterConfirmHost`)
landed with the following rulings:

- **Placement / scrim.** A full-shell dimmed scrim (`#CC000000`) + centered dark card, mounted in
  `ShellWindow.xaml` spanning **every column and row** (`Grid.ColumnSpan=2 Grid.RowSpan=4`) at
  `Panel.ZIndex=100` — above the banner layer (`ZIndex=10`), so a blocking decision dims and blocks
  the **entire** shell, sidebar/header/playlist/player and banner included. The opaque scrim `Border`
  captures all mouse input; keyboard is gated by the shell (below).
- **Focus ownership — the deliberate contrast with the banner.** The banner is passive chrome that
  **never calls `Focus()`** (it must not steal focus from the active control). The confirm card is the
  opposite **by design**: it **takes keyboard focus** (the **default button** is focused on show),
  because a blocking decision is the one surface that *should* own focus until answered. This contrast
  is intentional, not an inconsistency. The default button is Confirm except on the two-way
  `defaultToConfirm:false` path, where it is the negative/safe button (increment 8).
- **Accent follows the default button.** The accent (filled, primary) style is applied **to whichever
  button is the default** — the same button that takes focus — so the visual highlight always matches
  the Enter target. Both derive from one `defaultButton` reference in `ConfirmHost.Show`
  (`ApplyDefaultButtonStyling`); they cannot diverge by construction. (Increment-8 gate fix
  2026-06-12: the accent was previously hardcoded to Confirm, so a `defaultToConfirm:false` card showed
  Yes highlighted while Enter pressed No.)
- **Keyboard (Esc/Enter).** Handled in `ShellWindow_PreviewKeyDown` while `ConfirmHost.IsShowing`
  (the Window's tunnelling preview fires before the card's own elements): **Enter** → the
  focused/**default** button (Confirm on the standard path; the negative button when
  `defaultToConfirm:false`); **Esc** → the **safe answer** (two-way: `false` / the Cancel-or-No button;
  three-way: `Cancel`). Every other key is **swallowed** (`e.Handled = true`) so no shell shortcut
  (Space = play/pause, Ctrl+F, Delete, the shell's own Esc = GoBack) leaks behind the scrim.
- **Esc = negative is a deliberate, uniform improvement.** A Win32 **YesNo** `MessageBox` has **no
  Esc-close** (Esc does nothing without a Cancel button). All three converted sites (#3/#25/#32) were
  YesNo, so the new surface **adds** Esc = No (stay / keep editing — the safe answer). This is recorded
  here as an intentional, uniform semantics improvement across the confirm surface, not a silent
  per-site change: Esc can only ever take the *non-destructive* branch (it never discards or leaves).
- **Re-entrancy — refuse with exception.** Calling `ConfirmAsync` while a confirm is already showing
  throws `InvalidOperationException`. One blocking decision owns the surface at a time; the scrim
  structurally prevents a *second user-initiated* confirm, so a second call is a logic error. Refusing
  loudly is preferred over silently queuing a prompt the user never expected to be deferred (a queued
  blocking decision would surface later, detached from its triggering action). The `TaskCompletionSource`
  is torn down (`IsShowing` flips false) **before** the result is set, so a continuation that
  immediately raises another confirm is allowed.
- **Banner interplay.** Banners may still fire while the scrim is up — they render behind it at the
  lower Z and surface normally once the card is dismissed; the two surfaces do not interfere.
- **Pure-logic extraction — skipped, justified.** Unlike the banner (whose FIFO ordering + severity
  auto-dismiss policy warranted the pure, tested `AlertQueue`), the confirm host has **no non-trivial
  pure logic**: the result mapping is a direct 1:1 (Confirm → `true`, Cancel/Esc → `false`) with no
  ordering, no queue (re-entrancy is refused, not buffered), and no severity policy. There is nothing
  to extract that a test would meaningfully exercise; the manual WPF gate is the verification.
- **Three-way overload deferred.** Only the two-way `Task<bool> ConfirmAsync(message, title,
  confirmLabel = "Yes", cancelLabel = "No")` is implemented. The Yes/No/Cancel overload and the
  `ConfirmResult` enum are **deferred to the increment that converts #27** (the sole YesNoCancel
  site, bucket-B sweep) — they are not trivially shared with the two-way path (a third button +
  tri-state result), and Ruling 4 / the rollout discipline forbid stubbed `NotImplemented` members.

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
| 1 | `ShellWindow.xaml.cs:344` | Confirm delete album → Recycle Bin — **converted (inc 7)** | Warning / YesNo | B |
| 2 | `ShellWindow.xaml.cs:376` | Album delete failed — **converted (inc 3)** | Error / OK | A |
| 3 | `ShellWindow.xaml.cs:423` | Leave Import with unimported tracks — **converted (inc 6)** | Question / YesNo | B |
| 4 | `ShellWindow.xaml.cs:607` | Box set gone since list loaded — **converted (inc 5)** | Information / OK | A |
| 5 | `ShellWindow.xaml.cs:770` | Confirm delete concert JSON — **converted (inc 7)** | Warning / YesNo | B |
| 6 | `ShellWindow.xaml.cs:800` | Concert delete failed — **converted (inc 3)** | Error / OK | A |
| 7 | `AdvancedSearchDialog.xaml.cs:294` | No search criteria (songs) — **converted (inc 9, inline validation)** | Warning / OK | A |
| 8 | `AdvancedSearchDialog.xaml.cs:317` | No search criteria (date/venue) — **converted (inc 9, inline validation)** | Warning / OK | A |
| 10 | `ManageSongsDialog.xaml.cs:199` | Export succeeded — **converted (inc 9, embedded banner, Info)** | Information / OK | A |
| 11 | `ManageSongsDialog.xaml.cs:204` | Export failed — **converted (inc 9, embedded banner, Error)** | Error / OK | A |
| 12 | `ReleaseSelectorDialog.xaml.cs:34` | No release selected — **converted (inc 9, inline validation)** | Warning / OK | A |
| 13 | `UnmatchedSongsDialog.xaml.cs:118` | N songs added — **converted (inc 9, post-close shell banner, Info)** | Information / OK | A |
| 14 | `UnmatchedSongsDialog.xaml.cs:126` | Apply corrections failed — **converted (inc 9, post-close shell banner, Error)** | Error / OK | A |
| 15 | `Views/AlbumDetailView.xaml.cs:730` | Confirm delete track → Recycle Bin — **converted (inc 7)** | Warning / OKCancel | B |
| 16 | `Views/AlbumDetailView.xaml.cs:747` | Could not delete file — **converted (inc 3)** | Error / OK | A |
| 17 | `Views/BoxSetWizardView.xaml.cs:198` | Invalid track date(s) on save — **converted (inc 4)** | Warning / OK | A |
| 18 | `Views/BoxSetWizardView.xaml.cs:255` | Box set save failed — **converted (inc 4)** | Error / OK | A |
| 19 | `Views/BoxSetWizardView.xaml.cs:566` | Pull: invalid date entry — **converted (inc 4)** | Warning / OK | A |
| 20 | `Views/BoxSetWizardView.xaml.cs:574` | Pull: no setlist for date — **converted (inc 4, first Info)** | Information / OK | A |
| 21 | `Views/BoxSetWizardView.xaml.cs:666` | Confirm remove all tracks for date — **converted (inc 7)** | Warning / YesNo | B |
| 22 | `Views/EditMetadataView.xaml.cs:469` | Manifest sidecar write failed — **converted (inc 5)** | Warning / OK | A |
| 23 | `Views/EditMetadataView.xaml.cs:976` | Album type changed (move-files notice) — **converted (inc 5), Warning** (remapped from the original Information icon per gate finding: it is an actionable advisory and must persist) | Information / OK | A |
| 24 | `Views/EditMetadataView.xaml.cs:994` | Save changes failed — **converted (inc 3)** | Error / OK | A |
| 25 | `Views/EditMetadataView.xaml.cs:1007` | Cancel with unsaved changes — **converted (inc 6)** | Question / YesNo | B |
| 26 | `Views/EditMetadataView.xaml.cs:1317` | Cannot verify — required fields missing — **converted (inc 5)** | Warning / OK | A |
| 27 | `Views/EditMetadataView.xaml.cs:1703` | Existing MBID — refresh / search / cancel — **converted (inc 8, three-way)** | Question / YesNoCancel | B |
| 28 | `Views/EditSetlistView.xaml.cs:244` | Invalid date format on save — **converted (slice 1)** | Warning / OK | A |
| 29 | `Views/EditSetlistView.xaml.cs:261` | **Duplicate-date refusal (commit `a6a652b`)** — **converted (slice 1)** | Warning / OK | A |
| 30 | `Views/EditSetlistView.xaml.cs:271` | Empty setlist on save — **converted (slice 1)** | Warning / OK | A |
| 31 | `Views/EditSetlistView.xaml.cs:359` | Setlist save failed — **converted (inc 3)** | Error / OK | A |
| 32 | `Views/EditSetlistView.xaml.cs:369` | Cancel with unsaved changes — **converted (inc 6)** | Question / YesNo | B |
| 33 | `Views/ImportView.xaml.cs:1577` | Display info `.txt` file content — **converted (inc 10, scrollable read panel)** | Information / OK | C |
| 34 | `Views/MbidMigrationView.xaml.cs:80` | Confirm start migration fresh — **converted (inc 8)** | Question / YesNo | B |
| 35 | `Views/MbidMigrationView.xaml.cs:130` | Migration error — **converted (inc 3)** | Error / OK | A |
| 36 | `Views/ReleasesView.xaml.cs:326` | Confirm remove standalone release — **converted (inc 7)** | Question / YesNo | B |
| 37 | `Views/ReleasesView.xaml.cs:359` | Confirm remove last volume — **converted (inc 7)** | Question / YesNo | B |
| 38 | `Views/ReleasesView.xaml.cs:419` | Duplicate release name — **converted (inc 2)** | Warning / OK | A |
| 39 | `Views/SettingsView.xaml.cs:135` | Confirm re-enrich library — **converted (inc 8)** | Question / YesNo | B |
| 40 | `Views/SettingsView.xaml.cs:346` | Confirm reset library data — **converted (inc 8)** | Warning / YesNo | B |
| 41 | `Views/SettingsView.xaml.cs:393` | Reset complete — **converted (inc 5)** | Information / OK | A |
| 42 | `Views/SettingsView.xaml.cs:404` | Reset error — **converted (inc 5)** | Error / OK | A |
| 43 | `Views/SongsView.xaml.cs:227` | Duplicate song name (rename) — **converted (inc 2)** | Warning / OK | A |
| 44 | `Views/SongsView.xaml.cs:262` | Confirm remove song — **converted (inc 7)** | Question / YesNo | B |
| 45 | `Views/SongsView.xaml.cs:327` | Duplicate song name (add) — **converted (inc 2)** | Warning / OK | A |

**Bucket counts:** A = 29, B = 14, C = 1 (total 44) — **all live actionable sites converted**.
(Site IDs run 1–45 with **#9 retired**: `AlbumSearchDialog` was orphaned dead code and was **deleted
2026-06-19**, taking its `MessageBox` site with it — see `follow-ups.md`. The numbering keeps the gap
at #9 so the stable site IDs referenced elsewhere in this spec do not shift.)

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
    // Two-way (Yes/No, OK/Cancel) — IMPLEMENTED (increment 6). true = confirmLabel, false = cancelLabel/Esc.
    // defaultToConfirm (added increment 8): false focuses the negative button (Enter = the safe
    // answer), preserving sites that passed an explicit MessageBoxResult.No default.
    Task<bool> ConfirmAsync(string message, string title,
                            string confirmLabel = "Yes", string cancelLabel = "No",
                            bool defaultToConfirm = true);

    // Three-way (Yes/No/Cancel) — IMPLEMENTED (increment 8). Confirm/Decline/Cancel; default button
    // = Confirm (Enter); Esc = Cancel (the tri-state safe answer).
    Task<ConfirmResult> ConfirmAsync(string message, string title,
                                     string confirmLabel, string declineLabel, string cancelLabel);
}

public enum ConfirmResult { Confirm, Decline, Cancel }   // IMPLEMENTED (increment 8)
```

> **Implementation note (increment 6).** The two-way signature shipped as
> `ConfirmAsync(string message, string title, string confirmLabel = "Yes", string cancelLabel = "No")`
> — the defaults were changed from the original `"OK"/"Cancel"` sketch to `"Yes"/"No"` to match the
> three converted YesNo sites (#3/#25/#32). The host also exposes a parallel `IConfirmHost` interface
> (the view the service forwards to), mirroring the `IAlertSink` split for the banner.
>
> **Implementation note (increment 8).** The three-way overload + `ConfirmResult` shipped with the #27
> conversion. The enum is **role-named `Confirm`/`Decline`/`Cancel`** (not the earlier `Yes`/`No`/`Cancel`
> sketch) because the buttons carry caller-supplied labels — the names describe roles, not button text,
> and align with the two-way `confirmLabel`/`cancelLabel` vocabulary. Three-way keyboard: **Enter =
> default/focused button (Confirm); Esc = Cancel** (the tri-state safe answer, mirroring a Win32
> YesNoCancel box's Esc-close). Both paths share one card/scrim/`TaskCompletionSource<ConfirmResult>`
> and one re-entrancy guard — the two-way path hides the middle button and maps the tri-state result
> down to a bool (`Confirm` → true, else false). The new `defaultToConfirm` flag (two-way) focuses the
> negative button when `false`, **preserving the explicit `MessageBoxResult.No` defaults** three of the
> four batch-2 sites carried (#34/#39/#40 — destructive start-fresh / re-enrich / reset). No pure logic
> was extracted (the result/keyboard/default-button mappings are trivial 1:1 switches with no ordering
> or policy, unlike the banner's `AlertQueue`) — justified skip, consistent with increment 6.

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
6. **[Implemented — pending WPF gate]** Bucket-A sweep, batch 4 — final shell-hosted sites (the
   "increment 5" work session). Converted six sites to `App.Alerts.Notify`, surface only (flows
   unchanged): **#4** (`ShellWindow` box set gone since list loaded, Info), **#22** (`EditMetadataView`
   manifest sidecar write failed, Warning), **#23** (`EditMetadataView` album-type-changed move-files
   notice, **Warning** — see remap below), **#26** (`EditMetadataView` cannot verify — required fields
   missing, Warning), **#41** (`SettingsView` reset complete, Info), **#42** (`SettingsView` reset
   error, Error — full exception detail preserved). Severities map 1:1 from the code's existing
   `MessageBoxImage` except #23 (see below).
   **#23 + #4 blocking-dependency check:** both clear — #4's `MessageBox` fired *after*
   `NavigateToBoxSets()` already refreshed the list (only a bare `return` follows), and #23 moves **no
   files** (it advises the user to move the folder manually); the sole post-notice code is the
   unconditional `_shell.Navigation.GoBack()`, which does not depend on acknowledgment. So both are
   pure surface swaps — neither is a sequencing question.
   **#23 severity remap (gate finding 2026-06-11):** initially converted to Info (matching the old
   Information icon), but the banner auto-dismissed before the *"you may need to move the folder"*
   advisory could be read, because it fires simultaneously with the `GoBack` navigation. Remapped
   **Info → Warning** so it persists until closed — see the severity-follows-content principle in
   Ruling 1.
   Untouched: #25/#27 (EditMetadataView decisions), #39/#40 (SettingsView confirms), #1/#3/#5
   (ShellWindow confirms); all three files still host those `MessageBox` sites, so no aliases removed.
   No new tests (baseline holds at **355**); build clean (51 unique warnings).
   **This completes the bucket-A conversions for all shell-hosted views** — every remaining bucket-A
   site (#7–#14, minus orphaned #9) lives in a modal `ShowDialog()` window and is handled per Ruling 6
   in a later increment. **Manual WPF gate is Gregg's, separate.**
7. **[Implemented — pending WPF gate]** Increment 3 — confirm host (`ConfirmAsync`) + the
   unsaved-changes prompts (the "increment 6" work session — the **first bucket-B increment**, so A
   and B still never interleave per Ruling 5). New: `Views/ConfirmHost.xaml(.cs)` (the full-shell
   dimmed-scrim `IConfirmHost`, mounted in `ShellWindow` at `ZIndex=100` above the banner and
   registered via the new `AlertService.RegisterConfirmHost`); `IAlertService` extended with the
   two-way `Task<bool> ConfirmAsync(message, title, confirmLabel = "Yes", cancelLabel = "No")` and a
   new `IConfirmHost` interface in `Services/IAlertService.cs`; `AlertService.ConfirmAsync` forwards
   to the host (no host → `false`, the safe answer). Converted the three unsaved-changes / leave
   prompts from `MessageBox.Show(..., YesNo, Question)` to `await App.Alerts.ConfirmAsync(...)`,
   **branch mapping preserved exactly** (Yes/true → leave/discard + `GoBack`/`NavigateToLibrary`;
   No/Esc/false → return/stay): **#3** (`ShellWindow.HeaderBar_ImportCancelRequested`), **#25**
   (`EditMetadataView.CancelEdit`), **#32** (`EditSetlistView.CancelEdit`). Each containing method
   became `async void` — #3 was already an event subscription; #25/#32 are UI command handlers called
   fire-and-forget by the HeaderBar Cancel/Back click handlers (nothing runs after the call), so the
   ripple is contained and `NavigateBack()` still delegates unchanged. Decisions recorded in Ruling 2:
   **Esc = the safe negative answer** (a deliberate, uniform improvement — the old YesNo boxes had no
   Esc-close); **Enter = the default Confirm button**; **re-entrancy refuses with
   `InvalidOperationException`**; the card **takes focus by design** (the deliberate contrast with the
   never-focusing banner); banners may fire behind the scrim without breaking. `EditSetlistView`'s now
   unused `using MessageBox` alias removed (#32 was the file's last `MessageBox`); the `MessageBox`
   surfaces in `ShellWindow`/`EditMetadataView` stay (those files still host unconverted confirm
   sites). No new pure logic, **no new tests** (the confirm result mapping is a trivial 1:1, nothing
   to extract — see Ruling 2; baseline holds at **355**); build clean (51 unique warnings). **Manual
   WPF gate is Gregg's, separate.**
8. **[Implemented — pending WPF gate]** Increment 4 — bucket-B sweep, **batch 1: delete/remove
   confirms** (the "increment 7" work session). Converted the seven destructive confirms from
   `MessageBox.Show` to `await App.Alerts.ConfirmAsync(...)`, **branch mapping preserved exactly**
   (the affirmative result → the destructive action; the negative result / Esc → `return`): **#1**
   (`ShellWindow` delete album → Recycle Bin), **#5** (`ShellWindow` delete concert JSON), **#15**
   (`AlbumDetailView` delete track → Recycle Bin), **#21** (`BoxSetWizardView` remove all tracks for
   date), **#36** (`ReleasesView` remove standalone release), **#37** (`ReleasesView` remove last
   volume), **#44** (`SongsView` remove song). Each containing method became `async void`; all are
   either event subscriptions (#1/#5/#37/#21) or fire-and-forget context-menu Click lambdas
   (#15/#36/#44) with nothing running after the call, so the ripple is contained — **no call-site
   signature changes**.
   **#15 label decision:** the sole OKCancel site — labels kept as the original **"OK"/"Cancel"** (no
   relabel; the action-specific "Delete"/"Cancel" was considered and *not* taken, to stay faithful and
   avoid a gate-relabel flag). All YesNo sites use the default "Yes"/"No" labels (increment-6
   precedent).
   **Default-button parity:** every converted call used the 4-arg `MessageBox.Show` overload (no
   explicit `MessageBoxResult defaultResult`), so all defaulted to the first button (Yes/OK). The
   confirm card focuses the Confirm button → parity holds; **no site deliberately defaulted to the
   safe answer, so no Enter-semantics were flipped.**
   **#21 `e.Handled` reorder (flag):** the old handler set `e.Handled = true` only on the *confirmed*
   path, at the very end. Because the confirm is now async (the Click event finishes bubbling at the
   `await`), a late `e.Handled` would be a no-op — so it was moved **synchronously ahead of the
   await**, right after the count guard. Net effect: the ✕ click is now marked handled on every path
   past the guard (previously the No / `count==0` paths left it unhandled). Flagged for the WPF gate
   (confirm the ✕ still behaves — no stray accordion toggle).
   Aliases removed where the converted site was the file's last `MessageBox`: `BoxSetWizardView`
   (#21), `ReleasesView` (#36/#37), `SongsView` (#44). `ShellWindow`/`AlbumDetailView` used
   fully-qualified `System.Windows.MessageBox` (no alias) and now have no `MessageBox` sites. No new
   tests (the result mapping is trivial 1:1; baseline holds at **355**); build clean (51 unique
   warnings). Untouched, deferred to batch 2: #27 (the YesNoCancel site that adds the three-way
   overload + `ConfirmResult`), #34 (MBID start), #39/#40 (SettingsView re-enrich / reset);
   dialog-hosted bucket-A sites still wait for the Ruling 6 increments. **Manual WPF gate is Gregg's,
   separate.**
   **Gate result (2026-06-12):** five of seven gated live on throwaway records — **#1**, **#5**,
   **#21** (including the no-stray-toggle check on the ✕), **#36**, **#37** — all branch paths, silent
   card, scrim confirmed. **#44** and **#15** are **inspection-covered**: identical-pattern surface
   swaps whose UI triggers (both right-click context-menu items — see below) the owner could not
   locate during the gate, so they were not exercised live. **#15's OK/Cancel labels stand unreviewed**
   until that surface is discoverable. Both triggers banked as a delete-affordance discoverability
   follow-up: #44 = SongsView song row right-click → "Remove" `MenuItem`
   (`SongsView.xaml.cs:190-192`); #15 = AlbumDetailView track-grid right-click
   (`TracksDataGrid_MouseRightButtonUp`) → "🗑  Delete Track" `MenuItem`
   (`AlbumDetailView.xaml.cs:718-720`).
9. **[Implemented — pending WPF gate]** Increment 4 — bucket-B sweep, **batch 2: remaining confirms +
   the three-way** (the "increment 8" work session). **This completes bucket B.** Added the three-way
   API (`ConfirmResult { Confirm, Decline, Cancel }`, the `Task<ConfirmResult> ConfirmAsync(message,
   title, confirmLabel, declineLabel, cancelLabel)` overload on `IAlertService`/`IConfirmHost`/
   `AlertService`, the `defaultToConfirm` flag on the two-way path, and a third button in `ConfirmHost`
   shown only on the three-way path) — both paths share one card/scrim/`TaskCompletionSource<ConfirmResult>`
   and the re-entrancy guard (see the increment-8 implementation note under § Service API). Converted
   the four remaining bucket-B sites, **branch mapping preserved exactly**:
   - **#27** (`EditMetadataView` existing-MBID, **three-way**): old `Yes` → `Confirm` → refresh from
     existing MBID then return; old `No` → `Decline` → fall through to fingerprint/name search; old
     `Cancel` → `Cancel` → abort (return). Cancel and No **differ** (that is why it is three-way) and
     are preserved distinctly. Default = Confirm (the old first button, Yes); **Esc = Cancel** (the old
     YesNoCancel box's Esc-close). Labels kept faithful as Yes/No/Cancel (the message body references
     them). Containing handler `MusicBrainzButton_Click` already `async void` — no ripple.
   - **#34** (`MbidMigrationView` start fresh), **#39** (`SettingsView` re-enrich), **#40**
     (`SettingsView` reset library data): two-way, `Yes` (true) → action; `No`/Esc (false) → return.
     #34/#40 handlers became `async void` (event handlers, no ripple); #39 was already `async void`.
   **Default-button parity finding (contrast with batch 1):** unlike the increment-7 deletes,
   **all three** of #34/#39/#40 passed an **explicit `MessageBoxResult.No` default** in the old call
   (the 5-arg `MessageBox.Show` overload) — a deliberate safe-default on heavy/destructive actions
   (notably #40, a library wipe). The task expected none; this is the finding. Preserved per Ruling 2
   via **`defaultToConfirm: false`**, which focuses the negative "No" button so Enter takes the safe
   answer — Enter-semantics unchanged. #27 used the 4-arg overload (default = first button), so its
   default needed no override.
   Alias removed: `MbidMigrationView` (#34 was its last `MessageBox` site). `SettingsView` and
   `EditMetadataView` used fully-qualified `System.Windows.MessageBox` (no alias) and now host no
   `MessageBox` code (only prose references remain in comments). No new tests (the result/keyboard/
   default-button mappings are trivial 1:1, nothing to extract — justified skip per increment 6;
   baseline holds at **355**); build clean (51 unique warnings).
   **Bucket-B complete — remaining-site grep (2026-06-12):** a full `MessageBox.Show` sweep across the
   app (excluding the dead `.bak`) now returns **exactly the 9 expected sites and no strays**: the eight
   dialog-hosted bucket-A sites **#7/#8** (`AdvancedSearchDialog`), **#9** (`AlbumSearchDialog`,
   orphaned), **#10/#11** (`ManageSongsDialog`), **#12** (`ReleaseSelectorDialog`), **#13/#14**
   (`UnmatchedSongsDialog`) — deferred to the Ruling 6 increments — plus bucket-C **#33** (`ImportView`
   info-file viewer, via the `WpfMessageBox` alias). A cross-check grep for
   `MessageBoxButton`/`MessageBoxResult`/`MessageBoxImage` surfaced only those files plus comment-only
   references in the three converted files and `IAlertService.cs` (no live code). **Manual WPF gate is
   Gregg's, separate** — note for the gate: verify #27's three buttons (Refresh=Yes / Search=No /
   Cancel all distinct, Esc = Cancel) and that #34/#39/#40 focus **No** by default (Enter does not fire
   the destructive action).
   **Gate finding + fix (2026-06-12, accent amendment):** on the `defaultToConfirm:false` cards
   (#34/#39/#40) the keyboard correctly resolved No on Enter, but the accent fill was hardcoded to the
   Confirm button, so the card showed Yes highlighted while Enter pressed No. Fixed by deriving the
   accent and the focus from a single `defaultButton` reference in `ConfirmHost.Show`
   (`ApplyDefaultButtonStyling`): the default button is accented and focused, the others drop to the
   plain style — styling-only, resolution unchanged (Enter still resolves the default, Esc the safe
   answer).
10. **[Implemented — pending WPF gate]** Increment 9 — the **Ruling 6 dialog-hosted bucket-A sites**.
    Converted seven sites across three dialogs by the per-classification mechanism Ruling 6 prescribes
    (the shell banner cannot serve a site inside a modal `ShowDialog()` window — it is occluded behind
    the modal). **This completes all bucket-A conversions** (every notification site is now in-window).
    - **Inline validation** (input-validation refusals — the dialog stays open, the user must fix a
      field; no banner): **#7** (`AdvancedSearchDialog` no song/exclude/sequence criteria) and **#8**
      (`AdvancedSearchDialog` track-search no date/venue) render red validation text in the dialog —
      #7 at the **left of the bottom action row** (left of the Search button), #8 **immediately right
      of the Search Tracks button**, below the Date/Venue fields it references. **#12**
      (`ReleaseSelectorDialog` no release selected) renders amber text (dark dialog) at the **left of
      the Select/Cancel row**. Each appears on the failed action, clears on a valid retry (cleared at
      the top of the action handler) and — for #8/#12 — also the moment the user edits a field
      (`TextChanged`) or selects a row (`SelectionChanged`). #7's criteria are checkboxes/sequence
      across three tabs, so it clears on retry only (not per-checkbox) — minor, noted. No sound, no
      focus jump.
    - **Post-close shell-banner hand-off** (terminal notifications — the dialog closes): **#13**
      (songs added, Info) / **#14** (apply-corrections error, Error) in `UnmatchedSongsDialog`. The
      dialog no longer shows its own `MessageBox`; it records the outcome on new
      `SongsAddedCount` / `ApplyErrorMessage` properties, and **both callers** (`EditMetadataView` and
      `ImportView`, which each open it) fire `App.Alerts.Notify` on the shell **after `ShowDialog`
      returns**. **Behavior change (noted):** #14 previously kept the dialog open on error; it now
      closes (`DialogResult=true`, so partial corrections applied before the throw still flow through
      the caller's existing reload), matching Ruling 6's "dialog closes" classification — the error
      surfaces on the persistent shell Error banner instead.
    - **Embedded banner** (terminal notifications, but the dialog persists as a workspace and is not
      closed by the export): **#10** (export succeeded, Info — auto-dismiss) / **#11** (export failed,
      Error — persists) in `ManageSongsDialog`. An `AlertBannerHost` instance is embedded in the
      dialog (overlaid at the top of the list cell, shell pattern) and driven directly via
      `DialogBanner.Show(new AlertItem(...))`.
    **AlertBannerHost reusability — no refactor needed.** The control was already reusable outside the
    shell as-is: it owns a **per-instance** `AlertQueue` + `DispatcherTimer` and exposes a public
    `Show(AlertItem)`; the `AlertService` singleton registration is done **externally** by `ShellWindow`
    (`RegisterSink`), not in the control. So the embedded instance calls `Show` directly and is fully
    independent of the shell singleton's queue (#11's persistent Error in the dialog never touches the
    shell banner) — exactly the isolation Ruling 6 requires. (`xmlns:views` added to the dialog; the
    instance is named `DialogBanner`.)
    **Aliases removed** where the file's last `MessageBox` site was converted: `AdvancedSearchDialog`
    (#7/#8), `ManageSongsDialog` (#10/#11). `ReleaseSelectorDialog` and `UnmatchedSongsDialog` used
    fully-qualified `System.Windows.MessageBox` (no alias).
    **Closing grep (2026-06-12):** a full `MessageBox.Show`/`WpfMessageBox.Show` sweep (excluding the
    dead `.bak`) now returns **exactly two live sites**: **#9** (`AlbumSearchDialog`, orphaned — left
    per the banked delete-vs-revive decision) and **#33** (`ImportView`, bucket-C info-file viewer).
    A cross-check for `MessageBoxButton`/`MessageBoxResult`/`MessageBoxImage` surfaced only those two
    plus comment-only references in `IAlertService`/`MbidMigrationView`/`SettingsView` (no live code).
    No new tests (baseline holds at **355**); build clean (51 unique warnings). **Manual WPF gate is
    Gregg's, separate.**
    **Gate findings (2026-06-12):**
    - **Tab-switch clearing fix:** both `ValidationText` (#7) and `TrackValidationText` (#8) now clear
      on `AdvancedSearchDialog`'s `TabControl.SelectionChanged` (guarded on `OriginalSource` so inner
      `Selector` bubbling does not trigger it) — the #7 bottom-row message lingering while the user
      worked the Track Search tab read as a stale error.
    - **Search-trace verdict — PRE-EXISTING, not a regression:** "selected a song, clicked Search,
      dialog closed, nothing happened" is independent of inc 9. Our change only swapped the
      validation-fail `MessageBox` for inline text and restructured the button bar
      (`StackPanel`→`DockPanel`, same names/handlers/`IsDefault`); the success path
      (`DialogResult = true; Close()`) is unchanged. The caller `HeaderBar_AdvancedSearchRequested`
      (`ShellWindow.xaml.cs:316`) calls `dialog.ShowDialog()` and **discards the result** — the
      Contains/Exclude/Sequence criteria (`SelectedSongs`/`ExcludedSongs`/`SongSequence`, public) are
      **never read by any caller**. So the song-criteria tabs have no result wiring; only the Track
      Search tab (its own in-dialog results grid) is functional. Banked as a UX gap in `follow-ups.md`.
    - **Match Setlist button readability** in the `EditMetadataView` toolbar (washed-out/unreadable
      styling, owner screenshots) banked in `follow-ups.md`.
11. **[Implemented — pending WPF gate]** Increment 10 — **bucket C (#33), the Import info-file
    viewer**. Replaced the `WpfMessageBox.Show(infoContent, …, Information)` in
    `ImportView.ViewInfoButton_Click` with `App.Alerts.ShowReadPanel(name, content)` → a new
    **`Views/ReadPanelHost`**: a full-shell dimmed scrim + a LARGE centered dark card (insets the
    content by an 80×56 scrim margin) with the info-file name as title, a Close button, and a
    **read-only, selectable, `Consolas` (monospace), `NoWrap` `TextBox`** with both scrollbars.
    New service surface: `IAlertService.ShowReadPanel(title, content)` + `IReadPanelHost` +
    `AlertService.RegisterReadPanelHost`/forwarding, registered by `ShellWindow` alongside the other
    hosts. `WpfMessageBox` alias removed from `ImportView` (#33 was its last use).
    **Implementation shape — dedicated host (not a ConfirmHost generalization):** the viewer shares
    *none* of ConfirmHost's decision machinery — no result, no `TaskCompletionSource<ConfirmResult>`,
    no buttons-as-choices, no default-button accent/focus. Folding it in would have meant a mode flag
    and a divergent content area, muddying that host's single responsibility; a separate ~50-line
    UserControl reusing only the scrim/card/Esc/singleton-registration *shape* is the smaller clean
    option and keeps each host single-purpose. It is reached through the existing `App.Alerts` surface
    `ImportView` already uses (one new method), so no new wiring pattern is introduced — and no
    speculative generality (one caller).
    **Layering / z-order:** `ReadPanelHost` sits at **ZIndex 50** — above the banner (10) so it dims
    the whole shell, below the confirm host (100) so a blocking *decision* still outranks an
    informational viewer. A banner firing behind the scrim renders occluded and resurfaces unchanged
    on close (the banner owns its own queue). **Re-entrancy is unguarded** (unlike ConfirmHost's
    throw): the panel holds no pending result and the scrim blocks a second UI-initiated open, so a
    second `Show` would simply replace the text — harmless.
    **Keyboard:** the shell gates Esc → `Hide` while `ReadPanel.IsShowing`, but — unlike the confirm
    gate, which swallows everything — lets **all other keys fall through to the focused `TextBox`** so
    PgUp/PgDn/arrows scroll (the text area is focused on show); mouse-wheel scrolls natively. Shell
    shortcuts (Space/Ctrl+F/Delete/Esc-back) are still suppressed (the gate returns before them).
    **What the old `MessageBox` lost, now restored:** (1) **monospace alignment** — the Win32 box used
    a proportional font, mangling ASCII-art etext/taper-note columns; `Consolas` + `NoWrap` preserves
    them; (2) **full content** — the box had no scrolling, so long files were clipped/unreadable; the
    panel scrolls vertically and horizontally; (3) **selection/copy** in a comfortable reading area.
    **Closing grep (2026-06-12):** the only remaining `MessageBox.Show` in the app is **orphaned #9**
    (`AlbumSearchDialog:40`); a cross-check for `MessageBoxButton`/`Result`/`Image` confirms #9 is the
    sole live site. **Bucket C is complete; all 44 live actionable sites are converted.** No new tests
    (the panel is pure view; baseline holds at **355**); build clean (51 unique warnings). **Manual WPF
    gate is Gregg's, separate.**

12. **[Implemented — gate cleared 2026-06-12]** Spec close-out (this commit, doc-only). Increment 10's
    manual WPF gate (the #33 info-file read panel) **PASSED**, clearing the last converted bucket. With
    buckets A, B, and C all converted and gated, **Status flips to Implemented** (header). No code
    change; tests/warnings unchanged (355 passed; 51 unique warnings).

**True zero reached (2026-06-19).** *Implemented* originally meant **all live actionable MessageBox
sites are converted** — the in-scope sites across buckets A/B/C — while one orphan remained: **#9**
(`AlbumSearchDialog.xaml.cs:40`), a live `MessageBox.Show` with no caller, excluded from the actionable
count and tracked as a separate **delete-vs-revive** decision (`follow-ups.md`). That decision came
back **delete**: `AlbumSearchDialog` was dead since the March shell cutover (no caller, no lived demand
for a manual MusicBrainz-search entry point), so the dialog was removed and its `MessageBox` went with
it. A full sweep — `grep -rn "MessageBox.Show" --include=*.cs` (excluding `bin/`/`obj/`) — now returns
**zero hits in compiled source**. The codebase no longer contains a single live `MessageBox.Show`; the
dead `LibraryBrowserWindow.xaml.cs.bak` (4 phantom sites) was also **deleted 2026-06-19**
(`follow-ups.md`), leaving only comment-only `MessageBox` mentions. The dialog is
recoverable from git `006d170` if a manual album-search feature is ever scoped.
