using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using global::Windows.Graphics.Imaging;

namespace OpenSynapse.Core.Tests;

public sealed class WindowsDisplayCaptureAdapterTests
{
    [Fact]
    public void CopiesPixelsFromARealSoftwareBitmap()
    {
        using var bitmap = new SoftwareBitmap(
            BitmapPixelFormat.Bgra8,
            2,
            1,
            BitmapAlphaMode.Ignore);

        var frame = WindowsDisplayCaptureAdapter.CopyPixels(bitmap, 2, 1);

        Assert.Equal(2, frame.Width);
        Assert.Equal(1, frame.Height);
        Assert.True(frame.Stride >= 8);
        Assert.Equal(frame.Stride, frame.Bgra.Length);
    }

    [Fact]
    public void ReducesOnlyEdgeBandAndHonorsStride()
    {
        var source = new byte[20 * 3];
        SetPixel(source, 20, 0, 0, new RazerRgb(255, 0, 0));
        SetPixel(source, 20, 1, 0, new RazerRgb(255, 0, 0));
        SetPixel(source, 20, 3, 0, new RazerRgb(0, 255, 0));
        SetPixel(source, 20, 2, 0, new RazerRgb(0, 255, 0));
        SetPixel(source, 20, 0, 2, new RazerRgb(0, 0, 255));
        SetPixel(source, 20, 1, 2, new RazerRgb(0, 0, 255));
        SetPixel(source, 20, 3, 2, new RazerRgb(255, 255, 255));
        SetPixel(source, 20, 2, 2, new RazerRgb(255, 255, 255));

        var frame = WindowsDisplayCaptureAdapter.ReduceEdgeBand(source, 4, 3, 20, 1, 2, 2);

        Assert.Equal(4, frame.Pixels.Count);
        Assert.Equal(new RazerRgb(255, 0, 0), frame.Pixels[0]);
        Assert.Equal(new RazerRgb(0, 255, 0), frame.Pixels[1]);
        Assert.Equal(new RazerRgb(0, 0, 255), frame.Pixels[2]);
        Assert.Equal(new RazerRgb(255, 255, 255), frame.Pixels[3]);
    }

    [Fact]
    public void ExpandsOnePixelCaptureToEveryLogicalCell()
    {
        var frame = WindowsDisplayCaptureAdapter.ReduceEdgeBand(
            [12, 34, 56, 0],
            1,
            1,
            4,
            1,
            outputWidth: 3,
            outputHeight: 2);

        Assert.All(frame.Pixels, pixel => Assert.Equal(new RazerRgb(56, 34, 12), pixel));
    }

    [Fact]
    public async Task DropsStaleFramesAndDisposesSessionBeforeStopReturns()
    {
        var session = new FakeSession(
            new[]
            {
                Raw(new RazerRgb(255, 0, 0)),
                Raw(new RazerRgb(0, 0, 255)),
            });
        var adapter = new WindowsDisplayCaptureAdapter(() => session, 0.1);

        await adapter.StartAsync(CancellationToken.None);
        await EventuallyAsync(() => session.FramesRead >= 2);
        var latest = await adapter.ReadFrameAsync(CancellationToken.None);
        Assert.Equal(new RazerRgb(0, 0, 255), latest.Pixels[0]);

        await adapter.StopAsync();
        Assert.True(session.Disposed);
        await adapter.DisposeAsync();
    }

    [Fact]
    public async Task PermissionFailureIsTypedAndDoesNotStartWorker()
    {
        var expected = new AmbientCaptureException(
            AmbientCaptureFailure.PermissionDenied,
            "permission");
        var adapter = new WindowsDisplayCaptureAdapter(() => throw expected);

        var actual = await Assert.ThrowsAsync<AmbientCaptureException>(
            () => adapter.StartAsync(CancellationToken.None).AsTask());

        Assert.Equal(AmbientCaptureFailure.PermissionDenied, actual.Failure);
        await adapter.DisposeAsync();
    }

    [Fact]
    public async Task PropagatesCaptureFailureAndCleansWorker()
    {
        var expected = new AmbientCaptureException(
            AmbientCaptureFailure.CaptureFailed,
            "capture failed");
        var session = new FaultingSession(expected);
        var adapter = new WindowsDisplayCaptureAdapter(() => session);

        await adapter.StartAsync(CancellationToken.None);
        var readFailure = await Assert.ThrowsAsync<AmbientCaptureException>(
            () => adapter.ReadFrameAsync(CancellationToken.None).AsTask());
        Assert.Same(expected, readFailure);
        await Assert.ThrowsAsync<AmbientCaptureException>(() => adapter.StopAsync().AsTask());

        await adapter.DisposeAsync();
        Assert.True(session.Disposed);
    }

    private static RawDisplayFrame Raw(RazerRgb color)
    {
        var bytes = new byte[4];
        bytes[0] = color.Blue;
        bytes[1] = color.Green;
        bytes[2] = color.Red;
        return new RawDisplayFrame(bytes, 1, 1, 4);
    }

    private static void SetPixel(byte[] buffer, int stride, int x, int y, RazerRgb color)
    {
        var offset = y * stride + x * 4;
        buffer[offset] = color.Blue;
        buffer[offset + 1] = color.Green;
        buffer[offset + 2] = color.Red;
    }

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (condition())
            {
                return;
            }
            await Task.Delay(5);
        }
        Assert.Fail("Ambient capture did not produce the expected frame.");
    }

    private sealed class FakeSession(IEnumerable<RawDisplayFrame> frames) : IDisplayCaptureSession
    {
        private readonly Queue<RawDisplayFrame> _frames = new(frames);
        private readonly SemaphoreSlim _wake = new(0);
        public int FramesRead { get; private set; }
        public bool Disposed { get; private set; }

        public async ValueTask<RawDisplayFrame> ReadFrameAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                lock (_frames)
                {
                    if (_frames.TryDequeue(out var frame))
                    {
                        FramesRead++;
                        return frame;
                    }
                }
                await _wake.WaitAsync(cancellationToken);
            }
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            _wake.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FaultingSession(Exception failure) : IDisplayCaptureSession
    {
        public bool Disposed { get; private set; }

        public ValueTask<RawDisplayFrame> ReadFrameAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<RawDisplayFrame>(failure);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
