using System.Runtime.InteropServices;
using OpenSynapse.ChromaSdk.Protocol;

namespace OpenSynapse.ChromaSdk.Shim;

internal static class ChromaEffects
{
    private const int KeyboardCustomKeyBytes = 6 * 22 * sizeof(uint);

    public static int Translate(
        ChromaSdkDeviceType deviceType,
        int effect,
        IntPtr param,
        out ChromaSdkEffect? value)
    {
        value = null;
        if (effect < 0) return SdkResult.InvalidParameter;

        return deviceType switch
        {
            ChromaSdkDeviceType.Keyboard => TranslateKeyboard(effect, param, out value),
            ChromaSdkDeviceType.Mouse => TranslateMouse(effect, param, out value),
            ChromaSdkDeviceType.Headset => TranslateHeadset(effect, param, out value),
            ChromaSdkDeviceType.Mousepad => TranslateMousepad(effect, param, out value),
            ChromaSdkDeviceType.Keypad => TranslateKeypad(effect, param, out value),
            ChromaSdkDeviceType.ChromaLink => TranslateChromaLink(effect, param, out value),
            _ => SdkResult.InvalidParameter,
        };
    }

    private static int TranslateKeyboard(int effect, IntPtr param, out ChromaSdkEffect? value)
    {
        value = null;
        switch (effect)
        {
            case 0:
                value = None(ChromaSdkDeviceType.Keyboard);
                return SdkResult.Success;
            case 2:
                if (param == IntPtr.Zero) return SdkResult.InvalidParameter;
                value = Custom(ChromaSdkDeviceType.Keyboard, 22, 6, param);
                return SdkResult.Success;
            case 4:
                if (param == IntPtr.Zero) return SdkResult.InvalidParameter;
                value = Static(ChromaSdkDeviceType.Keyboard, ReadColor(param));
                return SdkResult.Success;
            case 8:
                if (param == IntPtr.Zero) return SdkResult.InvalidParameter;
                value = CustomKey(param);
                return SdkResult.Success;
            case 9:
                if (param == IntPtr.Zero) return SdkResult.InvalidParameter;
                value = Custom2(ChromaSdkDeviceType.Keyboard, 24, 8, param);
                return SdkResult.Success;
            case >= 10:
                return SdkResult.NotSupported;
            default:
                return SdkResult.NotSupported;
        }
    }

    private static int TranslateMouse(int effect, IntPtr param, out ChromaSdkEffect? value)
    {
        value = null;
        switch (effect)
        {
            case 0:
                value = None(ChromaSdkDeviceType.Mouse);
                return SdkResult.Success;
            case 3:
                if (param == IntPtr.Zero) return SdkResult.InvalidParameter;
                value = Custom(ChromaSdkDeviceType.Mouse, 30, 1, param);
                return SdkResult.Success;
            case 8:
                if (param == IntPtr.Zero) return SdkResult.InvalidParameter;
                value = Custom(ChromaSdkDeviceType.Mouse, 7, 9, param);
                return SdkResult.Success;
            case 6:
                if (param == IntPtr.Zero) return SdkResult.InvalidParameter;
                var ledId = Marshal.ReadInt32(param);
                if (ledId is not 0 and not 0xFFFF) return SdkResult.NotSupported;
                value = Static(ChromaSdkDeviceType.Mouse, unchecked((uint)Marshal.ReadInt32(param, 4)));
                return SdkResult.Success;
            case 9:
                if (param == IntPtr.Zero) return SdkResult.InvalidParameter;
                value = Static(ChromaSdkDeviceType.Mouse, ReadColor(param));
                return SdkResult.Success;
            case >= 10:
                return SdkResult.NotSupported;
            default:
                return SdkResult.NotSupported;
        }
    }

    private static int TranslateHeadset(int effect, IntPtr param, out ChromaSdkEffect? value)
    {
        value = null;
        switch (effect)
        {
            case 0:
                value = None(ChromaSdkDeviceType.Headset);
                return SdkResult.Success;
            case 1:
                if (param == IntPtr.Zero) return SdkResult.InvalidParameter;
                value = Static(ChromaSdkDeviceType.Headset, ReadColor(param));
                return SdkResult.Success;
            case 4:
                if (param == IntPtr.Zero) return SdkResult.InvalidParameter;
                value = Custom(ChromaSdkDeviceType.Headset, 5, 1, param);
                return SdkResult.Success;
            case >= 5:
                return SdkResult.NotSupported;
            default:
                return SdkResult.NotSupported;
        }
    }

