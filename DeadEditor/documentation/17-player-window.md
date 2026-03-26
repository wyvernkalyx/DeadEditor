# Player Window — Winamp-Style Global Playback

## Overview

A single global playback system replacing the current fragmented controls.
Consists of three components: PlaybackService (singleton), PlayerWindow, and
PlaylistWindow. A VIS (visualizer) window is stubbed for future use.

---

## PlaybackService

Singleton registered at app startup in App.xaml.cs.

**Exposes:**
- `CurrentTrack` (TrackInfo)
- `PlaybackState` (Playing / Paused / Stopped)
- `Position` (TimeSpan)
- `Duration` (TimeSpan)
- `Playlist` (ObservableCollection<TrackInfo>)

**Methods:** `Play(TrackInfo)`, `Pause()`, `Stop()`, `Next()`, `Previous()`, `Seek(TimeSpan)`, `LoadPlaylist(IEnumerable<TrackInfo>)`

**Events:** `TrackChanged`, `PlaybackStateChanged`, `PositionChanged`

All NAudio logic lives here. No other class instantiates an audio player.

---

## Gapless Auto-Advance

**Feature:** When a track finishes playing naturally, the player automatically advances to the next track in the playlist with minimal gap.

**Implementation:**

The auto-advance logic is implemented in `AudioPlayerService.OnPlaybackStopped()`:

1. **Detect natural end-of-track:**
   - Uses `_userInitiatedStop` flag to distinguish user-initiated stop from natural end
   - Flag is set to `true` in `Stop()` method before calling `_wavePlayer.Stop()`
   - Flag is checked in `OnPlaybackStopped()` handler along with `e.Exception == null`
   - Natural end = `!_userInitiatedStop && e.Exception == null`

2. **Auto-advance logic:**
   ```csharp
   if (wasNaturalEnd && _currentTrackIndex < _playlist.Count - 1)
   {
       // Auto-advance to next track
       Next();
   }
   else
   {
       // End of playlist or user-initiated stop
       SetPlaybackState(PlaybackState.Stopped);
       PlaybackStopped?.Invoke(this, EventArgs.Empty);
   }
   ```

3. **Behavior:**
   - **Natural end + next track exists:** Calls `Next()` → loads next file, stays in Playing state, play button remains ⏸
   - **Natural end + last track:** Transitions to Stopped, play button returns to ▶
   - **User clicks stop button:** Transitions to Stopped immediately, NO auto-advance

**Gap Duration:**
~50-200ms between tracks (time to dispose old `AudioFileReader`, create new reader, load next file). This is acceptable for most users but not truly gapless due to NAudio architecture (`AudioFileReader` does not support seamless chaining).

**NAudio Architecture:**
- **Output:** `WaveOutEvent` (event-based, good for WPF)
- **Reader:** `AudioFileReader` (MediaFoundation-based, must create new instance per track)
- True gapless would require custom `ISampleProvider` or `ConcatenatingSampleProvider` (significant refactoring)

---

## PlayerWindow Layout

Chromeless WPF Window (no standard title bar). Compact fixed-width panel.

```
┌─────────────────────────────────────────────┐
│ ≡  [drag area]                      [_] [×] │  <- thin drag bar, minimize, close
│  «   ▶/⏸   ■   »  │ PL │ VIS │            │  <- transport + PL/VIS toggles
│ ████████████████████░░░░  0:00 / 4:32       │  <- seek bar + elapsed/total time
│ ┌──────────────────────────────────────┐    │
│ │  Dark Star (1969-02-27) — Fillmore   │◄── │  <- scrolling marquee
│ └──────────────────────────────────────┘    │
│ VOL: ────●──────────  BAL: ─────●────       │  <- volume + balance sliders
└─────────────────────────────────────────────┘
```

**Transport button symbols (Segoe MDL2 Assets icon font):**

| Button | Symbol | Glyph Code | FontSize | Notes |
|--------|--------|------------|----------|-------|
| Previous | &#xE892; (previous track) | U+E892 | 14 | Segoe MDL2 Assets |
| Play/Pause (toggle) | &#xE768; / &#xE769; | U+E768 / U+E769 | 16 | Single button that changes glyph based on playback state |
| Stop | &#xE71A; (stop) | U+E71A | 14 | Segoe MDL2 Assets |
| Next | &#xE893; (next track) | U+E893 | 14 | Segoe MDL2 Assets |

