# Feature Documentation: Release Selector Dialog

## Purpose

The **Release Selector Dialog** presents users with multiple MusicBrainz release options when importing official albums, allowing selection of the specific edition/release to import. This dialog handles the common scenario where an album has multiple releases (original pressing, remaster, deluxe edition, international variants) and enables users to choose the correct metadata for their physical copy. The dialog displays key identifying information (year, label, country, format) to help users distinguish between releases.

## How It's Opened

**Trigger:** MusicBrainz fingerprint lookup returns multiple matching releases for an audio file

**Caller:** [MainWindow.xaml.cs:~L550](MainWindow.xaml.cs) (in MusicBrainz fingerprint workflow)
```csharp
// After MusicBrainzService.GetAllReleasesAsync() returns multiple releases
var releaseOptions = musicBrainzReleases.Select(r => new ReleaseOption
{
    Title = r.Title,
    Artist = r.Artist,
    Year = r.ReleaseYear,
    Label = r.Label,
    Country = r.Country,
    Format = r.Format,
    ReleaseId = r.MusicBrainzId,
    ArtworkUrl = r.ArtworkUrl
}).ToList();

var releaseSelectorDialog = new ReleaseSelectorDialog(albumInfoText, releaseOptions);
releaseSelectorDialog.Owner = this;

if (releaseSelectorDialog.ShowDialog() == true)
{
    var selectedRelease = releaseSelectorDialog.SelectedRelease;
    // Use selectedRelease.ReleaseId to fetch full metadata
}
```

**Parameters:**
- `string albumInfo` - Summary text describing the album (e.g., "American Beauty by Grateful Dead")
- `List<ReleaseOption> releases` - List of release candidates from MusicBrainz

**Modal Behavior:** Yes (ShowDialog blocks until user selects or cancels)

**Return Value:** `DialogResult` (true if selected, false if cancelled) + `SelectedRelease` property

---

## Screen Layout

