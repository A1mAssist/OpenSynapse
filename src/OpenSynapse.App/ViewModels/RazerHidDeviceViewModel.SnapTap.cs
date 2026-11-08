using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private readonly ObservableCollection<AnalogSnapTapPairViewModel> _analogSnapTapPairs = [];
    private readonly List<AnalogKeyKeyViewModel> _analogSnapTapKeyChoices = [];
    private bool _analogSnapTapAvailable;
    private bool _analogSnapTapEnabled;
    private string _analogSnapTapStatusText = "\u6b63\u5728\u8bfb\u53d6 Snap Tap \u8bbe\u7f6e\u2026";

    public IReadOnlyList<AnalogSnapTapPairViewModel> AnalogSnapTapPairs => _analogSnapTapPairs;
    public Visibility AnalogSnapTapVisibility => VisibleWhen(_analogSnapTapAvailable && _analogKeyLayout is not null);
    public bool CanApplyAnalogSnapTap => CanEditAnalogKey && _analogSnapTapAvailable;
    public bool AnalogSnapTapEnabled
    {
        get => _analogSnapTapEnabled;
        set => SetField(ref _analogSnapTapEnabled, value);
    }

    public string AnalogSnapTapStatusText
    {
        get => _analogSnapTapStatusText;
        private set => SetField(ref _analogSnapTapStatusText, value);
    }

    public void AddAnalogSnapTapPair()
    {
        if (!_analogSnapTapAvailable ||
            _analogSnapTapPairs.Count >= RazerAnalogKeyProtocol.MaxSnapTapPairs)
        {
            return;
        }

        _analogSnapTapPairs.Add(new AnalogSnapTapPairViewModel(_analogSnapTapKeyChoices));
    }

    public void RemoveAnalogSnapTapPair(AnalogSnapTapPairViewModel pair)
    {
        if (pair is not null)
        {
            _analogSnapTapPairs.Remove(pair);
        }
    }

    [ProfileSetting("keyboard.snap-tap", DeviceCategory.Keyboard, powerScoped: true)]
    public Task ApplyAnalogSnapTapAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.AnalogKeyWrite,
            () => IsAnalogKeyBusy, value => IsAnalogKeyBusy = value,
            async () =>
            {
                var previousEnabled = AnalogSnapTapEnabled;
                var previousProfile = ReadProfile(settings => settings.Keyboard.SnapTapEnabled);
                var previousPairs = _analogSnapTapPairs
                    .Where(pair => pair.Key1 is not null && pair.Key2 is not null)
                    .Select(pair => new RazerAnalogSnapTapPair(
                        pair.Key1!.KeyId, pair.Key2!.KeyId, pair.Mode))
                    .ToArray();
                var pairs = new List<RazerAnalogSnapTapPair>(_analogSnapTapPairs.Count);
                var used = new HashSet<byte>();
                foreach (var pair in _analogSnapTapPairs)
                {
                    if (pair.Key1 is null && pair.Key2 is null)
                    {
                        continue;
                    }

                    if (pair.Key1 is null || pair.Key2 is null)
                    {
                        AnalogSnapTapStatusText = "\u6bcf\u4e00\u7ec4\u90fd\u8981\u540c\u65f6\u9009\u4e24\u4e2a\u952e\u3002";
                        return;
                    }

                    if (pair.Key1.KeyId == pair.Key2.KeyId)
                    {
                        AnalogSnapTapStatusText = "\u540c\u4e00\u7ec4\u4e0d\u80fd\u9009\u4e24\u6b21\u540c\u4e00\u4e2a\u952e\u3002";
                        return;
                    }

                    if (!used.Add(pair.Key1.KeyId) || !used.Add(pair.Key2.KeyId))
                    {
                        AnalogSnapTapStatusText = "\u4e0d\u80fd\u8ba9\u4e0d\u540c\u7ec4\u91cd\u590d\u4f7f\u7528\u540c\u4e00\u4e2a\u952e\u3002";
                        return;
                    }

                    pairs.Add(new RazerAnalogSnapTapPair(pair.Key1.KeyId, pair.Key2.KeyId, pair.Mode));
                }

                if (AnalogSnapTapEnabled && pairs.Count == 0)
                {
                    AnalogSnapTapStatusText = "\u5f00\u542f Snap Tap \u524d\u81f3\u5c11\u8981\u914d\u4e00\u7ec4\u952e\u3002";
                    return;
                }

                await _service.SetAnalogSnapTapAsync(
                    Connection, AnalogKeyClassId, AnalogSnapTapEnabled, pairs, cancellationToken);
                await RefreshAnalogSnapTapAsync(cancellationToken);
                await SaveProfileAsync(
                    settings => settings.Keyboard.SnapTapEnabled = AnalogSnapTapEnabled,
                    settings => settings.Keyboard.SnapTapEnabled = previousProfile,
                    () => AnalogSnapTapEnabled = previousEnabled,
                    token => _service.SetAnalogSnapTapAsync(
                        Connection, AnalogKeyClassId, previousEnabled, previousPairs, token),
                    cancellationToken);
                AnalogSnapTapStatusText =
                    $"\u5df2\u5e94\u7528 {pairs.Count} \u7ec4 Snap Tap \u5e76\u8bfb\u56de\u3002";
            }, cancellationToken);

    private async Task RefreshAnalogSnapTapAsync(CancellationToken cancellationToken)
    {
        if (_analogKeyLayout is null)
        {
            return;
        }

        BuildAnalogSnapTapChoices();
        try
        {
            var state = await _service.GetAnalogSnapTapAsync(
                Connection, AnalogKeyClassId, cancellationToken);
            AnalogSnapTapEnabled = state.Enabled;
            _analogSnapTapPairs.Clear();
            foreach (var pair in state.Pairs)
            {
                var key1 = _analogKeyKeys.FirstOrDefault(key => key.KeyId == pair.KeyId1);
                var key2 = _analogKeyKeys.FirstOrDefault(key => key.KeyId == pair.KeyId2);
                _analogSnapTapPairs.Add(new AnalogSnapTapPairViewModel(
                    _analogSnapTapKeyChoices, key1, key2, pair.Mode));
            }

            _analogSnapTapAvailable = true;
            AnalogSnapTapStatusText = "\u9009\u4e24\u4e2a\u952e\u7ec4\u6210\u4e00\u5bf9\uff0c\u6700\u591a 4 \u7ec4\u3002Windows \u4e0e Menu \u952e\u4e0d\u53c2\u4e0e\u3002";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _analogSnapTapAvailable = false;
            AnalogSnapTapStatusText = LocalizeProtocolError(exception);
        }

        OnPropertyChanged(nameof(AnalogSnapTapVisibility));
        OnPropertyChanged(nameof(CanApplyAnalogSnapTap));
    }

    private void BuildAnalogSnapTapChoices()
    {
        if (_analogSnapTapKeyChoices.Count > 0)
        {
            return;
        }

        foreach (var key in _analogKeyKeys)
        {
            if (key.KeyId is 127 or 129)
            {
                continue;
            }

            _analogSnapTapKeyChoices.Add(key);
        }
    }
}
