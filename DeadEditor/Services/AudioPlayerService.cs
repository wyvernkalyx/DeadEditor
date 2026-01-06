using NAudio.Wave;
using System;

namespace DeadEditor.Services
{
    public class AudioPlayerService : IDisposable
    {
        private IWavePlayer? _wavePlayer;
        private AudioFileReader? _audioFileReader;
        private string? _currentFilePath;
        private bool _isPlaying;
        private bool _isSeeking;

        public event EventHandler? PlaybackStopped;
        public event EventHandler<TimeSpan>? PositionChanged;

        public bool IsPlaying => _isPlaying;
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

        public void LoadFile(string filePath)
        {
            Stop();

            _currentFilePath = filePath;
            _audioFileReader = new AudioFileReader(filePath);

            _wavePlayer = new WaveOutEvent();
            _wavePlayer.Init(_audioFileReader);
            _wavePlayer.PlaybackStopped += OnPlaybackStopped;
        }

        public void Play()
        {
            if (_wavePlayer == null || _audioFileReader == null)
                return;

            _wavePlayer.Play();
            _isPlaying = true;
        }

        public void Pause()
        {
            if (_wavePlayer == null)
                return;

            _wavePlayer.Pause();
            _isPlaying = false;
        }

        public void Stop()
        {
            if (_wavePlayer != null)
            {
                _wavePlayer.Stop();
                _wavePlayer.Dispose();
                _wavePlayer = null;
            }

            if (_audioFileReader != null)
            {
                _audioFileReader.Dispose();
                _audioFileReader = null;
            }

            _isPlaying = false;
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
            if (!_isSeeking && _audioFileReader != null && _isPlaying)
            {
                PositionChanged?.Invoke(this, _audioFileReader.CurrentTime);
            }
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            _isPlaying = false;
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
