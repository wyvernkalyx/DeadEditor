# Dead Editor - Technical Specification Document

## Project Overview

A simple, focused Windows desktop application for editing FLAC metadata with emphasis on Grateful Dead concert recordings. The application reads existing metadata from FLAC files, allows editing, normalizes song titles against a known database, and writes the corrected metadata back to the files.

**Primary Goal**: Actually write correct metadata to FLAC files. Not filenames. The embedded metadata tags.

---

## Technology Stack

| Component | Choice | Reason |
|-----------|--------|--------|
| Framework | .NET 8.0 | Current LTS, Windows desktop support |
| UI | WPF | Native Windows, proven with TagLibSharp |
| Metadata | TagLibSharp 2.3.0 | Proven to work for FLAC read/write |
| Data | JSON files | Simple, no database complexity |
| Styling | Default WPF (Phase 1) | Focus on functionality first |

---

## Project Structure

```
DeadEditor/
├── DeadEditor.sln
├── DeadEditor/
│   ├── DeadEditor.csproj
│   ├── App.xaml
│   ├── App.xaml.cs
│   ├── MainWindow.xaml
│   ├── MainWindow.xaml.cs
│   ├── Models/
│   │   ├── TrackInfo.cs
│   │   ├── AlbumInfo.cs
│   │   └── SongDatabase.cs
│   ├── Services/
│   │   ├── MetadataService.cs      # TagLibSharp read/write
│   │   ├── NormalizationService.cs # Song title matching
│   │   └── FileService.cs          # Folder scanning
│   └── Data/
│       └── songs.json              # Normalized song database
```

---

## Data Models

### TrackInfo.cs
```csharp
namespace DeadEditor.Models
{
    public class TrackInfo
    {
        public string FilePath { get; set; }           // Full path to FLAC file
        public string FileName { get; set; }           // Just the filename
        public int TrackNumber { get; set; }           // Track #
        public string Title { get; set; }              // Current title from metadata
        public string NormalizedTitle { get; set; }    // After normalization (null if not matched)
        public string PerformanceDate { get; set; }    // yyyy-MM-dd format
        public bool HasSegue { get; set; }             // Transitions to next track
        public string Duration { get; set; }           // MM:SS format (read-only, from file)
        public bool IsModified { get; set; }           // Has user made changes?
        
        // Computed property for final title
        public string FinalTitle => 
            $"{NormalizedTitle ?? Title} ({PerformanceDate})";
    }
}
```

### AlbumInfo.cs
```csharp
namespace DeadEditor.Models
{
    public class AlbumInfo
    {
        public string FolderPath { get; set; }         // Path to the folder
        public string Artist { get; set; }             // Default: "Grateful Dead"
        public string Date { get; set; }               // yyyy-MM-dd
        public string Venue { get; set; }              // e.g., "Barton Hall"
        public string City { get; set; }               // e.g., "Ithaca"
        public string State { get; set; }              // e.g., "NY"
        public string OfficialRelease { get; set; }    // e.g., "Dave's Picks Vol. 29" (optional)
        public bool IsModified { get; set; }
        
        // Computed property for album title
        public string AlbumTitle
        {
            get
            {
                var baseTitle = $"{Date} - {Venue} - {City}, {State}";
                if (!string.IsNullOrEmpty(OfficialRelease))
                    return $"{baseTitle} : {OfficialRelease}";
                return baseTitle;
            }
        }
    }
}
```

### SongDatabase.cs
```csharp
namespace DeadEditor.Models
{
    public class SongEntry
    {
        public string OfficialTitle { get; set; }      // The correct, normalized title
        public List<string> Aliases { get; set; }      // Alternative spellings/abbreviations
    }
    
    public class SongDatabase
    {
        public List<SongEntry> Songs { get; set; }
    }
}
```

---

## UI Specification

### MainWindow Layout

