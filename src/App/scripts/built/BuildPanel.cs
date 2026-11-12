using System;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core.Model;

namespace KTable.App;

sealed class BuildPanel : HudPart
{
    readonly VBoxContainer _box;
    readonly Action _rebuild;

    public BuildPanel(HudContext ctx, VBoxContainer box, Action rebuild) : base(ctx)
    {
        _box = box;
        _rebuild = rebuild;
    }

    static readonly (string Key, string Text, string Tip)[] Modes =
    {
        (BuildPrefs.Places, "Places", "Settlements, forts and single buildings"),
        (BuildPrefs.Roads, "Roads", "Roads, tracks, streams and rivers"),
        (BuildPrefs.Details, "Details", "Plot boundaries, farms and their lanes"),
        (BuildPrefs.Pick, "Pick", "Pick what is built to move it, lay it afresh or remove it · Alt+click picks from any of these"),
    };

    static readonly (string Key, string Text, string Tip)[] Places =
    {
        (BuiltKinds.Settlement, "Settlement", "A hamlet, village, town or city, by the size of the drag"), (BuiltKinds.Fort, "Fort", "A star of four to eight bastions"),
        (BuildPrefs.Redoubt, "Redoubt", "A plain square earthwork"), (BuiltKinds.Building, "Building", "One house, church, farm, barn, mill or public building"),
    };

    static readonly (string Key, string Text)[] Lines = { ("track", "Track"), ("road", "Road"), ("chaussee", "Chaussée"), ("stream", "Stream"), ("river", "River") };

    const float NoWalls = 0, Walled = 0.5f, Bastioned = 1, NoOutworks = 0, Glacis = 0.2f, Ravelins = 1;
    const double GlacisFrom = 0.1, RavelinsFrom = 0.35;

    BuildPrefs P => Cfg.Build;

    public bool Building => T.Tool == Tool.Build;

    public void Add()
    {
        _box.AddChild(Chips(Modes, P.Mode, 4, key => P.Mode = key));
        if (P.Mode == BuildPrefs.Places) AddPlaces();
        else if (P.Mode == BuildPrefs.Roads) AddRoads();
        else if (P.Mode == BuildPrefs.Details) AddDetails();
        else AddPick();
    }

    void Chosen()
    {
        Cfg.Save();
        T.Build.Cancel();
        if (Building) _rebuild();
        else T.SetTool(Tool.Build);
    }

    GridContainer Chips((string Key, string Text, string Tip)[] items, string now, int columns, Action<string> set)
    {
        var grid = new GridContainer { Columns = columns, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", Ui.Gap.Tight);
        grid.AddThemeConstantOverride("v_separation", Ui.Gap.Tight);
        foreach (var (key, text, tip) in items)
        {
            var b = new Button
            {
                Text = text, ToggleMode = true, ButtonPressed = now == key, FocusMode = Control.FocusModeEnum.None, TooltipText = tip,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 26),
            };
            b.Pressed += () => { set(key); Chosen(); };
            grid.AddChild(b);
        }
        return grid;
    }

    void ChipRow(string label, (string Key, string Text, string Tip)[] items, string now, Action<string> set)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(ReliefPanel.LabelWide, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        row.AddChild(Chips(items, now, items.Length, set));
        _box.AddChild(row);
    }

    HSlider Row(string label, double min, double max, double step, double value, Func<double, string> show, Action<double> set, string tip) =>
        ReliefPanel.Row(_box, Cfg, label, min, max, step, value, show, set, tip);

    static string Percent(double x) => $"{x * 100:0}%";

