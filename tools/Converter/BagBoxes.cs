using System.Text.RegularExpressions;
using KTable.Client.Effects;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter;

public sealed partial class BagBoxes
{
    public const string Box = "box", Chest = "chest", Crate = "crate";
    public const int FewPieces = 12, ManyPieces = 120;
    const double Neutral = 0.08;

    static readonly Dictionary<string, string> ByWord = Words(Chest, "chest", "chests", "trunk", "trunks", "strongbox", "treasure", "pay", "gold", "reserve", "reserves")
        .Concat(Words(Crate, "crate", "crates", "supply", "supplies", "stores", "ammo", "ammunition", "baggage", "building", "buildings", "house", "houses",
            "terrain", "scenery", "airplane", "airplanes", "aircraft", "plane", "planes", "ship", "ships", "vehicle", "vehicles", "wagon", "wagons"))
        .Concat(Words(Box, "box", "boxes", "token", "tokens", "counter", "counters", "marker", "markers", "dice", "die", "card", "cards", "chit", "chits",
            "order", "orders", "despatch", "despatches", "dispatch", "note", "notes"))
        .ToDictionary(p => p.Key, p => p.Value);

    static IEnumerable<KeyValuePair<string, string>> Words(string box, params string[] words) => words.Select(w => KeyValuePair.Create(w, box));

    readonly Dictionary<string, PieceDef> _defs;

    BagBoxes(Dictionary<string, PieceDef> defs) => _defs = defs;

    public static BagBoxes Load(string packagesRoot)
    {
        var path = Path.Combine(packagesRoot, EffectKinds.StandardPack, "pack.json");
        var ids = new[] { Box, Chest, Crate }.Select(IdOf).ToHashSet();
        var defs = File.Exists(path)
            ? Json.Load<PackManifest>(path).Pieces.Where(p => ids.Contains(p.Id) && p.Kind == "model").ToDictionary(p => p.Id)
            : new Dictionary<string, PieceDef>();
        return new BagBoxes(defs);
    }

    public static string IdOf(string box) => EffectKinds.StandardPack + "." + box;

    public bool Installed => _defs.Count == 3;

    public PieceDef? For(ModuleObj bag) => Installed && bag.Name == "Bag" ? _defs[IdOf(Pick(bag))] : null;

    public static string Pick(ModuleObj bag)
    {
        foreach (Match w in WordRx().Matches(bag.Nickname.ToLowerInvariant()))
            if (ByWord.TryGetValue(w.Value, out var box)) return box;
        int held = bag.Contained.Count();
        return held <= FewPieces ? Box : held >= ManyPieces ? Crate : Chest;
    }

    public static string? Tint(ModuleObj bag)
    {
        if (bag.J["ColorDiffuse"] is not System.Text.Json.Nodes.JsonObject c) return null;
        double V(string k) => Math.Clamp(ModuleValues.Finite(c[k], 1), 0, 1);
        double r = V("r"), g = V("g"), b = V("b");
        return Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) < Neutral ? null : bag.Color;
    }

    [GeneratedRegex("[a-z]+")] private static partial Regex WordRx();
}
