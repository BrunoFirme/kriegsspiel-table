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

    static readonly (string Key, string Text, string Tip)[] Lines =
    {
        ("track", "Track", ""), ("road", "Road", ""), ("chaussee", "Chaussée", ""), ("stream", "Stream", ""), ("river", "River", ""),
    };

    static readonly (string Key, string Text, string Tip)[] Walls =
    {
        ("none", "None", "An open place"), ("wall", "Walled", "A wall with towers and gates"), ("bastions", "Bastioned", "A bastioned trace with moat, ravelins and glacis"),
    };

    static readonly (string Key, string Text, string Tip)[] Plans = { ("grown", "Grown", "Lanes that grew from the road"), ("grid", "Grid", "Streets laid out square") };

    static readonly (string Key, string Text, string Tip)[] Outworks =
    {
        ("none", "None", "The trace and its moat alone"), ("glacis", "Glacis", "A glacis round the moat"), ("ravelins", "Ravelins", "A glacis, and ravelins before the curtains"),
    };

    const float NoWalls = 0, Walled = 0.5f, Bastioned = 1, NoOutworks = 0, Glacis = 0.2f, Ravelins = 1;
    const double GlacisFrom = 0.1, RavelinsFrom = 0.35;
    const int ModesToARow = 4, KindsToARow = 2, LinesToARow = 3, BlocksToARow = 3, GlacisChoices = 2;

    BuildPrefs P => Cfg.Build;

    public bool Building => T.Tool == Tool.Build;

    public void Add()
    {
        _box.AddChild(Ui.Chips(Modes, P.Mode, ModesToARow, Chose(key => P.Mode = key), tools: true));
        _box.AddChild(new HSeparator());
        if (P.Mode == BuildPrefs.Places) AddPlaces();
        else if (P.Mode == BuildPrefs.Roads) AddRoads();
        else if (P.Mode == BuildPrefs.Details) AddDetails();
        else AddPick();
    }

    Action<string> Chose(Action<string> set) => key =>
    {
        set(key);
        Cfg.Save();
        T.Build.Cancel();
        if (Building) _rebuild();
        else T.SetTool(Tool.Build);
    };

    void Choice(GridContainer form, string label, (string Key, string Text, string Tip)[] items, string now, Action<string> set)
    {
        form.AddChild(new Label { Text = label });
        form.AddChild(Ui.Chips(items, now, items.Length, Chose(set)));
    }

    HSlider Row(GridContainer form, string label, double min, double max, double step, double value, Func<double, string> show, Action<double> set, string tip) =>
        ReliefPanel.Row(form, Cfg, label, min, max, step, value, show, set, tip);

    static string Percent(double x) => $"{x * 100:0}%";

    void AddPlaces()
    {
        _box.AddChild(Ui.Chips(Places, P.Place, KindsToARow, Chose(key => P.Place = key)));
        if (P.Place == BuiltKinds.Building)
        {
            _box.AddChild(Ui.Chips(BuiltThings.Blocks.Select(b => (b, Title(b), "")).ToArray(), P.Block, BlocksToARow, Chose(key => P.Block = key)));
            return;
        }
        var form = Ui.Form(Ui.PanelLabels);
        if (P.Place == BuiltKinds.Settlement)
        {
            Row(form, "Houses", 0, 1, 0.05, P.Houses, Percent, x => P.Houses = (float)x, "Scattered houses with gardens, or packed streets");
            Choice(form, "Walls", Walls, P.Walls >= Settlements.BastionedFrom ? "bastions" : P.Walls >= Settlements.WalledFrom ? "wall" : "none",
                key => P.Walls = key == "bastions" ? Bastioned : key == "wall" ? Walled : NoWalls);
            Choice(form, "Plan", Plans, P.Grid ? "grid" : "grown", key => P.Grid = key == "grid");
        }
        else if (P.Place == BuiltKinds.Fort)
        {
            Row(form, "Bastions", Forts.FewestBastions, Forts.MostBastions, 1, P.Bastions, x => $"{x:0}", x => P.Bastions = (int)x, "A star of four to eight bastions");
            Choice(form, "Outworks", Outworks, P.Outworks >= RavelinsFrom ? "ravelins" : P.Outworks >= GlacisFrom ? "glacis" : "none",
                key => P.Outworks = key == "ravelins" ? Ravelins : key == "glacis" ? Glacis : NoOutworks);
        }
        else Choice(form, "Outworks", Outworks.Take(GlacisChoices).ToArray(), P.Outworks >= GlacisFrom ? "glacis" : "none", key => P.Outworks = key == "glacis" ? Glacis : NoOutworks);
        Ui.AlignForm(form);
        _box.AddChild(form);
    }

    void AddRoads()
    {
        _box.AddChild(Ui.Chips(Lines, P.Line, LinesToARow, Chose(key => P.Line = key)));
        var finds = new CheckBox { Text = "Finds its way", ButtonPressed = P.FindsWay, FocusMode = Control.FocusModeEnum.None, TooltipText = "Over the relief between waypoints; a stream runs down from the first" };
        finds.Toggled += on => { P.FindsWay = on; Cfg.Save(); };
        _box.AddChild(finds);
    }

    void AddDetails()
    {
        var form = Ui.Form(Ui.PanelLabels);
        Row(form, "Plots", 0, 1, 0.05, P.Plots, Percent, x => P.Plots = (float)x, "A few long boundaries, or small enclosed strips");
        Row(form, "Buildings", 0, 1, 0.05, P.Buildings, Percent, x => P.Buildings = (float)x, "None, lone farms, or hamlets");
        Row(form, "Size", BuildPrefs.SmallestBrush, BuildPrefs.LargestBrush, 0.5, P.Brush, x => $"{x:0.#} u", x => P.Brush = (float)x, "The brush's radius in map units");
        Ui.AlignForm(form);
        _box.AddChild(form);
    }

    void AddPick()
    {
        if (T.Build.Picked is not { } picked)
        {
            _box.AddChild(Hint("Nothing picked"));
            return;
        }
        _box.AddChild(Ui.HeadLabel(T.Build.PickedPart is { } part ? Title(part.Class) : Title(Forts.Plain(picked) ? BuildPrefs.Redoubt : picked.Kind)));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        if (BuiltThings.Remakes(picked)) row.AddChild(Btn("Again", T.Build.Again, BuiltThings.Rerolls(picked) ? "Lay it afresh, another way; parts moved by hand stay" : "Lay it afresh over the ground as it is now"));
        row.AddChild(Btn("Remove", () => T.Build.Delete(), "Remove what is picked · Delete"));
        _box.AddChild(row);
    }

    static string Title(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
}
