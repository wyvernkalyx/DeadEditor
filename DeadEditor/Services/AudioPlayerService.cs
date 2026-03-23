using DeadEditor.Models;
using NAudio.Wave;
using System;
using System.Collections.ObjectModel;
using System.Linq;

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
    /// Singleton playback service managing global audio playback across all windows.
    /// Only one instance exists per application lifetime.
    /// </summary>
    public class AudioPlayerService : IDisposable
    {
        // Singleton instance
        private static AudioPlayerService? _instance;
        private static readonly object _lock = new object();

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
            get => _audioFileReader?.Volume ?? 0.75f;
            set
            {
                if (_audioFileReader != null)
                {
                    _audioFileReader.Volume = Math.Clamp(value, 0f, 1f);
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
            try
            {
                Stop();

                _currentFilePath = filePath;
                System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] LoadFile: {filePath}");

                _audioFileReader = new AudioFileReader(filePath);

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
                Play();
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
        /// Plays the next track in the playlist.
        /// </summary>
        public void Next()
        {
            if (_playlist.Count == 0)
                return;

            if (_currentTrackIndex < _playlist.Count - 1)
            {
                _currentTrackIndex++;
                var nextTrack = _playlist[_currentTrackIndex];
                Play(nextTrack);
            }
        }

        /// <summary>
        /// Plays the previous track in the playlist.
        /// </summary>
        public void Previous()
        {
            if (_playlist.Count == 0)
                return;

            if (_currentTrackIndex > 0)
            {
                _currentTrackIndex--;
                var previousTrack = _playlist[_currentTrackIndex];
                Play(previousTrack);
            }
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
            // Check if this was a natural end-of-track (not user-initiated stop and no exception)
            bool wasNaturalEnd = !_userInitiatedStop && e.Exception == null;
            _userInitiatedStop = false;  // Reset flag

            if (wasNaturalEnd && _currentTrackIndex < _playlist.Count - 1)
            {
                // Auto-advance to next track
                Next();
            }
            else
            {
                // End of playlist or user-initiated stop - transition to Stopped
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
