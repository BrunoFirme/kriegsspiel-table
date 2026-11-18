using S = KTable.Core.Model.CrowdSlots;
using W = KTable.Core.Model.Wardrobe;

namespace KTable.Core.Model;

public static class CrowdDetails
{
    public const string Curls = "curls", Ribbon = "ribbon", Buttons = "buttons", Flaps = "flaps", Lapels = "lapels", Cuffs = "cuffs", Cross = "cross", Buckles = "buckles";
    public const string HatTrim = "hatTrim", HatBand = "hatBand", HoodTail = "hoodTail", BorkFlap = "borkFlap", TrimEdge = "trimEdge", FurTrim = "furTrim";
    public const string LongSleeves = "longSleeves", ObiKnot = "obiKnot", Clavi = "clavi", Pouches = "pouches", Pouch = "pouch", LowBelt = "lowBelt", Wings = "wings";

    public static readonly IReadOnlyList<string> All =
        [Curls, Ribbon, Buttons, Flaps, Lapels, Cuffs, Cross, Buckles, HatTrim, HatBand, HoodTail, BorkFlap, TrimEdge, FurTrim, LongSleeves, ObiKnot, Clavi, Pouches, Pouch, LowBelt, Wings];
}

public static class CrowdHolds
{
    public const int None = 0, Work = 1, Spear = 2, Gun = 3, Shield = 4, Hoe = 5, Cloak = 6;
}

sealed record Robe(bool FromShoulder, float Hem, float Flare, string Slot);

sealed record Apron(float Top, float Bottom, string Slot);

sealed class Kit
{
    public Robe? Robe;
    public float? CoatHem;
    public bool Paenula;
    public string Torso = S.Outer, Sleeves = S.Outer, Thigh = S.Legs, Shin = S.Legs, Feet = S.Accent, Cuffs = S.Under, HatSlot = S.Hat;
    public bool Baggy, Rolled, WideSleeves, Obi, Cravat, Fichu, Collar, Tie;
    public Apron? Apron;
    public string? Belt, Hat, Hair = "short", Beard, Item, Side, Gun;
    public int Hold;
    public readonly HashSet<string> Details = new();

    public bool Has(string detail) => Details.Contains(detail);

    static readonly IReadOnlyDictionary<string, (bool Shoulder, float Hem, float Flare)> Robes = new Dictionary<string, (bool, float, float)>
    {
        ["tunic"] = (true, 0.42f, 1.04f), ["gown"] = (true, 0.04f, 1.4f), ["skirt"] = (false, 0.05f, 1.3f), ["toga"] = (true, 0.1f, 1.15f),
        ["kimono"] = (true, 0.04f, 1.1f), ["hakama"] = (false, 0.03f, 1.5f), ["kaftan"] = (true, 0.06f, 1.2f),
    };

    static readonly IReadOnlyDictionary<string, float> Coats = new Dictionary<string, float> { ["coat"] = 0.3f, ["jacket"] = 0.5f, ["greatcoat"] = 0.12f, ["haori"] = 0.5f };

    public static Kit Of(CrowdLook look)
    {
        var k = new Kit();
        foreach (var d in look.Details ?? []) k.Details.Add(d);
        if (look.Part(W.Robe) is { } robe && Robes.TryGetValue(robe.Kind, out var r))
            k.Robe = new Robe(robe.From is { } from ? from == "shoulder" : r.Shoulder, (float)(robe.Hem ?? r.Hem), (float)(robe.Flare ?? r.Flare), robe.Slot ?? S.Outer);
        if (look.Part(W.Coat) is { } coat)
        {
            if (coat.Kind == "paenula") k.Paenula = true;
            else k.CoatHem = (float)(coat.Hem ?? Coats.GetValueOrDefault(coat.Kind, 0.3f));
        }
        k.Torso = look.Part(W.Torso)?.Slot ?? (k.Robe is { FromShoulder: true } ? k.Robe.Slot : S.Outer);
        var sleeves = look.Part(W.Sleeves);
        k.Sleeves = sleeves?.Kind == "bare" ? S.Skin : sleeves?.Slot ?? (k.CoatHem != null ? S.Outer : k.Torso);
        k.Rolled = sleeves?.Kind == "rolled";
        k.WideSleeves = sleeves?.Kind == "wide";
        k.Thigh = Leg(look.Part(W.Thigh), S.Legs);
        k.Baggy = look.Part(W.Thigh)?.Kind == "baggy";
        k.Shin = Leg(look.Part(W.Shin), S.Legs);
        k.Feet = Leg(look.Part(W.Feet), S.Accent);
        if (look.Part(W.Apron) is { } apron) k.Apron = apron.Kind == "smith" ? new Apron(0.72f, (float)(apron.Hem ?? 0.33), apron.Slot ?? S.Accent)
            : new Apron(0.6f, (float)(apron.Hem ?? 0.3), apron.Slot ?? S.Linen);
        if (look.Part(W.Belt) is { } belt)
        {
            k.Obi = belt.Kind == "obi";
            k.Belt = belt.Slot ?? (k.Obi ? S.Under : S.Accent);
        }
        k.Hat = look.Part(W.Hat)?.Kind;
        k.HatSlot = look.Part(W.Hat)?.Slot ?? S.Hat;
        k.Hair = look.Part(W.Hair)?.Kind ?? "none";
        k.Beard = look.Part(W.Beard)?.Kind;
        var neck = look.Part(W.Neck)?.Kind;
        (k.Cravat, k.Fichu, k.Collar, k.Tie) = (neck == "cravat", neck == "fichu", neck == "collar", neck == "tie");
        k.Side = look.Part(W.Side)?.Kind;
        k.Item = look.Item;
        k.Gun = look.Item switch { "musket" => "musket", "long-gun" => "long", "rifle" => "rifle", _ => null };
        if (k.Has(CrowdDetails.Cuffs)) k.Cuffs = S.Trim;
        k.Hold = HoldOf(k);
        return k;
    }

    static string Leg(Garment? g, string fallback) => g?.Kind == "bare" ? S.Skin : g?.Slot ?? fallback;

    public static int HoldOf(Kit k) => k.Gun != null ? CrowdHolds.Gun
        : k.Item switch { "spear" => CrowdHolds.Spear, "shield" => CrowdHolds.Shield, "hoe" => CrowdHolds.Hoe, "hammer" => CrowdHolds.Work, _ => k.Paenula && k.Item == null ? CrowdHolds.Cloak : CrowdHolds.None };

    public float LegAmp => Robe is { Hem: < 0.2f } ? 0.5f : Robe is { Hem: < 0.32f } || CoatHem is < 0.32f ? 0.8f : 1;
}
