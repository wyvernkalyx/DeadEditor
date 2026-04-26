# Double Date Suffix Bug Diagnostic — 2026-04-25

## 1. Existing date-suffix logic

### 1.1 Central parser: `MetadataService.ParseTitleAndDate()` (line 488)
- **What:** Splits a raw FLAC title into `(cleanedSongName, dateString)`. Handles 5 patterns: MusicBrainz Live format, yyyy-MM-dd in parens, bracket dates, slash dates, etc.
- **Key regex (line 602):** `^(.+?)\s*\((\d{4}-\d{2}-\d{2})[^)]*\)\s*$`
- **Call sites:**
  - `ReadFolder()` line 106 — **only in import mode** (`editMode == false`)
  - `EditMetadataView.NormalizeButton_Click()` line 751 — **only when user clicks Normalize**
- **Critical observation:** NOT called during Edit Metadata's load path.

### 1.2 `MetadataService.CleanTitle()` (line 695)
- **What:** Strips date suffixes and segue markers from titles.
- **Regex (line 701):** `\s*→?\s*\(\d{4}-\d{2}-\d{2}\)\s*$`
- **Call sites:** Currently commented out in `ReadFolder()` line 131 ("REMOVED - this was destroying original metadata")

### 1.3 `EditMetadataView.ReconstructRawTitles()` (line 711)
- **What:** Rebuilds `RawTitle` from `SongName + Segue + TrackDate`. Has double-date prevention regex.
- **Regex (line 718):** `\s*\(\d{4}-\d{2}-\d{2}\)(\s*\(\d{4}-\d{2}-\d{2}\))*\s*$`
- **Call sites:** Called from Normalize, Match Setlist, Match to Song, ApplyMbTrackTitles — **never from load**.

### 1.4 `MetadataService.WriteMetadata()` (line 334)
- **What:** Writes FLAC TITLE tag. Independently reconstructs title from `SongName + HasSegue + date`.
- **Regex (line 354):** `\s*\(\d{4}-\d{2}-\d{2}\)(\s*\(\d{4}-\d{2}-\d{2}\))*\s*$` — strips ALL date suffixes before re-adding.
- **Also strips segue markers (line 358):** `(\s*>)+\s*$`
- **Key insight:** WriteMetadata has its own double-date prevention. This means saving works correctly even if `SongName` contains a date suffix — it gets stripped before re-adding.

### 1.5 `LibraryImportService.BuildFinalTitle()` (line 674)
- **What:** Prevents double-date when building final FLAC TITLE for import.
- **Regex (line 674):** `\s*\((\d{4}-\d{2}-\d{2})\)\s*$`
- **Only used during import** — not relevant to Edit Metadata.

### 1.6 `NormalizationService.NormalizeDateInTitle()` (line 472)
- **What:** Converts slash-format dates to yyyy-MM-dd.
- **Only used during normalization** — not relevant to load path.

### 1.7 Date extraction in `EditMetadataView.LoadData()` (line 143)
- **What:** After `ReadFolder(editMode: true)`, extracts `TrackDate` from `SongName` using regex.
- **Regex (line 149):** `\((\d{4}-\d{2}-\d{2})\)`
- **Critical: extracts the date into `TrackDate` but does NOT strip it from `SongName`.**

## 2. Load paths

### 2.1 Import view load path
- **Entry:** `ImportView.LoadFolder()` (various lines)
- **Source:** `_metadataService.ReadFolder(folder, editMode: false)` — import mode
- **What happens at ReadFolder line 106:**
  ```csharp
  var (songName, extractedDate) = ParseTitleAndDate(rawTitle);
  // SongName = "Viola Lee Blues"  (clean, no date)
  // TrackDate = "1968-02-23"
  ```
- **ViewModel construction:** `new TrackInfoViewModel(track, _albumInfo, isEditMode: false)`
- **DisplayTitle builds:** `"Viola Lee Blues (1968-02-23)"` — one date, correct.

