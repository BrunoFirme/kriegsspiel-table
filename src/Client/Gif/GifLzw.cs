namespace KTable.Client.Gif;

static class GifLzw
{
    public const int MinCodeSize = 8;
    const int ClearCode = 1 << MinCodeSize, EndCode = ClearCode + 1, FirstFree = EndCode + 1;
    const int MostCodeSize = 12, MostCode = (1 << MostCodeSize) - 1;
    const int TableSize = 8209;
    const int BlockMost = 255;

    public static void Write(Stream output, ReadOnlySpan<byte> indices)
    {
        output.WriteByte(MinCodeSize);
        var bits = new BitBlocks(output);
        var keys = new int[TableSize];
        var codes = new int[TableSize];
        int codeSize = MinCodeSize + 1, next = FirstFree;
        bits.Put(ClearCode, codeSize);
        if (indices.Length == 0)
        {
            bits.Put(EndCode, codeSize);
            bits.Close();
            return;
        }
        int prefix = indices[0];
        for (int i = 1; i < indices.Length; i++)
        {
            int k = indices[i];
            int key = (prefix << MinCodeSize | k) + 1;
            int slot = (int)((uint)key % TableSize);
            while (keys[slot] != 0 && keys[slot] != key) slot = (slot + 1) % TableSize;
            if (keys[slot] == key)
            {
                prefix = codes[slot];
                continue;
            }
            bits.Put(prefix, codeSize);
            keys[slot] = key;
            codes[slot] = next;
            if (next >= 1 << codeSize) codeSize++;
            next++;
            if (next > MostCode)
            {
                bits.Put(ClearCode, codeSize);
                Array.Clear(keys);
                codeSize = MinCodeSize + 1;
                next = FirstFree;
            }
            prefix = k;
        }
        bits.Put(prefix, codeSize);
        bits.Put(EndCode, codeSize);
        bits.Close();
    }

    sealed class BitBlocks
    {
        readonly Stream _out;
        readonly byte[] _block = new byte[BlockMost];
        int _filled;
        ulong _pending;
        int _pendingBits;

        public BitBlocks(Stream output) { _out = output; }

        public void Put(int code, int size)
        {
            _pending |= (ulong)code << _pendingBits;
            _pendingBits += size;
            while (_pendingBits >= 8)
            {
                Add((byte)_pending);
                _pending >>= 8;
                _pendingBits -= 8;
            }
        }

        void Add(byte b)
        {
            _block[_filled++] = b;
            if (_filled == BlockMost) Flush();
        }

        void Flush()
        {
            if (_filled == 0) return;
            _out.WriteByte((byte)_filled);
            _out.Write(_block, 0, _filled);
            _filled = 0;
        }

        public void Close()
        {
            if (_pendingBits > 0) Add((byte)_pending);
            _pending = 0;
            _pendingBits = 0;
            Flush();
            _out.WriteByte(0);
        }
    }
}
