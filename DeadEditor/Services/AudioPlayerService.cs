using DeadEditor.Models;
using NAudio.Wave;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

namespace DeadEditor.Services
{
    /// <summary>
    /// Playback state enumeration (Playing, Paused, or Stopped).
    /// </summary>
    public enum PlaybackState
    {
        Stopped,
        Playing,
        Paused
    }

    /// <summary>
    /// Raised when playback cannot start because a track's file is missing/moved. Carries the
    /// offending track (may be null) and a human-readable reason. The service raises this instead
    /// of throwing so a deleted-under-us file degrades gracefully; the UI (PlayerBar) surfaces it
    /// as an in-window banner. Keeps the service UI-agnostic (mirrors the other event pattern).
    /// </summary>
    public class PlaybackFailedEventArgs : EventArgs
    {
        public TrackInfo? Track { get; }
        public string Reason { get; }

        public PlaybackFailedEventArgs(TrackInfo? track, string reason)
        {
            Track = track;
            Reason = reason;
        }
    }

    /// <summary>
    /// Singleton playback service managing global audio playback across all windows.
    /// Only one instance exists per application lifetime.
    /// </summary>
    public class AudioPlayerService : IDisposable
    {
        // Singleton instance
        private static AudioPlayerService? _instance;
        private static readonly object _lock = new object();

