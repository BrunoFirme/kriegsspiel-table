using G = KTable.Core.Model.Garment;
using H = KTable.Core.Model.RigHumanoid;

namespace KTable.Core.Model;

public static class CrowdBaseline
{
    public const string Id = "baseline";
    public const string Labourer = "labourer", Farmer = "farmer", Smith = "smith", Merchant = "merchant", Soldier = "soldier", Cleric = "cleric";
    public const string Housewife = "housewife", Washer = "washer", Youngster = "child";

    public static readonly IReadOnlyList<string> Bands = [CrowdEntry.Child, CrowdEntry.Youth, CrowdEntry.Adult, CrowdEntry.Elder];
    public static readonly IReadOnlyList<string> Wealths = [CrowdEntry.Poor, CrowdEntry.Middle, CrowdEntry.Rich];
    public static readonly IReadOnlyList<string> Materials = [CrowdSlots.Linen, "leather", CrowdSlots.Metal, CrowdSlots.Wood, CrowdSlots.Brass];

    public static readonly IReadOnlyList<HandItem> Items =
    [
        new() { Id = "spear", Hand = HandItem.Both, LengthM = 2.4 },
        new() { Id = "musket", Hand = HandItem.Shoulder, LengthM = 1.5 },
        new() { Id = "long-gun", Hand = HandItem.Shoulder, LengthM = 1.7 },
        new() { Id = "rifle", Hand = HandItem.Shoulder, LengthM = 1.25 },
        new() { Id = "bow", Hand = HandItem.Left, LengthM = 1.6 },
        new() { Id = "shield", Hand = HandItem.Left, LengthM = 1.1 },
        new() { Id = "hammer", Hand = HandItem.Right, LengthM = 0.4 },
        new() { Id = "hoe", Hand = HandItem.Both, LengthM = 1.4 },
        new() { Id = "basket", Hand = HandItem.Left, LengthM = 0.45 },
        new() { Id = "cane", Hand = HandItem.Right, LengthM = 0.9 },
        new() { Id = "bundle", Hand = HandItem.Back, LengthM = 0.6 },
        new() { Id = "sack", Hand = HandItem.Shoulder, LengthM = 0.7 },
        new() { Id = "torch", Hand = HandItem.Right, LengthM = 0.6 },
        new() { Id = "lantern", Hand = HandItem.Right, LengthM = 0.3 },
    ];

    static readonly IReadOnlyDictionary<string, double> AdultJoints = new Dictionary<string, double>
    {
        [H.Foot] = 0.04, [H.LowerLeg] = 0.285, [H.UpperLeg] = 0.47, [H.Hips] = 0.52, [H.Spine] = 0.61, [H.Chest] = 0.72, [H.UpperChest] = 0.755,
        [H.UpperArm] = 0.815, [H.Neck] = 0.845, [H.Head] = 0.867, [H.LowerArm] = 0.63, [H.Hand] = 0.485,
    };

    static readonly IReadOnlyDictionary<string, double> ChildJoints = new Dictionary<string, double>
    {
        [H.Foot] = 0.045, [H.LowerLeg] = 0.265, [H.UpperLeg] = 0.455, [H.Hips] = 0.505, [H.Spine] = 0.59, [H.Chest] = 0.69, [H.UpperChest] = 0.72,
        [H.UpperArm] = 0.78, [H.Neck] = 0.805, [H.Head] = 0.83, [H.LowerArm] = 0.6, [H.Hand] = 0.45,
    };

    const double YouthGrown = 0.72;

    public static IReadOnlyDictionary<string, double> Joints(string band) => band switch
    {
        CrowdEntry.Child => ChildJoints,
        CrowdEntry.Youth => AdultJoints.ToDictionary(j => j.Key, j => ChildJoints[j.Key] + (j.Value - ChildJoints[j.Key]) * YouthGrown),
        _ => AdultJoints,
    };

