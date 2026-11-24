using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private bool _isScrollWheelModeBusy;
    private string _scrollWheelModeStatusText = string.Empty;
    private RazerHidScrollWheelModeChoice? _selectedScrollWheelMode;
    private IReadOnlyList<RazerHidScrollWheelModeChoice> _scrollWheelModeChoices = [];
    private byte _scrollTactileMode;

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
         Has(RazerHidBackendCapability.ScrollTactileModeWrite) ||
         Has(RazerHidBackendCapability.ScrollFreeNotchedModeWrite) ||
         Has(RazerHidBackendCapability.SmartReelWrite));

    public byte ScrollTactileMode
    {
        get => _scrollTactileMode;
        set => SetField(ref _scrollTactileMode, value);
    }

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

    private bool HasWritableLkScrollMode =>
        Has(RazerHidBackendCapability.LkScrollWheelModeWrite);

    private bool HasScrollMode => HasAny(
        RazerHidBackendCapability.ScrollModeRead,
        RazerHidBackendCapability.ScrollModeWrite);

    private bool HasFreeNotchedScrollMode => HasAny(
        RazerHidBackendCapability.ScrollFreeNotchedModeRead,
        RazerHidBackendCapability.ScrollFreeNotchedModeWrite);

    private bool HasTactileScrollMode => HasAny(
        RazerHidBackendCapability.ScrollTactileModeRead,
        RazerHidBackendCapability.ScrollTactileModeWrite);

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
        else if (HasTactileScrollMode)
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

        // Device rescans rebuild the capability set several times a second; the list must
        // not throw away the mode the user just picked in the meantime.
        var kept = previousKind is { } kind
            ? choices.FirstOrDefault(choice => choice.Kind == kind)
            : null;
        SelectedScrollWheelMode = kept ?? choices.FirstOrDefault();
        if (kept is null)
        {
            RefreshScrollWheelModeSelection();
        }
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
        else if (HasWritableLkScrollMode)
        {
            kind = LkScrollMask switch
            {
                4 => RazerHidScrollWheelModeKind.MicroTactile,
                2 => RazerHidScrollWheelModeKind.FreeSpin,
                _ => RazerHidScrollWheelModeKind.Tactile,
            };
        }
        else if (HasTactileScrollMode)
        {
            kind = ScrollTactileMode switch
            {
                2 => RazerHidScrollWheelModeKind.MicroTactile,
                1 => RazerHidScrollWheelModeKind.FreeSpin,
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

    [ProfileSetting("mouse.scroll-preset", DeviceCategory.Mouse, powerScoped: true)]
    public Task ApplySelectedScrollWheelModeAsync(CancellationToken cancellationToken = default)
    {
        if (!CanEditScrollWheelMode || SelectedScrollWheelMode is not { } choice)
        {
            return Task.CompletedTask;
        }

        var capability = Has(RazerHidBackendCapability.LkScrollWheelModeWrite)
            ? RazerHidBackendCapability.LkScrollWheelModeWrite
            : Has(RazerHidBackendCapability.ScrollTactileModeWrite)
                ? RazerHidBackendCapability.ScrollTactileModeWrite
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
                var previousProfile = ReadProfile(settings => (
                    settings.Viper.ScrollMode,
                    settings.Viper.ScrollFreeNotched,
                    settings.Viper.SmartReelEnabled));
                var previousChoice = SelectedScrollWheelMode;
                var isSmartReel = choice.Kind == RazerHidScrollWheelModeKind.SmartReel;
                if (isSmartReel)
                {
                    if (Has(RazerHidBackendCapability.LkScrollWheelModeWrite))
                    {
                        await _service.SetLkScrollWheelModeAsync(
                            Connection, 3, cancellationToken);
                    }
                    else if (Has(RazerHidBackendCapability.ScrollTactileModeWrite))
                    {
                        await _service.SetScrollTactileModeAsync(
                            Connection, RazerScrollTactileMode.Tactile,
                            await _service.ResolveActiveProfileIdAsync(Connection, cancellationToken),
                            cancellationToken);
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
                    else if (Has(RazerHidBackendCapability.ScrollTactileModeWrite))
                    {
                        await _service.SetScrollTactileModeAsync(
                            Connection, (RazerScrollTactileMode)ScrollModeForPreset(choice.Kind),
                            await _service.ResolveActiveProfileIdAsync(Connection, cancellationToken),
                            cancellationToken);
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

                // Verification reads are advisory. The docked 35K answers the setters but
                // not every getter, and a getter that stays silent must not undo the write.
                var verified = false;
                if (Has(RazerHidBackendCapability.LkScrollWheelModeRead))
                {
                    verified |= await TryReadbackAsync(async () => SetLkScrollMask(
                        (await _service.GetLkScrollWheelModeAsync(Connection, cancellationToken)).ModeMask));
                }
                if (Has(RazerHidBackendCapability.ScrollModeRead))
                {
                    verified |= await TryReadbackAsync(async () =>
                    {
                        ScrollMode = await _service.GetScrollModeAsync(Connection, cancellationToken);
                    });
                }
                if (Has(RazerHidBackendCapability.ScrollTactileModeRead))
                {
                    verified |= await TryReadbackAsync(async () =>
                    {
                        ScrollTactileMode = (byte)await _service.GetScrollTactileModeAsync(
                            Connection,
                            await _service.ResolveActiveProfileIdAsync(Connection, cancellationToken),
                            cancellationToken);
                    });
                }
                if (Has(RazerHidBackendCapability.ScrollFreeNotchedModeRead))
                {
                    verified |= await TryReadbackAsync(async () =>
                    {
                        ScrollFreeNotchedModeIndex =
                            (int)await _service.GetScrollFreeNotchedModeAsync(Connection, cancellationToken);
                    });
                }
                if (Has(RazerHidBackendCapability.SmartReelRead))
                {
                    verified |= await TryReadbackAsync(async () =>
                    {
                        SmartReelEnabled = await _service.GetSmartReelAsync(Connection, cancellationToken);
                    });
                }
                _log?.Invoke($"scroll preset apply: preset={choice.Kind}, verified={verified}");

                var usesScrollModeProfile = !Has(RazerHidBackendCapability.LkScrollWheelModeWrite) &&
                    !Has(RazerHidBackendCapability.ScrollTactileModeWrite) &&
                    Has(RazerHidBackendCapability.ScrollModeWrite);
                var usesTactileModeProfile = !Has(RazerHidBackendCapability.LkScrollWheelModeWrite) &&
                    Has(RazerHidBackendCapability.ScrollTactileModeWrite);
                var usesFreeNotchedProfile = !Has(RazerHidBackendCapability.LkScrollWheelModeWrite) &&
                    !usesScrollModeProfile &&
                    !usesTactileModeProfile &&
                    Has(RazerHidBackendCapability.ScrollFreeNotchedModeWrite);
                if (verified)
                {
                    RefreshScrollWheelModeSelection();
                }
                await SaveProfileAsync(
                    settings =>
                    {
                        if (usesTactileModeProfile)
                        {
                            settings.Viper.ScrollMode = ToProfileScrollMode(ScrollTactileMode);
                        }
                        else if (usesScrollModeProfile || isSmartReel)
                        {
                            settings.Viper.ScrollMode =
                                ToProfileScrollMode(ScrollModeForPreset(choice.Kind));
                        }
                        if (usesScrollModeProfile || usesTactileModeProfile ||
                            usesFreeNotchedProfile || isSmartReel)
                        {
                            settings.Viper.ScrollFreeNotched =
                                choice.Kind == RazerHidScrollWheelModeKind.FreeSpin;
                        }
                        if (Has(RazerHidBackendCapability.SmartReelWrite))
                        {
                            settings.Viper.SmartReelEnabled = isSmartReel;
                        }
                    },
                    settings =>
                    {
                        settings.Viper.ScrollMode = previousProfile.ScrollMode;
                        settings.Viper.ScrollFreeNotched = previousProfile.ScrollFreeNotched;
                        settings.Viper.SmartReelEnabled = previousProfile.SmartReelEnabled;
                    },
                    () => SelectedScrollWheelMode = previousChoice,
                    null,
                    cancellationToken);
                ScrollWheelModeStatusText = AppStrings.Text("RazerHidScrollModeApplied");
            },
            cancellationToken);
    }

    /// <summary>
    /// A post-write read only refreshes the UI. Failing it is not a failed write, so it
    /// must not reach the caller's rollback path.
    /// </summary>
    private static async Task<bool> TryReadbackAsync(Func<Task> read)
    {
        try
        {
            await read();
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
