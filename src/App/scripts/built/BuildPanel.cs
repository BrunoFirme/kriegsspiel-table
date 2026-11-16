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
    }

    static readonly (string Key, string Text, string Tip)[] Modes =
    {
        (BuildPrefs.Laying, "Lay", "Settlements and forts, generated whole"),
        (BuildPrefs.Drawing, "Draw", "Roads, blocks and buildings, kept exactly where you put them; a settlement builds around them"),
        (BuildPrefs.Brushing, "Brush", "Houses, country and lamps, filled or cleared under the brush"),
        (BuildPrefs.Pick, "Pick", "Pick what is built to change it, lay it afresh or remove it · Alt+click picks from any of these"),
    };

    static readonly (string Key, string Text, string Tip)[] Draws =
    {
        (BuildPrefs.Roads, "Roads", "Roads, streets, tracks and watercourses; streets and lanes drawn in a settlement are its main streets"),
        (BuildPrefs.Blocks, "Blocks", "A block of a settlement: houses line it, the settlement's own streets and houses keep out of it"),
        (BuildPrefs.Things, "Buildings", "One made thing; inside a settlement it joins it and stays when the settlement is laid again"),
    };

    static readonly (string Key, string Text, string Tip)[] Brushes =
    {
        (BuildPrefs.Homes, "Houses", "More or fewer houses along the streets under the brush"),
        (BuildPrefs.Details, "Country", "Plot boundaries, farms and their lanes"),
        (BuildPrefs.Lamps, "Lamps", "Lamps of what is built, put out or lit under the brush"),
    };

    static readonly (string Key, string Text, string Tip)[] Lines =
    {
        ("track", "Track", "A cart track"), ("lane", "Lane", "A back lane; in a place, houses front it"), ("street", "Street", "A town street; in a place, houses front it"),
        ("road", "Road", "A country road"), ("chaussee", "Chaussée", "A made main road"), ("highway", "Highway", "A metalled motor road"),
        ("railway", "Railway", "A railway line; a town laid beside it stands its station there"), ("trench", "Trench", "A fire trench with its bays and traverses"),
        ("wire", "Wire", "A belt of barbed wire"), ("stream", "Stream", "A narrow watercourse"), ("river", "River", "A wide watercourse"),
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
    static readonly (string Key, string Text, string Tip)[] Namings =
    {
        (BuildPrefs.NoName, "None", "No name beside the place"),
        (BuildPrefs.Typed, "Typed", "A box opens at the place to type its name"),
    };

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

    static readonly (string Key, string Text, string Tip)[] Sweeps =
    {
        (Brushed.Fill, "Fill", "More houses along the streets under the brush"), (Brushed.Thin, "Thin", "Fewer houses under the brush, gardens in their place"),
    };

    const int KindsToARow = 2, KindsInOneRow = 3, LinesToARow = 3, GlacisChoices = 2;

    BuildPrefs P => Cfg.Build;

    public bool Building => T.Tool == Tool.Build;

    public void Add()
    {
        var mode = Chose(key =>
        {
            P.Mode = key;
            _mixing = false;
        });
        _box.AddChild(Ui.Links(Modes.Select(m => (m.Text, P.Mode == m.Key, m.Tip, (Action)(() => mode(m.Key)))).ToList()));
        if (P.Mode == BuildPrefs.Pick) AddPick();
        else if (_mixing) AddMix();
        else if (P.Mode == BuildPrefs.Laying) AddPlaces();
        else if (P.Mode == BuildPrefs.Drawing) AddDraw();
        else AddBrush();
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

    public static IEnumerable<string> Pictures => new[] { ("plan-", PlanChoices), ("outworks-", Outworks), ("name-", Namings), ("fields-", Fields), ("sweep-", Sweeps), ("lamps-", LampSwitches) }
        .SelectMany(set => set.Item2.Select(item => set.Item1 + item.Key)).Append("sweep-" + BuildTool.Gate)
        .Concat(new[] { Manners.Ancient, Manners.Medieval, Manners.Later, Manners.Modern }.SelectMany(Defences.Rings).Select(ring => "walls-" + ring.Key).Distinct());

    static VBoxContainer Pictured(string of, (string Key, string Text, string Tip)[] items, string now, Action<string> pick) =>
        ToolTile.Rows(items, now, Math.Min(items.Length, ChoicesToARow), pick, key => of + key, true);

    void Choice(GridContainer form, string label, string of, (string Key, string Text, string Tip)[] items, string now, Action<string> set)
    {
        form.AddChild(new Label { Text = label });
        form.AddChild(Pictured(of, items, now, Chose(set)));
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
        bool ancient = BuildTool.Ancient(S);
        var fort = Place == BuildPrefs.Redoubt ? Defences.Fieldwork(Lays)! : Defences.Fort(Lays);
        string Picture(string key) => key == BuiltKinds.Fort ? Defences.Fort(Lays).Id : key == BuildPrefs.Redoubt ? Defences.Fieldwork(Lays)?.Id ?? key : key == BuiltKinds.Settlement
            && ancient ? ToolIcon.Village : key;
        var first = Ui.Form(Ui.PanelLabels);
        AddCulture(first);
        Ui.AlignForm(first);
        _box.AddChild(first);
        var places = Places();
        _box.AddChild(ToolTile.Rows(places, Place, places.Length == KindsInOneRow ? KindsInOneRow : KindsToARow, Chose(key => P.Place = key), Picture));
        var form = Ui.Form(Ui.PanelLabels);
        if (Place == BuiltKinds.Settlement)
        {
            Row(form, "Houses", 0, 1, 0.05, P.Houses, Percent, x => P.Houses = (float)x, HousesTip);
            Row(form, "Spread", 0, 1, 0.05, P.Spread, Percent, x => P.Spread = (float)x, SpreadTip);
            Row(form, "Order", 0, 1, 0.05, P.Order, Percent, x => P.Order = (float)x, OrderTip);
            Choice(form, "Walls", "walls-", Rings(Lays), Ring(Lays, P.Walls), key => P.Walls = Walled(key));
            Choice(form, "Plan", "plan-", PlansOf(Lays), Plans.For(Lays, P.Plan), key => P.Plan = key);
        }
        else
        {
            if (fort.Starred) Row(form, "Bastions", Forts.FewestBastions, Forts.MostBastions, 1, P.Bastions, x => $"{x:0}", x => P.Bastions = (int)x, "A star of four to eight bastions");
            if (fort.Outworks)
                Choice(form, "Outworks", "outworks-", fort.Starred ? Outworks : Outworks.Take(GlacisChoices).ToArray(),
                    P.Outworks >= RavelinsFrom && fort.Starred ? "ravelins" : P.Outworks >= GlacisFrom ? "glacis" : "none", key => P.Outworks = key == "ravelins" ? Ravelins : key == "glacis" ? Glacis : NoOutworks);
        }
        ReliefPanel.Row(form, Cfg, "Radius", BuildPrefs.SmallestReachM, BuildPrefs.LargestReachM, 5, P.ReachM, x => $"{x:0} m", x => P.ReachM = (float)x, "The reach a click lays, in metres", exp: true);
        if (Place != BuildPrefs.Redoubt) AddNaming(form);
        if (form.GetChildCount() == 0)
        {
            form.Free();
            return;
        }
        Ui.AlignForm(form);
        _box.AddChild(new HSeparator());
        _box.AddChild(form);
    }

    void AddDraw()
    {
        _box.AddChild(ToolTile.Rows(Draws, P.Drawn, Draws.Length, Chose(key => P.Drawn = key), Pictured));
        _box.AddChild(new HSeparator());
        if (P.Drawn == BuildPrefs.Roads) AddRoads();
        if (P.Drawn != BuildPrefs.Things) return;
        var form = Ui.Form(Ui.PanelLabels);
        AddCulture(form);
        Ui.AlignForm(form);
        _box.AddChild(form);
        _box.AddChild(Palette(BuildTool.Ancient(S), false, P.Block, Lays, Chose(key => P.Block = key)));
    }

    void AddBrush()
    {
        _box.AddChild(ToolTile.Rows(Brushes, P.Brushed, Brushes.Length, Chose(key => P.Brushed = key), Pictured));
        _box.AddChild(new HSeparator());
        if (P.Brushed == BuildPrefs.Details)
        {
            AddDetails();
            return;
        }
        var form = Ui.Form(Ui.PanelLabels);
        if (P.Brushed == BuildPrefs.Lamps) Choice(form, "Lamps", "lamps-", LampSwitches, P.LampsOn ? "on" : "off", key => P.LampsOn = key == "on");
        else Choice(form, "Houses", "sweep-", Sweeps, P.Thins ? Brushed.Thin : Brushed.Fill, key => P.Thins = key == Brushed.Thin);
        SizeRow(form);
        Ui.AlignForm(form);
        _box.AddChild(form);
    }

    static string Pictured(string key) => key switch
    {
        BuildPrefs.Roads => "road",
        BuildPrefs.Blocks => "plan-" + Plans.Rows,
        BuildPrefs.Things => BuiltKinds.Building,
        BuildPrefs.Homes => "sweep-" + Brushed.Fill,
        BuildPrefs.Details => "fields-enclosed",
        _ => "lamps-on",
    };

    void SizeRow(GridContainer form) =>
        Row(form, "Size", BuildPrefs.SmallestBrush, BuildPrefs.LargestBrush, 0.5, P.Brush, x => $"{x:0.#} u", x => P.Brush = (float)x, "The brush's radius in map units");

    void AddRoads()
    {
        var routes = Defences.Routes(BuildTool.Epoch(S));
        var lines = Lines.Where(line => routes.Contains(line.Key)).ToArray();
        _box.AddChild(ToolTile.Rows(lines, routes.Contains(P.Line) ? P.Line : "road", LinesToARow, Chose(key => P.Line = key)));
        _box.AddChild(new HSeparator());
        var finds = new CheckBox { Text = "Finds its way", ButtonPressed = P.FindsWay, FocusMode = Control.FocusModeEnum.None, TooltipText = "Over the relief between waypoints; a stream runs down from the first" };
        finds.Toggled += on => { P.FindsWay = on; Cfg.Save(); };
        _box.AddChild(finds);
    }

    void AddDetails()
    {
        var form = Ui.Form(Ui.PanelLabels);
        AddCulture(form);
        Row(form, "Plots", 0, 1, 0.05, P.Plots, Percent, x => P.Plots = (float)x, "A few long boundaries, or small enclosed strips");
        Row(form, "Buildings", 0, 1, 0.05, P.Buildings, Percent, x => P.Buildings = (float)x, "None, lone farms, or hamlets");
        Choice(form, "Fields", "fields-", Fields, P.Shares ? "shares" : "enclosed", key => P.Shares = key == "shares");
        SizeRow(form);
        Ui.AlignForm(form);
        _box.AddChild(form);
    }

    (string Key, string Text, string Tip, Texture2D? Sign)[] Cultures() =>
        Manners.In(BuildTool.Epoch(S)).Select(m => (Manners.Id(m), Manners.Named(m).Name, Manners.Named(m).Tip, (Texture2D?)CultureSigns.Of(m))).ToArray();

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
        _box.AddChild(Ui.HeadLabel(T.Build.PickedPart is { } part ? Named(part.Class) : picked.Kind == BuiltKinds.Fort ? Defences.Named(picked).Name : Title(picked.Kind)));
        bool whole = T.Build.PickedPart == null, held = BuiltThings.Fixed(picked);
        if (BuiltThings.Named(picked) && whole) AddName(picked);
        if (whole && !held) AddSettings(picked);
        if (T.Build.PickedPart is { } stretching && BuiltThings.Stretches(stretching)) AddLength(BuiltThings.Sides(stretching));
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
        if (whole && KTable.Core.Model.Walls.Of(picked) != null) AddGates();
        if (whole && !held && Shaped(picked)) AddSides(picked);
    }

    void AddLength((double Long, double Deep) sides)
    {
        double perMetre = S.View.Calibration.UnitsPerMeter ?? 0;
        if (perMetre <= 0) return;
        var form = Ui.Form(Ui.PanelLabels);
        HSlider Side(string label, double now, Func<double, (double, double)> sized, string tip) => Held(form, label, LeastSideM, MostSideM, SideStepM, now / perMetre, x => $"{x:0.#} m",
            x => T.Build.Stretch(sized(x * perMetre)), tip);
        Side("Length", sides.Long, x => (x, sides.Deep), "Its length along its front, in metres");
        Side("Width", sides.Deep, x => (sides.Long, x), "Its depth from front to back, in metres");
        Ui.AlignForm(form);
        _box.AddChild(form);
    }

    const double LeastSideM = 1, MostSideM = 400, SideStepM = 0.5;

    static bool Shaped(BuiltThing picked) => picked.Kind is BuiltKinds.Settlement or BuiltKinds.Fort or BuiltKinds.Building or BuiltKinds.Details;

    void AddGates()
    {
        _box.AddChild(Header("Wall"));
        _box.AddChild(Pictured("sweep-", [(BuildTool.Gate, "Gate", "A gate in the wall where clicked; a click on a gate closes it")], T.Build.Adding ?? "", key =>
        {
            T.Build.Arm(T.Build.Adding == key ? null : key);
            _rebuild();
        }));
    }

    void AddNaming(GridContainer form)
    {
        Choice(form, "Name", "name-", Namings, P.Naming, key => P.Naming = key);
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
            form.AddChild(Pictured("outworks-", Outworks, works >= RavelinsFrom ? "ravelins" : works >= GlacisFrom ? "glacis" : "none",
                key => T.Build.Reset(Forts.Outworks, key == "ravelins" ? Ravelins : key == "glacis" ? Glacis : NoOutworks)));
        }
        else if (picked.Kind == BuiltKinds.Settlement)
        {
            double walls = picked.Setting(Settlements.Walls, 0);
            Held(form, "Houses", 0, 1, 0.05, picked.Setting(Settlements.Houses, 0.5), Percent, x => T.Build.Reset(Settlements.Houses, x), HousesTip);
            Held(form, "Spread", 0, 1, 0.05, picked.Setting(Settlements.Spread, Settlements.UsualSpread), Percent, x => T.Build.Reset(Settlements.Spread, x), SpreadTip);
            Held(form, "Order", 0, 1, 0.05, picked.Setting(Settlements.Order, Settlements.UsualOrder), Percent, x => T.Build.Reset(Settlements.Order, x), OrderTip);
            Held(form, "Ravaged", 0, 1, 0.05, picked.Setting(Settlements.Ravaged, 0), Percent, x => T.Build.Reset(Settlements.Ravaged, x), RavagedTip);
            form.AddChild(new Label { Text = "Walls" });
            form.AddChild(Pictured("walls-", Rings(sides.Lays), Ring(sides.Lays, walls), key => T.Build.Reset(Settlements.Walls, Walled(key))));
            form.AddChild(new Label { Text = "Plan" });
            form.AddChild(Pictured("plan-", PlansOf(sides.Lays), Settlements.Form(picked), key => T.Build.Reset(Settlements.Plan, Plans.Value(key))));
        }
        Ui.AlignForm(form);
        _box.AddChild(form);
    }

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
