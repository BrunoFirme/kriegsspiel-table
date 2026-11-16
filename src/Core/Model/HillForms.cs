namespace KTable.Core.Model;

public static class HillForms
{
    public const string Tell = "tell", Terp = "terp";
    public const double TellM = 6, TellSlopeM = 60, TerpM = 3, TerpSlopeM = 30;
    const double TopReach = 0.6, TellShare = 0.62, TerpShare = 0.5, LowM = 3, FlatGrade = 0.03;
    const int MoundSteps = 20;

    static bool High(Landform form) => form is Landform.Summit or Landform.Knoll or Landform.Ridge or Landform.Spur or Landform.Rim;

    public static Spot? Top(Landforms? land, int manner, double within)
    {
        if (land == null || Slopes.Of(manner).Hill is HillForm.Camp or HillForm.Ridgeline) return null;
        var top = land.Highest(within * TopReach);
        return High(land.At(top)) ? top : null;
    }

    public static bool Crowned(int manner) => Slopes.Of(manner).Hill is not (HillForm.Yamajiro or HillForm.Hillfort or HillForm.Camp or HillForm.Ridgeline);

    static bool Tells(int manner) => Manners.Known(manner) is Manners.Egyptian or Manners.Saracen or Manners.Persian;

    static bool Terps(int manner) => Manners.Known(manner) is Manners.Northern or Manners.Tribal;

    public static BuiltPart? Mound(Landforms? land, BuiltSite site, Spot centre, double within)
    {
        if (land == null || land.Grade(centre) > FlatGrade || land.AboveWater(centre) is not { } above || above > site.M(LowM)) return null;
        bool tell = Tells(site.Manner), terp = Terps(site.Manner) && (site.KindAt(centre) == GroundKinds.Marsh || land.At(centre) == Landform.Floodplain);
        if (!tell && !terp) return null;
        double reach = within * (tell ? TellShare : TerpShare);
        var ring = Enumerable.Range(0, MoundSteps).Select(i => centre + Spot.Toward(i * 2 * Math.PI / MoundSteps) * reach);
        return BuiltSite.Area(tell ? Tell : Terp, ring);
    }
}
