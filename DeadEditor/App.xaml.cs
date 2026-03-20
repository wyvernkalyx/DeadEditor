using DeadEditor.Services;
using System.Configuration;
using System.Data;

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

        // Create and show main library browser window
        var libraryWindow = new LibraryBrowserWindow();
        libraryWindow.Show();

        // Create and show player window (docked to library window)
        var playerWindow = new PlayerWindow(libraryWindow);
        playerWindow.Show();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        // Cleanup audio player on app exit
        AudioPlayerService.Instance.Dispose();
        base.OnExit(e);
    }
}

