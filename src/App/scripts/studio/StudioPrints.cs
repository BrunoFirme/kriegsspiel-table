using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client.Studio;
using KTable.Core.Model;

namespace KTable.App;

internal sealed class PrintPlacer
{
    readonly PieceStudio _s;

    public PrintPlacer(PieceStudio s) => _s = s;

    StudioCanvas Canvas => _s.Canvas;

    static Control Wide(Control c) { c.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; return c; }

    static FaceBox Rounded(FaceBox b) => new() { X = Math.Round(b.X, 4), Y = Math.Round(b.Y, 4), W = Math.Round(b.W, 4), H = Math.Round(b.H, 4) };

    public static List<StudioCanvas.PrintBox> Boxes(PieceDef d, string side)
    {
        var l = new List<StudioCanvas.PrintBox>();
        for (int i = 0; i < d.Counters.Count; i++)
            if (d.Counters[i].Place is { } p && p.Side == side) l.Add(new($"c{i}", d.Counters[i].Name, p.Box()));
        for (int i = 0; i < d.TextLines.Count; i++)
            if (d.TextLines[i].Place is { } p && p.Side == side) l.Add(new($"t{i}", d.TextLines[i].Name, p.Box()));
        return l;
    }

    public static bool IsItem(PieceDef d, string? key) =>
        key != null && (Index(key, 'c', d.Counters.Count) != null || Index(key, 't', d.TextLines.Count) != null);

    static int? Index(string key, char kind, int count) =>
        key.Length > 1 && key[0] == kind && int.TryParse(key.AsSpan(1), out var i) && i >= 0 && i < count ? i : null;

    public void Moved(string key, FaceBox box, string side) => Place(key, box, side, rebuild: true);

    void Place(string key, FaceBox box, string side, bool rebuild)
    {
        var b = Rounded(box);
        _s.Apply(x =>
        {
            if (Index(key, 'c', x.Counters.Count) is int ci) x.Counters[ci].Place = PrintPlace.On(side, b);
            else if (Index(key, 't', x.TextLines.Count) is int ti) x.TextLines[ti].Place = PrintPlace.On(side, b);
        }, what: "where it prints");
        if (rebuild) _s.RebuildToolPanel();
    }

    void Numbers(VBoxContainer body, PieceDef d, string key)
    {
        (FaceBox box, string side)? now = Index(key, 'c', d.Counters.Count) is int ci && d.Counters[ci].Place is { } cp ? (cp.Box(), cp.Side)
            : Index(key, 't', d.TextLines.Count) is int ti && d.TextLines[ti].Place is { } tp ? (tp.Box(), tp.Side)
            : null;
        if (now is not { } n) return;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        var vals = new[] { n.box.X, n.box.Y, n.box.W, n.box.H };
        var tips = new[] { "left", "top", "width", "height" };
        var boxes = new SpinBox[vals.Length];
        for (int i = 0; i < vals.Length; i++)
        {
            boxes[i] =
                new SpinBox { MinValue = 0, MaxValue = 100, Step = 1, Value = Math.Round(vals[i] * 100), Suffix = "%", CustomMinimumSize = new Vector2(62, 0),
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = $"{tips[i]} (% of the face)" };
            row.AddChild(boxes[i]);
        }
        foreach (var sb in boxes)
            sb.ValueChanged += _ =>
                Place(key, new FaceBox { X = boxes[0].Value / 100, Y = boxes[1].Value / 100, W = Math.Max(0.02, boxes[2].Value / 100), H = Math.Max(0.02, boxes[3].Value / 100) }, n.side,
                    rebuild: false);
        body.AddChild(row);
    }

    List<(string key, string label)> Items(PieceDef d)
    {
        var l = new List<(string, string)>();
        for (int i = 0; i < d.Counters.Count; i++) l.Add(($"c{i}", d.Counters[i].Name));
        for (int i = 0; i < d.TextLines.Count; i++) l.Add(($"t{i}", d.TextLines[i].Name));
        return l;
    }

    public void Options(VBoxContainer body)
    {
        var d = _s.Sel;
        if (d == null || d.Kind == "model") return;
        var items = Items(d);
        if (items.Count == 0)
        {
            body.AddChild(Ui.Hint("No counters or text lines"));
            return;
        }
        var chosen = items.FindIndex(i => i.key == Canvas.Chosen);
        var pick = new OptionButton { FitToLongestItem = false, ClipText = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "What the boxes on the face are" };
        for (int i = 0; i < items.Count; i++) pick.AddItem(items[i].label, i);
        pick.Selected = Math.Max(0, chosen);
        pick.ItemSelected += i =>
        {
            Canvas.Chosen = items[(int)i].key;
            Canvas.Redraw();
            _s.RebuildToolPanel();
        };
        body.AddChild(Wide(pick));
        var key = items[Math.Max(0, chosen)].key;
        Numbers(body, d, key);
        if (Index(key, 'c', d.Counters.Count) is int ci) CounterRows(body, d, ci);
        else if (Index(key, 't', d.TextLines.Count) is int ti) SideChoice(body, d.TextLines[ti].Place, ti, p => _s.Apply(x => x.TextLines[ti].Place = p));
        body.AddChild(new HSeparator());
        body.AddChild(Wide(Ui.Btn("Swap front and back", _s.SwapSides, "The front's picture and everything printed on it go to the back, and the back's to the front")));
    }

    void CounterRows(VBoxContainer body, PieceDef d, int ci)
    {
        var c = d.Counters[ci];
        SideChoice(body, c.Place, ci, p => _s.Apply(x => x.Counters[ci].Place = p));
        var row = new HBoxContainer();
        var name = new CheckBox { Text = "Name", ButtonPressed = c.ShowName };
        name.Toggled += on => _s.Apply(x => x.Counters[ci].ShowName = on);
        var buttons = new CheckBox { Text = "− +", ButtonPressed = c.ShowButtons, TooltipText = "Buttons that take one off and add one" };
        buttons.Toggled += on => _s.Apply(x => x.Counters[ci].ShowButtons = on);
        row.AddChild(name);
        row.AddChild(buttons);
        body.AddChild(row);
    }

    void SideChoice(VBoxContainer body, PrintPlace? place, int index, Action<PrintPlace?> set)
    {
        var ob = new OptionButton { FitToLongestItem = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        ob.AddItem("Not shown", 0);
        ob.AddItem("Front", 1);
        ob.AddItem("Back", 2);
        ob.Selected = place == null ? 0 : place.Side == PrintPlace.Back ? 2 : 1;
        ob.ItemSelected += i =>
        {
            string? side = i == 1 ? PrintPlace.Front : i == 2 ? PrintPlace.Back : null;
            set(side == null ? null : place == null ? PiecePrinting.Fresh(side, index) : PrintPlace.On(side, place.Box()));
            if (side != null) _s.ShowSide(side == PrintPlace.Back);
        };
        body.AddChild(Wide(ob));
    }
}
