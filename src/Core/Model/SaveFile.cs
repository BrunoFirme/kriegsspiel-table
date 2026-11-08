using KTable.Core.Protocol;
using System.Linq;

namespace KTable.Core.Model;

public sealed class SaveFile
{
    public const int CurrentFormat = 28;
    public int Format { get; set; } = CurrentFormat;
    public string ScenarioId { get; set; } = "";
    public string Created { get; set; } = "";
    public string Saved { get; set; } = "";
    public SavePreview? Preview { get; set; }
    public GameAccess Access { get; set; } = new();
    public GameState Snapshot { get; set; } = new();
    public List<Command> Commands { get; set; } = new();
    public List<TurnRecord> Turns { get; set; } = new();
    public string? MirrorOf { get; set; }

    public static bool EndsTurn(GameState g, Command c) => c is AdvanceClock || (c is SetClock scl && scl.Turn > g.Clock.Turn);

    public static TurnRecord RecordOf(GameState g, DateTime nowUtc) =>
        new()
        {
            Turn = g.Clock.Turn, GameTime = g.Clock.GameTime, EndedAt = nowUtc.ToString("o"), LastSeq = g.LastSeq,
            Pieces = g.Layer(Layers.Umpire).Count, Packed = TurnRecord.Pack(g),
        };

    public static SaveFile OfTurn(string scenarioId, string created, GameAccess access, IReadOnlyList<TurnRecord> turns, int index, byte[]? picture) => new()
    {
        ScenarioId = scenarioId, Created = created, Saved = turns[index].EndedAt, Access = access.Clone(),
        Preview = new SavePreview { Turn = turns[index].Turn, GameTime = turns[index].GameTime, Pieces = turns[index].Pieces, Picture = picture },
        Snapshot = turns[index].Unpack(), Turns = turns.Take(index).ToList(),
    };

    public static SaveFile Mirror(GameState live, GameAccess? access, string created, IEnumerable<TurnRecord> turns, string mirrorOf) => new()
    {
        ScenarioId = live.ScenarioId, Created = created, Saved = Repeatable.UtcNow.ToString("o"), Access = access?.Clone() ?? new GameAccess(),
        Snapshot = live, Turns = turns.ToList(), MirrorOf = mirrorOf,
    };
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
    public byte[]? Packed { get; set; }
    public GameState? State { get; set; }

    public GameState Unpack()
    {
        if (Packed != null)
        {
            using var z = new System.IO.Compression.GZipStream(new MemoryStream(Packed), System.IO.Compression.CompressionMode.Decompress);
            using var plain = new MemoryStream();
            z.CopyTo(plain);
            return Json.Deserialize<GameState>(plain.GetBuffer().AsSpan(0, (int)plain.Length));
        }
        return State != null ? Json.Clone(State) : new GameState();
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
