namespace OpenSynapse.Windows.Lifecycle;

public static partial class OpenSynapseServiceClient
{
    /// <summary>
    /// Asks the machine-wide service for the CPU package temperature. Only an elevated
    /// process can open PawnIO, so the app cannot read it locally. Null means the
    /// service is unavailable or could not read the sensor; the caller keeps its own value.
    /// </summary>
    public static async Task<double?> TryReadCpuTemperatureAsync(
        CancellationToken cancellationToken = default)
    {
        var request = new OpenSynapseServiceRequest(
            OpenSynapseServiceProtocol.Version,
            OpenSynapseServiceProtocol.ReadCpuTemperatureOperation,
            Enabled: null);

        var response = await SendForResponseAsync(
                request, cancellationToken, TimeSpan.FromSeconds(2))
            .ConfigureAwait(false);
        return response?.Handled == true ? response.CpuTemperatureCelsius : null;
    }
}
