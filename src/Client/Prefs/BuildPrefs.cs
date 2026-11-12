using System;
using KTable.Core.Model;

namespace KTable.Client;

public sealed class BuildPrefs
{
    public const string Places = "places", Roads = "roads", Details = "details", Pick = "pick";
    public static readonly string[] Modes = [Places, Roads, Details, Pick];
    public static readonly string[] PlaceKinds = [BuiltKinds.Settlement, BuiltKinds.Fort, BuiltKinds.Building];
    public const float SmallestBrush = 1, LargestBrush = 80;

    public string Mode { get; set; } = Places;
    public string Place { get; set; } = BuiltKinds.Settlement;
    public float Houses { get; set; } = 0.5f;
    public float Walls { get; set; }
    public bool Grid { get; set; }
    public int Bastions { get; set; } = 5;
    public float Outworks { get; set; } = 0.5f;
    public string Block { get; set; } = "house";
    public string Line { get; set; } = "road";
    public bool FindsWay { get; set; } = true;
    public float Plots { get; set; } = 0.5f;
    public float Buildings { get; set; } = 0.25f;
    public float Brush { get; set; } = 8;

    static float Share(float v, float otherwise) => float.IsFinite(v) ? Math.Clamp(v, 0, 1) : otherwise;

    public void Normalise()
    {
        if (Array.IndexOf(Modes, Mode) < 0) Mode = Places;
        if (Array.IndexOf(PlaceKinds, Place) < 0) Place = BuiltKinds.Settlement;
        if (!BuiltThings.Blocks.Contains(Block)) Block = "house";
        if (!BuiltRoads.Roads.Contains(Line) && !BuiltRoads.Waters.Contains(Line)) Line = "road";
        Houses = Share(Houses, 0.5f);
        Walls = Share(Walls, 0);
        Outworks = Share(Outworks, 0.5f);
        Plots = Share(Plots, 0.5f);
        Buildings = Share(Buildings, 0.25f);
        Bastions = Bastions < Forts.FewestBastions ? 0 : Math.Min(Bastions, Forts.MostBastions);
        Brush = float.IsFinite(Brush) ? Math.Clamp(Brush, SmallestBrush, LargestBrush) : 8;
    }
}
