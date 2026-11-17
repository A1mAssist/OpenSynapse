using OpenSynapse.Core.Macros;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidMappingViewModel
{
    internal string? DeviceInstanceId { get; set; }

    /// <summary>
    /// Triggers saved from this page must name the physical device; a bare
    /// product id lets an identical offline twin inherit the macro.
    /// </summary>
    private MacroInput? ResolveSoftwareTrigger(byte buttonId)
    {
        var input = _softwareTriggerResolver?.Invoke(buttonId);
        if (input is not null && string.IsNullOrWhiteSpace(input.InstanceId))
        {
            input.InstanceId = DeviceInstanceId;
        }

        return input;
    }
}
