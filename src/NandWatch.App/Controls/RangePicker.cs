using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NandWatch.App.Theme;

namespace NandWatch.App.Controls;

/// <summary>
/// The range picker in a chart's panel head, AI Usage Native's: a drop-down
/// of day ranges, 0 meaning all of the history. One per chart: it changes
/// that chart only.
/// </summary>
public static class RangePicker
{
    public static string Label(int days) => days == 0 ? "All days" : days == 1 ? "Last day" : $"Last {days} days";

    public static ComboBox Create(string automationName, int[] options, int current, Action<int> onChange)
    {
        var box = new ComboBox
        {
            FontFamily = Fonts.Sans,
            FontSize = 14,
            FontWeight = Fonts.Weight(550),
            Foreground = Palette.TextMutedBrush,
            Background = Palette.SurfaceBrush,
            BorderBrush = Palette.BorderBrightBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Ui.RadiusSmall),
            Padding = new Thickness(12, 5, 8, 5),
            MinHeight = 0,
            MinWidth = 0,
        };
        box.Resources["ComboBoxBackgroundPointerOver"] = Palette.SurfaceBrush;
        box.Resources["ComboBoxBorderBrushPointerOver"] = Palette.AccentBrush;
        box.Resources["ComboBoxForegroundPointerOver"] = Palette.TextBrush;
        box.Resources["ComboBoxBackgroundPressed"] = Palette.SurfaceBrush;
        box.Resources["ComboBoxBorderBrushPressed"] = Palette.AccentBrush;
        box.Resources["ComboBoxDropDownBackground"] = Palette.TooltipBgBrush;
        box.Resources["ComboBoxDropDownBorderBrush"] = Palette.BorderBrightBrush;
        foreach (var days in options)
            box.Items.Add(new ComboBoxItem { Content = Label(days), Tag = days, FontFamily = Fonts.Sans, FontSize = 14 });
        box.SelectedIndex = Math.Max(0, Array.IndexOf(options, current));
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is ComboBoxItem { Tag: int days } && days != current) onChange(days);
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, automationName);
        return box;
    }
}