    private static int TranslateMousepad(int effect, IntPtr param, out ChromaSdkEffect? value)
    {
        value = null;
        switch (effect)
        {
            case 0:
                value = None(ChromaSdkDeviceType.Mousepad);
                return SdkResult.Success;
            case 2:
                if (param == IntPtr.Zero) return SdkResult.InvalidParameter;
                value = Custom(ChromaSdkDeviceType.Mousepad, 15, 1, param);
                return SdkResult.Success;
            case 4:
                if (param == IntPtr.Zero) return SdkResult.InvalidParameter;
                value = Static(ChromaSdkDeviceType.Mousepad, ReadColor(param));
                return SdkResult.Success;
            case 6:
                if (param == IntPtr.Zero) return SdkResult.InvalidParameter;
                value = Custom(ChromaSdkDeviceType.Mousepad, 20, 1, param);
                return SdkResult.Success;
            case >= 7:
                return SdkResult.NotSupported;
            default:
                return SdkResult.NotSupported;
        }
    }

    private static int TranslateKeypad(int effect, IntPtr param, out ChromaSdkEffect? value)
    {
        value = null;
        switch (effect)
        {
            case 0:
                value = None(ChromaSdkDeviceType.Keypad);
                return SdkResult.Success;
            case 2:
                if (param == IntPtr.Zero) return SdkResult.InvalidParameter;
                value = Custom(ChromaSdkDeviceType.Keypad, 4, 5, param);
                return SdkResult.Success;
            case 5:
                if (param == IntPtr.Zero) return SdkResult.InvalidParameter;
                value = Static(ChromaSdkDeviceType.Keypad, ReadColor(param));
                return SdkResult.Success;
            case >= 7:
                return SdkResult.NotSupported;
            default:
                return SdkResult.NotSupported;
        }
    }

    private static int TranslateChromaLink(int effect, IntPtr param, out ChromaSdkEffect? value)
    {
        value = null;
        switch (effect)
        {
            case 0:
                value = None(ChromaSdkDeviceType.ChromaLink);
                return SdkResult.Success;
            case 1:
                if (param == IntPtr.Zero) return SdkResult.InvalidParameter;
                value = Custom(ChromaSdkDeviceType.ChromaLink, 5, 1, param);
                return SdkResult.Success;
            case 2:
                if (param == IntPtr.Zero) return SdkResult.InvalidParameter;
                value = Static(ChromaSdkDeviceType.ChromaLink, ReadColor(param));
                return SdkResult.Success;
            case >= 3:
                return SdkResult.NotSupported;
            default:
                return SdkResult.NotSupported;
        }
    }

    private static ChromaSdkEffect CustomKey(IntPtr param) => new(
        ChromaSdkDeviceType.Keyboard,
        ChromaSdkEffectKind.CustomKey,
        22,
        6,
        ReadColors(param, 6 * 22),
        ReadColors(IntPtr.Add(param, KeyboardCustomKeyBytes), 6 * 22));

    private static ChromaSdkEffect Custom(ChromaSdkDeviceType deviceType, int width, int height, IntPtr param) => new(
        deviceType,
        ChromaSdkEffectKind.Custom,
        width,
        height,
        ReadColors(param, width * height));

    private static ChromaSdkEffect Custom2(ChromaSdkDeviceType deviceType, int width, int height, IntPtr param) => new(
        deviceType,
        ChromaSdkEffectKind.Custom2,
        width,
        height,
        ReadColors(param, width * height));

    private static ChromaSdkEffect Static(ChromaSdkDeviceType deviceType, uint color) => new(
        deviceType,
        ChromaSdkEffectKind.Static,
        1,
        1,
        new[] { color });

    private static ChromaSdkEffect None(ChromaSdkDeviceType deviceType) => new(
        deviceType,
        ChromaSdkEffectKind.None,
        1,
        1,
        new uint[1]);

    private static uint ReadColor(IntPtr param) => unchecked((uint)Marshal.ReadInt32(param));

    private static unsafe uint[] ReadColors(IntPtr param, int count) =>
        new ReadOnlySpan<uint>((void*)param, count).ToArray();
}
