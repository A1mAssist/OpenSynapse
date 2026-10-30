namespace OpenSynapse.App.ViewModels;

public sealed partial class OpenRazerDeviceViewModel
{
    internal void FillMatrixWithPrimaryColor()
    {
        foreach (var cell in MatrixCells)
        {
            cell.Color = PrimaryColor;
        }
    }
}