All transport buttons use **Segoe MDL2 Assets** font family for consistent icon rendering.
Button labels use standard UI font.

**Play/Pause Toggle Button:**
- Single button that toggles between Play (U+E768) and Pause (U+E769) glyphs
- Button content updates automatically based on playback state:
  - Shows &#xE768; (Play) when playback is Stopped or Paused
  - Shows &#xE769; (Pause) when playback is Playing
- Click behavior:
  - When showing Play glyph (stopped/paused) → starts/resumes playback
  - When showing Pause glyph (playing) → pauses playback
- `UpdatePlaybackUI()` method handles glyph updates in response to PlaybackStateChanged events
- Content set via string escape sequences (`"\uE768"`, `"\uE769"`) in code-behind
- Replaces the previous design which had separate Play and Pause buttons (confusing UI)

**Playlist and Visualizer Buttons:**
- **Playlist button:** Text label "Playlist" (width 60px, FontSize 9, FontWeight Bold)
- **Visualizer button:** Text label "Visualizer" (width 70px, FontSize 9, FontWeight Bold)
- Both use the WinampButtonStyle with standard UI font (not icon font)
- Replaces previous cryptic "PL" and "VIS" abbreviations

**Marquee:** Canvas + TranslateTransform animation scrolling left when text exceeds
display width. Format: `Song Name (yyyy-MM-dd) — Venue, City, State`

---

## Window Properties

**PlayerWindow (PlayerWindow.xaml):**
- `ResizeMode="NoResize"` — No resize, no maximize (fixed size window)
- `WindowStyle="None"` — Custom chrome with drag bar
- `Topmost="True"` — Always on top of other windows
- `ShowInTaskbar="False"` — No taskbar icon
- Size: 450×180 (fixed)

**PlaylistWindow (PlaylistWindow.xaml):**
- `ResizeMode="CanResizeWithGrip"` — User can resize by dragging edges, **maximize button disabled**
- `WindowStyle="None"` — Custom chrome with drag bar
- `Topmost="True"` — Always on top
- `ShowInTaskbar="False"` — No taskbar icon
- Size: 450×400 (default), MinHeight=200, MinWidth=350

**VisWindow (VisWindow.xaml):**
- `ResizeMode="CanResizeWithGrip"` — User can resize by dragging edges, **maximize button disabled**
- `WindowStyle="None"` — Custom chrome with drag bar
- `Topmost="True"` — Always on top
- `ShowInTaskbar="False"` — No taskbar icon
- Size: 450×300 (default), MinHeight=150, MinWidth=350

**Rationale for ResizeMode Settings:**
- PlayerWindow is fixed size (no resize needed for compact transport controls)
- PlaylistWindow and VisWindow allow resizing for user flexibility (larger/smaller playlist view)
- **Maximize disabled** on all three to prevent breaking the floating window group layout
- `CanResizeWithGrip` removes maximize button while still allowing edge/corner dragging

---

## Window Positioning

PlayerWindow is a separate WPF Window (not embedded in LibraryBrowserWindow).

**Free-floating behavior:**
- PlayerWindow floats independently of LibraryBrowserWindow
- User can position it anywhere on screen by dragging
- Position is persisted to `%APPDATA%/DeadEditor/settings.json` on every move
- On app relaunch, PlayerWindow restores to last saved position
- If no saved position (first launch), defaults to center-bottom of screen (40px from bottom for taskbar)
- If saved position is off-screen (monitor disconnected), falls back to default position

**Position persistence:**
- Saved on `LocationChanged` event (only when window is in Normal state, not minimized)
- Settings keys: `PlayerWindowLeft`, `PlayerWindowTop`
- Screen bounds validation prevents restoring to invalid positions

**Close button:**
- × button in drag bar closes PlayerWindow
- User can re-open from LibraryBrowserWindow menu (or keyboard shortcut, if implemented)

**Note:** PlayerWindow no longer docks to LibraryBrowserWindow. The "Now Playing" placeholder bar has been removed from LibraryBrowserWindow.

