using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LummaSimulator.App;

public sealed class DesktopTextConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] == DependencyProperty.UnsetValue) return "";
        var text = DesktopLanguage.Current.Translate(values[0]?.ToString() ?? "");
        return parameter is string key && key.Length > 0
            ? string.Format(CultureInfo.InvariantCulture, DesktopLanguage.Current[key], text) : text;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
