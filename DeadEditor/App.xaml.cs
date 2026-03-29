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
        base.OnStartup(e);

        // Set shutdown mode: app exits when main window closes
        ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;

        // Initialize singleton on app startup to ensure it's ready
        _ = AudioPlayerService.Instance;

        // Restore saved playlist from settings
        RestorePlaylist();

        // Launch ShellWindow (single-window architecture)
        var shellWindow = new ShellWindow();
        shellWindow.Show();
        MainWindow = shellWindow;

        // Hook Windows session ending (log off/shutdown)
        SessionEnding += App_SessionEnding;
    }

    private void App_SessionEnding(object sender, System.Windows.SessionEndingCancelEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[App] SessionEnding: {e.ReasonSessionEnding}");
        // Stop playback immediately on Windows shutdown/logoff
        PlaybackService.Stop();
    }

    private void RestorePlaylist()
    {
        var settings = Models.LibrarySettings.Load();
        if (settings.SavedPlaylistPaths == null || !settings.SavedPlaylistPaths.Any())
            return;

        foreach (var path in settings.SavedPlaylistPaths)
        {
            // Skip missing files silently
            if (!System.IO.File.Exists(path))
                continue;

            try
            {
                // Read full metadata from file for proper playlist display
                using (var file = TagLib.File.Create(path))
                {
                    var rawTitle = file.Tag.Title ?? System.IO.Path.GetFileNameWithoutExtension(path);

                    // Parse title to extract date if embedded (e.g., "Song (1977-05-08)")
                    var extractedDate = "";
                    var songName = rawTitle;
                    var dateMatch = System.Text.RegularExpressions.Regex.Match(rawTitle, @"^(.+?)\s*\((\d{4}-\d{2}-\d{2})\)\s*>?$");
                    if (dateMatch.Success)
                    {
                        songName = dateMatch.Groups[1].Value.Trim();
                        extractedDate = dateMatch.Groups[2].Value;
                    }

                    var track = new Models.TrackInfo
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
                    };

                    PlaybackService.Playlist.Add(track);
                }
            }
            catch
            {
                // Skip files that can't be loaded
                continue;
            }
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