---

## Player Window Toggle Behavior

**Player Button/Menu in LibraryBrowserWindow:**
- Menu item: "🎵 Player"
- Keyboard shortcut: Ctrl+P
- **Three-state logic:** Hidden → show all; Minimized → restore all; Normal → hide all
- **Direct reference:** LibraryBrowserWindow stores a direct reference to PlayerWindow (`_playerWindowRef`) instead of searching `Application.Current.Windows`, because hidden windows are not reliably found in the Windows collection

**Toggle Behavior (Three States):**

1. **If PlayerWindow is Hidden (not visible):**
   - Reset all three windows to default size and position
   - Clear saved positions from settings (prevents off-screen placement)
   - Show PlayerWindow at center-bottom of primary screen
   - Show PlaylistWindow attached below PlayerWindow
   - Show VisWindow attached below PlaylistWindow
   - Activate PlayerWindow and bring to foreground

2. **If PlayerWindow is Minimized (visible but WindowState.Minimized):**
   - Restore PlayerWindow to `WindowState.Normal`
   - Restore PlaylistWindow to `WindowState.Normal` (if was minimized)
   - Restore VisWindow to `WindowState.Normal` (if was minimized)
   - Activate PlayerWindow and bring to foreground
   - **Note:** Does NOT reset position — windows restore to their existing position

3. **If PlayerWindow is Normal/Visible:**
   - Hide all three windows (PlayerWindow, PlaylistWindow, VisWindow)

**Position Reset on Show:**
- **PlayerWindow:** 450×180, center-bottom of primary screen (40px from bottom for taskbar)
- **PlaylistWindow:** 450×400, attached directly below PlayerWindow
- **VisWindow:** 450×300, attached directly below PlaylistWindow
- All three windows reset to `WindowState.Normal` (not maximized or minimized)
- Saved position settings (`PlayerWindowLeft`, `PlaylistWindowLeft`, `VisWindowLeft/Top`) are cleared

**Rationale for Three-State Logic:**
- **Hidden state:** Prevents windows from appearing off-screen after monitor disconnect; provides consistent placement
- **Minimized state:** Restores windows to their existing position (does not reset) so user's layout is preserved
- **Normal state:** Clean hide toggle for when user wants to temporarily dismiss the player group

**WPF Behavior Note:**
- Minimized windows have `IsVisible = true` but `WindowState = WindowState.Minimized`
- The toggle must check `WindowState` explicitly to distinguish minimized from normal/visible state
- Without this check, clicking Player button while minimized would hide the windows instead of restoring them

**Implementation:**
- `TogglePlayerWindowVisibility()` in LibraryBrowserWindow.xaml.cs (lines 1941-1986)
- `ResetPlayerWindowPositions()` in LibraryBrowserWindow.xaml.cs (lines 1988-2025)
- Uses `AttachToPlayerWindow()` and `AttachToPlaylistWindow()` to snap windows together

---

## Minimize/Restore/Close Behavior

All three player windows (PlayerWindow, PlaylistWindow, VisWindow) minimize, restore, and close together as a group.

**Grouped Window State Management:**
- When PlayerWindow is minimized → PlaylistWindow and VisWindow also hide (if visible)
- When PlayerWindow is restored → Companion windows remain hidden (restore via Player button in Library Browser)
- When PlayerWindow is closed (× button) → PlaylistWindow and VisWindow also hide
- This ensures the player windows stay synchronized and don't get separated

**Implementation Details:**
- PlayerWindow.StateChanged event handler hides companion windows on minimize
- PlayerWindow.OnClosing() method intercepts close attempts and hides windows instead
- **Shutdown detection:** Uses explicit `App.IsShuttingDown` flag to distinguish app shutdown from user clicking X
  - `App.IsShuttingDown` is set to `true` in `App.OnExit()` before cleanup begins
  - If `App.IsShuttingDown == true` → Allow PlayerWindow to close normally (app shutdown)
  - Otherwise → Cancel close event (`e.Cancel = true`), hide all three windows instead (user clicked X)
  - **CRITICAL:** Must call `base.OnClosing(e)` after setting `e.Cancel = true` to allow WPF to process the cancel flag
  - This approach is more reliable than counting windows or checking window states
