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
        (BuiltKinds.Settlement, "Settlement", "A hamlet, village, town or city, by the size of the drag"),
        (BuiltKinds.Fort, "Fort", "The fort of the map's time: a rampart camp on an ancient map, a bastioned star on later ones"),
        (BuildPrefs.Redoubt, "Redoubt", "A plain square earthwork"), (BuiltKinds.Building, "Building", "One house, church, farm, barn, mill or public building"),
    };

    static readonly (string Key, string Text, string Tip)[] Lines =
    {
        ("track", "Track", "A cart track"), ("lane", "Lane", "A back lane; in a place, houses front it"), ("street", "Street", "A town street; in a place, houses front it"),
        ("road", "Road", "A country road"), ("chaussee", "Chaussée", "A made main road"),
        ("stream", "Stream", "A narrow watercourse"), ("river", "River", "A wide watercourse"),
    };

    static readonly (string Key, string Text, string Tip)[] Walls =
    {
        ("none", "None", "An open place"), ("wall", "Walled", "A wall round what is built: a palisade for a village, towers, gates and a ditch for a town"), ("bastions", "Bastioned", "A bastioned trace fitted round the town, with moat, ravelins and glacis"),
    };

    static readonly (string Key, string Text, string Tip)[] PlanChoices =
    {
        (Plans.Grown, "Grown", "A clustered village: lanes that grew from the road"), (Plans.Street, "Street", "One street, rows of houses, long strip plots behind them"),
        (Plans.Green, "Green", "The street parts round a common green with its pond and church"), (Plans.Round, "Round", "A horseshoe of farmsteads round a small green, one way in"),
        (Plans.Grid, "Grid", "Streets laid out square"),
    };

    static readonly (string Key, string Text, string Tip)[] Outworks =
    {
        ("none", "None", "The trace and its moat alone"), ("glacis", "Glacis", "A glacis round the moat"), ("ravelins", "Ravelins", "A glacis, and ravelins before the curtains"),
    };

    const float NoWalls = 0, Walled = 0.5f, Bastioned = 1, NoOutworks = 0, Glacis = 0.2f, Ravelins = 1;
    const double GlacisFrom = 0.1, RavelinsFrom = 0.35;
    static readonly (string Key, string Text, string Tip)[] Namings =
    {
        (BuildPrefs.NoName, "None", "No name beside the place"), (BuildPrefs.Generated, "Generated", "A name made up in the Tongue"),
        (BuildPrefs.Typed, "Typed", "A box opens at the place to type its name"),
    };

    const int TonguesToARow = 3, PlansToARow = 3, WallsOfOld = 2, MannersToARow = 2;
    string _section = "";
    static readonly (string Key, string Text, string Tip)[] Sweeps =
    {
        (Brushed.Fill, "Fill", "More houses along the streets under the brush"), (Brushed.Thin, "Thin", "Fewer houses under the brush, gardens in their place"),
    };

    const int KindsToARow = 2, LinesToARow = 3, GlacisChoices = 2;

    BuildPrefs P => Cfg.Build;

    public bool Building => T.Tool == Tool.Build;

    public void Add()
    {
        var mode = Chose(key => P.Mode = key);
        _box.AddChild(Ui.Links(Modes.Select(m => (m.Text, P.Mode == m.Key, m.Tip, (Action)(() => mode(m.Key)))).ToList()));
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
        bool ancient = BuildTool.Ancient(S);
        string Picture(string key) => !ancient ? key : key == BuiltKinds.Fort ? ToolIcon.Camp : key == BuiltKinds.Settlement ? ToolIcon.Village : key;
        _box.AddChild(ToolTile.Rows(Places, P.Place, KindsToARow, Chose(key => P.Place = key), Picture));
        _box.AddChild(new HSeparator());
        var form = Ui.Form(Ui.PanelLabels);
        if (P.Place == BuiltKinds.Building)
        {
            _box.AddChild(Palette(ancient, true, P.Block, Manners.For(ancient, Manners.Of(P.Manner)), Chose(key => P.Block = key)));
            AddManner(form, ancient);
            Ui.AlignForm(form);
            _box.AddChild(form);
            return;
        }
        if (P.Place == BuiltKinds.Settlement)
        {
            Row(form, "Houses", 0, 1, 0.05, P.Houses, Percent, x => P.Houses = (float)x, "Scattered houses with gardens, or packed streets");
            Choice(form, "Walls", ancient ? Walls.Take(WallsOfOld).ToArray() : Walls, P.Walls >= Settlements.BastionedFrom && !ancient ? "bastions" : P.Walls >= Settlements.WalledFrom ? "wall" : "none",
                key => P.Walls = key == "bastions" ? Bastioned : key == "wall" ? Walled : NoWalls);
            form.AddChild(new Label { Text = "Plan" });
            form.AddChild(Ui.Chips(PlanChoices, P.Plan, PlansToARow, Chose(key => P.Plan = key)));
        }
        else if (P.Place == BuiltKinds.Fort && !ancient)
        {
            Row(form, "Bastions", Forts.FewestBastions, Forts.MostBastions, 1, P.Bastions, x => $"{x:0}", x => P.Bastions = (int)x, "A star of four to eight bastions");
            Choice(form, "Outworks", Outworks, P.Outworks >= RavelinsFrom ? "ravelins" : P.Outworks >= GlacisFrom ? "glacis" : "none",
                key => P.Outworks = key == "ravelins" ? Ravelins : key == "glacis" ? Glacis : NoOutworks);
        }
        else if (P.Place == BuildPrefs.Redoubt) Choice(form, "Outworks", Outworks.Take(GlacisChoices).ToArray(), P.Outworks >= GlacisFrom ? "glacis" : "none", key => P.Outworks = key == "glacis" ? Glacis : NoOutworks);
        if (P.Place != BuildPrefs.Redoubt) AddManner(form, ancient);
        if (P.Place != BuildPrefs.Redoubt) AddNaming(form);
        Ui.AlignForm(form);
        _box.AddChild(form);
    }

    void AddRoads()
    {
        _box.AddChild(ToolTile.Rows(Lines, P.Line, LinesToARow, Chose(key => P.Line = key)));
        _box.AddChild(new HSeparator());
        var finds = new CheckBox { Text = "Finds its way", ButtonPressed = P.FindsWay, FocusMode = Control.FocusModeEnum.None, TooltipText = "Over the relief between waypoints; a stream runs down from the first" };
        finds.Toggled += on => { P.FindsWay = on; Cfg.Save(); };
        _box.AddChild(finds);
    }

    void AddDetails()
    {
        _box.AddChild(new HSeparator());
        var form = Ui.Form(Ui.PanelLabels);
        Row(form, "Plots", 0, 1, 0.05, P.Plots, Percent, x => P.Plots = (float)x, "A few long boundaries, or small enclosed strips");
        Row(form, "Buildings", 0, 1, 0.05, P.Buildings, Percent, x => P.Buildings = (float)x, "None, lone farms, or hamlets");
        Row(form, "Size", BuildPrefs.SmallestBrush, BuildPrefs.LargestBrush, 0.5, P.Brush, x => $"{x:0.#} u", x => P.Brush = (float)x, "The brush's radius in map units");
        AddManner(form, BuildTool.Ancient(S));
        Ui.AlignForm(form);
        _box.AddChild(form);
    }

    static (string Key, string Text, string Tip)[] MannerChoices(bool ancient) => Manners.Of(ancient).Select(m => (Manners.All[m].Id, Manners.All[m].Name, Manners.All[m].Tip)).ToArray();

    void AddManner(GridContainer form, bool ancient)
    {
        form.AddChild(new Label { Text = "Manner" });
        form.AddChild(Ui.Chips(MannerChoices(ancient), Manners.Id(Manners.For(ancient, Manners.Of(P.Manner))), ancient ? MannerChoices(ancient).Length : MannersToARow, Chose(key => P.Manner = key)));
    }

    string? Renamed(string cls) => BuildTool.Ancient(S) ? cls switch { "church" => "Temple", "steeple" => "Tower", "shrine" => "Milestone", _ => null } : null;

    Control Palette(bool ancient, bool blocksOnly, string chosen, int manner, Action<string> pick) =>
        PartPalette.Of(ancient, blocksOnly, _section, chosen, MapStyles.Index(S.View.Lighting.MapStyle), manner, Renamed, id =>
        {
            _section = id;
            _rebuild();
        }, pick);

    void AddPick()
    {
        _box.AddChild(new HSeparator());
        if (T.Build.Picked is not { } picked)
        {
            _box.AddChild(Hint("Nothing picked"));
            return;
        }
        _box.AddChild(Ui.HeadLabel(T.Build.PickedPart is { } part ? Named(part.Class) : Title(Camps.Is(picked) ? ToolIcon.Camp : Forts.Plain(picked) ? BuildPrefs.Redoubt : picked.Kind)));
        bool ancient = BuildTool.Ancient(S);
        bool whole = T.Build.PickedPart == null, held = BuiltThings.Fixed(picked);
        if (BuiltThings.Named(picked) && whole) AddName(picked);
        bool near = T.Build.Near && picked.Kind is BuiltKinds.Settlement or BuiltKinds.Details;
        if (whole && !held && !near) AddSettings(picked);
        if (BuiltThings.Rerolls(picked))
        {
            var keep = new CheckBox { Text = "Keep", ButtonPressed = T.Build.Kept, FocusMode = Control.FocusModeEnum.None, TooltipText = "Laid again, it stays as it is" };
            keep.Toggled += T.Build.Keep;
            _box.AddChild(keep);
        }
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        if (BuiltThings.Remakes(picked) && !held)
            row.AddChild(Btn("Again", T.Build.Again, BuiltThings.Rerolls(picked) ? "Lay it afresh, another way; what is kept stays" : "Lay it afresh over the ground as it is now"));
        row.AddChild(Btn("Remove", () => T.Build.Delete(), "Remove what is picked · Delete"));
        _box.AddChild(row);
        if (picked.Kind is not (BuiltKinds.Settlement or BuiltKinds.Details)) return;
        void Armed(string key)
        {
            T.Build.Arm(T.Build.Adding == key ? null : key);
            _rebuild();
        }
        if (picked.Kind == BuiltKinds.Settlement && !near)
        {
            var shaping = Sweeps.ToList();
            if (KTable.Core.Model.Walls.Of(picked) != null) shaping.Add((BuildTool.Gate, "Gate", "A gate in the wall where clicked; a click on a gate closes it"));
            _box.AddChild(Header("Shape"));
            _box.AddChild(Ui.Chips(shaping, T.Build.Adding ?? "", shaping.Count, Armed));
        }
        _box.AddChild(Header("Add"));
        if (!near)
        {
            _box.AddChild(Btn("Dive in", T.Build.DiveIn, "Come close to the place to stand houses, stock and furnishings in it by hand"));
            return;
        }
        _box.AddChild(Palette(ancient, false, T.Build.Adding ?? "", Manners.Of(picked), Armed));
    }

    void AddNaming(GridContainer form)
    {
        Choice(form, "Name", Namings, P.Naming, key => P.Naming = key);
        if (P.Naming != BuildPrefs.Generated) return;
        form.AddChild(new Label { Text = "Tongue" });
        form.AddChild(Ui.Chips(PlaceNames.Tongues.Select(t => (t.Key, t.Name, "")).ToArray(), P.Tongue, TonguesToARow, Chose(key => P.Tongue = key)));
    }

    HSlider Held(GridContainer form, string label, double min, double max, double step, double value, Func<double, string> show, Action<double> apply, string tip)
    {
        double wanted = value;
        var slider = Row(form, label, min, max, step, value, show, x =>
        {
            wanted = x;
            if (!Input.IsMouseButtonPressed(MouseButton.Left)) apply(wanted);
        }, tip);
        slider.DragEnded += _ => apply(wanted);
        return slider;
    }

    void AddSettings(BuiltThing picked)
    {
        bool ancient = BuildTool.Ancient(S), star = picked.Kind == BuiltKinds.Fort && !Forts.Plain(picked) && !Camps.Is(picked);
        if (picked.Kind is not (BuiltKinds.Settlement or BuiltKinds.Fort or BuiltKinds.Building or BuiltKinds.Details) || Forts.Plain(picked) && picked.Kind == BuiltKinds.Fort) return;
        var form = Ui.Form(Ui.PanelLabels);
        form.AddChild(new Label { Text = "Manner" });
        form.AddChild(Ui.Chips(MannerChoices(ancient), Manners.Id(Manners.Of(picked)), ancient ? MannerChoices(ancient).Length
            : MannersToARow, key => T.Build.Reset(Manners.Setting, Manners.Of(key))));
        if (star)
        {
            Held(form, "Bastions", Forts.FewestBastions, Forts.MostBastions, 1, picked.Setting(Forts.Bastions, 5), x => $"{x:0}", x => T.Build.Reset(Forts.Bastions, x), "A star of four to eight bastions");
            double works = picked.Setting(Forts.Outworks, 0);
            form.AddChild(new Label { Text = "Outworks" });
            form.AddChild(Ui.Chips(Outworks, works >= RavelinsFrom ? "ravelins" : works >= GlacisFrom ? "glacis" : "none", Outworks.Length,
                key => T.Build.Reset(Forts.Outworks, key == "ravelins" ? Ravelins : key == "glacis" ? Glacis : NoOutworks)));
        }
        else if (picked.Kind == BuiltKinds.Settlement)
        {
            double walls = picked.Setting(Settlements.Walls, 0);
            Held(form, "Houses", 0, 1, 0.05, picked.Setting(Settlements.Houses, 0.5), Percent, x => T.Build.Reset(Settlements.Houses, x), "Scattered houses with gardens, or packed streets");
            form.AddChild(new Label { Text = "Walls" });
            form.AddChild(Ui.Chips(ancient ? Walls.Take(WallsOfOld).ToArray() : Walls, walls >= Settlements.BastionedFrom ? "bastions" : walls >= Settlements.WalledFrom ? "wall" : "none", Walls.Length,
                key => T.Build.Reset(Settlements.Walls, key == "bastions" ? Bastioned : key == "wall" ? Walled : NoWalls)));
            form.AddChild(new Label { Text = "Plan" });
            form.AddChild(Ui.Chips(PlanChoices, Plans.Of(picked.Setting(Settlements.Plan, 0)), PlansToARow, key => T.Build.Reset(Settlements.Plan, Plans.Value(key))));
        }
        Ui.AlignForm(form);
        _box.AddChild(form);
    }

    void AddName(BuiltThing picked)
    {
        var form = Ui.Form(Ui.PanelLabels);
        form.AddChild(new Label { Text = "Name" });
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        var entry = new LineEdit { Text = picked.Name ?? "", MaxLength = PlaceNames.MostLetters, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "The name lettered beside the place" };
        entry.TextSubmitted += text => T.Build.Rename(text);
        entry.FocusExited += () => { if (!entry.IsQueuedForDeletion()) T.Build.Rename(entry.Text); };
        row.AddChild(entry);
        row.AddChild(Btn("Generate", T.Build.NameAfresh, "Another name, made up in the Tongue chosen under Places"));
        form.AddChild(row);
        Ui.AlignForm(form);
        _box.AddChild(form);
    }

    string Named(string cls) => Renamed(cls) ?? (BuildTool.Ancient(S) && cls == "churchyard" ? "Precinct" : Catalogue.All.FirstOrDefault(e => e.Laid == cls)?.Name ?? Title(cls));

    static string Title(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
}
