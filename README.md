# Dead Editor

A focused Windows desktop application for editing FLAC metadata, specifically designed for Grateful Dead concert recordings.

## Overview

Dead Editor helps you properly tag your Grateful Dead FLAC collection with clean, normalized metadata. It reads existing metadata, normalizes song titles against a comprehensive database, and writes correctly formatted metadata back to the files.

## Features

### Phase 1 (Complete)
- **Read FLAC Metadata** - Extracts metadata from FLAC files using TagLibSharp
- **Normalize Song Titles** - Matches song titles against a database of ~95 Grateful Dead songs with common aliases
- **Edit Album & Track Info** - Full editing of artist, date, venue, city, state, and optional official release information
- **Segue Support** - Mark and automatically detect common Grateful Dead segues (China Cat > Rider, Scarlet > Fire, etc.)
- **Managed Library** - Import shows to an organized library structure: `Year\Date - Venue, City, State\`
- **Library Browser** - View all imported shows with complete track listings
- **Progress Tracking** - Visual progress bar during import operations
- **Multi-show Support** - Handles folders containing multiple concert dates
- **In-app Notifications** - Custom notification system (no system dialog boxes)

## Metadata Format

### Track Titles
```
Song Name (yyyy-MM-dd)
Song Name > (yyyy-MM-dd)  // for segues
```

### Album Titles
```
yyyy-MM-dd - Venue - City, State
yyyy-MM-dd - Venue - City, State : Official Release Name
```

### Example
**Track:** `Dark Star (1969-06-05)`
**Album:** `1969-06-05 - Fillmore West - San Francisco, CA`

## Technology Stack

- **.NET 8.0** - Cross-platform framework
- **WPF** - Windows Presentation Foundation for native UI
- **TagLibSharp 2.3.0** - FLAC metadata read/write
- **Newtonsoft.Json 13.0.3** - JSON database management

## Getting Started

### Prerequisites
- Windows 10 or later
- .NET 8.0 Runtime

### Building from Source

1. Clone the repository:
```bash
git clone https://github.com/YOUR_USERNAME/DeadEditor.git
cd DeadEditor
```

2. Build the solution:
```bash
dotnet build
```

3. Run the application:
```bash
dotnet run --project DeadEditor
```

## Usage

### Basic Workflow

1. **Browse** - Select a folder containing FLAC files
2. **Review** - Check the loaded metadata and album information
3. **Normalize** - Click "Normalize All Songs" to match titles against the database
4. **Edit** - Manually edit any fields as needed
5. **Write** - Click "Write to Files" to save metadata (for in-place editing)

### Library Workflow

1. **Set Library Root** - Choose a folder for your managed library
2. **Load Source Files** - Browse to a folder with FLAC files
3. **Normalize** - Normalize song titles
4. **Import to Library** - Copies files to organized structure and writes metadata
5. **Browse Library** - View your organized collection and track listings

## Song Database

The application includes a comprehensive database of Grateful Dead songs with common aliases:

- 95+ songs covering the full catalog
- Multiple aliases per song (abbreviations, alternate spellings)
- Support for early material (Aoxomoxoa, Live/Dead era)
- Support for classic material (1970s-1990s)

### Adding Songs

Songs are stored in `DeadEditor/Data/songs.json`:

```json
{
  "OfficialTitle": "China Cat Sunflower",
  "Aliases": ["China Cat", "CCS"]
}
```

## Project Structure

```
DeadEditor/
├── DeadEditor.sln
├── DeadEditor/
│   ├── DeadEditor.csproj
│   ├── MainWindow.xaml / .xaml.cs
│   ├── LibraryBrowserWindow.xaml / .xaml.cs
│   ├── Models/
│   │   ├── TrackInfo.cs
│   │   ├── AlbumInfo.cs
│   │   ├── SongDatabase.cs
│   │   └── LibrarySettings.cs
│   ├── Services/
│   │   ├── MetadataService.cs
│   │   ├── NormalizationService.cs
│   │   └── LibraryImportService.cs
│   └── Data/
│       └── songs.json
└── deadeditor.md (Technical Specification)
```

## Roadmap

### Phase 2 (Planned)
- File format conversion (FLAC to other formats)
- Multi-folder batch processing
- Enhanced song database management UI
- Export/import database functionality

### Phase 3 (Future)
- Audio fingerprinting integration
- Automatic show identification
- Online database integration

## Contributing

Contributions are welcome! Please feel free to submit issues or pull requests.

## License

[Choose your license - MIT, GPL, etc.]

## Acknowledgments

- Built for the Grateful Dead community
- Uses TagLibSharp for reliable FLAC metadata handling
- Song database compiled from official releases and community knowledge

## Support

For bugs, feature requests, or questions, please open an issue on GitHub.

---

**Note:** This application modifies FLAC file metadata. Always keep backups of your original files. The "Import to Library" feature creates copies, leaving originals untouched.
