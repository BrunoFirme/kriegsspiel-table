namespace KTable.Core.Model;

static class Siting
{
    const double Gentle = 0.15, FloodDear = 6, Sunless = 1.6, Sunny = 0.8, Sloping = 0.08, Facing = 0.5, Liked = 0.7, Disliked = 1.4, FlatOnly = 4, Neutral = 1;
    const double JoinM = 30, TongueMost = 2.4, ConfluenceReach = 1.2;

    public static Spot? Strelka(BuiltSite site, Spot centre, double within, double room)
    {
        var rivers = site.Others.SelectMany(o => o.Parts).Where(p => p.Kind == PartKinds.Line && p.Class is "river" or "stream")
            .Select(p => BuiltGeometry.Points(p.At).Select(Spot.Of).ToList()).Where(line => line.Count >= 2).ToList();
        Spot? best = null;
        for (int i = 0; i < rivers.Count; i++)
            foreach (bool first in new[] { true, false })
            {
                Spot end = first ? rivers[i][0] : rivers[i][^1], into = ((first ? rivers[i][1] : rivers[i][^2]) - end).Unit;
                for (int j = 0; j < rivers.Count; j++)
                {
                    if (j == i) continue;
                    var (near, main) = Nearest(rivers[j], end);
                    if ((near - end).Length > site.M(JoinM) || (near - centre).Length > within * ConfluenceReach) continue;
                    foreach (var way in new[] { main, main * -1 })
                    {
                        double angle = Math.Acos(Math.Clamp(into.Dot(way), -1, 1)), off = room / Math.Sin(angle / 2);
                        var at = near + (into + way).Unit * off;
                        if (angle > TongueMost || off > within || !site.Inside(at) || !site.Buildable(at) || site.Wet(at)) continue;
                        if (best is not { } known || (at - centre).Length < (known - centre).Length) best = at;
                    }
                }
            }
        return best;
    }

    static (Spot At, Spot Along) Nearest(List<Spot> line, Spot p)
    {
        (Spot At, Spot Along) best = (line[0], (line[1] - line[0]).Unit);
        double least = double.MaxValue;
        for (int k = 0; k + 1 < line.Count; k++)
        {
            var ab = line[k + 1] - line[k];
            var on = line[k] + ab * Math.Clamp((p - line[k]).Dot(ab) / Math.Max(ab.Dot(ab), 1e-18), 0, 1);
            if ((on - p).Length < least) (least, best) = ((on - p).Length, (on, ab.Unit));
        }
        return best;
    }

    static bool Alpine(int manner) => manner is Manners.Continental or Manners.Gothic or Manners.Northern or Manners.Western;

    static bool Waterborne(int manner) => manner is Manners.Egyptian or Manners.Pacific;

    static bool Hill(Landform form) => form is Landform.Summit or Landform.Knoll or Landform.Ridge or Landform.Spur or Landform.Rim or Landform.Upland;

    static bool Low(Landform form) => form is Landform.Floor or Landform.Terrace or Landform.Foot;

    public static double Dear(int manner, Landform form, Spot fall)
    {
        manner = Manners.Known(manner);
        double grade = fall.Length;
        if (manner == Manners.Mongol && grade > Gentle) return FlatOnly;
        double dear = form == Landform.Floodplain && !Waterborne(manner) ? FloodDear : Neutral;
        if (Alpine(manner) && grade > Sloping) dear *= fall.Y < -grade * Facing ? Sunless : fall.Y > grade * Facing ? Sunny : Neutral;
        if (manner == Manners.Chinese && grade > Sloping && fall.Y > grade * Facing) dear *= Sunny;
        return dear * manner switch
        {
            Manners.NorthAfrican => Hill(form) ? Liked : form == Landform.Floor ? Disliked : Neutral,
            Manners.Soviet or Manners.Japanese or Manners.Nippon or Manners.Chinese => Low(form) ? Liked : Hill(form) ? Disliked : Neutral,
            Manners.Byzantine or Manners.Ottoman or Manners.Saracen or Manners.Persian => Hill(form) || form == Landform.Fan ? Liked : Neutral,
            Manners.Rus or Manners.Egyptian => form is Landform.Terrace or Landform.Rim ? Liked : Neutral,
            Manners.Northern => form is Landform.Upland or Landform.Slope ? Liked : Neutral,
            Manners.Roman => form is Landform.Knoll or Landform.Plateau ? Liked : Neutral,
            _ => Neutral,
        };
    }
}
