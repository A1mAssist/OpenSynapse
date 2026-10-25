using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// Guards the device coverage claimed by the middleware protocol reference: every product it
/// lists must be referenced by the product code, except the ones whose middleware surface is
/// a native DLL rather than a wire protocol.
/// </summary>
public sealed class MiddlewareDeviceCoverageTests
{
    private static readonly IReadOnlyDictionary<string, string> NativeOnly =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // DiscoverBridges / PairBridge / GetConnectedBridgeInfo go through callDLLApi in
            // philipsEngineManager; no HID command of its own exists to parse.
            ["0301"] = "Philips Hue speaks to a bridge through a native DLL",
        };

    [Fact]
    public void EveryMiddlewareDeviceIsReferencedByTheProduct()
    {
        var repository = FindRepositoryRoot();
        var reference = File.ReadAllText(
            Path.Combine(repository, "docs", "MIDDLEWARE_DEVICE_PROTOCOLS.md"));
        var pids = Regex.Matches(reference, @"`1532:([0-9A-F]{4})`")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(119, pids.Length);

        var source = LoadProductSource(repository);
        var missing = pids
            .Where(pid => !NativeOnly.ContainsKey(pid))
            .Where(pid => !source.Contains($"0x{pid}", StringComparison.OrdinalIgnoreCase) &&
                          !source.Contains($"\"{pid}\"", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(missing.Length == 0,
            "Middleware devices without a product reference: " + string.Join(", ", missing));
    }

    private static string LoadProductSource(string repository)
    {
        var builder = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(repository, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                file.Contains($"{Path.DirectorySeparatorChar}.isolated-build{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            builder.AppendLine(File.ReadAllText(file));
        }

        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(repository, "src", "OpenSynapse.Windows", "Devices", "OpenRazer"),
                     "*.json",
                     SearchOption.AllDirectories))
        {
            builder.AppendLine(File.ReadAllText(file));
        }

        return builder.ToString();
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenSynapse.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the OpenSynapse repository root.");
    }
}
