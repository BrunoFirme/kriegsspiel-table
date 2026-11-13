using System.Text.Json.Serialization;

namespace KTable.Core.Model;

public static class BuiltKinds
{
    public const string Settlement = "settlement", Fort = "fort", Building = "building", Road = "road", Water = "water", Details = "details";
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

    public BuiltPart Clone() => new() { Kind = Kind, Class = Class, At = new(At), Turn = Turn, Kept = Kept };
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
        foreach (var p in moved.Parts) Shift(p.At, dx, dy);
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
