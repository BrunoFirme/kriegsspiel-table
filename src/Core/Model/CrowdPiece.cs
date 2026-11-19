using System.Globalization;
using System.Text.Json.Serialization;

namespace KTable.Core.Model;

public static class CrowdPiece
{
    public const string DefId = "std.crowds.person", Made = "crowd", Layer = "crowd";
    public static readonly PackManifest Pack = new() { Id = "std.crowds", Name = "Crowds" };
    public static readonly PieceDef Def = new() { Id = DefId, Name = "Crowd person", Kind = "model", Mesh = Made, Rig = Made, Size = new(0.5, 0.3), Thickness = 1.7, Tags = [PieceDef.LifeSizeTag] };

    public static CrowdLook? LookOf(PieceInstance piece, PieceDef? def) => piece.Look ?? (def?.Mesh == Made ? CrowdWardrobe.Fresh() : null);

    public static string Key(string thing, int person) => thing + ":" + person.ToString(CultureInfo.InvariantCulture);

    public static (string Thing, int Person)? Parse(string? key)
    {
        int at = key?.LastIndexOf(':') ?? -1;
        return at > 0 && int.TryParse(key![(at + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int person) ? (key[..at], person) : null;
    }
}

public static class CrowdFates
{
    public const string Down = "down", Dead = "dead", Held = "held", Gone = "gone", Kept = "kept";
    public static readonly IReadOnlyList<string> All = [Down, Dead, Held, Gone, Kept];
}

public sealed class CrowdFate
{
    public string State { get; set; } = CrowdFates.Down;
    public long At { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? By { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? Until { get; set; }

    public bool Holds(long at) => At <= at && (Until is not { } until || at < until);

    public CrowdFate Clone() => (CrowdFate)MemberwiseClone();
}
