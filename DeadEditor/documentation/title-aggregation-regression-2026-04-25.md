# Title Aggregation Regression Diagnostic — 2026-04-25

## 1. Aggregation logic location

There are **two** display title computation paths:

### A. TrackInfo.DisplayTitle (computed property)
**File:** `Models/TrackInfo.cs:133-157`

A read-only computed property on the model itself. Reconstructs from components: `SongName + " >" (if Segue) + " (date)"` where date = TrackDate ?? AlbumDate. Fires `OnPropertyChanged(nameof(DisplayTitle))` when SongName (line 55), TrackDate (line 81), or Segue (line 96) change.

### B. TrackInfoViewModel.UpdateDisplayTitle()
**File:** `Models/TrackInfoViewModel.cs:134-161`

A method that sets a backing `_displayTitle` field. Has two branches:

- **Edit mode** (`_isEditMode == true`, line 136-139): Sets `DisplayTitle = Track.SongName ?? Track.Title ?? ""` — **no date appended, no segue appended**.
- **Import mode** (`_isEditMode == false`, line 141-159): Builds `"{songName} ({effectiveDate})"` with segue marker.

Called from: constructor (line 26), `Track_PropertyChanged` handler (lines 37, 41), and proxy property setters for SongName (line 106), Segue (line 117), TrackDate (line 128).

## 2. Track model

### TrackInfo (`Models/TrackInfo.cs`)
- Implements `INotifyPropertyChanged` (line 6)
- Key properties with backing fields and INPC: `SongName` (line 46), `RawTitle` (line 60), `TrackDate` (line 72), `Segue` (line 86), `DiscNumber` (line 18), `IsMatched` (line 104)
- Simple properties (no INPC): `FilePath`, `FileName`, `TrackNumber`, `Duration`, `IsModified`, `AlbumDate`
- Computed properties:
  - `DisplayTitle` (line 133) — combines SongName + Segue + (TrackDate ?? AlbumDate)
  - `Title` (line 125) — legacy alias for RawTitle ?? SongName
  - `GetDisplayTitle(albumDate)` (line 160) — method variant with explicit album date param
  - `GetFinalMetadataTitle(albumDate)` (line 179) — for file writing
  - `SegueDisplay` (line 175) — ">" or ""
  - `DisplayTrackNumber` (line 33) — disc-aware format "107", "203"

### TrackInfoViewModel (`Models/TrackInfoViewModel.cs`)
- Implements `INotifyPropertyChanged` (line 11)
- Wraps a `TrackInfo Track` and an `AlbumInfo _albumInfo`
- Has `_isEditMode` flag (line 15) set at construction
- Proxy properties: `TrackNumber`, `DiscNumber`, `SongName`, `Segue`, `TrackDate`, `Duration`, `DisplayTrackNumber`
- Computed: `DisplayTitle` (backed by `_displayTitle`), `InheritedDate` (backed by `_inheritedDate`)
- Subscribes to `Track.PropertyChanged` to relay changes (line 30)

## 3. Edit Metadata grid binding

### Title column binding
**File:** `Views/EditMetadataView.xaml:289-290`
```xml
<DataGridTextColumn Header="Title" Width="Auto" MinWidth="250"
                    Binding="{Binding RawTitle, Mode=TwoWay, UpdateSourceTrigger=LostFocus}">
```
Binds directly to `TrackInfo.RawTitle`. This is the raw FLAC tag value. **Not** bound to `DisplayTitle` or `SongName`.

### Date column binding
**File:** `Views/EditMetadataView.xaml:322-323`
```xml
<DataGridTextColumn Header="Date" Width="Auto" MinWidth="100"
                    Binding="{Binding TrackDate, Mode=TwoWay, UpdateSourceTrigger=LostFocus}">
```
Binds directly to `TrackInfo.TrackDate`.

### ItemsSource
**File:** `Views/EditMetadataView.xaml.cs:189`
```csharp
TracksDataGrid.ItemsSource = _tracks;
```
Where `_tracks` is `List<TrackInfo>` (line 32). **Not** `TrackInfoViewModel` — the DataGrid binds directly to `TrackInfo` objects.

### Edit handlers
- `TracksDataGrid_CellEditEnding` (line 682-694): Sets `_hasUnsavedChanges = true` and `track.IsModified = true`. Does **not** call `ReconstructRawTitles()` or any display update.
- `TracksDataGrid_BeginningEdit` (line 677-680): Empty — no blocking logic.
- `Track_PropertyChanged` (line 658-675): Tracks unsaved changes for Segue, SongName, RawTitle, TrackDate, DiscNumber. Does **not** update display.

### Key observation
The Edit Metadata grid does **not** use `TrackInfoViewModel` at all. It binds directly to `TrackInfo` objects. The Title column shows `RawTitle`, not `DisplayTitle`. When the user edits `RawTitle` directly, `SongName` and `TrackDate` are **not** updated — they are separate properties. The aggregated `"Title (date)"` format is only reconstructed by `ReconstructRawTitles()` which is called from Normalize, MatchSetlist, and MatchToSong — but **never** from manual cell edits.

## 4. Import view comparison

**Different code path entirely.** ImportView uses `TrackInfoViewModel` wrappers.

### ImportView bindings (`Views/ImportView.xaml`)
- Title column (line 396): Display mode binds to `{Binding DisplayTitle}` (read-only TextBlock)
- Title column (line 414): Edit mode binds to `{Binding SongName, UpdateSourceTrigger=PropertyChanged}` (editable TextBox)
- Date column (line 442-451): Display shows `TrackDate` with fallback to `InheritedDate` (italic); edit mode binds to `TrackDate`

