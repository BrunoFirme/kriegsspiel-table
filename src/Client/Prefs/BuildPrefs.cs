using System;
using KTable.Core.Model;

namespace KTable.Client;

public sealed class BuildPrefs
{
    public const string Laying = "lay", Drawing = "draw", Brushing = "brush", Pick = "pick";
    public const string Places = "places", Roads = "roads", Blocks = "blocks", Things = "buildings", Homes = "houses", Details = "details", Lamps = "lamps", Redoubt = "redoubt";
    public const string Grow = "grow", Shrink = "shrink", Round = "round", Painted = "painted", Cornered = "traced";
    public static readonly string[] Outlines = [Round, Painted, Cornered];
    public static readonly string[] Modes = [Laying, Drawing, Brushing, Pick];
    public static readonly string[] Draws = [Roads, Blocks, Things];
    public static readonly string[] Brushes = [Homes, Details, Lamps, Grow, Shrink];
    public const string NoName = "none", Typed = "typed";
    public static readonly string[] Namings = [NoName, Typed];
    public static readonly string[] PlaceKinds = [BuiltKinds.Settlement, BuiltKinds.Fort, Redoubt];
    const int UsualBastions = 5;
    public const float SmallestBrush = 1, LargestBrush = 80;

    public string Mode { get; set; } = Laying;
    public string Drawn { get; set; } = Roads;
    public string Brushed { get; set; } = Homes;
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
    public string Surface { get; set; } = "";
    public bool FromCorner { get; set; }
    public string Outline { get; set; } = Round;
    public float Plots { get; set; } = 0.5f;
    public float Buildings { get; set; } = 0.25f;
    public bool Shares { get; set; }
    public float Brush { get; set; } = 8;
    public bool Thins { get; set; }
    public bool LampsOn { get; set; }
    public string Naming { get; set; } = NoName;
    public string Manner { get; set; } = Manners.Id(Manners.Continental);

    public string Activity() => Mode switch { Laying => Places, Drawing => Drawn, Brushing => Brushed, _ => Pick };

    public void Do(string activity)
    {
        if (Array.IndexOf(Draws, activity) >= 0) (Mode, Drawn) = (Drawing, activity);
        else if (Array.IndexOf(Brushes, activity) >= 0) (Mode, Brushed) = (Brushing, activity);
        else Mode = activity == Pick ? Pick : Laying;
    }

    static float Share(float v, float otherwise) => float.IsFinite(v) ? Math.Clamp(v, 0, 1) : otherwise;

    public void Normalise()
    {
        if (Mode is Roads or Details or Lamps) Do(Mode);
        else if (Mode is "trace") Do(Blocks);
        else if (Mode is Places) Mode = Laying;
        if (Place == BuiltKinds.Building)
        {
            Place = BuiltKinds.Settlement;
            if (Mode == Laying) Do(Things);
        }
        if (Array.IndexOf(Modes, Mode) < 0) Mode = Laying;
        if (Array.IndexOf(Draws, Drawn) < 0) Drawn = Roads;
        if (Array.IndexOf(Brushes, Brushed) < 0) Brushed = Homes;
        if (Array.IndexOf(PlaceKinds, Place) < 0) Place = BuiltKinds.Settlement;
        if (Catalogue.Find(Block) == null) Block = "house";
        if (Array.IndexOf(Namings, Naming) < 0) Naming = NoName;
        if (Array.IndexOf(Outlines, Outline) < 0) Outline = Round;
        if (Array.IndexOf(Plans.All, Plan) < 0) Plan = "";
        Manner = Manners.Parse(Manner).Code;
        if (!BuiltRoads.Roads.Contains(Line)) Line = "road";
        Houses = Share(Houses, 0.5f);
        Spread = Share(Spread, (float)Settlements.UsualSpread);
        Order = Share(Order, (float)Settlements.UsualOrder);
        Walls = Share(Walls, 0);
        Outworks = Share(Outworks, 0.5f);
        Plots = Share(Plots, 0.5f);
        Buildings = Share(Buildings, 0.25f);
        Bastions = Bastions < Forts.FewestBastions ? UsualBastions : Math.Min(Bastions, Forts.MostBastions);
        Brush = float.IsFinite(Brush) ? Math.Clamp(Brush, SmallestBrush, LargestBrush) : 8;
    }
}
