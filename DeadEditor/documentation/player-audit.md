# Player System Audit — Phase 1 Diagnosis

**Date:** 2026-03-20
**Purpose:** Document current playback implementation before refactoring to singleton PlaybackService per doc 17.

---

## 1. Playback Controls in UI

### MainWindow.xaml (Lines 550-561)

**Location:** Audio Playback Controls GroupBox at bottom of window

**Buttons:**
| Button Name | Symbol | FontSize | Width | Enabled Default |
|-------------|--------|----------|-------|-----------------|
| `PreviousTrackButton` | ⏮ | 16 | 40px | False |
| `PlayPauseButton` | ▶ | 16 (Bold) | 50px | False |
| `StopButton` | ⏹ | 16 | 40px | False |
| `NextTrackButton` | ⏭ | 16 | 40px | False |

**Icon Method:** Unicode symbols set directly in XAML `Content` attribute — **GOOD** (matches doc 17 spec)

**Additional UI Elements:**
- `NowPlayingText` TextBlock (displays "No track playing" or "♪ {song name}")
- `ProgressSlider` (0-100 scale, no time display visible in grep)
- `CurrentTimeText` + `TotalTimeText` (time displays)
- `VolumeSlider` (0-100 scale)

---

### LibraryBrowserWindow.xaml (Lines 718-769)

**Location:** Concert View bottom section, only visible when viewing a concert (not in library grid view)

**Buttons:**
| Button Name | Symbol | FontSize | Width | Height | Background | Enabled Default |
|-------------|--------|----------|-------|--------|------------|-----------------|
| `PreviousButton` | ⏮ | 20 | 60px | 46px | #3E3E42 | False |
| `PlayPauseButton` | ▶ | 22 (Bold) | 70px | 46px | #0E639C (blue) | False |
| `StopButton` | ⏹ | 20 | 60px | 46px | #3E3E42 | False |
| `NextButton` | ⏭ | 20 | 60px | 46px | #3E3E42 | False |

**Icon Method:** Unicode symbols set directly in XAML `Content` attribute — **GOOD** (matches doc 17 spec)

**Additional UI Elements:**
- `NowPlayingTrack` TextBlock (track name + segue info)
- `NowPlayingDetails` TextBlock ("Track {n} - {duration}")
- `AudioScrubber` Slider (0 to TotalDuration.TotalSeconds scale)
- `CurrentTimeText` + `TotalTimeText` (time displays)
- `VolumeSlider` (0-100 scale)

**Visibility:** Entire player controls section is in concert detail view, which has `Visibility="Collapsed"` by default. Only shown when user double-clicks a concert from library grid.

---

## 2. NAudio Instantiation

### Current Architecture: DUPLICATED (Not Shared)

**AudioPlayerService.cs** (Lines 1-125)
- Standalone service class in `Services/` folder
- Uses NAudio's `WaveOutEvent` + `AudioFileReader`
- **Not a singleton** — instantiated separately in each window

**MainWindow.xaml.cs** (Line 61)
```csharp
_audioPlayer = new AudioPlayerService();
```
- Created in MainWindow constructor
- Disposed in MainWindow_Closing (line 95)

**LibraryBrowserWindow.xaml.cs** (Line 67)
```csharp
_audioPlayer = new AudioPlayerService();
```
- Created in LibraryBrowserWindow constructor
- Disposed in OnClosing (line 1960)

### Result: TWO SEPARATE AUDIO PLAYERS

When both windows are open simultaneously, there are **two independent NAudio instances**. No shared state. This is the root cause of the "two players" bug.

---

## 3. What Happens When Import Window Opens

**Scenario:** User is in LibraryBrowserWindow with music playing, clicks File → Import New Concert

**Code Path:** LibraryBrowserWindow.xaml.cs line 1833-1838
```csharp
private void ImportMenuItem_Click(object sender, RoutedEventArgs e)
{
    var importWindow = new MainWindow();
    importWindow.ShowDialog();  // MODAL DIALOG — blocks LibraryBrowserWindow

    // Refresh library after import
    LoadShows();
}
```

