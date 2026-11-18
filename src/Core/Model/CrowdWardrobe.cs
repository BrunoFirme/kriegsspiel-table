using E = KTable.Core.Model.CrowdEntry;
using S = KTable.Core.Model.CrowdSlots;

namespace KTable.Core.Model;

public sealed record LookOffer(IReadOnlyDictionary<string, IReadOnlyList<string>> Kinds, IReadOnlyDictionary<string, IReadOnlyList<string>>? Colours, IReadOnlyList<string> Items)
{
    public bool Free => Colours == null;
}

public static class CrowdWardrobe
{
    public const double LeastHeightM = 0.8, MostHeightM = 2.3, LeastBuild = 0.6, MostBuild = 1.6, MostStoop = 1;
    const string Linen = "#d9cfb8", Undyed = "#8c7a5b", Leather = "#5b4636", Skin = "#c99a78", Hair = "#5a4632", Eye = "#4a5a6a", Lip = "#b0705f";
    static readonly string[] Greys = ["#8f8a83", "#aba69d", "#c8c3b9"];
    static readonly string[] WealthOrder = [E.Poor, E.Middle, E.Rich];

    public static CrowdLook Fresh()
    {
        var look = new CrowdLook { Sex = E.Man, Band = E.Adult, HeightM = CrowdBaseline.Person.Heights![E.Man] * CrowdBaseline.BandHeight[E.Adult] };
        foreach (var (part, kind, slot) in new[] { (Wardrobe.Torso, "shirt", S.Linen), (Wardrobe.Sleeves, "long", S.Linen), (Wardrobe.Thigh, "trousers", S.Legs),
            (Wardrobe.Shin, "trousers", S.Legs), (Wardrobe.Feet, "shoes", S.Trim), (Wardrobe.Hair, "short", S.Hair) })
            look.Wear(new Garment { Part = part, Kind = kind, Slot = slot });
        foreach (var slot in S.All) look.Colours[slot] = Linen;
        (look.Colours[S.Skin], look.Colours[S.Hair], look.Colours[S.Brow], look.Colours[S.Eye], look.Colours[S.Lip]) = (Skin, Hair, Hair, Eye, Lip);
        (look.Colours[S.Legs], look.Colours[S.Outer], look.Colours[S.Trim]) = (Undyed, Undyed, Leather);
        return look;
    }

    public static LookOffer Offer(Nationality? flat, string? wealth)
    {
        if (flat == null) return new(Wardrobe.Kinds, null, CrowdBaseline.Items.Select(i => i.Id).ToList());
        var kinds = new Dictionary<string, HashSet<string>>();
        foreach (var rule in flat.Dress ?? [])
        {
            if (wealth != null && rule.Wealth != null && !rule.Wealth.Contains(wealth)) continue;
            foreach (var g in rule.Wear ?? [])
                foreach (var kind in g.Kind.Split('|'))
                    (kinds.TryGetValue(g.Part, out var set) ? set : kinds[g.Part] = new()).Add(kind);
        }
        int most = wealth == null ? WealthOrder.Length - 1 : Array.IndexOf(WealthOrder, wealth);
        var dyes = WealthOrder.Take(most + 1).SelectMany(w => flat.Dyes?.GetValueOrDefault(w) ?? []).Concat(flat.ArmyCoats ?? []).Concat(flat.Materials?.Values ?? Enumerable.Empty<string>())
            .Append(Linen).Append(Undyed).Distinct().ToList();
        var hairs = (flat.Hairs ?? [Hair]).Concat(Greys).Distinct().ToList();
        var colours = S.All.ToDictionary(slot => slot, slot => (IReadOnlyList<string>)(slot switch
        {
            S.Skin => flat.Skins ?? [Skin],
            S.Hair or S.Brow => hairs,
            S.Eye => flat.Eyes ?? [Eye],
            _ => dyes,
        }));
        return new(kinds.ToDictionary(k => k.Key, k => (IReadOnlyList<string>)k.Value.Order().ToList()), colours, CrowdBaseline.Items.Select(i => i.Id).ToList());
    }

    public static string? Problem(CrowdLook look)
    {
        if (look.Sex is not (E.Man or E.Woman) || !CrowdBaseline.Bands.Contains(look.Band) || !CrowdLook.Faces.Contains(look.Face)) return "no such body";
        if (look.HeightM is < LeastHeightM or > MostHeightM || look.Build is < LeastBuild or > MostBuild || look.Stoop is < 0 or > MostStoop) return "a body out of range";
        if (look.Worn.Any(g => !Wardrobe.Kinds.TryGetValue(g.Part, out var kinds) || !kinds.Contains(g.Kind)) || look.Worn.GroupBy(g => g.Part).Any(p => p.Count() > 1)) return "no such garment";
        if (look.Worn.Any(g => g.Slot != null && !S.All.Contains(g.Slot) && !CrowdBaseline.Materials.Contains(g.Slot))) return "no such colour slot";
        if (look.Colours.Count > S.All.Count + CrowdBaseline.Materials.Count || look.Colours.Values.Any(c => !Hex(c))) return "colours are #rrggbb";
        if (look.Details?.Any(d => !CrowdDetails.All.Contains(d)) == true) return "no such detail";
        return look.Item != null && CrowdBaseline.Items.All(i => i.Id != look.Item) ? "no such thing to carry" : null;
    }

    public static bool Fits(CrowdLook look, LookOffer offer) =>
        look.Worn.All(g => offer.Kinds.TryGetValue(g.Part, out var kinds) && kinds.Contains(g.Kind))
        && (offer.Colours == null || look.Colours.All(c => !offer.Colours.TryGetValue(c.Key, out var allowed) || allowed.Contains(c.Value, StringComparer.OrdinalIgnoreCase)))
        && (look.Item == null || offer.Items.Contains(look.Item));

    static bool Hex(string c) => c.Length == 7 && c[0] == '#' && c.Skip(1).All(Uri.IsHexDigit);
}
