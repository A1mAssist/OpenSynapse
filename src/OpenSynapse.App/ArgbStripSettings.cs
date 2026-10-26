using OpenSynapse.Core.Storage;

namespace OpenSynapse.App;

internal static class ArgbStripSettings
{
    internal const int Ports = 6;
    internal const int MaxLeds = 80;

    private static readonly string SettingsPath = AppDataPaths.Combine("argb-led-count.txt");

    internal static int[] Load()
    {
        var counts = new int[Ports];
        Array.Fill(counts, MaxLeds);
        try
        {
            if (!File.Exists(SettingsPath)) return counts;
            var parts = File.ReadAllText(SettingsPath).Split(',');
            for (var index = 0; index < Math.Min(Ports, parts.Length); index++)
            {
                if (int.TryParse(parts[index], out var value) && value is >= 1 and <= MaxLeds)
                {
                    counts[index] = value;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return counts;
    }

    internal static void Save(int[] counts)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, string.Join(',', counts));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
