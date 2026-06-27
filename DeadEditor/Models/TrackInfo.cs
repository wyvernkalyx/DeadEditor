using DeadEditor.Services;
using System.ComponentModel;

namespace DeadEditor.Models
{
    public class TrackInfo : INotifyPropertyChanged
    {
        private bool? _isMatched;
        private string _songName;
        private string _rawTitle;
        private string? _originalTitle;
        private int _trackNumber;
        private int _discNumber = 1;
        private string _trackDate;
        private bool _segue;

        public string FilePath { get; set; }           // Full path to FLAC file
        public string FileName { get; set; }           // Just the filename

        // TrackNumber is hard-clamped to >= 0. WriteMetadata casts to (uint), so a
        // negative value would silently become a huge positive; the clamp prevents
        // that corruption. Zero values are detected and surfaced by MetadataValidator.
        public int TrackNumber
        {
            get => _trackNumber;
            set
            {
                var clamped = value < 0 ? 0 : value;
                if (_trackNumber != clamped)
                {
                    _trackNumber = clamped;
                    OnPropertyChanged(nameof(TrackNumber));
                    OnPropertyChanged(nameof(DisplayTrackNumber));
                }
            }
        }
        public int DiscNumber
        {
            get => _discNumber;
            set
            {
                var clamped = value < 0 ? 0 : value;
                if (_discNumber != clamped)
                {
                    _discNumber = clamped;
                    OnPropertyChanged(nameof(DiscNumber));
                    OnPropertyChanged(nameof(DisplayTrackNumber));
                }
            }
        }

        // Computed property: Disc-aware track number for display (101, 201, 301...)
        public string DisplayTrackNumber
        {
            get
            {
                var disc = DiscNumber > 0 ? DiscNumber : 1;
                return $"{disc}{TrackNumber:D2}";
                // Disc 1, Track 7 → "107"
                // Disc 2, Track 3 → "203"
                // Disc 3, Track 12 → "312"
            }
        }

        // SongName with PropertyChanged notification for DisplayTitle binding
        public string SongName
        {
            get => _songName;
            set
            {
                if (_songName != value)
                {
                    _songName = value;
                    OnPropertyChanged(nameof(SongName));
                    OnPropertyChanged(nameof(DisplayTitle));  // Notify DisplayTitle changed
                }
            }
        }

        public string RawTitle                           // Original title as stored in file (includes date/segue)
        {
            get => _rawTitle;
            set
            {
                if (_rawTitle != value)
                {
                    _rawTitle = value;
                    OnPropertyChanged(nameof(RawTitle));
                }
            }
        }

        // The track's original title, captured once at load (the parsed original
        // song name, before any Match Setlist / Normalize can change SongName).
        // Display-only — read by Track Info's "Original Title". Unlike RawTitle,
        // it is NOT a working buffer: ReconstructRawTitles never touches it, and it
        // is never written on Save. Set-once: the first non-empty assignment sticks
        // and later writes (e.g. a manifest override) are ignored, so it stays
        // immutable for the lifetime of the in-memory track.
        public string? OriginalTitle
        {
            get => _originalTitle;
            set
            {
                if (string.IsNullOrEmpty(_originalTitle))
                    _originalTitle = value;
            }
        }
        public string TrackDate
        {
            get => _trackDate;
            set
            {
                if (_trackDate != value)
                {
                    _trackDate = value;
                    OnPropertyChanged(nameof(TrackDate));
                    OnPropertyChanged(nameof(DisplayTitle));
                }
            }
        }
        public string AlbumDate { get; set; }          // Album-level date for display fallback (not persisted to tags)
        public bool Segue
        {
            get => _segue;
            set
            {
                if (_segue != value)
                {
                    _segue = value;
                    OnPropertyChanged(nameof(Segue));
                    OnPropertyChanged(nameof(SegueDisplay));
                    OnPropertyChanged(nameof(DisplayTitle));
                }
            }
        }
        public string Duration { get; set; }           // MM:SS format (read-only, from file)
        public bool IsModified { get; set; }           // Has user made changes?
        public string? AcoustIdFingerprint { get; set; }  // ACOUSTID_FINGERPRINT (FLAC Xiph) / "Acoustid Fingerprint" TXXX (MP3). Persisted across import/write.

