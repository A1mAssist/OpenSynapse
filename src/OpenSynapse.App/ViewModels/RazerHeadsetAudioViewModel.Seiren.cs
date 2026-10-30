using Microsoft.UI.Xaml;
namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    private bool IsSeirenEmote => _connection.ProductId == 0x0F1B;

    /// <summary>
    /// The 0F1B bundle declares <c>isChromaDevice:false</c> and exposes no matrix
    /// frame command, so the shared Chroma card stays hidden on this microphone.
    /// </summary>
    public Visibility MatrixLightingVisibility =>
        _hasLighting && !IsSeirenEmote ? Visibility.Visible : Visibility.Collapsed;
}