**What Happens to Playback:**

1. **LibraryBrowserWindow audio player keeps running** (in background)
   - `_audioPlayer` in LibraryBrowserWindow is still alive
   - `_updateTimer` continues ticking (if it wasn't stopped)
   - Music continues playing from LibraryBrowserWindow's player

2. **MainWindow creates its own audio player** (line 61)
   - New `AudioPlayerService` instance is created
   - Completely separate from LibraryBrowserWindow's player
   - If user plays a track in MainWindow, there are now **TWO audio streams playing simultaneously**

3. **When MainWindow closes:**
   - MainWindow's `_audioPlayer.Dispose()` is called (line 95)
   - LibraryBrowserWindow's `_audioPlayer` is unaffected
   - Music from LibraryBrowserWindow continues (or not, depending on whether it was stopped)

**Root Cause:** No shared playback state. Each window owns its own audio player instance.

---

## 4. Icon/Glyph Usage on Playback Buttons

### Current Implementation: Unicode Symbols (CORRECT)

**MainWindow buttons:**
- Previous: ⏮ (U+23EE)
- Play: ▶ (U+25B6)
- Pause: ⏸ (U+23F8)
- Stop: ⏹ (U+23F9)
- Next: ⏭ (U+23ED)

**LibraryBrowserWindow buttons:**
- Previous: ⏮ (U+23EE)
- Play: ▶ (U+25B6)
- Pause: ⏸ (U+23F8)
- Stop: ⏹ (U+23F9)
- Next: ⏭ (U+23ED)

**Method:** Direct XAML `Content` attribute assignment
```xml
<Button x:Name="PlayPauseButton" Content="▶" FontSize="22" .../>
```

**Assessment:** This is the **correct approach** per doc 17 spec. No icon fonts, no Segoe MDL2 Assets, no image resources. Just pure Unicode text content.

**Note:** There is no "disappearing button graphics" bug in the current implementation. The spec in doc 17 mentions this bug as a concern to avoid, but the current code already uses the correct approach (Unicode symbols). The refactor should maintain this pattern.

---

## 5. Additional Findings

### AudioPlayerService.cs Analysis

**Current Public Interface (Lines 1-125):**

```csharp
public class AudioPlayerService : IDisposable
{
    // Events (2)
    public event EventHandler? PlaybackStopped;
    public event EventHandler<TimeSpan>? PositionChanged;

    // Properties (5)
    public bool IsPlaying { get; }                    // Read-only, returns _isPlaying flag
    public string? CurrentFilePath { get; }           // Read-only, returns _currentFilePath
    public TimeSpan CurrentPosition { get; set; }     // Get/set _audioFileReader.CurrentTime
    public TimeSpan TotalDuration { get; }            // Read-only, returns _audioFileReader.TotalTime
    public float Volume { get; set; }                 // Get/set _audioFileReader.Volume (0-1 range, clamped)

    // Methods (7)
    public void LoadFile(string filePath);            // Stops current, loads new file, init WaveOutEvent
    public void Play();                               // Starts playback (_wavePlayer.Play())
    public void Pause();                              // Pauses playback (_wavePlayer.Pause())
    public void Stop();                               // Stops and disposes NAudio objects
    public void Seek(TimeSpan position);              // Seeks to position (sets CurrentTime)
    public void UpdatePosition();                     // Triggers PositionChanged event (unused)
    public void Dispose();                            // Calls Stop()
}
```

**Private Fields (5):**
- `_wavePlayer` (IWavePlayer?) — NAudio output device
- `_audioFileReader` (AudioFileReader?) — NAudio file reader
- `_currentFilePath` (string?) — Path to currently loaded file
- `_isPlaying` (bool) — Playback state flag
- `_isSeeking` (bool) — Prevents PositionChanged during seek

**Event Handlers (1):**
- `OnPlaybackStopped(object?, StoppedEventArgs)` — Sets _isPlaying = false, raises PlaybackStopped event

**Usage Notes:**
- `UpdatePosition()` method exists but is never called by windows (windows use timers instead)
- `PositionChanged` event is never subscribed to
- `LoadFile()` always calls `Stop()` first (cannot load without stopping current)

**Missing from Current Implementation (needed for doc 17):**
- `PlaybackState` enum (Playing/Paused/Stopped)
- `CurrentTrack` property (TrackInfo)
- `Playlist` (ObservableCollection<TrackInfo>)
- `Next()` / `Previous()` methods (track navigation logic is in windows, not service)
- `LoadPlaylist()` method
- `TrackChanged` event
- `PlaybackStateChanged` event

### Track Navigation Logic

**Current Location:** Implemented in each window's code-behind

**MainWindow.xaml.cs:**
- `_currentTrackIndex` (line 47) — tracks current position in `_tracks` list
- `PreviousTrackButton_Click` (line 1039) — decrements index, calls PlayTrack()
- `NextTrackButton_Click` (line 1049) — increments index, calls PlayTrack()
- `PlayTrack(int index)` (line 1058) — loads file, updates UI

**LibraryBrowserWindow.xaml.cs:**
- `_currentTrackIndex` (line 29) — tracks current position in `_currentTracks` list
- `PreviousButton_Click` (line 1744) — decrements index, calls PlayTrack()
- `NextButton_Click` (line 1755) — increments index, calls PlayTrack()
- `PlayTrack(int index)` (line 1653) — loads file, updates UI

**Note:** Track navigation logic is **duplicated** across both windows. This should move into PlaybackService.

### Timer-Based UI Updates

**MainWindow.xaml.cs:**
- `_playbackTimer` (line 46) — DispatcherTimer, 100ms interval
- `PlaybackTimer_Tick` (line 1112) — updates CurrentTimeText and ProgressSlider
- `_isScrubbing` flag (line 48) — prevents UI updates during user scrubbing

**LibraryBrowserWindow.xaml.cs:**
- `_updateTimer` (line 30) — DispatcherTimer, 100ms interval
- UpdateTimer_Tick (line 1786) — updates CurrentTimeText and AudioScrubber
- `_isScrubbing` flag (line 32) — prevents UI updates during user scrubbing

**Note:** Timer logic is **duplicated**. PlaybackService should expose events that UI subscribes to.

---

## 6. Summary

### Current State

✅ **GOOD:**
- Unicode symbols for button icons (correct approach)
- AudioPlayerService class already exists (foundational service architecture)
- NAudio logic is centralized in service class (not scattered in window code)

❌ **BAD:**
- AudioPlayerService instantiated separately in each window (not singleton)
- Two audio players running simultaneously when import window is open
- Track navigation logic duplicated in MainWindow and LibraryBrowserWindow
- Playlist management is window-specific (no global playlist)
- No shared playback state across windows

### Root Cause of "Two Players" Bug

When LibraryBrowserWindow opens MainWindow as a modal dialog (line 1833), both windows have their own `AudioPlayerService` instance. If music is playing in LibraryBrowserWindow and user plays a track in MainWindow, both NAudio instances output audio simultaneously.

### Migration Path to Singleton

**Phase 2 will:**
1. Convert `AudioPlayerService` to singleton pattern
2. Add missing properties/methods per doc 17 (Playlist, CurrentTrack, PlaybackState, etc.)
3. Move track navigation logic from windows into service
4. Register singleton in App.xaml.cs
5. Update MainWindow and LibraryBrowserWindow to reference singleton instead of creating instances
6. Remove all `new AudioPlayerService()` calls
7. Remove duplicate track navigation code

---

## 7. Files to Modify in Phase 2

**Create:**
- None (AudioPlayerService already exists)

**Modify:**
- `Services/AudioPlayerService.cs` — convert to singleton, add playlist/state management
- `App.xaml.cs` — register singleton at startup
- `MainWindow.xaml.cs` — remove instantiation, use singleton, remove nav logic
- `LibraryBrowserWindow.xaml.cs` — remove instantiation, use singleton, remove nav logic

**Delete:**
- None (no playback code scattered elsewhere)

---

**End of Phase 1 Audit**

Ready for Phase 2: PlaybackService singleton implementation.

---

## 8. Phase 3 Architecture Analysis (2026-03-20)

### Startup Sequence (Post-Phase 3)

**App.xaml.cs OnStartup() — Lines 18-32:**

```csharp
protected override void OnStartup(System.Windows.StartupEventArgs e)
{
    base.OnStartup(e);

    // Initialize singleton on app startup to ensure it's ready
    _ = AudioPlayerService.Instance;

    // Create and show main library browser window
    var libraryWindow = new LibraryBrowserWindow();
    libraryWindow.Show();

    // Create and show player window (docked to library window)
    var playerWindow = new PlayerWindow(libraryWindow);
    playerWindow.Show();
}
```

**Sequence:**
1. App.xaml has NO StartupUri (removed in Phase 3)
2. OnStartup() creates LibraryBrowserWindow FIRST
3. OnStartup() creates PlayerWindow SECOND (receives LibraryBrowserWindow as parent)
4. Both windows shown via .Show() (not .ShowDialog())
5. LibraryBrowserWindow is now the effective "main window" of the application

**MainWindow is NOT created at startup.** MainWindow is now a modal dialog opened on-demand.

### Window Relationships

**LibraryBrowserWindow (Primary Window):**
- Created at application startup (App.xaml.cs line 26)
- Shown with `.Show()` — non-modal, stays open
- Owns other dialogs (Settings, Advanced Search, Import)
- PlayerWindow docks to LibraryBrowserWindow by default

**MainWindow (Import Dialog):**
- Created on-demand when user clicks File → Import (LibraryBrowserWindow.xaml.cs line 1832)
- OR created when user clicks Edit button on a show (line 1864)
- Shown with `.ShowDialog()` — MODAL dialog
- `importWindow.Owner = this` (LibraryBrowserWindow is the owner)
- Blocks LibraryBrowserWindow while open
- Closes when import completes

**PlayerWindow (Global Player):**
- Created at application startup (App.xaml.cs line 30)
- Receives LibraryBrowserWindow as parent window
- Docks to LibraryBrowserWindow's bottom-left corner
- Listens to parent's LocationChanged and SizeChanged events
- Remains visible when MainWindow (import dialog) opens

### What Happens When User Opens MainWindow (Import Screen)?

**Current Behavior:**

1. User clicks File → Import from LibraryBrowserWindow menu
2. LibraryBrowserWindow creates `new MainWindow()` (line 1832)
3. MainWindow shown as modal dialog via `.ShowDialog()` (line 1833)
4. MainWindow appears OVER LibraryBrowserWindow (blocking modal)
5. **PlayerWindow remains docked to LibraryBrowserWindow** (which is now behind MainWindow)
6. PlayerWindow is visible but docked to the hidden/background window
7. When MainWindow closes, LibraryBrowserWindow comes back to front
8. PlayerWindow is still docked to LibraryBrowserWindow (unchanged)

**Problem:** PlayerWindow does not re-dock to MainWindow when import screen opens. It stays docked to LibraryBrowserWindow, which is now blocked/hidden by the modal MainWindow dialog.

**Visual Result:**
- MainWindow appears in front
- LibraryBrowserWindow is blocked (modal dialog behavior)
- PlayerWindow is visible at LibraryBrowserWindow's bottom-left position (not MainWindow's position)
- PlayerWindow is "orphaned" — visible but docked to the wrong window

