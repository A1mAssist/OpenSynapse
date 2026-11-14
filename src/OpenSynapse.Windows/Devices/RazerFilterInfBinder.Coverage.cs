namespace OpenSynapse.Windows.Devices;

internal static partial class RazerFilterInfBinder
{
    internal static bool IsAttachedForInstalledInfs(IReadOnlyList<ushort> productIds) =>
        InspectAttachment(
            productIds,
            coveredInterfaces: FindCoveredInterfaces(productIds)).IsAttached;

    /// <summary>
    /// The MI_xx function an INF actually declares. A package that only ships an
    /// MI_00/MI_01 HID filter cannot make the secondary MI_02 consumer collection
    /// bind, so that collection must not keep the device card in a failed state.
    /// Returns null when no installed INF names an interface at all.
    /// </summary>
    internal static IReadOnlySet<(ushort ProductId, int InterfaceNumber)>? FindCoveredInterfaces(
        IReadOnlyList<ushort> productIds,
        string? infDirectory = null)
    {
        var covered = new HashSet<(ushort ProductId, int InterfaceNumber)>();
        foreach (var productId in productIds)
        {
            foreach (var inf in FindStagedInfs(productId, infDirectory ?? WindowsInfDirectory))
            {
                try
                {
                    foreach (var line in File.ReadLines(inf))
                    {
                        // A package can name a wired sibling on a different interface.
                        // That sibling's MI_01 collection must not make this PID require
                        // the same interface when the device is connected over USB.
                        if (!line.Contains($"Pid_{productId:X4}", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        for (var index = line.IndexOf("MI_", StringComparison.OrdinalIgnoreCase);
                             index >= 0;
                             index = line.IndexOf("MI_", index + 3, StringComparison.OrdinalIgnoreCase))
                        {
                            if (index + 5 > line.Length)
                            {
                                continue;
                            }

                            try
                            {
                                covered.Add((
                                    productId,
                                    Convert.ToInt32(line.Substring(index + 3, 2), 16)));
                            }
                            catch (FormatException)
                            {
                            }
                            catch (ArgumentOutOfRangeException)
                            {
                            }
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
            }
        }

        return covered.Count == 0 ? null : covered;
    }

    private static bool TryGetInterfaceNumber(string deviceName, out int interfaceNumber)
    {
        interfaceNumber = 0;
        var index = deviceName.IndexOf("&MI_", StringComparison.OrdinalIgnoreCase);
        if (index < 0 || index + 6 > deviceName.Length)
        {
            return false;
        }

        try
        {
            interfaceNumber = Convert.ToInt32(deviceName.Substring(index + 4, 2), 16);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
