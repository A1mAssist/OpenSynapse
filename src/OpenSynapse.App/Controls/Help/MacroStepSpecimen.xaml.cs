using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenSynapse.App.Controls.Help;

public sealed partial class MacroStepSpecimen : UserControl
{
    private readonly List<string> _stepKeys =
    [
        "Help_Specimen_MacroDelay",
        "Help_Specimen_MacroChord",
        "Help_Specimen_MacroText",
    ];

    public MacroStepSpecimen()
    {
        Rows = [];
        InitializeComponent();
        ResetRows();
    }

    public ObservableCollection<MacroStepRow> Rows { get; }

    private void RemoveStepClick(object sender, RoutedEventArgs e)
    {
        if (StepList.SelectedIndex < 0)
        {
            return;
        }

        _stepKeys.RemoveAt(StepList.SelectedIndex);
        ResetRows();
    }

    private void ResetRows()
    {
        Rows.Clear();
        for (var index = 0; index < _stepKeys.Count; index++)
        {
            Rows.Add(new MacroStepRow
            {
                Number = index + 1,
                Text = AppStrings.Text(_stepKeys[index]),
                AutomationId = $"HelpMacroStep{index + 1}",
            });
        }
    }
}

public sealed class MacroStepRow
{
    public int Number { get; set; }
    public string Text { get; set; } = string.Empty;
    public string AutomationId { get; set; } = string.Empty;
}
