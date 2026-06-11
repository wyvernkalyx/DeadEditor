namespace DeadEditor.Services
{
    /// <summary>
    /// Severity of an in-window alert. Drives the banner color and the dismissal policy
    /// (alert-system-spec.md Ruling 1): Info auto-dismisses; Warning/Error persist until closed.
    /// </summary>
    public enum AlertSeverity
    {
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// One alert to display in the banner. Immutable; carries the body, the severity, and an
    /// optional title shown as the banner's bold label (falls back to the severity word).
    /// </summary>
    public sealed record AlertItem(string Message, AlertSeverity Severity, string? Title);

    /// <summary>
    /// Shell-wide alert surface (alert-system-spec.md). Slice 1 implements <see cref="Notify"/>
    /// only — the silent in-window banner for bucket-A fire-and-forget notifications.
    /// <c>ConfirmAsync</c> (bucket B) is added in increment 3, not stubbed here.
    /// </summary>
    public interface IAlertService
    {
        /// <summary>
        /// Show a fire-and-forget notification in the in-window banner. Silent (no system chime)
        /// and never steals keyboard focus. Returns immediately; if a banner is already showing
        /// the alert queues behind it.
        /// </summary>
        void Notify(string message, AlertSeverity severity = AlertSeverity.Info, string? title = null);
    }

    /// <summary>
    /// The visual banner host the <see cref="AlertService"/> drives. Implemented by the WPF
    /// banner control in the shell; kept as an interface so the service layer does not depend on
    /// the view type. <see cref="Show"/> is always invoked on the UI thread.
    /// </summary>
    public interface IAlertSink
    {
        void Show(AlertItem item);
    }
}
