using Microsoft.UI.Xaml;
using OpenSynapse.App.ViewModels;
using OpenSynapse.App.Views;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Core.Storage;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private static string? CategoryName(DeviceCategory category) =>
        category == DeviceCategory.Unknown ? null : category.ToString().ToLowerInvariant();

    internal Task SuspendStudioLightingAsync() =>
        _lightingStudioPage?.SuspendAppliedEffectAsync() ?? Task.CompletedTask;

    /// <summary>
    /// Replays the studio project that was live at the last exit. The page is
    /// created hidden; nothing is applied when the user never pressed Apply.
    /// </summary>
    internal async Task RestoreAppliedStudioLightingAsync()
    {
        if (!LightingStudioViewModel.HasAppliedProject)
        {
            return;
        }

        var created = _lightingStudioPage is null;
        try
        {
            var page = EnsureRestoreStudioPage();
            if (await page.RestoreAppliedEffectAsync())
            {
                return;
            }
        }
        catch (Exception)
        {
            // A bad snapshot must not stop startup; the user can still apply manually.
        }

        if (created && !IsPageCurrent("studio"))
        {
            ReleasePage("studio");
        }
    }

    /// <summary>
    /// Creates the hidden restore page without adding it to the page cache; cache
    /// trimming here would evict the settings page and drop its update prompt.
    /// </summary>
    private LightingStudioPage EnsureRestoreStudioPage()
    {
        if (_lightingStudioPage is { } existing)
        {
            return existing;
        }

        var page = CreateLightingStudioPage();
        _lightingStudioPage = page;
        return page;
    }

    private Task SuspendStudioLightingForDisplayAsync() =>
        _lightingStudioPage?.ViewModel?.SuspendForExternalAsync() ?? Task.CompletedTask;

    private async Task ResumeStudioLightingForDisplayAsync()
    {
        if (_viewModel.ChromaLightingActive?.Invoke() != true &&
            _lightingStudioPage?.ViewModel is { } viewModel)
        {
            await viewModel.ResumeAfterExternalAsync();
        }
    }

    private IReadOnlyList<StudioDeviceOption> BuildStudioDevices()
    {
        var options = new List<StudioDeviceOption>();
        foreach (var descriptor in _viewModel.CurrentDeviceDescriptors)
        {
            if (StudioBladeDevices.Resolve(descriptor) is not { } profile) continue;
            options.Add(new StudioDeviceOption(
                descriptor.Name,
                descriptor,
                descriptor.Id,
                descriptor.ProductId,
                profile.Rows,
                profile.Columns,
                IsBlade: true,
                BladeFrameMode: profile.Mode,
                Category: "keyboard"));
        }

        foreach (var row in _viewModel.RazerHid.Devices)
        {
            var connection = row.Connection;
            if (connection.Definition.Category == DeviceCategory.Laptop) continue;
            if (connection.Definition.ProductId == StudioArgbDevice.ProductId) continue;

            var staticZone = StudioHidOutputRouter.FindStaticZone(connection);
            var writesFrames = connection.Capabilities.Contains(RazerHidBackendCapability.MatrixFrameWrite);
            if (connection.Definition.MatrixDimensions is { } dimensions &&
                (writesFrames || staticZone is null))
            {
                options.Add(new StudioDeviceOption(
                    connection.Definition.DisplayName,
                    null,
                    connection.InstanceId,
                    connection.Definition.ProductId,
                    dimensions.Rows,
                    dimensions.Columns,
                    Category: CategoryName(connection.Definition.Category))
                {
                    HasNoPixelOutput = connection.IsReady && !writesFrames,
                    IsOutputAvailable = connection.IsReady,
                });
                continue;
            }

            if (staticZone is not null)
            {
                // Without frame writes, every LED in the zone shows one color; the
                // canvas gets one representative cell instead of a per-key promise.
                options.Add(new StudioDeviceOption(
                    connection.Definition.DisplayName,
                    null,
                    connection.InstanceId,
                    connection.Definition.ProductId,
                    1,
                    1,
                    IsSingleZone: true,
                    Category: CategoryName(connection.Definition.Category))
                {
                    IsOutputAvailable = connection.IsReady,
                });
            }
        }

        // Class-0x10 devices (mice, lamps, docks) have no frame channel on either
        // stack; the studio drives them through one representative colour.
        foreach (var chroma in _viewModel.Accessories.ChromaDevices)
        {
            var connection = chroma.Connection;
            if (connection.ProductId == StudioArgbDevice.ProductId) continue;
            options.Add(new StudioDeviceOption(
                connection.Name,
                null,
                connection.DevicePath,
                connection.ProductId,
                1,
                1,
                IsSingleZone: true,
                Category: CategoryName(connection.Category)));
        }

        // Keyboards owned by the keyboard page join the canvas through a synthetic
        // matrix definition; their settings page stays unchanged.
        foreach (var keyboard in _viewModel.Accessories.KeyboardDevices)
        {
            var connection = StudioKeyboardCatalog.CreateConnection(keyboard.Connection);
            if (connection?.Definition.MatrixDimensions is not { } keyboardMatrix ||
                options.Any(option => string.Equals(
                    option.EffectiveInstanceId, connection.InstanceId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            options.Add(new StudioDeviceOption(
                connection.Definition.DisplayName,
                null,
                connection.InstanceId,
                connection.Definition.ProductId,
                keyboardMatrix.Rows,
                keyboardMatrix.Columns,
                Category: "keyboard"));
        }

        if (_viewModel.RazerHid.ArgbConnection is { } argb)
        {
            var info = StudioArgbDevice.Create(
                argb.InstanceId, argb.DisplayName, ArgbStripSettings.Load());
            options.Add(new StudioDeviceOption(
                info.Name,
                null,
                info.InstanceId,
                info.ProductId,
                info.Rows,
                info.Columns,
                IsArgb: true,
                Layout: info.Layout,
                Category: "argb") { IsOutputAvailable = argb.IsReady });
        }

        AddHeadsetStudioDevice(options);

        return options;
    }

    internal async Task RefreshStudioArgbAsync()
    {
        try
        {
            await _viewModel.RazerHid.RefreshArgbDiscoveredAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Device enumeration failing must not break opening the studio page.
        }
    }

    private StudioHidOutputRouter? CreateStudioHidRouter(StudioInputSampler sampler)
    {
        var service = _viewModel.RazerHid.DeviceService;
        if (service is null) return null;
        return new StudioHidOutputRouter(
            service,
            () => _viewModel.RazerHid.CurrentConnections
                .Where(connection =>
                    connection.Definition.Category != DeviceCategory.Laptop &&
                    connection.Definition.ProductId != StudioArgbDevice.ProductId)
                .Concat(_viewModel.Accessories.KeyboardDevices
                    .Select(row => StudioKeyboardCatalog.CreateConnection(row.Connection))
                    .Where(connection => connection is not null)
                    .Select(connection => connection!))
                .ToArray(),
            inputProvider: (rows, columns) => sampler.Sample(rows, columns))
        {
            BeforeTakeOverAsync = _viewModel.RazerHid.PrepareStudioLightingAsync,
        };
    }

    private StudioArgbOutputRouter? CreateStudioArgbRouter(StudioInputSampler sampler)
    {
        var service = _viewModel.RazerHid.SpecialLightingService;
        if (service is null) return null;
        return new StudioArgbOutputRouter(
            service,
            () => ArgbStripSettings.Load(),
            (rows, columns) => sampler.Sample(rows, columns));
    }

    private StudioChromaStandardOutputRouter CreateStudioChromaRouter(StudioInputSampler sampler) =>
        new(
            () => _viewModel.Accessories.ChromaDevices
                .Select(row => row.Connection)
                .Where(connection => connection.ProductId != StudioArgbDevice.ProductId)
                .ToArray(),
            (rows, columns) => sampler.Sample(rows, columns));

    internal void ForwardBladeMappingInput(BladeMappingInputEvent input)
    {
        _dispatcherQueue.TryEnqueue(() => _lightingStudioPage?.HandleMappingInput(input));
    }
}
