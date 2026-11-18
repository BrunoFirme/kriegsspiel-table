using B = KTable.Core.Model.CrowdBaseline;
using E = KTable.Core.Model.CrowdEntry;
using G = KTable.Core.Model.Garment;
using S = KTable.Core.Model.CrowdSlots;
using W = KTable.Core.Model.Wardrobe;

namespace KTable.Core.Model;

public static class Nationalities
{
    const string Leather = "leather", Wealth = "wealth", Army = "army";
    static readonly List<string> Grown = [E.Adult, E.Elder];
    static readonly List<string> Moneyed = [E.Middle, E.Rich];
    static readonly List<string> NotRich = [E.Poor, E.Middle];
    static readonly List<string> West = ["#e3ba98", "#d7a985", "#ca9873", "#bc8865"];
    static readonly List<string> Near = ["#d0a07a", "#c08f6a", "#ae7c58", "#9a6a48"];
    static readonly List<string> Far = ["#e6c39c", "#dab18a", "#cd9f79"];
    static readonly List<string> South = ["#d8a67e", "#c6946d", "#b5835d", "#9f704d"];
    static readonly List<string> Light = ["#2a2119", "#3a2b1e", "#553c25", "#6e4d2e", "#8a6a44", "#a8895e"];
    static readonly List<string> Dark = ["#161310", "#1d1915", "#251f19", "#2e251c"];
    static readonly List<string> DarkEyes = ["#2a2019", "#21190f", "#30251b"];

    static Dictionary<string, string> C(params (string Slot, string Word)[] words) => words.ToDictionary(w => w.Slot, w => w.Word);

    static Dictionary<string, List<string>> Dyes(List<string> poor, List<string> middle, List<string> rich) => new() { [E.Poor] = poor, [E.Middle] = middle, [E.Rich] = rich };

    static Dictionary<string, string> Stuff(string linen, string leather, string metal, string wood, string brass) =>
        new() { [S.Linen] = linen, [Leather] = leather, [S.Metal] = metal, [S.Wood] = wood, [S.Brass] = brass };

    public static readonly Nationality Gothic = new()
    {
        Id = "gothic", Name = "Gothic", Culture = "gothic", Year = 1350, Faith = "Catholic",
        Heights = new() { [E.Man] = 1.70, [E.Woman] = 1.59 }, Skins = West, Hairs = Light,
        Dyes = Dyes(["#6e5b45", "#7d7466", "#806e52", "#5f5a49", "#8b7f68"], ["#4f6178", "#5f6b42", "#7a4a33", "#6a5d7a"], ["#7d2c26", "#2c3e63", "#1f1c1a", "#4a2e4f"]),
        Materials = Stuff("#d9cfb8", "#4b3626", "#6c6a64", "#7a5c3a", "#9a7d45"),
        ArmyCoats = ["#8c7b5a", "#7a6a4c", "#6b2f29", "#2f3f5e"],
        CallingNames = new() { [B.Soldier] = "Man-at-arms", [B.Cleric] = "Monk" },
        Dress =
        [
            new() { Wealth = [E.Rich], Colours = C((S.Legs, E.Middle)) },
            new() { Sex = E.Man, Wear = [G.Of(W.Hair, "short|cropped|curly|bob", S.Hair)] },
            new() { Sex = E.Man, Callings = [B.Labourer], Chance = 0.45, Wear = [G.Of(W.Hat, "hood", S.Hat)] },
            new() { Sex = E.Man, Callings = [B.Labourer], Chance = 0.25, Wear = [G.Of(W.Hat, "coif", S.Linen)] },
            new() { Sex = E.Man, Wealth = [E.Rich], Callings = [B.Labourer, B.Merchant, B.Farmer, B.Smith], Wear = [G.Of(W.Robe, "gown", S.Outer, 0.04, 1.4, "shoulder"), G.Of(W.Sleeves, "wide", S.Outer)] },
            new() { Callings = [B.Cleric], Wear = [G.Of(W.Hair, "tonsure", S.Hair), G.Of(W.Belt, "belt", S.Linen)] },
            new() { Callings = [B.Soldier], Wear = [G.Of(W.Robe, "tunic", S.Outer, 0.4, 1.1, "shoulder"), G.Of(W.Hat, "kettle", S.Metal), G.Of(W.Side, "arming", S.Metal)] },
            new() { Callings = [B.Smith], Chance = 0.5, Wear = [G.Of(W.Hat, "coif", S.Linen)] },
            new() { Callings = [B.Farmer], Chance = 0.5, Wear = [G.Of(W.Hat, "hood", S.Hat)] },
            new() { Callings = [B.Merchant], Wear = [G.Of(W.Hat, "chaperon", S.Hat)], Colours = C((S.Hat, E.Rich)) },
            new() { Sex = E.Woman, Wear = [G.Of(W.Hair, "bun|braid|long", S.Hair)] },
            new() { Sex = E.Woman, Bands = Grown, Wear = [G.Of(W.Hat, "wimple|veil", S.Linen)] },
            new() { Sex = E.Woman, Wealth = [E.Rich], Off = [W.Apron], Wear = [G.Of(W.Sleeves, "wide", S.Outer)] },
            new() { Sex = E.Man, Bands = Grown, Chance = 0.35, Wear = [G.Of(W.Beard, "full|short", S.Hair)] },
        ],
        Names = new()
        {
            ["given-m"] = ["Heinrich", "Konrad", "Ulrich", "Hans", "Peter", "Nikolaus", "Berthold", "Dietrich"],
            ["given-f"] = ["Agnes", "Else", "Katharina", "Grete", "Adelheid", "Mechthild", "Gertrud", "Kunigunde"],
            ["family"] = ["Schmied", "Müller", "Weber", "Becker", "Fischer", "Kramer", "Zimmermann", "Schuster"],
        },
        Day = new() { HolyAt = 8.5, DarkBy = 20 },
    };

