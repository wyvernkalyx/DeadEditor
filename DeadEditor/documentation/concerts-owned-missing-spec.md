# Concerts View: Owned/Missing Indicators + Click-to-Import

**Status:** Shipped in 451a0d7 (owned/missing row coloring, All/Owned/Missing filter, right-click "Import recording for this date…", ownership refresh at startup and after every import).

## 1. Goals

- **Visual indication** of which concerts in the 2,292-show list the user owns media for
- **Filter** the list to Show All / Owned Only / Missing Only
- **Click a missing concert** → folder picker → import that folder as the recording for that date (pre-populated metadata)
- **Click an owned concert** → navigate to the matching album detail (handle multiple recordings via intermediate Concert Detail enrichment)

## 2. Non-Goals

- Does NOT change the Releases view (separate future feature requiring MBID foundation)
- Does NOT change how imports work internally — only pre-populates the target date/venue
- Does NOT merge audience and official recordings into a single entity — they remain distinct `AlbumType` values
- Does NOT add MBID tagging (out of scope)

## 3. Ownership Model

### Definition

A concert date is "owned" if the user's library contains **any** recording (audience OR official) whose `Date` field matches.

### Implementation

- **Source of truth:** `_libraryDates` HashSet in [ConcertDatabaseView.xaml.cs](Views/ConcertDatabaseView.xaml.cs) — populated via `SetLibraryShows()`, which `ShellWindow.xaml.cs` invokes at startup (after library load) and after every import. `ConcertDatabaseView` consumes the companion `_libraryShowsByDate` for row coloring and the ownership filter.
- **Population:** ShellWindow must call `_concertsView.SetLibraryShows(showsByDate)` after library loads, passing dates and grouped shows extracted from `_shows` in LibraryGridView (same pattern used by `BuildMissingShowRows()` at [LibraryGridView.xaml.cs:540-554](Views/LibraryGridView.xaml.cs#L540-L554)).
- **Matching:** Exact string match on normalized `yyyy-MM-dd` format. No fuzzy/near-miss date matching.
- **Refresh timing:** Refresh `_libraryDates` at startup (after library load) AND after every successful import completion.

### Multi-show dates (Early/Late)

**Decision:** Ownership is per-date. If the user owns *any* recording dated `1969-01-26`, *all* `ConcertReference` entries for that date show as "owned."

Rationale: the `_libraryDates` HashSet contains only date strings with no Early/Late qualifier, and `LibraryShow.Date` likewise stores just the date. Distinguishing Early vs Late ownership would require matching on date + venue, which is fragile and not supported by the current data model.

### Recording count per date

For click-routing and the ConcertDetailView recordings list, we need to know how many `LibraryShow` entries exist for a given date. A `Dictionary<string, List<LibraryShow>> _libraryShowsByDate` is built in ConcertDatabaseView alongside `_libraryDates`. ShellWindow passes both the dates and the grouped shows via `SetLibraryShows()`.

## 4. UI Changes

### Row Styling

| State | Text Color | Rationale |
|-------|-----------|-----------|
| Owned | `#4EC9B0` (teal green) | Matches the existing theme's accent green; distinct from the cyan (`#4FC1FF`) used for set headers in ConcertDetailView |
| Missing | `#6E6E6E` (dim grey) | Clearly muted against the `#CCCCCC` default text, signals "not available" |

- Applies to **all columns** in the row (Date, Venue, City/State, Songs)
- **Selection highlight** preserved as-is (`#37373D` background, white foreground) for both owned and missing rows — selection overrides the color distinction
- **Hover state** preserved as-is (`#2A2D3A` background) — row text color remains teal or grey on hover

### Filter Toggle

- **Control type:** ComboBox (matches the Library header's Show Type filter style), placed to the **right of the count text** in the ConcertsHeader area
- **Options:** `All` | `Owned` | `Missing`
- **Default:** `All`
- **Persistence:** Session only — resets to `All` when navigating away from Concerts view and coming back. No settings.json change needed.
- **Interaction with search:** Filter and search compose (AND). Searching "cornell" with filter "Owned" shows only owned concerts matching "cornell."
- **Count display:** Header count updates to reflect the combined filter+search result (e.g., "47 / 2,292 concerts" when filtered to Owned)

### Right-click Context Menu

- Available on **every row** (both owned and missing)
- Menu item: **"Import recording for this date…"** (with ellipsis)
- Click → opens folder picker → on selection, navigates to ImportView with pre-populated metadata from the concert database
- Folder parsing overrides concert database values once a folder is selected

### Click Behavior

**Double-click always navigates to ConcertDetailView**, regardless of whether 0, 1, or many recordings exist for that date. Consistent behavior for all rows.

- ConcertDetailView is enriched (not replaced) to show library-aware content based on ownership state
- The library shows for the clicked date are passed to ConcertDetailView as an optional parameter

## 5. Concert Detail View — Library-Aware Enrichment

### Constructor Change

```csharp
public ConcertDetailView(ShellWindow shell, ConcertReference concert, List<LibraryShow>? libraryShows = null)
```

### Conditional Content (left panel, below setlist.fm link)

**When `libraryShows` is null or empty (missing concert):**
- **"Import Recording for This Date…"** button
- Click → opens folder picker
- On folder selection → navigates to ImportView with pre-populated metadata:
  - Date from `ConcertReference.Date`
  - Venue from `ConcertReference.Venue`
  - City/State from `ConcertReference.FormattedLocation`
- Folder parsing overrides concert database values once a folder is selected
- After import completes, `_libraryDates` is refreshed to reflect the new recording

**When `libraryShows` has entries (owned concert):**
- **"Recordings"** section listing each library recording:
  - Label: album name (official) or show type + date (audience)
  - Each entry is clickable → navigates to that recording's AlbumDetailView
  - Follows existing navigation pattern from [LibraryGridView.xaml.cs:1110-1111](Views/LibraryGridView.xaml.cs#L1110-L1111)

**No "Import Another Recording" button on owned dates.** Import is available via right-click context menu on the ConcertDatabaseView grid only.

### Navigation

- **Entry:** Double-click any row in ConcertDatabaseView (both owned and missing)
- **Back:** Returns to ConcertDatabaseView (existing behavior via `_shell.Navigation.GoBack()`)

## 6. Architecture

### Data Flow

1. **Library loads** → ShellWindow calls `LibraryGridView.LoadShowsAsync()` which populates `_shows`
2. **ShellWindow extracts dates** → builds `Dictionary<string, List<LibraryShow>>` of all `show.Date` values from `_shows`
3. **ShellWindow passes shows** → `_concertsView.SetLibraryShows(showsByDate)` — populates both `_libraryDates` and `_libraryShowsByDate`
4. **ConcertDatabaseView renders** → each row checks `_libraryDates.Contains(concert.Date)` for styling via `LoadingRow` event
5. **Filter toggle** → `ApplyFilter()` extended to also filter by ownership state
6. **Double-click** → `NavigateToConcertDetail(concert)` now also passes matching `LibraryShow` list to ConcertDetailView
7. **Right-click → Import** → opens folder picker, navigates to ImportView with pre-populated concert metadata
8. **Import completes** → ShellWindow fires `ImportCompleted` → refreshes library → re-calls `SetLibraryShows()` to update ownership indicators

### File Changes

**All changes below shipped in `451a0d7`.** The "Status" column reflects the pre-implementation plan.

| File | Status | Change |
|------|--------|--------|
| [Views/ConcertDatabaseView.xaml](Views/ConcertDatabaseView.xaml) | **Modified** | Add `LoadingRow` event handler for owned/missing row coloring |
| [Views/ConcertDatabaseView.xaml.cs](Views/ConcertDatabaseView.xaml.cs) | **Modified** | Add `SetLibraryShows()`, extend `ApplyFilter()` for ownership filter, add right-click context menu, pass library shows on double-click |
| [Views/ConcertDetailView.xaml](Views/ConcertDetailView.xaml) | **Modified** | Add "Import Recording…" button / "Recordings" section to left panel |
| [Views/ConcertDetailView.xaml.cs](Views/ConcertDetailView.xaml.cs) | **Modified** | Accept optional `List<LibraryShow>`, add click handlers for import and library navigation |
| [ShellWindow.xaml.cs](ShellWindow.xaml.cs) | **Modified** | Wire `SetLibraryShows()` after library load and after import; update `NavigateToConcertDetail()` to pass library shows; add `ImportForConcertDate()` |
| [Views/HeaderBar.xaml](Views/HeaderBar.xaml) | **Modified** | Add ownership filter ComboBox to concerts header |
| [Views/HeaderBar.xaml.cs](Views/HeaderBar.xaml.cs) | **Modified** | Add filter change event handling, expose filter state |
| [Views/ImportView.xaml.cs](Views/ImportView.xaml.cs) | **Modified** | Add `ImportForConcert()` method to pre-populate date/venue/city/state; folder parsing overrides pre-populated values |
| [Views/LibraryGridView.xaml.cs](Views/LibraryGridView.xaml.cs) | **Modified** | Add public `Shows` property for ShellWindow to access library shows |

**No new files required.** All changes are modifications to existing files.

### Data Structures

**New/modified on ConcertDatabaseView:**
```csharp
private Dictionary<string, List<LibraryShow>> _libraryShowsByDate = new();
private string _ownershipFilter = "All"; // "All", "Owned", "Missing"

public void SetLibraryShows(Dictionary<string, List<LibraryShow>> showsByDate)
// Populates both _libraryDates and _libraryShowsByDate, triggers visual refresh

public void SetOwnershipFilter(string filter)
// Sets _ownershipFilter and re-applies filter
```

**New on ImportView:**
```csharp
public void ImportForConcert(string folderPath, string date, string venue, string cityState)
// Clears previous state, stores pre-populated values, loads folder.
// After LoadFolderAsync fills _albumInfo from tags/folder, any EMPTY fields
// are filled from the stored pre-populated values. Folder parsing wins.
```

**New on ShellWindow:**
```csharp
public void ImportForConcertDate(ConcertReference concert)
// Opens folder picker, then navigates to ImportView with pre-populated metadata

public void NavigateToConcertDetail(ConcertReference concert, List<LibraryShow>? libraryShows = null)
// Passes library shows to ConcertDetailView constructor
```

## 7. Edge Cases

| Scenario | Handling |
|----------|----------|
| **Date in library but not in ConcertLookupService** | Not applicable — this feature only adds indicators to existing ConcertDatabaseView rows. Dates in the library but not in the concert database are unaffected (they appear in LibraryGridView as always). |
| **Near-miss date (off by one day)** | Strict match only. No fuzzy date matching. The yyyy-MM-dd format is deterministic; if a recording has the wrong date, the user should fix the metadata. |
| **Click-to-import on already-owned date** | Available via right-click context menu on every row (owned and missing). Users may want to add a second recording (e.g., audience + official). No import button shown in ConcertDetailView for owned dates. |
| **Folder picker cancelled** | No-op, stay on current view. |
| **Selected folder contains no audio files** | Handled by existing ImportView logic — it shows an error message when `LoadFolderAsync()` finds no FLAC/MP3 files. No new error handling needed. |
| **Multi-show dates (Early/Late)** | All entries for a date show as owned if any recording for that date exists. See Section 3 rationale. |
| **Library refreshes after import** | ShellWindow's existing `ImportCompleted` event handler calls `_libraryView?.ReloadLibrary()`. Extended to also re-call `SetLibraryShows()` on `_concertsView` so the grid updates without requiring navigation away and back. |
| **ConcertDatabaseView opened before library finishes loading** | `_libraryDates` starts empty → all rows show as "missing." Once library loads, ShellWindow calls `SetLibraryShows()` and the grid re-renders via `Items.Refresh()`. |
| **`ContainsDates` lazy-load performance** | Do NOT trigger `ContainsDates` lazy load for this feature. Only use `show.Date` (always available). Same guard pattern as `BuildMissingShowRows()` at [LibraryGridView.xaml.cs:546-548](Views/LibraryGridView.xaml.cs#L546-L548). |

## 8. Design Decisions Resolved

| # | Question | Decision | Rationale |
|---|----------|----------|-----------|
| 1 | Row color for owned | `#4EC9B0` (teal) | Distinct from cyan set headers; visible on dark background |
| 2 | Multi-show ownership granularity | Per-date | `_libraryDates` is date-only; venue matching is fragile |
| 3 | Double-click routing | Always ConcertDetailView | Consistent UX; preserves setlist access for all rows |
| 4 | Import button on owned dates | Right-click only | Keeps ConcertDetailView focused on library navigation for owned dates |
| 5 | Filter persistence | Session-only | Resets to "All" on navigation away; simpler, no settings change |
| 6 | Pre-populated import behavior | Folder parsing wins | Concert database values are initial suggestions; folder tags/name override |
| 7 | Library date refresh timing | Startup + after import | Ensures grid is always current without manual refresh |

## 9. Out of Scope / Future Work

- **Releases view owned/missing indicators** — requires MBID foundation per [releases-inspection-2026-04-17.md](releases-inspection-2026-04-17.md) Section 6
- **Cross-date search** ("show me all Terrapin Station performances I own") — existing Advanced Search covers song search; ownership filtering is a separate feature
- **Bulk import** (select multiple missing dates, import in batch)
- **Archive.org integration** (auto-download missing shows)
- **Unifying ShowLookupService and ConcertLookupService** — two overlapping concert data systems (noted in inspection report Section 6)
