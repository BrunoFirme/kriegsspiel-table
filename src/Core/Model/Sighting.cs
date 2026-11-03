namespace KTable.Core.Model;

public sealed class SightingShape
{
    public string Kind { get; set; } = "circle";
    public Vec2 Center { get; set; }
    public double Radius { get; set; }
    public List<Vec2> Polygon { get; set; } = new();

    public bool Contains(Vec2 p)
    {
        if (Kind == "circle") return (p - Center).Length <= Radius;
        if (Radius > 0 && (p - Center).Length > Radius) return false;
        bool inside = false;
        for (int i = 0, j = Polygon.Count - 1; i < Polygon.Count; j = i++)
        {
            var a = Polygon[i]; var b = Polygon[j];
            if ((a.Z > p.Z) != (b.Z > p.Z) && p.X < (b.X - a.X) * (p.Z - a.Z) / (b.Z - a.Z) + a.X)
                inside = !inside;
        }
        return inside;
    }
}

public sealed class Zone
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Color { get; set; } = "#E0B040";
    public List<Vec2> Polygon { get; set; } = new();

    public bool Contains(Vec2 p) => new SightingShape { Kind = "polygon", Polygon = Polygon }.Contains(p);
}

public sealed class DiceRoll
{
    public long Seq { get; set; }
    public string Actor { get; set; } = "";
    public int Sides { get; set; }
    public List<int> Results { get; set; } = new();
    public string Label { get; set; } = "";
}
