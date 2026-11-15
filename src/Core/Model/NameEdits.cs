using KTable.Core.Protocol;

namespace KTable.Core.Model;

public static class NameEdits
{
    const double Straight = 1e-9;

    public static Stroke Moved(Stroke name, Vec2 by)
    {
        var moved = Json.Clone(name);
        moved.Points = name.Points.Select(p => p + by).ToList();
        return moved;
    }

    public static Stroke Turned(Stroke name, Vec2 about, double clockwise)
    {
        var turned = Json.Clone(name);
        double rad = -clockwise * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad);
        turned.Points = name.Points.Select(p => About(about, p - about, cos, sin)).ToList();
        if (name.Points.Count == 1)
        {
            double turn = Stroke.Turned(name.TurnDeg + clockwise);
            turned.Turn = turn < Straight || Stroke.FullTurn - turn < Straight ? null : turn;
        }
        return turned;
    }

    static Vec2 About(Vec2 about, Vec2 off, double cos, double sin) => new(about.X + off.X * cos - off.Z * sin, about.Z + off.X * sin + off.Z * cos);

    public static Stroke Sized(Stroke name, Vec2 about, double by, double letterNow)
    {
        double letter = Math.Clamp(letterNow * by, Stroke.LeastLetter, Stroke.MostLetter), k = letter / letterNow;
        var sized = Json.Clone(name);
        sized.Points = name.Points.Select(p => new Vec2(about.X + (p.X - about.X) * k, about.Z + (p.Z - about.Z) * k)).ToList();
        sized.Letter = letter;
        return sized;
    }

    public static Batch Relaid(List<Stroke> layer, string layerName, Stroke old, Stroke changed)
    {
        var laid = Json.Clone(changed);
        laid.Id = old.Id;
        int at = layer.IndexOf(old);
        return new Batch
        {
            Items =
            {
                new EraseStroke { Layer = layerName, StrokeId = old.Id },
                new Draw { Layer = layerName, Stroke = laid, Index = at < 0 ? null : at },
            },
        };
    }
}
