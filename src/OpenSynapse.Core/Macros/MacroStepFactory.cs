namespace OpenSynapse.Core.Macros;

public static class MacroStepFactory
{
    public static IReadOnlyList<MacroStep> CreateCombo(
        MacroModifierKeys modifiers,
        MacroInput key,
        int delayMilliseconds = 0)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Kind != MacroInputKind.Keyboard)
        {
            throw new ArgumentException("A combo key must be a keyboard key.", nameof(key));
        }

        var groupId = Guid.NewGuid().ToString("N");
        var steps = new List<MacroStep>();
        var first = true;
        foreach (var modifier in ModifierInputs(modifiers))
        {
            steps.Add(new MacroStep
            {
                Kind = MacroStepKind.Input,
                Input = modifier.Clone(),
                IsDown = true,
                AutoRelease = false,
                DelayMilliseconds = first ? delayMilliseconds : 0,
                GroupId = groupId,
            });
            first = false;
        }

        steps.Add(new MacroStep
        {
            Kind = MacroStepKind.Input,
            Input = key.Clone(),
            IsDown = true,
            AutoRelease = false,
            DelayMilliseconds = first ? delayMilliseconds : 0,
            GroupId = groupId,
        });

        steps.Add(new MacroStep
        {
            Kind = MacroStepKind.Input,
            Input = key.Clone(),
            IsDown = false,
            GroupId = groupId,
        });

        var modifierInputs = ModifierInputs(modifiers).ToArray();
        for (var index = modifierInputs.Length - 1; index >= 0; index--)
        {
            steps.Add(new MacroStep
            {
                Kind = MacroStepKind.Input,
                Input = modifierInputs[index].Clone(),
                IsDown = false,
                GroupId = groupId,
            });
        }

        return steps;
    }

    public static MacroStep CreateTap(
        MacroInput input,
        int holdMilliseconds = MacroStep.DefaultHoldMilliseconds,
        int delayMilliseconds = 0)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new MacroStep
        {
            Kind = MacroStepKind.Input,
            Input = input.Clone(),
            IsDown = input.Kind != MacroInputKind.MouseWheel,
            AutoRelease = true,
            HoldMilliseconds = holdMilliseconds,
            DelayMilliseconds = delayMilliseconds,
        };
    }

    public static IReadOnlyList<MacroInput> ModifierInputs(MacroModifierKeys modifiers)
    {
        var inputs = new List<MacroInput>(4);
        if ((modifiers & MacroModifierKeys.Control) != 0)
        {
            inputs.Add(new MacroInput { Kind = MacroInputKind.Keyboard, VirtualKey = 0xA2 });
        }
        if ((modifiers & MacroModifierKeys.Shift) != 0)
        {
            inputs.Add(new MacroInput { Kind = MacroInputKind.Keyboard, VirtualKey = 0xA0 });
        }
        if ((modifiers & MacroModifierKeys.Alt) != 0)
        {
            inputs.Add(new MacroInput { Kind = MacroInputKind.Keyboard, VirtualKey = 0xA4 });
        }
        if ((modifiers & MacroModifierKeys.Windows) != 0)
        {
            inputs.Add(new MacroInput
            {
                Kind = MacroInputKind.Keyboard,
                VirtualKey = 0x5B,
                Extended = true,
            });
        }
        return inputs;
    }
}
