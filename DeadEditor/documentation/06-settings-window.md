# Feature Documentation: Settings Window

## Purpose

The **Settings Window** is the central configuration hub for DeadEditor, providing access to library path settings, primary artist configuration, song database management, and data reset operations. Users configure the two-path library system (audience recordings vs official releases), launch song management dialogs, and can perform destructive operations like resetting all data. This window serves as the gateway to AddSongDialog and ManageSongsDialog, maintaining the separation between configuration (Settings) and viewing (Library Browser).

## How It's Opened

**Trigger:** User clicks "Settings" from Library Browser Window menu bar

**Caller:** [LibraryBrowserWindow.xaml.cs:~L900](LibraryBrowserWindow.xaml.cs) (Menu item click handler)
```csharp
private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
{
    var settingsWindow = new SettingsWindow(this, _librarySettings, _normalizationService);
    settingsWindow.Owner = this;
    settingsWindow.ShowDialog();
}
```

**Parameters:**
- `LibraryBrowserWindow` - Parent window for callbacks (library refresh)
- `LibrarySettings` - Current settings object (modified in-place)
- `NormalizationService` - Song database service (passed to song dialogs)

**Modal Behavior:** Yes (ShowDialog blocks Library Browser until closed)

---

## Screen Layout

### Visual Structure
A centered modal dialog (650x400px, resizable with minimum 500x350) with vertically-stacked sections:

**1. Library Settings Section**
- **Library Root Path (Audience Recordings):**
  - Read-only TextBox showing current path
  - "Browse..." button to select folder
