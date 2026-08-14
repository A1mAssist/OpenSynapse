namespace OpenSynapse.ProtocolProbe;

public enum ProbeEvidenceLevel
{
    Verified,
    SourceBacked,
}

public sealed record ProbeCommand(
    ushort ProductId,
    string Name,
    ProbeEvidenceLevel Evidence,
    byte TransactionId,
    byte DataSize,
    byte CommandClass,
    byte CommandId,
    ReadOnlyMemory<byte> Arguments,
    int WaitMilliseconds,
    bool AllowRemainingPacketsMismatch = false);
