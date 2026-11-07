// Chroma shared-memory object creation, activation and decoding reference:
// worflor/neuron crates/neuron-host/src/adapters/chroma_shm.rs (GPL-3.0-or-later).
// This C# port is part of OpenSynapse and distributed under AGPL-3.0.
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenSynapse.ChromaSdk.Protocol;
using static OpenSynapse.ChromaBroker.ChromaNativeMethods;

namespace OpenSynapse.ChromaBroker;

internal sealed class ChromaAlreadyServingException : Exception
{
}

internal sealed class ChromaSharedMemoryServer : IDisposable
{
    private const uint PageReadWrite = 0x04;
    private const uint FileMapAllAccess = 0xF001F;
    private const uint EventAccess = 0x00100002; // EVENT_MODIFY_STATE | SYNCHRONIZE
    private const uint Synchronize = 0x00100000;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint StillActive = 259;
    private const int ErrorAlreadyExists = 183;
    private const string ChromaClientDll = "rzchromasdk64.dll";
    private static readonly IntPtr InvalidHandleValue = new(-1);

    private readonly EveryoneSecurity _security;
    private readonly List<MappedSection> _sections = [];
    private readonly List<IntPtr> _ownedHandles = [];
    private readonly List<IntPtr> _clientHandles = [];
    private readonly List<IntPtr> _maskHandles = [];
    private readonly Dictionary<ChromaBrokerDeviceClass, MappedSection> _deviceSections = [];
    private readonly Dictionary<ChromaBrokerDeviceClass, uint> _lastHeads = [];
    private readonly Dictionary<ChromaBrokerDeviceClass, ChromaDeviceFrame> _frames = [];
    private MappedSection? _appRegistry;
    private MappedSection? _sessionInfo;
    private uint? _clientPid;
    private uint _pendingClientPid;
    private uint _loggedStalePid;
    private long _nextClientCheck;
    private long _nextClientScan;
    private int _disposed;

    private ChromaSharedMemoryServer(EveryoneSecurity security)
    {
        _security = security;
    }

    public bool HasClient => _clientPid is not null;

    internal void NoteRegisteredClient(uint pid) => Volatile.Write(ref _pendingClientPid, pid);

    public static ChromaSharedMemoryServer Create()
    {
        if (MaskWorn())
        {
            throw new ChromaAlreadyServingException();
        }

        var security = EveryoneSecurity.Create();
        var server = new ChromaSharedMemoryServer(security);
        try
        {
            foreach (var definition in ChromaObjectCatalog.Objects.Where(
                         item => item.Origin == ChromaObjectOrigin.ServerCreated))
            {
                server.CreateServerObject(definition);
            }

            foreach (var definition in ChromaObjectCatalog.Objects.Where(
                         item => item.Origin == ChromaObjectOrigin.ClientCreated))
            {
                server.OpenClientObject(definition);
            }

            server.CreateMask();
            server.ResolveSections();
            BrokerLog.Info(
                $"Chroma objects ready: {server._sections.Count} sections, " +
                $"{server._ownedHandles.Count} server handles, {server._maskHandles.Count} mask handles.");
            return server;
        }
        catch
        {
            server.Dispose();
            throw;
        }
    }

    public bool UpdateClient(long now)
    {
        var lost = false;
        if (_clientPid is null)
        {
            var pending = Interlocked.Exchange(ref _pendingClientPid, 0);
            if (pending != 0 && pending != (uint)Environment.ProcessId && IsProcessAlive(pending))
            {
                ResetFrameTracking();
                ActivateClient(pending);
                _clientPid = pending;
                _nextClientCheck = now + 1_000;
                BrokerLog.Info($"app pipe registered Chroma client PID {pending}.");
                return lost;
            }
        }

        if (_clientPid is { } activePid)
        {
            if (now >= _nextClientCheck)
            {
                _nextClientCheck = now + 1_000;
                if (!IsProcessAlive(activePid))
                {
                    BrokerLog.Info($"Chroma client {activePid} exited.");
                    _clientPid = null;
                    _frames.Clear();
                    _lastHeads.Clear();
                    _nextClientScan = 0;
                    lost = true;
                }
            }

            if (_clientPid is not null)
            {
                return lost;
            }
        }

        if (now < _nextClientScan)
        {
            return lost;
        }
        _nextClientScan = now + 2_000;

        var pid = FindClientPid();
        if (pid == 0)
        {
            return lost;
        }

        ResetFrameTracking();
        ActivateClient(pid);
        _clientPid = pid;
        _nextClientCheck = now + 1_000;
        return lost;
    }

