using System.Text.Json.Serialization;

namespace KTable.Core.Model;

public sealed class CrowdScript
{
    public const string Prefix = "crowd:";

    public string Script { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? State { get; set; }

    public CrowdScript Clone() => (CrowdScript)MemberwiseClone();

    public static bool Is(string id) => id.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Of(string owner) => Prefix + owner;

    public static string OwnerOf(string id) => id[Prefix.Length..];

    public static bool Owns(GameState g, string owner) =>
        g.Built.Any(b => b.Id == owner && b.Kind == BuiltKinds.Settlement) || g.Crowds?.Made?.Any(m => m.Id == owner) == true;

    public static string NameOf(GameState g, string owner)
    {
        if (g.Crowds?.Made?.FirstOrDefault(m => m.Id == owner) is { } made) return made.Name;
        var towns = g.Built.Where(b => b.Kind == BuiltKinds.Settlement).ToList();
        int at = towns.FindIndex(b => b.Id == owner);
        return at < 0 ? g.Built.FirstOrDefault(b => b.Id == owner)?.Name ?? owner : towns[at].Name ?? $"Town {at + 1}";
    }
}
