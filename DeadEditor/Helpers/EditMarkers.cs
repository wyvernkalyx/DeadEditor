using System.Windows;

namespace DeadEditor.Helpers
{
    /// <summary>
    /// Attached property consumed by EditMetadataView's shared TextBox style.
    /// When set to true, the style trigger renders an amber left-edge marker
    /// (3-pixel border, MarkerAmberAccent brush) on the field to indicate the
    /// current value differs from the load-time baseline.
    /// </summary>
    public static class EditMarkers
    {
        public static readonly DependencyProperty IsDirtyProperty =
            DependencyProperty.RegisterAttached(
                "IsDirty",
                typeof(bool),
                typeof(EditMarkers),
                new PropertyMetadata(false));

        public static bool GetIsDirty(DependencyObject obj) => (bool)obj.GetValue(IsDirtyProperty);
        public static void SetIsDirty(DependencyObject obj, bool value) => obj.SetValue(IsDirtyProperty, value);
    }
}
