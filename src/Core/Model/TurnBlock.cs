using System.Buffers.Binary;
using System.IO.Compression;

namespace KTable.Core.Model;

public sealed class TurnBlock
{
    public const int Size = 10;

    public TurnBlock(byte[] bytes) { Bytes = bytes; }

    public byte[] Bytes { get; }

    public static TurnBlock Of(IReadOnlyList<TurnRecord> turns)
    {
        using var packed = new MemoryStream();
        using (var z = new BrotliStream(packed, CompressionLevel.Optimal, leaveOpen: true))
        {
            Span<byte> length = stackalloc byte[4];
            foreach (var t in turns)
            {
                var json = t.StateBytes();
                BinaryPrimitives.WriteInt32LittleEndian(length, json.Length);
                z.Write(length);
                z.Write(json);
            }
        }
        return new TurnBlock(packed.ToArray());
    }

    public byte[] Member(int at)
    {
        using var z = new BrotliStream(new MemoryStream(Bytes), CompressionMode.Decompress);
        var length = new byte[4];
        for (int i = 0; ; i++)
        {
            z.ReadExactly(length);
            int n = BinaryPrimitives.ReadInt32LittleEndian(length);
            if (n < 0) throw new InvalidDataException("a block of turns is damaged");
            var json = new byte[n];
            z.ReadExactly(json);
            if (i == at) return json;
        }
    }
}

public sealed class TurnSealer
{
    readonly Dictionary<TurnRecord, (TurnRecord[] Members, Task<TurnBlock> Packing)> _packing = new(ReferenceEqualityComparer.Instance);

    public void Seal(IReadOnlyList<TurnRecord> turns)
    {
        var wanted = new HashSet<TurnRecord>(ReferenceEqualityComparer.Instance);
        for (int start = 0; start + TurnBlock.Size <= turns.Count; start += TurnBlock.Size)
        {
            var group = new TurnRecord[TurnBlock.Size];
            for (int i = 0; i < group.Length; i++) group[i] = turns[start + i];
            if (group.Any(t => t.InBlock != null)) continue;
            wanted.Add(group[0]);
            if (!_packing.TryGetValue(group[0], out var p) || !p.Members.SequenceEqual(group, ReferenceEqualityComparer.Instance))
            {
                _packing[group[0]] = (group, Task.Run(() => TurnBlock.Of(group)));
                continue;
            }
            if (!p.Packing.IsCompleted) continue;
            _packing.Remove(group[0]);
            if (!p.Packing.IsCompletedSuccessfully) continue;
            for (int i = 0; i < group.Length; i++) group[i].PutIn(p.Packing.Result, i);
        }
        foreach (var gone in _packing.Keys.Where(k => !wanted.Contains(k)).ToList()) _packing.Remove(gone);
    }

    public bool WaitForAll(TimeSpan wait)
    {
        try { return Task.WaitAll(_packing.Values.Select(p => (Task)p.Packing).ToArray(), wait); }
        catch (AggregateException) { return true; }
    }
}
