using System;
using System.Globalization;
using System.Windows.Data;

namespace DeadEditor.Converters;

/// <summary>
/// Converter to determine if a date matches the selected date.
/// Used for highlighting the active date link in the Jump To Date panel.
/// </summary>
public class DateSelectionConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is string date && values[1] is string selectedDate)
        {
            return date == selectedDate;
        }
        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