### ImportView ItemsSource (`Views/ImportView.xaml.cs:54,88,256`)
```csharp
private ObservableCollection<TrackInfoViewModel> _tracks = new();
// ...
_tracks.Add(new TrackInfoViewModel(track, _albumInfo, isEditMode: false));
```

Uses `TrackInfoViewModel` with `isEditMode: false`, so `UpdateDisplayTitle()` builds the aggregated `"Song (date)"` format. When the user edits `SongName`, the ViewModel proxy setter calls `UpdateDisplayTitle()` which recalculates and fires PropertyChanged for `DisplayTitle`, and the read-only TextBlock updates.

**Import does NOT have this regression** because it uses the ViewModel layer with reactive `DisplayTitle`.

## 5. Recent changes (git diff in working tree)

### TrackInfoViewModel.cs
Only change: added `nameof(TrackInfo.Segue)` to the `Track_PropertyChanged` filter (was previously only `SongName`). This is a fix, not a regression source.

### EditMetadataView.xaml
Added: MatchSetlist button, MusicBrainz button, drag-to-reorder RowStyle, MBID display panel, folder path + Open Folder in status bar, context menu handler, AllowDrop. **No changes to the Title or Date column bindings.**

### EditMetadataView.xaml.cs
Major additions: MatchSetlist, MusicBrainz lookup, MBID write, context menu, drag-to-reorder, date auto-lookup. **No changes to `CellEditEnding` or the core data loading/binding approach.**

### TrackInfo.cs
No working tree changes.

## 6. Root cause hypothesis

### Primary: EditMetadataView bypasses the ViewModel layer and binds Title to `RawTitle` instead of a reactive computed property

**Evidence:**
- `EditMetadataView.xaml:289` binds Title to `{Binding RawTitle}` — a simple string property.
- `EditMetadataView.xaml.cs:189` sets `ItemsSource = _tracks` where `_tracks` is `List<TrackInfo>`, not `List<TrackInfoViewModel>`.
- When the user manually edits the Title cell, they edit `RawTitle` directly. This does **not** trigger any recalculation of the aggregated format.
- `RawTitle`'s setter (TrackInfo.cs:62-70) fires `PropertyChanged(nameof(RawTitle))` but does **not** update `SongName` or `TrackDate`.
- The aggregated format `"Title (date)"` is only rebuilt by `ReconstructRawTitles()` (EditMetadataView.xaml.cs:701-718), which is only called from `NormalizeButton_Click`, `MatchSetlistButton_Click`, `MatchToSong_Click`, and `ApplyMbTrackTitles` — **never** from manual cell edits.

The design choice to show `RawTitle` in edit mode was intentional (commit `bd3815f: "fix: Edit Metadata shows raw FLAC titles without transformation"`), but the consequence is that editing the Title cell doesn't produce the aggregated display format unless one of the batch operations is run afterward.

### Secondary: Editing the Date column doesn't propagate to RawTitle either

When the user edits the Date column (bound to `TrackDate`), `TrackDate` updates and fires PropertyChanged, but `RawTitle` is **not** recalculated. The grid still shows the old `RawTitle` value without the new date. Only `ReconstructRawTitles()` merges `SongName + Segue + TrackDate` back into `RawTitle`.

### Tertiary: No bidirectional parsing of RawTitle into SongName + TrackDate

When `RawTitle` is edited by the user, there is no logic to re-parse it into `SongName` and `TrackDate` components. The `ParseTitleAndDate` method exists in `MetadataService` but is only called during Normalize (EditMetadataView.xaml.cs:740). This means manual title edits and manual date edits are disconnected from each other.

## 7. Recommended fix direction

Two viable approaches (not mutually exclusive):

### Option A: Adopt TrackInfoViewModel in EditMetadataView (recommended)
Wrap tracks in `TrackInfoViewModel(track, albumInfo, isEditMode: true)` just like ImportView does. Change the XAML to use template columns with `DisplayTitle` for display and `SongName` for editing (matching ImportView's pattern). This provides reactive updates automatically.

However, `UpdateDisplayTitle()` currently strips the date in edit mode (line 138-139: `DisplayTitle = Track.SongName`). That branch would need to be changed to include the date suffix even in edit mode, or the `_isEditMode` flag should be set to `false` for EditMetadata (since the desired display is the same aggregated format).

### Option B: Add ReconstructRawTitles() call after cell edits
In `TracksDataGrid_CellEditEnding`, after a commit, parse the edited `RawTitle` back into `SongName` + `TrackDate` (using `MetadataService.ParseTitleAndDate`), then call `ReconstructRawTitles()` and `TracksDataGrid.Items.Refresh()`. This is simpler but more fragile.

### Surgical scope
- Option A touches: `EditMetadataView.xaml` (column bindings), `EditMetadataView.xaml.cs` (change `_tracks` type, wrap in ViewModel), `TrackInfoViewModel.cs` (fix edit mode branch of `UpdateDisplayTitle`)
- Option B touches: `EditMetadataView.xaml.cs` (modify `CellEditEnding`)

## 8. Open questions

1. **Is the "raw FLAC title" display in edit mode intentional UX?** Commit `bd3815f` specifically chose to show raw titles. If so, the aggregated `"Song (date)"` format is only expected after Normalize. The user may be expecting import-like behavior that was never implemented in edit mode.

2. **Should editing the Title cell in edit mode parse back into SongName + TrackDate?** Currently they are disconnected. If the user types "Dark Star (1972-04-08)" into RawTitle, neither SongName nor TrackDate is updated.

3. **Is the `_isEditMode` flag in TrackInfoViewModel designed for this use case?** It was created to strip dates in edit mode (line 138), but the user's expectation seems to be the opposite — they want to see dates.
