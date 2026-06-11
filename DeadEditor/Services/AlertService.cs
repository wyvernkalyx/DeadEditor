using System.Windows;

namespace DeadEditor.Services
{
    /// <summary>
    /// Shell-wide singleton alert service (alert-system-spec.md § Service API). Mirrors the
    /// <c>AudioPlayerService.Instance</c> pattern: one instance reached from any view (via
    /// <c>App.Alerts</c>). It owns no visuals — it forwards notifications to a registered
    /// <see cref="IAlertSink"/> (the shell's banner host) on the UI thread.
    /// </summary>
    public sealed class AlertService : IAlertService
    {
        private static readonly AlertService _instance = new();

        /// <summary>The singleton instance.</summary>
        public static AlertService Instance => _instance;

        private AlertService() { }

        private IAlertSink? _sink;

        /// <summary>
        /// Register the visual banner host. Called once by <c>ShellWindow</c> at construction — the
        /// shell is the single window created at startup, so the sink is always attached before any
        /// view can call <see cref="Notify"/>. With no sink attached, Notify is a silent no-op.
        /// </summary>
        public void RegisterSink(IAlertSink sink) => _sink = sink;

        public void Notify(string message, AlertSeverity severity = AlertSeverity.Info, string? title = null)
        {
            var sink = _sink;
            if (sink == null) return;

            var item = new AlertItem(message, severity, title);

            // Marshal to the UI thread; banners may be raised from background continuations.
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                sink.Show(item);
            else
                dispatcher.Invoke(() => sink.Show(item));
        }
    }
}