    public static readonly Nationality Continental = new()
    {
        Id = "continental", Name = "Continental", Culture = "continental", Year = 1765, Faith = "Lutheran",
        Heights = new() { [E.Man] = 1.67, [E.Woman] = 1.56 }, Skins = West, Hairs = Light,
        Dyes = Dyes(["#6a5644", "#77706a", "#5c564d", "#8b7b62", "#6f6450"], ["#3e4f68", "#5b4a3a", "#56603f", "#6e2f2a", "#4d5a66"], ["#24324f", "#1d1b1a", "#5e2433", "#3f4a37", "#5a4630"]),
        Materials = Stuff("#e0d8c6", "#3f2e22", "#6e6d68", "#6e5236", "#b8975a"),
        ArmyCoats = ["#2b3d63", "#8a2f27", "#d8d2c2"],
        CallingNames = new() { [B.Soldier] = "Musketeer" },
        Dress =
        [
            new() { Wealth = Moneyed, Colours = C((S.Under, E.Middle)) },
            new() { Colours = C((S.Hat, "#1e1c1a")) },
            new() { Sex = E.Man, Off = [W.Robe, W.Belt], Wear = [G.Of(W.Torso, "waistcoat", S.Under), G.Of(W.Thigh, "breeches", S.Legs), G.Of(W.Shin, "stockings", S.Linen), G.Of(W.Hair, "queue|curly", S.Hair)] },
            new() { Sex = E.Man, Bands = Grown, Wear = [G.Of(W.Neck, "cravat", S.Linen)] },
            new() { Sex = E.Man, Wealth = Moneyed, Callings = [B.Labourer, B.Merchant], Wear = [G.Of(W.Coat, "coat", S.Outer, 0.3), G.Of(W.Hat, "tricorne", S.Hat)] },
            new() { Sex = E.Man, Wealth = [E.Poor], Callings = [B.Labourer], Wear = [G.Of(W.Sleeves, "long", S.Linen)] },
            new() { Sex = E.Man, Wealth = [E.Poor], Callings = [B.Labourer], Chance = 0.6, Wear = [G.Of(W.Hat, "brim", S.Hat)], Colours = C((S.Hat, "#3a332b|#4a4136")) },
            new() { Callings = [B.Merchant], Chance = 0.5, Item = "cane" },
            new() { Callings = [B.Cleric], Wear = [G.Of(W.Robe, "gown", S.Outer, 0.03, 1.25, "shoulder"), G.Of(W.Hat, "brim", S.Hat), G.Of(W.Hair, "short", S.Hair)], Colours = C((S.Outer, "#1f1d1b"), (S.Hat, "#1a1918")) },
            new() { Callings = [B.Soldier], Wear = [G.Of(W.Coat, "coat", S.Outer, 0.3), G.Of(W.Hat, "tricorne", S.Hat), G.Of(W.Side, "hanger", S.Metal)], Colours = C((S.Under, "#c9b27e"), (S.Legs, "#c9b27e"), (S.Trim, "#8a2a24")), Item = "musket" },
            new() { Callings = [B.Smith], Wear = [G.Of(W.Sleeves, "rolled", S.Linen), G.Of(W.Hat, "cap", S.Hat)], Colours = C((S.Hat, E.Poor)) },
            new() { Callings = [B.Farmer], Wear = [G.Of(W.Robe, "tunic", S.Outer, 0.36, 1.2, "shoulder"), G.Of(W.Hat, "brim", S.Hat)], Colours = C((S.Outer, "#c8bd9f|#b9ad8c|#a89c7c"), (S.Hat, "#4a3f33")) },
            new() { Sex = E.Woman, Wear = [G.Of(W.Torso, "bodice", S.Under), G.Of(W.Robe, "skirt", S.Outer, 0.02, 1.55, "waist"), G.Of(W.Sleeves, "long", S.Linen), G.Of(W.Hair, "bun", S.Hair)], Colours = C((S.Under, Wealth)) },
            new() { Sex = E.Woman, Wealth = NotRich, Chance = 0.75, Wear = [G.Of(W.Apron, "apron", S.Linen)] },
            new() { Sex = E.Woman, Wealth = NotRich, Chance = 0.7, Wear = [G.Of(W.Neck, "fichu", S.Linen)] },
            new() { Sex = E.Woman, Bands = Grown, Wear = [G.Of(W.Hat, "mobcap", S.Linen)] },
            new() { Sex = E.Woman, Wealth = [E.Rich], Off = [W.Apron], Wear = [G.Of(W.Robe, "skirt", S.Outer, 0.02, 1.95, "waist"), G.Of(W.Sleeves, "long", S.Under)] },
            new() { Sex = E.Woman, Wealth = [E.Rich], Bands = Grown, Chance = 0.5, Wear = [G.Of(W.Hat, "bonnet", S.Hat)], Colours = C((S.Hat, "#c2ad7c")) },
        ],
        Names = new()
        {
            ["given-m"] = ["Johann", "Friedrich", "Wilhelm", "Christian", "Gottfried", "Karl", "Georg", "Heinrich"],
            ["given-f"] = ["Anna", "Maria", "Sophie", "Dorothea", "Charlotte", "Luise", "Elisabeth", "Johanna"],
            ["family"] = ["Müller", "Schulze", "Krüger", "Lehmann", "Richter", "Hoffmann", "Neumann", "Schröder"],
        },
        Day = new() { HolyAt = 9.2, DarkBy = 21 },
    };

