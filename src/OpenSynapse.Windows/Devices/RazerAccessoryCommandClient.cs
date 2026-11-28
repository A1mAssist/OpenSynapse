using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed class RazerAccessoryCommandClient : IAsyncDisposable
{
    private readonly Func<byte[], Func<byte[], bool>, bool, CancellationToken, Task<byte[]>> _exchange;
    private readonly RazerAccessoryConnection _connection;
    private readonly Action<string>? _diagnostic;
    private int _transactionId = 0x1F;

    internal RazerAccessoryCommandClient(
        RazerAccessoryConnection connection,
        Action<string>? diagnostic = null,
        Func<byte[], Func<byte[], bool>, bool, CancellationToken, Task<byte[]>>? exchange = null)
    {
        _connection = connection;
        _diagnostic = diagnostic;
        _exchange = exchange ?? new RazerAccessoryHidTransport(connection.DevicePath).QueryAsync;
    }

    internal ushort ProductId => _connection.ProductId;
    internal int ReportLength => _connection.ReportLength;
    internal string DevicePath => _connection.DevicePath;

    internal Task<byte[]> QueryAsync(
        byte commandClass,
        byte commandId,
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken = default) =>
        QueryAsync(
            (byte)arguments.Length,
            commandClass,
            commandId,
            arguments,
            response => RazerAccessoryProtocol.MatchesCommand(response, commandClass, commandId),
            validateStatus: true,
            cancellationToken);

    internal Task<byte[]> QueryAsync(
        byte dataSize,
        byte commandClass,
        byte commandId,
        ReadOnlyMemory<byte> arguments,
        Func<byte[], bool> matcher,
        CancellationToken cancellationToken = default) =>
        QueryAsync(dataSize, commandClass, commandId, arguments, matcher, validateStatus: true, cancellationToken);

    internal Task<byte[]> QueryRawAsync(
        byte[] request,
        Func<byte[], bool> matcher,
        CancellationToken cancellationToken = default) =>
        QueryRawAsync(request, matcher, validateStatus: true, cancellationToken);

    internal async Task<byte[]> QueryRawAsync(
        byte[] request,
        Func<byte[], bool> matcher,
        bool validateStatus,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(matcher);
        _diagnostic?.Invoke(
            $"accessory {_connection.ProductId:X4} raw command " +
            $"{request[7]:X2}:{request[8]:X2} size={request[6]} report={request.Length}");
        try
        {
            return await _exchange(request, matcher, validateStatus, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            throw new InvalidOperationException(
                $"Accessory {_connection.ProductId:X4} command {request[7]:X2}:{request[8]:X2} " +
                $"failed: {exception.Message}",
                exception);
        }
    }

    internal async Task<byte[]> QueryAsync(
        byte dataSize,
        byte commandClass,
        byte commandId,
        ReadOnlyMemory<byte> arguments,
        Func<byte[], bool> matcher,
        bool validateStatus,
        CancellationToken cancellationToken)
    {
        var transaction = unchecked((byte)(Interlocked.Increment(ref _transactionId) & 0x1F));
        var request = RazerAccessoryProtocol.CreateRequest(
            _connection.ReportLength,
            _connection.ReportId,
            transaction,
            dataSize,
            commandClass,
            commandId,
            arguments.Span);
        _diagnostic?.Invoke(
            $"accessory {_connection.ProductId:X4} command {commandClass:X2}:{commandId:X2} " +
            $"size={dataSize} report={_connection.ReportLength}");
        try
        {
            return await _exchange(request, matcher, validateStatus, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            throw new InvalidOperationException(
                $"Accessory {_connection.ProductId:X4} command {commandClass:X2}:{commandId:X2} " +
                $"failed: {exception.Message}",
                exception);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or TimeoutException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or ArgumentException;
}
