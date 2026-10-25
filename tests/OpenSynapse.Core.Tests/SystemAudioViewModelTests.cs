using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Audio;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class SystemAudioViewModelTests
{
    [Fact]
    public async Task LoadSelectsTheWindowsDefaultsAndTheirState()
    {
        var controller = new FakeSystemAudioController();
        var viewModel = new SystemAudioViewModel(controller);

        await viewModel.LoadAsync();

        Assert.Equal("render-default", viewModel.SelectedRender?.Id);
        Assert.Equal("capture-default", viewModel.SelectedCapture?.Id);
        Assert.Equal(50, viewModel.RenderVolume);
        Assert.Equal(80, viewModel.CaptureVolume);
        Assert.True(viewModel.RenderMuted);
        Assert.False(viewModel.CaptureMuted);
        Assert.True(viewModel.CanEditRender);
        Assert.True(viewModel.CanEditCapture);
    }

    [Fact]
    public async Task ApplyRenderWritesVolumeMuteAndDefaultThenReloads()
    {
        var controller = new FakeSystemAudioController();
        var viewModel = new SystemAudioViewModel(controller);
        await viewModel.LoadAsync();
        viewModel.SelectedRender = viewModel.RenderEndpoints.Single(item => item.Id == "render-secondary");
        viewModel.RenderVolume = 65;
        viewModel.RenderMuted = false;

        await viewModel.ApplyRenderAsync();

        Assert.Equal(
            ["render-secondary:65:False", "render-secondary:mute:False", "default:render-secondary"],
            controller.Writes);
        Assert.Equal("render-secondary", viewModel.SelectedRender?.Id);
        Assert.Equal(65, viewModel.RenderVolume);
        Assert.Equal("SystemAudioRenderApplied", viewModel.OperationStatusText);
    }

    private sealed class FakeSystemAudioController : ISystemAudioController
    {
        private readonly List<SystemAudioEndpoint> _render =
        [
            new("render-default", "Speakers", SystemAudioDirection.Render, true, 50, true),
            new("render-secondary", "Headset", SystemAudioDirection.Render, false, 40, false),
        ];
        private readonly List<SystemAudioEndpoint> _capture =
        [
            new("capture-default", "Microphone", SystemAudioDirection.Capture, false, 80, true),
        ];

        public List<string> Writes { get; } = [];

        public Task<SystemAudioSnapshot> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SystemAudioSnapshot(_render.ToArray(), _capture.ToArray()));

        public Task SetDefaultAsync(string endpointId, CancellationToken cancellationToken = default)
        {
            Writes.Add($"default:{endpointId}");
            ReplaceDefault(_render, endpointId);
            ReplaceDefault(_capture, endpointId);
            return Task.CompletedTask;
        }

        public Task SetVolumeAsync(string endpointId, int volumePercent, CancellationToken cancellationToken = default)
        {
            Writes.Add($"{endpointId}:{volumePercent}:{Find(endpointId).IsMuted}");
            Replace(endpointId, endpoint => endpoint with { VolumePercent = volumePercent });
            return Task.CompletedTask;
        }

        public Task SetMuteAsync(string endpointId, bool muted, CancellationToken cancellationToken = default)
        {
            Writes.Add($"{endpointId}:mute:{muted}");
            Replace(endpointId, endpoint => endpoint with { IsMuted = muted });
            return Task.CompletedTask;
        }

        private SystemAudioEndpoint Find(string id) =>
            _render.Concat(_capture).Single(endpoint => endpoint.Id == id);

        private void Replace(string id, Func<SystemAudioEndpoint, SystemAudioEndpoint> update)
        {
            for (var index = 0; index < _render.Count; index++)
            {
                if (_render[index].Id == id) _render[index] = update(_render[index]);
            }
            for (var index = 0; index < _capture.Count; index++)
            {
                if (_capture[index].Id == id) _capture[index] = update(_capture[index]);
            }
        }

        private static void ReplaceDefault(List<SystemAudioEndpoint> endpoints, string id)
        {
            for (var index = 0; index < endpoints.Count; index++)
            {
                endpoints[index] = endpoints[index] with { IsDefault = endpoints[index].Id == id };
            }
        }
    }
}
