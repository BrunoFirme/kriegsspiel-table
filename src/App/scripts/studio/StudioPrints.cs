using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Client.Studio;
using KTable.Core.Model;

namespace KTable.App;

internal sealed class PrintPlacer
{
    readonly PieceStudio _s;

    public PrintPlacer(PieceStudio s) => _s = s;

    StudioCanvas Canvas => _s.Canvas;

    static readonly (string Text, string? Side)[] Sides = { ("Front", PrintPlace.Front), ("Back", PrintPlace.Back), ("Hidden", null) };

    static FaceBox Rounded(FaceBox b) => new() { X = Math.Round(b.X, 4), Y = Math.Round(b.Y, 4), W = Math.Round(b.W, 4), H = Math.Round(b.H, 4) };

    public static List<StudioCanvas.PrintBox> Boxes(PieceDef d, string side, int state)
    {
        var l = new List<StudioCanvas.PrintBox>();
        for (int i = 0; i < d.Counters.Count; i++)
            if (StateLooks.PrintOf(d, state, d.Counters[i]) is { } p && p.Side == side) l.Add(new($"c{i}", d.Counters[i].Name, p.Box()));
        for (int i = 0; i < d.TextLines.Count; i++)
            if (StateLooks.PrintOf(d, state, d.TextLines[i]) is { } p && p.Side == side) l.Add(new($"t{i}", d.TextLines[i].Name, p.Box()));
        return l;
    }

    public static bool IsItem(PieceDef d, string? key) =>
        key != null && (Index(key, 'c', d.Counters.Count) != null || Index(key, 't', d.TextLines.Count) != null);

    static int? Index(string key, char kind, int count) =>
        key.Length > 1 && key[0] == kind && int.TryParse(key.AsSpan(1), out var i) && i >= 0 && i < count ? i : null;

    public static PrintPlace? PlaceOf(PieceDef d, string key, int state) =>
        Index(key, 'c', d.Counters.Count) is int ci ? StateLooks.PrintOf(d, state, d.Counters[ci])
        : Index(key, 't', d.TextLines.Count) is int ti ? StateLooks.PrintOf(d, state, d.TextLines[ti]) : null;

    public static PrintPlace NextFree(PieceDef d, string side, int state) =>
        PiecePrinting.Fresh(side, d.Counters.Count(c => StateLooks.PrintOf(d, state, c)?.Side == side) + d.TextLines.Count(t => StateLooks.PrintOf(d, state, t)?.Side == side));

    public static string? FirstKey(PieceDef d) => d.Counters.Count > 0 ? "c0" : d.TextLines.Count > 0 ? "t0" : null;

    static bool Prints(PieceDef d) => d.Kind != "model";

    public void Ink(PieceInstance? shown)
    {
        if (_s.Sel is not { } d || _s.CurFace is not { } f) return;
        bool printed = Prints(d) && !PieceFaces.IsShapeFace(f.Key) && !EffectFx.IsEffect(d);
        var inst = shown?.Clone() ?? new PieceInstance { Def = d.Id };
        inst.State = Math.Max(0, f.State);
        Canvas.Printing = printed ? CanvasInk.Of(d, inst, Canvas.PieceUnits, f.Back, _s.S.Art.Look.FaceTextScale) : new();
        Canvas.Markers = printed && !f.Back ? _s.Markers.OnFace(d) : new();
    }

    public void Played(PieceInstance shown, bool reprinted)
    {
        Canvas.Widgets = shown.Widgets.ToList();
        if (reprinted) Ink(shown);
        Canvas.Redraw();
    }

    public void SyncLive()
    {
        bool live = _s.View == "paint" && _s.Sel is { } d && Prints(d) && _s.Inspector.OnTab("Piece");
        if (Canvas.PrintsLive == live) return;
        Canvas.PrintsLive = live;
        Canvas.Redraw();
        _s.RefreshHint();
    }

    public void Open(string? key)
    {
        if (_s.Sel is not { } d) return;
        Canvas.Chosen = key;
        if (key != null && !_s.Inspector.OnTab("Piece")) _s.Inspector.Hold("Piece");
        if (key != null && PlaceOf(d, key, _s.State) is { } place && Prints(d))
        {
            bool back = place.Side == PrintPlace.Back;
            int state = _s.State;
            if (_s.View != "paint")
            {
                _s.Inspector.Hold("Piece");
                _s.PaintFace(PieceFaces.Key(state, back));
                return;
            }
            if (_s.CurFace is { } f && f.Back != back && !PieceFaces.IsShapeFace(f.Key)) _ = _s.SwitchFace(PieceFaces.Key(state, back));
        }
        _s.RebuildInspector();
        _s.RefreshHint();
        Canvas.Redraw();
    }

