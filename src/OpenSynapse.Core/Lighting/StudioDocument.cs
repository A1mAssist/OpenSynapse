using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSynapse.Core.Lighting;

/// <summary>
/// A lighting studio document: one canvas, an ordered layer stack, and the
/// device-independent effect parameters needed to reproduce a frame.
/// </summary>
public sealed class StudioDocument : INotifyPropertyChanged
{
    public const int CurrentSchemaVersion = 1;
    private string _name = "Untitled Studio";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set
        {
            var trimmed = string.IsNullOrWhiteSpace(value) ? "Untitled Studio" : value.Trim();
            if (_name == trimmed) return;
            _name = trimmed;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    /// <summary>Top of the stack first, matching the layer list in the editor.</summary>
    public ObservableCollection<StudioLayer> Layers { get; } = new();

    public StudioLayerSnapshot[] Snapshot() =>
        Layers.Where(layer => layer.Enabled)
            .Select(layer => layer.ToSnapshot())
            .ToArray();

    public static StudioDocument CreateDefault()
    {
        var document = new StudioDocument();
        document.Layers.Add(new StudioLayer("Background")
        {
            Effect = StudioEffectKind.Solid,
            Primary = new StudioRgb(0x44, 0xD6, 0x2C),
        });
        return document;
    }
}

internal sealed record StudioDocumentDto(
    int SchemaVersion,
    string? Name,
    StudioLayerDto[]? Layers);

internal sealed record StudioLayerDto(
    Guid Id,
    string? Name,
    StudioEffectKind Effect,
    bool Enabled,
    double Opacity,
    StudioBlendMode Blend,
    StudioRgb Primary,
    StudioRgb Secondary,
    double Speed);

public static class StudioDocumentCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(StudioDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var dto = new StudioDocumentDto(
            StudioDocument.CurrentSchemaVersion,
            document.Name,
            document.Layers.Select(layer => new StudioLayerDto(
                layer.Id,
                layer.Name,
                layer.Effect,
                layer.Enabled,
                layer.Opacity,
                layer.Blend,
                layer.Primary,
                layer.Secondary,
                layer.Speed)).ToArray());
        return JsonSerializer.Serialize(dto, Options);
    }

    public static StudioDocument Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var dto = JsonSerializer.Deserialize<StudioDocumentDto>(json, Options)
            ?? throw new InvalidOperationException("The lighting studio document is empty.");
        if (dto.SchemaVersion > StudioDocument.CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"The lighting studio document schema {dto.SchemaVersion} is newer than this build supports.");
        }

        var document = new StudioDocument { Name = dto.Name ?? "Untitled Studio" };
        foreach (var layer in dto.Layers ?? [])
        {
            if (layer is null) continue;
            document.Layers.Add(new StudioLayer(layer.Name ?? "Layer")
            {
                Id = layer.Id == Guid.Empty ? Guid.NewGuid() : layer.Id,
                Effect = Enum.IsDefined(layer.Effect) ? layer.Effect : StudioEffectKind.Solid,
                Enabled = layer.Enabled,
                Opacity = layer.Opacity,
                Blend = Enum.IsDefined(layer.Blend) ? layer.Blend : StudioBlendMode.Normal,
                Primary = layer.Primary,
                Secondary = layer.Secondary,
                Speed = layer.Speed,
            });
        }

        if (document.Layers.Count == 0)
        {
            document.Layers.Add(new StudioLayer("Background"));
        }

        return document;
    }
}
