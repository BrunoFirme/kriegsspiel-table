using System.Text.Json.Serialization;
using KTable.Core.Protocol;

namespace KTable.Core.Model;

public sealed class RecordEntry
{
    public const string Talk = "talk", Dice = "dice", World = "world", Speech = "speech", Noted = "note", Direct = "hotline";
    public const string ToUmpires = "u:", ToOne = "n:";
    public const string Table = "table";
    public const int MostKept = 20000, MostSent = 2000, TableTurnsKept = 3;

    public string Kind { get; set; } = Talk;
    public long At { get; set; }
    public long Seconds { get; set; }
    public int Turn { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Who { get; set; }
    public string Text { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Reach { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Color { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Piece { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PieceName { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Vec2? Pos { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ToPieceName { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Hearers { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Loud { get; set; }

    bool Names(PeerInfo peer) =>
        string.Equals(Who, peer.Name, StringComparison.OrdinalIgnoreCase) || Hearers != null && Hearers.Contains(peer.Name, StringComparer.OrdinalIgnoreCase);

    public bool SeenBy(PeerInfo peer)
    {
        if (Kind == Direct) return Visibility.IsLead(peer.Role) || Names(peer);
        if (Visibility.IsUmpire(peer.Role)) return true;
        if (!Visibility.Watches(peer.Role)) return false;
        if (Kind == Speech) return Names(peer);
        if (Reach == null || string.Equals(Who, peer.Name, StringComparison.OrdinalIgnoreCase)) return true;
        return Reach.StartsWith(ToOne, StringComparison.Ordinal) && string.Equals(Reach[ToOne.Length..], peer.Name, StringComparison.OrdinalIgnoreCase);
    }

    public string? ToName => Reach != null && Reach.StartsWith(ToOne, StringComparison.Ordinal) ? Reach[ToOne.Length..] : null;

    public static string DiceWords(RollDice d)
    {
        string what = d.PieceIds.Count > 0 ? (d.Label.Length > 0 ? d.Label : $"{d.Count} dice") + " on the table" : $"{d.Count}d{d.Sides}" + (d.Label.Length > 0 ? " for " + d.Label : "");
        string sum = d.Results.Count > 1 ? $" (sum {d.Results.Sum()})" : "";
        return $"rolled {what}: {string.Join(", ", d.Results)}{sum}";
    }

    public static RecordEntry? Of(Command c, GameClock clock)
    {
        var entry = c switch
        {
            Say s => new RecordEntry
            {
                Kind = Talk, Who = s.By, Text = s.Text, Color = s.Color,
                Reach = s.To == null ? null : s.To == Say.ToUmpires ? ToUmpires : s.ToName != null ? ToOne + s.ToName : ToUmpires,
            },
            RollDice d => new RecordEntry
            {
                Kind = Dice, Who = d.ActorName, Text = DiceWords(d), Reach = d.Layer != null && d.PieceIds.Count > 0 || d.Open ? null : ToUmpires,
            },
            AdvanceClock { Back: false } => new RecordEntry { Kind = World, Text = $"Turn {clock.Turn} began" },
            Speak sp => new RecordEntry { Kind = Speech, Who = sp.By, Text = sp.Text, Piece = sp.Id, PieceName = sp.Name, ToPieceName = sp.ToName, Pos = sp.Pos, Hearers = sp.Hearers, Loud = sp.Loud },
            Note n => new RecordEntry { Kind = Noted, Who = n.By, Text = n.Text, Pos = n.Pos, Reach = ToUmpires },
            Hotline h => new RecordEntry { Kind = Direct, Who = h.By, Text = h.Text, Piece = h.Id, PieceName = h.Name, Hearers = h.Readers },
            OrderPiece { Name: { } name } o => new RecordEntry { Kind = World, Text = OrderWords(o, name, clock), Piece = o.Id, PieceName = name, Reach = ToUmpires },
            _ => null,
        };
        if (entry == null) return null;
        entry.At = c.At;
        entry.Seconds = clock.Now;
        entry.Turn = clock.Turn;
        return entry;
    }

    public static string OrderWords(OrderPiece o, string name, GameClock clock) => o.Order is { } order
        ? $"{name} set off on an order, due {clock.Cal.Words((long)Math.Ceiling(order.Arrives))}"
        : $"{name}'s order ended";

    public static RecordEntry Happened(string text, GameClock clock, long at) => new() { Kind = World, Text = text, At = at, Seconds = clock.Now, Turn = clock.Turn };
}
