using System.Globalization;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

const string Mbid = "6faa7ca7-0d99-4a5e-bfa6-1fd5037520c6";
const string ApiKey = "66sBcvcLBIo7bYMjoNS7PwHq3wSnQHUzbyqI";
const string BaseUrl = $"https://api.setlist.fm/rest/1.0/artist/{Mbid}/setlists";
const int MaxRetries = 3;

// Parse optional arguments:
//   --output <path>     Explicit Data/ folder path (for shows.json)
//   --concerts <path>   Explicit concerts output folder (default: %APPDATA%/DeadEditor/concerts/)
string? explicitDataDir = null;
string? explicitConcertsDir = null;
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--output")
        explicitDataDir = args[i + 1];
    else if (args[i] == "--concerts")
        explicitConcertsDir = args[i + 1];
}

using var http = new HttpClient();
http.DefaultRequestHeaders.Add("x-api-key", ApiKey);
http.DefaultRequestHeaders.Add("Accept", "application/json");

// Track all shows: date -> list of raw setlist JSON objects
var allShows = new Dictionary<string, List<JToken>>();
int page = 1;
int totalPages = 1;

Console.WriteLine("Fetching Grateful Dead shows from setlist.fm...");
Console.WriteLine();

while (page <= totalPages)
{
    var json = await FetchPageWithRetry(page);
    if (json == null)
    {
        Console.WriteLine($"  [ERROR] Failed to fetch page {page} after {MaxRetries} retries. Skipping.");
        page++;
        continue;
    }

    // On first page, calculate total pages
    if (page == 1)
    {
        int total = json.Value<int>("total");
        int perPage = json.Value<int>("itemsPerPage");
        totalPages = (int)Math.Ceiling((double)total / perPage);
        Console.WriteLine($"  Total shows: {total}, Pages: {totalPages}");
        Console.WriteLine();
    }

    var setlists = json["setlist"] as JArray;
    if (setlists == null)
    {
        Console.WriteLine($"  [WARN] Page {page}: no setlist array found. Skipping.");
        page++;
        continue;
    }

    foreach (var setlist in setlists)
    {
        var eventDate = setlist.Value<string>("eventDate");
        var venue = setlist["venue"];

        if (string.IsNullOrEmpty(eventDate) || venue == null)
        {
            Console.WriteLine($"  [WARN] Skipping malformed entry (missing date or venue)");
            continue;
        }

        // Convert dd-MM-yyyy to yyyy-MM-dd
        if (!DateTime.TryParseExact(eventDate, "dd-MM-yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
        {
            Console.WriteLine($"  [WARN] Skipping entry with unparseable date: {eventDate}");
            continue;
        }

        string dateKey = date.ToString("yyyy-MM-dd");

        if (!allShows.ContainsKey(dateKey))
            allShows[dateKey] = new List<JToken>();

        allShows[dateKey].Add(setlist);
    }

    Console.WriteLine($"  Processing page {page} of {totalPages}... ({allShows.Count} shows)");

    page++;

    // Respect rate limiting: 1 second between requests
    if (page <= totalPages)
        await Task.Delay(1000);
}

// Resolve Data/ folder
string dataDir;
if (explicitDataDir != null)
{
    dataDir = explicitDataDir;
    Console.WriteLine($"  Using explicit output path: {dataDir}");
}
else
{
    // Walk up from both the exe directory and the current working directory
    // to find a folder containing Data/songs.json
    dataDir = null!;
    foreach (var startDir in new[] { new DirectoryInfo(AppContext.BaseDirectory), new DirectoryInfo(Environment.CurrentDirectory) })
    {
        var dir = startDir;
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Data", "songs.json");
            if (File.Exists(candidate))
            {
                dataDir = Path.Combine(dir.FullName, "Data");
                goto foundPath;
            }
            dir = dir.Parent;
        }
    }
    // Fallback: write next to the executable
    dataDir = AppContext.BaseDirectory;
    Console.WriteLine($"  [WARN] Data folder not found. Use --output <path> to specify.");
    Console.WriteLine($"  Writing to: {dataDir}");
}
foundPath:

string showsJsonPath = Path.Combine(dataDir, "shows.json");

// Concert files go to AppData by default (survives upgrades), or explicit --concerts path
string concertsDir;
if (explicitConcertsDir != null)
{
    concertsDir = explicitConcertsDir;
}
else
{
    concertsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DeadEditor", "concerts");
}
Directory.CreateDirectory(concertsDir);
Console.WriteLine($"  Concert files: {concertsDir}");

// ─── Generate shows.json (existing behavior) ───

var showsOutput = new SortedDictionary<string, object>();
int multiShowCount = 0;

foreach (var kvp in allShows.OrderBy(k => k.Key))
{
    bool isMultiShow = kvp.Value.Count > 1;
    if (isMultiShow) multiShowCount++;

    var first = kvp.Value[0];
    var venue = first["venue"]!;
    var city = venue["city"];

    showsOutput[kvp.Key] = new
    {
        venue = venue.Value<string>("name") ?? "",
        city = city?.Value<string>("name") ?? "",
        state = city?.Value<string>("stateCode") ?? "",
        country = city?["country"]?.Value<string>("code") ?? "",
        multiShow = isMultiShow
    };
}

string showsJson = JsonConvert.SerializeObject(showsOutput, Formatting.Indented);
await File.WriteAllTextAsync(showsJsonPath, showsJson);

// ─── Generate per-concert JSON files ───

int showsWithSetlists = 0;
int showsWithoutSetlists = 0;

foreach (var kvp in allShows.OrderBy(k => k.Key))
{
    string dateKey = kvp.Key;
    bool isMultiShow = kvp.Value.Count > 1;
    var first = kvp.Value[0];

    // Extract venue info
    var venue = first["venue"]!;
    var city = venue["city"];
    string venueName = venue.Value<string>("name") ?? "";
    string cityName = city?.Value<string>("name") ?? "";
    string stateCode = city?.Value<string>("stateCode") ?? "";
    string countryCode = city?["country"]?.Value<string>("code") ?? "";

    // Extract setlist.fm metadata
    string setlistFmId = first.Value<string>("id") ?? "";
    string setlistFmUrl = first.Value<string>("url") ?? "";

    // Parse sets and songs
    var setsArray = new List<object>();
    var tracksArray = new List<object>();
    int position = 0;
    bool hasAnySongs = false;

    var setsToken = first["sets"]?["set"] as JArray;
    if (setsToken != null)
    {
        foreach (var setToken in setsToken)
        {
            // Determine set name (strip trailing colon from API data)
            string setName;
            var encoreVal = setToken["encore"];
            if (encoreVal != null)
            {
                int encoreNum = encoreVal.Value<int>();
                setName = encoreNum <= 1 ? "Encore" : $"Encore {encoreNum}";
            }
            else
            {
                setName = (setToken.Value<string>("name") ?? "Set").TrimEnd(':').Trim();
            }

            var songs = setToken["song"] as JArray;
            var songsList = new List<object>();

            if (songs != null)
            {
                for (int i = 0; i < songs.Count; i++)
                {
                    var song = songs[i];
                    string songName = song.Value<string>("name") ?? "";
                    string info = song.Value<string>("info") ?? "";

                    // Skip unknown/empty songs
                    if (string.IsNullOrWhiteSpace(songName) || songName == "?")
                        continue;

                    hasAnySongs = true;

                    // Detect segue: info contains ">" or starts with ">"
                    bool segue = !string.IsNullOrEmpty(info) &&
                                 (info.Contains(">") || info.Contains("→"));

                    position++;

                    songsList.Add(new
                    {
                        name = songName,
                        date = dateKey,
                        segue,
                        info
                    });

                    tracksArray.Add(new
                    {
                        position,
                        songName,
                        date = dateKey,
                        segue,
                        set = setName
                    });
                }
            }

            setsArray.Add(new
            {
                name = setName,
                songs = songsList
            });
        }
    }

    if (hasAnySongs)
        showsWithSetlists++;
    else
        showsWithoutSetlists++;

    // Build concert object
    var concert = new Dictionary<string, object>
    {
        ["date"] = dateKey,
        ["venue"] = venueName,
        ["city"] = cityName,
        ["state"] = stateCode,
        ["country"] = countryCode,
        ["setlistFmId"] = setlistFmId,
        ["setlistFmUrl"] = setlistFmUrl,
        ["lastUpdated"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        ["hasSetlist"] = hasAnySongs,
        ["sets"] = setsArray,
        ["tracks"] = tracksArray
    };

    if (isMultiShow)
        concert["multiShow"] = true;

    string concertJson = JsonConvert.SerializeObject(concert, Formatting.Indented);
    string concertPath = Path.Combine(concertsDir, $"{dateKey}.json");
    await File.WriteAllTextAsync(concertPath, concertJson);
}

// Summary
var dates = allShows.Keys.OrderBy(d => d).ToList();
Console.WriteLine();
Console.WriteLine("=== Complete! ===");
Console.WriteLine($"  Total shows: {allShows.Count:N0}");
Console.WriteLine($"  Shows with setlists: {showsWithSetlists:N0}");
Console.WriteLine($"  Shows without setlists: {showsWithoutSetlists:N0}");
Console.WriteLine($"  Date range: {dates.First()} to {dates.Last()}");
Console.WriteLine($"  Multi-show dates: {multiShowCount}");
Console.WriteLine($"  Concert files written to: {concertsDir}");
Console.WriteLine($"  Shows.json updated: {showsJsonPath}");

// --- Helper methods ---

async Task<JObject?> FetchPageWithRetry(int pageNum)
{
    int delay = 1000;
    for (int attempt = 1; attempt <= MaxRetries; attempt++)
    {
        try
        {
            var response = await http.GetAsync($"{BaseUrl}?p={pageNum}");

            if (response.StatusCode == (HttpStatusCode)429)
            {
                Console.WriteLine($"  [RATE LIMITED] Page {pageNum}, attempt {attempt}. Waiting {delay / 1000}s...");
                await Task.Delay(delay);
                delay *= 2;
                continue;
            }

            if ((int)response.StatusCode >= 500)
            {
                Console.WriteLine($"  [SERVER ERROR {(int)response.StatusCode}] Page {pageNum}, attempt {attempt}. Retrying in {delay / 1000}s...");
                await Task.Delay(delay);
                delay *= 2;
                continue;
            }

            response.EnsureSuccessStatusCode();
            string body = await response.Content.ReadAsStringAsync();
            return JObject.Parse(body);
        }
        catch (HttpRequestException ex) when (attempt < MaxRetries)
        {
            Console.WriteLine($"  [ERROR] Page {pageNum}, attempt {attempt}: {ex.Message}. Retrying in {delay / 1000}s...");
            await Task.Delay(delay);
            delay *= 2;
        }
    }
    return null;
}
