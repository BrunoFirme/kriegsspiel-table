using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class BuildPanel : HudPart
{
    readonly VBoxContainer _box;
    readonly Action _rebuild;

    public BuildPanel(HudContext ctx, VBoxContainer box, Action rebuild) : base(ctx)
    {
        _box = box;
        _rebuild = rebuild;
        _zone = new PickZoneRows(ctx, Held);
    }

    readonly PickZoneRows _zone;

    const string Town = "town", Fort = "fort", Road = "road", Structure = "building", Country = "country";

    static readonly (string Key, string Text, string Tip)[] Tops =
    {
        (Town, "Town", "A hamlet, village, town or city, generated whole"),
        (Fort, "Fortifications", "The forts and fieldworks of the age, and walls, ditches, hedges and fences drawn where you want them"),
        (Road, "Road", "Roads, streets and tracks; streets and lanes drawn in a settlement are its main streets"),
        (Structure, "Building", "One made thing; inside a settlement it joins it and stays when the settlement is laid again"),
        (Country, "Country", "Plot boundaries, farms and their lanes, under the brush"),
        (BuildPrefs.Pick, "Change", "Change what is built, lay it afresh or remove it · Alt+click picks from any of these"),
    };

    const string Outlining = "outline";

    static readonly (string Key, string Text, string Tip)[] Changes =
    {
        (BuildPrefs.Pick, "Pick", "Pick a built thing to move, reshape, turn or remove it"),
        (BuildPrefs.Homes, "Houses", "More or fewer houses under the brush"),
        (BuildPrefs.Lamps, "Lamps", "Lamps of what is built, put out or lit under the brush"),
        (BuildPrefs.Wealth, "Wealth", "Houses made richer or poorer under the brush"),
        (Outlining, "Outline", "The settlement under the brush takes or gives up the ground painted and is laid again; what is kept stays"),
        (BuildPrefs.Blocks, "Block", "A block of a settlement: houses line it, the settlement's own streets and houses keep out of it"),
    };

    static readonly (string Key, string Text, string Tip)[] Reaches =
    {
        (BuildPrefs.Grow, "Grow", "The settlement under the brush takes the ground painted"), (BuildPrefs.Shrink, "Shrink", "The settlement under the brush gives up the ground painted"),
    };

    const int ChangesToARow = 3;

    static readonly (string Key, string Text, string Tip)[] Shapes =
    {
        (BuildPrefs.Round, "Round", "Dragged from its middle outward, along the line of its main street"),
        (BuildPrefs.Painted, "Painted", "Painted with the brush: the settlement fills the ground painted"),
        (BuildPrefs.Cornered, "Traced", "Its outline clicked point by point"),
    };

    static readonly (string Key, string Text, string Tip)[] Lines =
    {
        ("track", "Track", "A cart track"), ("lane", "Lane", "A back lane; in a place, houses front it"), ("street", "Street", "A town street; in a place, houses front it"),
        ("road", "Road", "A country road"), ("chaussee", "Chaussée", "A made main road"), ("highway", "Highway", "A metalled motor road"),
        ("railway", "Railway", "A railway line; a town laid beside it stands its station there"), ("trench", "Trench", "A fire trench with its bays and traverses"),
        ("wire", "Wire", "A belt of barbed wire"),
    };

    static readonly (string Key, string Text, string Tip)[] PlanChoices =
    {
        (Plans.Grown, "Grown", "Streets that grew from the roads and the lie of the land, round a market"), (Plans.Street, "Street", "One street, rows of houses, long strip plots behind them"),
        (Plans.Green, "Green", "The street parts round a common green with its pond and church"), (Plans.Round, "Round", "A horseshoe of farmsteads round a small green, one way in"),
        (Plans.Grid, "Grid", "Laid out square on two main streets, looser where it outgrew the plan"), (Plans.Maze, "Maze", "Narrow winding lanes and blind alleys between courtyard houses"),
        (Plans.Wards, "Wards", "Broad avenues to the points of the compass, a walled ward in each block"), (Plans.Rows, "Rows", "Close parallel rows of like houses inside a rectangle"),
        (Plans.Shore, "Shore", "Houses strung along the water's edge"), (Plans.Scatter, "Scatter", "Fenced steadings standing apart, no streets"),
    };

    int Lays => Manners.For(BuildTool.Epoch(S), Manners.Parse(P.Manner).Lays);

    (string Key, string Text, string Tip)[] Places()
    {
        var fort = Defences.Fort(Lays);
        var tiles = new List<(string Key, string Text, string Tip)>
        {
            (BuiltKinds.Settlement, "Settlement", "A hamlet, village, town or city, by the size of the drag"), (BuiltKinds.Fort, fort.Name, fort.Tip),
        };
        if (Defences.Fieldwork(Lays) is { } work) tiles.Add((BuildPrefs.Redoubt, work.Name, work.Tip));
        return tiles.ToArray();
    }

    string Place => P.Place == BuildPrefs.Redoubt && Defences.Fieldwork(Lays) == null ? BuiltKinds.Fort : P.Place;

    static (string Key, string Text, string Tip)[] Rings(int manner) => Defences.Rings(Manners.Named(manner).Epoch).ToArray();

    static string Ring(int manner, double walls)
    {
        var rings = Rings(manner);
        return walls >= Settlements.BastionedFrom && rings.Length > WallsOfOld ? rings[^1].Key : walls >= Settlements.WalledFrom ? rings[1].Key : rings[0].Key;
    }

    static float Walled(string key) => key == Defences.Bastions ? Bastioned : key == Defences.None ? NoWalls : Girt;

    static (string Key, string Text, string Tip)[] PlansOf(int manner) => Plans.Offered(manner).Select(plan => PlanChoices.First(choice => choice.Key == plan)).ToArray();

    static readonly (string Key, string Text, string Tip)[] Outworks =
    {
        ("none", "None", "The trace and its moat alone"), ("glacis", "Glacis", "A glacis round the moat"), ("ravelins", "Ravelins", "A glacis, and ravelins before the curtains"),
    };

    const float NoWalls = 0, Girt = 0.5f, Bastioned = 1, NoOutworks = 0, Glacis = 0.2f, Ravelins = 1;
    const double GlacisFrom = 0.1, RavelinsFrom = 0.35;
    const int WallsOfOld = 2;
    string _section = "";
    static readonly (string Key, string Text, string Tip)[] Fields =
    {
        ("enclosed", "Enclosed", "Fields hedged in as the land lies"), ("shares", "Shares", "Strips of one width running out from the stroke, each as far as water, wood or a place"),
    };

    static readonly (string Key, string Text, string Tip)[] LampSwitches =
    {
        ("off", "Out", "The brush puts lamps out"), ("on", "Lit", "The brush lights lamps again"),
    };

    static readonly (string Key, string Text, string Tip)[] WealthSwitches =
    {
        ("richer", "Richer", "The brush makes houses richer"), ("poorer", "Poorer", "The brush makes houses poorer"),
    };

    static readonly (string Key, string Text, string Tip)[] Starts =
    {
        ("middle", "Middle", "A drag runs from its middle along its length"), ("corner", "Corner", "A drag runs from one corner to the opposite one"),
    };

    static readonly (string Key, string Text, string Tip)[] Sweeps =
    {
        (Brushed.Fill, "Fill", "More houses along the streets under the brush"), (Brushed.Thin, "Thin", "Fewer houses under the brush, gardens in their place"),
    };

    const int LinesToARow = 4, GlacisChoices = 2;

    BuildPrefs P => Cfg.Build;

    public bool Building => T.Tool == Tool.Build;

    string Top => P.Activity() switch
    {
        BuildPrefs.Places => Place == BuiltKinds.Settlement ? Town : Fort,
        BuildPrefs.Works => Fort,
        BuildPrefs.Roads => Road,
        BuildPrefs.Things => Structure,
        BuildPrefs.Details => Country,
        _ => BuildPrefs.Pick,
    };

    void Go(string top)
    {
        P.Do(top switch
        {
            Town => BuildPrefs.Places, Fort => Works.Is(P.Work) ? BuildPrefs.Works : BuildPrefs.Places, Road => BuildPrefs.Roads, Structure => BuildPrefs.Things,
            Country => BuildPrefs.Details, _ => BuildPrefs.Pick,
        });
        if (top == Town) P.Place = BuiltKinds.Settlement;
        else if (top == Fort && P.Place == BuiltKinds.Settlement) P.Place = BuiltKinds.Fort;
        _mixing = false;
    }

    const int TopsToARow = 3;

    string TopPicture(string top) => top switch
    {
        Town => BuildTool.Ancient(S) ? ToolIcon.Village : BuiltKinds.Settlement,
        Fort => Defences.Fort(Lays).Id,
        Road => "road",
        Structure => BuiltKinds.Building,
        Country => "fields-enclosed",
        _ => "change-" + BuildPrefs.Pick,
    };

    string Change => P.Activity() is BuildPrefs.Grow or BuildPrefs.Shrink ? Outlining : P.Activity();

    void Take(string change) => P.Do(change == Outlining ? BuildPrefs.Grow : change);

    public void Add()
    {
        string top = Top;
        _box.AddChild(ToolTile.Grid(Tops, top, TopsToARow, Chose(Go), TopPicture));
        _box.AddChild(new HSeparator());
        if (top == BuildPrefs.Pick) AddChange();
        else if (_mixing) AddMix();
        else if (top == Town) AddPlaces();
        else if (top == Fort) AddFortifications();
        else if (top == Road) AddRoads();
        else if (top == Structure) AddBuilding();
        else AddDetails();
    }

    void AddChange()
    {
        _box.AddChild(Tiles(Changes, Change, Chose(Take), "change-", ChangesToARow, slim: false));
        if (P.Mode == BuildPrefs.Pick)
        {
            AddPick();
            return;
        }
        if (P.Activity() == BuildPrefs.Blocks) return;
        var form = Ui.Form(Ui.PanelLabels);
        if (P.Activity() == BuildPrefs.Homes) Choice(form, "Houses", Sweeps, P.Thins ? Brushed.Thin : Brushed.Fill, key => P.Thins = key == Brushed.Thin, "sweep-");
        else if (P.Activity() == BuildPrefs.Lamps) Choice(form, "Lamps", LampSwitches, P.LampsOn ? "on" : "off", key => P.LampsOn = key == "on", "lamps-");
        else if (P.Activity() == BuildPrefs.Wealth) Choice(form, "Wealth", WealthSwitches, P.Richer ? "richer" : "poorer", key => P.Richer = key == "richer", "wealth-");
        else if (P.Activity() is BuildPrefs.Grow or BuildPrefs.Shrink) Choice(form, "Outline", Reaches, P.Activity(), key => P.Do(key), "change-");
        SizeRow(form);
        Ui.AlignForm(form);
        _box.AddChild(new HSeparator());
        _box.AddChild(form);
    }

    Action<string> Chose(Action<string> set) => key =>
    {
        set(key);
        Cfg.Save();
        T.Build.Cancel();
        if (Building) _rebuild();
        else T.SetTool(Tool.Build);
        T.RefreshHint();
    };

    const int ChoicesToARow = 3;

    public static IEnumerable<string> Pictures => new[] { ("plan-", PlanChoices), ("outworks-", Outworks), ("fields-", Fields), ("sweep-", Sweeps), ("lamps-", LampSwitches),
            ("wealth-", WealthSwitches), ("outline-", Shapes), ("from-", Starts), ("change-", Changes), ("change-", Reaches), ("towers-", TowerChoices) }
        .SelectMany(set => set.Item2.Select(item => set.Item1 + item.Key)).Append("sweep-" + BuildTool.Gate)
        .Concat(Works.All.Select(cls => "work-" + cls)).Concat(CastlePlans(Castles.Plans).Select(plan => "castle-" + plan.Key))
        .Concat(new[] { Manners.Ancient, Manners.Medieval, Manners.Later, Manners.Modern }.SelectMany(Defences.Rings).Select(ring => "walls-" + ring.Key).Distinct());

    static VBoxContainer Picked((string Key, string Text, string Tip)[] items, string now, Action<string> pick) =>
        Ui.Chips(items, now, Math.Min(items.Length, ChoicesToARow), pick);

    const int TilesToARow = 4;

    static GridContainer Tiles((string Key, string Text, string Tip)[] items, string now, Action<string> pick, string pictures, int toARow = TilesToARow, bool slim = true) =>
        ToolTile.Grid(items, now, Columns(items.Length, toARow), pick, key => pictures + key, slim);

    static int Columns(int tiles, int most)
    {
        int rows = (tiles + most - 1) / most;
        return (tiles + rows - 1) / rows;
    }

    void Choice(GridContainer form, string label, (string Key, string Text, string Tip)[] items, string now, Action<string> set, string pictures)
    {
        form.AddChild(new Label { Text = label });
        form.AddChild(Tiles(items, now, Chose(set), pictures));
    }

    HSlider Row(GridContainer form, string label, double min, double max, double step, double value, Func<double, string> show, Action<double> set, string tip) =>
        ReliefPanel.Row(form, Cfg, label, min, max, step, value, show, set, tip);

    static string Percent(double x) => $"{x * 100:0}%";

    const string HousesTip = "Scattered houses with gardens, or packed streets; always closest at the middle";
    const string SpreadTip = "A close huddle, or straggling out along its roads";
    const string OrderTip = "Winding as it grew, or straight and laid out by line, with planned quarters";
    const string RavagedTip = "War damage of the age: burned, shelled or bombed homes, from none to most";

    void AddPlaces()
    {
        var first = Ui.Form(Ui.PanelLabels);
        AddCulture(first);
        Ui.AlignForm(first);
        _box.AddChild(first);
        var form = Ui.Form(Ui.PanelLabels);
        Choice(form, "Shape", Shapes, P.Outline, key => P.Outline = key, "outline-");
        if (P.Outline == BuildPrefs.Painted) SizeRow(form);
        Row(form, "Houses", 0, 1, 0.05, P.Houses, Percent, x => P.Houses = (float)x, HousesTip);
        Row(form, "Spread", 0, 1, 0.05, P.Spread, Percent, x => P.Spread = (float)x, SpreadTip);
        Row(form, "Order", 0, 1, 0.05, P.Order, Percent, x => P.Order = (float)x, OrderTip);
        Choice(form, "Walls", Rings(Lays), Ring(Lays, P.Walls), key => P.Walls = Walled(key), "walls-");
        Choice(form, "Plan", Fitting(PlansOf(Lays), P.Outline != BuildPrefs.Round), Plans.For(Lays, P.Plan), key => P.Plan = key, "plan-");
        form.AddChild(new Control());
        form.AddChild(NameTick());
        Ui.AlignForm(form);
        _box.AddChild(new HSeparator());
        _box.AddChild(form);
    }

    CheckBox NameTick() => Tick("Name it", P.Naming == BuildPrefs.Typed, on => P.Naming = on ? BuildPrefs.Typed : BuildPrefs.NoName, "A box opens at the place to type its name");

    CheckBox Tick(string text, bool on, Action<bool> set, string tip)
    {
        var tick = new CheckBox { Text = text, ButtonPressed = on, FocusMode = Control.FocusModeEnum.None, TooltipText = tip };
        tick.Toggled += now =>
        {
            set(now);
            Cfg.Save();
        };
        return tick;
    }

    const int FortsToARow = 4;

    static GridContainer FourAcross((string Key, string Text, string Tip)[] items, string now, Action<string> pick, Func<string, string> picture, bool slim = false)
    {
        var grid = new GridContainer { Columns = FortsToARow, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", Ui.Px(Ui.Gap.Tight));
        grid.AddThemeConstantOverride("v_separation", Ui.Px(Ui.Gap.Tight));
        foreach (var (key, text, tip) in items)
        {
            var b = ToolTile.Of(picture(key), text, tip, key == now, small: true, slim: slim);
            b.Pressed += () => pick(key);
            grid.AddChild(b);
        }
        for (int i = items.Length; i % FortsToARow != 0; i++) grid.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        return grid;
    }

    static readonly (string Key, string Text, string Tip)[] TowerChoices =
    {
        ("0", "None", "No towers"), ("1", "Few", "Towers at its corners, ends and gates"), ("2", "Many", "Towers along it too, at the age's spacing"),
    };

    static string WorkTip(string cls) => cls switch
    {
        Works.Wall => "A stone wall with towers and gates",
        Works.Palisade => "A timber stockade",
        Works.Rampart => "An earth bank",
        Works.Ditch => "A ditch; it holds water only where it lies level",
        Works.Trench => "A fire trench with its bays and traverses",
        Works.Wire => "A belt of barbed wire",
        Works.StoneWall => "A dry-stone field or garden wall",
        Works.Hedge => "A hedge",
        _ => "A post-and-rail fence",
    };

    static (string Key, string Text, string Tip)[] WorkTiles(IEnumerable<string> classes) => classes.Select(cls => (cls, Works.Named(cls), WorkTip(cls))).ToArray();

    static string PlanTip(string plan) => plan switch
    {
        "motte" => "A tower on a raised mound, a bailey below",
        "enclosure" => "A towered wall round the hilltop",
        "quadrangle" => "Four ranges round a court, a tower at each corner",
        "concentric" => "An inner ward inside a lower outer wall",
        "spur" => "On a spur of high ground, cut off by a ditch on the level side",
        "kremlin" => "A timber kremlin within its palisade",
        "kastron" => "A walled fort with close-set square towers",
        "citadel" => "A citadel on its talus with drum towers",
        _ => "Walled baileys stepping up to the keep",
    };

    static (string Key, string Text, string Tip)[] CastlePlans(IEnumerable<string> plans) => plans.Select(plan => (plan, Title(plan), PlanTip(plan))).ToArray();

    static readonly (string Key, string Text, string Tip) AnyPlan = ("any", "Any", "One of the culture's plans, rolled");

    string FortPicture(string key) => key == BuiltKinds.Fort ? Defences.Fort(Lays).Id : key == BuildPrefs.Redoubt ? Defences.Fieldwork(Lays)?.Id ?? key : key;

    bool Castled => Defences.Castled(Lays, Defences.Fort(Lays).Id) && Castles.PlansOf(Lays).Count > 1;

    string? ChosenPlan(int manner) =>
        P.CastlePlan >= 0 && P.CastlePlan < Castles.Plans.Count && Castles.PlansOf(manner).Contains(Castles.Plans[P.CastlePlan]) ? Castles.Plans[P.CastlePlan] : null;

    void AddFortifications()
    {
        var first = Ui.Form(Ui.PanelLabels);
        AddCulture(first);
        Ui.AlignForm(first);
        _box.AddChild(first);
        _box.AddChild(new HSeparator());
        bool works = P.Activity() == BuildPrefs.Works && Works.Is(P.Work);
        string now = works ? P.Work : Place;
        void Fort(string key)
        {
            P.Work = "";
            P.Place = key;
            P.Do(BuildPrefs.Places);
        }
        void Fortify(string cls)
        {
            P.Work = cls;
            P.Do(BuildPrefs.Works);
        }
        _box.AddChild(Header("Forts"));
        _box.AddChild(FourAcross(Places().Skip(1).ToArray(), now, Chose(Fort), FortPicture));
        _box.AddChild(Header("Works"));
        _box.AddChild(FourAcross(WorkTiles(Works.Of(BuildTool.Epoch(S))), now, Chose(Fortify), key => "work-" + key));
        _box.AddChild(Header("Fields"));
        _box.AddChild(FourAcross(WorkTiles(Works.Fields), now, Chose(Fortify), key => "work-" + key));
        if (works) AddWorkChoices();
        else AddFortChoices();
    }

    void AddFortChoices()
    {
        var fort = Place == BuildPrefs.Redoubt ? Defences.Fieldwork(Lays)! : Defences.Fort(Lays);
        if (Place != BuildPrefs.Redoubt && Castled)
        {
            _box.AddChild(Header("Plan"));
            _box.AddChild(FourAcross(CastlePlans(Castles.PlansOf(Lays)).Prepend(AnyPlan).ToArray(), ChosenPlan(Lays) ?? AnyPlan.Key,
                Chose(key => P.CastlePlan = Castles.Plans.ToList().IndexOf(key)), key => "castle-" + key, slim: true));
        }
        if (fort.Starred)
        {
            var form = Ui.Form(Ui.PanelLabels);
            Row(form, "Bastions", Forts.FewestBastions, Forts.MostBastions, 1, P.Bastions, x => $"{x:0}", x => P.Bastions = (int)x, "A star of four to eight bastions");
            Ui.AlignForm(form);
            _box.AddChild(form);
        }
        if (fort.Outworks)
        {
            _box.AddChild(Header("Outworks"));
            _box.AddChild(FourAcross(fort.Starred ? Outworks : Outworks.Take(GlacisChoices).ToArray(), P.Outworks >= RavelinsFrom && fort.Starred ? "ravelins" : P.Outworks >= GlacisFrom ? "glacis" : "none",
                Chose(key => P.Outworks = key == "ravelins" ? Ravelins : key == "glacis" ? Glacis : NoOutworks), key => "outworks-" + key, slim: true));
        }
        if (Place != BuildPrefs.Redoubt) _box.AddChild(NameTick());
    }

    void AddWorkChoices()
    {
        if (Works.HasTowers(P.Work))
        {
            _box.AddChild(Header("Towers"));
            _box.AddChild(FourAcross(TowerChoices, P.WorkTowers.ToString(), Chose(key => P.WorkTowers = int.Parse(key)), key => "towers-" + key, slim: true));
        }
        if (Works.HasDitch(P.Work)) _box.AddChild(Tick("Ditch", P.WorkDitch, on => P.WorkDitch = on, "A ditch dug outside it"));
    }

    void AddBuilding()
    {
        var form = Ui.Form(Ui.PanelLabels);
        AddCulture(form);
        Choice(form, "From", Starts, P.FromCorner ? "corner" : "middle", key => P.FromCorner = key == "corner", "from-");
        Ui.AlignForm(form);
        _box.AddChild(form);
        _box.AddChild(Palette(BuildTool.Ancient(S), false, P.Block, Lays, Chose(key => P.Block = key)));
    }

    void SizeRow(GridContainer form) =>
        Row(form, "Size", BuildPrefs.SmallestBrush, BuildPrefs.LargestBrush, 0.5, P.Brush, x => $"{x:0.#} u", x => P.Brush = (float)x, "The brush's radius in map units");

    void AddRoads()
    {
        var first = Ui.Form(Ui.PanelLabels);
        AddCulture(first);
        Ui.AlignForm(first);
        _box.AddChild(first);
        _box.AddChild(new HSeparator());
        var routes = Defences.Routes(BuildTool.Epoch(S));
        var lines = Lines.Where(line => routes.Contains(line.Key)).ToArray();
        _box.AddChild(ToolTile.Grid(lines, routes.Contains(P.Line) ? P.Line : "road", Columns(lines.Length, LinesToARow), Chose(key => P.Line = key), key => key));
        _box.AddChild(new HSeparator());
        if (P.Line is not ("railway" or "trench" or "wire"))
        {
            var form = Ui.Form(Ui.PanelLabels);
            form.AddChild(new Label { Text = "Surface" });
            form.AddChild(Ui.Picker(Own(Looks.Surfaces, "The surface its kind and culture give it"), Looks.Surface(P.Surface) ?? "", Chose(key => P.Surface = key)));
            Ui.AlignForm(form);
            _box.AddChild(form);
        }
        var finds = new CheckBox { Text = "Finds its way", ButtonPressed = P.FindsWay, FocusMode = Control.FocusModeEnum.None, TooltipText = "Over the relief between waypoints" };
        finds.Toggled += on => { P.FindsWay = on; Cfg.Save(); };
        _box.AddChild(finds);
    }

    void AddDetails()
    {
        var form = Ui.Form(Ui.PanelLabels);
        AddCulture(form);
        Row(form, "Plots", 0, 1, 0.05, P.Plots, Percent, x => P.Plots = (float)x, "A few long boundaries, or small enclosed strips");
        Row(form, "Buildings", 0, 1, 0.05, P.Buildings, Percent, x => P.Buildings = (float)x, "None, lone farms, or hamlets");
        Choice(form, "Fields", Fields, P.Shares ? "shares" : "enclosed", key => P.Shares = key == "shares", "fields-");
        SizeRow(form);
        Ui.AlignForm(form);
        _box.AddChild(form);
    }

    internal static (string Key, string Text, string Tip, Texture2D? Sign)[] CulturesOf(Session s) =>
        Manners.In(BuildTool.Epoch(s)).Select(m => (Manners.Id(m), Manners.Named(m).Name, Manners.Named(m).Tip, (Texture2D?)CultureSigns.Of(m))).ToArray();

    (string Key, string Text, string Tip, Texture2D? Sign)[] Cultures() => CulturesOf(S);

    const string AddMixText = "Add mix…", RemoveMixTip = "Take this mix off the scenario's Culture list; places laid in it stay as they are";

    void AddCulture(GridContainer form)
    {
        var kept = Mixes.Select(mix => (mix.Code, mix.Name, Manners.Parse(mix.Code).Name, (Texture2D?)CultureSigns.Of(Manners.Parse(mix.Code).Lays))).ToList();
        bool mixed = kept.Any(mix => mix.Code == P.Manner), umpire = S.CanEdit(Layers.Umpire);
        form.AddChild(new Label { Text = "Culture" });
        form.AddChild(Ui.Picker(Cultures().Concat(kept).ToArray(), mixed ? P.Manner : Manners.Id(Lays), Chose(key => P.Manner = key),
            umpire ? (AddMixText, "A culture of your own: one lays the place out, others build and adorn it; kept in the scenario under its name", () => Mixing(true)) : null));
        if (!mixed || !umpire) return;
        form.AddChild(new Control());
        form.AddChild(Shrunk(Btn("Remove mix", () => Forget(P.Manner), RemoveMixTip)));
    }

    static Button Shrunk(Button b)
    {
        b.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        return b;
    }

    bool _mixing;
    string _mixName = "";
    int _mixLays = -1, _mixBuilds = -1, _mixAdorns = -1, _mixWard = -1;
    double _mixShare = Manners.UsualWard;

    IEnumerable<NamedMix> Mixes => (S.View.Cultures ?? []).Where(mix => Manners.Named(Manners.Parse(mix.Code).Lays).Epoch == BuildTool.Epoch(S));

    void Mixing(bool on)
    {
        _mixing = on;
        (_mixLays, _mixBuilds, _mixAdorns, _mixWard, _mixShare, _mixName) = (-1, -1, -1, -1, Manners.UsualWard, "");
        _rebuild();
    }

    void Keep(string name, string code)
    {
        var list = (S.View.Cultures ?? []).Where(mix => !string.Equals(mix.Name, name, StringComparison.OrdinalIgnoreCase)).Select(mix => mix.Clone()).ToList();
        list.Add(new NamedMix { Name = name, Code = code });
        S.Do(new SetCultures { Cultures = list });
        Mixing(false);
    }

    void Forget(string code)
    {
        S.Do(new SetCultures { Cultures = (S.View.Cultures ?? []).Where(mix => mix.Code != code).Select(mix => mix.Clone()).ToList() });
        _rebuild();
    }

    int OfAge(int stored, int lays) => stored >= 0 && Manners.Named(stored).Epoch == Manners.Named(lays).Epoch ? Manners.Known(stored) : lays;

    LineEdit NameOf(GridContainer form, string label)
    {
        form.AddChild(new Label { Text = label });
        var entry = new LineEdit { Text = _mixName, MaxLength = PlaceNames.MostLetters, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "The mix's name on the Culture list" };
        entry.TextChanged += text => _mixName = text;
        form.AddChild(entry);
        return entry;
    }

    (string Key, string Text, string Tip, Texture2D? Sign)[] Quarters() => new[] { ("", "None", "No quarter of another culture", (Texture2D?)null) }.Concat(Cultures()).ToArray();

    void AddMix()
    {
        int lays = OfAge(_mixLays, Lays), builds = OfAge(_mixBuilds, lays), adorns = OfAge(_mixAdorns, lays), ward = _mixWard >= 0 && OfAge(_mixWard, lays) == _mixWard ? _mixWard : -1;
        _box.AddChild(Ui.HeadLabel("New mix"));
        var form = Ui.Form(Ui.PanelLabels);
        void Side(string label, int now, Action<int> set)
        {
            form.AddChild(new Label { Text = label });
            form.AddChild(Ui.Picker(Cultures(), Manners.Id(now), key =>
            {
                set(Manners.Of(key));
                _rebuild();
            }));
        }
        Side("Culture", lays, m => _mixLays = m);
        Side("Built by", builds, m => _mixBuilds = m);
        Side("Adorned by", adorns, m => _mixAdorns = m);
        form.AddChild(new Label { Text = "Quarter" });
        form.AddChild(Ui.Picker(Quarters(), ward < 0 ? "" : Manners.Id(ward), key =>
        {
            _mixWard = key.Length == 0 ? -1 : Manners.Of(key);
            _rebuild();
        }));
        if (ward >= 0) Row(form, "Its share", Manners.LeastWard, Manners.MostWard, 0.05, _mixShare, Percent, x => _mixShare = x, "How much of the place the quarter takes");
        NameOf(form, "Name");
        form.AddChild(new Control());
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        row.AddChild(Ui.Primary("Save mix", () =>
        {
            var mix = new Manners.Mix(lays, builds, adorns, ward, ward < 0 ? 0 : _mixShare);
            if (PlaceNames.Tidy(_mixName) is not { Length: > 0 } name || mix.Pure)
            {
                Say("A mix needs a name and a second culture.");
                return;
            }
            P.Manner = mix.Code;
            Cfg.Save();
            Keep(name, mix.Code);
        }, "Keep this mix in the scenario and lay places in it"));
        row.AddChild(Btn("Cancel", () => Mixing(false), "Back to the tools; nothing is kept"));
        form.AddChild(row);
        Ui.AlignForm(form);
        _box.AddChild(form);
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
        _box.AddChild(Ui.HeadLabel(Heading(picked)));
        bool whole = T.Build.PickedLead == null, held = BuiltThings.Fixed(picked);
        if (T.Build.PickedLead is { } lead && picked.Parts.Count > 1) AddWhere(picked, lead);
        if (BuiltThings.Named(picked) && whole) AddName(picked);
        if (whole && !held) AddSettings(picked);
        if (T.Build.Shaped is { } stretching)
        {
            AddLength(BuiltThings.Sides(stretching));
            _box.AddChild(Picked([(BuildTool.Wing, "Wing", "A wing drawn out from a side of the building")], T.Build.Adding ?? "", Armed));
        }
        if (T.Build.PickedLead is { } looked && Looks.Takes(looked) && S.CanEdit(Layers.Umpire)) AddLook(looked);
        if (T.Build.PickedLead is { } housed && Looks.Takes(housed) && S.CanEdit(Layers.Umpire)) _zone.Building(picked, housed, _box);
        if (T.Build.PickedLead is { } way && Looks.Paved(way) && S.CanEdit(Layers.Umpire))
        {
            var form = Ui.Form(Ui.PanelLabels);
            form.AddChild(new Label { Text = "Surface" });
            form.AddChild(Ui.Picker(Own(Looks.Surfaces, "The surface its kind and culture give it"), way.Surface ?? "", key => T.Build.Pave(key.Length == 0 ? null : key)));
            Ui.AlignForm(form);
            _box.AddChild(form);
        }
        if (whole && picked.Kind == BuiltKinds.Works && !held) AddWorks(picked);
        if (whole && !held && Shaped(picked)) AddSides(picked);
        AddFoot(picked, held);
    }

    string Heading(BuiltThing picked) => T.Build.PickedLead is { } part ? Named(part.Class)
        : picked.Kind switch { BuiltKinds.Fort => Defences.Named(picked).Name, BuiltKinds.Works => Works.Named(Works.ClassOf(picked)), _ => Title(picked.Kind) };

    void AddFoot(BuiltThing picked, bool held)
    {
        _box.AddChild(new HSeparator());
        if (BuiltThings.Rerolls(picked))
        {
            var keep = new CheckBox { Text = "Keep", ButtonPressed = T.Build.Kept, FocusMode = Control.FocusModeEnum.None, TooltipText = "Laid again, it stays as it is" };
            keep.Toggled += T.Build.Keep;
            _box.AddChild(keep);
        }
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        if (BuiltThings.Remakes(picked) && !held)
            row.AddChild(Btn("Again", T.Build.Again, BuiltThings.Rerolls(picked) ? "Lay it afresh, another way; what is kept stays" : "Lay it afresh over the ground as it is now"));
        row.AddChild(Btn("Remove", () => T.Build.Delete(), "Remove what is picked · Delete"));
        _box.AddChild(row);
    }

    void AddWorks(BuiltThing works)
    {
        string cls = Works.ClassOf(works);
        var kinds = Works.Fields.Contains(cls) ? Works.Fields : Works.Of(BuildTool.Epoch(S));
        _box.AddChild(FourAcross(WorkTiles(kinds), cls, key => T.Build.Rework(cls: key), key => "work-" + key));
        if (Works.HasTowers(cls))
        {
            _box.AddChild(Header("Towers"));
            _box.AddChild(FourAcross(TowerChoices, Works.TowersOf(works).ToString(), key => T.Build.Rework(towers: int.Parse(key)), key => "towers-" + key, slim: true));
        }
        if (Works.HasDitch(cls)) _box.AddChild(Tick("Ditch", Works.DitchOf(works), on => T.Build.Rework(ditch: on), "A ditch dug outside it"));
        if (Adds(cls) is { Length: > 0 } adds) _box.AddChild(Picked(adds, T.Build.Adding ?? "", Armed));
    }

    void AddWallRows(GridContainer form, string cls, int towers, bool ditch)
    {
        if (Works.HasTowers(cls))
        {
            form.AddChild(new Label { Text = "Towers" });
            form.AddChild(Tiles(TowerChoices, towers.ToString(), key => T.Build.Rework(towers: int.Parse(key)), "towers-"));
        }
        if (Works.HasDitch(cls))
        {
            form.AddChild(new Control());
            form.AddChild(Tick("Ditch", ditch, on => T.Build.Rework(ditch: on), "A ditch dug outside it"));
        }
        if (Adds(cls) is not { Length: > 0 } adds) return;
        form.AddChild(new Control());
        form.AddChild(Picked(adds, T.Build.Adding ?? "", Armed));
    }

    static (string Key, string Text, string Tip)[] Adds(string cls)
    {
        var adds = new List<(string Key, string Text, string Tip)>();
        if (Works.HasGates(cls)) adds.Add((BuildTool.Gate, "Gate", "A gate in the wall where clicked; a click on a gate closes it"));
        if (Works.HasTowers(cls)) adds.Add((BuildTool.Tower, "Tower", "A tower on the wall where clicked; a click on a tower takes it down"));
        return adds.ToArray();
    }

    void AddWhere(BuiltThing picked, BuiltPart lead)
    {
        string place = picked.Name ?? (picked.Kind == BuiltKinds.Fort ? Defences.Named(picked).Name : Title(picked.Kind));
        var steps = new List<(string, bool, string, Action)> { (place, false, "Pick the whole of it", T.Build.PickWhole) };
        bool grouped = Groups.Of(picked, picked.Parts.IndexOf(lead)).Count > 1;
        if (grouped) steps.Add((Named(lead.Class), !T.Build.PickedAlone, "Pick the building with what belongs to it", T.Build.PickBuilding));
        if (T.Build.PickedAlone || !grouped) steps.Add((grouped ? "Part" : Named(lead.Class), true, "This part alone", () => { }));
        _box.AddChild(Ui.Links(steps, false));
    }

    const string AsBuilt = "As built";

    static (string Key, string Text, string Tip, Texture2D? Sign)[] Own(IEnumerable<string> names, string tip) =>
        new[] { ("", AsBuilt, tip, (Texture2D?)null) }.Concat(names.Select(n => (n, n == "halfhip" ? "Half hip" : char.ToUpperInvariant(n[0]) + n[1..], "", (Texture2D?)null))).ToArray();

    void AddLook(BuiltPart part)
    {
        var form = Ui.Form(Ui.PanelLabels);
        form.AddChild(new Label { Text = "Storeys" });
        form.AddChild(Ui.Picker(Own(Enumerable.Range(1, Looks.MostStoreys).Select(n => n.ToString()), "As many storeys as its culture gives it"), part.Storeys > 0 ? part.Storeys.ToString() : "",
            key => T.Build.Look(key.Length == 0 ? 0 : int.Parse(key), part.Roof, part.Cover)));
        form.AddChild(new Label { Text = "Roof" });
        form.AddChild(Ui.Picker(Own(Looks.Roofs, "The roof its culture gives it"), part.Roof ?? "", key => T.Build.Look(part.Storeys, key.Length == 0 ? null : key, part.Cover)));
        form.AddChild(new Label { Text = "Covering" });
        form.AddChild(Ui.Picker(Own(Looks.Covers, "The covering its culture gives it"), part.Cover ?? "", key => T.Build.Look(part.Storeys, part.Roof, key.Length == 0 ? null : key)));
        Ui.AlignForm(form);
        _box.AddChild(form);
    }

    void AddLength((double Long, double Deep) sides)
    {
        double perMetre = S.View.Calibration.UnitsPerMeter ?? 0;
        if (perMetre <= 0) return;
        var form = Ui.Form(Ui.PanelLabels);
        HSlider Side(string label, double now, Func<double, (double, double)> sized, string tip) => Held(form, label, LeastSideM, MostSideM, SideStepM, now / perMetre, x => $"{x:0.#} m",
            x => T.Build.Stretch(sized(x * perMetre)), tip);
        Side("Size X", sides.Long, x => (x, sides.Deep), "Its size along its front, in metres");
        Side("Size Y", sides.Deep, x => (sides.Long, x), "Its size from front to back, in metres");
        Ui.AlignForm(form);
        _box.AddChild(form);
    }

    const double LeastSideM = 1, MostSideM = 400, SideStepM = 0.5;

    static bool Shaped(BuiltThing picked) => picked.Kind is BuiltKinds.Settlement or BuiltKinds.Fort or BuiltKinds.Building or BuiltKinds.Details;

    void Armed(string key)
    {
        T.Build.Arm(T.Build.Adding == key ? null : key);
        _rebuild();
        T.RefreshHint();
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
        var work = picked.Kind == BuiltKinds.Fort ? Defences.Named(picked) : null;
        bool star = work is { Starred: true };
        if (!Shaped(picked)) return;
        var form = Ui.Form(Ui.PanelLabels);
        var sides = Manners.SidesOf(picked);
        form.AddChild(new Label { Text = "Culture" });
        form.AddChild(Ui.Picker(Cultures(), Manners.Id(sides.Lays), key => T.Build.Reset(Manners.Setting, Manners.Of(key))));
        if (star)
        {
            Held(form, "Bastions", Forts.FewestBastions, Forts.MostBastions, 1, picked.Setting(Forts.Bastions, 5), x => $"{x:0}", x => T.Build.Reset(Forts.Bastions, x), "A star of four to eight bastions");
            double works = picked.Setting(Forts.Outworks, 0);
            form.AddChild(new Label { Text = "Outworks" });
            form.AddChild(Tiles(Outworks, works >= RavelinsFrom ? "ravelins" : works >= GlacisFrom ? "glacis" : "none",
                key => T.Build.Reset(Forts.Outworks, key == "ravelins" ? Ravelins : key == "glacis" ? Glacis : NoOutworks), "outworks-"));
        }
        else if (work != null && Defences.Castled(sides.Lays, work.Id) && Castles.PlansOf(sides.Lays) is { Count: > 1 } plans && picked.Setting("plan", -1) is >= 0 and var laid)
        {
            form.AddChild(new Label { Text = "Plan" });
            form.AddChild(Tiles(CastlePlans(plans), Castles.Plans[(int)laid], key => T.Build.Reset(Castles.Asked, Castles.Plans.ToList().IndexOf(key)), "castle-"));
        }
        else if (picked.Kind == BuiltKinds.Settlement)
        {
            Held(form, "Houses", 0, 1, 0.05, picked.Setting(Settlements.Houses, 0.5), Percent, x => T.Build.Reset(Settlements.Houses, x), HousesTip);
            Held(form, "Spread", 0, 1, 0.05, picked.Setting(Settlements.Spread, Settlements.UsualSpread), Percent, x => T.Build.Reset(Settlements.Spread, x), SpreadTip);
            Held(form, "Order", 0, 1, 0.05, picked.Setting(Settlements.Order, Settlements.UsualOrder), Percent, x => T.Build.Reset(Settlements.Order, x), OrderTip);
            form.AddChild(new Label { Text = "Plan" });
            form.AddChild(Tiles(Fitting(PlansOf(sides.Lays), Outlines.Of(picked) != null), Settlements.Form(picked), key => T.Build.Reset(Settlements.Plan, Plans.Value(key)), "plan-"));
        }
        Ui.AlignForm(form);
        _box.AddChild(form);
        if (picked.Kind != BuiltKinds.Settlement) return;
        var life = Ui.Form(Ui.PanelLabels);
        _zone.LivesOn(life, picked);
        Held(life, "Ravaged", 0, 1, 0.05, picked.Setting(Settlements.Ravaged, 0), Percent, x => T.Build.Reset(Settlements.Ravaged, x), RavagedTip);
        _zone.Hold(life, picked);
        Ui.AlignForm(life);
        _box.AddChild(Header("Life"));
        _box.AddChild(life);
        var wall = Ui.Form(Ui.PanelLabels);
        wall.AddChild(new Label { Text = "Walls" });
        wall.AddChild(Tiles(Rings(sides.Lays), Ring(sides.Lays, picked.Setting(Settlements.Walls, 0)), key => T.Build.Reset(Settlements.Walls, Walled(key)), "walls-"));
        if (KTable.Core.Model.Walls.Of(picked) is { } trace)
        {
            string cls = trace.Masonry ? Works.Wall : Works.Palisade;
            wall.AddChild(new Label { Text = "Kind" });
            wall.AddChild(Tiles(WorkTiles([Works.Wall, Works.Palisade]), cls, key => T.Build.Rework(cls: key), "work-"));
            AddWallRows(wall, cls, trace.Towers, trace.Ditch);
        }
        Ui.AlignForm(wall);
        _box.AddChild(Header("Wall"));
        _box.AddChild(wall);
    }

    static (string Key, string Text, string Tip)[] Fitting((string Key, string Text, string Tip)[] plans, bool outlined) =>
        outlined ? plans.Where(plan => Settlements.BoundedPlans.Contains(plan.Key)).ToArray() : plans;

    void AddSides(BuiltThing picked)
    {
        var form = Ui.Form(Ui.PanelLabels);
        var mix = Manners.MixOf(picked);
        form.AddChild(new Label { Text = "Built by" });
        form.AddChild(Ui.Picker(Cultures(), Manners.Id(mix.Builds), key => T.Build.Reset(Manners.Builders, Manners.Of(key))));
        form.AddChild(new Label { Text = "Adorned by" });
        form.AddChild(Ui.Picker(Cultures(), Manners.Id(mix.Adorns), key => T.Build.Reset(Manners.Adorners, Manners.Of(key))));
        form.AddChild(new Label { Text = "Quarter" });
        form.AddChild(Ui.Picker(Quarters(), mix.Ward < 0 ? "" : Manners.Id(mix.Ward), key => T.Build.Reset(Manners.Ward, key.Length == 0 ? -1 : Manners.Of(key))));
        if (mix.Ward >= 0)
            Held(form, "Its share", Manners.LeastWard, Manners.MostWard, 0.05, mix.Share, Percent, x => T.Build.Reset(Manners.WardShare, x), "How much of the place the quarter takes, built and adorned its own way");
        if (!mix.Pure && S.CanEdit(Layers.Umpire)) AddKeeping(form, mix);
        Ui.AlignForm(form);
        _box.AddChild(Header("Mix"));
        _box.AddChild(form);
    }

    void AddKeeping(GridContainer form, Manners.Mix mix)
    {
        bool known = (S.View.Cultures ?? []).Any(kept => kept.Code == mix.Code);
        if (!known) NameOf(form, "Mix name");
        form.AddChild(new Control());
        form.AddChild(Shrunk(known ? Btn("Remove mix", () => Forget(mix.Code), RemoveMixTip) : Btn("Keep mix", () =>
        {
            if (PlaceNames.Tidy(_mixName) is { Length: > 0 } name) Keep(name, mix.Code);
            else Say("A mix needs a name.");
        }, "Keep this mix in the scenario under its name, to lay other places in it")));
    }

    void AddName(BuiltThing picked)
    {
        var form = Ui.Form(Ui.PanelLabels);
        form.AddChild(new Label { Text = "Name" });
        var entry = new LineEdit { Text = picked.Name ?? "", MaxLength = PlaceNames.MostLetters, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "The name lettered beside the place" };
        entry.TextSubmitted += text => T.Build.Rename(text);
        entry.FocusExited += () => { if (!entry.IsQueuedForDeletion()) T.Build.Rename(entry.Text); };
        form.AddChild(entry);
        Ui.AlignForm(form);
        _box.AddChild(form);
    }

    string Named(string cls) => Renamed(cls) ?? (BuildTool.Ancient(S) && cls == "churchyard" ? "Precinct" : Catalogue.All.FirstOrDefault(e => e.Laid == cls)?.Name ?? Title(cls));

    static string Title(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
}
