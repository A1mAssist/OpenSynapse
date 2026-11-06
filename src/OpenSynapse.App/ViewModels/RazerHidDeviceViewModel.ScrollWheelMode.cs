using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private bool _isScrollWheelModeBusy;
    private string _scrollWheelModeStatusText = string.Empty;
    private RazerHidScrollWheelModeChoice? _selectedScrollWheelMode;
    private IReadOnlyList<RazerHidScrollWheelModeChoice> _scrollWheelModeChoices = [];

    public IReadOnlyList<RazerHidScrollWheelModeChoice> ScrollWheelModeChoices
    {
        get => _scrollWheelModeChoices;
        private set
        {
            _scrollWheelModeChoices = value;
            OnPropertyChanged(nameof(ScrollWheelModeChoices));
            OnPropertyChanged(nameof(ScrollWheelModePresetVisibility));
            OnPropertyChanged(nameof(CanApplyScrollWheelMode));
        }
    }

    public RazerHidScrollWheelModeChoice? SelectedScrollWheelMode
    {
        get => _selectedScrollWheelMode;
        set
        {
            if (!SetField(ref _selectedScrollWheelMode, value))
            {
                return;
            }
            OnPropertyChanged(nameof(CanApplyScrollWheelMode));
        }
    }

    public Visibility ScrollWheelModePresetVisibility =>
        VisibleWhen(ScrollWheelModeChoices.Count > 0);

    public bool IsScrollWheelModeBusy
    {
        get => _isScrollWheelModeBusy;
        private set => SetBusy(
            ref _isScrollWheelModeBusy, value,
            nameof(CanApplyScrollWheelMode),
            nameof(CanEditScrollWheelMode));
    }

    public bool CanApplyScrollWheelMode =>
        CanEditScrollWheelMode && SelectedScrollWheelMode is not null;

    public bool CanEditScrollWheelMode =>
        CanWrite &&
        !IsScrollWheelModeBusy &&
        (Has(RazerHidBackendCapability.LkScrollWheelModeWrite) ||
         Has(RazerHidBackendCapability.ScrollModeWrite) ||
         Has(RazerHidBackendCapability.ScrollFreeNotchedModeWrite) ||
         Has(RazerHidBackendCapability.SmartReelWrite));

    public string ScrollWheelModeStatusText
    {
        get => _scrollWheelModeStatusText;
        private set => SetField(ref _scrollWheelModeStatusText, value);
    }


    internal static byte LkScrollMaskForPreset(RazerHidScrollWheelModeKind kind) => kind switch
    {
        RazerHidScrollWheelModeKind.Tactile => 1,
        RazerHidScrollWheelModeKind.FreeSpin => 2,
        RazerHidScrollWheelModeKind.MicroTactile => 4,
        RazerHidScrollWheelModeKind.SmartReel => 3,
        _ => 1,
    };

    // The middleware scroll_mode enum is Tactile=0, FreeSpin=1, MicroTactile=2.
    internal static byte ScrollModeForPreset(RazerHidScrollWheelModeKind kind) => kind switch
    {
        RazerHidScrollWheelModeKind.FreeSpin => 1,
        RazerHidScrollWheelModeKind.MicroTactile => 2,
        _ => 0,
    };

    internal static RazerScrollFreeNotchedMode FreeNotchedModeForPreset(
        RazerHidScrollWheelModeKind kind) =>
        kind == RazerHidScrollWheelModeKind.FreeSpin
            ? RazerScrollFreeNotchedMode.FreeSpinMode
            : RazerScrollFreeNotchedMode.NotchedMode;

    private bool HasLkScrollMode => HasAny(
        RazerHidBackendCapability.LkScrollWheelModeRead,
        RazerHidBackendCapability.LkScrollWheelModeWrite);

    private bool HasScrollMode => HasAny(
        RazerHidBackendCapability.ScrollModeRead,
        RazerHidBackendCapability.ScrollModeWrite);

    private bool HasFreeNotchedScrollMode => HasAny(
        RazerHidBackendCapability.ScrollFreeNotchedModeRead,
        RazerHidBackendCapability.ScrollFreeNotchedModeWrite);

    private bool HasSmartReel => HasAny(
        RazerHidBackendCapability.SmartReelRead,
        RazerHidBackendCapability.SmartReelWrite);

    private void RebuildScrollWheelModeChoices()
    {
        var choices = new List<RazerHidScrollWheelModeChoice>();
        var hasLk = HasLkScrollMode;
        var hasScrollMode = HasScrollMode;
        var hasFreeNotched = HasFreeNotchedScrollMode;
        if (hasLk)
        {
            AddMode(choices, RazerHidScrollWheelModeKind.Tactile, "BleScrollTactile");
            AddMode(choices, RazerHidScrollWheelModeKind.FreeSpin, "BleScrollFreeSpin");
            AddMode(choices, RazerHidScrollWheelModeKind.MicroTactile, "BleScrollMicroTactile");
        }
        else if (hasScrollMode || hasFreeNotched)
        {
            AddMode(choices, RazerHidScrollWheelModeKind.Tactile, "BleScrollTactile");
            AddMode(choices, RazerHidScrollWheelModeKind.FreeSpin, "BleScrollFreeSpin");
        }

        if (HasSmartReel)
        {
            AddMode(choices, RazerHidScrollWheelModeKind.SmartReel, "RazerHidScrollModeSmartReel");
        }

        var previousKind = SelectedScrollWheelMode?.Kind;
        ScrollWheelModeChoices = choices;
        SelectedScrollWheelMode = choices.FirstOrDefault(choice => choice.Kind == previousKind)
            ?? choices.FirstOrDefault();
        RefreshScrollWheelModeSelection();
    }

    private static void AddMode(
        List<RazerHidScrollWheelModeChoice> choices,
        RazerHidScrollWheelModeKind kind,
        string titleKey) =>
        choices.Add(new RazerHidScrollWheelModeChoice(kind, AppStrings.Text(titleKey)));

    private void RefreshScrollWheelModeSelection()
    {
        if (ScrollWheelModeChoices.Count == 0)
        {
            SelectedScrollWheelMode = null;
            return;
        }

        RazerHidScrollWheelModeKind kind;
        if (HasSmartReel && SmartReelEnabled)
        {
            kind = RazerHidScrollWheelModeKind.SmartReel;
        }
        else if (HasLkScrollMode)
        {
            kind = LkScrollMask switch
            {
                4 => RazerHidScrollWheelModeKind.MicroTactile,
                2 => RazerHidScrollWheelModeKind.FreeSpin,
                _ => RazerHidScrollWheelModeKind.Tactile,
            };
        }
        else if (HasScrollMode)
        {
            kind = ScrollMode switch
            {
                2 => RazerHidScrollWheelModeKind.MicroTactile,
                1 => RazerHidScrollWheelModeKind.FreeSpin,
                _ => RazerHidScrollWheelModeKind.Tactile,
            };
        }
        else
        {
            kind = ScrollFreeNotchedModeIndex == 1
                ? RazerHidScrollWheelModeKind.FreeSpin
                : RazerHidScrollWheelModeKind.Tactile;
        }

        SelectedScrollWheelMode = ScrollWheelModeChoices.FirstOrDefault(choice => choice.Kind == kind)
            ?? ScrollWheelModeChoices[0];
    }

    public Task ApplySelectedScrollWheelModeAsync(CancellationToken cancellationToken = default)
    {
        if (!CanEditScrollWheelMode || SelectedScrollWheelMode is not { } choice)
        {
            return Task.CompletedTask;
        }

        var capability = Has(RazerHidBackendCapability.LkScrollWheelModeWrite)
            ? RazerHidBackendCapability.LkScrollWheelModeWrite
            : Has(RazerHidBackendCapability.ScrollModeWrite)
                ? RazerHidBackendCapability.ScrollModeWrite
                : Has(RazerHidBackendCapability.ScrollFreeNotchedModeWrite)
                    ? RazerHidBackendCapability.ScrollFreeNotchedModeWrite
                    : RazerHidBackendCapability.SmartReelWrite;

        return RunWriteAsync(
            capability,
            () => IsScrollWheelModeBusy,
            value => IsScrollWheelModeBusy = value,
            async () =>
            {
                var isSmartReel = choice.Kind == RazerHidScrollWheelModeKind.SmartReel;
                if (isSmartReel)
                {
                    if (Has(RazerHidBackendCapability.LkScrollWheelModeWrite))
                    {
                        await _service.SetLkScrollWheelModeAsync(
                            Connection, 3, cancellationToken);
                    }
                    else if (Has(RazerHidBackendCapability.ScrollFreeNotchedModeWrite))
                    {
                        await _service.SetScrollFreeNotchedModeAsync(
                            Connection, RazerScrollFreeNotchedMode.NotchedMode, cancellationToken);
                    }
                    if (Has(RazerHidBackendCapability.SmartReelWrite))
                    {
                        await _service.SetSmartReelAsync(Connection, true, cancellationToken);
                    }
                }
                else
                {
                    if (Has(RazerHidBackendCapability.LkScrollWheelModeWrite))
                    {
                        await _service.SetLkScrollWheelModeAsync(
                            Connection, LkScrollMaskForPreset(choice.Kind), cancellationToken);
                    }
                    else if (Has(RazerHidBackendCapability.ScrollModeWrite))
                    {
                        await _service.SetScrollModeAsync(
                            Connection, ScrollModeForPreset(choice.Kind), cancellationToken);
                    }
                    else if (Has(RazerHidBackendCapability.ScrollFreeNotchedModeWrite))
                    {
                        await _service.SetScrollFreeNotchedModeAsync(
                            Connection, FreeNotchedModeForPreset(choice.Kind), cancellationToken);
                    }
                    if (Has(RazerHidBackendCapability.SmartReelWrite))
                    {
                        await _service.SetSmartReelAsync(Connection, false, cancellationToken);
                    }
                }

                if (Has(RazerHidBackendCapability.LkScrollWheelModeRead))
                {
                    SetLkScrollMask((await _service.GetLkScrollWheelModeAsync(
                        Connection, cancellationToken)).ModeMask);
                }
                if (Has(RazerHidBackendCapability.ScrollModeRead))
                {
                    ScrollMode = await _service.GetScrollModeAsync(Connection, cancellationToken);
                }
                if (Has(RazerHidBackendCapability.ScrollFreeNotchedModeRead))
                {
                    ScrollFreeNotchedModeIndex = (int)await _service.GetScrollFreeNotchedModeAsync(
                        Connection, cancellationToken);
                }
                if (Has(RazerHidBackendCapability.SmartReelRead))
                {
                    SmartReelEnabled = await _service.GetSmartReelAsync(Connection, cancellationToken);
                }

                RefreshScrollWheelModeSelection();
                ScrollWheelModeStatusText = AppStrings.Text("RazerHidScrollModeApplied");
            },
            cancellationToken);
    }
}
