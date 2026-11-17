namespace KTable.Core.Model;

public static class PartHandles
{
    public const int Sides = 4, Count = 8;
    const double WingShare = 0.6, WingSunk = 0.08;

    static List<Spot> Corners(BuiltPart part) => BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();

    public static int Lone(BuiltThing thing)
    {
        if (thing.Kind != BuiltKinds.Building) return -1;
        var blocks = Enumerable.Range(0, thing.Parts.Count).Where(i => BuiltThings.Stretches(thing.Parts[i])).ToList();
        return blocks.Count == 1 ? blocks[0] : -1;
    }

    public static IReadOnlyList<Spot> Of(BuiltPart part)
    {
        var c = Corners(part);
        return [(c[0] + c[1]) * 0.5, (c[1] + c[2]) * 0.5, (c[2] + c[3]) * 0.5, (c[3] + c[0]) * 0.5, c[0], c[1], c[2], c[3]];
    }

    public static int Near(BuiltPart part, Spot p, double reach)
    {
        var handles = Of(part);
        int best = -1;
        for (int i = 0; i < handles.Count; i++)
            if ((handles[i] - p).Length is var d && d < reach)
                (best, reach) = (i, d);
        return best;
    }

    public static List<Spot> Pulled(BuiltPart part, int handle, Spot to, double least)
    {
        var c = Corners(part);
        Spot u = (c[1] - c[0]).Unit, v = (c[3] - c[0]).Unit;
        double a0 = 0, a1 = (c[1] - c[0]).Length, d0 = 0, d1 = (c[3] - c[0]).Length, s = (to - c[0]).Dot(u), t = (to - c[0]).Dot(v);
        if (handle is 3 or 4 or 7) a0 = Math.Min(s, a1 - least);
        if (handle is 1 or 5 or 6) a1 = Math.Max(s, a0 + least);
        if (handle is 0 or 4 or 5) d0 = Math.Min(t, d1 - least);
        if (handle is 2 or 6 or 7) d1 = Math.Max(t, d0 + least);
        return [c[0] + u * a0 + v * d0, c[0] + u * a1 + v * d0, c[0] + u * a1 + v * d1, c[0] + u * a0 + v * d1];
    }

    public static BuiltThing WithPulled(BuiltThing thing, int part, int handle, Spot to, double least)
    {
        var pulled = thing.Clone();
        pulled.Parts[part].At = BuiltGeometry.Flat(Pulled(thing.Parts[part], handle, to, least).Select(p => p.Stored));
        pulled.Parts[part].Kept = true;
        return pulled;
    }

    public static BuiltPart? Wing(BuiltPart part, Spot from, Spot to, double least)
    {
        var c = Corners(part);
        var middle = (c[0] + c[2]) * 0.5;
        int side = Enumerable.Range(0, Sides).MinBy(i => from.FromSegment(c[i], c[(i + 1) % Sides]));
        Spot a = c[side], b = c[(side + 1) % Sides], along = (b - a).Unit, away = along.Left.Dot((a + b) * 0.5 - middle) >= 0 ? along.Left : along.Left * -1;
        double run = (b - a).Length, other = (c[(side + 2) % Sides] - b).Length, wide = Math.Max(least, Math.Min(run, Math.Min(run, other) * WingShare));
        double far = (to - a).Dot(away), sunk = Math.Min(other, wide) * WingSunk;
        if (far < least) return null;
        double foot = Math.Clamp((from - a).Dot(along), wide / 2, Math.Max(wide / 2, run - wide / 2));
        var wing = BuiltSite.Block(part.Class, a + along * foot + away * ((far - sunk) / 2), away, far + sunk, wide);
        (wing.Kept, wing.Storeys, wing.Roof, wing.Cover) = (true, part.Storeys, part.Roof, part.Cover);
        return wing;
    }

    public static BuiltThing? WithWing(BuiltThing thing, int part, Spot from, Spot to, double least)
    {
        if (Wing(thing.Parts[part], from, to, least) is not { } wing) return null;
        var winged = thing.Clone();
        var body = winged.Parts[part];
        if (body.Group == 0) body.Group = winged.Parts.Max(p => p.Group) + 1;
        wing.Group = body.Group;
        body.Kept = true;
        winged.Parts.Insert(part + 1, wing);
        return winged;
    }
}