### Visual Structure
A centered modal dialog (700x500px, resizable) with **dark theme (#1E1E1E background, white text)**:

- **Header:** "Multiple releases found. Please select one:" (16pt bold, white)
- **Album Info:** Summary text describing the album (14pt, gray #CCCCCC)
- **Releases Grid:** DataGrid with 6 columns:
  - Artist (150px fixed)
  - Album Title (* width, fills remaining space)
  - Year (80px fixed)
  - Label (140px fixed)
  - Country (90px fixed)
  - Format (90px fixed)
- **Footer Buttons:**
  - "Select" button (default, Enter key)
  - "Cancel" button (cancel, Escape key)

### Dark Theme Styling
- Background: #1E1E1E (dark gray)
- Foreground: White
- DataGrid background: #2D2D30 (slightly lighter gray)
- Alternating rows: #252526 (even darker for contrast)
- Column headers: #3E3E42 (medium gray) with white text
- Selected row: #094771 (blue) with white text
- Grid lines: Horizontal only, #555555 (medium gray borders)
- Cell padding: 8px horizontal, 5px vertical

### Visual Hierarchy
- Album info text shown above grid (establishes context)
- Grid takes most space (main content)
- Buttons right-aligned (standard dialog footer layout)

---

## Interactive Elements

| Element | Type | Action | Event Handler | Status |
|---------|------|--------|---------------|--------|
| AlbumInfoTextBlock | TextBlock | Display album summary | None (display only) | WORKING |
| ReleasesDataGrid | DataGrid | Display release options | None (selection UI) | WORKING |
| ReleasesDataGrid | DataGrid | Double-click to select | `ReleasesDataGrid_MouseDoubleClick` | WORKING |
| SelectButton | Button | Accept selection and close | `SelectButton_Click` | WORKING |
| CancelButton | Button | Cancel and close | `CancelButton_Click` | WORKING |

**Total Interactive Elements:** 5 (1 grid, 2 buttons, 2 actions)

---

## User Workflows

### Workflow 1: Select Release with Mouse

**Goal:** Choose specific release edition from multiple options.

**Steps:**
1. User imports album folder with multiple MusicBrainz matches
2. Dialog opens showing release options:
   ```
   Multiple releases found. Please select one:
   American Beauty by Grateful Dead

   [Grid showing 5 releases:]
   Artist          | Album Title      | Year | Label            | Country | Format
   ----------------|------------------|------|------------------|---------|--------
   Grateful Dead   | American Beauty  | 1970 | Warner Bros.     | US      | CD
   Grateful Dead   | American Beauty  | 1971 | Warner Bros.     | UK      | Vinyl
   Grateful Dead   | American Beauty  | 2003 | Rhino            | US      | CD
   Grateful Dead   | American Beauty  | 2020 | Rhino/Dead.net   | US      | CD (Deluxe)
   Grateful Dead   | American Beauty  | 2020 | Rhino            | EU      | Vinyl
   ```
3. First row auto-selected on open (line 18-21):
   ```csharp
   if (releases.Count > 0)
   {
       ReleasesDataGrid.SelectedIndex = 0;
   }
   ```
4. User clicks desired row (e.g., 2003 Rhino remaster)
5. Row highlights blue (#094771)
6. User clicks "Select" button (or presses Enter)
7. `SelectButton_Click` validates selection (line 24-35):
   ```csharp
   if (ReleasesDataGrid.SelectedItem is ReleaseOption selected)
   {
       SelectedRelease = selected;
       DialogResult = true;
       Close();
   }
   ```
8. Dialog closes, MainWindow receives `SelectedRelease` with metadata

**Success Path:** Selected release returned via `SelectedRelease` property, `DialogResult = true`.

**Data Used:** `ReleaseOption.ReleaseId` (MusicBrainz release ID) used to fetch full album metadata.

---

### Workflow 2: Select Release with Double-Click

**Goal:** Quick selection without clicking "Select" button.

**Steps:**
1. Dialog opens with releases grid
2. User double-clicks desired row
3. `ReleasesDataGrid_MouseDoubleClick` handler executes (line 44-52):
   ```csharp
   if (ReleasesDataGrid.SelectedItem is ReleaseOption selected)
   {
       SelectedRelease = selected;
       DialogResult = true;
       Close();
   }
   ```
4. Dialog closes immediately with selection

**Success Path:** Same as Workflow 1, but one less click (no "Select" button required).

**UX Enhancement:** Double-click provides power-user shortcut.

---

### Workflow 3: Cancel Selection

**Goal:** Abort release selection, return to manual metadata entry.

**Steps:**
1. Dialog opens with releases grid
2. User realizes none of the options are correct
3. User clicks "Cancel" button (or presses Escape)
4. `CancelButton_Click` executes (line 38-42):
   ```csharp
   DialogResult = false;
   Close();
   ```
5. Dialog closes, MainWindow receives `DialogResult = false`
6. MainWindow falls back to manual metadata entry (no MusicBrainz data used)

**Success Path:** User can manually enter metadata if MusicBrainz options are wrong.

---

### Workflow 4: No Selection Made

**Goal:** User clicks "Select" without selecting a row (edge case).

**Steps:**
1. Dialog opens (first row auto-selected normally)
2. User clicks in empty space below grid (deselects row)
3. User clicks "Select" button
4. Validation fails (line 26):
   ```csharp
   if (ReleasesDataGrid.SelectedItem is ReleaseOption selected)
   ```
5. Else branch executes (line 32-35):
   ```csharp
   System.Windows.MessageBox.Show("Please select a release.", "No Selection",
       MessageBoxButton.OK, MessageBoxImage.Warning);
   ```
6. Warning message shown, dialog remains open
7. User must select row or cancel

**Success Path:** Validation prevents closing without selection.

**Edge Case:** Rare scenario (first row auto-selected), but handled gracefully.

---

## Data Flow

### Input Sources

**Parameters:**
1. `string albumInfo` - Album summary text (line 14)
   - Example: "American Beauty by Grateful Dead"
   - Displayed in `AlbumInfoTextBlock` (line 14)

2. `List<ReleaseOption> releases` - Release candidates (line 15)
   - Bound to `ReleasesDataGrid.ItemsSource` (line 15)
   - DataGrid columns auto-bind to ReleaseOption properties

**ReleaseOption Properties:**
```csharp
public class ReleaseOption
{
    public string Title { get; set; }          // Album title
    public string? Year { get; set; }          // Release year
    public string? Label { get; set; }         // Record label
    public string? Country { get; set; }       // Release country
    public string? Format { get; set; }        // CD, Vinyl, Digital, etc.
    public string ReleaseId { get; set; }      // MusicBrainz release ID (GUID)
    public string? ArtworkUrl { get; set; }    // Album artwork URL (not displayed)
    public string Artist { get; set; }         // Artist name
}
```

### Processing Logic

**Auto-Selection (line 18-21):**
```csharp
if (releases.Count > 0)
{
    ReleasesDataGrid.SelectedIndex = 0;
}
```

**Why First Row?** Provides default selection so user can just press Enter if first option is correct.

**Selection Validation (line 24-35):**
```csharp
if (ReleasesDataGrid.SelectedItem is ReleaseOption selected)
{
    SelectedRelease = selected;
    DialogResult = true;
    Close();
}
else
{
    MessageBox.Show("Please select a release.", "No Selection",
        MessageBoxButton.OK, MessageBoxImage.Warning);
}
```

**Pattern Matching:** `is ReleaseOption selected` combines type check + assignment.

### Output Data

**Return Value:** `bool? DialogResult` (true = selected, false = cancelled)

**Output Property:** `ReleaseOption? SelectedRelease` (line 8)
- Null if cancelled
- Populated with selected release if DialogResult = true

**Caller Usage:**
```csharp
if (releaseSelectorDialog.ShowDialog() == true)
{
    var releaseId = releaseSelectorDialog.SelectedRelease.ReleaseId;
    // Fetch full metadata using releaseId
}
```

---

## Business Rules

### 1. Auto-Select First Row
- **Rule:** First row selected by default when dialog opens (line 18-21)
- **Rationale:** Provides default selection, user can press Enter immediately if correct
- **Override:** User can select different row before clicking "Select"

### 2. Single Selection Only
- **Rule:** DataGrid configured with `SelectionMode="Single"` (line 33)
- **Behavior:** User can only select one release at a time
- **Rationale:** Import operation requires exactly one release selection

### 3. Validation Required
- **Rule:** "Select" button validates that a row is selected before closing (line 26)
- **Warning:** "Please select a release." if no selection
- **Rationale:** Prevent accidental empty selection

### 4. Double-Click Shortcut
- **Rule:** Double-clicking row is equivalent to select + "Select" button (line 44-52)
- **Behavior:** Immediate dialog close with selection
- **Rationale:** Power-user efficiency (common UI pattern)

### 5. Dark Theme Required
- **Rule:** Dialog uses dark theme (#1E1E1E background) (line 6-7)
- **Rationale:** Matches MainWindow dark theme, consistent visual style
- **Implication:** Text must be white/light for readability

### 6. Read-Only Grid
- **Rule:** DataGrid configured as read-only (line 29-31):
  - `CanUserAddRows="False"`
  - `CanUserDeleteRows="False"`
  - `IsReadOnly="True"` on all columns
- **Rationale:** Selection-only UI, no editing allowed

### 7. No Empty Release List
- **Rule:** Caller should only show dialog if `releases.Count >= 2`
- **Single Release:** MusicBrainz returns 1 match → Auto-select without showing dialog
- **Zero Releases:** MusicBrainz returns no matches → Skip dialog, use manual entry
- **Rationale:** Dialog only useful when user needs to disambiguate

---

## Validation Rules

### Selection Validation
- **Rule:** At least one row must be selected before "Select" button succeeds (line 26)
- **Error Message:** "Please select a release." (warning icon)
- **Behavior:** Dialog remains open until valid selection or cancel

**No Other Validation:** All release data comes from MusicBrainz (trusted source), no need to validate individual fields.

---

## Known Issues

### 1. Dialog Never Triggered (MAJOR ISSUE)
**Problem:** According to CLAUDE.md (line 260-273), this dialog is **implemented but never appears** during official release import.

**Possible Causes:**
1. **fpcalc.exe missing:** Audio fingerprinting fails silently, MusicBrainz lookup returns zero releases
2. **MusicBrainz API returns 1 release:** Code auto-selects without showing dialog
3. **MusicBrainz service not initialized:** Lookup skipped entirely

**Expected Behavior:**
- 0 releases → Error message
- 1 release → Auto-select (no dialog)
- 2+ releases → Show ReleaseSelectorDialog

**Current Behavior:** Unknown - dialog never reached in testing.

**Status:** DEAD CODE (implemented but not triggered in current workflow).

---

### 2. No Release Details Visible
**Problem:** Grid shows basic info (year, label, country, format), but no track listing or full description.

**Impact:** User may struggle to identify correct release if multiple editions have same year/label.

**Example:** "American Beauty (2003 Remaster)" vs "American Beauty (2003 HDCD)" - both show "2003, Rhino, US, CD".

**Workaround:** User must recognize correct release by format/country alone.

**Status:** LIMITATION - Grid columns limited to ReleaseOption properties.

---

### 3. No Artwork Preview
**Problem:** `ReleaseOption.ArtworkUrl` exists (line 63) but not displayed in grid.

**Impact:** Visual identification not available (artwork often helps distinguish editions).

**Expected:** Column showing album artwork thumbnail.

**Status:** MISSING FEATURE - Artwork URL available but unused.

---

### 4. No Sorting
**Problem:** Grid has no sortable columns (ColumnHeaderStyle doesn't enable sorting).

**Impact:** User cannot sort by year, label, or country to find desired release.

**Workaround:** Releases shown in order returned by MusicBrainz (usually chronological).

**Status:** LIMITATION - No column sorting enabled.

---

### 5. No Search/Filter
**Problem:** Large lists (e.g., 20+ releases for popular album) cannot be filtered.

**Impact:** User must scroll through entire list to find specific edition.

**Expected:** Search box to filter by year or label (similar to song search in other dialogs).

**Status:** LIMITATION - No filtering for large release lists.

---

## Edge Cases

### 1. Empty Release List
**Scenario:** Caller passes empty `List<ReleaseOption>` (count = 0).

**Behavior:**
- Dialog opens normally
- Album info shown
- DataGrid empty (no rows)
- Auto-selection check fails (line 18): `if (releases.Count > 0)` → false
- No row selected by default
- Clicking "Select" shows warning: "Please select a release."
- User must cancel (cannot select from empty list)

**Handling:** Dialog technically works but is useless with empty list.

**User Impact:** User stuck in dialog, must cancel (wasted interaction).

**Better Approach:** Caller should validate `releases.Count > 0` before showing dialog.

---

### 2. Single Release
**Scenario:** Caller passes list with exactly 1 release.

**Behavior:**
- Dialog opens
- First (only) row auto-selected
- User can press Enter immediately (selects the one release)
- Or user can cancel

**Handling:** Works correctly but unnecessary (caller should auto-select 1 release without showing dialog).

**User Impact:** Extra click required for obvious choice.

**Better Approach:** Caller should only show dialog if `releases.Count >= 2`.

---

### 3. Very Long Album Title
**Scenario:** Release has 100-character album title.

**Behavior:**
- "Album Title" column has `Width="*"` (fills remaining space) (line 44)
- Title wraps or truncates depending on DataGrid cell settings
- Likely truncates (no `TextWrapping` in cell style)

**Handling:** Long titles may be cut off.

**User Impact:** User cannot see full title, must infer from partial text.

---

### 4. Missing Year/Label/Country
**Scenario:** MusicBrainz data has null values for Year, Label, or Country.

**Behavior:**
- Properties are nullable: `string?` (line 58-60)
- DataGrid binding displays empty cell for null values
- No "N/A" or placeholder text shown

**Handling:** Empty cells acceptable for missing data.

**User Impact:** Some releases may have incomplete information (hard to distinguish).

---

### 5. Duplicate Releases (Identical Metadata)
**Scenario:** Two releases with identical Year, Label, Country, Format (e.g., multiple pressings).

**Behavior:**
- Both shown in grid with identical display text
- Different `ReleaseId` (GUID) distinguishes them internally
- User cannot see ReleaseId in grid

**Handling:** No visual distinction between duplicates.

**User Impact:** User cannot tell which release is which (must guess).

**Better Approach:** Show ReleaseId or additional distinguishing info.

---

### 6. Non-English Characters in Metadata
**Scenario:** Release from Japan with Japanese characters in label or format.

**Behavior:**
- WPF DataGrid supports full Unicode
- Text renders correctly if font includes glyphs

**Handling:** Full Unicode support.

**User Impact:** No issues with international characters.

---

### 7. Click Select Without Changing Default
**Scenario:** Dialog opens, first row auto-selected, user immediately clicks "Select".

**Behavior:**
- First row returned as selection
- DialogResult = true
- Normal success path

**Handling:** Default selection designed for this workflow.

**User Impact:** Fast selection if first option is correct (common case).

---

### 8. Close Dialog with Window X Button
**Scenario:** User clicks X button in title bar instead of "Cancel".

**Behavior:**
- Default WPF behavior: `DialogResult` remains null
- Caller receives null from `ShowDialog()`
- Treated as cancel (no selection)

**Handling:** X button equivalent to "Cancel" (standard WPF behavior).

**User Impact:** No issues, expected behavior.

---

### 9. Keyboard Navigation
**Scenario:** User uses arrow keys to navigate grid, Enter to select.

**Behavior:**
- Arrow keys move selection up/down (standard DataGrid behavior)
- Enter key triggers "Select" button (IsDefault="True") (line 76)
- Escape key triggers "Cancel" button (IsCancel="True") (line 77)

**Handling:** Full keyboard support.

**User Impact:** Efficient keyboard-only workflow available.

---

### 10. Alternating Row Colors with Odd Count
**Scenario:** Grid has odd number of rows (e.g., 5 releases).

**Behavior:**
- Rows alternate between #2D2D30 and #252526 (line 38-39)
- Last row uses `RowBackground` color (#2D2D30)

**Handling:** Standard alternating pattern.

**User Impact:** Visual contrast aids readability, works correctly.

---

## Dependencies

### Data Models
- **ReleaseOption** - Release metadata structure (line 55-65)
  - Properties: Title, Year, Label, Country, Format, ReleaseId, ArtworkUrl, Artist

### Parent Window
- **MainWindow** - Calls dialog during MusicBrainz import workflow

### External APIs
- **MusicBrainz API** - Provides release data (not called directly from dialog, data passed in)

### Services
- **MusicBrainzService** - Fetches release data before dialog opened (caller responsibility)

---

## File Paths

**XAML:** [ReleaseSelectorDialog.xaml](ReleaseSelectorDialog.xaml)
**Code-Behind:** [ReleaseSelectorDialog.xaml.cs](ReleaseSelectorDialog.xaml.cs)

**Lines of Code:** 81 XAML, 67 C# (148 total)

---

## Related Documentation

- [00-ui-audit.md](documentation/00-ui-audit.md) - Complete UI audit
- [01-main-window.md](documentation/01-main-window.md) - Parent window that triggers this dialog
- [08-album-search-dialog.md](documentation/08-album-search-dialog.md) - Manual MusicBrainz search (alternative to fingerprint)
- **CLAUDE.md** - Known issue documented (dialog never triggers, line 260-273)

---

**Last Updated:** 2026-03-01
**Status:** Complete feature documentation (NOTE: Dialog implemented but not currently triggered in workflow)
