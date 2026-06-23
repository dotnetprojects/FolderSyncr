using System.Windows;
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

        var resourceDictionaries = new[]
        {
            Application.Current.Resources,
            Application.Current.MainWindow.Resources
        };

        if (dark)
        {
            foreach (var resources in resourceDictionaries)
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
                Set(resources, "SelectionBrush", "#2563EB");
                Set(resources, "SelectionTextBrush", "#FFFFFF");
                Set(resources, "ColumnHeaderBrush", "#1F2937");
                Set(resources, "MenuHoverBrush", "#1E3A5F");
                Set(resources, "DisabledTextBrush", "#64748B");
                Set(resources, "ConflictRowBrush", "#473A16");
                Set(resources, SystemColors.MenuBrushKey, "#111827");
                Set(resources, SystemColors.MenuTextBrushKey, "#E5E7EB");
                Set(resources, SystemColors.HighlightBrushKey, "#2563EB");
                Set(resources, SystemColors.HighlightTextBrushKey, "#FFFFFF");
                Set(resources, SystemColors.ControlBrushKey, "#111827");
                Set(resources, SystemColors.ControlTextBrushKey, "#E5E7EB");
                Set(resources, SystemColors.GrayTextBrushKey, "#64748B");
                Set(resources, SystemColors.InactiveSelectionHighlightBrushKey, "#1E3A5F");
                Set(resources, SystemColors.InactiveSelectionHighlightTextBrushKey, "#FFFFFF");
            }
        }
        else
        {
            foreach (var resources in resourceDictionaries)
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
                Set(resources, "SelectionBrush", "#DBEAFE");
                Set(resources, "SelectionTextBrush", "#111827");
                Set(resources, "ColumnHeaderBrush", "#ECEFF3");
                Set(resources, "MenuHoverBrush", "#DBEAFE");
                Set(resources, "DisabledTextBrush", "#6B7280");
                Set(resources, "ConflictRowBrush", "#FFF7D6");
                Set(resources, SystemColors.MenuBrushKey, "#FFFFFF");
                Set(resources, SystemColors.MenuTextBrushKey, "#111827");
                Set(resources, SystemColors.HighlightBrushKey, "#DBEAFE");
                Set(resources, SystemColors.HighlightTextBrushKey, "#111827");
                Set(resources, SystemColors.ControlBrushKey, "#FFFFFF");
                Set(resources, SystemColors.ControlTextBrushKey, "#111827");
                Set(resources, SystemColors.GrayTextBrushKey, "#6B7280");
                Set(resources, SystemColors.InactiveSelectionHighlightBrushKey, "#DBEAFE");
                Set(resources, SystemColors.InactiveSelectionHighlightTextBrushKey, "#111827");
            }
        }
    }

    private static void Set(ResourceDictionary resources, object key, string color)
    {
        resources[key] = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(color));
    }
}
