using OpenSynapse.Core.Audio;

namespace OpenSynapse.Windows.Audio;

internal static class ThxAudioHelperProtocol
{
    internal const int Version = 1;
    internal const string ExecutableName = "OpenSynapse.ThxHelper.exe";
    internal const int MaximumLineCharacters = 64 * 1024;
}

internal sealed record ThxAudioHelperRequest(
    int Version,
    string Operation,
    string Endpoint,
    bool? Enabled = null,
    string? PresetId = null,
    double[]? GainsDb = null,
    string? Enhancement = null,
    int? LevelPercent = null,
    string? NativeVersion = null,
    string? ProductId = null,
    string? ContainerId = null);

internal sealed record ThxAudioHelperResponse(
    int Version,
    bool Ok,
    ThxAudioStatus? Status,
    string? Error);
