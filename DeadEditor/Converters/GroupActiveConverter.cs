using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using DeadEditor.Helpers;

namespace DeadEditor.Converters
{
    /// <summary>
    /// Drives a date-group Expander's <c>IsExpanded</c> in the box-set wizard's accordion
    /// (commit H2). A <see cref="MultiBinding"/> over [<c>CollectionViewGroup.Name</c> (the
    /// group's date key), <c>ActiveGroupDate</c>] returns true only for the single active
    /// group. The actual rule (including the null-vs-empty distinction) lives in
    /// <see cref="BoxSetGroupHeader.IsActiveGroup"/> so it stays unit-testable without WPF;
    /// this converter is a thin shell. One-way only — <see cref="ConvertBack"/> throws.
    /// </summary>
    public class GroupActiveConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // Before a group container is realized either value can be UnsetValue; treat any
            // unset/non-string input as "not active" so the Expander stays collapsed.
            if (values == null || values.Length < 2)
                return false;
            if (values[0] == DependencyProperty.UnsetValue || values[1] == DependencyProperty.UnsetValue)
                return false;

            string? groupName = values[0] as string;
            string? activeDate = values[1] as string;

            return BoxSetGroupHeader.IsActiveGroup(groupName, activeDate);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
