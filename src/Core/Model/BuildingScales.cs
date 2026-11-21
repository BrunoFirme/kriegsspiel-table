namespace KTable.Core.Model;

public static class BuildingScales
{
    const double Same = 1e-9;

    public static double By(BuiltThing thing, double builtPerMetre, double wasPerMetre = 0) =>
        builtPerMetre > 0 && thing.Setting(BuiltThings.Scale, wasPerMetre) is > 0 and var laidAt ? builtPerMetre / laidAt : 1;

    public static bool Fits(BuiltThing thing, double builtPerMetre, double wasPerMetre = 0) => Grown(thing, By(thing, builtPerMetre, wasPerMetre), Middle(thing)) != null;

    public static BuiltThing? Changed(BuiltSite site, BuiltThing old, double wasPerMetre = 0)
    {
        double by = By(old, site.UnitsPerMetre, wasPerMetre);
        if (Math.Abs(by - 1) < Same) return null;
        if (old.Kind is BuiltKinds.Road or BuiltKinds.Water or BuiltKinds.Works && !BuiltThings.Fixed(old))
            return Stamped(BuiltThings.Remade(site, Stamped(old.Clone(), site.UnitsPerMetre), old.Seed), site.UnitsPerMetre);
        if (Grown(old, by, Middle(old)) is not { } grown) return null;
        if (BuiltThings.Fixed(old) || old.Kind == BuiltKinds.Building || !BuiltThings.Remakes(old)) return Stamped(grown, site.UnitsPerMetre);
        if (grown.Kind == BuiltKinds.Details) grown.Settings[BuiltDetails.Radius] = grown.Setting(BuiltDetails.Radius, 0) * by;
        return Stamped(BuiltThings.Remade(site, Stamped(grown, site.UnitsPerMetre), old.Seed), site.UnitsPerMetre);
    }

    static BuiltThing Stamped(BuiltThing thing, double builtPerMetre)
    {
        thing.Settings[BuiltThings.Scale] = builtPerMetre;
        return thing;
    }

    public static SheetPoint Middle(BuiltThing thing)
    {
        var given = BuiltGeometry.Points(thing.Outline);
        if (thing.Kind is BuiltKinds.Settlement or BuiltKinds.Fort or BuiltKinds.Building && given.Count > 0) return given[0];
        return BuiltGeometry.Box(thing) is var (low, high) ? new((int)(((long)low.X + high.X) / 2), (int)(((long)low.Y + high.Y) / 2)) : new(0, 0);
    }

    public static BuiltThing? Grown(BuiltThing thing, double by, SheetPoint about)
    {
        var grown = thing.Clone();
        if (!Grow(grown.Outline, by, about)) return null;
        foreach (var part in grown.Parts)
            if (!Grow(part.At, by, about) || part.LaidAt != null && !Grow(part.LaidAt, by, about)) return null;
        return grown;
    }

    static bool Grow(List<int> at, double by, SheetPoint about)
    {
        for (int i = 0; i + 1 < at.Count; i += 2)
        {
            double x = Math.Round(about.X + (at[i] - (double)about.X) * by), y = Math.Round(about.Y + (at[i + 1] - (double)about.Y) * by);
            if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue) return false;
            (at[i], at[i + 1]) = ((int)x, (int)y);
        }
        return true;
    }
}
