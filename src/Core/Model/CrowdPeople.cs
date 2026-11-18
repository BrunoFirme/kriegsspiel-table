using System.Text.Json.Serialization;

namespace KTable.Core.Model;

public static class CrowdSlots
{
    public const string Skin = "skin", Outer = "outer", Under = "under", Legs = "legs", Hat = "hat", Hair = "hair", Accent = "accent", Linen = "linen";
    public const string Metal = "metal", Wood = "wood", Trim = "trim", Brass = "brass", Eye = "eye", Lip = "lip", Brow = "brow", Lamp = "lamp";

    public static readonly IReadOnlyList<string> All = [Skin, Outer, Under, Legs, Hat, Hair, Accent, Linen, Metal, Wood, Trim, Brass, Eye, Lip, Brow, Lamp];
}

public static class Wardrobe
{
    public const string Torso = "torso", Sleeves = "sleeves", Thigh = "thigh", Shin = "shin", Feet = "feet", Robe = "robe", Coat = "coat", Apron = "apron", Belt = "belt";
    public const string Hat = "hat", Hair = "hair", Beard = "beard", Neck = "neck", Side = "side";

    public static readonly IReadOnlyList<string> Parts = [Torso, Sleeves, Thigh, Shin, Feet, Robe, Coat, Apron, Belt, Hat, Hair, Beard, Neck, Side];

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Kinds = new Dictionary<string, IReadOnlyList<string>>
    {
        [Torso] = ["shirt", "doublet", "waistcoat", "cuirass", "bodice"],
        [Sleeves] = ["long", "rolled", "wide", "bare"],
        [Thigh] = ["hose", "breeches", "trousers", "baggy", "bare"],
        [Shin] = ["hose", "stockings", "trousers", "puttees", "bare"],
        [Feet] = ["shoes", "boots", "sandals", "bare"],
        [Robe] = ["tunic", "gown", "skirt", "toga", "kimono", "hakama", "kaftan"],
        [Coat] = ["coat", "jacket", "greatcoat", "haori", "paenula"],
        [Apron] = ["apron", "smith"],
        [Belt] = ["belt", "sash", "obi"],
        [Hat] = ["hood", "coif", "brim", "cap", "chaperon", "kettle", "wimple", "veil", "tricorne", "mobcap", "bonnet", "turban", "bigturban", "bork", "kalpak", "kasa",
            "jingasa", "headband", "galea", "palla", "petasos", "helmet", "flatcap", "fedora", "homburg", "beret", "headscarf"],
        [Hair] = ["short", "cropped", "curly", "bob", "balding", "long", "bun", "braid", "queue", "tonsure", "topknot", "chonmage", "none"],
        [Beard] = ["full", "short", "moustache"],
        [Neck] = ["cravat", "fichu", "collar", "tie"],
        [Side] = ["arming", "hanger", "smallsword", "yatagan", "dagger", "katana", "daisho", "gladius", "bayonet"],
    };
}

