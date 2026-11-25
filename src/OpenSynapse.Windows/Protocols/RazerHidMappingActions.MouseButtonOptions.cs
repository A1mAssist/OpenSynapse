namespace OpenSynapse.Windows.Protocols;

public static partial class RazerHidMappingActions
{
    /// <summary>The firmware button id behind a "Mouse button" dropdown option.</summary>
    public static bool TryGetMouseButtonId(int optionIndex, out byte buttonId)
    {
        if (optionIndex < 0 || optionIndex >= MouseButtons.Length)
        {
            buttonId = 0;
            return false;
        }

        buttonId = MouseButtons[optionIndex].Id;
        return true;
    }

    /// <summary>The dropdown option that already names this firmware button id.</summary>
    public static int MouseButtonOptionIndex(byte buttonId)
    {
        for (var index = 0; index < MouseButtons.Length; index++)
        {
            if (MouseButtons[index].Id == buttonId)
            {
                return index;
            }
        }

        return -1;
    }
}
