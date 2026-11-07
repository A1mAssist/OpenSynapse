namespace OpenSynapse.ChromaSdk.Shim;

internal static class SdkResult
{
    public const int Success = 0;
    public const int AccessDenied = 5;
    public const int NotSupported = 50;
    public const int InvalidParameter = 87;
    public const int ServiceNotActive = 1062;
    public const int ClientLimit = 1152;
    public const int DeviceNotConnected = 1167;
    public const int NotFound = 1168;
    public const int AlreadyInitialized = 1247;
    public const int NotValidState = 5023;
    public const int Failed = unchecked((int)0x80004005);
}