    public void OpenFirst()
    {
        if (_s.Sel is not { } d) return;
        _s.Inspector.Hold("Piece");
        if (FirstKey(d) is { } key && Canvas.Chosen == null) Open(key);
        else _s.RebuildInspector();
    }

    public void Moved(string key, FaceBox box, string side) => Place(key, box, side, rebuild: true);

    void Place(string key, FaceBox box, string side, bool rebuild) => Set(key, PrintPlace.On(side, Rounded(box)), rebuild);

    void Set(string key, PrintPlace? place, bool rebuild)
    {
        int state = _s.State;
        _s.Apply(x =>
        {
            if (Index(key, 'c', x.Counters.Count) is int ci) StateLooks.SetPrint(x, state, x.Counters[ci], place);
            else if (Index(key, 't', x.TextLines.Count) is int ti) StateLooks.SetPrint(x, state, x.TextLines[ti], place);
        }, inspector: rebuild, what: "where it prints");
    }

    public void AddCounter()
    {
        _s.Apply(x => x.Counters.Add(new CounterDef
        {
            Name = x.Counters.Count == 0 ? "Strength" : $"Counter {x.Counters.Count + 1}", Min = 0, Max = 9, Default = 9,
            Place = Prints(x) ? NextFree(x, PrintPlace.Front, 0) : null,
        }), what: "a new counter");
        if (_s.Sel is { } d) Open($"c{d.Counters.Count - 1}");
    }

    public void AddTextLine(string? ink)
    {
        _s.Apply(x => x.TextLines.Add(new TextLineDef
        {
            Name = x.TextLines.Count == 0 ? "Name" : $"Line {x.TextLines.Count + 1}", Color = ink,
            Place = Prints(x) ? NextFree(x, PrintPlace.Front, 0) : null,
        }), what: "a new text line");
        if (_s.Sel is { } d) Open($"t{d.TextLines.Count - 1}");
    }

    void Remove(string key)
    {
        Canvas.Chosen = null;
        _s.Apply(x =>
        {
            if (Index(key, 'c', x.Counters.Count) is int ci) { StateLooks.Forget(x, x.Counters[ci]); x.Counters.RemoveAt(ci); }
            else if (Index(key, 't', x.TextLines.Count) is int ti) { StateLooks.Forget(x, x.TextLines[ti]); x.TextLines.RemoveAt(ti); }
        }, inspector: true, what: key[0] == 'c' ? "a counter taken away" : "a text line taken away");
    }

    public void Row(VBoxContainer list, PieceDef d, string key)
    {
        bool counter = key[0] == 'c';
        string name = counter ? d.Counters[Index(key, 'c', d.Counters.Count)!.Value].Name : d.TextLines[Index(key, 't', d.TextLines.Count)!.Value].Name;
        bool open = Canvas.Chosen == key;
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        var toggle = new Button
        {
            Text = (open ? "▾  " : "▸  ") + name, Flat = true, Alignment = HorizontalAlignment.Left, ClipText = true, FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = open ? "Close" : "Open its values and its box on the face",
        };
        toggle.Pressed += () => Open(open ? null : key);
        head.AddChild(toggle);
        if (Prints(d)) head.AddChild(SideChoice(PlaceOf(d, key, _s.State), key));
        var remove = Ui.RowIcon("trash", (counter ? "Take this counter away" : "Take this text line away") + "   (Ctrl+Z brings it back)");
        remove.Pressed += () => Remove(key);
        head.AddChild(remove);
        list.AddChild(head);
        if (!open) return;
        var form = Ui.Form(Ui.PanelLabels);
        if (counter) CounterForm(form, d, Index(key, 'c', d.Counters.Count)!.Value);
        else TextForm(form, d, Index(key, 't', d.TextLines.Count)!.Value);
        if (PlaceOf(d, key, _s.State) is { } place) BoxRow(form, key, place);
        Ui.AlignForm(form);
        list.AddChild(form);
        Callable.From(() => InView(form)).CallDeferred();
    }

    static void InView(Control c)
    {
        if (!GodotObject.IsInstanceValid(c) || !c.IsInsideTree()) return;
        for (var n = c.GetParent(); n != null; n = n.GetParent())
            if (n is ScrollContainer sc) { sc.EnsureControlVisible(c); return; }
    }

    OptionButton SideChoice(PrintPlace? place, string key)
    {
        var ob = new OptionButton { FitToLongestItem = false, CustomMinimumSize = Ui.Px(84, 0), TooltipText = "Where it prints" };
        foreach (var (text, _) in Sides) ob.AddItem(text);
        ob.Selected = Array.FindIndex(Sides, s => s.Side == place?.Side);
        ob.ItemSelected += i =>
        {
            var side = Sides[(int)i].Side;
            Set(key, side == null ? null : place == null ? NextFree(_s.Sel!, side, _s.State) : PrintPlace.On(side, place.Box()), rebuild: true);
            if (side != null) Open(key);
        };
        return ob;
    }

