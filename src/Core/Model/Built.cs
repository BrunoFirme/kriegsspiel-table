using System.Text.Json.Serialization;

namespace KTable.Core.Model;

public static class BuiltKinds
{
    public const string Settlement = "settlement", Fort = "fort", Building = "building", Road = "road", Water = "water", Details = "details", Works = "works";
}

public static class PartKinds
{
    public const string Block = "block", Line = "line", Area = "area", Sign = "sign";
}

public sealed class BuiltPart
{
    public string Kind { get; set; } = PartKinds.Block;
    public string Class { get; set; } = "";
    public List<int> At { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Turn { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Kept { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int SizePercent { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<int>? Dark { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Group { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Storeys { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Roof { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Cover { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Surface { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Culture { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Wealth { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<int>? LaidAt { get; set; }

    public const int WholeSize = 100, LeastSizePercent = 20, MostSizePercent = 1000;

    [JsonIgnore] public double Size => SizePercent > 0 ? SizePercent / (double)WholeSize : 1;

    public BuiltPart Clone() => new()
    {
        Kind = Kind, Class = Class, At = new(At), Turn = Turn, Kept = Kept, SizePercent = SizePercent, Dark = Dark == null ? null : new(Dark), Group = Group,
        Storeys = Storeys, Roof = Roof, Cover = Cover, Surface = Surface, Culture = Culture, Wealth = Wealth, LaidAt = LaidAt == null ? null : new(LaidAt),
    };
}

public sealed class BuiltThing
{
    public string Id { get; set; } = "";
    public string Sheet { get; set; } = "";
    public string Kind { get; set; } = BuiltKinds.Building;
    public int Seed { get; set; }
    public Dictionary<string, double> Settings { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Name { get; set; }
    public List<int> Outline { get; set; } = new();
    public List<BuiltPart> Parts { get; set; } = new();

    public BuiltThing Clone() =>
        new() { Id = Id, Sheet = Sheet, Kind = Kind, Seed = Seed, Settings = new(Settings), Name = Name, Outline = new(Outline), Parts = Parts.Select(p => p.Clone()).ToList() };

    public double Setting(string name, double otherwise) => Settings.TryGetValue(name, out var v) ? v : otherwise;
}

public static class Looks
{
    public const int MostStoreys = 8;
    public static readonly IReadOnlyList<string> Roofs = ["gable", "hip", "halfhip", "pyramid", "flat", "lean", "mansard", "valley", "dome", "cone", "court", "vault"];
    public static readonly IReadOnlyList<string> Covers =
        ["tile", "slate", "thatch", "shingle", "lead", "mud", "lime", "turf", "stone", "copper", "kawara", "tin", "tar", "palm", "plank", "sheet", "pantile", "silt", "felt"];

    public const string Earth = "earth", Mud = "mud", Sand = "sand", Grass = "grass", Gravel = "gravel", Cobbles = "cobbles", Flags = "flags", Planks = "planks", Tar = "tar";
    public static readonly IReadOnlyList<string> Surfaces = [Earth, Mud, Sand, Grass, Gravel, Cobbles, Flags, Planks, Tar];
    static readonly string[] Ways = ["path", "track", "lane", "street", "road", "chaussee", "highway", "boulevard", "causeway", "paving"];

    public static bool Takes(BuiltPart part) => part.Kind == PartKinds.Block && part.At.Count >= 6;

    public static bool Paved(BuiltPart part) => part.Kind == PartKinds.Line && Array.IndexOf(Ways, part.Class) >= 0;

    public static string? Surface(string? asked) => asked != null && Surfaces.Contains(asked) ? asked : null;

    public static BuiltThing WithSurface(BuiltThing thing, int part, string? surface)
    {
        var set = thing.Clone();
        string cls = set.Parts[part].Class;
        foreach (var one in thing.Kind is BuiltKinds.Road ? set.Parts.Where(p => p.Kind == PartKinds.Line && p.Class == cls) : [set.Parts[part]])
        {
            one.Surface = Surface(surface);
            if (thing.Kind != BuiltKinds.Road) one.Kept = true;
        }
        return set;
    }

    public static BuiltThing With(BuiltThing thing, int part, int storeys, string? roof, string? cover)
    {
        var set = thing.Clone();
        var one = set.Parts[part];
        one.Storeys = Math.Clamp(storeys, 0, MostStoreys);
        one.Roof = roof != null && Roofs.Contains(roof) ? roof : null;
        one.Cover = cover != null && Covers.Contains(cover) ? cover : null;
        one.Kept = true;
        return set;
    }
}

public static class Groups
{
    public readonly struct Tying(List<BuiltPart> parts, BuiltPart? with) : IDisposable
    {
        readonly int _from = parts.Count;

        public void Dispose() => Tie(parts, _from, with);
    }

    public static Tying Open(List<BuiltPart> parts, BuiltPart? with = null) => new(parts, with);

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<List<BuiltPart>, System.Runtime.CompilerServices.StrongBox<int>> Counted = new();

    static int Next(List<BuiltPart> parts)
    {
        var last = Counted.GetValue(parts, listed => new(listed.Count == 0 ? 0 : listed.Max(p => p.Group)));
        return ++last.Value;
    }

    public static void Tie(List<BuiltPart> parts, int from, BuiltPart? with = null)
    {
        if (parts.Count - from + (with == null ? 0 : 1) < 2) return;
        int group = with is { Group: > 0 } ? with.Group : Next(parts);
        if (with != null) with.Group = group;
        for (int i = from; i < parts.Count; i++) parts[i].Group = group;
    }

    public static List<int> Of(BuiltThing thing, int part)
    {
        int group = part >= 0 && part < thing.Parts.Count ? thing.Parts[part].Group : 0;
        return group == 0 ? [part] : Enumerable.Range(0, thing.Parts.Count).Where(i => thing.Parts[i].Group == group).ToList();
    }

    public static void Apart(List<BuiltPart> fresh, List<BuiltPart> staying)
    {
        int taken = fresh.Count == 0 ? 0 : fresh.Max(p => p.Group);
        foreach (var part in staying.Where(p => p.Group > 0)) part.Group += taken;
    }
}

public readonly record struct SheetPoint(int X, int Y);

public static class BuiltGeometry
{
    public const int PerUnit = 1000;

    public static double FullHeight(SheetDef s) => s.Size.H / Math.Max(1e-6, (s.Crop ?? SheetGeometry.Whole).H);

    public static SheetPoint ToSheet(SheetDef s, Vec2 world)
    {
        var (u, v) = Ground.UvOf(s, world);
        return new((int)Math.Round(u * Ground.FullWidth(s) * PerUnit), (int)Math.Round(v * FullHeight(s) * PerUnit));
    }

    public static Vec2 ToWorld(SheetDef s, SheetPoint p) => Ground.FromUv(s, p.X / (Ground.FullWidth(s) * PerUnit), p.Y / (FullHeight(s) * PerUnit));

    public static List<SheetPoint> Points(List<int> at)
    {
        var points = new List<SheetPoint>(at.Count / 2);
        for (int i = 0; i + 1 < at.Count; i += 2) points.Add(new(at[i], at[i + 1]));
        return points;
    }

    public static List<int> Flat(IEnumerable<SheetPoint> points)
    {
        var at = new List<int>();
        foreach (var p in points)
        {
            at.Add(p.X);
            at.Add(p.Y);
        }
        return at;
    }

    public static (SheetPoint Low, SheetPoint High)? Box(BuiltThing thing)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
        foreach (var at in thing.Parts.Select(p => p.At).Append(thing.Outline))
            for (int i = 0; i + 1 < at.Count; i += 2)
            {
                x0 = Math.Min(x0, at[i]);
                x1 = Math.Max(x1, at[i]);
                y0 = Math.Min(y0, at[i + 1]);
                y1 = Math.Max(y1, at[i + 1]);
            }
        return x0 > x1 ? null : (new(x0, y0), new(x1, y1));
    }

    public static SheetPoint? NameSpot(BuiltThing thing) => Box(thing) is var (low, high) ? new SheetPoint(high.X, (low.Y + high.Y) / 2) : null;

    public static BuiltThing Moved(BuiltThing thing, int dx, int dy)
    {
        var moved = thing.Clone();
        Shift(moved.Outline, dx, dy);
        foreach (var p in moved.Parts)
        {
            Shift(p.At, dx, dy);
            if (p.LaidAt != null) Shift(p.LaidAt, dx, dy);
        }
        return moved;
    }

    static void Shift(List<int> at, int dx, int dy)
    {
        for (int i = 0; i + 1 < at.Count; i += 2)
        {
            at[i] += dx;
            at[i + 1] += dy;
        }
    }
}
