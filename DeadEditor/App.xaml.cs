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

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        // Initialize singleton on app startup to ensure it's ready
        _ = AudioPlayerService.Instance;

        // Restore saved playlist from settings
        RestorePlaylist();

        // Create and show main library browser window
        var libraryWindow = new LibraryBrowserWindow();
        libraryWindow.Show();

        // Create and show player window (free-floating, no docking)
        var playerWindow = new PlayerWindow();
        playerWindow.Show();
        System.Diagnostics.Debug.WriteLine($"[App] PlayerWindow created and shown at Left={playerWindow.Left}, Top={playerWindow.Top}");

        // Create and show playlist window (attached to player window)
        var playlistWindow = new PlaylistWindow(playerWindow);
        playerWindow.PlaylistWindowInstance = playlistWindow;
        playlistWindow.Show();

        // Create and show visualizer window (attached to playlist window)
        var visWindow = new VisWindow(playlistWindow);
        playerWindow.VisWindowInstance = visWindow;
        visWindow.Show();
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
                // Create minimal TrackInfo with just FilePath and Title
                // Full metadata will be loaded if the track is played
                var track = new Models.TrackInfo
                {
                    FilePath = path,
                    Title = System.IO.Path.GetFileNameWithoutExtension(path)
                };

                PlaybackService.Playlist.Add(track);
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
        // Save playlist to settings before exit
        SavePlaylist();

        // Cleanup audio player on app exit
        AudioPlayerService.Instance.Dispose();
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

