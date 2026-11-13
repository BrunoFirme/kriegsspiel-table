using System;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed partial class ScenarioDialog : HudPart
{
    AcceptDialog _dlg = null!;
    VBoxContainer _box = null!;
    VBoxContainer _lookBox = null!;

    ScenarioDialog(HudContext c) : base(c) { }

    public static void Open(HudContext c) => new ScenarioDialog(c).Show();

    void Show()
    {
        _dlg = new AcceptDialog { Title = $"Scenario · {S.View.Name}", OkButtonText = "Done" };
        var pages = new DialogPages("scenario", new Vector2(620, 420));
        _box = pages.Add("Game");
        AddHead();
        _box = pages.Add("Briefing");
        AddBriefing();
        _box = pages.Add("Map");
        AddDistances();
        AddNorth();
        AddGrid();
        AddGround();
        _box = _lookBox = pages.Add("Light & surroundings");
        AddLighting();
        _box = pages.Add("Wind");
        AddWind();
        pages.ShowFirst();
        _dlg.AddChild(pages.Root);
        Ui.Open(HudNode, _dlg, new Vector2I(660, 500));
        Ui.FreeOnClose(_dlg);
    }

    static HBoxContainer Fact(string name, string value, Button? change = null) => Ui.Fact(name, value, Ui.DialogLabels, change);

    void AddHead()
    {
        var dlg = _dlg;
        _box.AddChild(Header("Joining"));
        _box.AddChild(Fact("Password", S.Access?.Password is { Length: > 0 } ? "set" : "none: the door is open", Btn("Invite…", () => { dlg.Hide(); InviteDialog.Open(Ctx); }, "The address, the password and the invitation to paste")));
        var pointers = new CheckBox { Text = "Umpires cannot hide their pointer", ButtonPressed = S.View.PointersAlwaysShown, Disabled = !S.IsLead };
        pointers.Toggled += on => S.Do(new SetPointerRule { AlwaysShown = on });
        _box.AddChild(pointers);
        _box.AddChild(Header("Clock"));
        _box.AddChild(Fact("Now", Fmt.Clock(S.View.Clock), Btn("Set…", () => { dlg.Hide(); ClockDialog.Open(Ctx); })));
        _box.AddChild(Header("Script"));
        _box.AddChild(Fact("Global script", S.View.Script != null ? "set" : "none", S.IsLead ? Btn("Edit…", () =>
        {
            dlg.Hide();
            Ctx.ShowScriptWindow("");
        }, "The scenario's script, run by the host for the whole table") : null));
        _box.AddChild(Header("On the table"));
        _box.AddChild(Fact("Packs", S.View.Packs.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        _box.AddChild(Fact("Map sheets", S.View.Sheets.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        _box.AddChild(Fact("Umpire board", TitleStyle.PieceCount(S.View.Layer(Layers.Umpire).Count)));
    }

    void AddDistances()
    {
        _box.AddChild(Header("Distances"));
        var units = Calibration.Measures;
        var unitPick = new OptionButton { TooltipText = "The one measure every distance is written in; grid cells count the grid set below. The map's scale does not change" };
        for (int i = 0; i < units.Length; i++)
        {
            unitPick.AddItem(units[i].Name);
            if (units[i].Id == (S.View.Calibration.Units ?? Calibration.Metres)) unitPick.Selected = i;
        }
        var unitForm = Ui.Form(Ui.DialogLabels);
        unitForm.AddChild(new Label { Text = "Written in" });
        unitForm.AddChild(unitPick);
        Ui.AlignForm(unitForm);
        _box.AddChild(unitForm);
        _scaleLine = Hint(ScaleLine(S.View.Calibration, S.View.Grid ?? new GridDef()));
        _box.AddChild(_scaleLine);
        unitPick.ItemSelected += i =>
        {
            S.Do(new SetCalibration { UnitsPerMeter = S.View.Calibration.UnitsPerMeter, Units = units[(int)i].Id });
            _scaleLine.Text = ScaleLine(new Calibration { UnitsPerMeter = S.View.Calibration.UnitsPerMeter, Units = units[(int)i].Id }, S.View.Grid ?? new GridDef());
        };
    }

    Label? _scaleLine;

    static string ScaleLine(Calibration cal, GridDef grid)
    {
        if (cal.Units == Calibration.GridCells)
            return grid.Kind == GridDef.NoGrid || grid.Pitch <= 0 ? "No grid yet: distances read in table units." : "Distances count the cells of the grid below.";
        if (cal.UnitsPerMeter is not { } upm || upm <= 0) return "No scale yet: distances read in table units until the Calibrate tool is used on a known length.";
        var (length, words) = cal.ScaleBar;
        double across = cal.ToTableUnits(length);
        return $"Scale: {words} on the ground is {across:0.##} table units, read as {Fmt.Distance(across, cal)}.";
    }

    void AddNorth()
    {
        _box.AddChild(Header("North"));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Block);
        var dial = new NorthDial(Cfg.Keys) { North = S.View.Calibration.North };
        row.AddChild(dial);
        var col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        var says = Hint(NorthLine(dial.North));
        col.AddChild(says);
        row.AddChild(col);
        _box.AddChild(row);
        dial.Turned += (deg, final) =>
        {
            says.Text = NorthLine(deg);
            if (final && deg != S.View.Calibration.North) S.Do(new SetCalibration { UnitsPerMeter = S.View.Calibration.UnitsPerMeter, North = deg });
        };
    }

    static string NorthLine(double north) => north == 0 ? "North is the top of the map."
        : FormattableString.Invariant($"North is {north:0}° clockwise from the top of the map, so the top faces {Wind.Point(Calibration.Turned(-north))} ({Calibration.Turned(-north):000}°).");

    void AddGrid()
    {
        _box.AddChild(Header("Grid"));
        var g0 = S.View.Grid ?? new GridDef();
        var kinds = new[] { ("none", "None"), ("square", "Squares"), ("hex", "Hexes") };
        var gridForm = Ui.Form(Ui.DialogLabels);
        var kindPick = new OptionButton();
        for (int i = 0; i < kinds.Length; i++) { kindPick.AddItem(kinds[i].Item2); if (kinds[i].Item1 == g0.Kind) kindPick.Selected = i; }
        gridForm.AddChild(new Label { Text = "Shape" });
        gridForm.AddChild(kindPick);
        gridForm.AddChild(new Label { Text = "Across" });
        var pitch = new SpinBox { MinValue = 0.1, MaxValue = 5000, Step = 0.1, Value = g0.Pitch, Suffix = "u", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "The distance across one cell, in table units; a hex is measured across its flats" };
        gridForm.AddChild(pitch);
        gridForm.AddChild(new Label { Text = "Divide by" });
        var subs = new SpinBox { MinValue = 1, MaxValue = 20, Step = 1, Value = g0.Subdivisions, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "1 leaves the cells plain; 2 and up draw that many lighter lines inside each one" };
        gridForm.AddChild(subs);
        Ui.AlignForm(gridForm);
        _box.AddChild(gridForm);
        var snap = new CheckBox { Text = "Pieces snap to the cells", ButtonPressed = g0.Snap, TooltipText = "A piece set down lands on the centre of the nearest cell, the smaller cells when squares are divided; a group keeps its shape" };
        _box.AddChild(snap);
        var gridHint = Hint("");
        _box.AddChild(gridHint);
        GridDef ReadGrid() =>
            new() { Kind = kinds[kindPick.Selected < 0 ? 0 : kindPick.Selected].Item1, Pitch = pitch.Value, Subdivisions = (int)subs.Value, Color = g0.Color, Snap = snap.ButtonPressed };
        void ShowGrid()
        {
            var gd = ReadGrid();
            bool on = gd.Kind != "none";
            bool canDivide = gd.Kind == "square";
            pitch.Visible = on;
            subs.Visible = canDivide;
            snap.Visible = on;
            foreach (var c in gridForm.GetChildren())
                if (c is Label l) l.Visible = l.Text switch { "Across" => on, "Divide by" => canDivide, _ => true };
            gridHint.Visible = on;
            if (_scaleLine != null && GodotObject.IsInstanceValid(_scaleLine)) _scaleLine.Text = ScaleLine(S.View.Calibration, gd);
            gridHint.Text = !on ? ""
                : gd.Kind == "hex" ? $"One hex is {Fmt.Distance(gd.Pitch, S.View.Calibration)} across the flats."
                : $"One cell is {Fmt.Distance(gd.Pitch, S.View.Calibration)}{(gd.Subdivisions > 1 ? $", divided into {gd.Subdivisions}" : "")}.";
        }
        void SendGrid() { S.Do(new SetGrid { Grid = ReadGrid() }); ShowGrid(); }
        kindPick.ItemSelected += _ => SendGrid();
        pitch.ValueChanged += _ => SendGrid();
        subs.ValueChanged += _ => SendGrid();
        snap.Toggled += _ => SendGrid();
        ShowGrid();
    }

    static HSlider LightRow(GridContainer lg, string label, double min, double max, double step, double v, Func<double, string> say)
    {
        lg.AddChild(new Label { Text = label });
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Group);
        var sl =
            new HSlider { MinValue = min, MaxValue = max, Step = step, Value = v, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                CustomMinimumSize = new Vector2(170, 0) };
        var read = Ui.ValueBox(sl, say, 58);
        row.AddChild(sl);
        row.AddChild(read);
        lg.AddChild(row);
        return sl;
    }

    const double SunLit = 0.01;

    void AddLighting(Lighting? shown = null)
    {
        var cur = shown ?? S.View.Lighting;
        var l0 = cur.Clone();
        var presets = LightPresets.All;
        var pick = PresetPick(presets, l0);
        var lg = Ui.Form(Ui.DialogLabels);
        static string Percent(double v) => $"{v * 100:0}%";
        static string Warmth(double v) => v < -0.55 ? "cold" : v < -0.15 ? "cool" : v <= 0.15 ? "neutral" : v <= 0.55 ? "warm" : "hot";
        var sun = LightRow(lg, "Light", 0, 1.5, 0.05, l0.SunEnergy, Percent);
        var amb = LightRow(lg, "Ambient", 0, 1.5, 0.05, l0.Ambient, Percent);
        var warm = LightRow(lg, "Colour", -1, 1, 0.05, l0.Warmth, Warmth);
        warm.TooltipText = "Bluer to the left, redder to the right";
        var place = new SunRows(lg, l0);
        Ui.AlignForm(lg);
        var shadows = new CheckBox { Text = "Shadows", ButtonPressed = l0.Shadows };
        bool sync = false;
        var byClock = new CheckBox
        {
            Text = "Follow the game clock", ButtonPressed = l0.FromClock,
            TooltipText = "The game's hour sets the light: dark before dawn, warm and low at sunrise and sunset, white at midday.",
        };
        var daylight = new DaylightRows(l0);
        Lighting ReadLight()
        {
            var l = S.View.Lighting.Clone();
            (l.SunEnergy, l.Ambient, l.Warmth, l.SunFrom, l.SunHeight) = (sun.Value, amb.Value, warm.Value, place.From, place.Height);
            (l.Shadows, l.FromClock) = (shadows.ButtonPressed, byClock.ButtonPressed);
            daylight.Into(l);
            return l;
        }
        void PreviewLight()
        {
            if (sync) return;
            var l = ReadLight();
            place.Shine(l);
            T.ApplyLighting(l.FromClock ? DayLight.At(S.View.Clock, l) : l);
        }
        void SendLight() { if (sync) return; S.Do(new SetLighting { Lighting = ReadLight() }); }
        WhenDragged(new[] { sun, amb, warm }, () => { if (!sync) pick.Selected = 0; PreviewLight(); }, SendLight);
        place.Changed += final =>
        {
            if (sync) return;
            pick.Selected = 0;
            PreviewLight();
            if (final) SendLight();
        };
        shadows.Toggled += _ =>
        {
            if (!sync) pick.Selected = 0;
            PreviewLight();
            SendLight();
        };
        pick.ItemSelected += i =>
        {
            if (i == 0) return;
            var p = presets[(int)i - 1].Light;
            sync = true;
            sun.Value = p.SunEnergy;
            amb.Value = p.Ambient;
            warm.Value = p.Warmth;
            place.Show(p.SunFrom, p.SunHeight);
            shadows.ButtonPressed = p.Shadows;
            sync = false;
            PreviewLight();
            SendLight();
        };
        var clockHint = Hint("");
        var manual = Manual(pick, lg);
        void ShowClockState()
        {
            bool on = byClock.ButtonPressed;
            manual.Visible = !on;
            clockHint.Visible = on;
            daylight.Form.Visible = on;
            clockHint.Text = ClockLine(ReadLight());
        }
        daylight.Changed += () =>
        {
            ShowClockState();
            PreviewLight();
            SendLight();
        };
        byClock.Toggled += _ =>
        {
            ShowClockState();
            PreviewLight();
            SendLight();
        };
        ShowClockState();
        _box.AddChild(Header("Source"));
        _box.AddChild(byClock);
        _box.AddChild(clockHint);
        _box.AddChild(daylight.Form);
        _box.AddChild(manual);
        _box.AddChild(shadows);
        AddSurroundings(cur);
    }

    static void WhenDragged(HSlider[] sliders, Action moved, Action settled)
    {
        foreach (var slider in sliders)
        {
            bool dragging = false;
            slider.DragStarted += () => dragging = true;
            slider.ValueChanged += _ => { moved(); if (!dragging) settled(); };
            slider.DragEnded += _ => { dragging = false; settled(); };
        }
    }

    static OptionButton PresetPick(System.Collections.Generic.IReadOnlyList<(string Name, Lighting Light)> presets, Lighting now)
    {
        var pick = new OptionButton();
        pick.AddItem("Custom");
        foreach (var (name, _) in presets) pick.AddItem(name);
        for (int i = 0; i < presets.Count; i++) if (LightPresets.Same(presets[i].Light, now)) pick.Selected = i + 1;
        return pick;
    }

    static VBoxContainer Manual(OptionButton pick, GridContainer lights)
    {
        var manual = new VBoxContainer();
        manual.AddThemeConstantOverride("separation", Ui.Gap.Group);
        var presetForm = Ui.Form(Ui.DialogLabels);
        presetForm.AddChild(new Label { Text = "Preset" });
        presetForm.AddChild(pick);
        Ui.AlignForm(presetForm);
        manual.AddChild(presetForm);
        manual.AddChild(lights);
        return manual;
    }

    void RefreshLook(Lighting sent)
    {
        foreach (var child in _lookBox.GetChildren()) child.QueueFree();
        var was = _box;
        _box = _lookBox;
        AddLighting(sent);
        _box = was;
    }


    void AddBriefing()
    {
        _box.AddChild(Hint("Observers do not see it · # heading · - list"));
        var text = new TextEdit
        {
            Text = S.View.Briefing ?? "", Editable = S.IsLead, WrapMode = TextEdit.LineWrappingMode.Boundary,
            CustomMinimumSize = new Vector2(0, 260), SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            PlaceholderText = S.IsLead ? "18:00. The Prussian IV Corps holds Plancenoit…" : "No briefing yet.",
        };
        _box.AddChild(text);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Group);
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        Action keep = () => { };
        row.AddChild(Btn("Read it…", () =>
        {
            keep();
            _dlg.Hide();
            BriefingWindow.Open(HudNode, S);
        }, "The briefing in a window of its own, with find"));
        if (S.IsLead)
        {
            void Keep() { var t = text.Text.Trim().Length == 0 ? null : text.Text; if (t != S.View.Briefing) S.Do(new SetBriefing { Text = t }); }
            keep = Keep;
            text.FocusExited += Keep;
            _dlg.Confirmed += Keep;
            _dlg.CloseRequested += Keep;
            row.AddChild(Ui.Primary("Save", Keep, "Every umpire at the table gets it"));
        }
        _box.AddChild(row);
    }

    void AddWind()
    {
        var w0 = S.View.Wind.Clone();
        var windRow = new HBoxContainer();
        windRow.AddThemeConstantOverride("separation", Ui.Gap.Block);
        var rose = new WindRose { From = w0.From, Strength = w0.Strength };
        windRow.AddChild(rose);
        var wcol = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        var wf = Ui.Form(Ui.DialogLabels);
        wf.AddChild(new Label { Text = "From" });
        var fromBox = new SpinBox { MinValue = 0, MaxValue = 360, Step = 1, Value = w0.From, Suffix = "°", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "0 north, 90 east, 180 south, 270 west" };
        wf.AddChild(fromBox);
        wf.AddChild(new Label { Text = "Strength" });
        var strRow = new HBoxContainer();
        strRow.AddThemeConstantOverride("separation", Ui.Gap.Group);
        var strength =
            new HSlider { MinValue = 0, MaxValue = 5, Step = 0.25, Value = w0.Strength, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                CustomMinimumSize = new Vector2(120, 0) };
        strRow.AddChild(strength);
        strRow.AddChild(Ui.ValueBox(strength, v => $"{v:0.##}", 40));
        wf.AddChild(strRow);
        Ui.AlignForm(wf);
        wcol.AddChild(wf);
        var windSays = Hint("");
        wcol.AddChild(windSays);
        windRow.AddChild(wcol);
        _box.AddChild(windRow);
        bool windSync = false;
        Wind ReadWind() => new() { From = rose.From, Strength = strength.Value };
        void ShowWind()
        {
            var w = ReadWind();
            rose.Strength = w.Strength;
            windSays.Text = w.Strength < 0.25 ? "Calm"
                : $"{char.ToUpperInvariant(Wind.Words(w.Strength)[0])}{Wind.Words(w.Strength)[1..]} from the {Wind.Point(w.From)}";
            T.ApplyWind(w);
        }
        void SendWind() { if (windSync) return; S.Do(new SetWind { Wind = ReadWind() }); }
        rose.Turned += (deg, final) =>
        {
            windSync = true;
            fromBox.Value = Math.Round(deg) % 360;
            windSync = false;
            ShowWind();
            if (final) SendWind();
        };
        fromBox.ValueChanged += v =>
        {
            if (windSync) return;
            rose.From = v;
            ShowWind();
            SendWind();
        };
        bool windDrag = false;
        strength.DragStarted += () => windDrag = true;
        strength.ValueChanged += _ => { ShowWind(); if (!windDrag) SendWind(); };
        strength.DragEnded += _ => { windDrag = false; SendWind(); };
        ShowWind();
    }
}