### 2.2 Edit Metadata load path
- **Entry:** `EditMetadataView.LoadData()` (line 84)
- **Source:** `_metadataService.ReadFolder(folder, editMode: true)` — edit mode
- **What happens at ReadFolder line 88-100:**
  ```csharp
  // editMode == true: NO ParseTitleAndDate called
  SongName = rawTitle;  // "Viola Lee Blues > (1968-02-23)" — FULL FLAC title with date
  TrackDate = "";       // Empty — no extraction
  ```
- **Then at LoadData line 143-154:** Date extraction regex finds `(1968-02-23)` in `SongName`, populates `TrackDate = "1968-02-23"`. **But SongName is NOT cleaned.** SongName still = `"Viola Lee Blues > (1968-02-23)"`.
- **ViewModel construction (post-migration):** `new TrackInfoViewModel(track, _albumInfo, isEditMode: false)`
- **`UpdateDisplayTitle()` builds (line 142-154):**
  ```
  songName = "Viola Lee Blues > (1968-02-23)"  // from SongName — already has date!
  + " >" (if Segue, but Segue detected from title, so double segue too)
  + " (1968-02-23)"  // from TrackDate — SECOND date appended
  = "Viola Lee Blues > (1968-02-23) > (1968-02-23)"
  ```
- **Result:** Double date suffix. Bug confirmed.

