using System.Runtime.InteropServices;

namespace OpenSynapse.Core.Devices;

/// <summary>
/// Decodes AVIF with the bundled libavif + dav1d pair. Windows' AVIF codec drops the
/// alpha plane, so the mapping page needs its own decoder to keep transparent product
/// art clean.
/// </summary>
public static class RazerAvifDecoder
{
    private const int AvifResultOk = 0;
    private const int AvifRgbFormatBgra = 4;
    private const int MaxDimension = 4096;

    public static bool TryDecode(byte[] data, out byte[] bgra, out int width, out int height)
    {
        bgra = [];
        width = 0;
        height = 0;
        if (data is not { Length: > 0 })
        {
            return false;
        }

        IntPtr decoder = IntPtr.Zero;
        IntPtr image = IntPtr.Zero;
        var rgb = new AvifRgbImage();
        var allocated = false;
        try
        {
            decoder = Native.avifDecoderCreate();
            image = Native.avifImageCreateEmpty();
            if (decoder == IntPtr.Zero || image == IntPtr.Zero)
            {
                return false;
            }

            if (Native.avifDecoderReadMemory(decoder, image, data, (UIntPtr)data.Length) != AvifResultOk)
            {
                return false;
            }

            Native.avifRGBImageSetDefaults(ref rgb, image);
            if (rgb.Width == 0 || rgb.Height == 0 ||
                rgb.Width > MaxDimension || rgb.Height > MaxDimension)
            {
                return false;
            }

            rgb.Format = AvifRgbFormatBgra;
            rgb.Depth = 8;
            rgb.AlphaPremultiplied = 0;
            rgb.MaxThreads = 1;
            if (Native.avifRGBImageAllocatePixels(ref rgb) != AvifResultOk)
            {
                return false;
            }

            allocated = true;
            if (Native.avifImageYUVToRGB(image, ref rgb) != AvifResultOk)
            {
                return false;
            }

            var rowBytes = checked((int)rgb.Width * 4);
            var stride = checked((int)rgb.RowBytes);
            var pixels = new byte[checked(rowBytes * (int)rgb.Height)];
            for (var y = 0; y < (int)rgb.Height; y++)
            {
                Marshal.Copy(
                    IntPtr.Add(rgb.Pixels, checked(y * stride)),
                    pixels,
                    checked(y * rowBytes),
                    rowBytes);
            }

            bgra = pixels;
            width = (int)rgb.Width;
            height = (int)rgb.Height;
            return true;
        }
        catch (Exception exception) when (exception is DllNotFoundException or
            BadImageFormatException or EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            if (allocated)
            {
                Native.avifRGBImageFreePixels(ref rgb);
            }

            if (image != IntPtr.Zero)
            {
                Native.avifImageDestroy(image);
            }

            if (decoder != IntPtr.Zero)
            {
                Native.avifDecoderDestroy(decoder);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AvifRgbImage
    {
        public uint Width;
        public uint Height;
        public uint Depth;
        public int Format;
        public int ChromaUpsampling;
        public int ChromaDownsampling;
        public int AvoidLibYuv;
        public int IgnoreAlpha;
        public int AlphaPremultiplied;
        public int IsFloat;
        public int MaxThreads;
        public IntPtr Pixels;
        public uint RowBytes;
    }

    private static class Native
    {
        [DllImport("avif.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr avifDecoderCreate();

        [DllImport("avif.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void avifDecoderDestroy(IntPtr decoder);

        [DllImport("avif.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr avifImageCreateEmpty();

        [DllImport("avif.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void avifImageDestroy(IntPtr image);

        [DllImport("avif.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int avifDecoderReadMemory(
            IntPtr decoder, IntPtr image, byte[] data, UIntPtr size);

        [DllImport("avif.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void avifRGBImageSetDefaults(ref AvifRgbImage rgb, IntPtr image);

        [DllImport("avif.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int avifRGBImageAllocatePixels(ref AvifRgbImage rgb);

        [DllImport("avif.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void avifRGBImageFreePixels(ref AvifRgbImage rgb);

        [DllImport("avif.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int avifImageYUVToRGB(IntPtr image, ref AvifRgbImage rgb);
    }
}
