using System.Text.Json;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerMappingAssetService
{
    private sealed record AssetManifest(string? MainJs, string? Image3x, string? Svg)
    {
        public static AssetManifest? Parse(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json.TrimStart('\uFEFF'));
                if (!document.RootElement.TryGetProperty("files", out var files) ||
                    files.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                string? mainJs = null;
                string? image = null;
                string? svg = null;
                foreach (var property in files.EnumerateObject())
                {
                    if (property.NameEquals("main.js"))
                    {
                        mainJs = Value(property.Value);
                    }
                    else if (image is null &&
                        IsPrd3x(property.Name))
                    {
                        image = Value(property.Value);
                    }
                    else if (svg is null &&
                        property.Name.Equals("static/media/prd.svg", StringComparison.OrdinalIgnoreCase))
                    {
                        svg = Value(property.Value);
                    }
                }

                return new AssetManifest(mainJs, image, svg);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static bool IsPrd3x(string name) =>
            name.StartsWith("static/media/prd-3x", StringComparison.OrdinalIgnoreCase) &&
            (name.EndsWith(".avif", StringComparison.OrdinalIgnoreCase) ||
             name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
             name.EndsWith(".png", StringComparison.OrdinalIgnoreCase));

        private static string? Value(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            _ => null,
        };
    }
}
