using System;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core.Model;

namespace KTable.App;

sealed class BuildPanel : HudPart
{
    public readonly FloatPanel Panel;
    readonly VBoxContainer _box;

    public BuildPanel(HudContext ctx, IHudShell shell) : base(ctx)
    {
        Panel = shell.MakePanel("build", "Build");
        _box = Panel.Body;
    }

    static readonly (string Key, string Text, string Tip)[] Modes =
    {
        (BuildPrefs.Places, "Places", "Settlements, forts and single buildings"),
        (BuildPrefs.Roads, "Roads", "Roads, tracks, streams and rivers"),
        (BuildPrefs.Details, "Details", "Plot boundaries, farms and their lanes"),
        (BuildPrefs.Pick, "Pick", "Pick what is built to move, reroll or remove it"),
    };

    static readonly (string Key, string Text)[] Places = { (BuiltKinds.Settlement, "Settlement"), (BuiltKinds.Fort, "Fort"), (BuiltKinds.Building, "Building") };

    static readonly (string Key, string Text)[] Lines = { ("track", "Track"), ("road", "Road"), ("chaussee", "Chaussée"), ("stream", "Stream"), ("river", "River") };

    BuildPrefs P => Cfg.Build;

    public void Rebuild()
    {
        if (_box == null || T == null) return;
        foreach (var c in _box.GetChildren()) { _box.RemoveChild(c); c.QueueFree(); }
        if (!ReliefTool.Scaled(S))
        {
            _box.AddChild(Hint(GroundKinds.NeedsScale));
            _box.AddChild(Btn("Scale…", () => T.SetTool(Tool.Calibrate), "Drag along the map's scale bar"));
            Panel?.Fit();
            return;
        }
        Choices(Modes.Select(m => (m.Key, m.Text, m.Tip)).ToArray(), P.Mode, 2, key => P.Mode = key);
        if (P.Mode == BuildPrefs.Places) AddPlaces();
        else if (P.Mode == BuildPrefs.Roads) AddRoads();
        else if (P.Mode == BuildPrefs.Details) AddDetails();
        else AddPick();
        Panel?.Fit();
    }

    void Chosen()
    {
        Cfg.Save();
        T.SetTool(Tool.Build);
        T.Build.Cancel();
        Rebuild();
    }

    void Choices((string Key, string Text, string Tip)[] items, string now, int columns, Action<string> set)
    {
        var grid = new GridContainer { Columns = columns };
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
        _box.AddChild(grid);
    }

    HSlider Row(string label, double min, double max, double step, double value, Func<double, string> show, Action<double> set, string tip)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(62, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        var s = new HSlider
        {
            MinValue = min, MaxValue = max, Step = step, Value = value, CustomMinimumSize = new Vector2(80, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = tip,
        };
        var v = Ui.ValueBox(s, show, 42);
        s.ValueChanged += x => set(x);
        s.DragEnded += _ => Cfg.Save();
        row.AddChild(s);
        row.AddChild(v);
        _box.AddChild(row);
        return s;
    }

    static string Percent(double x) => $"{x * 100:0}%";

    void AddPlaces()
    {
        Choices(Places.Select(p => (p.Key, p.Text, "")).ToArray(), P.Place, 3, key => P.Place = key);
        if (P.Place == BuiltKinds.Settlement)
        {
            Row("Houses", 0, 1, 0.05, P.Houses, Percent, x => P.Houses = (float)x, "Scattered houses with gardens, or packed streets");
            Row("Walls", 0, 1, 0.05, P.Walls, Percent, x => P.Walls = (float)x, "None, a wall with towers, or a bastioned trace");
            Choices(new[] { ("grown", "Grown", "Lanes that grew from the road"), ("grid", "Grid", "Streets laid out square") }, P.Grid ? "grid" : "grown", 2, key => P.Grid = key == "grid");
        }
        else if (P.Place == BuiltKinds.Fort)
        {
            Row("Bastions", Forts.FewestBastions - 1, Forts.MostBastions, 1, Math.Max(P.Bastions, Forts.FewestBastions - 1), x => x < Forts.FewestBastions ? "redoubt" : $"{x:0}",
                x => P.Bastions = x < Forts.FewestBastions ? 0 : (int)x, "A plain redoubt, or a star of four to eight bastions");
            Row("Outworks", 0, 1, 0.05, P.Outworks, Percent, x => P.Outworks = (float)x, "None, a glacis, or ravelins before the curtains");
        }
        else Choices(BuiltThings.Blocks.Select(b => (b, char.ToUpperInvariant(b[0]) + b[1..], "")).ToArray(), P.Block, 3, key => P.Block = key);
    }

    void AddRoads()
    {
        Choices(Lines.Select(l => (l.Key, l.Text, "")).ToArray(), P.Line, 3, key => P.Line = key);
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
        _box.AddChild(Hint(picked == null ? "Nothing picked" : char.ToUpperInvariant(picked.Kind[0]) + picked.Kind[1..]));
        if (picked == null) return;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        if (BuiltThings.Remakes(picked)) row.AddChild(Btn("Again", T.Build.Again, BuiltThings.Rerolls(picked) ? "Lay it afresh, another way; parts moved by hand stay" : "Lay it afresh over the ground as it is now"));
        row.AddChild(Btn("Remove", () => T.Build.Delete(), "Remove what is picked · Delete"));
        _box.AddChild(row);
    }
}