    public static IReadOnlyDictionary<string, (double Wide, double Deep)> Girths(string sex) => sex == CrowdEntry.Woman
        ? new Dictionary<string, (double, double)> { [H.Hips] = (0.106, 0.072), [H.Spine] = (0.072, 0.055), [H.Chest] = (0.092, 0.074), [H.UpperChest] = (0.094, 0.068), [H.Neck] = (0.04, 0.036) }
        : new Dictionary<string, (double, double)> { [H.Hips] = (0.095, 0.066), [H.Spine] = (0.083, 0.058), [H.Chest] = (0.1, 0.068), [H.UpperChest] = (0.104, 0.066), [H.Neck] = (0.044, 0.04) };

    public static readonly IReadOnlyDictionary<string, double> BandHeight = new Dictionary<string, double>
    {
        [CrowdEntry.Child] = 0.72, [CrowdEntry.Youth] = 0.93, [CrowdEntry.Adult] = 1, [CrowdEntry.Elder] = 0.985,
    };

    public static readonly IReadOnlyDictionary<string, double> BandShares = new Dictionary<string, double>
    {
        [CrowdEntry.Child] = 18, [CrowdEntry.Youth] = 10, [CrowdEntry.Adult] = 60, [CrowdEntry.Elder] = 12,
    };

    public static readonly IReadOnlyDictionary<string, (int Least, int Most)> BandAges = new Dictionary<string, (int, int)>
    {
        [CrowdEntry.Child] = (5, 11), [CrowdEntry.Youth] = (12, 17), [CrowdEntry.Adult] = (18, 55), [CrowdEntry.Elder] = (56, 77),
    };

    public static readonly IReadOnlyDictionary<string, string> CallingWealth = new Dictionary<string, string>
    {
        [Merchant] = CrowdEntry.Rich, [Cleric] = CrowdEntry.Middle, [Soldier] = CrowdEntry.Poor, [Smith] = CrowdEntry.Middle,
    };