- Uses Hide() instead of Close() to keep windows reusable
- Uses Hide() instead of Minimize() because companion windows have `ShowInTaskbar="False"` and can't be independently recovered
- Restoring from minimize/hide: User must use Player button (Ctrl+P) in LibraryBrowserWindow to show all windows together
- No taskbar icon needed (all three windows have `ShowInTaskbar="False"`)

**State Synchronization:**
```
PlayerWindow minimized → PlaylistWindow hidden (if visible)
                      → VisWindow hidden (if visible)

PlayerWindow restored  → Companion windows remain hidden
                      → Use Player button (Ctrl+P) to show all

PlayerWindow closed (×)→ PlaylistWindow hidden
                      → VisWindow hidden
```

**Window Lifecycle:**
- Windows are created once at app startup and reused
- Close button (×) hides windows rather than disposing them
- User can re-show via LibraryBrowserWindow Player menu (Ctrl+P)
- Using Hide() instead of Close() preserves window state and avoids re-initialization cost

---

## PlaylistWindow Layout

Separate WPF Window. Attaches below PlayerWindow by default.

```
┌─────────────────────────────────────────────┐
│  PLAYLIST EDITOR                    [_] [■] │  <- drag bar, minimize, detach
├────┬──────────────────────────┬───────┬─────┤
│ #  │ Title                    │ Date  │Time │
├────┼──────────────────────────┼───────┼─────┤
│ 1  │ Dark Star                │ 02-27 │8:32 │  <- gold highlight = now playing
│ 2  │ St. Stephen              │ 02-27 │4:10 │
│ 3  │ The Eleven               │ 02-27 │7:44 │
├────┴──────────────────────────┴───────┴─────┤
│ [+ Add] [✕ Clear]          23 tracks  47:12 │
└─────────────────────────────────────────────┘
```

