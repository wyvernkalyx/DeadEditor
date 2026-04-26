# Source Folder Preservation Inspection — 2026-04-17

## 1. Import Copy Logic Today

### Entry point

`ImportButton_Click()` at [ImportView.xaml.cs:1390](Views/ImportView.xaml.cs#L1390). This is the handler for the "Import to Library" button.

### Source folder discovery

Three entry paths, all stored in `_currentFolderPath` (line 54):

1. **Browse dialog** — `BrowseForFolder()` at [ImportView.xaml.cs:102–117](Views/ImportView.xaml.cs#L102-L117). Uses `System.Windows.Forms.FolderBrowserDialog` with description "Select folder containing audio files (FLAC/MP3)". Selected path stored at line 111, then `LoadFolderAsync()` called at line 112.

2. **External call** — `LoadFolderFromPath(string folderPath)` at [ImportView.xaml.cs:95–99](Views/ImportView.xaml.cs#L95-L99). Called by ShellWindow header bar.

3. **Concert import** — `ImportForConcert(string folderPath, ...)` at [ImportView.xaml.cs:132–140](Views/ImportView.xaml.cs#L132-L140). Called from ConcertDetailView.

All three fire `FolderPathSelected` event (line 122) to notify ShellWindow, but the path is never displayed in ImportView's own XAML.

### What gets copied today

**Audio files only — FLAC and MP3.**

File discovery happens in `MetadataService.ReadFolder()` at [MetadataService.cs:62–70](Services/MetadataService.cs#L62-L70):

```csharp
var audioFiles = Directory.GetFiles(folderPath, "*.flac")
                         .Concat(Directory.GetFiles(folderPath, "*.mp3"))
                         .OrderBy(f => f)
                         .ToList();
```

- **No recursive search** — `Directory.GetFiles()` without `SearchOption.AllDirectories`
- **No non-audio files copied** — text files, images, PDFs, etc. are all excluded
- Only files that made it into the track grid are passed to `ImportToLibrary()`

### Target folder creation

**Folder name computation:** `BuildLibraryFolderName()` at [LibraryImportService.cs:324–362](Services/LibraryImportService.cs#L324-L362)

- **Live recordings** (has date+venue): `{Artist} - {Date} - {Venue} - {City}, {State} - {AlbumName}`
- **Studio albums** (no venue): `{Artist} - {Year} - {AlbumName}`

**Sanitization:** `SanitizeFolderName()` at [LibraryImportService.cs:364–389](Services/LibraryImportService.cs#L364-L389) — replaces colons with ` - `, strips invalid filename chars (`\ / * ? " < > |` → `_`), collapses spaces, trims dots/spaces.

**Folder creation:** [LibraryImportService.cs:40–48](Services/LibraryImportService.cs#L40-L48)

```csharp
var artistFolder = SanitizeFolderName(albumInfo.Artist ?? "Unknown Artist");
var albumFolder = BuildLibraryFolderName(albumInfo);
var targetFolder = Path.Combine(libraryRoot, artistFolder, albumFolder);

if (!Directory.Exists(targetFolder))
    Directory.CreateDirectory(targetFolder);
```

### Copy mechanism

`CopyFileWithRetry()` at [LibraryImportService.cs:167–182](Services/LibraryImportService.cs#L167-L182):

```csharp
private static void CopyFileWithRetry(string source, string destination, int maxRetries = 3)
{
    for (int attempt = 0; attempt < maxRetries; attempt++)
    {
        try
        {
            File.Copy(source, destination, overwrite: true);
            return;
        }
        catch (IOException ex) when (attempt < maxRetries - 1)
        {
            Debug.WriteLine($"[IMPORT] Retry {attempt + 1}/{maxRetries} for copy: {ex.Message}");
            Thread.Sleep(500);
        }
    }
}
```

- Uses `File.Copy` with `overwrite: true`
- 3 retries, 500ms delay between attempts
- Handles antivirus scanning / NTFS journal delays
- Called per-track at [LibraryImportService.cs:99](Services/LibraryImportService.cs#L99)

### Conflict behavior today

**Folder-level check:** `ShowExistsInLibrary()` at [LibraryImportService.cs:424–436](Services/LibraryImportService.cs#L424-L436) — returns `bool` based on whether target folder already exists.

**User prompt:** [ImportView.xaml.cs:1418–1429](Views/ImportView.xaml.cs#L1418-L1429) — if folder exists, shows async notification: "This album ({AlbumTitle}) already exists in your library. Overwrite?" with Yes/No.

- If No → import aborted entirely
- If Yes → all files copied with `overwrite: true` (no per-file prompt)

**At file level:** No individual file conflict checks. `File.Copy(..., overwrite: true)` silently overwrites every file.

### Post-copy metadata writes

Immediately after each file is copied, metadata is written via `WriteMetadataWithRetry()` at [LibraryImportService.cs:190–316](Services/LibraryImportService.cs#L190-L316).

Per-track sequence (in `ImportTracksToFolder`, lines 56–147):
1. Copy file (line 99)
2. Read original metadata from source — Genre, Comment, Copyright, Publisher, Composer (lines 108–125)
3. Write all metadata to copied file (line 138) — standard tags + custom FLAC/MP3 fields + artwork

### Sequence diagram (numbered steps with file:line)

```
1. User clicks "Import to Library"
   → ImportButton_Click() [ImportView.xaml.cs:1390]

2. Validate library path is set [line 1401]

3. Check if album folder already exists
   → ShowExistsInLibrary() [LibraryImportService.cs:424]
   → [ImportView.xaml.cs:1418-1420]

4. If exists: prompt "Overwrite?" [line 1424]
   → If No: abort [line 1428]

5. Confirm import dialog: "Import N tracks to {path}?" [line 1432]
   → If No: abort [line 1436]

6. Background thread start [line 1455]
   → ImportToLibrary(libraryRoot, albumInfo, tracks) [LibraryImportService.cs:25]

7. Build target path:
   → SanitizeFolderName(artist) [line 41]
   → BuildLibraryFolderName(albumInfo) [line 42]
   → Path.Combine(libraryRoot, artistFolder, albumFolder) [line 43]

8. Create target directory [line 47]

9. ImportTracksToFolder(targetFolder, tracks, albumInfo) [line 50]

10. FOR EACH track:
    a. Generate filename: "{TrackNumber:D2} - {SongName} ({date}){ext}" [lines 70-92]
    b. CopyFileWithRetry(source, target) [line 99]
    c. Read preserved metadata from source [lines 108-125]
    d. WriteMetadataWithRetry(target, track, albumInfo, ...) [line 138]
    e. Report progress [line 67]

11. ImportCompleted event → UI thread reset [ImportView.xaml.cs:1479-1482]
```

## 2. Multi-Disc Handling (current behavior)

**Multi-disc is tag-based, not folder-based.**

- Disc numbers come from ID3/Xiph tags: `file.Tag.Disc` at [MetadataService.cs:93](Services/MetadataService.cs#L93) (import mode) and [line 113](Services/MetadataService.cs#L113) (edit mode).
- If `Disc > 0`, that value is used; otherwise defaults to 1.
- `Directory.GetFiles()` is **not recursive** — files in `Disc 1/`, `Disc 2/` subfolders are **not discovered**.
- All tracks are copied to a **single flat target folder** — no disc subfolders created.
- Track numbers use disc-aware encoding (Disc 1 Track 1 = 101, Disc 2 Track 1 = 201).

**Implication for spec:** If a source folder contains subfolders with non-audio files (e.g., `artwork/`, `info/`), the current code doesn't see them at all. A recursive copy of "entire folder contents" would need new logic.

## 3. Source Folder Path Display in Import View

### Visibility today

**The source folder path is NOT displayed anywhere in ImportView's own XAML.**

- `_currentFolderPath` is tracked in code at [ImportView.xaml.cs:54](Views/ImportView.xaml.cs#L54) but never bound to any UI element.
- The `FolderPathSelected` event (line 122) fires to notify ShellWindow, but ShellWindow/HeaderBar doesn't display it either (no matching handler found in HeaderBar.xaml or HeaderBar.xaml.cs).
- `StatusTextBlock` at [ImportView.xaml:114–117](Views/ImportView.xaml#L114-L117) shows status messages ("Ready — select a folder to begin", "N tracks loaded") but never the path.

### Best placement for Open Folder button

The Import view's bottom bar is a 3-column Grid ([ImportView.xaml:96–127](Views/ImportView.xaml#L96-L127)):

| Column 0 (Left) | Column 1 (Center) | Column 2 (Right) |
|---|---|---|
| Browse + View Info buttons | StatusTextBlock | Write + Import buttons |

**Recommended placement:** Add a new row or element in Column 0, immediately after the Browse button. Pattern:
- `[Browse] [View Info] | {FolderPathTextBlock} [Open Folder]` — path + button inline in the left section
- Alternatively, a dedicated row above the bottom bar showing: `Source: {path} [📁 Open Folder]`

The path TextBlock should be a `TextBlock` with `TextTrimming="CharacterEllipsis"` (paths can be long), and the Open Folder button immediately to its right.

## 4. Managed Folder Path Display in Edit Metadata View

### Visibility today

**The managed folder path is NOT displayed anywhere in EditMetadataView's XAML.**

- The path is available in code via `_show.FolderPath` (line 114 in .xaml.cs) and `_show.FolderPaths` (line 90).
- `StatusTextBlock` at [EditMetadataView.xaml:442–445](Views/EditMetadataView.xaml#L442-L445) shows only status messages ("Ready", "Saved N files").
- No TextBlock, Label, or Border displays folder information.

### Best placement for Open Folder button

The Edit Metadata view's bottom section (Grid.Row="3") at [EditMetadataView.xaml:436–451](Views/EditMetadataView.xaml#L436-L451) is a 2-column Grid:

| Column 0 | Column 1 |
|---|---|
| StatusTextBlock | ProgressBar |

**Recommended placement:** Replace or extend Column 0 to include:
- `{ManagedFolderPathTextBlock} [📁 Open Folder] | StatusTextBlock`
- Or add a new row between the main content and the status bar.

The path should use the same `TextBlock` + `TextTrimming="CharacterEllipsis"` pattern as Import, and the Open Folder button should use identical styling.

### Prerequisite flag

**YES — prerequisite needed.** The managed folder path (`_show.FolderPath`) must be surfaced into a XAML element before the Open Folder button can be placed adjacent to it. This requires:
1. Adding a `TextBlock` for the path
2. Binding or setting it from `_show.FolderPath` in `LoadShow()` or equivalent

## 5. Icon / Button Conventions

### Icon library in use

**Segoe MDL2 Assets** — Microsoft's modern icon font, used consistently throughout the app.

Evidence:
- [SidebarPanel.xaml:61](Views/SidebarPanel.xaml#L61): `FontFamily="Segoe MDL2 Assets"` for all sidebar icons
- [PlayerBar.xaml:71](Views/PlayerBar.xaml#L71): Same font for media controls
- No other icon libraries detected (no Lucide, FontAwesome, or custom SVGs)

### Recommended icon for Open Folder

**`&#xE838;`** — Segoe MDL2 Assets "OpenLocal" glyph (folder with arrow). This represents "open in Explorer" semantically.

Alternative: **`&#xED25;`** — "OpenFolderHorizontal" glyph, which is more explicit about opening a folder.

The sidebar already uses **`&#xE8F1;`** for the Library icon (generic folder), so using a different glyph avoids confusion.

### Visual style to match

From existing button patterns:

**Small action buttons (like Browse, View Info in Import):**
- Height: 32px
- FontSize: 16
- Background: default WPF button (dark theme)
- No explicit icon styling — these are text-label buttons

**Sidebar icon buttons (SidebarButtonStyle):**
- Size: 44×44px, FontSize: 24
- Background: Transparent → `#2D2D30` on hover → `#007ACC` when active
- Foreground: `#888888` → `#E0E0E0` on hover → `#FFFFFF` when active
- Cursor: Hand, CornerRadius: 4

**HeaderBar back button (BackButtonStyle) at [HeaderBar.xaml:122–148](Views/HeaderBar.xaml#L122-L148):**
- Background: Transparent → `#3E3E42` on hover
- Foreground: `#E0E0E0` → `#FFFFFF` on hover
- Padding: 12,8; MinHeight: 44; FontSize: 18
- Cursor: Hand, CornerRadius: 4

**Recommended style for Open Folder button:**
- Match the small action button height (32px) used in Import/Edit Metadata bottom bars
- Content: icon glyph + " Open Folder" text label (consistent with "Browse" and "View Info" label pattern)
- Use `FontFamily="Segoe MDL2 Assets"` for the icon portion
- A composite button with a small StackPanel: `[icon TextBlock] [label TextBlock]`

## 6. Existing Conflict Dialog Patterns

### Any existing "file exists" dialog?

**Yes — folder-level conflict dialog exists in ImportView.**

[ImportView.xaml.cs:1418–1429](Views/ImportView.xaml.cs#L1418-L1429):

```csharp
bool exists = _libraryImportService.ShowExistsInLibrary(
    _librarySettings.LibraryRootPath, _albumInfo);

if (exists)
{
    bool overwrite = await ShowNotificationAsync("Show Already Exists",
        $"This album ({_albumInfo.AlbumTitle}) already exists in your library.\n\nOverwrite?",
        showYesNo: true);
    if (!overwrite) return;
}
```

**Implementation:** Custom async notification modal (`NotificationPanel`) at [ImportView.xaml:558–611](Views/ImportView.xaml#L558-L611):
- Border with `CornerRadius="6"`, dark theme (`#2D2D30` background, `#007ACC` border)
- `DropShadowEffect` with 30px blur
- Yes/No/OK buttons
- Black overlay with 0.7 opacity
- Method: `ShowNotificationAsync()` at [ImportView.xaml.cs:1602–1624](Views/ImportView.xaml.cs#L1602-L1624)

**Other MessageBox patterns in the codebase:**
- [EditMetadataView.xaml.cs:1141](Views/EditMetadataView.xaml.cs#L1141): `MessageBox.Show()` for MBID existence check (Yes/No/Cancel)
- [EditMetadataView.xaml.cs:630](Views/EditMetadataView.xaml.cs#L630): `MessageBox.Show()` for unsaved changes
- [SettingsView.xaml.cs:131, 342, 388, 399](Views/SettingsView.xaml.cs): Various `MessageBox.Show()` patterns

### Reusable? Or need new?

**The existing `NotificationPanel` pattern is reusable** for simple Yes/No prompts but **insufficient for the per-file conflict dialog** needed by the spec.

The spec requires:
- Per-file decision: Skip / Overwrite / Rename
- "Apply to all remaining" checkbox
- Display of conflicting filename

This needs a **new dialog** — either:
1. A new custom overlay panel (matching `NotificationPanel` visual style) with radio buttons + checkbox
2. A standalone WPF Window dialog (simpler to implement, but breaks the overlay pattern)

**Recommendation:** Extend the `NotificationPanel` pattern with a new variant that supports radio-button choices and an "apply to all" checkbox. This keeps the visual language consistent.

## 7. Design Implications for Spec

### Scope of code changes for each feature

**Feature 1: Copy entire source folder contents (not just audio files)**

| File | Change |
|---|---|
| `Services/LibraryImportService.cs` | New method to copy non-audio files from source to target folder, with skiplist filter (`Thumbs.db`, `.DS_Store`, `desktop.ini`, `*.lnk`) |
| `Views/ImportView.xaml.cs` | Pass `_currentFolderPath` to import service so it knows the source root (currently only track file paths are passed) |

**Complexity:** Low-medium. The track copy loop stays as-is. A separate pass copies non-audio files after tracks are imported.

**Feature 2: Per-file conflict prompt with "apply to all remaining"**

| File | Change |
|---|---|
| `Services/LibraryImportService.cs` | Replace `File.Copy(..., overwrite: true)` with conflict-aware logic; accept a callback or delegate for UI prompts |
| `Views/ImportView.xaml` | New conflict dialog panel (overlay, matching NotificationPanel style) |
| `Views/ImportView.xaml.cs` | Conflict dialog show/hide logic, "apply to all" state tracking |

**Complexity:** Medium. The main challenge is threading — `ImportToLibrary` runs on a background thread but the conflict dialog must show on the UI thread. Needs `Dispatcher.Invoke` or a callback pattern.

**Feature 3: Open Folder buttons**

| File | Change |
|---|---|
| `Views/ImportView.xaml` | Add folder path TextBlock + Open Folder button to bottom bar |
| `Views/ImportView.xaml.cs` | Button click handler: `Process.Start("explorer.exe", _currentFolderPath)` |
| `Views/EditMetadataView.xaml` | Add folder path TextBlock + Open Folder button to status bar area |
| `Views/EditMetadataView.xaml.cs` | Button click handler: `Process.Start("explorer.exe", _show.FolderPath)` |

**Complexity:** Low. Straightforward XAML + 2-line click handlers.

### Blockers and prerequisites

1. **Edit Metadata: managed folder path not displayed.** Must add a TextBlock bound to `_show.FolderPath` before the Open Folder button can be placed adjacent to it.

2. **Import: source folder path not displayed.** Same — must add a TextBlock bound to `_currentFolderPath`.

3. **Threading for conflict dialog.** `ImportToLibrary` runs on a background thread ([ImportView.xaml.cs:1455](Views/ImportView.xaml.cs#L1455)). The conflict dialog must marshal to the UI thread. This is solvable with `Dispatcher.Invoke` but must be designed carefully to avoid deadlocks.

4. **Source folder path availability at import time.** Currently `ImportToLibrary()` receives only `(libraryRoot, albumInfo, tracks)`. The source folder path (`_currentFolderPath`) is NOT passed. Must be added as a parameter for the non-audio file copy.

### Risks and coupling concerns

- **Recursive copy + subfolder structure:** If the source has subfolders (artwork, info, etc.), a flat copy into the target would merge everything. The spec says multi-disc source merging is out of scope, but need to decide: preserve subfolder structure in target, or flatten?

- **File naming collisions for non-audio files:** Audio files get renamed (`01 - SongName (date).flac`). Non-audio files keep their original names. If two source subfolders have a file with the same name (e.g., `cover.jpg`), flattening would cause a collision.

- **Re-import behavior change:** Currently re-import with "Overwrite" replaces all audio files. With the new per-file prompt, user expectations change — they may expect to keep some files and replace others. The existing all-or-nothing `ShowExistsInLibrary` check may need to be softened or removed.

## 8. Open Questions

1. **Should non-audio file copy be recursive into subfolders, or flat (top-level only)?** The source folder may contain subfolders like `artwork/`, `info/`, or disc-specific subfolders. Recursive copy preserves structure but introduces complexity. Flat copy is simpler but may miss nested files.

2. **Should the "Open Folder" button be enabled when no folder is loaded?** In ImportView, before the user selects a folder, `_currentFolderPath` is null. The button should presumably be disabled/hidden until a folder is loaded.

3. **Should the per-file conflict prompt appear for audio files too, or only non-audio files?** Currently audio files are always overwritten. The spec says "re-imports onto an existing managed folder prompt per-file on conflict" — does this apply to the renamed audio files (which would always conflict on re-import) or only to non-audio files being copied for the first time?

4. **What is the exact skiplist?** The prompt mentions `Thumbs.db`, `.DS_Store`, `desktop.ini`, `*.lnk`. Should this be hardcoded or configurable? Are there other common OS junk files to include (e.g., `__MACOSX/`, `.Spotlight-V100`, `ehthumbs.db`)?

5. **Edit Metadata: which path to display when `_show.FolderPaths` has multiple entries?** Multi-folder shows have multiple paths. Should Open Folder open the first? Show a dropdown? This edge case needs a decision.
