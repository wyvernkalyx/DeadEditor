using DeadEditor.Models;
using System.Windows;
using System.Windows.Controls;

namespace DeadEditor.Helpers;

/// <summary>
/// Selects the appropriate DataTemplate for concert view items.
/// Date headers use a clickable header template, tracks use a standard row template.
/// </summary>
public class ConcertViewTemplateSelector : DataTemplateSelector
{
    public DataTemplate? DateHeaderTemplate { get; set; }
    public DataTemplate? TrackTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        return item switch
        {
            DateHeaderItem => DateHeaderTemplate,
            TrackViewItem => TrackTemplate,
            _ => base.SelectTemplate(item, container)
        };
    }
}