### Does Docking Parent Need to Change?

**Answer: No architectural change needed, but behavior could be improved.**

**Current Architecture is Correct:**
- LibraryBrowserWindow is the primary/main window (correct)
- MainWindow is a modal import dialog (correct)
- PlayerWindow docks to LibraryBrowserWindow (correct for normal use)

**Options for Phase 4+:**

1. **Keep Current Behavior (Simplest)**
   - PlayerWindow stays docked to LibraryBrowserWindow
   - When import dialog opens, PlayerWindow remains at library window position
   - Acceptable because playback continues in background during import

2. **Hide PlayerWindow During Import (Better UX)**
   - When MainWindow.ShowDialog() is called, temporarily hide PlayerWindow
   - When MainWindow closes, show PlayerWindow again
   - Prevents visual confusion of player being visible but docked to hidden window

3. **Re-dock to Active Window (Complex)**
   - When MainWindow opens, undock from LibraryBrowserWindow
   - Dock to MainWindow temporarily
   - When MainWindow closes, re-dock to LibraryBrowserWindow
   - Most complex, requires tracking window lifecycle

**Recommendation:** Option 1 (keep current behavior) for Phase 4. PlayerWindow is a global player, staying docked to the primary window (LibraryBrowserWindow) is correct. Users can manually undock if needed.

