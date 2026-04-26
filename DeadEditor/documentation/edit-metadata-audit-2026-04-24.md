# EditMetadataView Audit vs Spec — 2026-04-24

## Summary Table

| # | Feature | Spec Source | Expected | Actual State | Confidence |
|---|---------|------------|----------|--------------|------------|
| 1 | Track Info context menu | feature-parity §5.1 | Present | **Present** | High |
| 2 | Match to Song context menu | feature-parity §3.3 | Present (conditional) | **Present** | High |
| 3 | Date auto-lookup | feature-parity §3.2 | Present | **Present** | High |
| 4 | MBID display field | feature-parity §3.4 | Present | **Present** | High |
| 5 | MB Lookup button | feature-parity §3.6 | Present | **Present** | High |
| 6 | Match Setlist button | feature-parity §3.7 | Present | **Present** | High |
| 7 | Drag-to-reorder | feature-parity §3.8 | Present | **Present** | High |
| 8 | Save writes MBID | feature-parity §3.6 | Present | **Present** | High |
| 9 | Folder path display | source-folder §6 | Present | **Present** | High |
| 10 | Open Folder button | source-folder §7 | Present, identical to Import | **Present — placement differs** | High |

## Per-Feature Findings

### 1. Track Info Context Menu — PRESENT

