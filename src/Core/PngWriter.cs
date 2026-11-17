using System.Buffers.Binary;
using System.IO.Compression;

namespace KTable.Core;

public sealed class PngWriter : IDisposable
{
    static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    const byte EightBits = 8, Rgba = 6, SubFilter = 1;
    const int Channels = 4, HeaderBytes = 13, ChunkBytes = 1 << 16;

    readonly Stream _out;
    readonly int _w;
    readonly Chunks _data;
    readonly ZLibStream _packed;
    readonly byte[] _line;
    int _rowsLeft;
    bool _finished;

    public PngWriter(Stream output, int w, int h)
    {
        if (w <= 0 || h <= 0) throw new ArgumentException("picture has no size");
        (_out, _w, _rowsLeft) = (output, w, h);
        _line = new byte[1 + w * Channels];
        output.Write(Signature);
        var header = new byte[HeaderBytes];
        BinaryPrimitives.WriteInt32BigEndian(header, w);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), h);
        (header[8], header[9]) = (EightBits, Rgba);
        Chunk(output, "IHDR", header);
        _data = new Chunks(output);
        _packed = new ZLibStream(_data, CompressionLevel.Optimal, leaveOpen: true);
    }

    public void Rows(byte[] rgba, int rows)
    {
        if (rows > _rowsLeft) throw new InvalidOperationException("more rows than the picture has");
        _rowsLeft -= rows;
        int wide = _w * Channels;
        for (int r = 0; r < rows; r++)
        {
            int at = r * wide;
            _line[0] = SubFilter;
            for (int i = 0; i < wide; i++) _line[1 + i] = (byte)(rgba[at + i] - (i < Channels ? 0 : rgba[at + i - Channels]));
            _packed.Write(_line);
        }
    }

    public void Finish()
    {
        if (_rowsLeft != 0) throw new InvalidOperationException($"{_rowsLeft} rows of the picture were never written");
        _finished = true;
        _packed.Dispose();
        _data.Flush();
        Chunk(_out, "IEND", []);
    }

    public void Dispose()
    {
        if (!_finished) _packed.Dispose();
    }

    static void Chunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4], name = stackalloc byte[4];
        for (int i = 0; i < name.Length; i++) name[i] = (byte)type[i];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        output.Write(word);
        output.Write(name);
        output.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(word, ~Crc(Crc(uint.MaxValue, name), data));
        output.Write(word);
    }

    static readonly uint[] CrcTable = MakeCrcTable();

    static uint[] MakeCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < table.Length; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    sealed class Chunks(Stream output) : Stream
    {
        readonly byte[] _held = new byte[ChunkBytes];
        int _count;

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            while (buffer.Length > 0)
            {
                int take = Math.Min(buffer.Length, _held.Length - _count);
                buffer[..take].CopyTo(_held.AsSpan(_count));
                _count += take;
                buffer = buffer[take..];
                if (_count == _held.Length) Flush();
            }
        }

        public override void Flush()
        {
            if (_count == 0) return;
            Chunk(output, "IDAT", _held.AsSpan(0, _count));
            _count = 0;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
