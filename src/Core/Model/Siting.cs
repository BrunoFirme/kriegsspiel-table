namespace KTable.Core.Model;

static class Siting
{
    const double FloodDear = 6, Sunless = 1.6, Sunny = 0.8, Sloping = 0.08, Facing = 0.5, Liked = 0.7, Disliked = 1.4, FlatOnly = 4, Neutral = 1;

    static bool Alpine(int manner) => manner is Manners.Continental or Manners.Gothic or Manners.Northern or Manners.Western;

    static bool Waterborne(int manner) => manner is Manners.Egyptian or Manners.Pacific;

    static bool Hill(Landform form) => form is Landform.Summit or Landform.Knoll or Landform.Ridge or Landform.Spur or Landform.Rim or Landform.Upland;

    static bool Low(Landform form) => form is Landform.Floor or Landform.Terrace or Landform.Foot;

    public static double Dear(int manner, Landform form, Spot fall)
    {
        manner = Manners.Known(manner);
        double grade = fall.Length;
        if (manner == Manners.Mongol && grade > Sloping) return FlatOnly;
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
