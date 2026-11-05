using OpenSynapse.Windows.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerDkmTableReaderTests
{
    [Fact]
    public void ReadsExactly256Bytes()
    {
        var control = new FakeControl();
        var table = new byte[RazerDkmTableReader.TableLength];

        Assert.True(RazerDkmTableReader.TryRead(control, table, out var error));

        Assert.Null(error);
        Assert.Equal(0x100, table.Length);
        Assert.Equal((byte)0xFF, table[^1]);
        Assert.Same(table, control.LastBuffer);
    }

    [Fact]
    public void RejectsBufferThatIsNotExactly256Bytes()
    {
        var control = new FakeControl();
        var table = new byte[0xFF];

        Assert.False(RazerDkmTableReader.TryRead(control, table, out var error));

        Assert.False(control.Called);
        Assert.Contains("256", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertsControlFailureToFalse()
    {
        var control = new FakeControl { Result = false, Failure = "driver refused." };
        var table = new byte[RazerDkmTableReader.TableLength];

        Assert.False(RazerDkmTableReader.TryRead(control, table, out var error));

        Assert.Equal("driver refused.", error);
    }

    [Fact]
    public void ConvertsControlExceptionToFalse()
    {
        var control = new FakeControl { Thrown = new IOException("device disappeared.") };
        var table = new byte[RazerDkmTableReader.TableLength];

        Assert.False(RazerDkmTableReader.TryRead(control, table, out var error));

        Assert.Contains("device disappeared.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void UsesTheDocumentedControlCodeAndExactTableLength()
    {
        Assert.Equal(0x88883108u, RazerDkmTableReader.GetDkmTableControlCode);
        Assert.Equal(0x100, RazerDkmTableReader.TableLength);
    }

    private sealed class FakeControl : IRazerDkmControl
    {
        internal bool Result { get; init; } = true;

        internal string? Failure { get; init; }

        internal Exception? Thrown { get; init; }

        internal byte[]? LastBuffer { get; private set; }

        internal bool Called => LastBuffer is not null;

        public bool TryRead(byte[] buffer, out string? error)
        {
            LastBuffer = buffer;
            if (Thrown is not null)
            {
                throw Thrown;
            }

            if (!Result)
            {
                error = Failure;
                return false;
            }

            for (var index = 0; index < buffer.Length; index++)
            {
                buffer[index] = (byte)index;
            }

            error = null;
            return true;
        }
    }
}
