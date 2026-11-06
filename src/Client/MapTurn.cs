using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client;

public static class MapTurn
{
    const double TouchShare = 0.01;

    public static List<SheetDef> Joined(IReadOnlyList<SheetDef> sheets, string id)
    {
        if (sheets.FirstOrDefault(s => s.Id == id) is not { } start) return new();
        var boxes = sheets.ToDictionary(s => s.Id, Box);
        var group = new List<SheetDef> { start };
        var seen = new HashSet<string> { id };
        for (int i = 0; i < group.Count; i++)
            foreach (var s in sheets)
                if (!seen.Contains(s.Id) && Meet(boxes[group[i].Id], boxes[s.Id])) { seen.Add(s.Id); group.Add(s); }
        return group;
    }

    public static Batch Turned(IReadOnlyList<SheetDef> group, IReadOnlyList<PieceInstance> pieces, string layer, double deg)
    {
        var boxes = group.Select(Box).ToList();
        var pivot = new Vec2((boxes.Min(b => b.x0) + boxes.Max(b => b.x1)) / 2, (boxes.Min(b => b.z0) + boxes.Max(b => b.z1)) / 2);
        double r = deg * Math.PI / 180, cos = Math.Cos(r), sin = Math.Sin(r);
        Vec2 Turn(Vec2 p)
        {
            var d = p - pivot;
            return new Vec2(Math.Round(pivot.X + d.X * cos + d.Z * sin, 4), Math.Round(pivot.Z - d.X * sin + d.Z * cos, 4));
        }
        var items = new List<Command>();
        foreach (var s in group) items.Add(new MoveSheet { Id = s.Id, Pos = Turn(s.Pos), RotDeg = s.RotDeg + deg, Z = s.Z });
        var carried = pieces.SelectMany(p => p.Attached ?? []).ToHashSet();
        foreach (var p in pieces.Where(p => !carried.Contains(p.Id) && group.Any(s => Ground.Uv(s, p.Pos) != null)))
            items.Add(new MovePiece { Layer = layer, Id = p.Id, Pos = Turn(p.Pos), RotDeg = p.RotDeg + deg, Stack = p.Stack, Y = p.Y });
        return new Batch { Items = items };
    }

    static (double x0, double z0, double x1, double z1) Box(SheetDef s)
    {
        var c = s.Crop ?? SheetGeometry.Whole;
        var corners = new[] { (0.0, 0.0), (1.0, 0.0), (0.0, 1.0), (1.0, 1.0) }.Select(k => Ground.FromUv(s, c.X + k.Item1 * c.W, c.Y + k.Item2 * c.H)).ToList();
        double grow = TouchShare * Math.Min(s.Size.W, s.Size.H);
        return (corners.Min(p => p.X) - grow, corners.Min(p => p.Z) - grow, corners.Max(p => p.X) + grow, corners.Max(p => p.Z) + grow);
    }

    static bool Meet((double x0, double z0, double x1, double z1) a, (double x0, double z0, double x1, double z1) b) =>
        a.x0 <= b.x1 && b.x0 <= a.x1 && a.z0 <= b.z1 && b.z0 <= a.z1;
}
