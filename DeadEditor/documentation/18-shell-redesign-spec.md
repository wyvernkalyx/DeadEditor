# 18 — Shell Redesign Spec

## Overview

Replace DeadEditor's multi-window architecture with a single-window shell. All views (Library, Import, Edit Metadata, Settings) become UserControls that swap within one main content area. The player bar and playlist are permanently docked at the bottom. No more separate windows, no more window management bugs.

**Core Principles:**
- One window to rule them all — no separate windows for any feature
- Sidebar navigation with icons (Library, Import, Settings)
- Drill-in navigation for contextual views (Library → Album → Edit)
- Player bar permanently docked at bottom — always visible, never lost
- Playlist always visible between content and player bar
- PlaybackService singleton unchanged — already decoupled from UI

---

## Motivation

The current multi-window architecture (LibraryBrowserWindow, MainWindow, PlayerWindow, PlaylistWindow, VisWindow, SettingsWindow, plus multiple dialogs) causes persistent bugs:
- Player window disappears when closed via X (window destroyed, toggle can't recover)
- Z-order issues (playlist/visualizer fall behind player when dragging)
- Minimize state not handled correctly across window groups
- ShutdownMode conflicts (closing player triggers app exit)
- Window state synchronization is fragile and bug-prone

A single-window shell eliminates all of these permanently.

---

## Shell Structure

```
┌──┬──────────────────────────────────────────────────┐
│  │  Header Bar (breadcrumb + context actions)        │
│  ├──────────────────────────────────────────────────┤
│S │                                                    │
│I │                                                    │
│D │           Main Content Area                        │
│E │     (swaps between views/UserControls)             │
│B │                                                    │
│A │                                                    │
│R │                                                    │
│  ├──────────────────────────────────────────────────┤
│  │  Playlist (always visible, collapsible)           │
│  ├──────────────────────────────────────────────────┤
│  │  Player Bar (always visible, never changes)       │
└──┴──────────────────────────────────────────────────┘
```

---

## Section 1: Sidebar

**Layout:** Vertical strip, fixed width (~52px), left edge of shell.

**Icons (top to bottom):**
1. Library (book icon) — shows the library grid view
2. Import (download arrow icon) — shows the import/new folder view
3. (spacer/flex)
4. Settings (gear icon) — shows settings view

**Behavior:**
- One icon is always highlighted (active view context)
- Clicking an icon switches the main content area to that view
- Library icon stays highlighted when drilled into Album or Edit views (you're still "in" Library)
- Import icon highlighted only when in the Import view
- Clicking Library while in Album or Edit view returns to the Library grid (not back one step — that's what the header back button does)

**Visual:**
- Active icon: filled/highlighted background (e.g., blue tint)
- Inactive icons: muted stroke color
- Hover: subtle background highlight
- Optional: "DEAD EDITOR" text rotated vertically at bottom of sidebar

---

## Section 2: Header Bar

**Layout:** Horizontal bar, full width of content area, above the main content.

**Content depends on current view:**

### Library Grid View
```
Library    3 of 15 concerts          [Show: All ▼]  [Search: ___________]  [Advanced Search]
```
- Title: "Library"
- Concert count (shows "X of Y concerts" when filtered, "Y concerts" when unfiltered)
- Show type filter dropdown: All, Official Releases, Audience Recordings, By Date
- Search field: case-insensitive partial match against Date, Venue, City, State, Location, AlbumName, OfficialRelease, Edition, ReleaseYear, and track/song titles (cached on LibraryShow.TrackTitles at load time)
- Clear button (✕) appears when search has text
- Placeholder text "Search library..." when empty
- Search and type filter combine (both applied together)
- Advanced Search button → opens AdvancedSearchDialog modal

### Album Detail View
```
[← Library]  Blues for Allah 50th Anniversary    Official Release          [Edit Metadata]  [Delete]
```
- Back button: "← Library" (returns to library grid)
- Album name (bold)
- Album type badge
- "Edit Metadata" button (navigates to edit view)
- "Delete" button (red/danger styled) — confirmation dialog, sends files to Recycle Bin, removes from library, navigates back to grid

### Edit Metadata View
```
[← Blues for Allah 50th Anniversary]    Editing Metadata           [Save Changes]  [Cancel]
```
- Back button: "← {Album Name}" (returns to album detail, discarding changes)
- Context label: "Editing Metadata"
- Save Changes button (green/primary)
- Cancel button

### Import View
```
Import    [Select Folder…]  /path/to/folder              [MusicBrainz]  [Normalize]  [Renumber]  [Import to Library]
```
- Title: "Import"
- Folder selection
- Action buttons (same as current MainWindow action bar)

### Settings View
```
Settings
```
- Title: "Settings"
- No action buttons

---

## Section 3: Main Content Area

**Layout:** Fills all remaining space between header and playlist. Scrollable.

This area swaps between UserControls based on navigation. Only ONE view is visible at a time.

### View: Library Grid
**Source:** Converted from LibraryBrowserWindow's DataGrid and search logic.

**Content:**
- DataGrid with fixed columns (same layout for all album types):
  - **By Album mode** (default): Date | Album Name | Venue | City, State | Tracks
  - **By Date mode**: Date | Venue | City, State | From Album | Tracks
- "By Date" mode explodes multi-date albums into one row per concert date
  - Dates extracted from track title suffixes (e.g., "Dark Star (1972-05-04)")
  - Track count shows tracks for that specific date, not the whole album
  - "From Album" shows the parent album name
  - Sorted chronologically (oldest first)
- Click/double-click a row → navigates to Album Detail view (in By Date mode, navigates to parent album)

**Data:** Loaded from library scan on startup, same as current LibraryBrowserWindow.

### View: Album Detail
**Source:** Converted from LibraryBrowserWindow's concert detail panel.

**Content:**
- Album art (left side, ~140-180px)
- Album metadata summary (Artist, Release Year, Track count, Dates)
- Tracks grouped by date, each date section with:
  - Date header + track count + "Add All" button
  - Track list: # | Title | Duration
  - Tracks are clickable (double-click to play, right-click for context menu)
- Collapsible date sections (triangle toggle)

**Navigation:**
- Reached by clicking a row in Library Grid
- "← Library" in header bar returns to Library Grid
- "Edit Metadata" in header bar navigates to Edit Metadata view

### View: Edit Metadata
**Source:** Converted from MainWindow in edit mode.

**Content:**
- Album info bar: Artist | Concert Date | Venue | City, State | Album/Release Name | Release Year (all editable TextBoxes)
- Track grid: # | Disc | Title | Segue (→) | Date | Time
  - **Title column:** Displays `RawTitle` (exact FLAC TITLE tag value). Double-click to edit directly.
  - **Editable columns:** Title (double-click to edit RawTitle), Date (double-click), Disc (double-click), Segue (checkbox, single-click toggle)
  - **Read-only columns:** Track # (display only), Time (computed from audio file)
- All fields populated from LibraryShow data (album-level) and FLAC tags (track-level)
- Edit mode behavior: read and display as-is, no transforms
- Normalize button: cleans raw FLAC titles via ParseTitleAndDate, runs NormalizeAll, then reconstructs RawTitle from normalized SongName + Segue + TrackDate so the grid shows the corrected result.
- Renumber button: renumbers tracks using disc-aware 101/201/301 convention
- Change tracking: any edit to album fields or track cells sets `_hasUnsavedChanges`
- Album Type display in artwork panel area

**Save Changes behavior:**
- Writes all track-level tags (TITLE, TRACKNUMBER, DISCNUMBER, DATE) per track
- Writes album-level tags (ARTIST, ALBUM, YEAR) to every FLAC file
- Writes custom FLAC Xiph fields (ALBUMDATE, VENUE, CITYSTATE, ALBUMNAME, ALBUMTYPE)
- Updates LibraryShow in-place (no library re-scan needed)
- Navigates back to Album Detail on success
- Shows error message and stays on Edit view on failure

**Cancel behavior:**
- If unsaved changes exist: confirmation dialog "Discard changes?" [Yes/No]
- Discards all in-memory edits and navigates back to Album Detail

**Navigation:**
- Reached by clicking "Edit Metadata" from Album Detail view
- "← {Album Name}" in header bar returns to Album Detail (discards unsaved changes, with confirmation dialog)
- "Save Changes" writes to FLAC files and database, then returns to Album Detail
- "Cancel" discards changes and returns to Album Detail

### View: Import
**Source:** Converted from MainWindow in import mode.

**Content:**
- Same as current MainWindow: folder selection, album info bar, track grid, artwork panel
- Full import pipeline: Read → MusicBrainz → Normalize → Renumber → Write/Import
- All buttons active (this is import mode, not edit mode)

**Navigation:**
- Reached by clicking Import icon in sidebar
- After successful import: option to view the imported album in Library
- Cancel returns to Library (or stays on Import with empty state)

### View: Settings
**Source:** Converted from SettingsWindow.

**Content:**
- All current settings: folder paths, fpcalc.exe path, preferences
- Displayed as a scrollable form within the main content area

**Navigation:**
- Reached by clicking Settings icon in sidebar
- Changes saved immediately or via explicit Save button

---

## Section 4: Playlist Panel

**Layout:** Horizontal strip between main content area and player bar. Always visible.

**Header row:**
```
Playlist    29 tracks                                                    [Clear]
```

**Content:**
- Compact track list: # | Title | Duration
- Currently playing track highlighted
- Scrollable if many tracks
- Height: ~80-100px default, collapsible to just the header row (toggle arrow)
- Drag to reorder (future enhancement)

**Behavior:**
- Double-clicking a track in any view adds it to playlist and starts playing
- "Add All" button on date sections appends all tracks from that date
- Clear button empties the playlist
- Playlist persists across view switches (it's always there)
- Playlist saves to settings on app close, restores on launch

---

## Section 5: Player Bar

**Layout:** Horizontal strip at the very bottom of the shell. Fixed height (~50px). Always visible.

**Content (left to right):**
```
[⏮] [▶] [⏭]   Track Name (date)   ──●────── 1:24 / 7:23   🔊━━━
```

- Transport controls: Previous, Play/Pause, Next
- Track title (scrolling marquee for long titles)
- Progress bar (seekable)
- Current time / Total time
- Volume control

**Behavior:**
- Always shows current playback state
- Continues playing through all view switches
- PlaybackService singleton drives all state (no change from current architecture)
- No Stop button (Pause is sufficient; stop = pause + seek to 0 if desired)

---

## Navigation Model

### Navigation Stack

The shell maintains a simple navigation stack for the Library context:

```
Library Grid → Album Detail → Edit Metadata
```

Back navigation pops the stack. Clicking the Library sidebar icon resets to Library Grid.

### State Preservation

- **Library Grid:** Scroll position and selection preserved when drilling into Album Detail and coming back
- **Album Detail:** Loaded from LibraryShow data; no state to preserve beyond what's in the data model
- **Edit Metadata:** Unsaved changes prompt a confirmation dialog on back navigation
- **Import:** State preserved while switching to Library and back (folder selection, track data stay loaded)

### Sidebar vs Back Button

- **Sidebar icons** switch top-level views (Library context, Import, Settings)
- **Header back button** navigates within a context (Album → Library, Edit → Album)
- Clicking a sidebar icon while deep in a drill-in (e.g., in Edit Metadata) goes to that top-level view. If returning to Library, it goes to Library Grid, not the last drill-in level.

---

## Dialogs

Some existing dialogs remain as modal dialogs (they're small, focused interactions):

- **AddSongDialog** — modal, opened from normalization unmatched songs
- **ManageSongsDialog** — modal, opened from settings or menu
- **ReleaseSelectorDialog** — modal, opened from MusicBrainz results
- **AlbumSearchDialog** — modal, opened from advanced search
- **AdvancedSearchDialog** — modal, opened from Library header

These do NOT become views in the shell. They stay as modal dialogs that overlay the shell.

---

## Removed Components

After shell implementation, these are deleted:

- **PlayerWindow.xaml/.cs** — replaced by Player Bar in shell
- **PlaylistWindow.xaml/.cs** — replaced by Playlist Panel in shell
- **VisWindow.xaml/.cs** — removed (deferred to future enhancement)
- **LibraryBrowserWindow.xaml/.cs** — replaced by Library Grid + Album Detail UserControls
- **MainWindow.xaml/.cs** — replaced by Import + Edit Metadata UserControls

The **SettingsWindow** is also removed and replaced by a Settings UserControl.

---

## New Components

### ShellWindow.xaml
The single application window. Contains:
- Grid layout: Sidebar | (Header + Content + Playlist + Player)
- ContentPresenter or Frame for swapping views
- Navigation logic (stack-based)

### UserControls (one per view)
- **LibraryGridView.xaml** — DataGrid, search, filters
- **AlbumDetailView.xaml** — Album art, metadata, grouped track lists
- **EditMetadataView.xaml** — Album info bar, editable track grid, artwork panel
- **ImportView.xaml** — Folder selection, track grid, action buttons, artwork panel
- **SettingsView.xaml** — Settings form
- **PlaylistPanel.xaml** — Compact track list, clear button, collapse toggle
- **PlayerBar.xaml** — Transport controls, progress bar, volume

### Shared Services (unchanged)
- **PlaybackService** — singleton, audio playback
- **MetadataService** — FLAC tag reading/writing
- **NormalizationService** — song name normalization
- **MusicBrainzService** — metadata lookup
- **LibraryImportService** — library scanning

---

## Implementation Strategy

### Phase 1: Shell Scaffold
- Create ShellWindow with sidebar, header, content area, playlist panel, player bar
- Wire up sidebar navigation to swap between placeholder UserControls
- PlaybackService integration in PlayerBar
- Playlist panel with basic functionality
- App startup creates ShellWindow instead of LibraryBrowserWindow

### Phase 2: Library Views
- Convert LibraryBrowserWindow grid logic into LibraryGridView UserControl
- Convert concert detail panel into AlbumDetailView UserControl
- Wire drill-in navigation (grid row click → album detail)
- Wire back navigation (header back button → grid)

### Phase 3: Import View
- Convert MainWindow import mode into ImportView UserControl
- All import pipeline logic preserved
- Wire sidebar navigation

### Phase 4: Edit Metadata View
- Convert MainWindow edit mode into EditMetadataView UserControl
- Receives LibraryShow data from Album Detail navigation
- Wire "Edit Metadata" button → Edit view, Save/Cancel → back to Album

### Phase 5: Settings + Cleanup
- Convert SettingsWindow into SettingsView UserControl
- Delete all old Window files
- Update CLAUDE.md documentation lookup table
- Update all documentation files

### Phase 6: Polish + Search/Filter (COMPLETED)
- Library grid search: real-time text filtering by Date, Venue, City/State, AlbumName, OfficialRelease, Year, Edition
- Library grid type filter: dropdown for All / Official Releases / Audience Recordings
- Combined search + type filter with "X of Y concerts" display
- Advanced Search button wired to AdvancedSearchDialog
- Keyboard shortcuts:
  - **Space**: Play/Pause toggle (global, works from any view when not typing)
  - **Ctrl+F**: Focus the search box in Library Grid header
  - **Escape**: Clear search text, or navigate back one level
  - **Delete**: Remove selected track from playlist
- UI polish: dark-themed context menus, visual separators between sections, cursor feedback on clickable rows
- State preservation on navigation
- Window title updates based on current view

---

## WPF Implementation Notes

### View Switching Pattern

Use a ContentControl in ShellWindow with DataTemplate selection, or simply swap UserControl instances in a Grid:

```xml
<Grid>
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="52"/>  <!-- Sidebar -->
        <ColumnDefinition Width="*"/>   <!-- Content -->
    </Grid.ColumnDefinitions>
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto"/>  <!-- Header -->
        <RowDefinition Height="*"/>     <!-- Main Content -->
        <RowDefinition Height="Auto"/>  <!-- Playlist -->
        <RowDefinition Height="Auto"/>  <!-- Player Bar -->
    </Grid.RowDefinitions>

    <local:SidebarPanel Grid.Column="0" Grid.RowSpan="4"/>
    <local:HeaderBar Grid.Column="1" Grid.Row="0"/>
    <ContentPresenter Grid.Column="1" Grid.Row="1"
                      Content="{Binding CurrentView}"/>
    <local:PlaylistPanel Grid.Column="1" Grid.Row="2"/>
    <local:PlayerBar Grid.Column="1" Grid.Row="3"/>
</Grid>
```

### Navigation Service

Create a simple NavigationService that manages the view stack:

```csharp
public class NavigationService
{
    private Stack<UserControl> _backStack = new();
    public UserControl CurrentView { get; private set; }

    public void NavigateTo(UserControl view) { ... }
    public void GoBack() { ... }
    public void NavigateToRoot(UserControl view) { _backStack.Clear(); ... }
}
```

### Data Flow Between Views

- Library Grid → Album Detail: pass the `LibraryShow` object
- Album Detail → Edit Metadata: pass the `LibraryShow` object
- Edit Metadata → Album Detail (on save): pass updated `LibraryShow`
- Import → Library (after import): trigger library refresh

---

## Success Criteria

1. **Single window** — only one Window in the entire application (plus modal dialogs)
2. **No window management bugs** — no lost windows, no z-order issues, no minimize/restore bugs, no shutdown cascades
3. **Player always accessible** — player bar visible on every view, playback never interrupted by navigation
4. **Playlist always visible** — can see and manage the queue from any view
5. **Drill-in navigation works** — Library → Album → Edit with back buttons, no confusion
6. **Import works** — full import pipeline functional in the Import view
7. **Edit works** — edit mode displays FLAC tags as-is, save writes back correctly
8. **Settings accessible** — all settings available in Settings view
9. **Performance** — view switching is instantaneous (no window creation overhead)
10. **All existing features preserved** — search, advanced search, MusicBrainz, normalization, renumbering, playback, playlist management
