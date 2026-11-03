using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Renders the studio document onto the Blade logical canvas and maps it to the
/// sparse 6 x 17 device frame used by the software lighting pump.
/// </summary>
public sealed class StudioBladeFrameSource : ISoftwareLightingFrameSource
{
    private StudioLayerSnapshot[] _layers;

    public StudioBladeFrameSource(IEnumerable<StudioLayerSnapshot> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        _layers = layers.ToArray();
    }

    /// <summary>Replaces the document snapshot used by the next frame.</summary>
    public void Update(IEnumerable<StudioLayerSnapshot> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        Volatile.Write(ref _layers, layers.ToArray());
    }

    public ValueTask<IReadOnlyList<RazerRgb>> RenderAsync(
        TimeSpan elapsed,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var logical = StudioCompositor.Render(
            Volatile.Read(ref _layers),
            BladeLightingLayout.LogicalRows,
            BladeLightingLayout.LogicalColumns,
            elapsed);
        var colors = new RazerRgb[logical.Length];
        for (var index = 0; index < logical.Length; index++)
        {
            colors[index] = new RazerRgb(logical[index].R, logical[index].G, logical[index].B);
        }

        return ValueTask.FromResult<IReadOnlyList<RazerRgb>>(
            BladeLightingLayout.MapToDeviceFrame(colors));
    }
}
