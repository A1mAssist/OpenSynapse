using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace OpenSynapse.App;

public sealed class VisibilityLayoutConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var visible = value is Visibility.Visible;
        return parameter as string == "Column"
            ? visible ? 1 : 0
            : visible ? 1 : 2;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