        // IsMatched with PropertyChanged notification for WPF DataTrigger binding
        public bool? IsMatched
        {
            get => _isMatched;
            set
            {
                if (_isMatched != value)
                {
                    _isMatched = value;
                    OnPropertyChanged(nameof(IsMatched));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Computed property: Legacy Title field for backward compatibility (maps to RawTitle if available, else SongName)
        public string Title
        {
            get => !string.IsNullOrEmpty(RawTitle) ? RawTitle : SongName;
            set => SongName = value;
        }

        // Computed property: Full display title with date and segue for library browser
        // Always reconstructs from components to ensure correct "Song > (Date)" format
        public string DisplayTitle
        {
            get
            {
                // Always reconstruct from components to ensure correct format
                // (RawTitle may have old wrong format like "Song (Date) >" from previous imports)
                var title = SongName ?? "";

                // Add segue marker if present (must come before date)
                if (Segue && !title.EndsWith(">"))
                {
                    title = title.TrimEnd() + " >";
                }

                // Add date if present (must come after segue)
                // Use track-specific date, falling back to album date for display
                var effectiveDate = !string.IsNullOrEmpty(TrackDate) ? TrackDate : AlbumDate;
                if (!string.IsNullOrEmpty(effectiveDate))
                {
                    title = $"{title} ({effectiveDate})";
                }

                return title;
            }
        }

        // Computed property: Display title with auto-appended date (what you see is what gets written)
        public string GetDisplayTitle(string albumDate)
        {
            var effectiveDate = string.IsNullOrEmpty(TrackDate) ? albumDate : TrackDate;
            var songName = SongName ?? "";

            // If effective track date differs from album date, append it
            if (!string.IsNullOrEmpty(effectiveDate) && effectiveDate != albumDate)
            {
                return $"{songName} ({effectiveDate})";
            }

            return songName;
        }

        // Computed property for segue display in library browser
        public string SegueDisplay =>
            Segue ? ">" : "";

        // Method to get final metadata title with date and segue for file writing
        public string GetFinalMetadataTitle(string albumDate)
        {
            var effectiveDate = string.IsNullOrEmpty(TrackDate) ? albumDate : TrackDate;
            var title = GetDisplayTitle(albumDate);

            // Add segue marker if present and not already in title
            if (Segue && !System.Text.RegularExpressions.Regex.IsMatch(title, @"[-–]?>|→|\[>\]\s*$"))
            {
                title = title + " >";
            }

            return title;
        }

        // Legacy property: Performance date (maps to TrackDate for backward compatibility)
        public string PerformanceDate
        {
            get => TrackDate;
            set => TrackDate = value;
        }

        // Legacy property: HasSegue (maps to Segue for backward compatibility)
        public bool HasSegue
        {
            get => Segue;
            set => Segue = value;
        }

        // Heady version indicator — shows ⚡ if this song+date is a community-rated heady version
        public string HeadyIcon
        {
            get
            {
                if (string.IsNullOrEmpty(SongName) || string.IsNullOrEmpty(TrackDate))
                    return "";
                var heady = HeadyVersionService.Instance.GetHeadyVersion(SongName, TrackDate);
                return heady != null ? "\u26A1" : "";
            }
        }

        public string HeadyTooltip
        {
            get
            {
                if (string.IsNullOrEmpty(SongName) || string.IsNullOrEmpty(TrackDate))
                    return "";
                var heady = HeadyVersionService.Instance.GetHeadyVersion(SongName, TrackDate);
                if (heady == null) return "";
                return $"\u26A1 #{heady.Rank} rated {heady.Song} on headyversion.com ({heady.Votes} votes)";
            }
        }

        public string? HeadyUrl
        {
            get
            {
                if (string.IsNullOrEmpty(SongName) || string.IsNullOrEmpty(TrackDate))
                    return null;
                var heady = HeadyVersionService.Instance.GetHeadyVersion(SongName, TrackDate);
                return heady?.Url;
            }
        }

        // Truncates a Chromaprint fingerprint for compact display (first 12 chars + ellipsis).
        // Returns em-dash for null/empty/whitespace; full value when length <= 12.
        // Callers using this for a copy-to-clipboard affordance must keep the raw value
        // separately - the truncated form is display-only.
        public static string FormatFingerprintForDisplay(string? fingerprint)
        {
            if (string.IsNullOrWhiteSpace(fingerprint)) return "—";
            if (fingerprint.Length <= 12) return fingerprint;
            return fingerprint.Substring(0, 12) + "…";
        }

        // Override Equals and GetHashCode for value-based equality (needed for IndexOf in playlists)
        // Two tracks are equal if they refer to the same file
        public override bool Equals(object? obj)
        {
            if (obj is TrackInfo other)
            {
                return string.Equals(FilePath, other.FilePath, System.StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return FilePath?.ToLowerInvariant().GetHashCode() ?? 0;
        }
    }
}
