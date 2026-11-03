using System.Buffers.Binary;

namespace KTable.Core.Protocol;

public static class BulkFrame
{
    const byte MarkK = (byte)'K', MarkB = (byte)'B';
    const int HeadAt = 6;
    const int MaxHeadBytes = 64 * 1024;
    public const int MaxDataBytes = 1024 * 1024;

    public static byte[] Encode(FilePiece piece)
    {
        var head = Json.SerializeBytes<Message>(piece);
        var frame = new byte[HeadAt + head.Length + piece.Data.Length];
        frame[0] = MarkK; frame[1] = MarkB;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(2), head.Length);
        head.CopyTo(frame, HeadAt);
        piece.Data.CopyTo(frame, HeadAt + head.Length);
        return frame;
    }

    public static FilePiece Decode(byte[] frame)
    {
        if (frame.Length < HeadAt || frame[0] != MarkK || frame[1] != MarkB) throw new InvalidDataException("not a file piece");
        var headBytes = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(2));
        if (headBytes is <= 0 or > MaxHeadBytes || HeadAt + headBytes > frame.Length) throw new InvalidDataException("a file piece with a broken header");
        var dataBytes = frame.Length - HeadAt - headBytes;
        if (dataBytes > MaxDataBytes) throw new InvalidDataException($"a file piece of {dataBytes} bytes, more than the {MaxDataBytes} allowed");
        if (Json.Deserialize<Message>(frame.AsSpan(HeadAt, headBytes)) is not FilePiece piece) throw new InvalidDataException("a file piece that is something else");
        piece.Data = frame[(HeadAt + headBytes)..];
        return piece;
    }
}
