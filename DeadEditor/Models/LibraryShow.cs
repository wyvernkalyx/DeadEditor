using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DeadEditor.Models
{
    /// <summary>
    /// LibraryShow - Represents a concert/album in the library browser
    /// Supports audience recordings, official releases, and studio albums
    /// </summary>
    public class LibraryShow
    {
        // Album type (defaults to Audience Recording)
        public AlbumType Type { get; set; } = AlbumType.AudienceRecording;

        // True when Type was set from the ALBUMTYPE tag in the audio file (source of truth),
        // false when inferred from which folder-scanning method loaded this show.
        public bool TypeFromTag { get; set; }

        // Live recording properties
        public string Date { get; set; } = "";
        public string Venue { get; set; } = "";
        public string City { get; set; } = "";
        public string State { get; set; } = "";
        public string Location { get; set; } = "";
        public string OfficialRelease { get; set; } = "";

        // Multi-date/multi-venue support for official releases
        // Internal so MergeOfficialReleasesByAlbumName can access without triggering lazy load
        internal List<string>? _containsDates;
        internal bool _containsDatesLoaded;

        public List<string> ContainsDates
        {
            get
            {
                if (!_containsDatesLoaded)
                {
                    // Trigger lazy load of track titles, which also populates ContainsDates
                    _ = TrackTitles;
                }
                return _containsDates ?? new List<string>();
            }
            set
            {
                _containsDates = value;
                _containsDatesLoaded = true;
            }
        }

        public List<string> ContainsVenues { get; set; } = new List<string>();

        // Studio album properties
        public string AlbumName { get; set; } = "";
        public int? ReleaseYear { get; set; }
        public string Edition { get; set; } = "";

        // Common properties
        public int TrackCount { get; set; }
        public List<string> FolderPaths { get; set; } = new List<string>();

        // Track titles — lazy-loaded on first access (reads TagLib tags from audio files).
        // NOT loaded at startup to avoid the 50+ second penalty of opening every audio file.
        private List<string>? _trackTitles;
        private bool _trackTitlesLoaded;

        public List<string> TrackTitles
        {
            get
            {
                if (!_trackTitlesLoaded)
                {
                    _trackTitlesLoaded = true;
                    _trackTitles = LoadTrackTitlesFromDisk();

                    // Also populate ContainsDates if not already set
                    if (!_containsDatesLoaded)
                    {
                        _containsDatesLoaded = true;
                        _containsDates = ExtractDatesFromTitles(_trackTitles);
                        // Update the Date field if it was empty
                        if (string.IsNullOrEmpty(Date) && _containsDates.Count > 0)
                            Date = _containsDates[0];
                    }
                }
                return _trackTitles ?? new List<string>();
            }
            set
            {
                _trackTitles = value;
                _trackTitlesLoaded = true;
            }
        }

        private List<string> LoadTrackTitlesFromDisk()
        {
            var titles = new List<string>();
            var folders = FolderPaths.Count > 0 ? FolderPaths : new List<string>();
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                try
                {
                    var audioFiles = Directory.GetFiles(folder, "*.flac")
                        .Concat(Directory.GetFiles(folder, "*.mp3"));
                    foreach (var file in audioFiles)
                    {
                        try
                        {
                            using var tagFile = TagLib.File.Create(file);
                            var title = tagFile.Tag.Title;
                            if (!string.IsNullOrEmpty(title))
                                titles.Add(title);
                        }
                        catch { }
                    }
                }
                catch { }
            }
            return titles;
        }

        private static List<string> ExtractDatesFromTitles(List<string> titles)
        {
            var dates = new HashSet<string>();
            var regex = new System.Text.RegularExpressions.Regex(@"\((\d{4}-\d{2}-\d{2})");
            foreach (var title in titles)
            {
                var match = regex.Match(title);
                if (match.Success)
                    dates.Add(match.Groups[1].Value);
            }
            return dates.OrderBy(d => d).ToList();
        }

        // Backward-compatible property for code not yet updated
        public string FolderPath
        {
            get => FolderPaths.FirstOrDefault() ?? "";
            set
            {
                if (FolderPaths.Count == 0)
                    FolderPaths.Add(value);
                else
                    FolderPaths[0] = value;
            }
        }

        // Heady version indicator — shows ⚡ if any track date has a heady version.
        // IMPORTANT: Only checks dates that are already loaded to avoid triggering
        // the expensive lazy-load of track titles from disk (50+ seconds for all albums).
        // ContainsDates is only accessed if _containsDatesLoaded is already true.
        public string HeadyIcon
        {
            get
            {
                var heady = HeadyVersionService.Instance;
                if (!string.IsNullOrEmpty(Date) && heady.HasHeadyVersionsOnDate(Date))
                    return "\u26A1";
                if (_containsDatesLoaded && _containsDates != null)
                {
                    foreach (var d in _containsDates)
                    {
                        if (heady.HasHeadyVersionsOnDate(d))
                            return "\u26A1";
                    }
                }
                return "";
            }
        }

        /// <summary>
        /// Tooltip text for the heady icon listing the heady songs on this show's dates.
        /// Same lazy-load guard as HeadyIcon.
        /// </summary>
        public string HeadyTooltip
        {
            get
            {
                var heady = HeadyVersionService.Instance;
                var dates = new List<string>();
                if (!string.IsNullOrEmpty(Date)) dates.Add(Date);
                if (_containsDatesLoaded && _containsDates != null)
                {
                    foreach (var d in _containsDates)
                        if (!dates.Contains(d)) dates.Add(d);
                }

                var lines = new List<string>();
                foreach (var d in dates)
                {
                    foreach (var v in heady.GetHeadyVersionsForDate(d))
                    {
                        lines.Add($"#{v.Rank} {v.Song} ({v.Votes} votes)");
                    }
                }
                return lines.Count > 0 ? string.Join("\n", lines) : "";
            }
        }

        // Smart display properties that adapt based on type
        public string TypeIcon => Type == AlbumType.OfficialRelease ? "📀" : "🎸";

        public string PrimaryInfo =>
            Type == AlbumType.OfficialRelease
                ? (!string.IsNullOrEmpty(OfficialRelease) ? OfficialRelease : AlbumName)
                : Date;

        public string SecondaryInfo =>
            Type == AlbumType.OfficialRelease
                ? (ContainsDates.Count > 1 ? string.Join(", ", ContainsDates) : (ReleaseYear.HasValue ? ReleaseYear.Value.ToString() : Date))
                : Venue;

        public string TertiaryInfo =>
            Type == AlbumType.OfficialRelease
                ? (ContainsVenues.Count > 1 ? string.Join(", ", ContainsVenues) : Edition)
                : Location;
    }
}