**Attachment behavior:**
- Default: attached directly below PlayerWindow, follows PlayerWindow when it moves
- Detach button (■) makes it a free-floating window with independent position
- When attached: position not persisted (always follows PlayerWindow)
- When detached: position persisted to settings (`PlaylistWindowLeft`, `PlaylistWindowTop`)
- On app relaunch: restores to detached position if saved, otherwise attaches below PlayerWindow
- Double-click a row → PlaybackService.Play(track)
- Currently playing row highlighted in gold (#D7BA7D)

**Sortable Columns:**
- All columns are sortable by clicking the column header
- Clicking the same header again reverses the sort direction (ascending ↔ descending)
- Sort indicator arrow appears on the active column header
- **Default sort:** Track number (#) ascending (disc-aware: 101, 102... 201, 202...)
- **# column:** Sorts by `SortKey` computed property: `(DiscNumber * 100) + TrackNumber`
  - Multi-disc albums: 101, 102... 201, 202... (disc-aware)
  - Single-disc albums: 1, 2, 3, 4... (sequential)
- **Title column:** Sorts alphabetically by normalized song name
- **Date column:** Sorts by yyyy-MM-dd format (correct chronological order)
- **Time column:** Sorts by duration string in MM:SS format (mostly correct, minor edge cases)
- Playback and highlighting continue to work correctly after sorting

**Context Menu:**
- Right-click a playlist track → "🎵 Go to Concert" menu item
- Clicking it navigates LibraryBrowserWindow to the track's parent concert
- Scrolls to and highlights the specific track in the concert view
- LibraryBrowserWindow comes to foreground
- PlaylistWindow remains open
- If track not found in library (e.g., added from import screen and not yet saved):
  - Shows informational message: "Could not find this track in the library. The track may have been added from the import screen and not yet saved to the library."

**PL button on PlayerWindow:** toggles PlaylistWindow.Visibility

**Playlist Population Rules:**

The playlist is populated through four methods:

1. **Double-click track in LibraryBrowserWindow concert view**
   - Adds track to playlist (if not already present) via `AddTracksToPlaylist()` helper
   - Immediately plays the track via `App.PlaybackService.Play(track)`
   - Duplicate prevention: uses `TrackInfo.Equals()` (FilePath comparison) via `Playlist.Contains()`

2. **Right-click context menu in LibraryBrowserWindow**
   - **▶ Play Now:** Adds track and plays it
   - **＋ Add to Playlist:** Adds track without playing
   - **✕ Remove from Playlist:** Removes track from playlist (greyed out if not in playlist)
   - **＋ Add Selected to Playlist:** Adds all selected tracks (multi-select support)
   - All menu items use `AddTracksToPlaylist()` for duplicate prevention

3. **"Add All" button (multi-night concerts)**
   - Appears on date section headers in box sets and official releases
   - Adds all tracks for that specific concert date at once
   - Shows confirmation message: "X tracks from yyyy-MM-dd added to playlist"

4. **Opening a concert in LibraryBrowserWindow**
   - When user double-clicks a concert in the library grid, calls `PlaybackService.LoadPlaylist(_currentTracks)`
   - **Note:** This *replaces* the entire playlist with the concert's tracks (does not append)

**Clear All Behavior:**

The [✕ Clear] button in PlaylistWindow:
- Calls `PlaybackService.Stop()` to halt playback
- Clears the playlist via `PlaybackService.Playlist.Clear()`
- PlayerWindow marquee resets to blank (via `TrackChanged` event)

**Playlist Binding:**
- PlaylistWindow DataGrid bound directly to `PlaybackService.Playlist` (ObservableCollection<TrackInfo>)
- Changes automatically reflect in UI via ObservableCollection.CollectionChanged event

---

## Playlist Persistence

The playlist is saved to `%APPDATA%/DeadEditor/settings.json` on app exit and restored on app startup.

**Save on Exit (App.xaml.cs OnExit):**
```csharp
private void SavePlaylist()
{
    var settings = Models.LibrarySettings.Load();
    settings.SavedPlaylistPaths = PlaybackService.Playlist
        .Select(t => t.FilePath)
        .ToList();
    settings.Save();
}
```

**Restore on Startup (App.xaml.cs OnStartup):**
```csharp
private void RestorePlaylist()
{
    var settings = Models.LibrarySettings.Load();
    if (settings.SavedPlaylistPaths == null || !settings.SavedPlaylistPaths.Any())
        return;

    foreach (var path in settings.SavedPlaylistPaths)
    {
        // Skip missing files silently
        if (!System.IO.File.Exists(path))
            continue;

        try
        {
            // Read full metadata from file for proper playlist display
            using (var file = TagLib.File.Create(path))
            {
                var rawTitle = file.Tag.Title ?? System.IO.Path.GetFileNameWithoutExtension(path);

                // Parse title to extract date if embedded (e.g., "Song (1977-05-08)")
                var extractedDate = "";
                var songName = rawTitle;
                var dateMatch = System.Text.RegularExpressions.Regex.Match(rawTitle, @"^(.+?)\s*\((\d{4}-\d{2}-\d{2})\)\s*>?$");
                if (dateMatch.Success)
                {
                    songName = dateMatch.Groups[1].Value.Trim();
                    extractedDate = dateMatch.Groups[2].Value;
                }

                var track = new Models.TrackInfo
                {
                    FilePath = path,
                    FileName = System.IO.Path.GetFileName(path),
                    TrackNumber = (int)file.Tag.Track,
                    DiscNumber = file.Tag.Disc > 0 ? (int)file.Tag.Disc : 1,
                    SongName = songName,
                    RawTitle = rawTitle,
                    TrackDate = extractedDate,
                    Duration = file.Properties.Duration.ToString(@"mm\:ss"),
                    Segue = rawTitle.TrimEnd().EndsWith(">"),
                    IsModified = false
                };

                PlaybackService.Playlist.Add(track);
            }
        }
        catch
        {
            // Skip files that can't be loaded
            continue;
        }
    }
}
```

**Settings Schema:**
- `SavedPlaylistPaths` (List<string>) — File paths of tracks in saved playlist
- Added to LibrarySettings.cs as a new property
- Persists alongside window positions and library paths

**Metadata Loading:**
- Restored tracks read **full ID3 tag metadata** (TrackNumber, Duration, SongName, TrackDate)
- This ensures PlaylistWindow DataGrid displays track numbers, dates, and durations correctly
- **Previous bug:** Tracks were restored with only FilePath + Title, causing "0" track numbers and blank date/time columns
- **Fix:** Read all metadata from audio file tags using TagLib during restoration (2026-03-22)

**Error Handling:**
- Missing files (deleted/moved) are silently skipped during restore
- Corrupted/invalid audio files are silently skipped (TagLib.File.Create() failure caught)
- No error messages shown to user for missing or corrupted files

**Automated Tests:**
- Test coverage for playlist restoration exists in `DeadEditor.Tests/PlaylistRestorationTests.cs`
- Tests verify TrackNumber != 0, Duration != null, and graceful handling of missing/corrupted files
- Run tests: `dotnet test DeadEditor.Tests`

---

## VIS Window (stub)

**Startup Behavior:**
- VisWindow is created at app startup but **starts hidden** (not shown by default)
- User explicitly opens it via "Visualizer" button on PlayerWindow
- This prevents clutter on first launch - most users won't use visualizer immediately

**Attachment behavior:**
- Default: attached directly below PlaylistWindow, follows PlaylistWindow when it moves
- Detach button (■) makes it a free-floating window with independent position
- When attached: position not persisted (always follows PlaylistWindow)
- When detached: position persisted to settings (`VisWindowLeft`, `VisWindowTop`)
- On app relaunch: restores to detached position if saved, otherwise attaches below PlaylistWindow
- Content: dark background, centered text "Visualizer — coming soon"
- "Visualizer" button on PlayerWindow toggles visibility
- Architecture is identical to PlaylistWindow so real visualizer content
  can replace the stub later with no structural changes

**Attachment chain:** PlayerWindow → PlaylistWindow → VisWindow (all follow each other when docked)

---

## What Gets Removed

- Playback controls from MainWindow (import dialog)
- Playback controls from LibraryBrowserWindow (primary app window)
- Any NAudio instantiation outside of PlaybackService
- Docking system (PlayerWindow no longer snaps to LibraryBrowserWindow)
- Placeholder bar from LibraryBrowserWindow (no longer needed without docking)

---

## Commit Order (Completed)

1. ✓ PlaybackService singleton + remove duplicate playback logic (no UI changes)
2. ✓ PlayerWindow + docking behavior
3. ✓ PlaylistWindow + attachment behavior
4. ✓ VIS stub window
5. ✓ Remove old controls + add LibraryBrowserWindow placeholder bar
6. ✓ Documentation updates (01-main-window.md, 02-library-browser.md, this file)

## Startup Sequence

1. App.xaml.cs OnStartup() creates windows in order:
   - LibraryBrowserWindow (primary window)
   - PlayerWindow() - free-floating (restores last position or defaults to center-bottom)
   - PlaylistWindow(playerWindow) - attaches below PlayerWindow (or restores detached position)
   - VisWindow(playlistWindow) - attaches below PlaylistWindow (or restores detached position)
2. All windows shown at startup
3. PlayerWindow is independent (no longer follows LibraryBrowserWindow)
4. PlaylistWindow follows PlayerWindow (when attached)
5. VisWindow follows PlaylistWindow (when attached)

## Position Persistence Implementation

All three companion windows (PlayerWindow, PlaylistWindow, VisWindow) persist their positions:

**PlayerWindow:**
- Always saves position on `LocationChanged`
- Settings keys: `PlayerWindowLeft`, `PlayerWindowTop`
- Default position: center-bottom of screen (40px from bottom)
- Screen bounds validation on restore

**PlaylistWindow:**
- Saves position only when detached (`_isAttached == false`)
- When attached: clears saved position (position follows PlayerWindow)
- Settings keys: `PlaylistWindowLeft`, `PlaylistWindowTop`
- Restoring saved position implies detached state

**VisWindow:**
- Saves position only when detached (`_isAttached == false`)
- When attached: clears saved position (position follows PlaylistWindow)
- Settings keys: `VisWindowLeft`, `VisWindowTop`
- Restoring saved position implies detached state

**Settings persistence:**
- All settings saved to `%APPDATA%/DeadEditor/settings.json`
- Uses existing `LibrarySettings.Save()` infrastructure
- JSON serialization via Newtonsoft.Json