public sealed class Garment
{
    public string Part { get; set; } = "";
    public string Kind { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Slot { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Hem { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Flare { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? From { get; set; }

    public Garment Clone() => (Garment)MemberwiseClone();

    public static Garment Of(string part, string kind, string? slot = null, double? hem = null, double? flare = null, string? from = null) =>
        new() { Part = part, Kind = kind, Slot = slot, Hem = hem, Flare = flare, From = from };
}

public sealed class HandItem
{
    public const string Right = "right", Left = "left", Both = "both", Shoulder = "shoulder", Back = "back";

    public string Id { get; set; } = "";
    public string Hand { get; set; } = Right;
    public double LengthM { get; set; }
}

public sealed class DressRule
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Sex { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Callings { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Wealth { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Bands { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double Chance { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<Garment>? Wear { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Off { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, string>? Colours { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Item { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Details { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Wearing { get; set; }
}

public sealed class NameRule
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Sex { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Callings { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Wealth { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double Chance { get; set; }
    public string Pattern { get; set; } = "";
}

public sealed class DayRules
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? HolyDay { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? HolyAt { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<double>? Prayers { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<double>? Rest { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? MarketDays { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? DarkBy { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? NightLights { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? NightShare { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, List<TimetableStop>>? Timetables { get; set; }
}

public sealed class TimetableStop
{
    public const string Home = "home", Work = "work", Out = "out", Back = "back", Shop = "shop", Rest = "rest", Prayers = "prayers", Watch = "watch", None = "none", Away = "away";
    public const string Anyone = "*", Dark = "dark", Holy = "holy", Outsider = "outsider";
    static readonly string[] Conditions = [Work, "man", "woman", CrowdEntry.Child, CrowdEntry.Youth, CrowdEntry.Adult, CrowdEntry.Elder, Rest, Prayers, "market", Holy];

    public double At { get; set; }
    public string To { get; set; } = Home;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? From { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double After { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double Play { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double Chance { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double Hours { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? If { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool With { get; set; }

    public static bool Known(string to) => to.Split('|').All(o => o.Split(':')[0] is var w && (w is Home or Work or Out or Back or Shop or Rest or Prayers or Watch or None or Away
        || Enum.TryParse<CrowdUse>(w, true, out _)));

    public bool Holds() => Known(To) && From is null or Dark or Holy && At is >= -24 and <= 24 && After is >= 0 and <= 24 && Chance is >= 0 and <= 1 && Play >= 0 && Hours >= 0
        && (If ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).All(c => Conditions.Contains(c.TrimStart('!')));
}

public sealed class Nationality
{
    public string Id { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Name { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Base { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Culture { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Year { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Faith { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Face { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, double>? Heights { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Skins { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Hairs { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Eyes { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, List<string>>? Dyes { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, string>? Materials { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? ArmyCoats { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? ArmyFacings { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, string>? CallingNames { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, double>? Callings { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<DressRule>? Dress { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, List<string>>? Names { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<NameRule>? Naming { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DayRules? Day { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, double>? Kinds { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<TrafficRig>? Traffic { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Strangers { get; set; }
}

public sealed class CrowdLook
{
    public const string West = "west", Near = "near", Far = "far", South = "south";
    public static readonly IReadOnlyList<string> Faces = [West, Near, Far, South];

    public string Sex { get; set; } = CrowdEntry.Man;
    public string Band { get; set; } = CrowdEntry.Adult;
    public string Face { get; set; } = West;
    public double HeightM { get; set; }
    public double Build { get; set; } = 1;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double Stoop { get; set; }
    public List<Garment> Worn { get; set; } = new();
    public Dictionary<string, string> Colours { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Item { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Details { get; set; }

    public Garment? Part(string part) => Worn.Find(g => g.Part == part);

    public void Wear(Garment g)
    {
        Worn.RemoveAll(w => w.Part == g.Part);
        Worn.Add(g.Clone());
        Worn.Sort((a, b) => Order(a.Part).CompareTo(Order(b.Part)));
    }

    public void TakeOff(string part) => Worn.RemoveAll(w => w.Part == part);

    static int Order(string part) => Wardrobe.Parts.ToList().IndexOf(part) is var i && i >= 0 ? i : int.MaxValue;

    public CrowdLook Clone() => new()
    {
        Sex = Sex, Band = Band, Face = Face, HeightM = HeightM, Build = Build, Stoop = Stoop, Worn = Worn.Select(g => g.Clone())
            .ToList(), Colours = new(Colours), Item = Item, Details = Details?.ToList(),
    };
}

public sealed class CrowdEntry
{
    public const string Man = "m", Woman = "f";
    public const string Child = "child", Youth = "youth", Adult = "adult", Elder = "elder";
    public const string Poor = "poor", Middle = "middle", Rich = "rich";

    public int Id { get; set; }
    public int Seed { get; set; }
    public string Nationality { get; set; } = "";
    public string Name { get; set; } = "";
    public string Sex { get; set; } = Man;
    public string Band { get; set; } = Adult;
    public long Born { get; set; }
    public string Calling { get; set; } = "";
    public string Wealth { get; set; } = Poor;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Faith { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Household { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Home { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Work { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CrowdLook? Look { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Kind { get; set; }
}
