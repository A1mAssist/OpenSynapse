using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace OpenSynapse.App.Controls.Help;

public sealed partial class DpiStageSpecimen : UserControl
{
    private static readonly int[] DpiValues = [800, 1600, 2400, 3200, 6400];
    private int _stage = 3;

    public DpiStageSpecimen()
    {
        InitializeComponent();
        UpdateSelection();
    }

    private void StageClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string tag } || !int.TryParse(tag, out var stage))
        {
            return;
        }

        _stage = stage;
        foreach (var button in new[] { DpiStage1, DpiStage2, DpiStage3, DpiStage4, DpiStage5 })
        {
            button.IsChecked = ReferenceEquals(button, sender);
        }

        UpdateSelection();
    }

    private void UpdateSelection()
    {
        foreach (var (button, index) in new[] { DpiStage1, DpiStage2, DpiStage3, DpiStage4, DpiStage5 }
                     .Select((button, index) => (button, index)))
        {
            button.IsChecked = index + 1 == _stage;
        }

        SelectionText.Text =
            $"{string.Format(AppStrings.Text("Help_Specimen_DpiStage"), _stage)} / {DpiValues[_stage - 1]} DPI";
    }
}
