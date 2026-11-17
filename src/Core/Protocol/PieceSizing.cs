using KTable.Core.Model;

namespace KTable.Core.Protocol;

static class PieceSizing
{
    public static string? Apply(GameState g, LayerCommand c, ref Command? inverse) => c switch
    {
        SetScale sz => Resize(g, sz, ref inverse),
        SetScales ss => Keep(g, ss, ref inverse),
        _ => "not a size",
    };

    static bool Fits(double scale) => scale >= PieceInstance.LeastScale && scale < PieceInstance.MostScale;

    static bool Stretches(double? by) => by is null or >= PieceInstance.LeastStretch and <= PieceInstance.MostStretch;

    static string? Resize(GameState g, SetScale sz, ref Command? inverse)
    {
        if (g.Find(sz.Layer, sz.Id) is not { } p) return "no such piece";
        if (!Fits(sz.Scale)) return "bad scale";
        if (!Stretches(sz.Wide) || !Stretches(sz.Deep) || !Stretches(sz.Tall)) return "bad width, depth or height";
        inverse = new SetScale { Layer = sz.Layer, Id = sz.Id, Scale = p.Scale, Wide = p.WideBy, Deep = p.DeepBy, Tall = p.TallBy };
        p.Scale = sz.Scale;
        if (sz.Wide is { } wide) p.Wide = wide == 1 ? null : wide;
        if (sz.Deep is { } deep) p.Deep = deep == 1 ? null : deep;
        if (sz.Tall is { } tall) p.Tall = tall == 1 ? null : tall;
        return null;
    }

    static string? Keep(GameState g, SetScales ss, ref Command? inverse)
    {
        if (g.Find(ss.Layer, ss.Id) is not { } p) return "no such piece";
        if (!ss.Scales.All(Fits)) return "bad scale";
        inverse = new SetScales { Layer = ss.Layer, Id = ss.Id, Scales = p.Scales?.ToList() ?? new() };
        p.Scales = ss.Scales.Count > 0 ? ss.Scales.Distinct().ToList() : null;
        return null;
    }
}