    public static readonly Nationality Person = new()
    {
        Id = Id, Name = "Baseline", Culture = "continental", Year = 1700, Faith = "",
        Heights = new() { [CrowdEntry.Man] = 1.68, [CrowdEntry.Woman] = 1.57 },
        Skins = ["#e3ba98", "#d7a985", "#ca9873", "#bc8865"],
        Hairs = ["#2a2119", "#3a2b1e", "#553c25", "#6e4d2e", "#8a6a44", "#a8895e"],
        Eyes = ["#3b3a3e", "#3d3024", "#4a5662", "#2f2620"],
        Dyes = new()
        {
            [CrowdEntry.Poor] = ["#6e5b45", "#7d7466", "#806e52", "#5f5a49", "#8b7f68"],
            [CrowdEntry.Middle] = ["#4f6178", "#5f6b42", "#7a4a33", "#6a5d7a"],
            [CrowdEntry.Rich] = ["#7d2c26", "#2c3e63", "#1f1c1a", "#4a2e4f"],
        },
        Materials = new() { [CrowdSlots.Linen] = "#d9cfb8", ["leather"] = "#4b3626", [CrowdSlots.Metal] = "#6c6a64", [CrowdSlots.Wood] = "#7a5c3a", [CrowdSlots.Brass] = "#a8894e" },
        ArmyCoats = ["#6e5b45"],
        CallingNames = new()
        {
            [Labourer] = "Labourer", [Farmer] = "Farmer", [Smith] = "Smith", [Merchant] = "Merchant", [Soldier] = "Soldier", [Cleric] = "Priest",
            [Housewife] = "Townswoman", [Washer] = "Washerwoman", [Youngster] = "Child",
        },
        Callings = new()
        {
            ["m:" + Labourer] = 32, ["m:" + Farmer] = 18, ["m:" + Smith] = 6, ["m:" + Merchant] = 7, ["m:" + Soldier] = 8, ["m:" + Cleric] = 4,
            ["f:" + Housewife] = 72, ["f:" + Washer] = 18,
        },
        Dress =
        [
            new()
            {
                Wear = [G.Of(Wardrobe.Torso, "shirt", CrowdSlots.Under), G.Of(Wardrobe.Sleeves, "long", CrowdSlots.Outer), G.Of(Wardrobe.Thigh, "hose", CrowdSlots.Legs),
                    G.Of(Wardrobe.Shin, "hose", CrowdSlots.Legs), G.Of(Wardrobe.Feet, "shoes", CrowdSlots.Accent), G.Of(Wardrobe.Hair, "short", CrowdSlots.Hair)],
                Colours = new()
                {
                    [CrowdSlots.Outer] = "wealth", [CrowdSlots.Under] = CrowdEntry.Poor, [CrowdSlots.Legs] = CrowdEntry.Poor, [CrowdSlots.Hat] = CrowdEntry.Poor,
                    [CrowdSlots.Accent] = "leather", [CrowdSlots.Trim] = "=under",
                },
            },
            new() { Sex = CrowdEntry.Man, Wear = [G.Of(Wardrobe.Robe, "tunic", CrowdSlots.Outer, 0.36, 1.2, "shoulder"), G.Of(Wardrobe.Belt, "belt", CrowdSlots.Accent)] },
            new() { Sex = CrowdEntry.Woman, Wear = [G.Of(Wardrobe.Robe, "gown", CrowdSlots.Outer, 0.02, 1.35, "shoulder"), G.Of(Wardrobe.Hair, "bun", CrowdSlots.Hair)] },
            new() { Sex = CrowdEntry.Woman, Wealth = [CrowdEntry.Poor, CrowdEntry.Middle], Chance = 0.6, Wear = [G.Of(Wardrobe.Apron, "apron", CrowdSlots.Linen)] },
            new() { Callings = [Soldier], Colours = new() { [CrowdSlots.Outer] = "army" }, Wear = [G.Of(Wardrobe.Hat, "cap", CrowdSlots.Hat)], Item = "spear" },
            new() { Callings = [Smith], Wear = [G.Of(Wardrobe.Apron, "smith", CrowdSlots.Accent), G.Of(Wardrobe.Sleeves, "rolled", CrowdSlots.Under)], Item = "hammer" },
            new() { Callings = [Farmer], Wear = [G.Of(Wardrobe.Hat, "brim", CrowdSlots.Hat)], Colours = new() { [CrowdSlots.Hat] = "#b49a68" }, Item = "hoe" },
            new() { Callings = [Merchant], Wear = [G.Of(Wardrobe.Robe, "gown", CrowdSlots.Outer, 0.07, 1.3, "shoulder")] },
            new() { Callings = [Cleric], Wear = [G.Of(Wardrobe.Robe, "gown", CrowdSlots.Outer, 0.03, 1.3, "shoulder")], Colours = new() { [CrowdSlots.Outer] = "#26221e|#4b3d30|#5d574f" } },
            new() { Callings = [Washer], Item = "basket" },
            new() { Bands = [CrowdEntry.Child], Off = [Wardrobe.Hat, Wardrobe.Apron, Wardrobe.Side], Item = "" },
            new() { Bands = [CrowdEntry.Elder], Chance = 0.6, Item = "cane" },
        ],
        Names = new()
        {
            ["given-m"] = ["John", "Peter", "Paul", "Thomas", "James", "Martin"],
            ["given-f"] = ["Anna", "Mary", "Margaret", "Elizabeth", "Agnes", "Joan"],
            ["family"] = ["Miller", "Smith", "Baker", "Weaver", "Cooper", "Fisher"],
        },
        Naming = [new() { Pattern = "{given} {family}" }],
        Day = new() { HolyDay = "Sunday", HolyAt = 9, Rest = [], Prayers = [], MarketDays = ["Wednesday", "Saturday"], DarkBy = 21 },
    };
}
