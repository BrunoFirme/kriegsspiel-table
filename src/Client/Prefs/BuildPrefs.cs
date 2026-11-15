using System;
using KTable.Core.Model;

namespace KTable.Client;

public sealed class BuildPrefs
{
    public const string Places = "places", Roads = "roads", Details = "details", Lamps = "lamps", Pick = "pick", Redoubt = "redoubt";
    public static readonly string[] Modes = [Places, Roads, Details, Lamps, Pick];
    public const string NoName = "none", Typed = "typed";
    public static readonly string[] Namings = [NoName, Typed];
    public static readonly string[] PlaceKinds = [BuiltKinds.Settlement, BuiltKinds.Fort, Redoubt, BuiltKinds.Building];
    const int UsualBastions = 5;
    public const float SmallestBrush = 1, LargestBrush = 80, SmallestReachM = 20, LargestReachM = 5000, UsualReachM = 250;

    public string Mode { get; set; } = Places;
    public string Place { get; set; } = BuiltKinds.Settlement;
    public float Houses { get; set; } = 0.5f;
    public float Spread { get; set; } = (float)Settlements.UsualSpread;
    public float Order { get; set; } = (float)Settlements.UsualOrder;
    public float Walls { get; set; }
    public string Plan { get; set; } = "";
    public int Bastions { get; set; } = UsualBastions;
    public float Outworks { get; set; } = 0.5f;
    public string Block { get; set; } = "house";
    public string Line { get; set; } = "road";
    public bool FindsWay { get; set; } = true;
    public float Plots { get; set; } = 0.5f;
    public float Buildings { get; set; } = 0.25f;
    public bool Shares { get; set; }
    public float Brush { get; set; } = 8;
    public bool LampsOn { get; set; }
    public float ReachM { get; set; } = UsualReachM;
    public string Naming { get; set; } = NoName;
    public string Manner { get; set; } = Manners.Id(Manners.Continental);

    static float Share(float v, float otherwise) => float.IsFinite(v) ? Math.Clamp(v, 0, 1) : otherwise;

    public void Normalise()
    {
        if (Array.IndexOf(Modes, Mode) < 0) Mode = Places;
        if (Array.IndexOf(PlaceKinds, Place) < 0) Place = BuiltKinds.Settlement;
        if (!BuiltThings.Blocks.Contains(Block)) Block = "house";
        if (Array.IndexOf(Namings, Naming) < 0) Naming = NoName;
        if (Array.IndexOf(Plans.All, Plan) < 0) Plan = "";
        Manner = Manners.Parse(Manner).Code;
        if (!BuiltRoads.Roads.Contains(Line) && !BuiltRoads.Waters.Contains(Line)) Line = "road";
        Houses = Share(Houses, 0.5f);
        Spread = Share(Spread, (float)Settlements.UsualSpread);
        Order = Share(Order, (float)Settlements.UsualOrder);
        Walls = Share(Walls, 0);
        Outworks = Share(Outworks, 0.5f);
        Plots = Share(Plots, 0.5f);
        Buildings = Share(Buildings, 0.25f);
        Bastions = Bastions < Forts.FewestBastions ? UsualBastions : Math.Min(Bastions, Forts.MostBastions);
        Brush = float.IsFinite(Brush) ? Math.Clamp(Brush, SmallestBrush, LargestBrush) : 8;
        ReachM = float.IsFinite(ReachM) ? Math.Clamp(ReachM, SmallestReachM, LargestReachM) : UsualReachM;
    }
}
