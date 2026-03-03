# Feature Documentation: Album Search Dialog

## Purpose

The **Album Search Dialog** provides manual MusicBrainz search capabilities when automatic audio fingerprinting fails or is unavailable. Users enter album name and artist (required fields) plus optional year (to narrow results), then the dialog returns these search parameters to the caller. This dialog is a lightweight input form that collects search criteria - the actual MusicBrainz API call happens in the MainWindow after the dialog closes. It includes smart focus management to minimize user clicks.

## How It's Opened

**Trigger:** User clicks "Manual Search" button in MainWindow when MusicBrainz fingerprint lookup fails or fpcalc.exe is missing

**Caller:** [MainWindow.xaml.cs:~L600](MainWindow.xaml.cs) (in MusicBrainz import workflow)
```csharp
// When fingerprint lookup fails or user requests manual search
var searchDialog = new AlbumSearchDialog(
    initialAlbumName: folderName,  // Pre-fill from folder name
    initialArtist: detectedArtist   // Pre-fill from ID3 tags if available
);
searchDialog.Owner = this;

if (searchDialog.ShowDialog() == true)
{
    var albumName = searchDialog.AlbumName;
    var artist = searchDialog.Artist;
    var year = searchDialog.Year;

    // Call MusicBrainzService.SearchAlbumAsync(albumName, artist, year)
    // Then show ReleaseSelectorDialog with results
}
```

**Parameters:**
- `string initialAlbumName` (optional) - Pre-fill album name field (default: empty)
- `string initialArtist` (optional) - Pre-fill artist field (default: empty)

**Modal Behavior:** Yes (ShowDialog blocks until user searches or cancels)

**Return Value:** `DialogResult` (true if search clicked, false if cancelled) + properties: `AlbumName`, `Artist`, `Year`

---

## Screen Layout

