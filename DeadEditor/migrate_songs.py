#!/usr/bin/env python3
"""
Migrate songs.json from flat structure to artist-based structure.
This preserves backward compatibility by keeping the old Songs array.
"""

import json
import os

# NRPS songs that should be attributed to New Riders of the Purple Sage
NRPS_SONGS = {
    "Truck Driving Man",
    "Panama Red",
    "Glendale Train",
    "Henry",
    "Last Lonely Eagle",
    "Louisiana Lady",
    "Whatcha Gonna Do",
    "Dirty Business",
    "Garden of Eden",
    "Hello Mary Lou",
    "She's No Angel",
    "Willie and the Hand Jive"
}

def migrate_songs():
    # Path to songs.json
    script_dir = os.path.dirname(os.path.abspath(__file__))
    songs_path = os.path.join(script_dir, "Data", "songs.json")

    # Read current database
    with open(songs_path, 'r', encoding='utf-8') as f:
        data = json.load(f)

    # Check if already migrated
    if "Artists" in data and data["Artists"]:
        print("Database already migrated to artist-based structure!")
        return

    # Get all songs from the flat structure
    all_songs = data.get("Songs", [])

    print(f"Found {len(all_songs)} songs to migrate")

    # Categorize songs
    grateful_dead_songs = []
    nrps_songs = []

    for song in all_songs:
        title = song["OfficialTitle"]
        if title in NRPS_SONGS:
            nrps_songs.append(song)
        else:
            grateful_dead_songs.append(song)

    # Create new structure
    artists = []

    if grateful_dead_songs:
        artists.append({
            "Name": "Grateful Dead",
            "Songs": sorted(grateful_dead_songs, key=lambda s: s["OfficialTitle"])
        })

    if nrps_songs:
        artists.append({
            "Name": "New Riders of the Purple Sage",
            "Songs": sorted(nrps_songs, key=lambda s: s["OfficialTitle"])
        })

    # Create new database structure
    new_data = {
        "Artists": artists,
        "Songs": all_songs  # Keep for backward compatibility
    }

    # Backup original file
    backup_path = songs_path + ".backup"
    with open(backup_path, 'w', encoding='utf-8') as f:
        json.dump(data, f, indent=2, ensure_ascii=False)

    print(f"Backup created: {backup_path}")

    # Write new structure
    with open(songs_path, 'w', encoding='utf-8') as f:
        json.dump(new_data, f, indent=2, ensure_ascii=False)

    print(f"\nMigration complete!")
    print(f"- Grateful Dead: {len(grateful_dead_songs)} songs")
    print(f"- New Riders of the Purple Sage: {len(nrps_songs)} songs")
    print(f"- Total: {len(all_songs)} songs")

if __name__ == "__main__":
    migrate_songs()