    public static readonly Nationality Ottoman = new()
    {
        Id = "ottoman", Name = "Ottoman", Culture = "ottoman", Year = 1720, Faith = "Sunni Islam",
        Heights = new() { [E.Man] = 1.66, [E.Woman] = 1.54 }, Skins = Near, Hairs = Dark, Eyes = DarkEyes,
        Dyes = Dyes(["#7a6248", "#8a7f6c", "#6c5c48", "#a8916a", "#5f5444"], ["#3a4e68", "#6b3a30", "#4f5f3e", "#a8813a", "#5a4a5e"], ["#7a2328", "#24305a", "#2f4b37", "#5b2a4f", "#8a6a2a"]),
        Materials = Stuff("#e6dfcf", "#4a3424", "#7a7568", "#7a5a38", "#b08a48"),
        ArmyCoats = ["#2f3f6e", "#7a2328", "#3d5a40"],
        CallingNames = new() { [B.Soldier] = "Janissary", [B.Cleric] = "Imam" },
        Dress =
        [
            new() { Colours = C((S.Under, "#a8813a|#7a2328|#3a4e68|#2f4b37"), (S.Hat, S.Linen)), Wear = [G.Of(W.Thigh, "baggy", S.Legs), G.Of(W.Belt, "sash", S.Under)] },
            new() { Sex = E.Man, Wear = [G.Of(W.Robe, "kaftan", S.Outer, 0.05, 1.25, "shoulder"), G.Of(W.Hat, "turban", S.Hat)] },
            new() { Sex = E.Man, Wealth = [E.Poor], Callings = [B.Labourer, B.Farmer, B.Smith], Wear = [G.Of(W.Robe, "tunic", S.Outer, 0.42, 1.15, "waist")] },
            new() { Sex = E.Man, Wealth = [E.Poor], Callings = [B.Labourer, B.Farmer], Chance = 0.4, Wear = [G.Of(W.Hat, "cap", S.Hat)], Colours = C((S.Hat, E.Poor)) },
            new() { Sex = E.Man, Wealth = [E.Rich], Callings = [B.Labourer, B.Merchant, B.Farmer, B.Smith], Wear = [G.Of(W.Coat, "greatcoat", S.Outer, 0.05), G.Of(W.Hat, "bigturban", S.Hat)] },
            new() { Sex = E.Man, Wealth = [E.Middle], Callings = [B.Labourer, B.Merchant, B.Farmer, B.Smith], Chance = 0.5, Wear = [G.Of(W.Coat, "coat", S.Outer, 0.3)] },
            new() { Callings = [B.Soldier], Wear = [G.Of(W.Robe, "kaftan", S.Outer, 0.24, 1.25, "shoulder"), G.Of(W.Hat, "bork", S.Hat), G.Of(W.Side, "yatagan", S.Metal), G.Of(W.Beard, "moustache", S.Hair)],
                Colours = C((S.Hat, "#e4ddcc")), Item = "long-gun" },
            new() { Callings = [B.Cleric], Wear = [G.Of(W.Robe, "kaftan", S.Outer, 0.02, 1.25, "shoulder"), G.Of(W.Hat, "bigturban", S.Hat)], Colours = C((S.Outer, "#e3dccb|#2a2724|#3d5440")) },
            new() { Callings = [B.Smith], Wear = [G.Of(W.Hat, "cap", S.Hat)], Colours = C((S.Hat, E.Poor)) },
            new() { Callings = [B.Merchant], Wear = [G.Of(W.Hat, "bigturban", S.Hat)] },
            new() { Callings = [B.Merchant], Chance = 0.4, Item = "cane" },
            new() { Sex = E.Man, Bands = Grown, Callings = [B.Labourer, B.Farmer, B.Smith, B.Merchant, B.Cleric], Wear = [G.Of(W.Beard, "full|full|moustache", S.Hair)] },
            new() { Sex = E.Woman, Off = [W.Belt], Wear = [G.Of(W.Robe, "gown", S.Outer, 0.02, 1.3, "shoulder"), G.Of(W.Hat, "veil", S.Hat)],
                Colours = C((S.Outer, "#5a4a3e|#3a4e68|#6b5a4a|#4a3a44|#7a6a5a")) },
        ],
        Names = new()
        {
            ["given-m"] = ["Mehmed", "Ahmed", "Mustafa", "Ali", "Hasan", "Ibrahim", "Osman", "Süleyman"],
            ["given-f"] = ["Fatma", "Ayşe", "Emine", "Hatice", "Zeynep", "Hanife", "Rabia", "Şerife"],
            ["father"] = ["Mehmed", "Ahmed", "Mustafa", "Ali", "Hasan", "Ibrahim", "Osman", "Süleyman"],
            ["child-of-m"] = ["son of"], ["child-of-f"] = ["daughter of"],
        },
        Naming = [new() { Pattern = "{given}, {child-of} {father}" }],
        Day = new() { HolyDay = "Friday", HolyAt = 11.7, Prayers = [5, 12.5, 15.5, 18, 19.5], Rest = [12.5, 14], MarketDays = ["Monday", "Thursday"], DarkBy = 20 },
    };