```
┌─────────────────────────────────────────────────────────────────────────┐
│  Dead Editor                                                    [─][□][×]│
├─────────────────────────────────────────────────────────────────────────┤
│  Folder: [_________________________________________________] [Browse...] │
│                                                                          │
├────────────────────────────────┬────────────────────────────────────────┤
│  ALBUM INFORMATION             │  TRACK LIST                            │
│  ──────────────────            │  ──────────                            │
│                                │                                        │
│  Artist:                       │  ┌────┬─────────────────────────┬────┐ │
│  [Grateful Dead            ]   │  │ #  │ Title                   │ →  │ │
│                                │  ├────┼─────────────────────────┼────┤ │
│  Date:                         │  │ 1  │ Minglewood Blues        │ ☐  │ │
│  [1977-05-08]                  │  │ 2  │ Loser                   │ ☐  │ │
│                                │  │ 3  │ El Paso                 │ ☑  │ │
│  Venue:                        │  │ 4  │ They Love Each Other    │ ☐  │ │
│  [Barton Hall              ]   │  │ 5  │ Jack Straw              │ ☑  │ │
│                                │  │ 6  │ Deal                    │ ☐  │ │
│  City:                         │  │ 7  │ Lazy Lightnin           │ ☑  │ │
│  [Ithaca                   ]   │  │ 8  │ Supplication            │ ☐  │ │
│                                │  │ ...│                         │    │ │
│  State:                        │  └────┴─────────────────────────┴────┘ │
│  [NY                       ]   │                                        │
│                                │  Selected: Lazy Lightnin               │
│  Official Release (optional):  │  ┌────────────────────────────────────┐│
│  [Dave's Picks Vol. 29     ]   │  │ Title: [Lazy Lightning         ]  ││
│                                │  │ Date:  [1977-05-08]               ││
│  ──────────────────            │  │ Segue: [☑]                        ││
│  Album Title Preview:          │  └────────────────────────────────────┘│
│  ┌────────────────────────────┐│                                        │
│  │ 1977-05-08 - Barton Hall - ││                                        │
│  │ Ithaca, NY : Dave's Picks  ││                                        │
│  │ Vol. 29                    ││                                        │
│  └────────────────────────────┘│                                        │
│                                │                                        │
├────────────────────────────────┴────────────────────────────────────────┤
│                                                                          │
│  [Read from Files]    [Normalize All Songs]    [Write to Files]          │
│                                                                          │
│  Status: Ready                                                           │
└─────────────────────────────────────────────────────────────────────────┘
```

### UI Behavior Requirements

1. **Folder Selection**
   - Browse button opens FolderBrowserDialog
   - After selection, automatically scans for FLAC files
   - Automatically calls "Read from Files"

2. **Album Information Panel**
   - All fields are editable TextBoxes
   - Changes update the "Album Title Preview" in real-time
   - Artist defaults to "Grateful Dead"

3. **Track List**
   - DataGrid with columns: #, Title, → (segue checkbox)
   - Single-click selects row and populates "Selected Track" editor below
   - Segue checkbox (→) can be toggled directly in grid
   - Rows with unmatched titles (after normalization attempt) should have yellow background

4. **Selected Track Editor**
   - Shows details of currently selected track
   - Title field is editable
   - Date field defaults to album date but can be overridden (for compilations)
   - Segue checkbox

5. **Action Buttons**
   - **Read from Files**: Reads metadata from all FLAC files in folder
   - **Normalize All Songs**: Attempts to match all titles against song database
   - **Write to Files**: Writes updated metadata to all FLAC files

6. **Status Bar**
   - Shows current operation status
   - Shows count: "12 tracks loaded" or "8 of 12 songs normalized"

---

## Core Services

### MetadataService.cs

