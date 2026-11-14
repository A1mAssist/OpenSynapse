using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace OpenSynapse.App;

public sealed partial class MacroEditorWindow
{
    private async void AddMacroClickAtClick(object sender, RoutedEventArgs e)
    {
        var xBox = CreateCoordinateNumberBox("MacroMouseClickXLabel");
        var yBox = CreateCoordinateNumberBox("MacroMouseClickYLabel");
        var buttonBox = new ComboBox
        {
            Header = AppStrings.Text("MacroMouseClickButtonLabel"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        for (var button = 1; button <= 5; button++)
        {
            buttonBox.Items.Add(new ComboBoxItem
            {
                Content = AppStrings.Text($"MacroMouseButton{button}"),
            });
        }
        buttonBox.SelectedIndex = 0;
        AutomationProperties.SetName(buttonBox, AppStrings.Text("MacroMouseClickButtonLabel"));

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(xBox);
        content.Children.Add(yBox);
        content.Children.Add(buttonBox);
        content.Children.Add(new TextBlock
        {
            Text = AppStrings.Text("MacroMouseClickHint"),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
        });

        var dialog = new ContentDialog
        {
            Title = AppStrings.Text("MacroMouseClickDialogTitle"),
            Content = content,
            PrimaryButtonText = AppStrings.Text("MacroAdd"),
            CloseButtonText = AppStrings.Text("MacroCancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootLayout.XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await _viewModel.Macros.AddMacroClickAtAsync(
                ReadCoordinate(xBox),
                ReadCoordinate(yBox),
                Math.Clamp(buttonBox.SelectedIndex + 1, 1, 5),
                _lifetime.Token);
        }
    }

    private static NumberBox CreateCoordinateNumberBox(string labelKey)
    {
        var box = new NumberBox
        {
            Header = AppStrings.Text(labelKey),
            Value = 0,
            Minimum = -100_000,
            Maximum = 100_000,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(box, AppStrings.Text(labelKey));
        return box;
    }

    private static int ReadCoordinate(NumberBox box) =>
        double.IsFinite(box.Value)
            ? Math.Clamp(
                (int)Math.Round(box.Value, MidpointRounding.AwayFromZero),
                -100_000,
                100_000)
            : 0;
}
