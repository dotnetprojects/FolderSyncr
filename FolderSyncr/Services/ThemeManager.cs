using System.Windows.Media;

namespace FolderSyncr.Services;

public static class ThemeManager
{
    public static void Apply(bool dark)
    {
        if (System.Windows.Application.Current.MainWindow is null)
        {
            return;
        }

        var resources = System.Windows.Application.Current.MainWindow.Resources;
        if (dark)
        {
            Set(resources, "WindowBrush", "#0F172A");
            Set(resources, "ChromeBrush", "#172033");
            Set(resources, "PanelBrush", "#111827");
            Set(resources, "InputBrush", "#0B1220");
            Set(resources, "ButtonBrush", "#1F2937");
            Set(resources, "ButtonHoverBrush", "#263244");
            Set(resources, "BorderBrushSoft", "#334155");
            Set(resources, "GridLineBrush", "#263244");
            Set(resources, "HeaderBlueBrush", "#1D4ED8");
            Set(resources, "TextBrush", "#E5E7EB");
            Set(resources, "MutedTextBrush", "#94A3B8");
            Set(resources, "RowAltBrush", "#162033");
            Set(resources, "RowHoverBrush", "#1E3A5F");
            Set(resources, "ColumnHeaderBrush", "#1F2937");
        }
        else
        {
            Set(resources, "WindowBrush", "#E9ECEF");
            Set(resources, "ChromeBrush", "#F3F4F6");
            Set(resources, "PanelBrush", "#FFFFFF");
            Set(resources, "InputBrush", "#FFFFFF");
            Set(resources, "ButtonBrush", "#E5E7EB");
            Set(resources, "ButtonHoverBrush", "#F9FAFB");
            Set(resources, "BorderBrushSoft", "#BFC7D1");
            Set(resources, "GridLineBrush", "#D8DEE6");
            Set(resources, "HeaderBlueBrush", "#0875C9");
            Set(resources, "TextBrush", "#111827");
            Set(resources, "MutedTextBrush", "#5B6472");
            Set(resources, "RowAltBrush", "#F7F7F7");
            Set(resources, "RowHoverBrush", "#EAF4FF");
            Set(resources, "ColumnHeaderBrush", "#ECEFF3");
        }
    }

    private static void Set(System.Windows.ResourceDictionary resources, string key, string color)
    {
        if (resources[key] is SolidColorBrush brush)
        {
            brush.Color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color);
        }
    }
}
