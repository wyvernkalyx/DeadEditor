# Feature Documentation: Add Song Dialog

## Purpose

The **Add Song Dialog** enables users to add new songs to the song database (`Data/songs.json`) without recompiling the application. Users can select an existing artist or create a new artist, enter the canonical song title, and optionally provide aliases (typos, abbreviations, alternative spellings). Songs are saved immediately to disk and become available for fuzzy matching in the import workflow. This dialog supports the artist-agnostic design by allowing any artist to be added, not just Grateful Dead.

## How It's Opened

**Trigger:** User clicks "Add Song..." button in Settings Window

**Caller:** [SettingsWindow.xaml.cs:181-186](SettingsWindow.xaml.cs#L181-L186)
```csharp
private void AddSongButton_Click(object sender, RoutedEventArgs e)
{
    var dialog = new AddSongDialog(_normalizationService);
    dialog.Owner = this;
    dialog.ShowDialog();
}
```

**Parameters:**
- `NormalizationService` - Service for accessing/modifying song database

**Modal Behavior:** Yes (ShowDialog blocks until closed)

---

## Screen Layout

### Visual Structure
A centered modal dialog (550x420px, resizable with minimum 550x420) containing:

- **Header:** "Add New Song" title (16pt bold)
- **Artist Selection Section:**
  - ComboBox (editable) with refresh button (🔄)
  - Hint text explaining "Select existing or type new artist name"
- **Official Song Title Section:**
  - TextBox for canonical song name
  - Helper text: "The canonical/correct title for this song"
- **Aliases Section:**
  - Multi-line TextBox (80px height, accepts returns)
  - Helper text: "Enter alternative spellings or abbreviations, one per line"
- **Status Message Area:**
  - TextBlock showing success/error messages (green/red)
- **Footer Buttons:**
  - "Add Song" (default button, Enter key)
  - "Cancel" (cancel button, Escape key)

### Color Scheme
- Success messages: Green
- Error messages: Red
- Helper text: Gray (#888)
- Hints: Blue (#0066CC) for emphasized portions

---

## Interactive Elements

| Element | Type | Action | Event Handler | API/Service Call | Status |
|---------|------|--------|---------------|------------------|--------|
| ArtistComboBox | ComboBox (editable) | Select existing artist or type new name | None (input only) | `_normalizationService.GetAllArtists()` | WORKING |
| RefreshArtistsButton | Button (🔄) | Reload artist list from database | `RefreshArtistsButton_Click` | `_normalizationService.GetAllArtists()` | WORKING |
| OfficialTitleTextBox | TextBox | Enter canonical song title | None (input only) | None | WORKING |
| AliasesTextBox | TextBox (multi-line) | Enter aliases, one per line | None (input only) | None | WORKING |
| StatusTextBlock | TextBlock | Display success/error messages | Updated by validation/save logic | None (UI only) | WORKING |
| AddButton | Button | Validate inputs, add song, clear form | `AddButton_Click` | `_normalizationService.AddSong()` | WORKING |
| CancelButton | Button | Close dialog without saving | `CancelButton_Click` | None (sets `DialogResult = false`) | WORKING |

**Total Interactive Elements:** 7

---

## User Workflows

### Workflow 1: Add Song to Existing Artist

**Goal:** Add a new song for an existing artist (e.g., add "Box of Rain" to Grateful Dead).

**Steps:**
1. User clicks "Add Song..." button in Settings Window
2. Dialog opens with default artist "Grateful Dead" pre-selected (line 20)
3. User selects different artist from dropdown if needed
   - Dropdown populated via `LoadArtists()` (line 23-40)
   - Contains all artists from database + common artists list
   - Sorted alphabetically
4. User enters official song title in `OfficialTitleTextBox` (e.g., "Box of Rain")
5. User optionally enters aliases in `AliasesTextBox`:
   ```
   Box Of Rain
   Boxofrain
   Box o Rain
   ```
   - One alias per line (accepts Enter key)
   - Auto-scrolls with `VerticalScrollBarVisibility="Auto"`
6. User clicks "Add Song" button (or presses Enter)
7. Validation runs (line 51-69):
   - Artist name trimmed and checked for empty (line 52, 55)
   - Official title trimmed and checked for empty (line 53, 63)
   - Focus moved to invalid field if validation fails
8. Aliases parsed (line 72-82):
   - Text split on `\r` and `\n` characters
   - Each line trimmed
   - Empty lines removed
   - Result: List<string> of aliases
9. Service call: `_normalizationService.AddSong(officialTitle, aliases, artistName)` (line 87)
   - Song written to `Data/songs.json` immediately
10. Success message shown: "✓ Song '{title}' added successfully!" (line 89-90)
11. Form cleared for next entry (line 93-95):
    - `OfficialTitleTextBox.Clear()`
    - `AliasesTextBox.Clear()`
    - Focus returns to `OfficialTitleTextBox`
12. Dialog remains open for additional song entries

**Success Path:** Song saved to database, form cleared, ready for next song.

**Failure Paths:**
- Empty artist name → Status message: "Please enter an artist name" (red), focus to artist combo
- Empty song title → Status message: "Please enter a song title" (red), focus to title textbox
- Exception during save → Status message: "Error: {exception message}" (red) (line 103)

**Design Decision:** Dialog does NOT close after successful add (line 98-99 commented out). This allows bulk addition of multiple songs without re-opening dialog each time.

---

### Workflow 2: Add Song for New Artist

**Goal:** Add a song for an artist not yet in the database (e.g., add "Friend of the Devil" for "Bob Weir").

**Steps:**
1. User opens dialog (default shows "Grateful Dead")
2. User types new artist name directly into editable ComboBox (e.g., "Bob Weir")
   - `IsEditable="True"` allows freeform text entry (line 29)
3. User enters song title and aliases as in Workflow 1
4. User clicks "Add Song"
5. Validation passes (artist text is not empty)
6. Service call: `_normalizationService.AddSong(title, aliases, "Bob Weir")`
   - If "Bob Weir" doesn't exist in database, new artist created automatically
   - Song added under new artist
7. Success message shown, form cleared
8. User clicks refresh button (🔄) to see new artist in dropdown
   - `RefreshArtistsButton_Click` reloads artist list (line 42-47)
   - "Bob Weir" now appears in alphabetical order
   - Status message: "Artist list refreshed" (green)

**Success Path:** New artist created, song added, artist list refreshed.

**Key Feature:** Editable ComboBox enables creating new artists on-the-fly without separate "Add Artist" dialog.

---

### Workflow 3: Add Multiple Songs Sequentially

**Goal:** Add 10 songs in one session without closing dialog.

**Steps:**
1. User opens dialog, enters first song
2. Clicks "Add Song" → Success message, form clears
3. `OfficialTitleTextBox` auto-focused (line 95)
4. User types next song title immediately (no need to click into field)
5. Repeats for all 10 songs
6. Clicks "Cancel" when done

**Success Path:** All 10 songs added efficiently, single dialog session.

**UX Enhancement:** Auto-focus and form clearing enables fast sequential data entry.

---

### Workflow 4: Refresh Artist List After External Changes

**Goal:** Reload artist list if songs.json was modified externally (e.g., manual edit or import from backup).

**Steps:**
1. User opens dialog
2. ComboBox shows outdated artist list
3. User clicks refresh button (🔄)
4. `RefreshArtistsButton_Click` handler executes (line 42):
   - Calls `LoadArtists()` (line 44)
   - Reloads from `_normalizationService.GetAllArtists()`
   - Updates `ArtistComboBox.ItemsSource` (line 39)
   - Status message: "Artist list refreshed" (line 45)
5. New artists appear in dropdown

**Success Path:** Artist list synchronized with current database state.

**Use Case:** Useful after restoring songs.json from backup or manual JSON editing.

---

## Data Flow

### Input Sources

1. **User Input:**
   - Artist name (from ComboBox selection or manual entry)
   - Official song title (from TextBox)
   - Aliases (multi-line TextBox, one per line)

2. **Service Data:**
   - `NormalizationService.GetAllArtists()` → List of existing artists from database

### Processing Logic

#### Artist List Population (line 23-40)
```csharp
private void LoadArtists()
{
    var artists = _normalizationService.GetAllArtists();

    // Add common artists if not present
    var commonArtists = new List<string> {
        "Grateful Dead",
        "New Riders of the Purple Sage",
        "Jerry Garcia Band",
        "Bob Weir",
        "Phil Lesh"
    };

    foreach (var artist in commonArtists)
    {
        if (!artists.Contains(artist))
        {
            artists.Add(artist);
        }
    }

    artists = artists.OrderBy(a => a).ToList();
    ArtistComboBox.ItemsSource = artists;
}
```

**Key Pattern:** Common artists list ensures frequently-used artists always appear, even in new databases.

**Note:** This is the ONLY place in the codebase with artist names (Grateful Dead, NRPS, etc.). This is acceptable because it's a UI convenience for dropdown population, not functional logic.

#### Validation Logic (line 51-69)
```csharp
var artistName = ArtistComboBox.Text.Trim();
var officialTitle = OfficialTitleTextBox.Text.Trim();

if (string.IsNullOrWhiteSpace(artistName))
{
    StatusTextBlock.Text = "Please enter an artist name";
    StatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
    ArtistComboBox.Focus();
    return;
}

if (string.IsNullOrWhiteSpace(officialTitle))
{
    StatusTextBlock.Text = "Please enter a song title";
    StatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
    OfficialTitleTextBox.Focus();
    return;
}
```

**Validation Rules:**
- Artist name must not be empty/whitespace
- Official title must not be empty/whitespace
- Aliases are optional (empty alias field is valid)

#### Alias Parsing (line 72-82)
```csharp
var aliasesText = AliasesTextBox.Text.Trim();
var aliases = new List<string>();

if (!string.IsNullOrWhiteSpace(aliasesText))
{
    aliases = aliasesText
        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(a => a.Trim())
        .Where(a => !string.IsNullOrWhiteSpace(a))
        .ToList();
}
```

**Parsing Strategy:**
- Split on `\r` and `\n` (handles Windows CRLF, Unix LF, Mac CR)
- `RemoveEmptyEntries` eliminates blank lines
- Trim each line (removes leading/trailing spaces)
- Filter out whitespace-only lines

**Result:** Clean List<string> of aliases, no empty strings.

#### Service Call (line 87)
```csharp
_normalizationService.AddSong(officialTitle, aliases, artistName);
```

**NormalizationService.AddSong() Behavior:**
1. Load existing `Data/songs.json`
2. Find artist by name (case-sensitive)
3. If artist doesn't exist, create new artist entry
4. Add song with canonical title and aliases
5. Save to disk immediately

**Persistence:** Changes written to `songs.json` synchronously (blocking call).

### Output Data

**Return Value:** None (void ShowDialog, no DialogResult set on success)

**Side Effects:**
1. `Data/songs.json` modified on disk
2. Song immediately available for fuzzy matching in MainWindow normalize operation
3. Form cleared for next entry
4. Status message displayed to user

**No Return to Caller:** Dialog does not return data. Changes are persisted directly to database file.

---

## Business Rules

### 1. Default Artist
- **Rule:** Dialog opens with "Grateful Dead" pre-selected (line 20)
- **Rationale:** Primary use case during development/testing
- **Artist-Agnostic Note:** This is a UI default only, not a functional restriction

### 2. Common Artists List
- **Rule:** Five common artists always appear in dropdown, even in empty database (line 28)
- **Artists:** Grateful Dead, New Riders of the Purple Sage, Jerry Garcia Band, Bob Weir, Phil Lesh
- **Rationale:** Bootstrap new databases with likely artists
- **Implementation:** `LoadArtists()` merges common list with database artists

### 3. Artist Name Case Sensitivity
- **Rule:** Artist names are case-sensitive in database lookups
- **Example:** "grateful dead" and "Grateful Dead" are different artists
- **Implication:** User must match existing case exactly or create duplicate

### 4. Alphabetical Sorting
- **Rule:** Artist dropdown sorted alphabetically (line 37)
- **Rationale:** Easy lookup in large artist lists

### 5. Alias Line-Based Format
- **Rule:** Aliases parsed as one per line (split on newline characters)
- **Alternative Formats:** Comma-separated or other delimiters NOT supported
- **Rationale:** Multi-line TextBox with `AcceptsReturn="True"` encourages line-based entry

### 6. Empty Aliases Valid
- **Rule:** Aliases field is optional, can be left blank
- **Behavior:** Empty alias field results in empty List<string> (line 73)
- **Rationale:** Many songs have no common typos/variants

### 7. Dialog Remains Open After Save
- **Rule:** Successful save does NOT close dialog (line 98-99 commented out)
- **Rationale:** Enable bulk entry of multiple songs
- **User Action Required:** Must click "Cancel" to close

### 8. Form Auto-Clear After Save
- **Rule:** Official title and aliases fields cleared after successful add (line 93-94)
- **Artist Field:** NOT cleared (retains previous artist for next song)
- **Rationale:** User often adds multiple songs for same artist

### 9. Focus Management
- **Rule:** After save, focus returns to `OfficialTitleTextBox` (line 95)
- **Rationale:** Next field user needs to fill
- **On Validation Failure:** Focus moves to invalid field (line 59, 67)

### 10. Immediate Persistence
- **Rule:** Songs saved to disk immediately, no "Save All" button required
- **Rationale:** Prevents data loss if application crashes
- **Implication:** No undo functionality (song cannot be un-added from this dialog)

### 11. Error Handling
- **Rule:** Exceptions during save caught and displayed to user (line 101-105)
- **Behavior:** Error message shown in red, dialog remains open, form NOT cleared
- **Rationale:** User can correct issue and retry without re-entering data

---

## Validation Rules

### Artist Name Validation
- **Rule:** Must not be empty or whitespace-only
- **Trimming:** Leading/trailing spaces removed before validation
- **Error Message:** "Please enter an artist name"
- **Error Color:** Red
- **Focus Behavior:** ArtistComboBox.Focus() called
- **Implementation:** Line 55-61

### Official Title Validation
- **Rule:** Must not be empty or whitespace-only
- **Trimming:** Leading/trailing spaces removed before validation
- **Error Message:** "Please enter a song title"
- **Error Color:** Red
- **Focus Behavior:** OfficialTitleTextBox.Focus() called
- **Implementation:** Line 63-69

### Aliases Validation
- **Rule:** No validation required (optional field)
- **Parsing:** Splits on newlines, trims each line, removes empty lines
- **Result:** List<string> (may be empty)
- **Implementation:** Line 72-82

### Duplicate Song Check
- **Rule:** NO duplicate checking performed in this dialog
- **Behavior:** User can add same song title multiple times
- **Rationale:** NormalizationService handles duplicates (unclear from this code what happens)
- **Risk:** User may accidentally create duplicate entries

---

## Known Issues

### 1. No Duplicate Song Prevention
**Problem:** Dialog does not check if song already exists before adding.

**Impact:** User can create duplicate songs with identical canonical titles.

**Current Behavior:** `NormalizationService.AddSong()` called regardless of existing songs.

**Expected Behavior:** Ideally, dialog would warn: "Song 'Dark Star' already exists for Grateful Dead. Add anyway?"

**Status:** NO VALIDATION - User responsible for avoiding duplicates.

---

### 2. Case-Sensitive Artist Matching
**Problem:** Artist names are case-sensitive. Typing "grateful dead" creates a separate artist from "Grateful Dead".

**Impact:** Database can accumulate duplicate artists with different casing.

**Example:**
- "Grateful Dead" (598 songs)
- "grateful dead" (1 song added via manual entry)

**Workaround:** User must select from dropdown to match existing case.

**Status:** WORKING AS DESIGNED - No case-insensitive matching.

---

### 3. No Undo Functionality
**Problem:** Songs immediately saved to disk with no undo.

**Impact:** User must manually edit `songs.json` to remove incorrectly-added songs.

**Workaround:** Use ManageSongsDialog to verify additions (but it's read-only).

**Status:** LIMITATION - No delete functionality exists in any dialog.

---

### 4. Common Artists List Hardcoded
**Problem:** Line 28 has hardcoded list of "common artists" (Grateful Dead, NRPS, JGB, Bob Weir, Phil Lesh).

**Impact:** Not truly artist-agnostic. Other music collections won't have relevant defaults.

**Rationale:** Acceptable as UI convenience, not functional requirement.

**Status:** WORKING AS DESIGNED - Development default values.

---

### 5. Refresh Button Not Auto-Triggered
**Problem:** After adding song with new artist, dropdown doesn't automatically refresh.

**Impact:** User must manually click refresh (🔄) to see new artist in dropdown.

**Expected Behavior:** Dropdown could auto-refresh after successful add.

**Workaround:** Click refresh button after adding new artist.

**Status:** MINOR UX ISSUE - Manual refresh required.

---

## Edge Cases

### 1. Empty Artist Dropdown (New Database)
**Scenario:** User opens dialog on first run with empty `songs.json`.

**Behavior:**
- `_normalizationService.GetAllArtists()` returns empty list
- Common artists list added (5 artists)
- Dropdown shows: "Grateful Dead", "New Riders of the Purple Sage", "Jerry Garcia Band", "Bob Weir", "Phil Lesh"

**Handling:** Common artists provide bootstrap values.

**User Impact:** Can immediately start adding songs without manual artist entry.

---

### 2. Alias Field with Only Whitespace
**Scenario:** User enters several spaces/tabs/newlines in alias field but no actual text.

**Behavior:**
- Alias parsing splits on newlines (line 78)
- Each line trimmed (line 79)
- Empty/whitespace-only lines filtered out (line 80)
- Result: Empty List<string>

**Handling:** Works correctly - no aliases added.

**User Impact:** No invalid empty aliases saved to database.

---

### 3. Song Title with Leading/Trailing Spaces
**Scenario:** User enters "  Dark Star  " with spaces.

**Behavior:**
- Title trimmed before validation: `OfficialTitleTextBox.Text.Trim()` (line 53)
- Saved as "Dark Star" (no spaces)

**Handling:** Automatic cleanup prevents inconsistent spacing.

**User Impact:** Database remains clean with normalized spacing.

---

### 4. Alias with Newline in Middle (Not Line Break)
**Scenario:** User copies text with embedded newlines from external source.

**Behavior:**
- Split recognizes both `\r` and `\n` (line 78)
- Each line processed separately
- Multi-line paste becomes multiple aliases

**Handling:** Works correctly - each line becomes separate alias.

**User Impact:** Can paste multi-line text, auto-splits into aliases.

---

### 5. Very Long Song Title
**Scenario:** User enters 200-character song title.

**Behavior:**
- No max-length validation in dialog
- Passes to `NormalizationService.AddSong()`
- Saved to JSON (no character limit)

**Handling:** No truncation or rejection.

**User Impact:** Long titles accepted, may cause UI layout issues elsewhere.

---

### 6. Special Characters in Song Title
**Scenario:** User enters title with special characters: `"C.C. Rider (Live) [Acoustic]"`.

**Behavior:**
- No character filtering in dialog
- Passed directly to service
- Saved to JSON (JSON escaping handled by Newtonsoft.Json)

**Handling:** Special characters preserved correctly.

**User Impact:** Full Unicode support, no restrictions.

---

### 7. Duplicate Aliases
**Scenario:** User enters same alias multiple times:
```
Dark star
dark star
DARK STAR
```

**Behavior:**
- All three saved to aliases list (no duplicate checking)
- Each becomes separate entry in List<string>

**Handling:** No deduplication performed.

**User Impact:** Duplicate aliases waste space but functionally harmless (fuzzy matching handles them).

---

### 8. Exception During Save
**Scenario:** `songs.json` file locked by another process during save operation.

**Behavior:**
- `_normalizationService.AddSong()` throws exception (line 87)
- Exception caught by try/catch (line 101)
- Error message displayed: "Error: {ex.Message}" (line 103)
- StatusTextBlock.Foreground set to Red (line 104)
- Form NOT cleared (user data preserved)
- Dialog remains open

**Handling:** Graceful error handling with user feedback.

**User Impact:** Can retry after resolving file lock issue.

---

### 9. Cancel After Partial Entry
**Scenario:** User types song title, then clicks "Cancel" without saving.

**Behavior:**
- `CancelButton_Click` sets `DialogResult = false` (line 110)
- Dialog closes immediately
- No data saved
- Form contents discarded

**Handling:** Standard cancel behavior.

**User Impact:** No confirmation prompt - unsaved data lost immediately.

---

### 10. Extremely Long Alias List
**Scenario:** User pastes 1000 lines into alias field.

**Behavior:**
- All lines parsed (no limit)
- List<string> with 1000 elements created
- Passed to service
- Saved to JSON (JSON file becomes large)

**Handling:** No limit enforced.

**User Impact:** Large JSON file, potential performance impact on fuzzy matching.

---

## Dependencies

### Services
- **NormalizationService** - Provides `GetAllArtists()` and `AddSong(title, aliases, artist)`

### Data Files
- **Data/songs.json** - Modified directly by service call

### Parent Window
- **SettingsWindow** - Sets dialog owner for modal centering

### External Libraries
- **Newtonsoft.Json** - Used by NormalizationService for JSON serialization (not directly referenced in dialog code)

---

## File Paths

**XAML:** [AddSongDialog.xaml](AddSongDialog.xaml)
**Code-Behind:** [AddSongDialog.xaml.cs](AddSongDialog.xaml.cs)

**Lines of Code:** 74 XAML, 115 C# (189 total)

---

## Related Documentation

- [00-ui-audit.md](documentation/00-ui-audit.md) - Complete UI audit
- [05-manage-songs-dialog.md](documentation/05-manage-songs-dialog.md) - Browse/export songs (read-only counterpart)
- [06-settings-window.md](documentation/06-settings-window.md) - Parent window that opens this dialog

---

**Last Updated:** 2026-03-01
**Status:** Complete feature documentation