### Summary

- **Startup:** LibraryBrowserWindow → PlayerWindow (no MainWindow)
- **Architecture:** LibraryBrowserWindow is primary, MainWindow is modal dialog
- **PlayerWindow Parent:** LibraryBrowserWindow (correct, does not need to change)
- **Import Workflow:** MainWindow appears over LibraryBrowserWindow (modal), PlayerWindow stays docked to LibraryBrowserWindow
- **No Architectural Change Needed:** Current design is sound

---

**End of Phase 3 Architecture Analysis**

---

## 9. Bug Diagnosis (2026-03-20 Post-Phase 6)

### Bug 1: PlayerWindow Won't Come to Front

**Symptom:** PlayerWindow appears in the taskbar but clicking it only flashes — it won't raise to the foreground.

**Diagnosis:**

1. **Owner Property:** ✓ NOT SET
   - Checked PlayerWindow.xaml.cs for `Owner` assignments: NONE FOUND
   - PlayerWindow has no Owner set, so it should be freely focusable

2. **ShowInTaskbar:** ✓ SET TO TRUE
   - PlayerWindow.xaml line 11: `ShowInTaskbar="True"`
   - PlaylistWindow.xaml line 12: `ShowInTaskbar="True"`
   - VisWindow.xaml line 11: `ShowInTaskbar="True"`
   - All three windows appear in taskbar