### 2.3 Pre-migration behavior (for comparison)
- Before today's ViewModel migration, EditMetadataView bound the Title column to `RawTitle` directly.
- `RawTitle` = the raw FLAC title as-is (e.g., `"Viola Lee Blues > (1968-02-23)"`).
- This showed ONE date suffix — the one baked into the FLAC tag. No double.
- **The ViewModel migration changed the binding from `RawTitle` to `DisplayTitle`**, which aggregates `SongName + Segue + TrackDate`. Since `SongName` already contains the date (from edit mode's raw read), and `TrackDate` was extracted from it, the date appears twice.

### 2.4 LibraryShow load path
- At startup, library scanner reads tracks for search/display purposes.
- Not directly relevant — Edit Metadata re-reads from disk via `ReadFolder`.

## 3. Normalize button

### 3.1 Edit Metadata handler (`NormalizeButton_Click`, line 733)
```csharp
foreach (var vm in _tracks)
{
    var track = vm.Track;
    var (cleanName, date) = _metadataService.ParseTitleAndDate(track.RawTitle);
    track.SongName = cleanName;  // NOW stripped of date
    if (!string.IsNullOrEmpty(date) && string.IsNullOrEmpty(track.TrackDate))
        track.TrackDate = date;
}
```
- **After Normalize:** `SongName = "Viola Lee Blues"` (clean), `TrackDate = "1968-02-23"`.
- **DisplayTitle rebuilds:** `"Viola Lee Blues (1968-02-23)"` — one date, correct.
- **This is the "fix" the user remembers** — clicking Normalize strips the date from SongName.

### 3.2 Import handler (not relevant)
- Import uses `ReadFolder(editMode: false)` which calls `ParseTitleAndDate` automatically. The Normalize button in Import runs `NormalizeAll` for fuzzy matching but the date is already stripped.

## 4. Import vs Edit Metadata behavior comparison

| Aspect | Import | Edit Metadata (post-migration) |
|--------|--------|-------------------------------|
| ReadFolder mode | `editMode: false` | `editMode: true` |
| ParseTitleAndDate called on load? | **Yes** (line 106) | **No** |
| SongName after load | `"Viola Lee Blues"` (clean) | `"Viola Lee Blues > (1968-02-23)"` (raw) |
| TrackDate after load | `"1968-02-23"` (extracted) | `"1968-02-23"` (extracted post-hoc at line 149) |
| ViewModel isEditMode | `false` | `false` (changed by today's migration) |
| DisplayTitle | `"Viola Lee Blues (1968-02-23)"` ✓ | `"Viola Lee Blues > (1968-02-23) (1968-02-23)"` ✗ |
| After Normalize click | Still correct | Corrected to single date |

**The divergence is clear:** Import strips dates during `ReadFolder` via `ParseTitleAndDate`. Edit Metadata reads raw titles (edit mode) and never strips them. The ViewModel migration then surfaces this dirty `SongName` through `DisplayTitle` aggregation.

**Pre-migration this was invisible** because the Title column bound to `RawTitle` (the raw FLAC tag), not to `DisplayTitle`. The date was in the tag, and `RawTitle` showed it as-is — one copy. The migration changed the binding to `DisplayTitle`, which appends `TrackDate` on top of the already-dated `SongName`.

## 5. Git history of related changes

### The original double-date fix
**`66e1bf8` (2026-03-07)** — "Fix date doubling bug when re-importing files with formatted metadata"
- Created `ParseTitleAndDate()` to split title into SongName + TrackDate.
- Fixed the import path only. Edit mode didn't exist yet.

### Edit mode introduced
**`c7582cc` (2026-03-26)** — "Phase 1 - Shell window scaffold with sidebar navigation"
- Added `editMode` parameter to `ReadFolder()`.
- Edit mode intentionally skips `ParseTitleAndDate` — designed for "as-is" display where the Title column bound to `RawTitle`.
- This was correct at the time because Edit Metadata's grid showed `RawTitle`, not a computed `DisplayTitle`.

### ParseTitleAndDate made public for Edit Metadata's Normalize
**`7d3cc59` (2026-03-29)** — "Clean raw FLAC titles before normalization in Edit Metadata"
- Made `ParseTitleAndDate` public so Edit Metadata's Normalize button can call it.
- This was a manual fix path: click Normalize → calls `ParseTitleAndDate` → strips date from `SongName`.

### Today's migration
**Uncommitted** — "Migrate EditMetadataView to TrackInfoViewModel architecture"
- Changed `_tracks` from `List<TrackInfo>` to `ObservableCollection<TrackInfoViewModel>`.
- Changed Title column from `{Binding RawTitle}` to `{Binding DisplayTitle}` (display) / `{Binding SongName}` (edit).
- Constructed ViewModels with `isEditMode: false` so `DisplayTitle` uses the aggregated format.
- **This is what exposed the bug:** `DisplayTitle` aggregates `SongName + TrackDate`, but `SongName` already contains the date from the raw FLAC title.

## 6. Most likely hypothesis

**Hypothesis E is correct, with nuance: Today's ViewModel migration exposed a latent inconsistency, but the root cause predates the migration.**

Evidence:
1. Edit mode (`ReadFolder` line 83-100) stores the full FLAC title (including date suffix) into `SongName`. This was always the case since `c7582cc`.
2. `LoadData` line 143-154 extracts the date from `SongName` into `TrackDate` but doesn't strip it from `SongName`. This was also always the case.
3. Pre-migration, `SongName` containing the date was invisible because the grid bound to `RawTitle`. The data was "wrong" but the display was "right" by accident.
4. The migration changed the display binding to `DisplayTitle`, which computes `SongName + " (" + TrackDate + ")"`. Now the date appears twice — once embedded in `SongName`, once appended by `DisplayTitle`.
5. The Normalize button has always been the manual fix: it calls `ParseTitleAndDate` to strip the date from `SongName`, making `DisplayTitle` correct. But this requires user action.

**The underlying design tension:** Edit mode was designed to show raw FLAC tags as-is. The ViewModel migration changed "as-is" to "computed aggregation" without adding the strip-on-read step that the aggregation requires.

### Why Import doesn't have this bug
Import calls `ReadFolder(editMode: false)`, which runs `ParseTitleAndDate` during load. `SongName` is always clean. `DisplayTitle` aggregation works correctly.

### The double segue marker
There's also a secondary bug: `SongName` = `"Viola Lee Blues > (1968-02-23)"` has a segue marker `>` embedded. The `HasSegue` flag is set to `true` (detected at ReadFolder line 98). Then `UpdateDisplayTitle()` line 147-149 appends another `" >"`. Result: double segue marker.

## 7. Recommended fix

### Primary fix: Strip-on-read in Edit Metadata's LoadData

**Location:** `EditMetadataView.xaml.cs`, `LoadData()` method, after `ReadFolder` returns and before ViewModel wrapping.

**Change the date extraction block (current lines 143-154)** from "extract date but leave SongName dirty" to "extract date AND strip from SongName":

```csharp
// After ReadFolder(editMode: true), SongName contains the raw FLAC title
// including any date suffix and segue markers. Parse it clean.
foreach (var track in rawTracks)
{
    var (cleanName, date) = _metadataService.ParseTitleAndDate(track.SongName);
    track.SongName = cleanName;
    if (!string.IsNullOrEmpty(date))
        track.TrackDate = date;
    // HasSegue was already detected by ReadFolder from the raw title
}
```

This is the **exact same logic** that the Normalize button already runs (line 749-754), just moved to load time. It uses the existing `ParseTitleAndDate` method which handles all 5 date patterns plus segue stripping.

### Why this is safe
- `ParseTitleAndDate` is battle-tested (5 patterns, used by Import on every load).
- `RawTitle` is preserved unchanged — it still holds the original FLAC tag value.
- `WriteMetadata` reads `SongName` (not `RawTitle`) and independently strips/re-adds the date suffix, so save correctness is unaffected.
- The Normalize button still works — it re-runs `ParseTitleAndDate` on `RawTitle` (which won't change since `RawTitle` was never modified).

### What NOT to change
- Do NOT change `ReadFolder` itself. The `editMode` parameter is used by other callers (or may be in the future). The fix belongs in EditMetadataView's load path.
- Do NOT change `TrackInfoViewModel.UpdateDisplayTitle()`. The aggregation logic is correct — it's the input data that's wrong.
- Do NOT remove `ReconstructRawTitles()`. It's still called from batch operations.

### Scope
- **Single file:** `EditMetadataView.xaml.cs`
- **Single location:** `LoadData()`, replacing the date extraction block (~10 lines changed)
- **Regex/pattern:** Reuses existing `ParseTitleAndDate` — no new regex needed

### Alternative: Call ParseTitleAndDate inside ReadFolder even in editMode
This would fix it at the source, but changes the contract of `editMode` which was explicitly designed to preserve raw titles. The LoadData fix is more surgical and doesn't risk side effects for other callers.

## 8. Open questions

1. **Should the Normalize button's `ParseTitleAndDate` call be removed after this fix?** If SongName is already clean on load, the Normalize button's pre-clean step (line 749-754) becomes a no-op for the date portion. It still does the `NormalizeAll` fuzzy matching, so it's not completely redundant, but the `ParseTitleAndDate` call inside it would be redundant. Leave it for safety — double-parsing a clean title is harmless.

2. **Are there other views that read tracks with `editMode: true`?** Search found only EditMetadataView. But if any future view uses edit mode and wraps in ViewModels, it would hit the same bug. Consider whether the fix should be in `ReadFolder` itself (always parse, store raw in `RawTitle`, clean in `SongName`) vs. in the view.

3. **Does the double segue marker also need fixing?** The same strip-on-read via `ParseTitleAndDate` will fix this too — `ParseTitleAndDate` strips trailing segue markers from the extracted song name. `HasSegue` is already set correctly by `ReadFolder` from the raw title.

4. **Should we add a regression test?** A unit test that loads a track with title `"Dark Star > (1968-02-23)"`, wraps in a ViewModel with `isEditMode: false`, and asserts `DisplayTitle == "Dark Star > (1968-02-23)"` (not `"Dark Star > (1968-02-23) > (1968-02-23)"`) would prevent this from recurring.
