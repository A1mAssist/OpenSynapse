namespace OpenSynapse.Core.Lighting;

/// <summary>Product cover availability for one studio device option.</summary>
public enum StudioProductImageState
{
    None,
    Loaded,
    Unavailable,
}

/// <summary>A product cover plus the initial shown when no usable image exists.</summary>
public sealed record StudioProductImage(
    StudioProductImageState State,
    string Initial,
    string? Path = null,
    string? Error = null)
{
    public static StudioProductImage ForName(string? name) =>
        new(StudioProductImageState.None, InitialFrom(name));

    public static StudioProductImage Loaded(string? name, string path) =>
        new(StudioProductImageState.Loaded, InitialFrom(name), path);

    public static StudioProductImage Unavailable(string? name, string? error) =>
        new(StudioProductImageState.Unavailable, InitialFrom(name), Error: error);

    public static string InitialFrom(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed)
            ? "?"
            : char.ToUpperInvariant(trimmed[0]).ToString();
    }
}