    public IReadOnlyList<ChromaSdkEffect> PollFrames(ulong nowMs)
    {
        if (_clientPid is null)
        {
            return Array.Empty<ChromaSdkEffect>();
        }

        var effects = new List<ChromaSdkEffect>();
        foreach (var device in ChromaBrokerDeviceClasses.All)
        {
            var section = _deviceSections[device];
            var head = unchecked((uint)Marshal.ReadInt32(section.View));
            var changed = !_lastHeads.TryGetValue(device, out var previous) || previous != head;
            if (changed)
            {
                _lastHeads[device] = head;
                var frame = ChromaFrameDecoder.Decode(Snapshot(section), device);
                if (frame is not null)
                {
                    _frames[device] = frame;
                }
            }

            if (_frames.TryGetValue(device, out var cached) &&
                (changed || ChromaFrameDecoder.IsAnimated(cached.Effect)))
            {
                effects.Add(ChromaFrameDecoder.ToEffect(cached, nowMs));
            }
        }
        return effects;
    }

    public IReadOnlyList<ChromaSdkEffect> GetLatestEffects(ulong nowMs)
    {
        if (_clientPid is null)
        {
            return Array.Empty<ChromaSdkEffect>();
        }

        var effects = new List<ChromaSdkEffect>();
        foreach (var device in ChromaBrokerDeviceClasses.All)
        {
            if (_frames.TryGetValue(device, out var frame))
            {
                effects.Add(ChromaFrameDecoder.ToEffect(frame, nowMs));
            }
        }
        return effects;
    }

    public static bool IsProcessAlive(uint pid)
    {
        if (pid == 0)
        {
            return false;
        }

        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return GetExitCodeProcess(handle, out var exitCode) && exitCode == StillActive;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _frames.Clear();
        _lastHeads.Clear();
        CloseHandles(_maskHandles);
        CloseHandles(_clientHandles);
        CloseHandles(_ownedHandles);
        foreach (var section in _sections)
        {
            section.Dispose();
        }
        _sections.Clear();
        _security.Dispose();
    }

    private static bool MaskWorn()
    {
        var handle = OpenMutexW(Synchronize, false, ChromaObjectCatalog.Name(ChromaObjectCatalog.MaskMutexes[0]));
        if (handle == IntPtr.Zero)
        {
            return false;
        }
        CloseHandle(handle);
        return true;
    }

    private static void CloseHandles(List<IntPtr> handles)
    {
        foreach (var handle in handles)
        {
            if (handle != IntPtr.Zero)
            {
                CloseHandle(handle);
            }
        }
        handles.Clear();
    }

