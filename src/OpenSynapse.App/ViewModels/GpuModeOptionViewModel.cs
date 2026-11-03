using OpenSynapse.Core.Displays;

namespace OpenSynapse.App.ViewModels;

public sealed record GpuModeOptionViewModel(
    BladeGpuMode Mode,
    string Title,
    string Summary,
    bool IsSelected,
    bool IsEnabled);
