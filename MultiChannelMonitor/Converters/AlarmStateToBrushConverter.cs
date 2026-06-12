using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Domain.Tags;

namespace Presentation.Wpf.Converters;

public sealed class AlarmStateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is TagAlarmState.AlarmHigh or TagAlarmState.AlarmLow or TagAlarmState.Invalid or TagAlarmState.Offline
            ? Brushes.Firebrick
            : value is TagAlarmState.WarningHigh or TagAlarmState.WarningLow
                ? Brushes.DarkOrange
                : Brushes.SeaGreen;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
