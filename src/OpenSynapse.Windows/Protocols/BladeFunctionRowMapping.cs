using System.Text.Json.Nodes;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Synapse keeps one function-row mapping graph and flips its HyperShift layer
/// when the Fn row's primary layer changes. The shipped graph is function-primary,
/// so multimedia-primary moves every KEY_F1..KEY_F12 input off the HyperShift layer.
/// </summary>
public static class BladeFunctionRowMapping
{
    public static void ApplyPrimary(JsonObject graph, bool functionPrimary)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (graph["mappings"] is not JsonArray mappings)
        {
            return;
        }

        foreach (var node in mappings)
        {
            if (node is not JsonObject mapping ||
                mapping["input"] is not JsonObject input ||
                input["type"]?.GetValue<string>() is not "keyboard" ||
                input["scancode"]?.GetValue<int>() is not int scancode ||
                !IsFunctionRowScanCode(scancode))
            {
                continue;
            }

            input["hypershift"] = functionPrimary;
        }
    }

    private static bool IsFunctionRowScanCode(int scancode) =>
        scancode is >= 0x3B and <= 0x44 or 0x57 or 0x58;
}