    public static readonly Nationality Japanese = new()
    {
        Id = "japanese", Name = "Japanese", Culture = "japanese", Year = 1800, Faith = "Buddhist and Shinto",
        Heights = new() { [E.Man] = 1.57, [E.Woman] = 1.46 }, Skins = Far, Hairs = Dark, Eyes = DarkEyes,
        Dyes = Dyes(["#2f3d58", "#5a4b3c", "#6f6a62", "#3c4a5f", "#4e4a40"], ["#273350", "#4a3b30", "#5b5348", "#7a4a2c", "#3d4636"], ["#1c1b1d", "#2a2e45", "#4b2d2e", "#3a4035", "#5a4630"]),
        Materials = Stuff("#e2dccd", "#3a2e24", "#3a3836", "#a08a5e", "#6a5a40"),
        ArmyCoats = ["#2a2a2e", "#3a3530"],
        CallingNames = new() { [B.Soldier] = "Ashigaru", [B.Cleric] = "Monk", ["samurai"] = "Samurai" },
        Callings = new() { ["m:samurai"] = 8 },
        Dress =
        [
            new() { Colours = C((S.Under, "#5a4a3a|#2a2724|#7a6a50|#3a3a44"), (S.Hat, "#b59c6c"), (S.Accent, "#8a7350")),
                Wear = [G.Of(W.Belt, "sash", S.Under), G.Of(W.Sleeves, "wide", S.Outer), G.Of(W.Thigh, "bare", S.Skin), G.Of(W.Shin, "bare", S.Skin), G.Of(W.Feet, "sandals", S.Accent), G.Of(W.Neck, "collar", S.Under)] },
            new() { Sex = E.Man, Wear = [G.Of(W.Robe, "kimono", S.Outer, 0.3, 1.08, "shoulder"), G.Of(W.Hair, "chonmage", S.Hair)] },
            new() { Sex = E.Man, Callings = [B.Labourer], Wear = [G.Of(W.Robe, "kimono", S.Outer, 0.44, 1.05, "shoulder"), G.Of(W.Thigh, "trousers", S.Legs), G.Of(W.Shin, "trousers", S.Legs), G.Of(W.Sleeves, "long", S.Outer)] },
            new() { Sex = E.Man, Callings = [B.Labourer], Wealth = [E.Poor], Chance = 0.3, Wear = [G.Of(W.Hat, "headband", S.Hat)], Colours = C((S.Hat, "#e2dccd|#2f3d58")) },
            new() { Sex = E.Man, Callings = [B.Merchant], Wear = [G.Of(W.Robe, "hakama", S.Legs, 0.03, 1.5, "waist"), G.Of(W.Coat, "haori", S.Outer, 0.5)], Colours = C((S.Legs, "#2a2a30|#3a3a40|#4a3b30")) },
            new() { Callings = ["samurai"], Off = [W.Apron], Wear = [G.Of(W.Robe, "hakama", S.Legs, 0.03, 1.5, "waist"), G.Of(W.Side, "daisho", S.Metal)],
                Colours = C((S.Legs, "#2a2a30|#3a3a40|#4a3b30|#2e3446"), (S.Outer, "#5b5348|#4a3b30|#6f6a62|#3c4a5f"), (S.Trim, "#1b1a1c")) },
            new() { Callings = [B.Soldier], Wear = [G.Of(W.Robe, "kimono", S.Outer, 0.38, 1.08, "shoulder"), G.Of(W.Hat, "jingasa", S.Hat), G.Of(W.Thigh, "trousers", S.Legs), G.Of(W.Shin, "trousers", S.Legs),
                G.Of(W.Sleeves, "long", S.Outer), G.Of(W.Side, "katana", S.Metal)], Colours = C((S.Hat, "#2a2622"), (S.Legs, "#2a2a2e")) },
            new() { Callings = [B.Cleric], Wear = [G.Of(W.Robe, "kimono", S.Outer, 0.03, 1.08, "shoulder"), G.Of(W.Hair, "none", S.Hair)], Colours = C((S.Outer, "#2a2724|#5b4e3e")) },
            new() { Callings = [B.Cleric], Chance = 0.4, Wear = [G.Of(W.Hat, "kasa", S.Hat)] },
            new() { Callings = [B.Farmer], Wear = [G.Of(W.Hat, "kasa", S.Hat)] },
            new() { Callings = [B.Smith], Wear = [G.Of(W.Sleeves, "long", S.Outer)], Colours = C((S.Accent, "#3a2e24")) },
            new() { Sex = E.Woman, Off = [W.Apron], Wear = [G.Of(W.Robe, "kimono", S.Outer, 0.02, 1.12, "shoulder"), G.Of(W.Belt, "obi", S.Under), G.Of(W.Hair, "bun", S.Hair)],
                Colours = C((S.Under, "#8a3a2a|#5d6a3e|#b08a3e|#3a3550|#7a5a3a")) },
        ],
        Names = new()
        {
            ["given-m"] = ["Gonbei", "Tasuke", "Heikichi", "Kyūbei", "Jirōbei", "Matsuzō", "Kanta", "Yohei"],
            ["given-f"] = ["O-Kiku", "O-Haru", "O-Tsuru", "O-Sato", "O-Yuki", "O-Mitsu", "O-Fumi", "O-Take"],
            ["family"] = ["Okabe", "Mizuno", "Toda", "Naitō", "Matsuda", "Inagaki"],
            ["samurai-given"] = ["Kanbei", "Shinpachi", "Tadashichi", "Jūbei", "Gendayū"],
        },
        Naming = [new() { Callings = ["samurai"], Pattern = "{family} {samurai-given}" }, new() { Pattern = "{given}" }],
        Day = new() { HolyDay = "", HolyAt = 9, MarketDays = [], DarkBy = 19 },
    };

