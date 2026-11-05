namespace OpenSynapse.Windows.Macros;

/// <summary>
/// Slot ids are 1..MaxId. The mapping page only offers slots the device does not
/// already list, so saving a user macro cannot silently replace another macro.
/// </summary>
public static class RazerOnboardMacroSlots
{
    public static IReadOnlyList<byte> FindFreeSlots(
        int maxId,
        IReadOnlyCollection<byte> usedIds)
    {
        ArgumentNullException.ThrowIfNull(usedIds);
        var limit = Math.Clamp(maxId, 0, byte.MaxValue);
        if (limit == 0)
        {
            return [];
        }

        var used = usedIds.ToHashSet();
        var free = new List<byte>(limit);
        for (var id = 1; id <= limit; id++)
        {
            var slot = (byte)id;
            if (!used.Contains(slot))
            {
                free.Add(slot);
            }
        }

        return free;
    }
}
