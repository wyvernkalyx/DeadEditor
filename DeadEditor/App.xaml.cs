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
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        // Cleanup audio player on app exit
        AudioPlayerService.Instance.Dispose();
        base.OnExit(e);
    }
}

