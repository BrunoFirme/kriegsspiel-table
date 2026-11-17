using System.Text.Json.Serialization;
using KTable.Core.Model;

namespace KTable.Core.Protocol;

public sealed class Speak : Command
{
    public string Layer { get; set; } = Layers.Umpire;
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public int Loud { get; set; } = Earshot.Say;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Name { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? To { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ToName { get; set; }
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

    public Speak Overheard() => new() { Seq = Seq, Actor = Actor, At = At, Layer = Layer, Id = Id, Text = Text, Loud = Loud, Name = Name, To = To, ToName = ToName, Pos = Pos };
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

public sealed class SetDirectors : Command
{
    public const int MostNames = 40, MostNameLength = 80;

    public string Id { get; set; } = "";
    public List<string> Names { get; set; } = new();
    public override Sender SentBy() => Sender.Lead;
    public override bool OnUndoStack() => true;
    public override Shown ShownToObservers() => Shown.UmpiresOnly;
    public override IEnumerable<(string Layer, string Id)> ZonePieces() => [];
    public override IEnumerable<(string Layer, Vec2 Pos)> ZonePoints() => [];
    public override IEnumerable<string> ScriptsToReload() => [];
}
