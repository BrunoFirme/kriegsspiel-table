using AssetsTools.NET;
using AssetRipper.TextureDecoder.Dxt;
using AssetRipper.TextureDecoder.Rgb.Formats;

namespace KTable.Converter.Bundles;

public sealed record Picture(int Width, int Height, byte[] Rgba);

public static class UnityTexture
{
    const int Alpha8 = 1;
    const int Rgb24 = 3;
    const int Rgba32 = 4;
    const int Dxt1 = 10;
    const int Dxt5 = 12;
    const int Channels = 4;
    const int Opaque = 255;

    public static Picture? Read(AssetTypeValueField texture, Func<AssetTypeValueField, byte[]?> sideFile)
    {
        int width = texture["m_Width"].AsInt, height = texture["m_Height"].AsInt;
        if (width <= 0 || height <= 0) return null;
        var bytes = texture["image data"].AsByteArray;
        if (bytes.Length == 0) bytes = sideFile(texture["m_StreamData"]) ?? Array.Empty<byte>();
        if (bytes.Length == 0) return null;
        var rgba = Decode(texture["m_TextureFormat"].AsInt, bytes, width, height);
        return rgba == null ? null : new Picture(width, height, Upright(rgba, width, height));
    }

    static byte[]? Decode(int format, byte[] bytes, int width, int height)
    {
        int pixels = width * height;
        switch (format)
        {
            case Rgba32:
                return bytes.Length < pixels * Channels ? null : bytes[..(pixels * Channels)];
            case Rgb24:
            {
                if (bytes.Length < pixels * 3) return null;
                var opaque = new byte[pixels * Channels];
                for (int p = 0; p < pixels; p++)
                {
                    opaque[p * Channels] = bytes[p * 3];
                    opaque[p * Channels + 1] = bytes[p * 3 + 1];
                    opaque[p * Channels + 2] = bytes[p * 3 + 2];
                    opaque[p * Channels + 3] = Opaque;
                }
                return opaque;
            }
            case Alpha8:
            {
                if (bytes.Length < pixels) return null;
                var grey = new byte[pixels * Channels];
                for (int p = 0; p < pixels; p++)
                {
                    grey[p * Channels] = grey[p * Channels + 1] = grey[p * Channels + 2] = bytes[p];
                    grey[p * Channels + 3] = Opaque;
                }
                return grey;
            }
            case Dxt1:
            case Dxt5:
            {
                byte[] rgba;
                if (format == Dxt1) DxtDecoder.DecompressDXT1<ColorRGBA<byte>, byte>(bytes, width, height, out rgba);
                else DxtDecoder.DecompressDXT5<ColorRGBA<byte>, byte>(bytes, width, height, out rgba);
                return rgba.Length < pixels * Channels ? null : rgba[..(pixels * Channels)];
            }
            default:
                return null;
        }
    }

    static byte[] Upright(byte[] rgba, int width, int height)
    {
        int row = width * Channels;
        var flipped = new byte[rgba.Length];
        for (int y = 0; y < height; y++)
            Array.Copy(rgba, y * row, flipped, (height - 1 - y) * row, row);
        return flipped;
    }
}
