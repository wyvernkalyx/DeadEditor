# Add Concert Spec

**Status:** Implemented (2026-06-10)
**Date:** 2026-06-10
**Branch:** `feature/library-verification-surface`

**Cross-references**
- `documentation/concert-verification-spec.md` — the verification surface this feature feeds into
  (a hand-created concert is born unverified and uses the same `ConcertVerifyGate` to become
  verified). Add Concert was explicitly carved out of that spec as a related but separate feature.
- `documentation/verification-model.md` § Concert Surface — the shipped concert verification model.
- `documentation/follow-ups.md` — the Add Concert entry (now active, pointing here).
- `Views/ConcertDatabaseView.xaml(.cs)`, `Views/HeaderBar.xaml(.cs)`, `Views/EditSetlistView.xaml.cs`,
  `Services/ConcertLookupService.cs`, `Models/ConcertReference.cs`, `ShellWindow.xaml.cs`.

---

## BLUF
There is no way to create a concert record in-app — the concert store (`Data/concerts/*.json` via
`ConcertLookupService`) is populated only by the `tools/SetlistFetcher` crawl, and `EditSetlistView`
edits existing records only. Add Concert is a UI entry point to hand-build a brand-new
`ConcertReference` and save it as a first write. The record is **born unverified** and uses the
already-shipped `ConcertVerifyGate` to become verified later. This is the box-set "+ New Box Set"
pattern applied to concerts, plus one new save-time rule (duplicate-date refusal) that doubles as a
fix for a pre-existing silent-overwrite bug in the normal edit flow.

---

## Purpose
A UI entry point to create a brand-new `ConcertReference` by hand.

**Lived demand.** Gregg hit this wall during the 2026-06-10 verification gating: there was no way to
enter a show that the fetcher had never produced a file for. The app is **multi-band by design**, and
no `SetlistFetcher` source exists for most artists — for an actively touring band, new shows happen
and have no setlist.fm-sourced file. Hand entry is the only path for those records. This passes the
CLAUDE.md "check lived demand, not abstract desire" test: it is a concrete gap blocking real use, not
a speculative nicety.

**Phase A finding (2026-06-10).** The substrate already handles a blank `ConcertReference`
end-to-end; the audit confirmed each link:
- The first-save atomic write (`EditSetlistView.SaveChangesAsync`, temp-write + rename) works on an
  **absent** target — `File.Exists(targetPath)` guards the pre-delete, so a first write just moves
  the temp into place.
- The old-date recycle step is **guarded against `_originalDate == ""`** (the blank-instance case):
  the recycle only fires when `_originalDate` is non-empty AND differs from the saved date, so a
  first save never tries to recycle a nonexistent `{oldDate}.json`.
- `ConcertLookupService.NotifySaved(oldDate, concert)` **inserts absent keys** and maintains
  `_sortedDates` (binary-search insert), so a brand-new date lands in sorted position in the cache.
- A blank concert is **born unverified** (`ConcertReference.Verified` defaults `false`) and is
  **gate-blocked** — `ConcertVerifyGate.Evaluate` fails first on the empty date.
- `ConcertDatabaseView.RefreshFromCache`, called on every navigation-becomes-current, rebuilds the
  grid's `ItemsSource` from the cache, so a newly saved concert appears on back-navigation.

So the feature is **the entry point plus one new save rule** — not a new storage, cache, or
verification mechanism.

---

## Decision 1 — Duplicate-date policy: refuse at save
The save path **refuses** when the entered date collides with a *different* existing record:

```
ConcertLookupService.Instance.HasConcert(date)
    && !string.Equals(date, _originalDate, StringComparison.Ordinal)
```

- **The `_originalDate` exclusion is load-bearing.** Without it, re-saving an existing concert
  without changing its date would refuse against itself (the date is already in the cache — it *is*
  that record). Excluding the case where the entered date equals the record's own original date lets
  an unchanged-date save through while still catching a collision with a *different* record. For a
  hand-created concert `_originalDate == ""`, so any existing date collides — exactly the intent.
- **The refusal message names the existing record's venue** (via
  `ConcertLookupService.GetConcertByDate(date)`), so the user knows what would have been clobbered
  (e.g. *"A concert already exists for 1977-05-08 (Barton Hall, Cornell University). Change the date
  or edit the existing record."*).
- **Scope note — this also fixes a pre-existing silent-overwrite bug in the normal edit flow.**
  Today, editing concert A's date to equal concert B's date overwrites B's `{date}.json` file and
  replaces B's cache entry with no prompt (the atomic write deletes any existing target; `NotifySaved`
  overwrites the dictionary slot). One check at the save path covers **both** the New-Concert and
  the mid-edit collision paths — the same predicate, because the `_originalDate` exclusion is what
  distinguishes "saving myself" from "landing on someone else."
- **Surface.** `MessageBox` for now, consistent with the existing `SaveChangesAsync` validations
  (invalid date, empty setlist). The banked in-app-alert follow-up sweeps all of these dialogs
  together later; this feature does not pre-empt that.

**Rejected alternatives.**
- *Silent overwrite* (status quo) — a foot-gun with hand-entered dates; the whole reason this rule
  exists.
- *Overwrite / Cancel prompt* — invites the destructive choice the feature is meant to prevent; a
  curated record should not be one mis-click from being clobbered.
