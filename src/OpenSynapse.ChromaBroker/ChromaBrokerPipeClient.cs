// Chroma shared-memory protocol reference:
// worflor/neuron crates/neuron-host/src/adapters/chroma_shm.rs (GPL-3.0-or-later).
// This C# port is part of OpenSynapse and distributed under AGPL-3.0.
using System.IO.Pipes;
using OpenSynapse.ChromaSdk.Protocol;

namespace OpenSynapse.ChromaBroker;

internal sealed class ChromaBrokerPipeClient : IDisposable
{
    internal const string DefaultPipeName = "OpenSynapseChromaBroker";
    private const int RetryMilliseconds = 1_000;

    private readonly string _name;
    private readonly object _gate = new();
    private NamedPipeClientStream? _pipe;
    private long _nextAttempt;
    private bool _unavailableLogged;
    private int _disposed;

    public ChromaBrokerPipeClient(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
    }

    public bool TryConnect()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return false;
        }

        lock (_gate)
        {
            if (_pipe is not null)
            {
                return false;
            }

            var now = Environment.TickCount64;
            if (now < _nextAttempt)
            {
                return false;
            }
            _nextAttempt = now + RetryMilliseconds;

            NamedPipeClientStream? pipe = null;
            try
            {
                pipe = new NamedPipeClientStream(".", _name, PipeDirection.Out, PipeOptions.None);
                pipe.Connect(100);
                _pipe = pipe;
                _unavailableLogged = false;
                BrokerLog.Info($"pipe connected: {_name}");
                return true;
            }
            catch (Exception exception) when (
                exception is IOException or TimeoutException or UnauthorizedAccessException or InvalidOperationException)
            {
                pipe?.Dispose();
                if (!_unavailableLogged)
                {
                    BrokerLog.Info($"pipe {_name} unavailable; retrying every second.");
                    _unavailableLogged = true;
                }
                return false;
            }
        }
    }

    public bool TrySend(ChromaSdkMessage message)
    {
        lock (_gate)
        {
            if (_pipe is null)
            {
                return false;
            }

            try
            {
                ChromaSdkPipe.Write(_pipe, message);
                return true;
            }
            catch (Exception exception) when (
                exception is IOException or InvalidDataException or ObjectDisposedException or UnauthorizedAccessException)
            {
                BrokerLog.Error($"pipe write failed: {exception.Message}");
                CloseLocked();
                return false;
            }
        }
    }

    public void SendByeAndDisconnect()
    {
        lock (_gate)
        {
            if (_pipe is null)
            {
                return;
            }

            try
            {
                ChromaSdkPipe.Write(_pipe, new ChromaSdkByeMessage());
                BrokerLog.Info("sent Bye for the lost Chroma client.");
            }
            catch (Exception exception) when (
                exception is IOException or InvalidDataException or ObjectDisposedException or UnauthorizedAccessException)
            {
                BrokerLog.Error($"pipe Bye failed: {exception.Message}");
            }
            finally
            {
                CloseLocked();
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        lock (_gate)
        {
            if (_pipe is not null)
            {
                try
                {
                    ChromaSdkPipe.Write(_pipe, new ChromaSdkByeMessage());
                }
                catch (Exception exception) when (
                    exception is IOException or InvalidDataException or ObjectDisposedException or UnauthorizedAccessException)
                {
                }
                CloseLocked();
            }
        }
    }

    private void CloseLocked()
    {
        try
        {
            _pipe?.Dispose();
        }
        catch (IOException)
        {
        }
        _pipe = null;
        _nextAttempt = Environment.TickCount64 + RetryMilliseconds;
        BrokerLog.Info("pipe disconnected.");
    }
}