3. **Topmost Property:** ✓ SET TO FALSE (correct)
   - PlayerWindow.xaml line 10: `Topmost="False"`
   - PlaylistWindow.xaml line 11: `Topmost="False"`
   - VisWindow.xaml line 10: `Topmost="False"`
   - None are topmost windows

4. **Focus/Activate Calls:** ✓ NONE FOUND
   - Grepped PlayerWindow.xaml.cs for `Owner|Focus|Activate|Topmost`: NO MATCHES
   - No code fighting with window focus

5. **WindowStyle:** ✓ CORRECT
   - PlayerWindow.xaml line 6: `WindowStyle="None"`
   - AllowsTransparency line 9: `AllowsTransparency="True"`
   - ResizeMode line 7: `ResizeMode="NoResize"`

**ROOT CAUSE HYPOTHESIS:**

The combination of `WindowStyle="None"` + `AllowsTransparency="True"` creates a WPF layered window (software rendered). Layered windows have known focus issues in WPF:

- **WPF Bug:** Layered windows (AllowsTransparency=True) cannot receive focus via taskbar click in some scenarios
- **Reference:** https://github.com/dotnet/wpf/issues/2466
- **Workaround:** Use `WindowStyle="ToolWindow"` OR remove `AllowsTransparency` OR handle taskbar clicks manually

**Additional Evidence:**
- ShowInTaskbar="True" + AllowsTransparency="True" + WindowStyle="None" is a problematic combination
- Window flashing (instead of activating) is classic symptom of WPF layered window focus bug

