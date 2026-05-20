using System;
using System.Collections.Generic;

namespace DeadEditor.Models
{
    /// <summary>
    /// Captures the verified metadata state of an imported album.
    /// Stored as a sidecar JSON file adjacent to the album folder.
    /// </summary>
    public class AlbumManifest
    {
        public int Version { get; set; } = 2;
        public string FolderName { get; set; } = "";
        public string AlbumName { get; set; } = "";
        public string AlbumType { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Date { get; set; } = "";
        public string Venue { get; set; } = "";
        public string City { get; set; } = "";
        public string State { get; set; } = "";
        public string Edition { get; set; } = "";
        public string OfficialRelease { get; set; } = "";
        public string Year { get; set; } = "";
        public bool Verified { get; set; }
        public string ArchivistNote { get; set; } = "";
        public DateTime ManifestSavedAt { get; set; }
        public List<ManifestTrack> Tracks { get; set; } = new();
    }

    public class ManifestTrack
    {
        public string Filename { get; set; } = "";
        public int TrackNumber { get; set; }
        public int DiscNumber { get; set; }
        public string Title { get; set; } = "";
        public string SongName { get; set; } = "";
        public string TrackDate { get; set; } = "";
        public bool Segue { get; set; }
        public string AcoustIdFingerprint { get; set; } = "";
    }
}