    private void CreateServerObject(ChromaObjectDefinition definition)
    {
        var name = ChromaObjectCatalog.Name(definition.Guid);
        switch (definition.Kind)
        {
            case ChromaObjectKind.Section:
            {
                var mapping = CreateFileMappingW(
                    InvalidHandleValue,
                    ref _security.Attributes,
                    PageReadWrite,
                    0,
                    (uint)definition.Size,
                    name);
                if (mapping == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), $"CreateFileMappingW({name})");
                }

                var view = MapViewOfFile(mapping, FileMapAllAccess, 0, 0, (UIntPtr)(ulong)definition.Size);
                if (view == IntPtr.Zero)
                {
                    var error = Marshal.GetLastPInvokeError();
                    CloseHandle(mapping);
                    throw new Win32Exception(error, $"MapViewOfFile({name})");
                }

                _sections.Add(new MappedSection(definition.Guid, definition.Size, mapping, view));
                BrokerLog.Info($"created section {definition.Guid} ({definition.Size} bytes)");
                break;
            }
            case ChromaObjectKind.Event:
            {
                var handle = CreateEventW(ref _security.Attributes, true, false, name);
                if (handle == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), $"CreateEventW({name})");
                }
                _ownedHandles.Add(handle);
                BrokerLog.Info($"created event {definition.Guid}");
                break;
            }
            case ChromaObjectKind.Mutex:
            {
                var handle = CreateMutexW(ref _security.Attributes, false, name);
                if (handle == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), $"CreateMutexW({name})");
                }
                _ownedHandles.Add(handle);
                BrokerLog.Info($"created mutex {definition.Guid}");
                break;
            }
        }
    }

    private void OpenClientObject(ChromaObjectDefinition definition)
    {
        var name = ChromaObjectCatalog.Name(definition.Guid);
        var handle = definition.Kind switch
        {
            ChromaObjectKind.Event => OpenEventW(EventAccess, false, name),
            ChromaObjectKind.Mutex => OpenMutexW(Synchronize, false, name),
            _ => IntPtr.Zero,
        };
        if (handle == IntPtr.Zero)
        {
            BrokerLog.Info(
                $"client object unavailable {definition.Guid} (error {Marshal.GetLastPInvokeError()})");
            return;
        }

        _clientHandles.Add(handle);
        BrokerLog.Info($"opened client object {definition.Guid}");
    }

    private void CreateMask()
    {
        var userName = Environment.GetEnvironmentVariable("USERNAME") ?? Environment.UserName;
        var mutexNames = ChromaObjectCatalog.MaskMutexes
            .Select(ChromaObjectCatalog.Name)
            .Append(ChromaObjectCatalog.PerUserMaskName(userName))
            .ToArray();

        for (var index = 0; index < mutexNames.Length; index++)
        {
            Marshal.SetLastPInvokeError(0);
            var handle = CreateMutexW(ref _security.Attributes, false, mutexNames[index]);
            if (handle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), $"CreateMutexW({mutexNames[index]})");
            }

            if (index == 0 && Marshal.GetLastPInvokeError() == ErrorAlreadyExists)
            {
                CloseHandle(handle);
                throw new ChromaAlreadyServingException();
            }

            _maskHandles.Add(handle);
            BrokerLog.Info($"held mask mutex {mutexNames[index]}");
        }

        foreach (var guid in ChromaObjectCatalog.MaskEvents)
        {
            var name = ChromaObjectCatalog.Name(guid);
            var handle = CreateEventW(ref _security.Attributes, true, true, name);
            if (handle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), $"CreateEventW({name})");
            }
            _maskHandles.Add(handle);
            BrokerLog.Info($"held mask event {guid}");
        }
    }

    private void ResolveSections()
    {
        _appRegistry = FindSection(ChromaObjectCatalog.AppRegistry) ??
            throw new InvalidOperationException("The app-registry section is missing.");
        _sessionInfo = FindSection(ChromaObjectCatalog.SessionInfo) ??
            throw new InvalidOperationException("The SessionInfo section is missing.");

        foreach (var device in ChromaBrokerDeviceClasses.All)
        {
            _deviceSections[device] = FindSection(device.SectionGuid()) ??
                throw new InvalidOperationException($"The {device} section is missing.");
        }
    }

    private MappedSection? FindSection(string guid) =>
        _sections.FirstOrDefault(section => section.Guid == guid);

    private uint FindClientPid()
    {
        if (Volatile.Read(ref _pendingClientPid) != 0)
        {
            return 0;
        }

        var registered = ReadRegisteredPid();
        if (registered == 0)
        {
            _loggedStalePid = 0;
        }
        if (registered != 0 && registered != (uint)Environment.ProcessId && IsProcessAlive(registered))
        {
            _loggedStalePid = 0;
            BrokerLog.Info($"found registered Chroma client PID {registered}.");
            return registered;
        }
        if (registered != 0 && registered != _loggedStalePid)
        {
            _loggedStalePid = registered;
            BrokerLog.Info($"registered Chroma client PID {registered} is stale; scanning modules.");
        }

        var scanned = FindProcessWithChromaModule();
        if (scanned != 0 && scanned != (uint)Environment.ProcessId && IsProcessAlive(scanned))
        {
            _loggedStalePid = 0;
            BrokerLog.Info($"found Chroma client PID {scanned} by module scan.");
            return scanned;
        }
        return 0;
    }

    private uint ReadRegisteredPid()
    {
        if (_appRegistry is null || _appRegistry.Size < 0x200 + 0x10)
        {
            return 0;
        }
        if (unchecked((uint)Marshal.ReadInt32(_appRegistry.View)) == 0)
        {
            return 0;
        }
        return unchecked((uint)Marshal.ReadInt32(_appRegistry.View, 0x200 + 0x0C));
    }

    private uint FindProcessWithChromaModule()
    {
        foreach (var process in Process.GetProcesses())
        {
            if (Volatile.Read(ref _pendingClientPid) != 0)
            {
                return 0;
            }

            using (process)
            {
                if (process.Id <= 4 || process.Id == Environment.ProcessId)
                {
                    continue;
                }
                if (HasChromaModule(process.Id))
                {
                    return (uint)process.Id;
                }
            }
        }
        return 0;
    }

    private static bool HasChromaModule(int processId)
    {
        var snapshot = CreateToolhelp32Snapshot(
            SnapshotModules | SnapshotModules32,
            unchecked((uint)processId));
        if (snapshot == InvalidHandleValue || snapshot == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var entry = new ModuleEntry32
            {
                Size = (uint)Marshal.SizeOf<ModuleEntry32>(),
            };
            if (!Module32FirstW(snapshot, ref entry))
            {
                return false;
            }
            do
            {
                if (string.Equals(entry.ModuleName, ChromaClientDll, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            while (Module32NextW(snapshot, ref entry));
            return false;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    private void ResetFrameTracking()
    {
        _frames.Clear();
        _lastHeads.Clear();
        foreach (var device in ChromaBrokerDeviceClasses.All)
        {
            _lastHeads[device] = unchecked((uint)Marshal.ReadInt32(_deviceSections[device].View));
        }
    }

    private void ActivateClient(uint pid)
    {
        var sessionId = GetSessionId(pid);
        WriteGrant(pid, sessionId);
        var sessionWorkerSignalled = SignalEvent(ChromaObjectCatalog.SessionWorkerEvent);
        Thread.Sleep(60);
        var onlineSignalled = SignalEvent(ChromaObjectCatalog.ServerOnlineEvent);
        BrokerLog.Info(
            $"granted Chroma client PID {pid}, session {sessionId}; " +
            $"worker={sessionWorkerSignalled}, online={onlineSignalled}.");
    }

    private void WriteGrant(uint pid, uint sessionId)
    {
        if (_appRegistry is null || _appRegistry.Size < 0x200 + 0x10 || _sessionInfo is null ||
            _sessionInfo.Size < 12)
        {
            throw new InvalidOperationException("Chroma grant sections are not mapped.");
        }

        Marshal.WriteInt32(_appRegistry.View, 0, 1);
        Marshal.WriteInt32(_appRegistry.View, 0x200 + 0x0C, unchecked((int)pid));
        Marshal.WriteInt32(_sessionInfo.View, 0, 0);
        Marshal.WriteInt32(_sessionInfo.View, 4, 8);
        Marshal.WriteInt32(_sessionInfo.View, 8, unchecked((int)sessionId));
    }

    private static uint GetSessionId(uint pid)
    {
        return ProcessIdToSessionId(pid, out var sessionId) ? sessionId : 1;
    }

    private static bool SignalEvent(string guid)
    {
        var name = ChromaObjectCatalog.Name(guid);
        var handle = OpenEventW(EventAccess, false, name);
        if (handle == IntPtr.Zero)
        {
            BrokerLog.Error($"open event {guid} failed: {Marshal.GetLastPInvokeError()}");
            return false;
        }

        try
        {
            var signalled = SetEvent(handle);
            if (!signalled)
            {
                BrokerLog.Error($"SetEvent {guid} failed: {Marshal.GetLastPInvokeError()}");
            }
            return signalled;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static byte[] Snapshot(MappedSection section)
    {
        var bytes = new byte[section.Size];
        Marshal.Copy(section.View, bytes, 0, bytes.Length);
        return bytes;
    }

}
