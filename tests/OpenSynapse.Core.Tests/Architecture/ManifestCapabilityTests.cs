using System.Reflection;
using System.Text.RegularExpressions;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class ManifestCapabilityTests
{
    private static readonly Regex CapabilityLiteral =
        new("\"[a-z0-9]+(?:-[a-z0-9]+)*\\.(?:get|set)\"", RegexOptions.Compiled);

    [Fact]
    public void RegistryCapabilityIdsAreDeclared()
    {
        var declared = CapabilityIds.All.ToHashSet(StringComparer.Ordinal);
        var missing = new List<string>();

        foreach (var (family, capabilities) in RazerDeviceRegistry.Families)
        {
            missing.AddRange(capabilities.Keys
                .Where(capability => !declared.Contains(capability))
                .Select(capability => $"family {family}: {capability}"));
        }

        foreach (var manifest in RazerDeviceRegistry.BuiltIn.Manifests)
        {
            missing.AddRange(manifest.Capabilities.Keys
                .Where(capability => !declared.Contains(capability))
                .Select(capability => $"manifest {manifest.Id}: {capability}"));
        }

        foreach (var (from, to) in RazerDeviceRegistry.CapabilityRenameMap)
        {
            if (!declared.Contains(from))
            {
                missing.Add($"rename key: {from}");
            }
            if (!declared.Contains(to))
            {
                missing.Add($"rename value: {to}");
            }
        }

        Assert.True(
            missing.Count == 0,
            "Capability ids without a CapabilityIds constant:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, missing.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void EveryCapabilityConstantIsUsedInSource()
    {
        var constants = typeof(CapabilityIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (field.Name, Value: (string)field.GetRawConstantValue()!))
            .OrderBy(field => field.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(constants.Select(field => field.Value).ToArray(), CapabilityIds.All.ToArray());

        var sources = RepoLayout.CodeFiles()
            .Where(path => !string.Equals(Path.GetFileName(path), "CapabilityIds.cs", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToArray();
        var unused = constants
            .Where(field => !sources.Any(source =>
                source.Contains("CapabilityIds." + field.Name, StringComparison.Ordinal)))
            .Select(field => "CapabilityIds." + field.Name)
            .ToArray();

        Assert.True(
            unused.Length == 0,
            "Capability constants never used outside CapabilityIds.cs:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, unused));
    }

    [Fact]
    public void SourceContainsNoCapabilityIdLiterals()
    {
        var violations = new List<string>();
        foreach (var path in RepoLayout.CodeFiles())
        {
            if (string.Equals(Path.GetFileName(path), "CapabilityIds.cs", StringComparison.Ordinal))
            {
                continue;
            }

            var lineNumber = 0;
            foreach (var line in File.ReadLines(path))
            {
                lineNumber++;
                foreach (Match match in CapabilityLiteral.Matches(line))
                {
                    violations.Add($"{RepoLayout.Relative(path)}:{lineNumber}: {match.Value}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Capability ids must come from CapabilityIds, not string literals:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }
}