    void AddPlaces()
    {
        _box.AddChild(Chips(Places, P.Place, 2, key => P.Place = key));
        if (P.Place == BuiltKinds.Settlement)
        {
            Row("Houses", 0, 1, 0.05, P.Houses, Percent, x => P.Houses = (float)x, "Scattered houses with gardens, or packed streets");
            ChipRow("Walls", new[] { ("none", "None", "An open place"), ("wall", "Walled", "A wall with towers and gates"), ("bastions", "Bastioned", "A bastioned trace with moat, ravelins and glacis") },
                P.Walls >= Settlements.BastionedFrom ? "bastions" : P.Walls >= Settlements.WalledFrom ? "wall" : "none", key => P.Walls = key == "bastions" ? Bastioned : key == "wall" ? Walled : NoWalls);
            ChipRow("Plan", new[] { ("grown", "Grown", "Lanes that grew from the road"), ("grid", "Grid", "Streets laid out square") }, P.Grid ? "grid" : "grown", key => P.Grid = key == "grid");
        }
        else if (P.Place == BuiltKinds.Fort)
        {
            Row("Bastions", Forts.FewestBastions, Forts.MostBastions, 1, P.Bastions, x => $"{x:0}", x => P.Bastions = (int)x, "A star of four to eight bastions");
            ChipRow("Outworks", new[] { ("none", "None", "The trace and its moat alone"), ("glacis", "Glacis", "A glacis round the moat"), ("ravelins", "Ravelins", "A glacis, and ravelins before the curtains") },
                P.Outworks >= RavelinsFrom ? "ravelins" : P.Outworks >= GlacisFrom ? "glacis" : "none", key => P.Outworks = key == "ravelins" ? Ravelins : key == "glacis" ? Glacis : NoOutworks);
        }
        else if (P.Place == BuildPrefs.Redoubt)
            ChipRow("Outworks", new[] { ("none", "None", "The rampart and its ditch alone"), ("glacis", "Glacis", "A glacis round the ditch") },
                P.Outworks >= GlacisFrom ? "glacis" : "none", key => P.Outworks = key == "glacis" ? Glacis : NoOutworks);
        else _box.AddChild(Chips(BuiltThings.Blocks.Select(b => (b, char.ToUpperInvariant(b[0]) + b[1..], "")).ToArray(), P.Block, 3, key => P.Block = key));
    }

    void AddRoads()
    {
        _box.AddChild(Chips(Lines.Select(l => (l.Key, l.Text, "")).ToArray(), P.Line, 3, key => P.Line = key));
        var finds = new CheckBox { Text = "Finds its way", ButtonPressed = P.FindsWay, FocusMode = Control.FocusModeEnum.None, TooltipText = "Over the relief between waypoints; a stream runs down from the first" };
        finds.Toggled += on => { P.FindsWay = on; Cfg.Save(); };
        _box.AddChild(finds);
    }

    void AddDetails()
    {
        Row("Plots", 0, 1, 0.05, P.Plots, Percent, x => P.Plots = (float)x, "Big open fields, or small plots bounded by hedges and fences");
        Row("Buildings", 0, 1, 0.05, P.Buildings, Percent, x => P.Buildings = (float)x, "None, lone farms, or hamlets");
        Row("Size", BuildPrefs.SmallestBrush, BuildPrefs.LargestBrush, 0.5, P.Brush, x => $"{x:0.#} u", x => P.Brush = (float)x, "The brush's radius in map units");
    }

    void AddPick()
    {
        var picked = T.Build.Picked;
        _box.AddChild(Hint(picked == null ? "Nothing picked" : T.Build.PickedPart is { } part ? Title(part.Class) : Title(Forts.Plain(picked) ? BuildPrefs.Redoubt : picked.Kind)));
        if (picked == null) return;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        if (BuiltThings.Remakes(picked)) row.AddChild(Btn("Again", T.Build.Again, BuiltThings.Rerolls(picked) ? "Lay it afresh, another way; parts moved by hand stay" : "Lay it afresh over the ground as it is now"));
        row.AddChild(Btn("Remove", () => T.Build.Delete(), "Remove what is picked · Delete"));
        _box.AddChild(row);
    }

    static string Title(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
}