        // Serializes every guarded FLAC write window app-wide (see WithFileReleased).
        // App-wide via static: there is one playback singleton today, but writes target
        // files, not the player, so the gate belongs to the write activity, not an instance.
        private static readonly SemaphoreSlim _writeGate = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Gets the singleton instance of AudioPlayerService.
        /// </summary>
        public static AudioPlayerService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new AudioPlayerService();
                        }
                    }
                }
                return _instance;
            }
        }

        // NAudio objects
        private IWavePlayer? _wavePlayer;
        private AudioFileReader? _audioFileReader;
        private string? _currentFilePath;
        private bool _isSeeking;
        private float _volume = 0.75f;  // Cached volume level — applied to new AudioFileReaders

        // Playback state
        private PlaybackState _playbackState;
        private TrackInfo? _currentTrack;
        private int _currentTrackIndex = -1;
        private bool _userInitiatedStop = false;

        // Playlist
        private readonly ObservableCollection<TrackInfo> _playlist;

        // Events (existing + new)
        public event EventHandler? PlaybackStopped;
        public event EventHandler<TimeSpan>? PositionChanged;
        public event EventHandler? TrackChanged;
        public event EventHandler? PlaybackStateChanged;
        // Raised when a track cannot be played because its file is missing/moved (see
        // PlaybackFailedEventArgs). Surfaced by PlayerBar as a banner; never crashes playback.
        public event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;

        // Existing properties (preserved for backward compatibility)
        public bool IsPlaying => _playbackState == PlaybackState.Playing;
        public string? CurrentFilePath => _currentFilePath;

        public TimeSpan CurrentPosition
        {
            get => _audioFileReader?.CurrentTime ?? TimeSpan.Zero;
            set
            {
                if (_audioFileReader != null)
                {
                    _audioFileReader.CurrentTime = value;
                }
            }
        }

        public TimeSpan TotalDuration => _audioFileReader?.TotalTime ?? TimeSpan.Zero;

        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Clamp(value, 0f, 1f);
                if (_audioFileReader != null)
                {
                    _audioFileReader.Volume = _volume;
                }
            }
        }

        // New properties for doc 17 spec
        public PlaybackState State => _playbackState;
        public TrackInfo? CurrentTrack => _currentTrack;
        public ObservableCollection<TrackInfo> Playlist => _playlist;
        public int CurrentTrackIndex => _currentTrackIndex;

        /// <summary>
        /// Private constructor for singleton pattern.
        /// </summary>
        private AudioPlayerService()
        {
            _playlist = new ObservableCollection<TrackInfo>();
            _playbackState = PlaybackState.Stopped;
        }

        public void LoadFile(string filePath)
        {
            // Defensive guard: a missing/moved file would throw out of NAudio's AudioFileReader
            // ctor below and, with no unhandled-exception handler, crash the app. Callers
            // (Play(TrackInfo), Play() Case 3) already pre-check existence, so this is belt-and-
            // suspenders: raise PlaybackFailed and return without touching the current player
            // (leaves _wavePlayer as-is; callers null-check before dereferencing).
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
            {
                System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] LoadFile SKIPPED - missing file: {filePath}");
                RaisePlaybackFailed(_currentTrack, "File not found");
                return;
            }

            try
            {
                Stop();

                _currentFilePath = filePath;
                System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] LoadFile: {filePath}");

                _audioFileReader = new AudioFileReader(filePath);
                _audioFileReader.Volume = _volume;  // Apply cached volume to new reader

                _wavePlayer = new WaveOutEvent();
                _wavePlayer.Init(_audioFileReader);
                _wavePlayer.PlaybackStopped += OnPlaybackStopped;

                System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] LoadFile SUCCESS - Duration: {_audioFileReader.TotalTime}");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] LoadFile FAILED: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] Exception: {ex}");
                throw; // Re-throw to caller
            }
        }

        public void Play()
        {
            // Case 1: Wave player exists and is paused - resume playback
            if (_wavePlayer != null && _audioFileReader != null && _playbackState == PlaybackState.Paused)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] Play() - Resuming from paused state");
                    _wavePlayer.Play();
                    SetPlaybackState(PlaybackState.Playing);
                    System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] Play() SUCCESS - Resumed");
                    return;
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] Play() resume FAILED: {ex.Message}");
                    throw;
                }
            }

            // Case 2: Already playing - do nothing
            if (_wavePlayer != null && _audioFileReader != null && _playbackState == PlaybackState.Playing)
            {
                System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] Play() - Already playing");
                return;
            }

            // Case 3: CurrentTrack exists but player is not initialized - reload and play
            if (_currentTrack != null && System.IO.File.Exists(_currentTrack.FilePath))
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] Play() - Reloading current track: {_currentTrack.SongName ?? _currentTrack.Title}");
                    LoadFile(_currentTrack.FilePath);
                    _wavePlayer.Play();
                    SetPlaybackState(PlaybackState.Playing);
                    System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] Play() SUCCESS - Reloaded and playing");
                    return;
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] Play() reload FAILED: {ex.Message}");
                    throw;
                }
            }

            // Case 4: No current track - cannot play
            System.Diagnostics.Debug.WriteLine("[AudioPlayerService] Play() called but no track to play");
        }

        /// <summary>
        /// Plays a specific track from the playlist.
        /// </summary>
        public void Play(TrackInfo track)
        {
            // Guard: never hand a missing/empty path to LoadFile/NAudio (which would throw and,
            // with no unhandled-exception handler, crash the app). A DIRECT play of a missing track
            // banners via PlaybackFailed and stays put — it does not auto-jump to another track.
            // Mirrors the existing File.Exists precedent in the parameterless Play() Case 3.
            if (track == null || string.IsNullOrEmpty(track.FilePath) || !System.IO.File.Exists(track.FilePath))
            {
                RaisePlaybackFailed(track, "File not found");
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] Play(TrackInfo) called - Track: {track.SongName ?? track.Title}");
                System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] FilePath: {track.FilePath}");

                // Find track in playlist
                var index = _playlist.IndexOf(track);
                System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] Track index in playlist: {index} (playlist count: {_playlist.Count})");

                if (index >= 0)
                {
                    _currentTrackIndex = index;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] WARNING: Track not found in playlist");
                }

                _currentTrack = track;
                LoadFile(track.FilePath);

                // Defensive: LoadFile leaves _wavePlayer null if the file vanished between the
                // guard above and here (TOCTOU). Bail without crashing; PlaybackFailed already fired.
                if (_wavePlayer == null)
                    return;

                // Start playback directly (don't call parameterless Play() which
                // would re-enter Case 3 and redundantly LoadFile a second time)
                _wavePlayer.Play();
                SetPlaybackState(PlaybackState.Playing);

                TrackChanged?.Invoke(this, EventArgs.Empty);

                System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] Play(TrackInfo) completed successfully");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] Play(TrackInfo) FAILED: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] Exception: {ex}");
                throw;
            }
        }

        public void Pause()
        {
            if (_wavePlayer == null)
                return;

            _wavePlayer.Pause();
            SetPlaybackState(PlaybackState.Paused);
        }

        public void Stop()
        {
            if (_wavePlayer != null)
            {
                _userInitiatedStop = true;
                // Unsubscribe BEFORE stopping to prevent the old player's async
                // PlaybackStopped event from clobbering state after a new player starts
                _wavePlayer.PlaybackStopped -= OnPlaybackStopped;
                _wavePlayer.Stop();
                _wavePlayer.Dispose();
                _wavePlayer = null;
            }

            if (_audioFileReader != null)
            {
                _audioFileReader.Dispose();
                _audioFileReader = null;
            }

            SetPlaybackState(PlaybackState.Stopped);
            _currentFilePath = null;
        }

        public void Seek(TimeSpan position)
        {
            if (_audioFileReader != null)
            {
                _isSeeking = true;
                _audioFileReader.CurrentTime = position;
                _isSeeking = false;
            }
        }

        public void UpdatePosition()
        {
            if (!_isSeeking && _audioFileReader != null && _playbackState == PlaybackState.Playing)
            {
                PositionChanged?.Invoke(this, _audioFileReader.CurrentTime);
            }
        }

        /// <summary>
        /// Releases the NAudio file handle around an action when the currently-loaded
        /// file would otherwise block a writer. Captures position and play/paused state;
        /// restores the previous player state after the action completes (or throws).
        /// No-ops when the loaded file is not among the supplied paths.
        /// </summary>
        /// <param name="paths">Files the action intends to write. If the currently
        /// loaded file is among them, the player is stopped before the action runs and
        /// reloaded afterward; otherwise playback is left untouched.</param>
        /// <param name="action">The write operation needing exclusive file access.</param>
        public void WithFileReleased(System.Collections.Generic.IEnumerable<string> paths, Action action)
        {
            // Windows paths — case-insensitive. Materialize the (possibly one-shot)
            // enumerable so we can test membership.
            var pathSet = new System.Collections.Generic.HashSet<string>(
                paths ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            // Serialize every guarded write window app-wide: only one caller may be
            // inside its action() (the FLAC write) at a time. This closes the whole
            // concurrent-writer class (save+save, save+fingerprint, save+MBID) that
            // produced the "used by another process" data-loss lock — TagLib opens the
            // file for exclusive write in Save(), so two overlapping writers collide.
            // Non-reentrant SemaphoreSlim is safe: no action passed here re-enters
            // WithFileReleased (verified). Callers run this inside Task.Run, so the
            // blocking Wait() only ever parks a background thread, never the UI thread.
            _writeGate.Wait();
            try
            {
                // Nothing loaded, or the loaded file isn't a write target: run the action
                // untouched so unrelated saves never interrupt playback. Still serialized:
                // this branch also performs the write, so it must hold the gate.
                if (_currentFilePath == null || !pathSet.Contains(_currentFilePath))
                {
                    action();
                    return;
                }

                // The loaded file is about to be written — capture state (position must be
                // read before Stop() disposes the reader), then release the handle.
                var capturedTrack = _currentTrack;
                var capturedPosition = CurrentPosition;
                var capturedState = _playbackState;

                Stop();  // disposes AudioFileReader/WaveOutEvent and nulls _currentFilePath

                try
                {
                    action();
                }
                finally
                {
                    // Restore the player even if the action threw, so a failed write does
                    // not leave playback dead. (capturedTrack/Stopped are defensive: when
                    // _currentFilePath was non-null the state is Playing or Paused.)
                    if (capturedTrack != null)
                    {
                        if (capturedState == PlaybackState.Playing)
                        {
                            Play(capturedTrack);
                            Seek(capturedPosition);
                        }
                        else if (capturedState == PlaybackState.Paused)
                        {
                            // No load-without-play primitive exists, so reload via Play()
                            // then re-pause at the captured position. The reader starts
                            // briefly before Pause(), producing a short audible blip —
                            // accepted trade-off until a true paused-load is added.
                            Play(capturedTrack);
                            Seek(capturedPosition);
                            Pause();
                        }
                    }
                }
            }
            finally
            {
                _writeGate.Release();
            }
        }

        /// <summary>
        /// Loads a new playlist (replaces current playlist).
        /// </summary>
        public void LoadPlaylist(System.Collections.Generic.IEnumerable<TrackInfo> tracks)
        {
            _playlist.Clear();
            foreach (var track in tracks)
            {
                _playlist.Add(track);
            }
            _currentTrackIndex = -1;
        }

        /// <summary>
        /// Plays the next PLAYABLE track in the playlist, skipping any whose file is missing/moved
        /// (each raises PlaybackFailed). No-ops at end-of-playlist with nothing playable ahead.
        /// </summary>
        public void Next() => TryAdvance(+1);

        /// <summary>
        /// Plays the previous PLAYABLE track in the playlist, skipping any whose file is missing.
        /// No-ops at start-of-playlist with nothing playable behind.
        /// </summary>
        public void Previous() => TryAdvance(-1);

        /// <summary>
        /// Advances the playlist cursor to the next playable track in <paramref name="direction"/>
        /// (+1 forward, -1 back) and plays it. Returns true if a track started, false if none
        /// remained playable that way. Skipped missing files each raise PlaybackFailed. The advance
        /// is ITERATIVE (see NextPlayableIndex) — an all-missing run cannot recurse unbounded.
        /// </summary>
        private bool TryAdvance(int direction)
        {
            if (_playlist.Count == 0)
                return false;

            int idx = NextPlayableIndex(_currentTrackIndex, direction);
            if (idx < 0)
                return false;

            _currentTrackIndex = idx;
            Play(_playlist[idx]);
            return true;
        }

        /// <summary>
        /// Scans the playlist from <paramref name="currentIndex"/> outward in <paramref name="direction"/>
        /// (+1/-1) and returns the index of the first track whose file exists on disk, or -1 if none.
        /// Every track skipped for a missing/empty path raises PlaybackFailed. Purely iterative and
        /// bounded by the playlist length — no recursion, so consecutive missing files cannot overflow.
        /// Internal for unit testing the skip decision without decoding audio.
        /// </summary>
        internal int NextPlayableIndex(int currentIndex, int direction)
        {
            for (int i = currentIndex + direction; i >= 0 && i < _playlist.Count; i += direction)
            {
                var track = _playlist[i];
                if (!string.IsNullOrEmpty(track.FilePath) && System.IO.File.Exists(track.FilePath))
                    return i;

                // Missing file — skip it and tell the UI why.
                RaisePlaybackFailed(track, "File not found");
            }

            return -1;
        }

        /// <summary>
        /// Raises <see cref="PlaybackFailed"/>. Central so every degrade path (direct play,
        /// skip-on-advance, defensive LoadFile) reports uniformly.
        /// </summary>
        private void RaisePlaybackFailed(TrackInfo? track, string reason)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[AudioPlayerService] PlaybackFailed: {reason} - {track?.FilePath}");
            PlaybackFailed?.Invoke(this, new PlaybackFailedEventArgs(track, reason));
        }

        /// <summary>
        /// Sets the playback state and raises PlaybackStateChanged event.
        /// </summary>
        private void SetPlaybackState(PlaybackState newState)
        {
            if (_playbackState != newState)
            {
                _playbackState = newState;
                PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            // NAudio's WaveOutEvent may fire PlaybackStopped when Pause() is called
            // (the background read thread stops). Ignore this event if we're paused —
            // the Paused state was explicitly set by the user and must be preserved.
            if (_playbackState == PlaybackState.Paused)
                return;

            // Check if this was a natural end-of-track (not user-initiated stop and no exception)
            bool wasNaturalEnd = !_userInitiatedStop && e.Exception == null;
            _userInitiatedStop = false;  // Reset flag

            if (wasNaturalEnd)
            {
                // Auto-advance, skipping any missing-file tracks (each banners via PlaybackFailed).
                // If nothing playable remains ahead — true end of playlist, or an all-missing tail —
                // end cleanly at Stopped rather than wedging or crashing.
                if (!TryAdvance(+1))
                {
                    SetPlaybackState(PlaybackState.Stopped);
                    PlaybackStopped?.Invoke(this, EventArgs.Empty);
                }
            }
            else
            {
                // User-initiated stop or a mid-track decode error - transition to Stopped
                SetPlaybackState(PlaybackState.Stopped);
                PlaybackStopped?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
