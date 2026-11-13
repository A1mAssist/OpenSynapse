using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Devices;
using Windows.UI;

namespace OpenSynapse.App;

public sealed class BatteryLevelBrushConverter : IValueConverter
{
    private static readonly Brush Good = new SolidColorBrush(Color.FromArgb(255, 68, 214, 44));
    private static readonly Brush Low = new SolidColorBrush(Color.FromArgb(255, 240, 185, 90));
    private static readonly Brush Critical = new SolidColorBrush(Color.FromArgb(255, 240, 125, 121));
    private static readonly Brush Unknown = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160));

    public object Convert(object value, Type targetType, object parameter, string language) =>
        BatteryDisplay.Level(value as int?) switch
        {
            BatteryLevel.Good => Good,
            BatteryLevel.Low => Low,
            BatteryLevel.Critical => Critical,
            _ => Unknown,
        };

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