```csharp
using TagLib;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DeadEditor.Services
{
    public class MetadataService
    {
        /// <summary>
        /// Reads metadata from all FLAC files in a folder
        /// </summary>
        public List<TrackInfo> ReadFolder(string folderPath)
        {
            var tracks = new List<TrackInfo>();
            var flacFiles = Directory.GetFiles(folderPath, "*.flac")
                                     .OrderBy(f => f)
                                     .ToList();
            
            foreach (var filePath in flacFiles)
            {
                try
                {
                    using (var file = TagLib.File.Create(filePath))
                    {
                        var track = new TrackInfo
                        {
                            FilePath = filePath,
                            FileName = Path.GetFileName(filePath),
                            TrackNumber = (int)file.Tag.Track,
                            Title = file.Tag.Title ?? Path.GetFileNameWithoutExtension(filePath),
                            Duration = file.Properties.Duration.ToString(@"mm\:ss"),
                            IsModified = false
                        };
                        
                        // Try to extract date from existing title if in format "Song (yyyy-MM-dd)"
                        track.PerformanceDate = ExtractDateFromTitle(track.Title) 
                                               ?? ExtractDateFromAlbum(file.Tag.Album);
                        
                        // Clean the title (remove date suffix if present)
                        track.Title = CleanTitle(track.Title);
                        
                        tracks.Add(track);
                    }
                }
                catch (Exception ex)
                {
                    // Log error but continue with other files
                    System.Diagnostics.Debug.WriteLine($"Error reading {filePath}: {ex.Message}");
                }
            }
            
            // If no track numbers, assign based on order
            if (tracks.All(t => t.TrackNumber == 0))
            {
                for (int i = 0; i < tracks.Count; i++)
                {
                    tracks[i].TrackNumber = i + 1;
                }
            }
            
            return tracks.OrderBy(t => t.TrackNumber).ToList();
        }
        
        /// <summary>
        /// Reads album-level metadata from first file in folder
        /// </summary>
        public AlbumInfo ReadAlbumInfo(string folderPath, List<TrackInfo> tracks)
        {
            var albumInfo = new AlbumInfo
            {
                FolderPath = folderPath,
                Artist = "Grateful Dead"
            };
            
            var firstFile = tracks.FirstOrDefault()?.FilePath;
            if (firstFile != null)
            {
                using (var file = TagLib.File.Create(firstFile))
                {
                    albumInfo.Artist = file.Tag.FirstPerformer ?? "Grateful Dead";
                    
                    // Try to parse album title
                    var album = file.Tag.Album;
                    if (!string.IsNullOrEmpty(album))
                    {
                        ParseAlbumTitle(album, albumInfo);
                    }
                    
                    // Try to get date
                    if (file.Tag.Year > 0)
                    {
                        // Year only - might need to extract full date from album
                        var dateFromAlbum = ExtractDateFromAlbum(album);
                        albumInfo.Date = dateFromAlbum ?? $"{file.Tag.Year}-01-01";
                    }
                }
            }
            
            return albumInfo;
        }
        
        /// <summary>
        /// Writes metadata to all FLAC files
        /// </summary>
        public void WriteMetadata(AlbumInfo album, List<TrackInfo> tracks)
        {
            foreach (var track in tracks)
            {
                using (var file = TagLib.File.Create(track.FilePath))
                {
                    // Build the final title with date
                    var date = track.PerformanceDate ?? album.Date;
                    var title = track.NormalizedTitle ?? track.Title;
                    
                    // Add segue marker if needed
                    if (track.HasSegue)
                    {
                        title = title + " →";
                    }
                    
                    file.Tag.Title = $"{title} ({date})";
                    file.Tag.Album = album.AlbumTitle;
                    file.Tag.Performers = new[] { album.Artist };
                    file.Tag.AlbumArtists = new[] { album.Artist };
                    file.Tag.Track = (uint)track.TrackNumber;
                    
                    // Parse year from date
                    if (DateTime.TryParse(album.Date, out var parsedDate))
                    {
                        file.Tag.Year = (uint)parsedDate.Year;
                    }
                    
                    file.Save();
                }
            }
        }
        
        // Helper methods
        private string ExtractDateFromTitle(string title)
        {
            // Match pattern: "Song Name (yyyy-MM-dd)"
            var match = System.Text.RegularExpressions.Regex.Match(
                title, @"\((\d{4}-\d{2}-\d{2})\)\s*$");
            return match.Success ? match.Groups[1].Value : null;
        }
        
        private string ExtractDateFromAlbum(string album)
        {
            if (string.IsNullOrEmpty(album)) return null;
            
            // Match pattern: "yyyy-MM-dd - ..."
            var match = System.Text.RegularExpressions.Regex.Match(
                album, @"^(\d{4}-\d{2}-\d{2})");
            return match.Success ? match.Groups[1].Value : null;
        }
        
        private string CleanTitle(string title)
        {
            // Remove date suffix and segue marker
            var cleaned = System.Text.RegularExpressions.Regex.Replace(
                title, @"\s*→?\s*\(\d{4}-\d{2}-\d{2}\)\s*$", "");
            return cleaned.TrimEnd('→', ' ');
        }
        
        private void ParseAlbumTitle(string album, AlbumInfo info)
        {
            // Pattern: "yyyy-MM-dd - Venue - City, State" or with " : Release"
            var match = System.Text.RegularExpressions.Regex.Match(
                album, 
                @"^(\d{4}-\d{2}-\d{2})\s*-\s*([^-]+)\s*-\s*([^,]+),\s*([A-Z]{2})(?:\s*:\s*(.+))?$");
            
            if (match.Success)
            {
                info.Date = match.Groups[1].Value;
                info.Venue = match.Groups[2].Value.Trim();
                info.City = match.Groups[3].Value.Trim();
                info.State = match.Groups[4].Value.Trim();
                if (match.Groups[5].Success)
                {
                    info.OfficialRelease = match.Groups[5].Value.Trim();
                }
            }
        }
    }
}
```