### Visual Structure
A centered modal dialog (600x350px, resizable with minimum 500x300) with **dark theme (#1E1E1E background, white text)**:

**Vertical Stack Layout:**

1. **Instructions:** "Search MusicBrainz for album releases:" (14pt, gray #CCCCCC)

2. **Album Name Section:**
   - Label: "Album Name:" (white, 13pt)
   - TextBox (35px height, dark #2D2D30 background, white text)

3. **Artist Section:**
   - Label: "Artist:" (white, 13pt)
   - TextBox (35px height, dark #2D2D30 background, white text)

4. **Year Section:**
   - Label: "Year (optional - helps narrow results):" (white, 13pt)
   - TextBox (35px height, 4-character max, dark #2D2D30 background, white text)
   - Tooltip: "e.g., 2003"

5. **Footer Buttons (right-aligned):**
   - "Search" button (blue #0E639C, white text, default button)
   - "Cancel" button (gray #3E3E42, white text, cancel button)

### Dark Theme Styling
- Window background: #1E1E1E (dark gray)
- Text foreground: White
- TextBox background: #2D2D30 (slightly lighter dark gray)
- TextBox border: #555555 (medium gray)
- TextBox padding: 8px horizontal, 6px vertical
- Button colors: Blue for primary action, gray for cancel
- No borders on buttons (BorderThickness="0")

### Spacing
- 20px margin around entire content
- 20px margin below each section
- 25px margin below year field (larger spacing before buttons)
- 8px margin between field labels and textboxes

---

## Interactive Elements

| Element | Type | Action | Event Handler | Status |
|---------|------|--------|---------------|--------|
| AlbumNameTextBox | TextBox | Enter album name | None (input only) | WORKING |
| ArtistTextBox | TextBox | Enter artist name | None (input only) | WORKING |
| YearTextBox | TextBox | Enter optional year (4 chars max) | None (input only) | WORKING |
| SearchButton | Button | Validate and return search criteria | `SearchButton_Click` | WORKING |
| CancelButton | Button | Cancel and close | `CancelButton_Click` | WORKING |

**Total Interactive Elements:** 5 (3 textboxes, 2 buttons)

---

## User Workflows

### Workflow 1: Manual Search with Pre-Filled Data

**Goal:** Search for album when MainWindow detects album name/artist from folder/tags.

**Steps:**
1. User imports album folder named "American Beauty (1970)"
2. Fingerprint lookup fails (fpcalc.exe missing)
3. MainWindow offers manual search option
4. User clicks "Manual Search" button
5. Dialog opens with pre-filled fields (line 11-16):
   ```csharp
   AlbumSearchDialog(initialAlbumName: "American Beauty", initialArtist: "Grateful Dead")
   ```
   - AlbumNameTextBox.Text = "American Beauty"
   - ArtistTextBox.Text = "Grateful Dead"
6. Focus management logic executes (line 18-30):
   - Album name NOT empty → Skip (line 19-22)
   - Artist NOT empty → Skip (line 23-26)
   - Both filled → Focus year field (line 29)
7. User types "2003" in year field (cursor already there)
8. User presses Enter (Search button is default)
9. `SearchButton_Click` validates (line 33-51):
   ```csharp
   var albumName = AlbumNameTextBox.Text.Trim();
   var artist = ArtistTextBox.Text.Trim();

   if (string.IsNullOrWhiteSpace(albumName) || string.IsNullOrWhiteSpace(artist))
   {
       MessageBox.Show("Please enter both Album Name and Artist.",
           "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
       return;
   }

   AlbumName = albumName;
   Artist = artist;
   Year = string.IsNullOrWhiteSpace(YearTextBox.Text) ? null : YearTextBox.Text.Trim();

   DialogResult = true;
   Close();
   ```
10. Dialog closes with search parameters
11. MainWindow calls `MusicBrainzService.SearchAlbumAsync("American Beauty", "Grateful Dead", "2003")`
12. Results shown in ReleaseSelectorDialog

**Success Path:** Search criteria returned, MusicBrainz search executed.

**Smart Focus:** Year field auto-focused when both album/artist pre-filled (minimizes user interaction).

---

### Workflow 2: Manual Search with Empty Fields

**Goal:** Enter all search criteria manually.

**Steps:**
1. User imports album folder with unrecognized name
2. MainWindow detects no album/artist info
3. Dialog opens with all fields empty
4. Focus logic (line 18-30):
   - Album name empty → `AlbumNameTextBox.Focus()` (line 21)
5. User types album name, presses Tab
6. User types artist name, presses Tab
7. User types year (optional), presses Enter
8. Validation passes, dialog closes with criteria

**Success Path:** All fields entered manually, search executed.

**Smart Focus:** Album name field auto-focused when empty (first field to fill).

---

### Workflow 3: Search Without Year

**Goal:** Search with only album name and artist (skip optional year).

**Steps:**
1. Dialog opens with pre-filled or manual entry
2. User fills album name and artist
3. User skips year field (leaves empty)
4. User clicks "Search"
5. Year property set to null (line 47):
   ```csharp
   Year = string.IsNullOrWhiteSpace(YearTextBox.Text) ? null : YearTextBox.Text.Trim();
   ```
6. Dialog closes, MainWindow searches without year filter

**Success Path:** MusicBrainz search with broad criteria (no year restriction).

**Use Case:** User doesn't know release year, wants to see all releases.

---

### Workflow 4: Validation Failure

**Goal:** User tries to search with missing required fields.

**Steps:**
1. Dialog opens
2. User enters album name only (leaves artist empty)
3. User clicks "Search" or presses Enter
4. Validation fails (line 38):
   ```csharp
   if (string.IsNullOrWhiteSpace(albumName) || string.IsNullOrWhiteSpace(artist))
   ```
5. Warning message shown (line 40-42):
   ```
   Please enter both Album Name and Artist.
   ```
   - MessageBoxButton.OK
   - MessageBoxImage.Warning
6. Dialog remains open
7. User must fill missing field or cancel

**Success Path:** Validation prevents incomplete search, user corrects input.

**Required Fields:** Both album name AND artist must be non-empty.

---

### Workflow 5: Cancel Search

**Goal:** Abort manual search, return to automatic fingerprint attempt or skip MusicBrainz.

**Steps:**
1. Dialog opens
2. User decides not to search (e.g., wants to manually enter metadata instead)
3. User clicks "Cancel" or presses Escape
4. `CancelButton_Click` executes (line 53-57):
   ```csharp
   DialogResult = false;
   Close();
   ```
5. Dialog closes, MainWindow receives `DialogResult = false`
6. MainWindow skips MusicBrainz search, allows manual metadata entry

**Success Path:** User opts out of MusicBrainz, enters metadata manually.

---

## Data Flow

### Input Sources

**Constructor Parameters (line 11-31):**
- `string initialAlbumName` (default: empty) - Pre-fill album name
- `string initialArtist` (default: empty) - Pre-fill artist name

**User Input:**
- Album name (required)
- Artist (required)
- Year (optional, max 4 characters)

### Processing Logic

#### Initialization (line 11-31)

**Field Pre-Fill:**
```csharp
AlbumNameTextBox.Text = initialAlbumName;
ArtistTextBox.Text = initialArtist;
```

**Smart Focus Logic:**
```csharp
if (string.IsNullOrWhiteSpace(initialAlbumName))
{
    AlbumNameTextBox.Focus();  // First empty field
}
else if (string.IsNullOrWhiteSpace(initialArtist))
{
    ArtistTextBox.Focus();  // Second empty field
}
else
{
    YearTextBox.Focus();  // Both filled, focus optional field
}
```

**Focus Priority:**
1. Album name (if empty)
2. Artist (if album filled but artist empty)
3. Year (if both album and artist filled)

**Rationale:** Minimize user clicks by focusing first field that needs data.

#### Validation (line 35-43)

**Trim and Check:**
```csharp
var albumName = AlbumNameTextBox.Text.Trim();
var artist = ArtistTextBox.Text.Trim();

if (string.IsNullOrWhiteSpace(albumName) || string.IsNullOrWhiteSpace(artist))
{
    MessageBox.Show("Please enter both Album Name and Artist.",
        "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
    return;  // Keep dialog open
}
```

**Trimming:** Removes leading/trailing spaces before validation.

**Whitespace Check:** `IsNullOrWhiteSpace()` rejects empty strings and space-only strings.

#### Year Handling (line 47)

**Null for Empty:**
```csharp
Year = string.IsNullOrWhiteSpace(YearTextBox.Text) ? null : YearTextBox.Text.Trim();
```

**Result:**
- Empty field → `null` (tells MusicBrainz to ignore year filter)
- Non-empty → Trimmed string (e.g., "2003")

### Output Data

**Return Value:** `bool? DialogResult` (true = search, false = cancel)

**Output Properties:**
```csharp
public string AlbumName { get; private set; } = "";  // Trimmed album name
public string Artist { get; private set; } = "";      // Trimmed artist name
public string? Year { get; private set; }             // Trimmed year or null
```

**Caller Usage:**
```csharp
if (searchDialog.ShowDialog() == true)
{
    var results = await _musicBrainzService.SearchAlbumAsync(
        searchDialog.AlbumName,
        searchDialog.Artist,
        searchDialog.Year
    );

    // Show ReleaseSelectorDialog with results
}
```

**No Direct API Call:** Dialog only collects input, does NOT call MusicBrainz itself.

---

## Business Rules

### 1. Required Fields
- **Rule:** Album name and artist are required (line 38)
- **Validation:** Both must be non-empty and non-whitespace
- **Error Message:** "Please enter both Album Name and Artist."

### 2. Optional Year
- **Rule:** Year field is optional (line 37 label explicitly states this)
- **Behavior:** Empty year field results in `null` property (line 47)
- **Rationale:** User may not know release year, or may want all years

### 3. Year Max Length
- **Rule:** Year field limited to 4 characters (line 38 XAML: `MaxLength="4"`)
- **Rationale:** Year format is yyyy (e.g., "2003")
- **Enforcement:** TextBox prevents typing more than 4 characters

### 4. Smart Focus Management
- **Rule:** Dialog auto-focuses first empty field on open (line 18-30)
- **Priority:** Album name → Artist → Year
- **Rationale:** Minimize user clicks, cursor ready for input

### 5. Trimming All Inputs
- **Rule:** All inputs trimmed before validation and property assignment (line 35-36, 47)
- **Behavior:** Leading/trailing spaces removed
- **Rationale:** Prevent accidental whitespace from user copy/paste

### 6. Search Button as Default
- **Rule:** Search button has `IsDefault="True"` (line 45)
- **Behavior:** Enter key anywhere in dialog triggers search
- **Rationale:** Fast keyboard workflow (type → Enter → done)

### 7. Cancel Button as Cancel
- **Rule:** Cancel button has `IsCancel="True"` (line 48)
- **Behavior:** Escape key closes dialog without searching
- **Rationale:** Standard dialog behavior

### 8. Pre-Fill Support
- **Rule:** Constructor accepts optional initial values (line 11)
- **Behavior:** Fields pre-filled with detected/suggested values
- **Rationale:** Reduce user typing when album/artist can be inferred

### 9. Dark Theme Consistency
- **Rule:** Dialog uses same dark theme as ReleaseSelectorDialog (#1E1E1E) (line 7-8)
- **Rationale:** Visual consistency across MusicBrainz dialogs

---

## Validation Rules

### Album Name Validation
- **Rule:** Must not be empty or whitespace-only (line 38)
- **Trimming:** Leading/trailing spaces removed before check (line 35)
- **Error:** "Please enter both Album Name and Artist."
- **Focus:** Does NOT auto-focus to invalid field (user must manually correct)

### Artist Validation
- **Rule:** Must not be empty or whitespace-only (line 38)
- **Trimming:** Leading/trailing spaces removed before check (line 36)
- **Error:** Same error message as album name (combined check)
- **Focus:** Does NOT auto-focus to invalid field

### Year Validation
- **Rule:** NO validation (optional field accepts any input)
- **Max Length:** 4 characters enforced by TextBox (line 38 XAML)
- **Format:** No format checking (e.g., accepts "abcd", not just "2003")
- **Rationale:** MusicBrainz API handles invalid year gracefully (returns no results)

---

## Known Issues

### 1. Year Format Not Validated
**Problem:** Year field accepts any 4 characters, not just numbers (e.g., "abcd" is valid).

**Impact:** User can enter non-numeric year, MusicBrainz search will fail or return no results.

**Expected:** Numeric-only validation (e.g., regex `^\d{4}$`).

**Workaround:** User responsible for entering valid 4-digit year.

**Status:** NO VALIDATION - Accepts any text up to 4 characters.

---

### 2. No Focus to Invalid Field on Validation Failure
**Problem:** When validation fails, focus does NOT move to empty field.

**Example:**
1. User enters album name only (artist empty)
2. Clicks "Search"
3. Warning shown
4. Dialog remains open with focus unchanged (not on artist field)

**Impact:** User must manually click into artist field.

**Expected:** `ArtistTextBox.Focus()` after validation failure.

**Status:** MINOR UX ISSUE - No auto-focus on error.

---

### 3. No Year Range Validation
**Problem:** Year field accepts any 4-digit number (e.g., "0001", "9999").

**Impact:** User can enter unrealistic years for albums.

**Example:** "1850" (before recorded music existed) or "2099" (future).

**Expected:** Range validation (e.g., 1900-2099).

**Status:** NO VALIDATION - Accepts any 4 characters.

---

### 4. No Character Filtering in Year Field
**Problem:** Year field allows non-numeric characters via paste (TextBox.MaxLength prevents typing but not pasting).

**Example:** User pastes "Year 2003" → Truncated to "Year" (4 chars).

**Impact:** Invalid year submitted to MusicBrainz.

**Expected:** Numeric-only TextBox (e.g., `PreviewTextInput` event handler).

**Status:** NO FILTERING - Paste can bypass intended numeric format.

---

### 5. Tooltip Only on Year Field
**Problem:** Tooltip shown only for year field (line 40), not for album/artist.

**Impact:** No help text for album name or artist format.

**Example:** User doesn't know if artist should be "Grateful Dead" or "The Grateful Dead".

**Expected:** Tooltips with examples for all fields.

**Status:** MINOR - Only year has guidance.

---

## Edge Cases

### 1. Both Fields Pre-Filled
**Scenario:** Caller passes both `initialAlbumName` and `initialArtist`.

**Behavior:**
- Both fields populated (line 15-16)
- Focus moves to year field (line 29)
- User can press Enter immediately to search (if year not needed)

**Handling:** Optimal workflow - minimal user interaction.

**User Impact:** Fast search (just press Enter).

---

### 2. Album Pre-Filled, Artist Empty
**Scenario:** Caller passes album name but no artist.

**Behavior:**
- Album name filled, artist empty
- Focus moves to artist field (line 25)
- User types artist, presses Enter

**Handling:** Cursor ready for user to type missing field.

**User Impact:** Efficient data entry.

---

### 3. No Pre-Fill Data
**Scenario:** Caller passes empty strings for both parameters.

**Behavior:**
- Both fields empty
- Focus moves to album name field (line 21)
- User fills both fields manually

**Handling:** Standard empty-form workflow.

**User Impact:** User types all data (expected for manual search).

---

### 4. Album/Artist with Leading/Trailing Spaces
**Scenario:** User pastes "  American Beauty  " from external source.

**Behavior:**
- Spaces visible in TextBox during entry
- Validation trims before check (line 35)
- Saved value has spaces removed
- MusicBrainz receives "American Beauty" (clean)

**Handling:** Automatic cleanup prevents spacing issues.

**User Impact:** No errors from accidental spaces.

---

### 5. Empty String vs Null Initial Values
**Scenario:** Caller passes `null` for initial parameters (not empty string).

**Behavior:**
- Constructor parameters have default empty string: `= ""` (line 11)
- Null becomes empty string at call site
- No null reference exceptions

**Handling:** Defensive null-to-empty conversion.

**User Impact:** No issues with null parameters.

---

### 6. Year Field with Partial Entry
**Scenario:** User types "20" (partial year) and clicks Search.

**Behavior:**
- Validation ignores year (optional field)
- Year property set to "20" (line 47)
- MusicBrainz receives "20" as year filter

**Impact:** MusicBrainz may interpret "20" as year AD 20 (no results) or reject it.

**Handling:** No validation of year completeness.

**User Impact:** Search may fail with partial year.

---

### 7. Special Characters in Album/Artist
**Scenario:** User enters "C.C. Rider (Live)" or "AC/DC".

**Behavior:**
- No character filtering in textboxes
- Special characters passed to MusicBrainz as-is
- MusicBrainz API handles special chars (URL encoding in service layer)

**Handling:** Full Unicode support.

**User Impact:** Special characters work correctly.

---

### 8. Very Long Album/Artist Names
**Scenario:** User pastes 500-character string into album name field.

**Behavior:**
- No max-length validation (TextBox accepts unlimited text)
- Full string passed to validation (line 35)
- Full string passed to MusicBrainz API

**Impact:** API may reject or truncate excessively long queries.

**Handling:** No length restrictions.

**User Impact:** Possible API errors with extremely long inputs.

---

### 9. Case Sensitivity in Search
**Scenario:** User enters "american beauty" (lowercase) vs "American Beauty".

**Behavior:**
- No case normalization in dialog
- Value passed to MusicBrainz as user typed it
- MusicBrainz API performs case-insensitive search

**Handling:** MusicBrainz handles case, dialog doesn't need to.

**User Impact:** Case doesn't matter for search results.

---

### 10. Enter Key in Year Field
**Scenario:** User types year and presses Enter (without clicking Search button).

**Behavior:**
- Year TextBox does NOT have focus when Enter pressed (SearchButton has `IsDefault="True"`)
- Enter key triggers `SearchButton_Click` (line 45)
- Same as clicking Search button

**Handling:** Default button behavior provides keyboard shortcut.

**User Impact:** Efficient keyboard workflow (no mouse needed).

---

### 11. Tab Order Through Fields
**Scenario:** User presses Tab to navigate fields.

**Behavior:**
- Standard WPF tab order (top to bottom):
  1. AlbumNameTextBox
  2. ArtistTextBox
  3. YearTextBox
  4. SearchButton
  5. CancelButton
- No custom `TabIndex` properties (uses default order)

**Handling:** Logical top-to-bottom, left-to-right tab order.

**User Impact:** Intuitive keyboard navigation.

---

### 12. Cancel After Partial Entry
**Scenario:** User types album name, then clicks Cancel without filling artist.

**Behavior:**
- `CancelButton_Click` sets `DialogResult = false` (line 55)
- Dialog closes immediately (no confirmation)
- Partial data discarded (not saved to properties)

**Handling:** Clean cancellation with no side effects.

**User Impact:** Partial entry lost (expected cancel behavior).

---

## Dependencies

### Parent Window
- **MainWindow** - Calls dialog when manual MusicBrainz search requested

### Related Dialogs
- **ReleaseSelectorDialog** - Shown after MusicBrainz search completes (receives results)

### Services
- **MusicBrainzService** - Called by MainWindow (NOT by this dialog) with returned search criteria

### External APIs
- **MusicBrainz API** - Ultimate destination of search criteria (via MusicBrainzService)

---

## File Paths

**XAML:** [AlbumSearchDialog.xaml](AlbumSearchDialog.xaml)
**Code-Behind:** [AlbumSearchDialog.xaml.cs](AlbumSearchDialog.xaml.cs)

**Lines of Code:** 53 XAML, 60 C# (113 total)

---

## Related Documentation

- [00-ui-audit.md](documentation/00-ui-audit.md) - Complete UI audit
- [01-main-window.md](documentation/01-main-window.md) - Parent window that triggers this dialog
- [07-release-selector-dialog.md](documentation/07-release-selector-dialog.md) - Follows this dialog in workflow (shows search results)

---

**Last Updated:** 2026-03-01
**Status:** Complete feature documentation
