# Source Folder Preservation + Open Folder Buttons

## 1. Goals

- Import preserves the entire contents of a source folder (not just FLACs/MP3s) so EAC logs, text files, artwork, readmes, checksums, and other provenance travel with the recording
- Both Import view and Edit Metadata view display the folder path and provide a one-click "Open Folder" button that reveals it in File Explorer
- Re-import conflicts get per-file choice, not one blanket yes/no
- Users who point Import at a multi-disc parent folder get a clear prompt explaining why it won't import, instead of silent "0 tracks"

## 2. Non-Goals

- Multi-disc source merging (user does this manually in Explorer for now)
- Backfill tool for albums already imported under the old copy-FLACs-only behavior
- Preserving subfolder structure in the managed folder (we flatten)
- Folder rename when `BuildLibraryFolderName()` output changes (out of scope)
- Size limits on copied files
- Symlink handling (default `File.Copy` behavior if encountered; not tested, not guaranteed)

## 3. Import Copy Behavior

### Current behavior (per inspection)

Audio files only (FLAC/MP3) discovered via `Directory.GetFiles()` (non-recursive) at [MetadataService.cs:62-70](Services/MetadataService.cs#L62-L70), copied to the managed folder via `CopyFileWithRetry` (3 retries, 500ms delay, `overwrite: true`) at [LibraryImportService.cs:167-182](Services/LibraryImportService.cs#L167-L182). No non-audio files are copied.

### New behavior

- **Step 1:** Copy audio files into managed folder (unchanged from today — the existing `ImportTracksToFolder` loop at [LibraryImportService.cs:56-147](Services/LibraryImportService.cs#L56-L147) remains as-is)
- **Step 2:** Enumerate source folder **recursively** (`Directory.GetFiles(sourceFolderPath, "*", SearchOption.AllDirectories)`), collect ALL files
- **Step 3:** For each file:
  - If audio file (`.flac`, `.mp3`) that was already copied in Step 1: **skip**
  - If filename matches skiplist (case-insensitive): **skip**
  - Otherwise: copy to managed folder top level (flattened), using existing `CopyFileWithRetry` pattern
- **Step 4:** Report result: "Copied X audio files and Y additional files"

### Skiplist (case-insensitive)

| Pattern | Reason |
|---------|--------|
| `Thumbs.db` | Windows thumbnail cache |
| `.DS_Store` | macOS folder metadata |
| `desktop.ini` | Windows folder customization |
| `*.lnk` | Windows shortcuts |

Matched by filename only (not full path). Extension match (`*.lnk`) uses `Path.GetExtension()`. All comparisons are `StringComparison.OrdinalIgnoreCase`.

### Audio file scope

Import remains flat — `Directory.GetFiles()` non-recursive at [MetadataService.cs:62-70](Services/MetadataService.cs#L62-L70). Recursive behavior applies **ONLY** to the non-audio file copy in Step 2. Audio files outside the top-level source folder are NOT imported.

### Plumbing change

`ImportToLibrary()` at [LibraryImportService.cs:25](Services/LibraryImportService.cs#L25) currently receives `(string libraryRoot, AlbumInfo albumInfo, List<TrackInfo> tracks, IProgress?)`. It does **not** receive the source folder path.

**Change:** Add `string? sourceFolderPath` parameter. When non-null, execute the non-audio copy (Steps 2-4) after `ImportTracksToFolder` returns.

**Call sites to update:**

1. [ImportView.xaml.cs:1457](Views/ImportView.xaml.cs#L1457) — `_libraryImportService.ImportToLibrary(...)` inside `Task.Run`. Pass `_currentFolderPath` (field at [ImportView.xaml.cs:54](Views/ImportView.xaml.cs#L54)).
2. Any other caller of `ImportToLibrary` — search codebase; currently only ImportView calls it.

The parameter is nullable (`string?`) so existing call patterns that don't need non-audio copy can pass `null` with no behavior change.

### Flattening conflicts within one import

If two source files flatten to the same managed-folder filename (e.g., `Disc 1/cover.jpg` and `Disc 2/cover.jpg`), the second occurrence triggers the same conflict prompt used for re-imports (Section 4). The prompt text acknowledges this is a same-import collision:

> "Another file from this import already copied as `{name}`. Overwrite / Skip / Rename / Apply to all remaining."

### Error handling

Individual non-audio file copy failures (permission denied, disk full, etc.) log a warning via `Debug.WriteLine` and continue with the next file. A single bad file does **NOT** abort the entire import. After all files are processed, report any failures in the status message: "Copied X audio files and Y additional files (Z failures)."

## 4. Conflict Prompt (Re-Import)

### Current behavior

Folder-level single Yes/No prompt via `NotificationPanel` overlay at [ImportView.xaml:558-611](Views/ImportView.xaml#L558-L611), triggered at [ImportView.xaml.cs:1418-1429](Views/ImportView.xaml.cs#L1418-L1429). If Yes → all files copied with `overwrite: true` (no per-file prompt). If No → import aborted entirely.

### New behavior

Replace the folder-level Yes/No with per-file prompts. The `ShowExistsInLibrary()` check at [LibraryImportService.cs:424-436](Services/LibraryImportService.cs#L424-L436) still runs — if the folder doesn't exist, no conflict prompts are needed.

When the folder **does** exist, for each file (audio AND non-audio) that already exists in the target managed folder:

**Options:**
- **Overwrite** — replace existing file with source file
- **Skip** — keep existing, do not copy source
- **Rename** — copy source with a suffix (e.g., `cover (2).jpg`; if `(2)` exists, try `(3)`, etc.)
- **Apply to all remaining** — checkbox that persists the chosen action (Overwrite/Skip/Rename) for all remaining conflicts in THIS import session only
- **Cancel Import** — abort the entire import (no further copies; files already copied remain)

### UI approach

Extend the existing `NotificationPanel` pattern at [ImportView.xaml:558-611](Views/ImportView.xaml#L558-L611) with a new variant. The existing panel supports Yes/No/OK buttons but **not** radio buttons or a checkbox.

**Recommended approach:** Create a new `ConflictPanel` overlay in `ImportView.xaml`, visually matching `NotificationPanel` (same dark theme: `#2D2D30` background, `#007ACC` border, `CornerRadius="6"`, `DropShadowEffect` with 30px blur, black overlay at 0.7 opacity). Add:
- Title: "File Already Exists"
- Message: showing filename and size comparison
- Three buttons: Overwrite / Skip / Rename (styled like `NotificationYesButton`/`NotificationNoButton`)
- CheckBox: "Apply to all remaining conflicts" (below buttons)
- Cancel Import link/button (small, below checkbox)

A new `ShowConflictAsync()` method returns a `ConflictAction` enum (`Overwrite`, `Skip`, `Rename`, `Cancel`).

### Threading

`ImportToLibrary` runs on a background thread at [ImportView.xaml.cs:1455](Views/ImportView.xaml.cs#L1455). The conflict dialog must show on the UI thread. Use a callback delegate pattern:

```csharp
// In LibraryImportService
public delegate ConflictAction ConflictPromptCallback(string fileName, long existingSize, long newSize);

public void ImportToLibrary(..., ConflictPromptCallback? onConflict = null)
```

ImportView provides the callback, which internally uses `Dispatcher.Invoke` to marshal to the UI thread:

```csharp
ConflictAction callback(string fileName, long existingSize, long newSize)
{
    return Dispatcher.Invoke(() => ShowConflictAsync(fileName, existingSize, newSize).Result);
}
```

### Session-only persistence

"Apply to all remaining" state lives in a local variable within the callback closure. It does NOT persist to settings or across imports. When the import completes or is cancelled, the state is discarded.

## 5. Multi-Disc Source Guidance

### Detection

After `LoadFolderAsync()` completes at [ImportView.xaml.cs:145](Views/ImportView.xaml.cs#L145), when the track list is empty (line 153: `trackList.Count == 0`), add a secondary check:

- Source folder contains one or more subfolders (`Directory.GetDirectories(folderPath).Length > 0`)

If both conditions hold (zero audio files AND subfolders exist), replace the current notification at [ImportView.xaml.cs:155-156](Views/ImportView.xaml.cs#L155-L156) with:

> "No audio files found in this folder. This folder contains subfolders that may be individual discs. To import, either:
> - Select each disc folder separately and import as separate albums
> - Combine the disc contents into a single folder in File Explorer, then import the combined folder"

### Placement

Replaces the existing `ShowNotificationAsync("No Files", "No audio files (FLAC/MP3) found in the selected folder.")` at [ImportView.xaml.cs:155-156](Views/ImportView.xaml.cs#L155-L156). Uses the same `NotificationPanel` overlay. If the source folder has zero audio files AND zero subfolders, the original message is shown unchanged.

### No action buttons

This is informational only — no "Open in Explorer" shortcut, no "Combine discs for me" button. Just the message with an OK button to dismiss.

## 6. Folder Path Display

### Import view

- **Element:** Read-only `TextBox` (`IsReadOnly="true"`, `BorderThickness="0"`, `Background="Transparent"`) displaying `_currentFolderPath`. Using `TextBox` rather than `TextBlock` because `TextBox` supports native select-and-copy without extra work.
- **Location:** In the action bar (Grid.Row="0") at [ImportView.xaml:79-129](Views/ImportView.xaml#L79-L129). Add the path display in Column 1 (currently holds `StatusTextBlock`). Restructure Column 1 to stack the folder path above the status text, or place the path inline between the left action buttons and the center status. The exact layout: a small `StackPanel` in Column 1 with the path `TextBox` on top and the existing `StatusTextBlock` below.
- **Style:** `Foreground="#AAAAAA"`, `FontSize="13"`, `TextTrimming="CharacterEllipsis"` (via `TextWrapping="NoWrap"`). Monospace not required — match the standard app font.
- **Updates:** Set in `LoadFolderAsync()` after `_currentFolderPath` is assigned. Also set in `LoadFolderFromPath()` at [ImportView.xaml.cs:95-99](Views/ImportView.xaml.cs#L95-L99) and `ImportForConcert()` at [ImportView.xaml.cs:132-140](Views/ImportView.xaml.cs#L132-L140).
- **When no path is set:** Placeholder text: "No folder loaded" (in `#666666` foreground).

### Edit Metadata view

- **Element:** Read-only `TextBox` (same style as Import view) displaying the managed folder path.
- **Data source:** `_show.FolderPath` at [EditMetadataView.xaml.cs:114](Views/EditMetadataView.xaml.cs#L114). For multi-folder albums (`_show.FolderPaths` at [EditMetadataView.xaml.cs:90](Views/EditMetadataView.xaml.cs#L90)), display the first path. The Open Folder button opens whichever path is displayed.
- **Location:** In the status bar area (Grid.Row="3") at [EditMetadataView.xaml:436-451](Views/EditMetadataView.xaml#L436-L451). Restructure Column 0 to show: `[FolderPathTextBox] [Open Folder button] [StatusTextBlock]`. Or add a new row above the status bar. The key constraint is that the folder path and Open Folder button are visually grouped together.
- **Style:** Matches Import view exactly.
- **When no path:** Display "(unknown)" — this state shouldn't occur since the album always has a folder, but handle defensively.

## 7. Open Folder Button

### Placement

Immediately adjacent to the folder path display in each view (to its right, with `Margin="6,0,0,0"` spacing). Identical placement relationship in both views.

### Visual style

- Button content: StackPanel with icon TextBlock + label TextBlock (inline)
  - Icon: `FontFamily="Segoe MDL2 Assets"` glyph `&#xE838;` (OpenLocal), `FontSize="14"`
  - Label: "Open Folder", `FontSize="14"`
- Height: 32px (matches Browse, View Info, and other action bar buttons at [ImportView.xaml:88](Views/ImportView.xaml#L88))
- Background/Foreground: default WPF button style (matches existing action buttons in both views)
- Tooltip: "Open this folder in File Explorer"

### Behavior

Click handler:

```csharp
Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
```

This uses `UseShellExecute = true` which opens the folder with its contents visible (not `/select,` which selects the folder in its parent). This matches the existing `Process.Start` pattern used elsewhere in the codebase (e.g., [AlbumDetailView.xaml.cs:894](Views/AlbumDetailView.xaml.cs#L894), [MbidCandidateDialog.xaml.cs:166](Views/MbidCandidateDialog.xaml.cs#L166)).

If the path no longer exists on disk (e.g., deleted outside the app), catch `Win32Exception` / `InvalidOperationException` and show a brief status message: "Folder not found: {path}" in the `StatusTextBlock`.

### Import view specifics

- Opens `_currentFolderPath` — the **source** folder the user selected for import
- Button disabled when `_currentFolderPath` is null/empty (before any folder is loaded)

### Edit Metadata view specifics

- Opens the **managed** folder (`_show.FolderPath`)
- Button should never be disabled in practice (the album exists), but defensively disable if path is null/empty

## 8. Architecture

### Files to modify

| File | Change | Section |
|------|--------|---------|
| [Services/LibraryImportService.cs](Services/LibraryImportService.cs) | Add `sourceFolderPath` parameter to `ImportToLibrary()`. Add `CopyNonAudioFiles()` private method with skiplist, flattening, and conflict callback. Add `ConflictPromptCallback` delegate and `ConflictAction` enum. | §3, §4 |
| [Views/ImportView.xaml](Views/ImportView.xaml) | Add folder path `TextBox` and Open Folder button to action bar. Add `ConflictPanel` overlay (matching `NotificationPanel` style). | §4, §6, §7 |
| [Views/ImportView.xaml.cs](Views/ImportView.xaml.cs) | Pass `_currentFolderPath` to `ImportToLibrary()`. Wire Open Folder click handler. Implement `ShowConflictAsync()` method. Add multi-disc detection in `LoadFolderAsync()`. Update folder path display on load. | §3, §4, §5, §7 |
| [Views/EditMetadataView.xaml](Views/EditMetadataView.xaml) | Add folder path `TextBox` and Open Folder button to status bar area. | §6, §7 |
| [Views/EditMetadataView.xaml.cs](Views/EditMetadataView.xaml.cs) | Wire Open Folder click handler. Set folder path display in `LoadShow()`. | §7 |

### Files NOT to modify

- MBID foundation files (uncommitted) — unrelated to this change
- Feature parity files (uncommitted) — untouched; the folder path display in Edit Metadata is additive XAML, not a conflict
- `Services/MetadataService.cs` — audio file discovery stays flat; non-audio copy is in `LibraryImportService`
- `Services/MusicBrainzService.cs`, `Services/NormalizationService.cs`, `Services/ShowLookupService.cs` — unrelated
- `ShellWindow.xaml.cs` — no changes needed for folder path display (it's shown in each view, not the shell)

### New files

Likely **none**. The `ConflictPanel` overlay lives in `ImportView.xaml` alongside the existing `NotificationPanel`. The `ConflictAction` enum and `ConflictPromptCallback` delegate live in `LibraryImportService.cs`.

If the conflict overlay becomes too complex to share XAML with ImportView (unlikely — it's ~30 lines of XAML), it could be extracted to a standalone `ConflictDialog.xaml` window. Flag during implementation if this happens.

## 9. Edge Cases

| Scenario | Handling |
|----------|----------|
| Source folder contains only subfolders (no audio, no top-level files) | Show multi-disc guidance message (§5). Do not import. |
| Source folder contains audio AND subfolders with more audio | Import top-level audio only (no change from current). Recursively copy non-audio from all levels (flattened). |
| Source folder is a network share that disconnects mid-import | Individual copy failures log and continue (§3 error handling). If ALL copies fail, surface an error in the status message. |
| File with no extension (e.g., `README`) | No skiplist match on pattern like `*.lnk`; copy it (provenance could include extensionless files). |
| Very large non-audio files (multi-GB video files) | No size limit enforced. They get copied. If this becomes a problem in practice, add a size threshold later. |
| Re-import: audio files always conflict (renamed to `01 - Song.flac`) | Per-file conflict prompt applies to audio files too — they're files in the managed folder that already exist. This replaces the current blanket overwrite behavior. |
| Flattening produces a name collision with an audio file (e.g., source has `01 - Song.flac` in a subfolder) | Audio files in subfolders are skipped in Step 3 (they match the `.flac`/`.mp3` extension filter). Only top-level audio files are imported in Step 1. |
| Multi-folder album in Edit Metadata (`_show.FolderPaths` has multiple entries) | Display the first path. Open Folder opens the first path. No dropdown — this is a rare case and the user can navigate between folders from Explorer. |
| Source folder path contains special characters (Unicode, spaces, ampersands) | No special handling needed — `File.Copy` and `Process.Start` handle these natively on Windows. |

## 10. Testing Plan

### Manual verification checklist (post-implementation)

1. **Basic provenance copy:** Import a folder with FLACs + EAC log + cover.jpg + readme.txt. Confirm managed folder contains all of them.
2. **Skiplist filtering:** Import a folder with FLAC + `Thumbs.db`. Confirm `Thumbs.db` is NOT in managed folder.
3. **Recursive flattening:** Import a folder with nested `scans/front.jpg` and `scans/back.jpg`. Confirm both JPGs are at the top of the managed folder.
4. **Flattening collision:** Import a folder with `Disc 1/cover.jpg` + `Disc 2/cover.jpg`. Confirm conflict prompt appears for the second file.
5. **Re-import per-file conflict:** Re-import an album. Confirm per-file conflict prompt with Overwrite/Skip/Rename options.
6. **Apply to all remaining:** On re-import with many conflicts, check "Apply to all remaining" after first prompt. Confirm remaining conflicts use the chosen action automatically.
7. **Cancel Import mid-conflict:** Click Cancel Import during a conflict prompt. Confirm import stops; files already copied remain.
8. **Multi-disc parent folder:** Point Import at a multi-disc parent folder with no audio at top level. Confirm guidance message appears (not the generic "No audio files" message).
9. **Multi-disc parent with no subfolders:** Point Import at an empty folder. Confirm the original "No audio files" message appears (not the multi-disc guidance).
10. **Open Folder — Import view:** Load a folder in Import, click Open Folder. Confirm Explorer opens at the source folder with contents visible.
11. **Open Folder — Import view disabled state:** Before loading any folder, confirm Open Folder button is disabled.
12. **Open Folder — Edit Metadata:** Open an album in Edit Metadata, click Open Folder. Confirm Explorer opens at the managed folder.
13. **Open Folder — missing folder:** Delete a managed folder from disk, click Open Folder in Edit Metadata. Confirm graceful "Folder not found" message in status bar.
14. **Folder path display — Import view:** Load a folder. Confirm path is displayed and selectable (can copy to clipboard). Load a different folder. Confirm path updates.
15. **Folder path display — Edit Metadata:** Open an album. Confirm managed folder path is displayed and selectable.
16. **Full re-import cycle:** Reset the library, re-import test albums. Confirm EAC logs and other provenance files all land in managed folders alongside audio.

### No unit tests

Test project does not exist. Flagged in prior inspections, still deferred.

## 11. Out of Scope / Future Work

- Multi-disc source merging (automated or prompted)
- Size limits on copied files
- Symlink handling
- Folder rename when `BuildLibraryFolderName()` changes
- Backfill tool for pre-existing imports (unnecessary given user's intact source data)
- "Open source folder" in Edit Metadata (only managed folder is shown there)
- Editing / moving the managed folder from Edit Metadata
- Configurable skiplist (hardcoded is sufficient for now)
- Subfolder structure preservation in managed folder (we flatten deliberately)