    void CounterForm(GridContainer g, PieceDef d, int ci)
    {
        var c = d.Counters[ci];
        g.AddChild(new Label { Text = "Name" });
        var name = new LineEdit { Text = c.Name };
        void Commit() { if (name.Text.Trim().Length > 0 && name.Text.Trim() != c.Name) _s.Apply(x => StateLooks.Rename(x, x.Counters[ci], name.Text.Trim()), inspector: true, what: "a counter's name"); }
        name.TextSubmitted += _ => Commit();
        name.FocusExited += Commit;
        g.AddChild(name);
        g.AddChild(new Label { Text = "Range" });
        var range = new HBoxContainer();
        range.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        range.AddChild(Spin(c.Min, "Lowest", v => _s.Apply(x => x.Counters[ci].Min = v, what: "a counter's range")));
        range.AddChild(Spin(c.Max, "Highest", v => _s.Apply(x => x.Counters[ci].Max = v, what: "a counter's range")));
        g.AddChild(range);
        g.AddChild(new Label { Text = "Start" });
        g.AddChild(Spin(c.Default, "Its value when the piece is put down", v => _s.Apply(x => x.Counters[ci].Default = v, what: "a counter's start")));
        g.AddChild(new Label { Text = "Shows" });
        var shows = new HBoxContainer();
        var named = new CheckBox { Text = "Name", ButtonPressed = c.ShowName };
        named.Toggled += on => _s.Apply(x => x.Counters[ci].ShowName = on, what: "what a counter shows");
        var buttons = new CheckBox { Text = "− +", ButtonPressed = c.ShowButtons, TooltipText = "Buttons that take one off and add one" };
        buttons.Toggled += on => _s.Apply(x => x.Counters[ci].ShowButtons = on, what: "what a counter shows");
        shows.AddChild(named);
        shows.AddChild(buttons);
        g.AddChild(shows);
    }

    static SpinBox Spin(int value, string tip, Action<int> changed)
    {
        var sb = new SpinBox { MinValue = -999, MaxValue = 999, Value = value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = Ui.Px(58, 0), TooltipText = tip };
        sb.GetLineEdit().AddThemeConstantOverride("minimum_character_width", 2);
        sb.ValueChanged += v => changed((int)v);
        return sb;
    }

    void TextForm(GridContainer g, PieceDef d, int ti)
    {
        var t = d.TextLines[ti];
        g.AddChild(new Label { Text = "Name" });
        var name = new LineEdit { Text = t.Name };
        void CommitName() { if (name.Text.Trim().Length > 0 && name.Text.Trim() != t.Name) _s.Apply(x => StateLooks.Rename(x, x.TextLines[ti], name.Text.Trim()), inspector: true, what: "a text line's name"); }
        name.TextSubmitted += _ => CommitName();
        name.FocusExited += CommitName;
        g.AddChild(name);
        g.AddChild(new Label { Text = "Start" });
        var start = new LineEdit { Text = t.Default };
        void CommitStart() { if (start.Text != t.Default) _s.Apply(x => x.TextLines[ti].Default = start.Text, what: "a text line's start"); }
        start.TextSubmitted += _ => CommitStart();
        start.FocusExited += CommitStart;
        g.AddChild(start);
        g.AddChild(new Label { Text = "Colour" });
        var ink = Ui.ColourField(Hex.To(t.Color, FaceInk.TextColor), "The colour it prints in");
        Ui.WhenPicked(ink, col => _s.Apply(x => x.TextLines[ti].Color = Hex.Of(col), what: "a text line's colour"));
        g.AddChild(ink);
    }

    void BoxRow(GridContainer g, string key, PrintPlace place)
    {
        g.AddChild(new Label { Text = "Box" });
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        var b = place.Box();
        var vals = new[] { b.X, b.Y, b.W, b.H };
        var tips = new[] { "left", "top", "width", "height" };
        var boxes = new SpinBox[vals.Length];
        for (int i = 0; i < vals.Length; i++)
        {
            boxes[i] = new SpinBox { MinValue = 0, MaxValue = 100, Step = 1, Value = Math.Round(vals[i] * 100), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = $"{tips[i]} (% of the face)" };
            boxes[i].GetLineEdit().AddThemeConstantOverride("minimum_character_width", 2);
            row.AddChild(boxes[i]);
        }
        foreach (var sb in boxes)
            sb.ValueChanged += _ =>
                Place(key, new FaceBox { X = boxes[0].Value / 100, Y = boxes[1].Value / 100, W = Math.Max(0.02, boxes[2].Value / 100), H = Math.Max(0.02, boxes[3].Value / 100) }, place.Side,
                    rebuild: false);
        g.AddChild(row);
    }
}
