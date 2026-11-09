using System.IO.Compression;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.Client;

public static class Framing
{
    public const int PackAbove = 64 * 1024;
    public const long MaxUnpacked = 1L << 30;
    public const long MaxFromStranger = 64 * 1024;

    public static long MostFrom(PeerInfo? sender) => sender != null && Visibility.IsUmpire(sender.Role) ? MaxUnpacked : MaxFromStranger;

    public static byte[] ForWire(byte[] json) => json.Length > PackAbove ? Pack(json) : json;

    public static byte[] Pack(byte[] plain)
    {
        using var packed = new MemoryStream();
        using (var z = new GZipStream(packed, CompressionLevel.Fastest, leaveOpen: true)) z.Write(plain);
        return packed.ToArray();
    }

    public static byte[] Unpack(byte[] data, long max = MaxUnpacked)
    {
        if (data.Length < 2 || data[0] != 0x1F || data[1] != 0x8B)
            return data.Length <= max ? data : throw new InvalidDataException($"a message of {data.Length} bytes, more than the {max} allowed");
        using var z = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var plain = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = z.Read(buf, 0, buf.Length)) > 0)
        {
            if (plain.Length + n > max) throw new InvalidDataException($"a packed message that unpacks to more than the {max} bytes allowed");
            plain.Write(buf, 0, n);
        }
        return plain.ToArray();
    }
}
