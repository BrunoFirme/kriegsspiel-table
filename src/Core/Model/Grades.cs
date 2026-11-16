namespace KTable.Core.Model;

public static class Grades
{
    public const string Stairs = "stairs", Ramp = "ramp", Holloway = "holloway", Tunnel = "tunnel", Viaduct = "viaduct", Cutting = "cutting", Embankment = "embankment";
    public const double MainLine = 0.0125, MountainLine = 0.025, BranchLine = 0.04, Motorway = 0.05, MotorwayMost = 0.08;
    public const double HeldM = 1.5, StairsSteepest = 0.6, RampSteepest = 0.3, HollowFrom = 0.08, CuttingMostM = 20, EmbankingMostM = 15, AimM = 160;
    static readonly double[] Lines = [MainLine, MountainLine, BranchLine];
    static readonly string[] Ranked = ["railway", "highway", "chaussee", "road", "boulevard", "street", "lane", "track", "path"];

    public static double Of(int manner, string cls) => cls switch
    {
        "railway" => MainLine,
        "highway" => Math.Clamp(Slopes.Of(manner).Road, Motorway, MotorwayMost),
        "boulevard" => Slopes.Of(manner).Street,
        _ => Slopes.Of(manner).Grade(cls),
    };

    public static IReadOnlyList<double> Ruling(int manner, string cls) => cls == "railway" ? Lines : [Of(manner, cls)];

    public static bool Graded(BuiltPart part) => part.Kind == PartKinds.Line && Array.IndexOf(Ranked, part.Class) >= 0;

    public static int Rank(string cls) => Math.Max(0, Array.IndexOf(Ranked, cls));

    public static bool Engineered(string cls) => cls is "railway" or "highway";

    public static bool Climbs(string cls) => cls is "street" or "lane" or "path";

    public static bool Pinned(string cls) => cls is Stairs or Ramp;

    public static bool Spanned(string cls) => cls is Tunnel or Viaduct;

    public static bool Banked(string cls) => cls is Cutting or Embankment;

    public static bool Shapes(BuiltPart part) =>
        Graded(part) || part.Kind == PartKinds.Line && (Pinned(part.Class) || Spanned(part.Class) || part.Class == Holloway) || part.Kind == PartKinds.Area && Banked(part.Class);

    public static bool Mule(int manner) => manner is Manners.Roman or Manners.Ottoman or Manners.Saracen or Manners.Byzantine or Manners.Persian or Manners.NorthAfrican or Manners.Japanese;

    public static bool Sinks(int manner) => manner is Manners.Gothic or Manners.Northern or Manners.Continental or Manners.American or Manners.Rus;

    public static double HalfM(string cls) => cls switch
    {
        "highway" => 6,
        "chaussee" => 4.75,
        "boulevard" => 7,
        "street" or "road" or "railway" => 3.25,
        "lane" => 1.8,
        "track" => 1.3,
        _ => 1,
    };
}