**Recommended Fix:**
1. Change `ShowInTaskbar="False"` (tool windows shouldn't be in taskbar separately)
2. OR remove `AllowsTransparency="True"` and use opaque background
3. OR add manual activation handling in WndProc

---

### Bug 2: Playback Not Starting from LibraryBrowserWindow or PlaylistWindow

**Symptom:** Double-clicking tracks doesn't start playback.

**Diagnosis:**

1. **LibraryBrowserWindow Double-Click Handler:**
   - Event: `TracksDataGridRow_MouseDoubleClick` (line 1612)
   - Code: Lines 1614-1620
   ```csharp
   private void TracksDataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
   {
       if (sender is DataGridRow row && row.Item is TrackInfo track)
       {
           // Load playlist and play selected track using singleton
           _audioPlayer.LoadPlaylist(_currentTracks);
           _audioPlayer.Play(track);
       }
   }
   ```
   - ✓ Calls `LoadPlaylist()` BEFORE `Play(track)` — correct order
   - ✓ Track extracted from DataGridRow.Item

2. **PlaylistWindow Double-Click Handler:**
   - Event: `PlaylistDataGrid_MouseDoubleClick` (line 245)
   - Code: Lines 247-251
   ```csharp
   private void PlaylistDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
   {
       if (PlaylistDataGrid.SelectedItem is PlaylistTrackViewModel vm)
       {
           _player.Play(vm.Track);
       }
   }
   ```
   - ✓ Calls `Play(track)` directly
   - ⚠️ Does NOT call `LoadPlaylist()` first — assumes playlist already loaded

3. **AudioPlayerService.Play(TrackInfo) Method:**
   - Location: Services/AudioPlayerService.cs line 139
   - Code: Lines 139-152
   ```csharp
   public void Play(TrackInfo track)
   {
       // Find track in playlist
       var index = _playlist.IndexOf(track);
       if (index >= 0)
       {
           _currentTrackIndex = index;
       }

       _currentTrack = track;
       LoadFile(track.FilePath);
       Play();
       TrackChanged?.Invoke(this, EventArgs.Empty);
   }
   ```
   - ✓ Finds track in playlist using `IndexOf()`
   - ✓ Sets _currentTrackIndex if found
   - ✓ Calls `LoadFile()` then `Play()`
   - ⚠️ **POTENTIAL ISSUE:** If track NOT in playlist (`index < 0`), _currentTrackIndex is NOT updated
   - ⚠️ **POTENTIAL ISSUE:** `Play()` is called regardless of whether track is in playlist

4. **LoadPlaylist() Method:**
   - Location: Services/AudioPlayerService.cs line 203
   - Code: Lines 203-211
   ```csharp
   public void LoadPlaylist(System.Collections.Generic.IEnumerable<TrackInfo> tracks)
   {
       _playlist.Clear();
       foreach (var track in tracks)
       {
           _playlist.Add(track);
       }
       _currentTrackIndex = -1;
   }
   ```
   - ✓ Clears playlist first
   - ✓ Adds all tracks
   - ✓ Resets _currentTrackIndex to -1

**ROOT CAUSE HYPOTHESIS:**

Several possible issues:

1. **IsPlaying Check Missing:**
   - `AudioPlayerService.Play()` method (parameterless) may check IsPlaying and return early
   - Need to check if Play() actually starts playback or is a no-op

2. **Track Reference Inequality:**
   - `_playlist.IndexOf(track)` uses reference equality (default for objects)
   - If LibraryBrowserWindow passes a different TrackInfo instance than what's in playlist, IndexOf() returns -1
   - Need to verify if TrackInfo has custom Equals() implementation

3. **LoadFile() Errors Swallowed:**
   - Need to check if LoadFile() throws exceptions that are being caught/ignored
   - Need to check if file path is valid

4. **NAudio Initialization:**
   - WaveOutEvent may not be initialized properly
   - Need to check if _wavePlayer is null

**Recommended Debug Steps:**

1. Add `Debug.WriteLine()` to `Play(TrackInfo)` entry point
2. Add `Debug.WriteLine()` to `LoadFile()` entry point
3. Add `Debug.WriteLine()` to parameterless `Play()` entry point
4. Check if file paths are valid
5. Check if NAudio throws exceptions
6. Verify TrackInfo reference equality

**Need to Check:**
- AudioPlayerService.Play() (parameterless) implementation
- AudioPlayerService.LoadFile() implementation
- TrackInfo.Equals() implementation (if any)
- Exception handling in AudioPlayerService

---

**End of Bug Diagnosis**

**Next Steps:** Report findings, wait for approval before adding debug logging or making fixes.