### NormalizationService.cs

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace DeadEditor.Services
{
    public class NormalizationService
    {
        private SongDatabase _database;
        private Dictionary<string, string> _aliasLookup;
        
        public NormalizationService()
        {
            LoadDatabase();
        }
        
        private void LoadDatabase()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "songs.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                _database = JsonConvert.DeserializeObject<SongDatabase>(json);
            }
            else
            {
                _database = new SongDatabase { Songs = new List<SongEntry>() };
            }
            
            // Build lookup dictionary
            _aliasLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var song in _database.Songs)
            {
                // Add official title as its own lookup
                _aliasLookup[song.OfficialTitle] = song.OfficialTitle;
                
                // Add all aliases
                foreach (var alias in song.Aliases ?? new List<string>())
                {
                    _aliasLookup[alias] = song.OfficialTitle;
                }
            }
        }
        
        /// <summary>
        /// Attempts to normalize a single song title
        /// </summary>
        public string Normalize(string title)
        {
            if (string.IsNullOrEmpty(title)) return null;
            
            // Clean the title first
            var cleaned = title.Trim();
            
            // Direct lookup
            if (_aliasLookup.TryGetValue(cleaned, out var official))
            {
                return official;
            }
            
            // Try without common suffixes
            var withoutSuffix = cleaned
                .Replace(" (1)", "")
                .Replace(" (2)", "")
                .Replace(" Reprise", "")
                .Replace(" reprise", "")
                .Trim();
            
            if (_aliasLookup.TryGetValue(withoutSuffix, out official))
            {
                return official;
            }
            
            return null; // No match found
        }
        
        /// <summary>
        /// Normalizes all tracks, returns count of successful matches
        /// </summary>
        public int NormalizeAll(List<TrackInfo> tracks)
        {
            int matched = 0;
            foreach (var track in tracks)
            {
                var normalized = Normalize(track.Title);
                if (normalized != null)
                {
                    track.NormalizedTitle = normalized;
                    matched++;
                }
            }
            return matched;
        }
        
        /// <summary>
        /// Gets all known song titles for autocomplete
        /// </summary>
        public List<string> GetAllTitles()
        {
            return _database.Songs.Select(s => s.OfficialTitle).OrderBy(t => t).ToList();
        }
        
        /// <summary>
        /// Adds a new song to the database
        /// </summary>
        public void AddSong(string officialTitle, List<string> aliases = null)
        {
            if (_database.Songs.Any(s => s.OfficialTitle.Equals(officialTitle, StringComparison.OrdinalIgnoreCase)))
                return;
            
            _database.Songs.Add(new SongEntry
            {
                OfficialTitle = officialTitle,
                Aliases = aliases ?? new List<string>()
            });
            
            SaveDatabase();
            LoadDatabase(); // Reload to update lookup
        }
        
        private void SaveDatabase()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "songs.json");
            var json = JsonConvert.SerializeObject(_database, Formatting.Indented);
            File.WriteAllText(path, json);
        }
    }
}
```

---

## Starter Song Database (songs.json)

```json
{
  "Songs": [
    {
      "OfficialTitle": "Alabama Getaway",
      "Aliases": ["Alabama"]
    },
    {
      "OfficialTitle": "Althea",
      "Aliases": []
    },
    {
      "OfficialTitle": "Around and Around",
      "Aliases": ["Around & Around"]
    },
    {
      "OfficialTitle": "Beat It On Down the Line",
      "Aliases": ["Beat It On Down", "BIODTL"]
    },
    {
      "OfficialTitle": "Bertha",
      "Aliases": []
    },
    {
      "OfficialTitle": "Big River",
      "Aliases": []
    },
    {
      "OfficialTitle": "Bird Song",
      "Aliases": ["Birdsong"]
    },
    {
      "OfficialTitle": "Black Peter",
      "Aliases": []
    },
    {
      "OfficialTitle": "Box of Rain",
      "Aliases": []
    },
    {
      "OfficialTitle": "Brokedown Palace",
      "Aliases": ["Broke Down Palace"]
    },
    {
      "OfficialTitle": "Brown-Eyed Women",
      "Aliases": ["Brown Eyed Women", "BEW"]
    },
    {
      "OfficialTitle": "Cassidy",
      "Aliases": []
    },
    {
      "OfficialTitle": "China Cat Sunflower",
      "Aliases": ["China Cat", "CCS"]
    },
    {
      "OfficialTitle": "Cold Rain and Snow",
      "Aliases": ["Cold Rain & Snow"]
    },
    {
      "OfficialTitle": "Cumberland Blues",
      "Aliases": []
    },
    {
      "OfficialTitle": "Dancing in the Street",
      "Aliases": ["Dancing In The Streets", "DITS"]
    },
    {
      "OfficialTitle": "Dark Star",
      "Aliases": []
    },
    {
      "OfficialTitle": "Deal",
      "Aliases": []
    },
    {
      "OfficialTitle": "Dire Wolf",
      "Aliases": []
    },
    {
      "OfficialTitle": "Drums",
      "Aliases": ["Drum Solo"]
    },
    {
      "OfficialTitle": "El Paso",
      "Aliases": []
    },
    {
      "OfficialTitle": "Estimated Prophet",
      "Aliases": ["Estimated"]
    },
    {
      "OfficialTitle": "Eyes of the World",
      "Aliases": ["Eyes", "EOTW"]
    },
    {
      "OfficialTitle": "Fire on the Mountain",
      "Aliases": ["Fire", "FOTM"]
    },
    {
      "OfficialTitle": "Franklin's Tower",
      "Aliases": ["Franklins Tower", "Franklin"]
    },
    {
      "OfficialTitle": "Friend of the Devil",
      "Aliases": ["FOTD", "Friend Of The Devil"]
    },
    {
      "OfficialTitle": "Going Down the Road Feeling Bad",
      "Aliases": ["GDTRFB", "Goin Down The Road", "Going Down The Road"]
    },
    {
      "OfficialTitle": "Good Lovin'",
      "Aliases": ["Good Lovin", "Good Loving"]
    },
    {
      "OfficialTitle": "Greatest Story Ever Told",
      "Aliases": ["Greatest Story", "GSET"]
    },
    {
      "OfficialTitle": "Half Step",
      "Aliases": ["Half-Step", "Mississippi Half-Step Uptown Toodeloo"]
    },
    {
      "OfficialTitle": "He's Gone",
      "Aliases": ["Hes Gone", "He Is Gone"]
    },
    {
      "OfficialTitle": "Help on the Way",
      "Aliases": ["Help On The Way", "HOTW"]
    },
    {
      "OfficialTitle": "Here Comes Sunshine",
      "Aliases": []
    },
    {
      "OfficialTitle": "I Know You Rider",
      "Aliases": ["Rider", "I Know You, Rider"]
    },
    {
      "OfficialTitle": "I Need a Miracle",
      "Aliases": ["Miracle", "INAM"]
    },
    {
      "OfficialTitle": "Jack Straw",
      "Aliases": ["Jackstraw"]
    },
    {
      "OfficialTitle": "Johnny B. Goode",
      "Aliases": ["Johnny B Goode", "JBG"]
    },
    {
      "OfficialTitle": "Lazy Lightning",
      "Aliases": ["Lazy Lightnin"]
    },
    {
      "OfficialTitle": "Let It Grow",
      "Aliases": []
    },
    {
      "OfficialTitle": "Looks Like Rain",
      "Aliases": []
    },
    {
      "OfficialTitle": "Loser",
      "Aliases": []
    },
    {
      "OfficialTitle": "Lost Sailor",
      "Aliases": []
    },
    {
      "OfficialTitle": "Minglewood Blues",
      "Aliases": ["All New Minglewood Blues", "New Minglewood Blues", "Minglewood"]
    },
    {
      "OfficialTitle": "Mississippi Half-Step Uptown Toodeloo",
      "Aliases": ["Half Step", "Mississippi Half Step"]
    },
    {
      "OfficialTitle": "Morning Dew",
      "Aliases": []
    },
    {
      "OfficialTitle": "Music Never Stopped",
      "Aliases": ["The Music Never Stopped", "TMNS"]
    },
    {
      "OfficialTitle": "New Speedway Boogie",
      "Aliases": ["Speedway Boogie"]
    },
    {
      "OfficialTitle": "Not Fade Away",
      "Aliases": ["NFA"]
    },
    {
      "OfficialTitle": "One More Saturday Night",
      "Aliases": ["Saturday Night", "OMSN"]
    },
    {
      "OfficialTitle": "Peggy-O",
      "Aliases": ["Peggy O", "Fennario"]
    },
    {
      "OfficialTitle": "Playing in the Band",
      "Aliases": ["Playin", "Playin'", "PITB", "Playin In The Band"]
    },
    {
      "OfficialTitle": "Promised Land",
      "Aliases": []
    },
    {
      "OfficialTitle": "Ramble On Rose",
      "Aliases": []
    },
    {
      "OfficialTitle": "Ripple",
      "Aliases": []
    },
    {
      "OfficialTitle": "Saint of Circumstance",
      "Aliases": ["St. Stephen"]
    },
    {
      "OfficialTitle": "Samson and Delilah",
      "Aliases": ["Samson & Delilah", "Samson"]
    },
    {
      "OfficialTitle": "Scarlet Begonias",
      "Aliases": ["Scarlet"]
    },
    {
      "OfficialTitle": "Shakedown Street",
      "Aliases": ["Shakedown"]
    },
    {
      "OfficialTitle": "Ship of Fools",
      "Aliases": []
    },
    {
      "OfficialTitle": "Slipknot!",
      "Aliases": ["Slipknot"]
    },
    {
      "OfficialTitle": "Space",
      "Aliases": []
    },
    {
      "OfficialTitle": "St. Stephen",
      "Aliases": ["Saint Stephen", "St Stephen"]
    },
    {
      "OfficialTitle": "Stella Blue",
      "Aliases": []
    },
    {
      "OfficialTitle": "Sugar Magnolia",
      "Aliases": ["Sugaree", "Sugar Mag"]
    },
    {
      "OfficialTitle": "Sugaree",
      "Aliases": []
    },
    {
      "OfficialTitle": "Sunrise",
      "Aliases": []
    },
    {
      "OfficialTitle": "Supplication",
      "Aliases": []
    },
    {
      "OfficialTitle": "Tennessee Jed",
      "Aliases": ["Tennesee Jed"]
    },
    {
      "OfficialTitle": "Terrapin Station",
      "Aliases": ["Terrapin"]
    },
    {
      "OfficialTitle": "The Other One",
      "Aliases": ["Other One", "TOO"]
    },
    {
      "OfficialTitle": "The Wheel",
      "Aliases": ["Wheel"]
    },
    {
      "OfficialTitle": "They Love Each Other",
      "Aliases": ["TLEO"]
    },
    {
      "OfficialTitle": "Throwing Stones",
      "Aliases": []
    },
    {
      "OfficialTitle": "Touch of Grey",
      "Aliases": ["Touch Of Gray"]
    },
    {
      "OfficialTitle": "Truckin'",
      "Aliases": ["Truckin", "Trucking"]
    },
    {
      "OfficialTitle": "Turn On Your Love Light",
      "Aliases": ["Love Light", "Lovelight"]
    },
    {
      "OfficialTitle": "U.S. Blues",
      "Aliases": ["US Blues", "U S Blues"]
    },
    {
      "OfficialTitle": "Uncle John's Band",
      "Aliases": ["Uncle Johns Band", "UJB"]
    },
    {
      "OfficialTitle": "Victim or the Crime",
      "Aliases": []
    },
    {
      "OfficialTitle": "Viola Lee Blues",
      "Aliases": []
    },
    {
      "OfficialTitle": "Wharf Rat",
      "Aliases": []
    }
  ]
}
```

---

## Implementation Order

### Step 1: Project Setup
1. Create new WPF Application (.NET 8.0)
2. Add NuGet packages:
   - `TagLibSharp` (2.3.0)
   - `Newtonsoft.Json` (13.0.3)
3. Create folder structure (Models, Services, Data)
4. Add songs.json to Data folder (set Copy to Output Directory: Copy if newer)

### Step 2: Data Models
1. Create TrackInfo.cs
2. Create AlbumInfo.cs
3. Create SongDatabase.cs (with SongEntry)

### Step 3: Core Services
1. Create MetadataService.cs with ReadFolder() method only
2. Test: Can it read FLAC files and extract metadata?

### Step 4: Basic UI
1. Create MainWindow with folder browser and track list DataGrid
2. Wire up Browse button to MetadataService.ReadFolder()
3. Display tracks in grid
4. **TEST CHECKPOINT**: Can see tracks from FLAC files

### Step 5: Album Info Panel
1. Add album info fields to UI
2. Wire up MetadataService.ReadAlbumInfo()
3. Add real-time preview of AlbumTitle
4. **TEST CHECKPOINT**: Can see and edit album info

### Step 6: Track Editing
1. Add selected track editor panel
2. Wire up track selection
3. Add segue checkbox to grid
4. **TEST CHECKPOINT**: Can edit individual tracks

### Step 7: Write Metadata
1. Implement MetadataService.WriteMetadata()
2. Add Write button with confirmation
3. **TEST CHECKPOINT**: Changes are saved to FLAC files!

### Step 8: Normalization
1. Create NormalizationService.cs
2. Add Normalize button
3. Highlight unmatched songs in yellow
4. **TEST CHECKPOINT**: Song titles get normalized

### Step 9: Polish
1. Add status bar with operation feedback
2. Add modified indicators
3. Add "unsaved changes" warning on close
4. Error handling and user feedback

---

## Testing Checklist

- [ ] Can browse to folder containing FLAC files
- [ ] Track list populates with correct track numbers and titles
- [ ] Album info fields populate from existing metadata
- [ ] Can edit album info and see preview update
- [ ] Can edit individual track titles
- [ ] Can toggle segue checkboxes
- [ ] Normalize button matches known songs
- [ ] Unmatched songs are visually highlighted
- [ ] Write button saves all changes to FLAC files
- [ ] Verify in another player (VLC, foobar2000) that metadata is correct
- [ ] Song title format: "Song Name (yyyy-MM-dd)"
- [ ] Album title format: "yyyy-MM-dd - Venue - City, State" or with " : Release"

---

## Success Criteria

**Phase 1 is complete when:**
1. You can open a folder of FLAC files
2. You can see and edit all metadata
3. You can normalize song titles against the database
4. You can write the corrected metadata back to the files
5. Another music player shows the correct metadata

**Only then do we add:**
- File format conversion (Phase 2)
- Multi-folder scanning (Phase 2)
- Audio fingerprinting (Phase 3)