- *Open-existing-instead* — only meaningful at creation time, misses the mid-edit collision path, and
  adds navigation plumbing. Refuse is uniform across both paths.

Refuse is the concert analog of the box-set `BoxSetSaveResolution.NameCollision` outcome.

---

## Decision 2 — Entry wiring: grid header button, shell constructs blank, editor directly
Mirror the box-set "+ New Box Set" pattern.

- **Header.** Restructure the `ConcertsHeader` block in `HeaderBar.xaml` from its single horizontal
  `StackPanel` into the **3-column Grid (`Auto / * / Auto`)** the Box Sets header uses, with a
  right-aligned **`+ New Concert`** button. The button style is cloned from `SaveChangesButton` — the
  established in-header primary-action treatment, matching `NewBoxSetButton`. The existing title /
  count / ownership filter / search controls move into the left/middle columns unchanged.
- **Event.** The button raises a new `NewConcertRequested` event on `HeaderBar` (mirroring
  `NewBoxSetRequested`).
- **Shell handler.** Constructs a blank record and navigates straight to the editor:
  ```
  var concert = new ConcertReference();
  _navigationService.NavigateTo(new EditSetlistView(this, concert), concert);
  ```
  **No intermediate blank detail view** — the editor opens directly on the empty form.
- **Back-navigation.** Save or cancel lands on `ConcertDatabaseView` (the back-stack entry), which
  `RefreshFromCache` repaints: a **saved** new concert appears in sorted position; a **cancelled**
  one leaves no file and no cache entry (nothing was written, nothing was inserted).
- **Header back-label fallback.** `HeaderBar.ShowEditSetlistHeader` sets the back button to
  `"← {VenueName}"`. For a blank concert `VenueName` is empty, which would render a bare arrow — fall
  back to **"New Concert"** when the venue is empty.

---

## Decision 3 — Save floor: unchanged
The creation floor remains today's `SaveChangesAsync` validations:
- date matches `^\d{4}-\d{2}-\d{2}$`, and
- at least one track.

Venue and per-track song names remain **verify-gate** requirements only — they are *not* added to the
save floor. A hand-created record is **not held to a stricter save standard than a fetched one**: the
fetcher writes venue-only stubs and setlist-less shells today, and the editor must remain able to save
the same shapes. The verify gate is the quality bar; the save floor only guarantees a **keyable,
non-degenerate** file (a valid date for the `{date}.json` filename + cache key, and a non-empty
setlist so the record is not an empty husk).

---

## Verification interaction (documented behavior, no code change)
Hand-created concerts are **born unverified**. The `SetlistFetcher` skip-verified guard (commit
`8258116`) only protects **verified** concerts: it skips the write when the existing file's `verified`
field is `true`, and writes normally otherwise.

**Exposure.** A hand-created concert for a date the fetcher's source *does* know about **will be
overwritten on the next full crawl** unless it is verified first. The skip-guard does not protect
unverified files, by design (unverified files are assumed fetcher-owned and refreshable).

**Spec guidance.** Verify hand-created concerts once they are correct, to protect the curation from the
next crawl. For dates or artists **outside** the fetcher's source (the multi-band case that motivates
this feature), no such exposure exists — the fetcher will never produce a competing file.

---

## Out of scope
- Add-Concert from `ConcertDetailView` (only the grid-header entry point ships).
- Any change to the verify gate (`ConcertVerifyGate` is unchanged).
- In-app alert replacement of `MessageBox` (banked follow-up; this feature uses `MessageBox` to stay
  consistent with the surrounding save validations).
- Album / box-set adoption of the Unverify control (separate banked follow-up).

---

## Commit plan (ledger)
1. **[Implemented `67be225`]** Spec doc (`add-concert-spec.md`) + `follow-ups.md` update marking the
   Add Concert entry active. Docs-only.
2. **[Implemented `7362cee`]** Entry point: `ConcertsHeader` restructure + `+ New Concert` button +
   `NewConcertRequested` event + shell handler constructing the blank record + back-label fallback.
   Manual WPF gate cleared 2026-06-10.
3. **[Implemented `a6a652b`]** Duplicate-date refusal on the save path (with the `_originalDate`
   exclusion). Manual WPF gate cleared 2026-06-10 — both the collision case (refused) and the
   no-change re-save case (allowed through) confirmed, plus the free-date rekey regression.
4. **[Implemented `e182ec3`]** Tests + polish: extracted the collision predicate as the pure
   `Services/DuplicateDateRule.IsCollision` (behavior-identical rewire of the commit-3 inline check),
   with five xUnit cases (create-collision, free date, no-change re-save, mid-edit collision,
   free-date rekey). The planned sixth Ordinal-sensitivity case was **dropped as non-meaningful**:
   `yyyy-MM-dd` keys are pure ASCII (digits + hyphens), so an Ordinal vs culture-aware comparison can
   never diverge for them — the rationale is recorded in the test file. Test baseline moves
   **333 → 338**. No WPF in tests.
5. **[Implemented `<this commit>`]** Docs close-out: this ledger to Implemented; `follow-ups.md`
   Add Concert entry closed and the in-app-alert entry strengthened; `verification-model.md` Concert
   Surface cross-reference; `CLAUDE.md` Handbook entry + test baseline bumped to 338. Docs-only.