    public static readonly Nationality Roman = new()
    {
        Id = "roman", Name = "Roman", Culture = "roman", Year = 60, Faith = "the Roman gods",
        Heights = new() { [E.Man] = 1.66, [E.Woman] = 1.55 }, Skins = South, Hairs = Dark, Eyes = DarkEyes,
        Dyes = Dyes(["#cfc5ad", "#a39274", "#8d7b5e", "#b9a888"], ["#b88a52", "#8f5a3a", "#6d7a74", "#8a6a5a"], ["#e4ddcb", "#5a2747", "#7a2a2a", "#2f4a5a"]),
        Materials = Stuff("#e6dfcd", "#5a3e28", "#8a7a5a", "#7a5c3a", "#a07d4a"),
        ArmyCoats = ["#86332a"],
        CallingNames = new() { [B.Soldier] = "Legionary" },
        Dress =
        [
            new() { Colours = C((S.Under, "#5a2747")), Wear = [G.Of(W.Thigh, "bare", S.Skin), G.Of(W.Shin, "bare", S.Skin), G.Of(W.Sleeves, "bare", S.Skin), G.Of(W.Feet, "sandals", S.Accent)] },
            new() { Wealth = [E.Rich], Colours = C((S.Outer, E.Middle)) },
            new() { Sex = E.Man, Wear = [G.Of(W.Robe, "tunic", S.Outer, 0.34, 1.15, "shoulder"), G.Of(W.Hair, "short|cropped|curly", S.Hair)] },
            new() { Sex = E.Man, Wealth = [E.Rich], Callings = [B.Labourer, B.Merchant, B.Farmer, B.Smith],
                Wear = [G.Of(W.Robe, "toga", S.Outer, 0.04, 1.35, "shoulder"), G.Of(W.Sleeves, "long", S.Outer), G.Of(W.Belt, "sash", S.Under)],
                Colours = C((S.Outer, "#e4ddcb")) },
            new() { Callings = [B.Merchant], Wear = [G.Of(W.Robe, "toga", S.Outer, 0.04, 1.35, "shoulder"), G.Of(W.Sleeves, "long", S.Outer)], Colours = C((S.Outer, "#e4ddcb")) },
            new() { Sex = E.Man, Callings = [B.Labourer], Chance = 0.5, Wear = [G.Of(W.Coat, "paenula", S.Hat, 0.3)], Colours = C((S.Hat, "#7b5e44|#6e5a44")) },
            new() { Callings = [B.Soldier], Wear = [G.Of(W.Torso, "cuirass", S.Metal), G.Of(W.Robe, "tunic", S.Outer, 0.38, 1.15, "waist"), G.Of(W.Hat, "galea", S.Metal), G.Of(W.Side, "gladius", S.Metal)],
                Colours = C((S.Trim, "#7a2a2a")) },
            new() { Callings = [B.Cleric], Wear = [G.Of(W.Robe, "toga", S.Outer, 0.03, 1.35, "shoulder"), G.Of(W.Hat, "palla", S.Hat), G.Of(W.Sleeves, "long", S.Outer)], Colours = C((S.Outer, "#e4ddcb"), (S.Hat, "#e4ddcb")) },
            new() { Callings = [B.Smith], Wear = [G.Of(W.Hat, "cap", S.Hat)] },
            new() { Callings = [B.Farmer], Wear = [G.Of(W.Hat, "hood", S.Hat)] },
            new() { Sex = E.Woman, Wear = [G.Of(W.Robe, "gown", S.Outer, 0.02, 1.3, "shoulder"), G.Of(W.Sleeves, "long", S.Outer), G.Of(W.Hair, "bun", S.Hair)],
                Colours = C((S.Outer, "#6b7f86|#8a5a6a|#b39a5e|#7a6a58|#cfc5ad")) },
            new() { Sex = E.Woman, Bands = Grown, Chance = 0.6, Wear = [G.Of(W.Hat, "palla", S.Hat)], Colours = C((S.Hat, "#5a6b72|#7a4a5a|#9a8452|#cfc5ad")) },
        ],
        Names = new()
        {
            ["praenomen"] = ["Gaius", "Lucius", "Marcus", "Quintus", "Titus", "Publius"],
            ["nomen-m"] = ["Julius", "Valerius", "Cornelius", "Flavius", "Caecilius", "Petronius"],
            ["nomen-f"] = ["Julia", "Valeria", "Cornelia", "Flavia", "Caecilia", "Petronia"],
            ["cognomen-m"] = ["Felix", "Rufus", "Severus", "Primus", "Crispus", "Niger"],
            ["cognomen-f"] = ["Prima", "Secunda", "Fortunata", "Tertia", "Sabina"],
            ["greek-m"] = ["Eros", "Hermes", "Phoebus", "Thallus", "Onesimus"],
            ["greek-f"] = ["Chloe", "Nike", "Phoebe", "Tyche", "Helpis"],
        },
        Naming = [new() { Wealth = [E.Poor], Chance = 0.5, Pattern = "{greek}" }, new() { Sex = E.Man, Pattern = "{praenomen} {nomen} {cognomen}" }, new() { Pattern = "{nomen} {cognomen}" }],
        Day = new() { HolyDay = "", HolyAt = 8.4, Rest = [12, 14], MarketDays = [], DarkBy = 19 },
    };

