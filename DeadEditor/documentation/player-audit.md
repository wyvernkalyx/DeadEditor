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
