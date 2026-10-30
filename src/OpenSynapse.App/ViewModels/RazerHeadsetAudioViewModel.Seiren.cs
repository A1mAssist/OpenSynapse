using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    private RazerSeirenEmoteViewModel? _seirenEmote;

    private bool IsSeirenEmote => _connection.ProductId == 0x0F1B;

    public RazerSeirenEmoteViewModel? SeirenEmote => _seirenEmote;

    /// <summary>
    /// Mic-body lighting (the Seiren Emote matrix) has its own 0F1B frame path;
    /// the shared class 0x10 Chroma card must not be used for it.
    /// </summary>
    public Visibility MatrixLightingVisibility =>
        _hasLighting && !IsSeirenEmote ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SeirenEmoteVisibility =>
        _seirenEmote is null ? Visibility.Collapsed : Visibility.Visible;

    private void InitializeSeirenEmote()
    {
        if (IsSeirenEmote)
        {
            _seirenEmote = new RazerSeirenEmoteViewModel(_diagnostic);
        }
    }

    private Task LoadSeirenEmoteAsync(CancellationToken cancellationToken) =>
        _seirenEmote?.LoadAsync(cancellationToken) ?? Task.CompletedTask;

    private async Task DisposeSeirenEmoteAsync()
    {
        if (_seirenEmote is { } seirenEmote)
        {
            await seirenEmote.DisposeAsync().ConfigureAwait(false);
        }
    }
}
