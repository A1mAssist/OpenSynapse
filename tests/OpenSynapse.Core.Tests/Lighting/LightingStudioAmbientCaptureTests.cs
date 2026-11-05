using System.Text.Json;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Core.Storage;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioAmbientCaptureTests
{
    [Fact]
    public void ClampKeepsValidRegion()
    {
        var region = new StudioScreenRect(0.25, 0.5, 0.1, 0.2);

        Assert.Equal(region, region.Clamp());
    }

    [Fact]
    public void ClampDefaultsEmptyAndNonFiniteRegionsToFullScreen()
    {
        Assert.Equal(StudioScreenRect.FullScreen, new StudioScreenRect(0.2, 0.3, 0, 0.5).Clamp());
        Assert.Equal(StudioScreenRect.FullScreen, new StudioScreenRect(double.NaN, 0, 1, 1).Clamp());
        Assert.Equal(StudioScreenRect.FullScreen, new StudioScreenRect(0, 0, 1, double.PositiveInfinity).Clamp());
    }

    [Fact]
    public void ClampMovesOutOfBoundsAndGrowsTinyRegions()
    {
        Assert.Equal(
            new StudioScreenRect(0.5, 0.5, 0.5, 0.5),
            new StudioScreenRect(0.99, 0.99, 0.5, 0.5).Clamp());
        Assert.Equal(
            new StudioScreenRect(0, 0, 0.4, 0.5),
            new StudioScreenRect(-0.1, -0.2, 0.4, 0.5).Clamp());
        Assert.Equal(
            new StudioScreenRect(0.5, 0.5, 0.01, 0.01),
            new StudioScreenRect(0.5, 0.5, 0.001, 0.001).Clamp());
    }

    [Fact]
    public void ToPixelBoundsUsesPhysicalPixelsWithoutDpiScaling()
    {
        var quarter = new StudioScreenRect(0.25, 0.25, 0.5, 0.5);

        Assert.Equal((480, 270, 960, 540), quarter.ToPixelBounds(1920, 1080));
        // 2560x1440 is a 150% scaled 1707x960 desktop. Normalized coordinates
        // already describe the physical capture, so 0.3/0.4 stays 768/576 pixels.
        Assert.Equal((256, 288, 768, 576), new StudioScreenRect(0.1, 0.2, 0.3, 0.4).ToPixelBounds(2560, 1440));
        // The 0.1% input is below the 1% minimum, so it grows and shifts to the edge.
        Assert.Equal((1900, 1069, 20, 11), new StudioScreenRect(0.999, 0.999, 0.001, 0.001).ToPixelBounds(1920, 1080));
    }

    [Fact]
    public void CroppedReductionMatchesSamplingTheCroppedFrame()
    {
        var frame = CreateFrame(16, 16);
        var cropped = Crop(frame, 10, 6, 5, 7);

        var actual = WindowsDisplayCaptureAdapter.ReduceEdgeBand(
            frame.Bgra, frame.Width, frame.Height, frame.Stride, 10, 6, 5, 7, 2);
        var expected = WindowsDisplayCaptureAdapter.ReduceEdgeBand(
            cropped.Bgra, cropped.Width, cropped.Height, cropped.Stride, 2);

        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Pixels, actual.Pixels);
    }

    [Fact]
    public void FullCropMatchesTheLegacyFullFrameReduction()
    {
        var frame = CreateFrame(12, 10);

        var legacy = WindowsDisplayCaptureAdapter.ReduceEdgeBand(
            frame.Bgra, frame.Width, frame.Height, frame.Stride, 2);
        var cropped = WindowsDisplayCaptureAdapter.ReduceEdgeBand(
            frame.Bgra, frame.Width, frame.Height, frame.Stride, 0, 0, 12, 10, 2);

        Assert.Equal(legacy.Pixels, cropped.Pixels);
    }

    [Fact]
    public void CaptureAdapterClampsRegionBeforeTheNextFrame()
    {
        var adapter = new WindowsDisplayCaptureAdapter(() => new IdleSession());

        adapter.SetCaptureRegion(new StudioScreenRect(-1, -1, 0, 0));

        Assert.Equal(StudioScreenRect.FullScreen, adapter.CaptureRegion);
    }

    [Fact]
    public void CaptureRegionRoundTripsThroughAppSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opensynapse-ambient-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new AppBehaviorSettings
            {
                AmbientCaptureRegion = new StudioScreenRect(0.1, 0.2, 0.3, 0.4),
            };
            settings.Save(path);

            var restored = AppBehaviorSettings.Load(path);

            Assert.Equal(settings.AmbientCaptureRegion, restored.AmbientCaptureRegion);
            var invalid = JsonSerializer.Deserialize<AppBehaviorSettings>(
                "{\"AmbientCaptureRegion\":{\"X\":-1,\"Y\":-1,\"Width\":0,\"Height\":0}}")!;
            Assert.Equal(StudioScreenRect.FullScreen, invalid.AmbientCaptureRegion);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ScreenPresetsUseTheOfficialTenPercentEdgeBands()
    {
        Assert.Equal(new StudioScreenRect(0, 0, 0.1, 1), StudioScreenRect.Resolve("left", null));
        Assert.Equal(new StudioScreenRect(0, 0, 1, 0.1), StudioScreenRect.Resolve("top", null));
        Assert.Equal(new StudioScreenRect(0.9, 0, 0.1, 1), StudioScreenRect.Resolve("right", null));
        Assert.Equal(new StudioScreenRect(0, 0.9, 1, 0.1), StudioScreenRect.Resolve("bottom", null));
        Assert.Equal(StudioScreenRect.FullScreen, StudioScreenRect.Resolve("full", null));
    }

    [Fact]
    public void CustomScreenRectUsesTheLayerRect()
    {
        var custom = new StudioScreenRect(0.1, 0.2, 0.3, 0.4);

        Assert.Equal(custom, StudioScreenRect.Resolve("custom", custom));
    }

    [Fact]
    public void EffectRegionIsMappedIntoTheCaptureRegion()
    {
        var samples = new List<(double U, double V)>();
        var frame = StudioEffectRenderer.Render(
            new StudioEffectParams
            {
                Effect = StudioEffectKind.Ambient,
                Screen = "full",
                Blur = 0,
            },
            2,
            2,
            0,
            new StudioEffectInput(
                ScreenSamplerUv: (u, v) =>
                {
                    samples.Add((u, v));
                    return new StudioRgb(9, 8, 7);
                },
                ScreenCaptureRect: new StudioScreenRect(0.25, 0.25, 0.5, 0.5)));

        Assert.All(frame, pixel => Assert.Equal(new StudioRgb(9, 8, 7), pixel));
        Assert.Equal(4, samples.Count);
        Assert.All(samples, sample =>
        {
            Assert.InRange(sample.U, 0.25, 0.75);
            Assert.InRange(sample.V, 0.25, 0.75);
        });
        Assert.Equal((0.375, 0.375), samples[0]);
        Assert.Equal((0.625, 0.375), samples[1]);
        Assert.Equal((0.375, 0.625), samples[2]);
        Assert.Equal((0.625, 0.625), samples[3]);
    }

    [Fact]
    public void EffectRegionComposesWithTheGlobalCaptureCrop()
    {
        var capture = new StudioScreenRect(0.25, 0.25, 0.5, 0.5);

        Assert.Equal(
            new StudioScreenRect(0, 0, 0.5, 0.5),
            StudioScreenRect.MapIntoCapture(new StudioScreenRect(0.25, 0.25, 0.25, 0.25), capture));
        Assert.Equal(
            StudioScreenRect.FullScreen,
            StudioScreenRect.MapIntoCapture(new StudioScreenRect(0, 0, 0.1, 1), capture));
    }

    [Fact]
    public async Task CaptureFailureSurfacesAndTheAdapterRestarts()
    {
        var sessions = new Queue<IDisplayCaptureSession>(
        [
            new FaultingSession(CreateFrame(8, 8)),
            new OneFrameSession(CreateFrame(8, 8)),
        ]);
        await using var adapter = new WindowsDisplayCaptureAdapter(() => sessions.Dequeue(), 0.5);

        await adapter.StartAsync(CancellationToken.None);
        var first = await adapter.ReadFrameAsync(CancellationToken.None);
        Assert.Contains(first.Pixels, pixel => pixel.Red + pixel.Green + pixel.Blue > 0);

        await Assert.ThrowsAsync<AmbientCaptureException>(async () =>
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                await adapter.ReadFrameAsync(CancellationToken.None);
                await Task.Delay(20);
            }

            throw new TimeoutException("The capture worker failure did not surface.");
        }).WaitAsync(TimeSpan.FromSeconds(10));

        await adapter.StopAsync();
        await adapter.StartAsync(CancellationToken.None);
        var restarted = await adapter.ReadFrameAsync(CancellationToken.None);
        Assert.Contains(restarted.Pixels, pixel => pixel.Red + pixel.Green + pixel.Blue > 0);
        await adapter.StopAsync();
    }

    private static RawDisplayFrame CreateFrame(int width, int height)
    {
        var stride = width * 4;
        var bytes = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = y * stride + x * 4;
                bytes[offset] = (byte)((x * 17 + y * 3) % 256);
                bytes[offset + 1] = (byte)((x * 5 + y * 11) % 256);
                bytes[offset + 2] = (byte)((x * 7 + y * 13) % 256);
                bytes[offset + 3] = 255;
            }
        }
        return new RawDisplayFrame(bytes, width, height, stride);
    }

    private static RawDisplayFrame Crop(
        RawDisplayFrame frame,
        int x,
        int y,
        int width,
        int height)
    {
        var stride = width * 4;
        var bytes = new byte[stride * height];
        for (var row = 0; row < height; row++)
        {
            Buffer.BlockCopy(
                frame.Bgra,
                (y + row) * frame.Stride + x * 4,
                bytes,
                row * stride,
                stride);
        }
        return new RawDisplayFrame(bytes, width, height, stride);
    }

    private sealed class IdleSession : IDisplayCaptureSession
    {
        public ValueTask<RawDisplayFrame> ReadFrameAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    [Fact]
    public async Task CaptureWorkerSamplesOnlyTheConfiguredRegion()
    {
        var frame = CreateFrame(4, 4);
        await using var adapter = new WindowsDisplayCaptureAdapter(() => new OneFrameSession(frame), 0.5);
        adapter.SetCaptureRegion(new StudioScreenRect(0.25, 0.25, 0.5, 0.5));

        await adapter.StartAsync(CancellationToken.None);
        var actual = await adapter.ReadFrameAsync(CancellationToken.None);

        var expected = WindowsDisplayCaptureAdapter.ReduceEdgeBand(
            frame.Bgra, frame.Width, frame.Height, frame.Stride, 1, 1, 2, 2, 1);
        Assert.Equal(expected.Pixels, actual.Pixels);
    }

    private sealed class FaultingSession(RawDisplayFrame frame) : IDisplayCaptureSession
    {
        private int _reads;

        public ValueTask<RawDisplayFrame> ReadFrameAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _reads) == 1) return ValueTask.FromResult(frame);
            throw new AmbientCaptureException(
                AmbientCaptureFailure.CaptureFailed,
                "Synthetic capture failure.");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class OneFrameSession(RawDisplayFrame frame) : IDisplayCaptureSession
    {
        private int _read;

        public async ValueTask<RawDisplayFrame> ReadFrameAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _read, 1) == 0) return frame;
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new OperationCanceledException(cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

}
