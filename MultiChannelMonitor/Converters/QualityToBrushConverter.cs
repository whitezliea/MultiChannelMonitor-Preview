using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Domain.Tags;

namespace Presentation.Wpf.Converters;

public sealed class QualityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is TagQuality.Good ? Brushes.SeaGreen : Brushes.DarkOrange;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
