using DeadEditor.Services;
using System.Configuration;
using System.Data;
using System.Linq;

namespace DeadEditor;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    /// <summary>
    /// Static accessor for the singleton PlaybackService.
    /// Initialized on first access.
    /// </summary>
    public static AudioPlayerService PlaybackService => AudioPlayerService.Instance;

    /// <summary>
    /// Flag indicating whether the application is shutting down.
    /// Set to true in OnExit before cleanup.
    /// Used by child windows to distinguish app shutdown from user clicking X.
    /// </summary>
    public static bool IsShuttingDown { get; private set; } = false;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        System.Diagnostics.Debug.WriteLine($"[STARTUP] App.OnStartup begin: {sw.ElapsedMilliseconds}ms");

        base.OnStartup(e);

        // Set shutdown mode: app exits when main window closes
        ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;

        // Initialize singleton on app startup to ensure it's ready
        _ = AudioPlayerService.Instance;
        System.Diagnostics.Debug.WriteLine($"[STARTUP] AudioPlayerService initialized: {sw.ElapsedMilliseconds}ms");

        // Launch ShellWindow immediately (don't block on playlist restore)
        var shellWindow = new ShellWindow();
        System.Diagnostics.Debug.WriteLine($"[STARTUP] ShellWindow created: {sw.ElapsedMilliseconds}ms");
        shellWindow.Show();
        System.Diagnostics.Debug.WriteLine($"[STARTUP] ShellWindow.Show() complete: {sw.ElapsedMilliseconds}ms");
        MainWindow = shellWindow;

        // Restore saved playlist on background thread (reads TagLib for each track)
        RestorePlaylistAsync();

        // Hook Windows session ending (log off/shutdown)
        SessionEnding += App_SessionEnding;

        System.Diagnostics.Debug.WriteLine($"[STARTUP] App.OnStartup complete: {sw.ElapsedMilliseconds}ms");
    }

    private void App_SessionEnding(object sender, System.Windows.SessionEndingCancelEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[App] SessionEnding: {e.ReasonSessionEnding}");
        // Stop playback immediately on Windows shutdown/logoff
        PlaybackService.Stop();
    }

    private async void RestorePlaylistAsync()
    {
        var settings = Models.LibrarySettings.Load();
        if (settings.SavedPlaylistPaths == null || !settings.SavedPlaylistPaths.Any())
            return;

        var paths = settings.SavedPlaylistPaths.ToList();

        // Read TagLib metadata on background thread (can be slow with many tracks)
        var tracks = await System.Threading.Tasks.Task.Run(() =>
        {
            var result = new System.Collections.Generic.List<Models.TrackInfo>();
            foreach (var path in paths)
            {
                if (!System.IO.File.Exists(path))
                    continue;

                try
                {
                    using (var file = TagLib.File.Create(path))
                    {
                        var rawTitle = file.Tag.Title ?? System.IO.Path.GetFileNameWithoutExtension(path);

                        var extractedDate = "";
                        var songName = rawTitle;
                        var dateMatch = System.Text.RegularExpressions.Regex.Match(rawTitle, @"^(.+?)\s*\((\d{4}-\d{2}-\d{2})\)\s*>?$");
                        if (dateMatch.Success)
                        {
                            songName = dateMatch.Groups[1].Value.Trim();
                            extractedDate = dateMatch.Groups[2].Value;
                        }

                        result.Add(new Models.TrackInfo
                        {
                            FilePath = path,
                            FileName = System.IO.Path.GetFileName(path),
                            TrackNumber = (int)file.Tag.Track,
                            DiscNumber = file.Tag.Disc > 0 ? (int)file.Tag.Disc : 1,
                            SongName = songName,
                            RawTitle = rawTitle,
                            TrackDate = extractedDate,
                            Duration = file.Properties.Duration.ToString(@"mm\:ss"),
                            Segue = rawTitle.TrimEnd().EndsWith(">"),
                            IsModified = false
                        });
                    }
                }
                catch
                {
                    continue;
                }
            }
            return result;
        });

        // Add to playlist on UI thread
        foreach (var track in tracks)
        {
            PlaybackService.Playlist.Add(track);
        }

        System.Diagnostics.Debug.WriteLine($"[App] Restored {PlaybackService.Playlist.Count} tracks to playlist from settings");
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[App] OnExit called - stopping playback and cleaning up");

        // Set shutdown flag so child windows know the app is closing
        IsShuttingDown = true;

        // Stop playback explicitly before dispose
        PlaybackService.Stop();

        // Save playlist to settings before exit
        SavePlaylist();

        // Cleanup audio player on app exit
        PlaybackService.Dispose();

        base.OnExit(e);
    }

    private void SavePlaylist()
    {
        var settings = Models.LibrarySettings.Load();
        settings.SavedPlaylistPaths = PlaybackService.Playlist
            .Select(t => t.FilePath)
            .ToList();
        settings.Save();

        System.Diagnostics.Debug.WriteLine($"[App] Saved {settings.SavedPlaylistPaths.Count} tracks to settings");
    }
}

