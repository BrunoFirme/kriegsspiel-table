using System.Linq;
using System.Text.Json.Serialization;
using KTable.Core.Protocol;

namespace KTable.Core.Model;

public sealed class SaveFile
{
    public const int CurrentFormat = 34;
    public int Format { get; set; } = CurrentFormat;
    public string Id { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public string Created { get; set; } = "";
    public string Saved { get; set; } = "";
    public SavePreview? Preview { get; set; }
    public GameAccess Access { get; set; } = new();
    public GameState Snapshot { get; set; } = new();
    public List<Command> Commands { get; set; } = new();
    public List<TurnRecord> Turns { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<byte[]>? TurnBlocks { get; set; }
    public string? MirrorOf { get; set; }

    public SaveFile WithTurns(IEnumerable<TurnRecord> turns)
    {
        var filed = new List<TurnRecord>();
        List<byte[]>? blocks = null;
        var index = new Dictionary<TurnBlock, int>(ReferenceEqualityComparer.Instance);
        foreach (var t in turns)
        {
            var f = new TurnRecord { Turn = t.Turn, GameTime = t.GameTime, EndedAt = t.EndedAt, LastSeq = t.LastSeq, Pieces = t.Pieces, Picture = t.Picture };
            if (t.Packed is { } packed) f.Packed = packed;
            else if (t.InBlock is { } block)
            {
                if (!index.TryGetValue(block, out var i))
                {
                    blocks ??= new();
                    i = blocks.Count;
                    blocks.Add(block.Bytes);
                    index[block] = i;
                }
                f.Block = i;
                f.At = t.InBlockAt;
            }
            else if (t.State != null) f.Packed = TurnRecord.Pack(t.State);
            filed.Add(f);
        }
        Turns = filed;
        TurnBlocks = blocks;
        return this;
    }

    public void BindTurns()
    {
        var blocks = TurnBlocks?.Select(b => new TurnBlock(b)).ToList();
        foreach (var t in Turns ??= new())
        {
            if (t.Block is int i)
            {
                if (blocks != null && i >= 0 && i < blocks.Count) t.PutIn(blocks[i], t.At ?? 0);
                t.Block = null;
                t.At = null;
            }
            else t.Normalize();
        }
        TurnBlocks = null;
    }

    public static bool EndsTurn(GameState g, Command c) => c is AdvanceClock || (c is SetClock scl && scl.Turn > g.Clock.Turn);

    public static TurnRecord RecordOf(GameState g, DateTime nowUtc) =>
        new()
        {
            Turn = g.Clock.Turn, GameTime = g.Clock.GameTime, EndedAt = nowUtc.ToString("o"), LastSeq = g.LastSeq,
            Pieces = g.Layer(Layers.Umpire).Count, Packed = TurnRecord.Pack(g),
        };

    public static SaveFile OfTurn(string id, string scenarioId, string created, GameAccess access, IReadOnlyList<TurnRecord> turns, int index) => new SaveFile
    {
        Id = id, ScenarioId = scenarioId, Created = created, Saved = turns[index].EndedAt, Access = access.Clone(),
        Preview = new SavePreview { Turn = turns[index].Turn, GameTime = turns[index].GameTime, Pieces = turns[index].Pieces, Picture = turns[index].Picture },
        Snapshot = turns[index].Unpack(),
    }.WithTurns(turns.Take(index));

    public static SaveFile Mirror(GameState live, GameAccess? access, string created, IEnumerable<TurnRecord> turns, string mirrorOf) => new SaveFile
    {
        ScenarioId = live.ScenarioId, Created = created, Saved = Repeatable.UtcNow.ToString("o"), Access = access?.Clone() ?? new GameAccess(),
        Snapshot = live, MirrorOf = mirrorOf,
    }.WithTurns(turns);

    public static string NewId(DateTime nowUtc) => nowUtc.ToUniversalTime().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..6];
}

public sealed class SavePreview
{
    public int Turn { get; set; }
    public string GameTime { get; set; } = "";
    public int Pieces { get; set; }
    public byte[]? Picture { get; set; }

    public static SavePreview Of(GameState g, byte[]? picture) =>
        new() { Turn = g.Clock.Turn, GameTime = g.Clock.GameTime, Pieces = g.Layer(Layers.Umpire).Count, Picture = picture };
}

public sealed class RunMark
{
    public string Id { get; set; } = "";
    public string Created { get; set; } = "";
    public string? From { get; set; }
    public bool Backfill { get; set; }
}

public sealed class SavesInTrash
{
    public string Scenario { get; set; } = "";
    public string Parent { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class TurnRecord
{
    public int Turn { get; set; }
    public string GameTime { get; set; } = "";
    public string EndedAt { get; set; } = "";
    public long LastSeq { get; set; }
    public int Pieces { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public byte[]? Packed { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public GameState? State { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public byte[]? Picture { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Block { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? At { get; set; }
    [JsonIgnore] public TurnBlock? InBlock { get; private set; }
    [JsonIgnore] public int InBlockAt { get; private set; }
    [JsonIgnore] public bool HasTable => Packed != null || InBlock != null || State != null;

    public void PutIn(TurnBlock block, int at)
    {
        InBlockAt = at;
        InBlock = block;
        Packed = null;
    }

    public byte[] StateBytes()
    {
        if (Packed is { } packed)
        {
            using var z = new System.IO.Compression.GZipStream(new MemoryStream(packed), System.IO.Compression.CompressionMode.Decompress);
            using var plain = new MemoryStream();
            z.CopyTo(plain);
            return plain.ToArray();
        }
        if (InBlock is { } block) return block.Member(InBlockAt);
        if (State != null) return Json.SerializeBytes(State);
        throw new InvalidDataException($"the table at the end of turn {Turn} was not kept in this save");
    }

    public GameState Unpack()
    {
        if (!HasTable) return new GameState();
        return Json.Deserialize<GameState>(StateBytes());
    }

    public static byte[] Pack(GameState g)
    {
        using var packed = new MemoryStream();
        using (var z = new System.IO.Compression.GZipStream(packed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            z.Write(Json.SerializeBytes(g));
        return packed.ToArray();
    }

    public void Normalize()
    {
        if (State == null) return;
        if (Packed == null) { Packed = Pack(State); Pieces = State.Layer(Layers.Umpire).Count; }
        State = null;
    }
}

public sealed class SaveTooNewException(string message) : IOException(message);

public sealed class TurnInfo
{
    public int Index { get; set; }
    public int Turn { get; set; }
    public string GameTime { get; set; } = "";
    public string EndedAt { get; set; } = "";
    public int Pieces { get; set; }
    public string? SaveName { get; set; }

    public bool IsTurn => Index >= 0 && SaveName == null;
}

public sealed class TileIndex
{
    public int TileSize { get; set; } = 256;
    public int SourceW { get; set; }
    public int SourceH { get; set; }
    public string Ext { get; set; } = "webp";
    public List<TileLevel> Levels { get; set; } = new();
}

public sealed class TileLevel
{
    public int Level { get; set; }
    public int W { get; set; }
    public int H { get; set; }
    public int Cols { get; set; }
    public int Rows { get; set; }
}

public sealed class SaveHeldException(string message) : Exception(message);
