using System;
using System.Linq;
using Godot;
using KTable.Client.Effects;
using KTable.Core.Model;

namespace KTable.App;

internal sealed class EffectTab
{
    readonly PieceStudio _s;

    public EffectTab(PieceStudio s) => _s = s;

    void Set(Action<EffectDef> change, bool rebuild = false) => _s.Apply(x => { x.Effect ??= EffectKinds.FromName(x.Name); change(x.Effect); }, list: rebuild, inspector: rebuild);

    public void Build(VBoxContainer page)
    {
        var d = _s.Sel!;
        var e = d.Effect ?? EffectKinds.FromName(d.Name);
        var p = _s.Inspector.Section(page, "Effect");
        KindRows(p, d, e);
        var sg = Ui.Form(76);
        p.AddChild(sg);
        Sliders(sg, e);
        LookRows(sg, e);
        Ui.AlignForm(sg);
        p.AddChild(Ui.Hint("The size of the piece is the ground it covers: a volley is a long thin piece along the line (Piece → Size)."));
        ParticleSection(page, e);
        StartFromSection(page);
    }

    void KindRows(VBoxContainer p, PieceDef d, EffectDef e)
    {
        var g = Ui.Form(76);
        p.AddChild(g);
        g.AddChild(new Label { Text = "Kind" });
        var kind = new OptionButton { FitToLongestItem = false };
        foreach (var (id, name, what) in EffectKinds.Kinds)
        {
            kind.AddItem(name);
            kind.SetItemMetadata(kind.ItemCount - 1, id);
            kind.SetItemTooltip(kind.ItemCount - 1, what);
            if (id == e.Kind) kind.Selected = kind.ItemCount - 1;
        }
        kind.ItemSelected += i => Set(x =>
        {
            x.Kind = kind.GetItemMetadata((int)i).AsString();
            x.Every = 0; x.Color = null; x.Volley = null;
            var tags = d.Tags.Where(t => t == "effect" || !EffectKinds.Kinds.Any(k => k.Id == t)).Append(x.Kind).Distinct().ToList();
            d.Tags = tags;
        }, rebuild: true);
        g.AddChild(kind);
        Ui.AlignForm(g);
        p.AddChild(Ui.Hint(EffectKinds.Kinds.FirstOrDefault(k => k.Id == e.Kind).What ?? ""));
        if (!EffectKinds.Volleys(e.Kind)) return;
        var vf = Ui.Form(76);
        vf.AddChild(new Label { Text = "Firing" });
        var how = new OptionButton { FitToLongestItem = false };
        var modes = e.Kind == "powder"
            ? new[] { ("At will", "random"), ("Volleys", "together"), ("Rolling, by platoons", "rolling") }
            : e.Kind == "column"
            ? new[] { ("All at once", "together"), ("One after another", "rolling") }
            : new[] { ("All guns together", "together"), ("In turn down the line", "rolling") };
        foreach (var (label, id) in modes) { how.AddItem(label); how.SetItemMetadata(how.ItemCount - 1, id); if (id == EffectKinds.Mode(e)) how.Selected = how.ItemCount - 1; }
        how.ItemSelected += i => Set(x => x.Volley = how.GetItemMetadata((int)i).AsString(), rebuild: true);
        vf.AddChild(how);
        Ui.AlignForm(vf);
        p.AddChild(vf);
    }

    void Sliders(GridContainer sg, EffectDef e)
    {
        void Slide(string label, string tip, double value, double min, double max, Action<EffectDef, double> put)
        {
            sg.AddChild(new Label { Text = label, TooltipText = tip, MouseFilter = Control.MouseFilterEnum.Pass });
            var row = new HBoxContainer();
            var s = new HSlider { MinValue = min, MaxValue = max, Step = 0.05, Value = value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = tip };
            var shown = new Label { Text = $"{value:0.##}", CustomMinimumSize = new Vector2(34, 0), Modulate = Ui.Muted };
            s.ValueChanged += v => { shown.Text = $"{v:0.##}"; Set(x => put(x, v)); };
            row.AddChild(s);
            row.AddChild(shown);
            sg.AddChild(row);
        }
        string many = e.Kind switch { "column" => "How many shells fall", "broadside" => "How many turrets fire", "wake" => "How many torpedoes run", _ => "How many puffs, flames or sparks" };
        Slide("Amount", many, e.Amount, 0.1, 4, (x, v) => x.Amount = v);
        Slide("Height", "How high it rises, or how far it is thrown", e.Height, 0.2, 4, (x, v) => x.Height = v);
        Slide("Puff size", "How big each puff or flame is", e.Puff, 0.25, 4, (x, v) => x.Puff = v);
        Slide("Speed", "How fast it plays", e.Speed, 0.25, 3, (x, v) => x.Speed = v);
        Slide("Wind", "How much the scenario's wind carries it (Scenario properties → Wind sets where it blows and how hard)", e.Wind, 0, 3, (x, v) => x.Wind = v);
        if (EffectKinds.Fires(e))
        {
            sg.AddChild(new Label { Text = "Every" });
            var every = new SpinBox { MinValue = 0.5, MaxValue = 120, Step = 0.5, Value = EffectKinds.Interval(e), Suffix = "s", TooltipText = "Seconds between shots" };
            every.ValueChanged += v => Set(x => x.Every = v);
            sg.AddChild(every);
        }
    }

