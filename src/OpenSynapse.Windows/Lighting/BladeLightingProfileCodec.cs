using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

internal static class BladeLightingProfileCodec
{
    internal static BladeLightingEffect Parse(LightingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var mode = profile.Effect?.Trim().ToLowerInvariant()
            ?? throw new InvalidOperationException("键盘灯效不能为空。");
        var parameters = profile.Parameters ?? new Dictionary<string, string>();
        string[] allowed = mode switch
        {
            "static" or "breathing" => ["color"],
            "wave" => ["direction"],
            "off" or "spectrum" or "fire" => [],
            _ => throw new InvalidOperationException($"不支持的键盘灯效：{profile.Effect}。"),
        };
        if (parameters.Keys.Any(key => !allowed.Contains(key, StringComparer.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"灯效 {profile.Effect} 包含不支持的参数。");
        }

        var color = parameters.TryGetValue("color", out var hex) ? ParseColor(hex) : default;
        var direction = ParseDirection(parameters);
        return new BladeLightingEffect(mode switch
        {
            "off" => BladeLightingMode.Off,
            "static" => BladeLightingMode.Static,
            "breathing" => BladeLightingMode.Breathing,
            "spectrum" => BladeLightingMode.Spectrum,
            "wave" => BladeLightingMode.Wave,
            _ => BladeLightingMode.Fire,
        }, color, direction);
    }

    internal static LightingProfile Create(BladeLightingEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        var profile = new LightingProfile { Effect = effect.Mode.ToString().ToLowerInvariant() };
        if (effect.Mode is BladeLightingMode.Static or BladeLightingMode.Breathing)
        {
            profile.Parameters["color"] =
                $"{effect.Color.Red:X2}{effect.Color.Green:X2}{effect.Color.Blue:X2}";
        }
        else if (effect.Mode == BladeLightingMode.Wave)
        {
            profile.Parameters["direction"] =
                effect.Direction == BladeWaveDirection.Left ? "left" : "right";
        }

        return profile;
    }

    internal static string Fingerprint(LightingProfile profile, string devicePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        var effect = Parse(profile);
        return $"{devicePath}\n{effect.Mode}\n{effect.Color.Red:X2}{effect.Color.Green:X2}{effect.Color.Blue:X2}\n{effect.Direction}";
    }

    private static BladeWaveDirection ParseDirection(IReadOnlyDictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("direction", out var value))
        {
            return BladeWaveDirection.Right;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "left" => BladeWaveDirection.Left,
            "right" => BladeWaveDirection.Right,
            _ => throw new InvalidOperationException("Wave 方向必须是 left 或 right。"),
        };
    }

    private static RazerRgb ParseColor(string value)
    {
        try
        {
            var bytes = Convert.FromHexString(value);
            return bytes.Length == 3
                ? new RazerRgb(bytes[0], bytes[1], bytes[2])
                : throw new FormatException();
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("灯效颜色必须是六位 RRGGBB。", exception);
        }
    }
}
