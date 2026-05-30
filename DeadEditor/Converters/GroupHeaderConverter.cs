using System;
using System.Globalization;
using System.Windows.Data;
using DeadEditor.Helpers;
using DeadEditor.Services;

namespace DeadEditor.Converters
{
    /// <summary>
    /// Renders a date-group header for the box-set wizard's grouped track grid (commit H).
    /// Bound to the group's <see cref="CollectionViewGroup"/>: reads the date (the group
    /// key) and item count, resolves the venue via <see cref="ShowLookupService"/> (an O(1)
    /// lookup, realized once per header rather than per row), and formats the text via
    /// <see cref="BoxSetGroupHeader.FormatGroupHeader"/>.
    /// </summary>
    public class GroupHeaderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is CollectionViewGroup group)
            {
                string date = group.Name as string ?? "";
                string? venue = ShowLookupService.Instance.GetShowByDate(date)?.FormattedVenueLocation;
                return BoxSetGroupHeader.FormatGroupHeader(date, venue, group.ItemCount);
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
