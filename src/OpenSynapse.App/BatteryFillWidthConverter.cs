using Microsoft.UI.Xaml.Data;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.App;

public sealed class BatteryFillWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        BatteryDisplay.FillWidth(value as int?);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
