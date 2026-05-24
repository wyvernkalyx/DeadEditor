using System;
using System.Globalization;
using System.Windows.Data;
using DeadEditor.Models;

namespace DeadEditor.Converters;

/// <summary>
/// Maps a <see cref="VerificationState"/> to the leading-column glyph:
/// ✓ for Verified, ◐ for Partial, empty for Unverified (absence is the indicator).
/// </summary>
public class VerificationStateToGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is VerificationState state)
        {
            return state switch
            {
                VerificationState.Verified => "✓",
                VerificationState.Partial => "◐",
                _ => ""
            };
        }
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Maps a <see cref="VerificationState"/> to the glyph foreground brush:
/// the verified-badge green for Verified, amber for Partial, transparent for Unverified.
/// Reuses the named App.xaml brushes (BadgeVerifiedBg used as a foreground here).
/// </summary>
public class VerificationStateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is VerificationState state)
        {
            return state switch
            {
                VerificationState.Verified => System.Windows.Application.Current.FindResource("BadgeVerifiedGlyph"),
                VerificationState.Partial => System.Windows.Application.Current.FindResource("MarkerAmberAccent"),
                _ => System.Windows.Media.Brushes.Transparent
            };
        }
        return System.Windows.Media.Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Maps a whole <see cref="LibraryShow"/> to the verification-icon tooltip text.
/// Uses VerifiedFolderCount / FolderPaths.Count to describe merged multi-folder rows.
/// </summary>
public class LibraryShowToVerificationTooltipConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not LibraryShow show)
            return null;

        return show.VerificationState switch
        {
            VerificationState.Verified when show.FolderPaths.Count == 1 => "Verified",
            VerificationState.Verified => $"Verified ({show.VerifiedFolderCount} of {show.FolderPaths.Count} folders)",
            VerificationState.Partial => $"Partially verified ({show.VerifiedFolderCount} of {show.FolderPaths.Count} folders)",
            VerificationState.Unverified => "Unverified",
            _ => null
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
