namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    internal void FillMatrixWithPrimaryColor()
    {
        foreach (var cell in MatrixCells)
        {
            if (!cell.IsPlaceholder)
            {
                cell.Color = PrimaryColor;
            }
        }
    }
}