    public static readonly Nationality European1940s = new()
    {
        Id = "modern", Name = "European 1940s", Culture = "western", Year = 1942, Faith = "Christian",
        Heights = new() { [E.Man] = 1.72, [E.Woman] = 1.61 }, Skins = West, Hairs = Light,
        Dyes = Dyes(["#3f4a5a", "#5a5248", "#4a4a46", "#5e5a4c"], ["#3b3f45", "#5b4a3d", "#4b5560", "#6a5a44", "#454a3c", "#6a3b3b"], ["#1f2023", "#2c3340", "#4a3a32", "#3a2a2e"]),
        Materials = Stuff("#dcd6c8", "#2e2620", "#4a4a48", "#6a5238", "#8a7a5a"),
        ArmyCoats = ["#5a5e4e", "#5b5a3c", "#4e5446"],
        CallingNames = new() { [B.Smith] = "Mechanic" },
        Dress =
        [
            new() { Colours = C((S.Under, "#c9c3b4"), (S.Legs, "=outer"), (S.Hat, "#2a2826|#3a3530|#4a4540")), Wear = [G.Of(W.Thigh, "trousers", S.Legs), G.Of(W.Shin, "trousers", S.Legs)] },
            new() { Sex = E.Man, Off = [W.Robe, W.Belt], Wear = [G.Of(W.Coat, "jacket", S.Outer, 0.45), G.Of(W.Hair, "short|cropped", S.Hair)] },
            new() { Sex = E.Man, Bands = Grown, Callings = [B.Labourer, B.Merchant, B.Farmer], Wealth = Moneyed, Wear = [G.Of(W.Hat, "fedora|fedora|homburg", S.Hat), G.Of(W.Neck, "tie", S.Trim)],
                Colours = C((S.Trim, "#5a2a2a|#2a3550|#3a3a2a|#4a3a4a")) },
            new() { Sex = E.Man, Wealth = [E.Rich], Callings = [B.Labourer, B.Merchant], Wear = [G.Of(W.Coat, "greatcoat", S.Outer, 0.26)] },
            new() { Sex = E.Man, Wealth = [E.Rich], Chance = 0.5, Callings = [B.Labourer, B.Merchant], Item = "cane" },
            new() { Sex = E.Man, Wealth = [E.Poor], Callings = [B.Labourer, B.Smith, B.Farmer], Off = [W.Coat], Wear = [G.Of(W.Hat, "flatcap|flatcap|beret", S.Hat)],
                Colours = C((S.Outer, "#3f4a5a|#4a4a46|#5a5248"), (S.Hat, "#3a3632|#4a453e")) },
            new() { Callings = [B.Smith, B.Labourer], Chance = 0.4, Wear = [G.Of(W.Sleeves, "rolled", S.Linen)] },
            new() { Callings = [B.Soldier], Wear = [G.Of(W.Hat, "helmet", S.Outer), G.Of(W.Shin, "puttees", S.Accent), G.Of(W.Belt, "belt", S.Accent), G.Of(W.Side, "bayonet", S.Metal)], Item = "rifle" },
            new() { Callings = [B.Cleric], Off = [W.Coat], Wear = [G.Of(W.Robe, "gown", S.Outer, 0.03, 1.2, "shoulder"), G.Of(W.Hat, "brim", S.Hat)], Colours = C((S.Outer, "#1d1c1b"), (S.Hat, "#1a1918")) },
            new() { Callings = [B.Farmer], Colours = C((S.Legs, E.Poor)), Wear = [G.Of(W.Hat, "flatcap", S.Hat)] },
            new() { Sex = E.Woman, Wear = [G.Of(W.Torso, "shirt", S.Under), G.Of(W.Robe, "skirt", S.Outer, 0.3, 1.25, "waist"), G.Of(W.Shin, "stockings", S.Skin), G.Of(W.Hair, "bob|bun|short", S.Hair)],
                Colours = C((S.Under, E.Middle)) },
            new() { Sex = E.Woman, Wealth = Moneyed, Chance = 0.45, Wear = [G.Of(W.Robe, "gown", S.Outer, 0.28, 1.18, "shoulder")] },
            new() { Sex = E.Woman, Wealth = [E.Poor], Chance = 0.6, Wear = [G.Of(W.Hat, "headscarf", S.Hat)], Colours = C((S.Hat, E.Middle)) },
            new() { Sex = E.Man, Bands = Grown, Chance = 0.12, Wear = [G.Of(W.Beard, "moustache", S.Hair)] },
        ],
        Names = new()
        {
            ["given-m"] = ["Arthur", "George", "Harold", "Stanley", "Albert", "Frank", "Ernest", "Walter"],
            ["given-f"] = ["Edith", "Doris", "Vera", "Joan", "Mabel", "Elsie", "Hilda", "Ivy"],
            ["family"] = ["Smith", "Taylor", "Brown", "Wilson", "Clarke", "Walker", "Wright", "Hughes"],
        },
        Day = new() { HolyAt = 9.6, MarketDays = ["Saturday"], DarkBy = 23 },
    };

    public static readonly IReadOnlyList<Nationality> All = [Gothic, Continental, Ottoman, Japanese, Roman, European1940s];
}
