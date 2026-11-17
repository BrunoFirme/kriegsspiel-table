using System.Text.Json.Serialization;
using KTable.Core.Model;

namespace KTable.Core.Protocol;

public static class Earshot
{
    public const int Whisper = 0, Say = 1, Shout = 2, MostHearers = 200, MostLength = 500;
    static readonly double[] Metres = { 2, 15, 60 };
    static readonly string[] Verbs = { "whispers", "says", "shouts" };

    public static int Loudness(int loud) => Math.Clamp(loud, Whisper, Shout);

    public static string Verb(int loud) => Verbs[Loudness(loud)];

    public static double Reach(int loud, Calibration calibration) => Metres[Loudness(loud)] * (calibration.UnitsPerMeter is > 0 and var perMetre ? perMetre : 1);

    static double Apart(Vec2 a, Vec2 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    public static List<PieceInstance> Hearers(GameState g, PieceInstance speaker, int loud)
    {
        double reach = Reach(loud, g.Calibration);
        return g.Layer(Layers.Umpire).Where(p => p.Id != speaker.Id && Apart(p.Pos, speaker.Pos) <= reach).OrderBy(p => Apart(p.Pos, speaker.Pos)).Take(MostHearers).ToList();
    }
}

public sealed class Speak : Command
{
    public string Layer { get; set; } = Layers.Umpire;
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Loud { get; set; } = Earshot.Say;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Name { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? By { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Vec2? Pos { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Heard { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Hearers { get; set; }
    public override Sender SentBy() => Sender.UmpireAlone;
    public override bool OnUndoStack() => false;
    public override Shown ShownToObservers() => Shown.Addressee;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [(Layer, Id)];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];

    public bool HeardBy(string person) =>
        string.Equals(By, person, StringComparison.OrdinalIgnoreCase) || Hearers != null && Hearers.Contains(person, StringComparer.OrdinalIgnoreCase);

    public Speak Overheard() => new() { Seq = Seq, Actor = Actor, At = At, Layer = Layer, Id = Id, Text = Text, Loud = Loud, Name = Name, Pos = Pos };
}

public sealed class Note : Command
{
    public string Text { get; set; } = "";
    public Vec2 Pos { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? By { get; set; }
    public override Sender SentBy() => Sender.UmpireAlone;
    public override bool OnUndoStack() => false;
    public override Shown ShownToObservers() => Shown.UmpiresOnly;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}

public sealed class Hotline : Command
{
    public string Layer { get; set; } = Layers.Umpire;
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Name { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? By { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Readers { get; set; }
    public override Sender SentBy() => Sender.UmpireAlone;
    public override bool OnUndoStack() => false;
    public override Shown ShownToObservers() => Shown.Addressee;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];

    public bool ReadBy(PeerInfo peer) =>
        string.Equals(By, peer.Name, StringComparison.OrdinalIgnoreCase) || Readers != null && Readers.Contains(peer.Name, StringComparer.OrdinalIgnoreCase);
}
