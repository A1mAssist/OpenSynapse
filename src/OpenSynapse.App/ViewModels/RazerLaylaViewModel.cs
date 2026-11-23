using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Dispatching;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed record RazerLaylaActionOption(string Name, RazerLaylaBinding Binding);

public sealed class RazerLaylaMappingRow(RazerLaylaInput input, IReadOnlyList<RazerLaylaActionOption> options, RazerLaylaBinding binding)
    : INotifyPropertyChanged
{
    private RazerLaylaActionOption? _selected = options.FirstOrDefault(option => option.Binding == binding);
    public RazerLaylaInput Input { get; } = input;
    public string Name => AppStrings.Text("LaylaInput" + Input);
    public IReadOnlyList<RazerLaylaActionOption> Options { get; } = options;
    public RazerLaylaActionOption? Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class RazerLaylaViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly RazerBleDevice _device;
    private readonly Func<ProfileDocument> _getProfile;
    private readonly Func<CancellationToken, Task<bool>> _save;
    private readonly Action<string>? _diagnostic;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    private readonly RazerLaylaActionExecutor _executor;
    private RazerLaylaService? _service;
    private bool _busy;
    private string _status = string.Empty;
    private string _error = string.Empty;

    static RazerLaylaViewModel() => ProfileSettingCatalog.Register(new(
        RazerLaylaMappings.SettingKey, DeviceCategory.Accessory, PowerScoped: false));

    public RazerLaylaViewModel(RazerBleDevice device, Func<ProfileDocument> getProfile,
        Func<CancellationToken, Task<bool>> save, Action<string>? diagnostic)
    {
        _device = device; _getProfile = getProfile; _save = save; _diagnostic = diagnostic;
        _executor = new(message => _dispatcher.TryEnqueue(() => SetError(message)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<RazerLaylaMappingRow> Rows { get; } = [];
    public RazerLaylaState? State { get; private set; }
    public bool IsRunning => _service?.IsRunning == true;
    public bool CanEdit => !_busy;
    public bool HasError => _error.Length > 0;
    public string ErrorText => _error;
    public string StatusText => _status;

    public async Task LoadAsync(CancellationToken token = default)
    {
        if (_lifetime.IsCancellationRequested) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        try { await _gate.WaitAsync(linked.Token); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
        SetBusy(true);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            ApplyMappings();
            if (_service?.IsRunning != true)
            {
                if (_service is not null) await _service.DisposeAsync();
                _service = null;
                await OpenSessionAsync(linked.Token);
            }
            State = await _service!.ReadStateAsync(linked.Token);
            _status = AppStrings.Text("LaylaActive");
            SetError(string.Join(Environment.NewLine, State.Errors));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            if (_service is not null) { await _service.DisposeAsync(); _service = null; }
            _status = AppStrings.Text("LaylaStopped");
            SetError(exception.Message);
        }
        finally { SetBusy(false); _gate.Release(); }
    }

    private async Task OpenSessionAsync(CancellationToken token)
    {
        var service = await RazerLaylaService.OpenAsync(_device, _executor.ExecuteAsync, _diagnostic, token);
        _service = service;
        service.Failed += message => _dispatcher.TryEnqueue(() =>
        {
            if (!ReferenceEquals(_service, service)) return;
            if (!service.IsRunning) _status = AppStrings.Text("LaylaStopped");
            SetError(message);
        });
    }

    private void ApplyMappings()
    {
        var document = _getProfile();
        Dictionary<RazerLaylaInput, RazerLaylaBinding> bindings;
        try { bindings = RazerLaylaMappings.Read(document, _device); }
        catch
        {
            BuildRows(RazerLaylaMappings.Defaults(), RazerLaylaMappings.Macros(document));
            throw;
        }
        var macros = RazerLaylaMappings.Macros(document);
        BuildRows(bindings, macros);
        _executor.Update(bindings, macros);
    }

    private void BuildRows(IReadOnlyDictionary<RazerLaylaInput, RazerLaylaBinding> bindings,
        IReadOnlyList<OpenSynapse.Core.Macros.MacroDefinition> macros)
    {
        var options = new List<RazerLaylaActionOption>
        {
            new(AppStrings.Text("LaylaDisabled"), new(RazerLaylaAction.Disabled)),
            new(AppStrings.Text("MacroMediaMute"), new(RazerLaylaAction.Mute)),
            new(AppStrings.Text("MacroMediaPlayPause"), new(RazerLaylaAction.PlayPause)),
            new(AppStrings.Text("MacroMediaNext"), new(RazerLaylaAction.NextTrack)),
            new(AppStrings.Text("MacroMediaPrevious"), new(RazerLaylaAction.PreviousTrack)),
            new(AppStrings.Text("MacroMediaVolumeUp"), new(RazerLaylaAction.VolumeUp)),
            new(AppStrings.Text("MacroMediaVolumeDown"), new(RazerLaylaAction.VolumeDown)),
            new(AppStrings.Text("LaylaCyclePlayback"), new(RazerLaylaAction.CyclePlayback)),
        };
        options.AddRange(macros.Where(macro => macro.Enabled).Select(macro => new RazerLaylaActionOption(
            AppStrings.Text("RazerMappingMacroHeader") + ": " + macro.Name, new(RazerLaylaAction.Macro, macro.Id))));
        Rows.Clear();
        foreach (var input in Enum.GetValues<RazerLaylaInput>()) Rows.Add(new(input, options, bindings[input]));
    }

    [NotProfileScoped]
    public async Task ApplyProfileAsync(CancellationToken token = default)
    {
        if (_lifetime.IsCancellationRequested) return;
        InvalidateMacros();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        try { await _gate.WaitAsync(linked.Token); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            ApplyMappings();
            _status = IsRunning ? AppStrings.Text("LaylaActive") : string.Empty;
            SetError(string.Empty);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            // An invalid new profile must never leave the previous profile's actions active.
            if (_service is not null) { await _service.DisposeAsync(); _service = null; }
            _status = AppStrings.Text("LaylaStopped");
            SetError(exception.Message);
        }
        finally { _gate.Release(); }
    }

    [ProfileSetting(RazerLaylaMappings.SettingKey, DeviceCategory.Accessory)]
    public async Task SaveAsync(CancellationToken token = default)
    {
        if (_lifetime.IsCancellationRequested) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        try { await _gate.WaitAsync(linked.Token); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
        SetBusy(true);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            var bindings = Rows.ToDictionary(row => row.Input, row => row.Selected?.Binding
                ?? throw new InvalidDataException(AppStrings.Text("LaylaSelectAction")));
            if (!await RazerLaylaMappings.SaveAsync(_getProfile(), _device, bindings, _save, linked.Token))
                throw new IOException(AppStrings.Text("LaylaSaveFailed"));
            ApplyMappings();
            _status = AppStrings.Text("BlackSharkSaved");
            SetError(string.Empty);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception exception) { SetError(exception.Message); }
        finally { SetBusy(false); _gate.Release(); }
    }

    private void SetBusy(bool value) { _busy = value; PropertyChanged?.Invoke(this, new(string.Empty)); }
    private void SetError(string value) { _error = value; PropertyChanged?.Invoke(this, new(string.Empty)); }

    internal void InvalidateMacros() => _executor.InvalidateMacros();

    internal void CancelOperations()
    {
        _lifetime.Cancel();
        InvalidateMacros();
    }

    public async ValueTask DisposeAsync()
    {
        CancelOperations();
        await _gate.WaitAsync();
        try
        {
            if (_disposed) return;
            _disposed = true;
            if (_service is not null) { await _service.DisposeAsync(); _service = null; }
            await _executor.DisposeAsync();
        }
        finally { _gate.Release(); }
    }
}