- **Official Releases Path (Dave's Picks, Road Trips, etc.):**
  - Read-only TextBox showing current path
  - "Browse..." button to select folder
- **fpcalc.exe Path (MusicBrainz Fingerprinting):**
  - Read-only TextBox showing current path to fpcalc.exe
  - "Browse..." button to select .exe file
  - Helper text: "Required for MusicBrainz audio fingerprinting. Download from https://acoustid.org/chromaprint"
- **Primary Artist Name:**
  - Editable TextBox (optional field)
  - Helper text: "(Leave empty to show all artists from MusicBrainz matches)"

**Separator (horizontal line)**

**2. Song Database Section**
- Title: "Song Database" (16pt bold)
- Description: "Add songs to the database without recompiling the application."
- Two buttons (side-by-side):
  - "Add Song..." (light blue background #E3F2FD)
  - "Manage Songs..." (light green background #E8F5E9)

**Separator (horizontal line)**

**3. Data Management Section**
- Title: "Data Management" (16pt bold)
- Warning: "Reset all library data and settings. All files will be moved to the Recycle Bin."
- "Reset All Data" button (light red background #FFEBEE)

**4. Footer**
- "Close" button (right-aligned)

### Color Scheme
- Light blue (#E3F2FD): Add Song button
- Light green (#E8F5E9): Manage Songs button
- Light red (#FFEBEE): Reset Data button (warning color)
- Gray helper text for descriptions

---

## Interactive Elements

| Element | Type | Action | Event Handler | API/Service Call | Status |
|---------|------|--------|---------------|------------------|--------|
| LibraryRootTextBox | TextBox (read-only) | Display library root path | None | None | WORKING |
| BrowseLibraryButton | Button | Open folder browser for library root | `BrowseLibraryButton_Click` | `FolderBrowserDialog`, `_librarySettings.Save()`, `_libraryWindow.UpdateLibraryRootDisplay()` | WORKING |
| OfficialReleasesTextBox | TextBox (read-only) | Display official releases path | None | None | WORKING |
| BrowseOfficialReleasesButton | Button | Open folder browser for official releases | `BrowseOfficialReleasesButton_Click` | `FolderBrowserDialog`, `_librarySettings.Save()`, `_libraryWindow.UpdateLibraryRootDisplay()` | WORKING |
| FpcalcPathTextBox | TextBox (read-only) | Display fpcalc.exe path | None | None | WORKING |
| BrowseFpcalcButton | Button | Open file browser for fpcalc.exe | `BrowseFpcalcButton_Click` | `OpenFileDialog` (filter: .exe), `_librarySettings.Save()` | WORKING |
| PrimaryArtistTextBox | TextBox | Enter primary artist name (optional) | None (input only) | None (saved on close) | WORKING |
| AddSongButton | Button | Open AddSongDialog | `AddSongButton_Click` | Opens `AddSongDialog` | WORKING |
| ManageSongsButton | Button | Open ManageSongsDialog | `ManageSongsButton_Click` | Opens `ManageSongsDialog` | WORKING |
| ResetDataButton | Button | Delete all library files and settings | `ResetDataButton_Click` | `Directory.GetDirectories/GetFiles()`, `FileSystem.DeleteDirectory/DeleteFile()` (recycle bin), `_librarySettings.Save()`, `_libraryWindow.ClearCurrentView()` | WORKING |
| CloseButton | Button | Save primary artist setting and close | `CloseButton_Click` | `_librarySettings.Save()` | WORKING |

**Total Interactive Elements:** 11

---

## User Workflows

### Workflow 1: Configure Library Paths

**Goal:** Set up two-path library system for audience recordings and official releases.

**Steps:**
1. User clicks "Settings" from Library Browser menu
2. Dialog opens showing current paths (may be empty on first run)
3. User clicks "Browse..." next to "Library Root Path" field
4. `BrowseLibraryButton_Click` handler executes (line 28):
   - `FolderBrowserDialog` shown with description: "Select Library Root Folder (Audience Recordings)"
   - Dialog initializes with current path as `SelectedPath` (line 33)
5. User navigates to folder (e.g., `D:\Music\Grateful Dead\Live Recordings`)
6. User clicks OK in folder browser
7. Path saved immediately (line 38-40):
   ```csharp
   _librarySettings.LibraryRootPath = folderDialog.SelectedPath;
   _librarySettings.Save();
   LibraryRootTextBox.Text = _librarySettings.LibraryRootPath;
   ```
8. Library Browser updated: `_libraryWindow.UpdateLibraryRootDisplay(path)` (line 43)
9. User repeats for Official Releases path
10. User clicks "Close"

**Success Path:** Paths saved to `%APPDATA%/DeadEditor/settings.json`, Library Browser refreshes to show new library content.

**Side Effect:** Library Browser calls `LoadShows()` in response to `UpdateLibraryRootDisplay()`, rebuilding concert grid.

---

### Workflow 2: Set Primary Artist Name

**Goal:** Configure MusicBrainz search to filter by specific artist.

**Steps:**
1. User opens Settings window
2. User types "Grateful Dead" in Primary Artist Name field
3. User clicks "Close"
4. `CloseButton_Click` handler executes (line 195-201):
   ```csharp
   _librarySettings.PrimaryArtistName = PrimaryArtistTextBox.Text;
   _librarySettings.Save();
   this.Close();
   ```
5. Setting saved to `settings.json`

**Success Path:** Primary artist name stored, used by MusicBrainz service to filter search results.

**Use Case:** If user's collection is primarily one artist, this filters MusicBrainz results to reduce noise.

**Note:** Field can be left empty (line 43-44 helper text explains this shows all artists).

---

### Workflow 3: Configure fpcalc.exe Path

**Goal:** Set path to fpcalc.exe for MusicBrainz audio fingerprinting.

**Steps:**
1. User opens Settings window
2. User clicks "Browse..." next to "fpcalc.exe Path" field
3. `BrowseFpcalcButton_Click` handler executes:
   - `OpenFileDialog` shown with filter: "Executable Files|*.exe"
   - Dialog title: "Select fpcalc.exe (Chromaprint)"
   - Dialog initializes with current path if set
4. User navigates to fpcalc.exe location (e.g., `C:\Tools\fpcalc.exe`)
5. User selects fpcalc.exe file and clicks OK
6. Path saved immediately:
   ```csharp
   _librarySettings.FpcalcPath = fileDialog.FileName;
   _librarySettings.Save();
   FpcalcPathTextBox.Text = _librarySettings.FpcalcPath;
   ```
7. User clicks "Close"

**Success Path:** fpcalc.exe path saved to `settings.json`, MusicBrainz fingerprinting enabled for studio album imports.

**Use Case:** Required for automatic album identification via audio fingerprinting. Without this, studio album imports will fail with clear error message directing user to Settings.

**Download Source:** https://acoustid.org/chromaprint

**Validation:** When MusicBrainzService attempts fingerprinting, it will throw exception if path not configured or file doesn't exist at configured path.

---

### Workflow 4: Add Songs to Database

**Goal:** Open AddSongDialog to add new songs.

**Steps:**
1. User clicks "Add Song..." button (light blue)
2. `AddSongButton_Click` executes (line 181-186):
   ```csharp
   var dialog = new AddSongDialog(_normalizationService);
   dialog.Owner = this;
   dialog.ShowDialog();
   ```
3. AddSongDialog opens as modal child
4. User adds songs (see [04-add-song-dialog.md](documentation/04-add-song-dialog.md))
5. User closes AddSongDialog (via Cancel or close button)
6. Control returns to Settings Window (remains open)

**Success Path:** Songs added to database, Settings Window still open for additional operations.

**Design Decision:** Settings Window does NOT close when song dialogs close (allows sequential operations).

---

### Workflow 5: Browse Song Database

**Goal:** View all songs with search/filter capabilities.

**Steps:**
1. User clicks "Manage Songs..." button (light green)
2. `ManageSongsButton_Click` executes (line 188-193):
   ```csharp
   var dialog = new ManageSongsDialog(_normalizationService);
   dialog.Owner = this;
   dialog.ShowDialog();
   ```
3. ManageSongsDialog opens as modal child
4. User browses/searches songs (see [05-manage-songs-dialog.md](documentation/05-manage-songs-dialog.md))
5. User closes ManageSongsDialog
6. Control returns to Settings Window

**Success Path:** User views database, returns to Settings for more configuration.

---

### Workflow 6: Reset All Data

**Goal:** Delete all library content and reset settings (nuclear option for fresh start).

**Steps:**
1. User clicks "Reset All Data" button (light red)
2. `ResetDataButton_Click` executes (line 66)
3. Confirmation dialog shown (line 69-83):
   ```
   This will:
   • Move all files in your library to the Recycle Bin
   • Reset the songs database
   • Clear library settings

   This cannot be easily undone. Continue?
   ```
   - MessageBoxButton.YesNo with Warning icon
   - Default button: No (safer default)
4. User clicks "Yes" to confirm
5. Deletion process executes (line 85-143):
   - **Library Root Path contents:**
     - `Directory.GetDirectories()` gets all subdirectories (line 93)
     - `Directory.GetFiles()` gets all files in root (line 94)
     - Each directory: `FileSystem.DeleteDirectory()` with `SendToRecycleBin` (line 99-103)
     - Each file: `FileSystem.DeleteFile()` with `SendToRecycleBin` (line 109-113)
     - Counter incremented for each deleted item
   - **Official Releases Path contents:** (same process, line 118-143)
   - **Library Browser cleared:** `_libraryWindow.ClearCurrentView()` (line 146)
   - **Settings reset:** (line 149-153)
     ```csharp
     _librarySettings.LibraryRootPath = "";
     _librarySettings.OfficialReleasesPath = "";
     _librarySettings.Save();
     ```
   - **UI updated:** TextBoxes cleared to empty strings (line 152-153)
   - **Library Browser updated:** `UpdateLibraryRootDisplay("")` (line 156)
6. Success message shown (line 158-165):
   ```
   Successfully reset all data!

   • {deletedItems} items moved to Recycle Bin
   • Library settings cleared

   You can restore files from the Recycle Bin if needed.
   ```
7. Settings Window closes automatically (line 168)
8. Library Browser activated (brought to foreground) (line 169)

**Success Path:** All library files in Recycle Bin, settings cleared, clean slate for new library.

**Safety Features:**
- Confirmation dialog with explicit warning
- Recycle Bin (not permanent deletion) - files can be restored
- "No" button is default (prevents accidental Enter-key deletion)

**Failure Paths:**
- User clicks "No" → Nothing happens, dialog remains open (line 82)
- IOException during deletion → Error message shown, process halts (line 171-178)
- Directory doesn't exist → Skipped gracefully (line 90-92, 118-120)

---

## Data Flow

### Input Sources

1. **User Input:**
   - Library root path (via folder browser)
   - Official releases path (via folder browser)
   - Primary artist name (text entry)

2. **Current State:**
   - `LibrarySettings` object passed from Library Browser (line 15-20)

### Processing Logic

#### Path Selection (line 28-64)

**Library Root Path:**
```csharp
var folderDialog = new System.Windows.Forms.FolderBrowserDialog
{
    Description = "Select Library Root Folder (Audience Recordings)",
    SelectedPath = _librarySettings.LibraryRootPath
};

if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
{
    _librarySettings.LibraryRootPath = folderDialog.SelectedPath;
    _librarySettings.Save();  // Immediate persistence
    LibraryRootTextBox.Text = _librarySettings.LibraryRootPath;
    _libraryWindow.UpdateLibraryRootDisplay(_librarySettings.LibraryRootPath);
}
```

**Key Pattern:** Settings saved immediately on change (no "Apply" button required).

**Official Releases Path:** Same logic (line 47-63), note that callback calls same method (`UpdateLibraryRootDisplay`) for both paths (line 62).

#### Reset Data Logic (line 66-179)

**Three-stage deletion:**
1. **Library Root:** Delete directories, then files (line 89-115)
2. **Official Releases:** Delete directories, then files (line 117-143)
3. **Settings:** Clear paths, save empty state (line 149-153)

**Recycle Bin API:**
```csharp
Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
    dir,
    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin
);
```

**Why Microsoft.VisualBasic.FileIO?** Native .NET `Directory.Delete()` permanently deletes. FileSystem API supports Recycle Bin.

#### Primary Artist Saving (line 195-201)

**Deferred Save:** Primary artist name NOT saved until "Close" button clicked (unlike paths, which save immediately).

**Rationale:** Path changes trigger library refresh (expensive operation), so immediate save makes sense. Artist name has no side effects, so defer until user finishes all edits.

### Output Data

**Return Value:** None (void ShowDialog)

**Side Effects:**
1. **Settings File Modified:** `%APPDATA%/DeadEditor/settings.json` updated
2. **Library Browser Refreshed:** `LoadShows()` called if paths changed
3. **Files Deleted:** Library content moved to Recycle Bin (if reset operation performed)

**Persistent State:**
- `LibrarySettings.LibraryRootPath`
- `LibrarySettings.OfficialReleasesPath`
- `LibrarySettings.PrimaryArtistName`

---

## Business Rules

### 1. Two-Path System
- **Rule:** Two separate configurable paths (Library Root and Official Releases)
- **Can Be Same:** User can point both to same directory
- **Can Be Different:** User can separate audience recordings from official releases
- **Rationale:** Organizational flexibility for different library structures

### 2. Immediate Path Persistence
- **Rule:** Path changes saved immediately when user clicks OK in folder browser
- **No Apply Button:** Every path selection is an immediate commit
- **Rationale:** Prevents confusion about unsaved changes

### 3. Deferred Artist Persistence
- **Rule:** Primary artist name saved only on "Close" button click
- **Can Discard:** User can type value, then click window X to discard
- **Rationale:** Lightweight setting with no side effects, no need for immediate save

### 4. Library Browser Refresh on Path Change
- **Rule:** Changing either path triggers `UpdateLibraryRootDisplay()` callback
- **Behavior:** Library Browser calls `LoadShows()` to rebuild concert grid
- **Implication:** Path change immediately visible in Library Browser (Settings Window still open)

### 5. Reset Confirmation Required
- **Rule:** Reset operation shows Yes/No confirmation dialog before proceeding
- **Default:** "No" button is default (safer)
- **Rationale:** Prevent accidental data loss

### 6. Recycle Bin (Not Permanent Deletion)
- **Rule:** Reset operation moves files to Recycle Bin, not permanent deletion
- **Rationale:** User can restore files if reset was accidental
- **API:** Uses `Microsoft.VisualBasic.FileIO.FileSystem` for Recycle Bin support

### 7. Settings Window Remains Open After Song Dialogs
- **Rule:** Opening AddSongDialog or ManageSongsDialog does NOT close Settings Window
- **Behavior:** Both dialogs are modal children; control returns to Settings after they close
- **Rationale:** User can perform multiple operations without reopening Settings

### 8. Settings Window Auto-Closes After Reset
- **Rule:** Successful reset operation closes Settings Window automatically (line 168)
- **Behavior:** Library Browser activated (brought to foreground)
- **Rationale:** Reset is terminal operation; user likely wants to see empty library

### 9. Empty Paths Allowed
- **Rule:** Library paths can be empty (no validation requiring non-empty paths)
- **Behavior:** Empty path → Library Browser shows no concerts
- **Rationale:** First-run scenario; user can configure later

### 10. Primary Artist Optional
- **Rule:** Primary artist name can be left empty (line 43-44 helper text explains this)
- **Behavior:** Empty → MusicBrainz shows all artists in search results
- **Non-Empty:** MusicBrainz filters results to specified artist only

---

## Validation Rules

**No Validation Enforced** - Settings Window accepts any user input:
- Paths: Any folder path (even invalid/nonexistent)
- Primary artist: Any string (no character restrictions)

**Rationale:** File system operations handle invalid paths gracefully (Directory.Exists checks prevent errors).

---

## Known Issues

### 1. Both Paths Call Same Refresh Method
**Problem:** Changing Official Releases path calls `UpdateLibraryRootDisplay(LibraryRootPath)` instead of separate method (line 62).

**Code:**
```csharp
// BrowseOfficialReleasesButton_Click
_librarySettings.OfficialReleasesPath = folderDialog.SelectedPath;
_librarySettings.Save();
OfficialReleasesTextBox.Text = _librarySettings.OfficialReleasesPath;

// Update library window to reload with new path
_libraryWindow.UpdateLibraryRootDisplay(_librarySettings.LibraryRootPath);  // ← Passes LibraryRootPath, not OfficialReleasesPath
```

**Impact:** Library Browser refresh works correctly (reads both paths from settings), but parameter is misleading.

**Status:** WORKING (functionally correct but confusing code).

---

### 2. No Path Validation
**Problem:** Dialog accepts invalid paths (e.g., `C:\NonexistentFolder`).

**Impact:** Invalid path saved to settings. Library Browser handles gracefully (`Directory.Exists` checks), but user may not realize path is wrong.

**Expected:** Folder browser could validate path exists before accepting.

**Status:** LIMITATION - No existence validation.

---

### 3. Reset Doesn't Delete songs.json
**Problem:** "Reset all library data and settings" warning (line 66-72) says "Reset the songs database", but code does NOT delete `Data/songs.json`.

**Actual Behavior:**
- Library content deleted (audio files)
- Settings cleared (paths)
- **songs.json untouched** (all 598 songs remain)

**Impact:** Misleading warning message. Song database survives reset.

**Status:** INCONSISTENCY - Warning text doesn't match behavior (line 71-72).

---

### 4. No Undo for Path Changes
**Problem:** Path changes saved immediately with no "Cancel" or "Revert" option.

**Impact:** User cannot experiment with paths without committing changes.

**Workaround:** User must manually restore old path if they change their mind.

**Status:** LIMITATION - No undo functionality.

---

### 5. Reset Progress Not Shown
**Problem:** Deleting large library may take minutes, but no progress indicator shown.

**Behavior:** UI appears frozen during deletion (line 97-142 runs on UI thread).

**Impact:** User may think application crashed during long delete operations.

**Expected:** Progress bar or status message showing "{N} files deleted..."

**Status:** UX ISSUE - Async operation (line 66 `async void`), but no await or progress UI.

---

### 6. Primary Artist Not Saved on Path Changes
**Problem:** If user types primary artist name, then changes path without closing dialog, primary artist NOT saved.

**Example:**
1. Type "Grateful Dead" in Primary Artist field
2. Click "Browse..." to change Library Root path
3. Click OK in folder browser
4. Close Settings via window X button (not "Close" button)
5. Primary artist NOT saved (only saved in `CloseButton_Click`, line 198)

**Impact:** User loses unsaved primary artist value.

**Status:** MINOR ISSUE - Inconsistent save behavior (paths immediate, artist deferred).

---

## Edge Cases

### 1. Empty Library Paths on First Run
**Scenario:** User opens Settings on first run with no configured paths.

**Behavior:**
- `LibraryRootTextBox.Text` is empty (line 23)
- `OfficialReleasesTextBox.Text` is empty (line 24)
- Folder browser opens with empty `SelectedPath` (line 33, 52)

**Handling:** Works correctly - user can select folders normally.

**User Impact:** No issues, expected first-run behavior.

---

### 2. Same Path for Both Library Root and Official Releases
**Scenario:** User sets both paths to same folder (e.g., `D:\Music`).

**Behavior:**
- Both settings saved with identical path
- Library Browser reads same folder twice (once for audience recordings, once for official releases)
- Same albums may appear multiple times in grid

**Handling:** No validation preventing identical paths.

**User Impact:** Confusing duplicate entries in library, but functionally harmless.

---

### 3. Path to Empty Folder
**Scenario:** User selects folder with no subfolders or audio files.

**Behavior:**
- Path saved normally
- Library Browser calls `LoadShows()` (line 43, 62)
- `Directory.GetDirectories()` returns empty array
- Concert grid remains empty

**Handling:** Graceful empty state.

**User Impact:** No errors, just empty library view.

---

### 4. Reset with No Configured Paths
**Scenario:** User clicks "Reset All Data" when both paths are empty.

**Behavior:**
- Confirmation dialog shown normally
- User clicks "Yes"
- Path existence checks fail (line 90-92, 118-120):
  ```csharp
  if (!string.IsNullOrEmpty(_librarySettings.LibraryRootPath) &&
      Directory.Exists(_librarySettings.LibraryRootPath))
  ```
- Both deletion sections skipped
- `deletedItems` remains 0
- Success message: "0 items moved to Recycle Bin"

**Handling:** Safe no-op.

**User Impact:** Harmless, though "Reset All Data" button is pointless when no data exists.

---

### 5. Reset Interrupted (IOException)
**Scenario:** File locked by another process during deletion.

**Behavior:**
- Deletion throws IOException (e.g., "File in use by another process")
- Exception caught (line 171-178)
- Error message shown: "Error resetting data: {ex.Message}"
- **Partial Deletion:** Some files deleted before error (not transactional)
- Settings Window remains open

**Handling:** Exception caught, user notified.

**User Impact:** Library in inconsistent state (some files deleted, others remain). User must manually clean up.

---

### 6. Reset Canceled in Confirmation Dialog
**Scenario:** User clicks "Reset All Data", then "No" in confirmation.

**Behavior:**
- Confirmation result checked (line 80)
- `return` statement exits early (line 82)
- No deletion occurs
- Dialog remains open

**Handling:** Clean cancellation.

**User Impact:** No changes made, as expected.

---

### 7. Primary Artist with Leading/Trailing Spaces
**Scenario:** User types "  Grateful Dead  " with extra spaces.

**Behavior:**
- Value saved as-is (no trimming in `CloseButton_Click`, line 198)
- `_librarySettings.PrimaryArtistName = PrimaryArtistTextBox.Text;`
- Spaces included in MusicBrainz search queries

**Impact:** May cause MusicBrainz search to fail (exact match required).

**Handling:** No trimming performed.

**User Impact:** User may get no results from MusicBrainz due to spaces.

---

### 8. Very Long Primary Artist Name
**Scenario:** User pastes 500-character string into Primary Artist field.

**Behavior:**
- No max-length validation
- Full string saved to settings.json
- Passed to MusicBrainz API (may reject or truncate)

**Handling:** No restrictions.

**User Impact:** Possible MusicBrainz API errors due to excessive length.

---

### 9. AddSongDialog Opened, Then Settings Window Closed
**Scenario:** User clicks "Add Song...", AddSongDialog opens, user tries to close Settings Window.

**Behavior:**
- AddSongDialog is modal child (line 184: `dialog.Owner = this;`)
- Settings Window cannot close while child modal is open
- User must close AddSongDialog first

**Handling:** WPF enforces modal ownership.

**User Impact:** Expected modal dialog behavior.

---

### 10. Library Browser Closed While Settings Window Open
**Scenario:** User opens Settings, then closes Library Browser (parent window).

**Behavior:**
- Settings Window is modal child (caller uses `ShowDialog()`)
- Closing parent typically closes all child windows
- Settings Window closes automatically
- Unsaved primary artist value lost

**Handling:** Standard WPF owner/child lifetime.

**User Impact:** Primary artist lost if not saved via "Close" button.

---

### 11. Path with Special Characters
**Scenario:** User selects path with special characters: `D:\Music\#1 Hits (2003)`

**Behavior:**
- FolderBrowserDialog returns path as-is
- Path saved to settings.json (JSON escaping handled by serializer)
- Directory operations accept special characters

**Handling:** Full Unicode path support.

**User Impact:** No issues with special characters in paths.

---

### 12. Recycle Bin Full During Reset
**Scenario:** Recycle Bin full, cannot accept more files.

**Behavior:**
- `FileSystem.DeleteDirectory/DeleteFile` throws exception
- Caught by try/catch (line 171)
- Error message shown to user
- Partial deletion (files processed before error remain deleted)

**Handling:** Exception caught, process halts.

**User Impact:** Incomplete reset, must manually empty Recycle Bin and retry.

---

## Dependencies

### Services
- **NormalizationService** - Passed to AddSongDialog and ManageSongsDialog for song database access

### Data Models
- **LibrarySettings** - Settings object modified in-place, saved via `.Save()` method

### Parent Window
- **LibraryBrowserWindow** - Receives callbacks for library refresh (`UpdateLibraryRootDisplay`, `ClearCurrentView`, `Activate`)

### Child Dialogs
- **AddSongDialog** - Opened for adding songs (line 181-186)
- **ManageSongsDialog** - Opened for browsing songs (line 188-193)

### External Libraries
- **System.Windows.Forms.FolderBrowserDialog** - Folder selection (WinForms component in WPF app)
- **Microsoft.VisualBasic.FileIO.FileSystem** - Recycle Bin support for file deletion

---

## File Paths

**XAML:** [SettingsWindow.xaml](SettingsWindow.xaml)
**Code-Behind:** [SettingsWindow.xaml.cs](SettingsWindow.xaml.cs)

**Lines of Code:** 78 XAML, 205 C# (283 total)

---

## Related Documentation

- [00-ui-audit.md](documentation/00-ui-audit.md) - Complete UI audit
- [02-library-browser.md](documentation/02-library-browser.md) - Parent window that opens Settings
- [04-add-song-dialog.md](documentation/04-add-song-dialog.md) - Child dialog for adding songs
- [05-manage-songs-dialog.md](documentation/05-manage-songs-dialog.md) - Child dialog for browsing songs

---

**Last Updated:** 2026-03-02
**Status:** Updated to include fpcalc.exe path configuration for MusicBrainz fingerprinting
