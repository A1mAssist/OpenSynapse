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
    internal static IReadOnlySet<int>? FindCoveredInterfaces(
        IReadOnlyList<ushort> productIds,
        string? infDirectory = null)
    {
        var covered = new HashSet<int>();
        foreach (var productId in productIds)
        {
            foreach (var inf in FindStagedInfs(productId, infDirectory ?? WindowsInfDirectory))
            {
                string contents;
                try
                {
                    contents = File.ReadAllText(inf);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                for (var index = contents.IndexOf("MI_", StringComparison.OrdinalIgnoreCase);
                     index >= 0;
                     index = contents.IndexOf("MI_", index + 3, StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 5 > contents.Length)
                    {
                        continue;
                    }

                    try
                    {
                        covered.Add(Convert.ToInt32(contents.Substring(index + 3, 2), 16));
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
