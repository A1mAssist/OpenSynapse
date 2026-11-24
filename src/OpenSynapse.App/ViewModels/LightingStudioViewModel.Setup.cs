using Microsoft.UI.Dispatching;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Lighting;

namespace OpenSynapse.App.ViewModels;

public sealed partial class LightingStudioViewModel
{
    internal LightingStudioViewModel(
        IBladeLightingController? lighting,
        Func<IReadOnlyList<DeviceDescriptor>> devices,
        Func<IReadOnlyList<StudioDeviceOption>>? matrixDevices = null,
        StudioHidOutputRouter? hidRouter = null,
        StudioInputSampler? inputSampler = null,
        StudioArgbOutputRouter? argbRouter = null,
        StudioHeadsetOutputRouter? headsetRouter = null,
        StudioChromaStandardOutputRouter? chromaRouter = null,
        Func<Task>? restoreAfterExternal = null)
    {
        _lighting = lighting;
        _devices = devices ?? throw new ArgumentNullException(nameof(devices));
        _matrixDevices = matrixDevices;
        _hidRouter = hidRouter;
        _argbRouter = argbRouter;
        _headsetRouter = headsetRouter;
        _chromaRouter = chromaRouter;
        _inputSampler = inputSampler;
        _world = new StudioWorldRenderer(
            (rows, columns) => _inputSampler?.Sample(rows, columns) ?? default);
        _restoreAfterExternal = restoreAfterExternal;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        if (_hidRouter is not null)
        {
            _hidRouter.AttachWorld(_world);
            _hidRouter.WriteFailed += OnOutputWriteFailed;
            _hidRouter.RestoreFailed += OnHidRestoreFailed;
            _hidRouter.InputObserved += OnHidInputObserved;
        }
        if (_argbRouter is not null)
        {
            _argbRouter.AttachWorld(_world);
            _argbRouter.WriteFailed += OnOutputWriteFailed;
        }
        if (_headsetRouter is not null)
        {
            _headsetRouter.AttachWorld(_world);
            _headsetRouter.WriteFailed += OnOutputWriteFailed;
        }
        if (_chromaRouter is not null)
        {
            _chromaRouter.AttachWorld(_world);
            _chromaRouter.WriteFailed += OnOutputWriteFailed;
            _chromaRouter.RestoreFailed += OnHidRestoreFailed;
        }
        if (_lighting is not null) _lighting.RestoreFailed += OnHidRestoreFailed;

        _project = StudioProject.CreateDefault();
        LocalizeDefaultProject(_project);
        _history = new StudioHistory(_project);
        Layers.CollectionChanged += OnLayersChanged;
        LoadLocalizedChoices();
        LoadSceneCards();

        AttachProfile(_project.SelectedProfile);
        SyncLayersFromProfile();
        _selectedLayer = Layers.FirstOrDefault();
        RefreshRegionChoices();
        // The first screen must already show Ambient/Audio/Generate instead of a black frame.
        _ = EnsureInputAsync();
        _ = EnsureMediaSamplerAsync();
    }
}