    void LookRows(GridContainer sg, EffectDef e)
    {
        bool fire = e.Kind is "fire" or "oil";
        sg.AddChild(new Label { Text = fire ? "Smoke" : "Colour", TooltipText = fire ? "The colour of the fire's smoke" : "", MouseFilter = Control.MouseFilterEnum.Pass });
        var colRow = new HBoxContainer();
        var col = new ColorPickerButton { Color = EffectFx.ColorOf(e), CustomMinimumSize = new Vector2(48, 24), EditAlpha = false };
        col.PopupClosed += () => Set(x => x.Color = EffectKinds.Recolour("#" + col.Color.ToHtml(false), x.Color), rebuild: true);
        colRow.AddChild(col);
        if (e.Color != null) colRow.AddChild(Ui.Btn("The kind's own", () => Set(x => x.Color = null, rebuild: true)));
        sg.AddChild(colRow);
        if (EffectKinds.Burns(e.Kind))
        {
            sg.AddChild(new Label { Text = "" });
            var light = new CheckBox { Text = "Lights the table around it", ButtonPressed = e.Light };
            light.Toggled += on => Set(x => x.Light = on);
            sg.AddChild(light);
        }
        if (EffectKinds.Smokes(e.Kind))
        {
            sg.AddChild(new Label { Text = "" });
            var smoke = new CheckBox { Text = "Leaves smoke", ButtonPressed = e.Smoke };
            smoke.Toggled += on => Set(x => x.Smoke = on, rebuild: true);
            sg.AddChild(smoke);
        }
        if (EffectKinds.Lingers(e.Kind))
        {
            sg.AddChild(new Label { Text = "" });
            var lingers = new CheckBox
            {
                Text = "Dense smoke, drifting as far as real powder smoke", ButtonPressed = e.Lingers,
                TooltipText = $"Carried downwind as far as powder smoke goes in the scenario's wind, on the map's scale (or {SmokeDrift.UncalibratedMetresPerUnit:0} m to a unit before the map is calibrated)",
            };
            lingers.Toggled += on => Set(x => x.Lingers = on, rebuild: true);
            sg.AddChild(lingers);
        }
    }

    void ParticleSection(VBoxContainer page, EffectDef e)
    {
        var p = _s.Inspector.Section(page, "Particle", open: e.OwnPicture);
        var pg = Ui.Form(76);
        p.AddChild(pg);
        pg.AddChild(new Label { Text = "Picture" });
        var pic = new OptionButton { FitToLongestItem = false };
        pic.AddItem("Built in for the kind");
        pic.AddItem("Its own picture");
        pic.Selected = e.OwnPicture ? 1 : 0;
        pic.ItemSelected += i => Set(x => x.OwnPicture = i == 1, rebuild: true);
        pg.AddChild(pic);
        Ui.AlignForm(pg);
        var pr = new HBoxContainer();
        pr.AddChild(Ui.Btn("Paint it…", () => { Set(x => x.OwnPicture = true); _s.PaintFace("front"); }, "Paint the particle in the Picture view   (Alt+2)"));
        pr.AddChild(Ui.Btn("From a file…", () => _s.PickFiles(ImageFiles.Filter, f => { if (f.Length > 0) ParticleFromFile(f[0]); }, "A particle picture"), "A picture with a see-through background, about 128 px"));
        p.AddChild(pr);
        p.AddChild(Ui.Hint("Its own picture keeps its colours; the kind gives the motion and the fading."));
    }

    void StartFromSection(VBoxContainer page)
    {
        var p = _s.Inspector.Section(page, "Start from", open: false);
        var start = new OptionButton { FitToLongestItem = false };
        start.AddItem($"One of {FirmeEffects.PackName}…");
        foreach (var fp in FirmeEffects.Presets) { start.AddItem(fp.Name); start.SetItemTooltip(start.ItemCount - 1, fp.What); }
        start.ItemSelected += i =>
        {
            if (i <= 0) return;
            var fp = FirmeEffects.Presets[(int)i - 1];
            _s.Apply(x =>
            {
                bool own = x.Effect?.OwnPicture == true;
                x.Effect = fp.Fx.Clone();
                x.Effect.OwnPicture = own;
                x.Size = new Size2(fp.W, fp.H);
                x.Tags = x.Tags.Where(t => t == "effect" || !EffectKinds.Kinds.Any(k => k.Id == t)).Append(fp.Fx.Kind).Distinct().ToList();
            }, list: true, inspector: true);
        };
        p.AddChild(start);
        p.AddChild(Ui.Hint("Takes that effect's settings and size; the name stays."));
    }

    void ParticleFromFile(string path)
    {
        var sel = _s.Sel;
        if (sel == null) return;
        if (PackEdit.AddImageFile(_s.Store, _s.Pack, path) is not { } added) { _s.Flash("Could not read " + System.IO.Path.GetFileName(path)); return; }
        _s.MadeHere.Add(added.sha);
        var old = sel.Image;
        _s.Apply(x => { x.Image = added.sha; x.Effect ??= EffectKinds.FromName(x.Name); x.Effect.OwnPicture = true; }, list: true, inspector: true);
        _s.Superseded(old, added.sha);
    }
}
