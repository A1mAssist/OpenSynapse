using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Lighting;

namespace OpenSynapse.App.ViewModels;

/// <summary>One selectable studio device, its canvas matrix, and its product art.</summary>
public sealed class StudioDeviceOption : INotifyPropertyChanged
{
    private StudioProductImage _productImage;
    private ImageSource? _image;

    public StudioDeviceOption(
        string name,
        DeviceDescriptor? descriptor,
        string? instanceId = null,
        ushort productId = 0,
        int rows = 0,
        int columns = 0,
        bool IsBlade = false,
        bool IsArgb = false,
        StudioLedLayout? Layout = null,
        bool IsSingleZone = false,
        StudioBladeFrameMode BladeFrameMode = StudioBladeFrameMode.Sparse710,
        string? Category = null)
    {
        Name = name;
        Descriptor = descriptor;
        InstanceId = instanceId;
        ProductId = productId;
        Rows = rows;
        Columns = columns;
        this.IsBlade = IsBlade;
        this.IsArgb = IsArgb;
        this.Layout = Layout;
        this.IsSingleZone = IsSingleZone;
        this.BladeFrameMode = BladeFrameMode;
        this.Category = Category;
        _productImage = StudioProductImage.ForName(name);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    public DeviceDescriptor? Descriptor { get; }

    public string? InstanceId { get; }

    public ushort ProductId { get; }

    public int Rows { get; }

    public int Columns { get; }

    public bool IsBlade { get; }

    /// <summary>How this Blade's preview matrix becomes the HID frame.</summary>
    public StudioBladeFrameMode BladeFrameMode { get; }

    /// <summary>Chroma device category for profile export, such as keyboard or mouse.</summary>
    public string? Category { get; }

    public bool IsArgb { get; }

    /// <summary>One representative color for a device that cannot address individual LEDs.</summary>
    public bool IsSingleZone { get; }

    /// <summary>True when a matrix is visible but the device accepts neither frame writes nor a static zone.</summary>
    public bool HasNoPixelOutput { get; init; }

    public StudioLedLayout? Layout { get; }

    public ushort EffectiveProductId => Descriptor?.ProductId ?? ProductId;

    public string? EffectiveInstanceId => InstanceId ?? Descriptor?.Id;

    public bool HasSameIdentity(StudioDeviceOption? other) =>
        other is not null && EffectiveProductId == other.EffectiveProductId &&
        string.Equals(EffectiveInstanceId, other.EffectiveInstanceId, StringComparison.OrdinalIgnoreCase);

    public bool HasMatrix => Rows > 0 && Columns > 0;

    public StudioProductImage ProductImage => _productImage;

    public StudioProductImageState ImageState => _productImage.State;

    public string ImageInitial => _productImage.Initial;

    public ImageSource? Image => _image;

    public Visibility ImageVisibility => _image is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility FallbackVisibility => _image is null ? Visibility.Visible : Visibility.Collapsed;

    public string MatrixLabel => HasMatrix ? FormattableString.Invariant($"{Rows} × {Columns}") : string.Empty;

    public void ApplyProductImage(StudioProductImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        _productImage = image;
        _image = image.State == StudioProductImageState.Loaded && image.Path is { } path
            ? new BitmapImage(new Uri(path)) { DecodePixelWidth = 256 }
            : null;
        OnPropertyChanged(nameof(ProductImage));
        OnPropertyChanged(nameof(ImageState));
        OnPropertyChanged(nameof(ImageInitial));
        OnPropertyChanged(nameof(Image));
        OnPropertyChanged(nameof(ImageVisibility));
        OnPropertyChanged(nameof(FallbackVisibility));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