- **XAML:** `MouseRightButtonUp="TracksDataGrid_MouseRightButtonUp"` at [EditMetadataView.xaml:223](Views/EditMetadataView.xaml#L223)
- **Code-behind:** `TracksDataGrid_MouseRightButtonUp` handler at [EditMetadataView.xaml.cs:872](Views/EditMetadataView.xaml.cs#L872), builds dark-themed context menu with "📄 Track Info" item at line 925. Opens `TrackInfoDialog` at line 928.
- **Matches spec:** Yes — Play Now, Add to Playlist, Track Info (with separator), conditional Match to Song.

### 2. Match to Song Context Menu — PRESENT

- **Code-behind:** Conditional block at [EditMetadataView.xaml.cs:938-953](Views/EditMetadataView.xaml.cs#L938-L953). Gated on `_matchSetlistHasRun`, `clickedTrack.IsMatched != true`, and unclaimed setlist songs existing.
- **Handler:** `MatchToSong_Click` at [EditMetadataView.xaml.cs:993](Views/EditMetadataView.xaml.cs#L993). Opens `MatchToSongDialog`, updates title, learns alias, marks claimed position.
- **Matches spec:** Yes — conditional visibility, no renumbering, auto-alias learning.

### 3. Date Auto-Lookup — PRESENT

- **XAML:** `LostFocus="AlbumDateTextBox_LostFocus"` at [EditMetadataView.xaml:120](Views/EditMetadataView.xaml#L120)
- **Code-behind:** `AlbumDateTextBox_LostFocus` at [EditMetadataView.xaml.cs:818](Views/EditMetadataView.xaml.cs#L818). Validates yyyy-MM-dd format, calls `ShowLookupService.Instance.GetShowByDate(date)`, only fills empty Venue/CityState fields. Also calls `UpdateMatchSetlistButton()`.
- **Matches spec:** Yes — exact behavior per §3.2.

### 4. MBID Display Field — PRESENT

- **XAML:** `MbidDisplayText` TextBlock at [EditMetadataView.xaml:410](Views/EditMetadataView.xaml#L410), with `MbidCopyButton` at line 417. Located below the Album Type dropdown in the artwork panel (right side), under a "MBID" label at line 403.
- **Code-behind:** `RefreshUI()` at [EditMetadataView.xaml.cs:229-241](Views/EditMetadataView.xaml.cs#L229-L241) reads `_show.MusicBrainzReleaseId`, shows value or em-dash, toggles copy button visibility.
- **Copy handler:** `MbidCopyButton_Click` at [EditMetadataView.xaml.cs:860](Views/EditMetadataView.xaml.cs#L860).
- **Matches spec:** Yes — read-only TextBlock with "—" when empty, copy button visible when set.

### 5. MB Lookup Button — PRESENT

- **XAML:** `MusicBrainzButton` at [EditMetadataView.xaml:79-83](Views/EditMetadataView.xaml#L79-L83) in the action bar. Content is "🔎 MusicBrainz", click handler `MusicBrainzButton_Click`.
- **Code-behind:** Full async handler at [EditMetadataView.xaml.cs:1130-1212](Views/EditMetadataView.xaml.cs#L1130-L1212). Implements:
  - Existing MBID check with Yes/No/Cancel prompt (line 1146)
  - Fingerprint path via `LookupAllReleasesAsync` (line 1173)
  - Name search fallback via `SearchReleasesByNameAsync` (line 1188)
  - Shows `MbidCandidateDialog` with `showFieldCheckboxes: true` (line 1248-1254)
  - Applies `MbidApplyResult` fields per checkbox selections (lines 1269-1299)
  - Track title apply via `ApplyMbTrackTitles` (line 1310)
- **Matches spec:** Yes — full implementation per §3.6 and §4.

### 6. Match Setlist Button — PRESENT

- **XAML:** `MatchSetlistButton` at [EditMetadataView.xaml:74-78](Views/EditMetadataView.xaml#L74-L78). Initially `IsEnabled="False"` with tooltip "No setlist data for this date".
- **Code-behind:** `MatchSetlistButton_Click` at [EditMetadataView.xaml.cs:1040](Views/EditMetadataView.xaml.cs#L1040). Flattens setlist, matches by canonical name, stores state for Match to Song, no renumbering.
- **Button enable/disable:** `UpdateMatchSetlistButton()` at [EditMetadataView.xaml.cs:842](Views/EditMetadataView.xaml.cs#L842), called from `RefreshUI` and `AlbumDateTextBox_LostFocus`.
- **Matches spec:** Yes — suggestions-only behavior per §6. No overflow disc, no renumbering.

### 7. Drag-to-Reorder — PRESENT

- **XAML:** `DropIndicator` Border at [EditMetadataView.xaml:344-350](Views/EditMetadataView.xaml#L344-L350). Row style with drag event handlers at lines 270-276 (`PreviewMouseLeftButtonDown`, `MouseMove`, `DragOver`, `Drop`). DataGrid `AllowDrop="True"` at line 224.
- **Code-behind:** Full drag-to-reorder at [EditMetadataView.xaml.cs:1354-1471](Views/EditMetadataView.xaml.cs#L1354-L1471). Cross-disc drag blocked (line 1410). Auto-renumber via `RenumberDisc()` (line 1439).
- **Matches spec:** Yes — exact behavior per §7. Within-disc only, auto-renumber on drop.

### 8. Save Writes MBID — PRESENT

- **Code-behind:** `WriteMbidToTracks()` at [EditMetadataView.xaml.cs:1475-1511](Views/EditMetadataView.xaml.cs#L1475-L1511). Called from `SaveChangesAsync()` at line 505, after `WriteMetadata()`. Handles both FLAC (Xiph `MUSICBRAINZ_ALBUMID`) and MP3 (TXXX `MusicBrainz Album Id`).
- **Matches spec:** Yes — writes MBID to all tracks using standard Picard conventions per MBID foundation §4.

### 9. Folder Path Display — PRESENT

- **XAML:** `FolderPathTextBox` at [EditMetadataView.xaml:457-469](Views/EditMetadataView.xaml#L457-L469). Read-only, transparent background, `#AAAAAA` foreground, `FontSize="13"`, no wrapping.
- **Code-behind:** Set in `LoadData()` at [EditMetadataView.xaml.cs:198-200](Views/EditMetadataView.xaml.cs#L198-L200). Displays first path from `_show.FolderPaths` or `_show.FolderPath`. Default text "(unknown)".
- **Matches spec:** Yes — read-only, selectable text, correct data source.

### 10. Open Folder Button — PRESENT, PLACEMENT DIFFERS FROM IMPORT

- **XAML:** `OpenFolderButton` at [EditMetadataView.xaml:445-456](Views/EditMetadataView.xaml#L445-L456). Uses `&#xE838;` glyph with "Open Folder" label, Segoe MDL2 Assets font, correct icon.
- **Code-behind:** `OpenFolderButton_Click` at [EditMetadataView.xaml.cs:1515](Views/EditMetadataView.xaml.cs#L1515). Uses `Process.Start` with `UseShellExecute = true`.
- **Placement in Edit Metadata:** Grid.Row="3" — **status bar** area (bottom of view), at [EditMetadataView.xaml:436-470](Views/EditMetadataView.xaml#L436-L470).
- **Placement in Import:** Grid.Row="0", Column 1 — **action bar** area (top of view), at [ImportView.xaml:114-139](Views/ImportView.xaml#L114-L139). The folder path + Open Folder button sit in the center of the action bar, between left action buttons and right status.
- **Discrepancy:** Spec §7 says "Immediately adjacent to the folder path display" and "Identical placement relationship in both views." The button IS adjacent to the folder path in both views, but:
  - **Import:** Folder path + Open Folder are in the **top action bar** (Row 0, Column 1), above the album info fields. Folder path is above the status text.
  - **Edit Metadata:** Folder path + Open Folder are in the **bottom status bar** (Row 3, Column 0), below the track grid.
  - The *relative* placement (button next to path) matches. The *absolute* screen position does not — Import has it at the top, Edit Metadata has it at the bottom.
- **Spec says (§6 Edit Metadata):** "In the status bar area (Grid.Row='3')". This is exactly what's implemented. The spec explicitly placed Edit Metadata's path in the status bar, not the action bar. So the implementation matches the spec, but the **spec itself** produced an inconsistency between the two views.

## Gaps Between Report Claims and Actual State

**No gaps found.** All 10 features are present in the actual code. The earlier concern about missing features appears to be unfounded — the implementation is complete and matches the spec.

Specific claims verified:

1. **MBID display field:** Present in `EditMetadataView.xaml` at lines 402-423 (NOT only in `TrackInfoDialog.xaml`). Both locations have it.
2. **MB Lookup button:** Present in both `.xaml` (line 79) AND `.xaml.cs` (line 1130). Full XAML binding + handler.
3. **Match Setlist button:** Present in `.xaml` (line 74) with full handler in `.xaml.cs` (line 1040).
4. **Open Folder button:** Present in `EditMetadataView.xaml` at line 445 (status bar) and in `ImportView.xaml` at line 116 (action bar). Both exist, both work — the visual inconsistency is a spec-level choice, not a missing feature.

## Adjacent File Findings

- **ImportView.xaml Open Folder placement:** Line 116 in Grid.Row="0", Column 1 (action bar, center). Folder path at line 127 in same DockPanel. Status text at line 140 below the path.
- **AlbumDetailView Track Info menu:** **Present.** `TracksDataGrid_MouseRightButtonUp` at [AlbumDetailView.xaml.cs:617](Views/AlbumDetailView.xaml.cs#L617). "📄 Track Info" menu item at line 699, opens `TrackInfoDialog` at line 702.
- **PlaylistPanel Track Info menu:** **Present.** `PlaylistDataGrid_MouseRightButtonUp` at [PlaylistPanel.xaml.cs:127](Views/PlaylistPanel.xaml.cs#L127). "📄 Track Info" menu item at line 162, opens `TrackInfoDialog` at line 165.
- **MbidCandidateDialog checkbox panel:** **Present.** "Fields to apply" panel at [MbidCandidateDialog.xaml:53](Views/MbidCandidateDialog.xaml#L53), with `showFieldCheckboxes` support.

## Likely Root Causes (for initially-reported issues)

The three originally-reported issues ("MBID display field missing", "MB Lookup button missing", "Open Folder placement inconsistency") appear to stem from one of:

1. **Testing with albums that have no MBID:** The MBID display shows "—" in `#888888` foreground, which is very subtle against the `#2D2D30` panel background. Easy to miss visually.
2. **The MBID copy button is `Visibility="Collapsed"` when no MBID is set** (line 421), making the entire MBID section look like just a dim label + dash — could appear to be placeholder rather than an implemented feature.
3. **Open Folder placement inconsistency is by spec design:** The source-folder-preservation spec §6 explicitly placed the Edit Metadata path in the status bar area. The visual difference between Import (top) and Edit Metadata (bottom) is a spec-level choice. Whether this should be harmonized is a UX decision, not a bug.

## Recommended Next Step

**No code changes needed for missing features — all 10 features are implemented.**

If the Open Folder placement inconsistency is a UX concern worth fixing, it would involve moving the folder path + Open Folder button from Grid.Row="3" to Grid.Row="0" in `EditMetadataView.xaml` (adding a Column 1 in the action bar, matching Import's layout). This would be ~20 lines of XAML restructuring, no code-behind changes. Update the source-folder-preservation spec §6 first to reflect the new placement.

Optional polish: make the MBID display more visually prominent when empty (increase label/dash contrast or add a "no MBID" state indicator) to avoid future confusion about whether it's implemented.
