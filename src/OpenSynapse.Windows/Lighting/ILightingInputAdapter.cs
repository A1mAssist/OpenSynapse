namespace OpenSynapse.Windows.Lighting;

internal interface ILightingInputAdapter : IAsyncDisposable
{
    ValueTask StartAsync(CancellationToken cancellationToken);
    ValueTask StopAsync();
}
