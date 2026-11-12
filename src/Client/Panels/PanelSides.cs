using KTable.Core.Model;

namespace KTable.Client.Panels;

public static class PanelSides
{
    public const double ObjectUnitsPerPanelUnit = 0.01;
    const double Margin = 1e-4;

    public static bool FacesAway(PanelBox b) => Math.Cos(b.Rotation.X * Math.PI / 180) * Math.Cos(b.Rotation.Y * Math.PI / 180) < 0;

    public static double Depth(PanelBox b)
    {
        double d = b.Depth;
        for (var at = b.Parent; at != null; at = at.Parent) d = at.Depth + (FacesAway(at) ? -d : d);
        return d;
    }

    public static double? Bottom(PieceDef? def) =>
        def is { ModuleRise: > 0 and var rise } ? -(def.ModuleOrigin ?? 0) / rise : null;

    public static bool Under(PanelBox b, double bottom) => -Depth(b) * ObjectUnitsPerPanelUnit < bottom - Margin;

    public static bool AnyUnder(PanelBox b, double bottom) => b.Active && (Under(b, bottom) || b.Children.Any(c => AnyUnder(c, bottom)));
}
