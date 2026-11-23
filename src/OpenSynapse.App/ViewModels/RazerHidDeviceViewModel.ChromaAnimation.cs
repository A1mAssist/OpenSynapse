using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    public Visibility ChromaAnimationVisibility => VisibleWhen(SupportsSoftwareLighting);

    /// <summary>
    /// Stores the animation in the shared lighting folder and applies it through
    /// the same software-frame runtime as the built-in keyboard effects.
    /// </summary>
    public async Task ImportChromaAnimationAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!SupportsSoftwareLighting)
        {
            throw new InvalidOperationException(
                "This keyboard cannot play a software .chroma animation.");
        }

        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("The .chroma animation was not found.", path);
        }
        if (info.Length > ChromaFileAnimationParser.MaximumFileSizeBytes)
        {
            throw new InvalidDataException(
                $"The .chroma file cannot exceed {ChromaFileAnimationParser.MaximumFileSizeBytes / (1024 * 1024)} MiB.");
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var animation = ChromaFileAnimationStore.Import(bytes);
        _chromaAnimation = animation;
        _chromaFileName = animation.FileName;
        SelectedLightingEffect = RazerHidLightingEffect.SoftwareChromaFile;
        await ApplyLightingAsync(cancellationToken).ConfigureAwait(true);
    }

    private bool TryLoadChromaAnimation()
    {
        if (_chromaAnimation is not null)
        {
            return true;
        }
        if (string.IsNullOrWhiteSpace(_chromaFileName))
        {
            return false;
        }

        return ChromaFileAnimationStore.TryLoad(_chromaFileName, out _chromaAnimation) &&
            _chromaAnimation is not null;
    }
}
