using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FolderSyncr.ViewModels;

public sealed class BoolToGridLengthConverter : IValueConverter
{
    public GridLength TrueValue { get; set; } = new(1, GridUnitType.Star);

    public GridLength FalseValue { get; set; } = new(0);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? TrueValue : FalseValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
