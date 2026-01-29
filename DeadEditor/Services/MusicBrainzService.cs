using DeadEditor.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace DeadEditor.Services
{
    public class MusicBrainzService
    {
        private readonly string _acoustIdApiKey;
        private readonly HttpClient _httpClient;

        public MusicBrainzService(string acoustIdApiKey)
        {
            _acoustIdApiKey = acoustIdApiKey;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "DeadEditor/1.0 (https://github.com/yourrepo)");
        }

        /// <summary>
        /// Looks up album information using audio fingerprinting
        /// </summary>
        public async Task<AlbumLookupResult?> LookupAlbumAsync(List<TrackInfo> tracks)
        {
            try
            {
                Console.WriteLine($"=== STARTING ALBUM LOOKUP ===");
                Console.WriteLine($"Total tracks: {tracks.Count}");

                // Take first 3 tracks for fingerprinting (faster and more reliable)
                var tracksToFingerprint = tracks.Take(3).ToList();

                if (tracksToFingerprint.Count == 0)
                {
                    Console.WriteLine("No tracks to fingerprint");
                    return null;
                }

                Console.WriteLine($"Fingerprinting {tracksToFingerprint.Count} tracks...");

                // Get fingerprints and query AcoustID
                var recordings = new List<string>();

                foreach (var track in tracksToFingerprint)
                {
                    try
                    {
                        Console.WriteLine($"\n--- Processing: {track.FileName} ---");
                        var fingerprint = await GetFingerprintAsync(track.FilePath);

                        if (!string.IsNullOrEmpty(fingerprint))
                        {
                            Console.WriteLine($"Got fingerprint: {fingerprint.Substring(0, Math.Min(50, fingerprint.Length))}...");

                            var recordingId = await QueryAcoustIdAsync(fingerprint, track.FilePath);
                            if (!string.IsNullOrEmpty(recordingId))
                            {
                                Console.WriteLine($"Found recording ID: {recordingId}");
                                recordings.Add(recordingId);
                            }
                            else
                            {
                                Console.WriteLine("No recording ID found in AcoustID");
                            }
                        }
                        else
                        {
                            Console.WriteLine("Failed to generate fingerprint");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing track: {ex.Message}");
                        // Skip problematic tracks
                        continue;
                    }
                }

                Console.WriteLine($"\nTotal recordings found: {recordings.Count}");

                if (recordings.Count == 0)
                {
                    Console.WriteLine("No recordings found - returning null");
                    return null;
                }

                // Query MusicBrainz for album info using the recording ID
                var albumInfo = await QueryMusicBrainzAsync(recordings.First());
                return albumInfo;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error looking up album: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Search for releases by album name and artist (no fingerprinting)
        /// </summary>
        public async Task<List<ReleaseOption>?> SearchReleasesByNameAsync(string albumName, string artistName, string? year = null)
        {
            try
            {
                Console.WriteLine($"=== SEARCHING BY NAME ===");
                Console.WriteLine($"Album: {albumName}");
                Console.WriteLine($"Artist: {artistName}");
                if (!string.IsNullOrEmpty(year))
                {
                    Console.WriteLine($"Year: {year}");
                }

                // Search for release-groups matching the album name and artist
                var queryBuilder = $"releasegroup:{Uri.EscapeDataString(albumName)}%20AND%20artist:{Uri.EscapeDataString(artistName)}";

                // Add year if specified (searches for releases with that year)
                if (!string.IsNullOrEmpty(year))
                {
                    queryBuilder += $"%20AND%20date:{year}";
                }

                var searchUrl = $"https://musicbrainz.org/ws/2/release-group/?query={queryBuilder}&fmt=json&limit=10";

                var response = await _httpClient.GetAsync(searchUrl);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(json);

                var releaseGroups = data["release-groups"] as JArray;
                if (releaseGroups == null || releaseGroups.Count == 0)
                {
                    Console.WriteLine("No release-groups found");
                    return null;
                }

                Console.WriteLine($"Found {releaseGroups.Count} release-group(s)");

                var allReleaseOptions = new List<ReleaseOption>();

                // Get releases for each matching release-group
                foreach (var releaseGroup in releaseGroups.Take(3)) // Top 3 matches
                {
                    var releaseGroupId = releaseGroup["id"]?.ToString();
                    var title = releaseGroup["title"]?.ToString();
                    var primaryType = releaseGroup["primary-type"]?.ToString();

                    // Only include albums
                    if (primaryType != "Album")
                    {
                        Console.WriteLine($"Skipping {title} - not an album (type: {primaryType})");
                        continue;
                    }

                    if (string.IsNullOrEmpty(releaseGroupId))
                        continue;

                    Console.WriteLine($"Fetching releases for: {title}");

                    var releases = await GetReleasesForReleaseGroupAsync(releaseGroupId, artistName);
                    if (releases != null)
                    {
                        allReleaseOptions.AddRange(releases);
                    }

                    // Rate limiting
                    await Task.Delay(1000);
                }

                // Remove duplicates
                allReleaseOptions = allReleaseOptions
                    .GroupBy(r => new { r.Title, r.Year })
                    .Select(g => g.First())
                    .OrderBy(r => r.Year)
                    .ToList();

                Console.WriteLine($"Total unique releases found: {allReleaseOptions.Count}");

                return allReleaseOptions.Count > 0 ? allReleaseOptions : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error searching by name: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Looks up all available releases for album selection UI (using fingerprinting)
        /// </summary>
        public async Task<List<ReleaseOption>?> LookupAllReleasesAsync(List<TrackInfo> tracks)
        {
            try
            {
                Console.WriteLine($"=== STARTING ALBUM LOOKUP (WITH SELECTION) ===");
                Console.WriteLine($"Total tracks: {tracks.Count}");

                // Take first 3 tracks for fingerprinting
                var tracksToFingerprint = tracks.Take(3).ToList();

                if (tracksToFingerprint.Count == 0)
                {
                    Console.WriteLine("No tracks to fingerprint");
                    return null;
                }

                Console.WriteLine($"Fingerprinting {tracksToFingerprint.Count} tracks...");

                // Get fingerprints and query AcoustID
                var recordings = new List<string>();

                foreach (var track in tracksToFingerprint)
                {
                    try
                    {
                        Console.WriteLine($"\n--- Processing: {track.FileName} ---");
                        var fingerprint = await GetFingerprintAsync(track.FilePath);

                        if (!string.IsNullOrEmpty(fingerprint))
                        {
                            Console.WriteLine($"Got fingerprint: {fingerprint.Substring(0, Math.Min(50, fingerprint.Length))}...");

                            var recordingId = await QueryAcoustIdAsync(fingerprint, track.FilePath);
                            if (!string.IsNullOrEmpty(recordingId))
                            {
                                Console.WriteLine($"Found recording ID: {recordingId}");
                                recordings.Add(recordingId);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing track: {ex.Message}");
                        continue;
                    }
                }

                Console.WriteLine($"\nTotal recordings found: {recordings.Count}");

                if (recordings.Count == 0)
                {
                    Console.WriteLine("No recordings found - returning null");
                    return null;
                }

                // Get all releases for each recording and find common release-groups
                var allReleases = await FindCommonReleasesAsync(recordings);
                return allReleases;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error looking up releases: {ex.Message}");
                return null;
            }
        }

        private async Task<string?> GetFingerprintAsync(string filePath)
        {
            try
            {
                // Use fpcalc.exe (Chromaprint) to generate fingerprint
                var fpcalcPath = "fpcalc.exe";

                // Try to find fpcalc in various locations
                if (!File.Exists(fpcalcPath))
                {
                    // Try walking up from bin\Debug\net8.0-windows to find D:\Projects\fpcalc.exe
                    var currentDir = AppDomain.CurrentDomain.BaseDirectory;
                    Console.WriteLine($"Starting from: {currentDir}");

                    // Walk up directories until we find fpcalc.exe or run out of parents
                    var dir = new DirectoryInfo(currentDir);
                    while (dir != null && dir.Parent != null)
                    {
                        var testPath = Path.Combine(dir.FullName, "fpcalc.exe");
                        Console.WriteLine($"Checking: {testPath}");

                        if (File.Exists(testPath))
                        {
                            fpcalcPath = testPath;
                            Console.WriteLine($"Found fpcalc.exe at: {testPath}");
                            break;
                        }

                        dir = dir.Parent;
                    }
                }

                // Try system PATH
                if (!File.Exists(fpcalcPath))
                {
                    fpcalcPath = FindInPath("fpcalc.exe");
                }

                if (string.IsNullOrEmpty(fpcalcPath) || !File.Exists(fpcalcPath))
                {
                    Console.WriteLine("ERROR: fpcalc.exe not found - audio fingerprinting unavailable");
                    Console.WriteLine($"Searched locations:");
                    Console.WriteLine($"  - Current directory");
                    Console.WriteLine($"  - Parent directories from {AppDomain.CurrentDomain.BaseDirectory}");
                    Console.WriteLine($"  - System PATH");
                    return null;
                }

                Console.WriteLine($"Using fpcalc at: {fpcalcPath}");

                // Run fpcalc to get fingerprint
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = fpcalcPath,
                        Arguments = $"\"{filePath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                // Parse output for FINGERPRINT= line
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    if (line.StartsWith("FINGERPRINT="))
                    {
                        return line.Substring("FINGERPRINT=".Length).Trim();
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating fingerprint: {ex.Message}");
                return null;
            }
        }

        private string? FindInPath(string fileName)
        {
            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
            foreach (var path in paths)
            {
                var fullPath = Path.Combine(path, fileName);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            return null;
        }

        private async Task<string?> QueryAcoustIdAsync(string fingerprint, string filePath)
        {
            try
            {
                // Get duration from file
                int duration = GetDurationInSeconds(filePath);

                // Query AcoustID API directly via HTTP
                var url = $"https://api.acoustid.org/v2/lookup?client={_acoustIdApiKey}&duration={duration}&fingerprint={fingerprint}&meta=recordings+releasegroups";

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(json);

                // Get best match
                var results = data["results"] as JArray;
                if (results == null || results.Count == 0)
                    return null;

                var firstResult = results[0];
                var recordings = firstResult["recordings"] as JArray;

                if (recordings != null && recordings.Count > 0)
                {
                    var recording = recordings[0];
                    return recording["id"]?.ToString();
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error querying AcoustID: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Finds releases that contain multiple matched recordings (to identify albums vs compilations)
        /// </summary>
        private async Task<List<ReleaseOption>?> FindCommonReleasesAsync(List<string> recordingIds)
        {
            try
            {
                Console.WriteLine($"\n=== Finding common releases across {recordingIds.Count} recordings ===");

                // Get release-groups for each recording
                var releaseGroupCounts = new Dictionary<string, int>();
                var releaseGroupInfo = new Dictionary<string, (string title, string? year, string artist)>();

                foreach (var recordingId in recordingIds)
                {
                    Console.WriteLine($"\nQuerying recording: {recordingId}");
                    var url = $"https://musicbrainz.org/ws/2/recording/{recordingId}?inc=releases+release-groups+artists&fmt=json";

                    var response = await _httpClient.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync();
                    var data = JObject.Parse(json);

                    var releases = data["releases"] as JArray;
                    if (releases == null) continue;

                    var artistCredit = data["artist-credit"]?[0]?["name"]?.ToString() ?? "Unknown Artist";

                    foreach (var release in releases)
                    {
                        var releaseGroup = release["release-group"];
                        if (releaseGroup == null) continue;

                        var releaseGroupId = releaseGroup["id"]?.ToString();
                        if (string.IsNullOrEmpty(releaseGroupId)) continue;

                        var primaryType = releaseGroup["primary-type"]?.ToString();
                        var status = release["status"]?.ToString();

                        // Only count official albums (not compilations, singles, etc.)
                        if (status == "Official" && primaryType == "Album")
                        {
                            if (!releaseGroupCounts.ContainsKey(releaseGroupId))
                            {
                                releaseGroupCounts[releaseGroupId] = 0;
                                var title = releaseGroup["title"]?.ToString() ?? "Unknown";
                                var date = release["date"]?.ToString();
                                string? year = null;
                                if (!string.IsNullOrEmpty(date) && date.Length >= 4)
                                {
                                    year = date.Substring(0, 4);
                                }
                                releaseGroupInfo[releaseGroupId] = (title, year, artistCredit);
                            }
                            releaseGroupCounts[releaseGroupId]++;
                        }
                    }

                    // Rate limiting for MusicBrainz API (1 request per second)
                    await Task.Delay(1000);
                }

                // Find release-groups that have the most matches
                if (releaseGroupCounts.Count == 0)
                {
                    Console.WriteLine("No release-groups found");
                    return null;
                }

                var maxMatches = releaseGroupCounts.Values.Max();
                Console.WriteLine($"\nMaximum track matches found: {maxMatches} out of {recordingIds.Count}");

                // Get release-groups with at least half the tracks matching (or at least 2)
                var threshold = Math.Max(2, recordingIds.Count / 2);
                var candidateReleaseGroups = releaseGroupCounts
                    .Where(kvp => kvp.Value >= threshold)
                    .OrderByDescending(kvp => kvp.Value)
                    .Select(kvp => kvp.Key)
                    .ToList();

                Console.WriteLine($"Found {candidateReleaseGroups.Count} candidate release-groups with {threshold}+ matches");

                if (candidateReleaseGroups.Count == 0)
                {
                    Console.WriteLine("No release-groups met the threshold");
                    return null;
                }

                // Get all releases for the best-matching release-group(s)
                var allReleaseOptions = new List<ReleaseOption>();

                foreach (var releaseGroupId in candidateReleaseGroups.Take(3)) // Top 3 matches
                {
                    var info = releaseGroupInfo[releaseGroupId];
                    Console.WriteLine($"\nFetching releases for: {info.title} ({info.year}) - {releaseGroupCounts[releaseGroupId]} tracks matched");

                    var releases = await GetReleasesForReleaseGroupAsync(releaseGroupId, info.artist);
                    if (releases != null)
                    {
                        allReleaseOptions.AddRange(releases);
                    }

                    // Rate limiting
                    await Task.Delay(1000);
                }

                // Remove duplicates
                allReleaseOptions = allReleaseOptions
                    .GroupBy(r => new { r.Title, r.Year })
                    .Select(g => g.First())
                    .OrderBy(r => r.Year)
                    .ToList();

                Console.WriteLine($"\nTotal unique releases found: {allReleaseOptions.Count}");

                return allReleaseOptions.Count > 0 ? allReleaseOptions : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error finding common releases: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets all releases for a specific release-group ID
        /// </summary>
        private async Task<List<ReleaseOption>?> GetReleasesForReleaseGroupAsync(string releaseGroupId, string artist)
        {
            try
            {
                var url = $"https://musicbrainz.org/ws/2/release-group/{releaseGroupId}?inc=releases+artists&fmt=json";

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(json);

                var releases = data["releases"] as JArray;
                if (releases == null || releases.Count == 0)
                    return null;

                var releaseOptions = new List<ReleaseOption>();
                var albumTitle = data["title"]?.ToString() ?? "Unknown Album";

                foreach (var release in releases)
                {
                    var status = release["status"]?.ToString();
                    if (status != "Official") continue;

                    var title = release["title"]?.ToString() ?? albumTitle;
                    var releaseDate = release["date"]?.ToString();
                    var country = release["country"]?.ToString();
                    var releaseId = release["id"]?.ToString() ?? "";

                    string? year = null;
                    if (!string.IsNullOrEmpty(releaseDate) && releaseDate.Length >= 4)
                    {
                        year = releaseDate.Substring(0, 4);
                    }

                    string? label = null;
                    var labelInfo = release["label-info"] as JArray;
                    if (labelInfo != null && labelInfo.Count > 0)
                    {
                        label = labelInfo[0]["label"]?["name"]?.ToString();
                    }

                    string? format = null;
                    var media = release["media"] as JArray;
                    if (media != null && media.Count > 0)
                    {
                        format = media[0]["format"]?.ToString();
                    }

                    // Get artwork
                    string? artworkUrl = null;
                    if (!string.IsNullOrEmpty(releaseGroupId))
                    {
                        artworkUrl = await GetCoverArtUrlAsync(releaseGroupId);
                    }

                    releaseOptions.Add(new ReleaseOption
                    {
                        Title = title,
                        Year = year,
                        Label = label,
                        Country = country,
                        Format = format,
                        ReleaseId = releaseId,
                        ArtworkUrl = artworkUrl,
                        Artist = artist
                    });
                }

                return releaseOptions.Count > 0 ? releaseOptions : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting releases for release-group: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets all releases for a recording ID
        /// </summary>
        public async Task<List<ReleaseOption>?> GetAllReleasesAsync(string recordingId)
        {
            try
            {
                // Query MusicBrainz API for recording details
                var url = $"https://musicbrainz.org/ws/2/recording/{recordingId}?inc=releases+release-groups+artists&fmt=json";

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(json);

                // Extract all releases
                var releases = data["releases"] as JArray;
                if (releases == null || releases.Count == 0)
                    return null;

                var artistCredit = data["artist-credit"]?[0]?["name"]?.ToString() ?? "Unknown Artist";

                Console.WriteLine($"Artist found: {artistCredit}");

                var releaseOptions = new List<ReleaseOption>();

                Console.WriteLine($"Processing {releases.Count} releases from MusicBrainz...");

                foreach (var release in releases)
                {
                    // Filter to only official studio albums
                    var releaseGroup = release["release-group"];
                    var primaryType = releaseGroup?["primary-type"]?.ToString();
                    var status = release["status"]?.ToString();

                    Console.WriteLine($"  Release: {release["title"]}, Type: {primaryType}, Status: {status}");

                    // Only include official albums (be less strict - some remasters might not have "Album" type)
                    if (status != "Official")
                    {
                        Console.WriteLine($"    Skipping - not official");
                        continue;
                    }

                    if (primaryType != "Album" && primaryType != null)
                    {
                        Console.WriteLine($"    Skipping - not an album");
                        continue;
                    }

                    var title = release["title"]?.ToString() ?? "Unknown Album";
                    var releaseDate = release["date"]?.ToString();
                    var country = release["country"]?.ToString();
                    var releaseId = release["id"]?.ToString() ?? "";
                    var releaseGroupId = releaseGroup?["id"]?.ToString();

                    // Extract year from date
                    string? year = null;
                    if (!string.IsNullOrEmpty(releaseDate) && releaseDate.Length >= 4)
                    {
                        year = releaseDate.Substring(0, 4);
                    }

                    // Get label info
                    string? label = null;
                    var labelInfo = release["label-info"] as JArray;
                    if (labelInfo != null && labelInfo.Count > 0)
                    {
                        label = labelInfo[0]["label"]?["name"]?.ToString();
                    }

                    // Get format info (CD, Vinyl, etc.)
                    string? format = null;
                    var media = release["media"] as JArray;
                    if (media != null && media.Count > 0)
                    {
                        format = media[0]["format"]?.ToString();
                    }

                    // Get artwork URL
                    string? artworkUrl = null;
                    if (!string.IsNullOrEmpty(releaseGroupId))
                    {
                        artworkUrl = await GetCoverArtUrlAsync(releaseGroupId);
                    }

                    releaseOptions.Add(new ReleaseOption
                    {
                        Title = title,
                        Year = year,
                        Label = label,
                        Country = country,
                        Format = format,
                        ReleaseId = releaseId,
                        ArtworkUrl = artworkUrl,
                        Artist = artistCredit
                    });
                }

                // Remove duplicates (same title + year)
                releaseOptions = releaseOptions
                    .GroupBy(r => new { r.Title, r.Year })
                    .Select(g => g.First())
                    .OrderBy(r => r.Year)
                    .ToList();

                Console.WriteLine($"Total unique releases after filtering: {releaseOptions.Count}");

                return releaseOptions.Count > 0 ? releaseOptions : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error querying MusicBrainz for all releases: {ex.Message}");
                return null;
            }
        }

        private async Task<AlbumLookupResult?> QueryMusicBrainzAsync(string recordingId)
        {
            try
            {
                // Query MusicBrainz API for recording details
                var url = $"https://musicbrainz.org/ws/2/recording/{recordingId}?inc=releases+release-groups+artists&fmt=json";

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(json);

                // Extract album information
                var releases = data["releases"] as JArray;
                if (releases == null || releases.Count == 0)
                    return null;

                // Find the primary release (usually the original studio album)
                var primaryRelease = releases.FirstOrDefault(r =>
                    r["release-group"]?["primary-type"]?.ToString() == "Album" &&
                    r["status"]?.ToString() == "Official");

                if (primaryRelease == null)
                    primaryRelease = releases.First();

                var albumName = primaryRelease["title"]?.ToString();
                var releaseDate = primaryRelease["date"]?.ToString();
                var artistCredit = data["artist-credit"]?[0]?["name"]?.ToString();
                var releaseGroupId = primaryRelease["release-group"]?["id"]?.ToString();

                int? releaseYear = null;
                if (!string.IsNullOrEmpty(releaseDate) && releaseDate.Length >= 4)
                {
                    if (int.TryParse(releaseDate.Substring(0, 4), out var year))
                    {
                        releaseYear = year;
                    }
                }

                // Get album artwork
                string? artworkUrl = null;
                if (!string.IsNullOrEmpty(releaseGroupId))
                {
                    artworkUrl = await GetCoverArtUrlAsync(releaseGroupId);
                }

                return new AlbumLookupResult
                {
                    AlbumName = albumName ?? "Unknown Album",
                    ReleaseYear = releaseYear,
                    Artist = artistCredit ?? "Unknown Artist",
                    ArtworkUrl = artworkUrl,
                    Confidence = 0.85 // Placeholder - could calculate based on match quality
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error querying MusicBrainz: {ex.Message}");
                return null;
            }
        }

        private async Task<string?> GetCoverArtUrlAsync(string releaseGroupId)
        {
            try
            {
                var url = $"https://coverartarchive.org/release-group/{releaseGroupId}";
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(json);

                var images = data["images"] as JArray;
                if (images == null || images.Count == 0)
                    return null;

                // Get the front cover
                var frontCover = images.FirstOrDefault(i => i["front"]?.ToString() == "True");
                if (frontCover != null)
                {
                    return frontCover["image"]?.ToString();
                }

                // Fallback to first image
                return images[0]["image"]?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private int GetDurationInSeconds(string filePath)
        {
            try
            {
                using var file = TagLib.File.Create(filePath);
                return (int)file.Properties.Duration.TotalSeconds;
            }
            catch
            {
                return 0;
            }
        }
    }

    public class AlbumLookupResult
    {
        public string AlbumName { get; set; } = "";
        public int? ReleaseYear { get; set; }
        public string Artist { get; set; } = "";
        public string? ArtworkUrl { get; set; }
        public double Confidence { get; set; }
    }
}
