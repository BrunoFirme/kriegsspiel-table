using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Studio;

namespace KTable.App;

internal sealed class ToolPanel
{
    readonly PieceStudio _s;
    FloatPanel _panel = null!;
    VBoxContainer _body = null!;
    TextEdit? _textEdit;
    const float PanelW = 214;

    public ToolPanel(PieceStudio s)
    {
        _s = s;
        Colours = new ColourChooser(s, this);
    }

    public ColourChooser Colours { get; }
    public FloatPanel Panel => _panel;
    public VBoxContainer Body => _body;

    StudioCanvas Canvas => _s.Canvas;
    StudioPrefs Prefs => _s.Prefs;

    public void Build()
    {
        _panel = new FloatPanel("Brush") { Key = "tool" };
        _panel.AddThemeStyleboxOverride("panel", Ui.Panel(0.95f));
        _body = _panel.Body;
        _body.CustomMinimumSize = new Vector2(PanelW, 0);
        _body.AddThemeConstantOverride("separation", Ui.Gap.Row);
        _panel.Closed = () =>
        {
            _panel.Visible = false;
            if (_s.View == "model") _s.Model?.SetTool("orbit");
            else if (_s.View == "paint") Canvas.SetTool("pan");
            Canvas.Refit();
        };
        _panel.Moved = (k, at) =>
        {
            Prefs.Panels[k] = new[] { at.X, at.Y };
            Prefs.Save();
            Canvas.Refit();
        };
        Ui.SwallowWheel(_panel);
        _s.Root.AddChild(_panel);
        _panel.Visible = false;
    }

    public void Place()
    {
        if (_panel == null) return;
        bool show = _s.Sel != null && ((_s.View == "paint" && Canvas.Tool != "pan") || (_s.View == "model" && _s.Model?.Tool is "paint" or "erase" or "fill" or "pick" or "decal"));
        if (!show) { _panel.Visible = false; return; }
        _panel.Visible = true;
        var c = _s.Centre.GetGlobalRect();
        _panel.Ceiling = c.Position.Y;
        _panel.Fit();
        if (Prefs.Panels.TryGetValue("tool", out var at) && at.Length == 2) _panel.Position = new Vector2(at[0], at[1]);
        else _panel.Position = new Vector2(c.Position.X + 6, c.Position.Y + 6);
        _panel.Clamp();
        Callable.From(() => Canvas.Refit()).CallDeferred();
    }

    public void Refit() => Callable.From(() => { if (_panel.Visible) { _panel.Fit(); Canvas.Refit(); } }).CallDeferred();

    public void Rebuild()
    {
        if (_body == null) return;
        PieceStudio.Clear(_body);
        _textEdit = null;
        if (_s.View == "model")
        {
            _s.Model?.Panels.BuildOptions(_s, _body);
            _panel.SetTitle(_s.Model?.ToolTitle ?? "3D");
            Place();
            Refit();
            _s.RefreshHint();
            return;
        }
        if (_s.View != "paint" || _s.Sel == null) { Place(); return; }
        var tool = Canvas.Tool;
        _panel.SetTitle(tool switch
        {
            "move" => "Move", "brush" => "Brush", "erase" => "Rubber", "shape" => "Shapes", "text" => "Text", "fill" => "Fill",
            "pick" => "Colour picker", "crop" => "Crop", "outline" => "Outline", "measure" => "Measure", "place" => "Place", _ => "Hand",
        });
        switch (tool)
        {
            case "brush":
                SizeRow(Prefs.Brush, 0.5f, 400, v => Prefs.Brush = v);
                Colours.Section(false);
                break;
            case "erase":
                SizeRow(Prefs.Eraser, 0.5f, 600, v => Prefs.Eraser = v);
                break;
            case "shape":
                ShapeOptions();
                break;
            case "text":
                TextOptions();
                break;
            case "fill":
                FillOptions();
                break;
            case "pick":
                Colours.Section(false);
                break;
            case "move":
                TransformRows();
                break;
            case "crop":
                CropOptions();
                break;
            case "outline":
                OutlineOptions();
                break;
            case "measure":
                MeasureOptions();
                break;
            case "place":
                PlaceOptions();
                break;
        }
        Place();
        Refit();
        _s.RefreshHint();
    }

    internal static Label Small(string text)
    {
        var l = new Label { Text = text, Modulate = Ui.Muted, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        l.AddThemeFontSizeOverride("font_size", 12);
        return l;
    }

    static Button Wide(Button b) { b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; return b; }

    void SizeRow(float value, float min, float max, Action<float> set, string label = "Size")
    {
        var row = new HBoxContainer();
        row.AddChild(Small(label));
        var s =
            new HSlider { MinValue = Math.Sqrt(min), MaxValue = Math.Sqrt(max), Step = 0.01, Value = Math.Sqrt(value), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        var num = new SpinBox { MinValue = min, MaxValue = max, Step = 0.5, Value = value, CustomMinimumSize = new Vector2(66, 0), TooltipText = "pixels" };
        bool sync = false;
        s.ValueChanged += v =>
        {
            if (sync) return;
            sync = true;
            num.Value = Math.Round(v * v, 1);
            sync = false;
            set((float)(v * v));
            Canvas.Redraw();
        };
        num.ValueChanged += v =>
        {
            if (sync) return;
            sync = true;
            s.Value = Math.Sqrt(v);
            sync = false;
            set((float)v);
            Canvas.Redraw();
        };
        row.AddChild(s);
        row.AddChild(num);
        _body.AddChild(row);
    }

    void ShapeOptions()
    {
        ShapeKinds();
        FillModes();
        if (Prefs.ShapeFill != "fill" || Prefs.ShapeKind is "line" or "arrow") SizeRow(Prefs.Line, 0.5f, 200, v => Prefs.Line = v, "Line");
        if (Prefs.ShapeKind is "ngon" or "star")
        {
            var row = new HBoxContainer();
            row.AddChild(Small(Prefs.ShapeKind == "star" ? "Points" : "Sides"));
            var sides = new SpinBox { MinValue = 3, MaxValue = 48, Value = Prefs.Sides, CustomMinimumSize = new Vector2(70, 0) };
            sides.ValueChanged += v => Prefs.Sides = (int)v;
            row.AddChild(sides);
            if (Prefs.ShapeKind == "star")
            {
                var inner =
                    new HSlider { MinValue = 0.1, MaxValue = 0.9, Step = 0.01, Value = Prefs.StarInner, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = "How deep the points are" };
                inner.ValueChanged += v => Prefs.StarInner = (float)v;
                row.AddChild(inner);
                row.AddChild(Ui.ValueBox(inner, v => $"{v * 100:0}%", 36));
            }
            _body.AddChild(row);
        }
        Colours.Section(Prefs.ShapeFill == "both" && Prefs.ShapeKind is not ("line" or "arrow"));
    }

    void ShapeKinds()
    {
        var kinds = new (string Key, string Icon, string Tip)[]
        {
            ("line", "shape-line", "Line"), ("rect", "shape-rect", "Rectangle"), ("round", "rounded", "Rounded rectangle"), ("ellipse", "shape-ellipse", "Ellipse"),
            ("arrow", "shape-arrow", "Arrow"), ("poly", "outline", "Polygon: click the corners"), ("ngon", "ngon", "Regular polygon"), ("star", "star", "Star"),
        };
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", Ui.Gap.Tight);
        grid.AddThemeConstantOverride("v_separation", Ui.Gap.Tight);
        foreach (var (key, icon, tip) in kinds)
        {
            var b =
                new Button { ToggleMode = true, ButtonPressed = Prefs.ShapeKind == key, FocusMode = Control.FocusModeEnum.None, TooltipText = tip, CustomMinimumSize = new Vector2(30, 30),
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            b.AddChild(icon switch
            {
                "rounded" => new ShapeGlyph { Kind = "round" },
                "ngon" => new ShapeGlyph { Kind = "ngon" },
                "star" => new ShapeGlyph { Kind = "star" },
                _ => new StudioIcon { Kind = icon },
            });
            var k = key;
            b.Pressed += () =>
            {
                Prefs.ShapeKind = k;
                Canvas.SetTool("shape");
                Rebuild();
            };
            grid.AddChild(b);
        }
        _body.AddChild(grid);
    }

    void FillModes()
    {
        if (Prefs.ShapeKind is "line" or "arrow") return;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Hair);
        foreach (var (key, text, tip) in new[] { ("line", "Line", "Only the line round it"), ("fill", "Filled", "Filled with the colour, no line"), ("both", "Both", "A line round a fill of the second colour") })
        {
            var b =
                new Button { Text = text, ToggleMode = true, ButtonPressed = Prefs.ShapeFill == key, FocusMode = Control.FocusModeEnum.None, TooltipText = tip,
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var k = key;
            b.Pressed += () => { Prefs.ShapeFill = k; Rebuild(); };
            row.AddChild(b);
        }
        _body.AddChild(row);
    }

    void FillOptions()
    {
        var row = new HBoxContainer();
        row.AddChild(Small("Reach"));
        var tol =
            new HSlider { MinValue = 0, MaxValue = 0.6, Step = 0.01, Value = Prefs.FillTolerance, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = "How different a colour may be and still be flooded" };
        tol.ValueChanged += v => Prefs.FillTolerance = (float)v;
        row.AddChild(tol);
        row.AddChild(Ui.ValueBox(tol, v => $"{v * 100:0}%", 36));
        _body.AddChild(row);
        var all = new CheckBox { Text = "Look at every layer", ButtonPressed = Prefs.FillAllLayers, TooltipText = "Off: only the selected layer decides where the fill stops" };
        all.Toggled += on => Prefs.FillAllLayers = on;
        _body.AddChild(all);
        Colours.Section(false);
    }

    void CropOptions()
    {
        if (Canvas.Sel is { Kind: "image" } il)
        {
            _body.AddChild(Wide(Ui.Btn("Show the whole picture", () =>
            {
                il.Crop = null;
                Canvas.Refresh(il);
                Canvas.Commit("Uncrop");
            })));
            _body.AddChild(Wide(Ui.Btn("Trim its empty edges", () => TrimLayer(il), "Crop to where the picture has something on it")));
        }
        else _body.AddChild(Small("Select a picture layer to crop it."));
        _body.AddChild(Wide(Ui.Btn("Cut the face to the layer", () =>
        {
            if (Canvas.Sel is not { } l) return;
            Canvas.CropDocument(DocBounds(l));
            _s.RebuildFaceBar();
        }, "The whole picture shrinks to the selected layer's frame")));
    }

    void MeasureOptions()
    {
        var row = new HBoxContainer();
        row.AddChild(Small("m per unit"));
        var mpu = new SpinBox { MinValue = 0.1, MaxValue = 5000, Step = 0.5, Value = Prefs.MetresPerUnit, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "For the metre reading: depends on the map (Miranda de Azán is 48.5 m)" };
        mpu.ValueChanged += v => { Prefs.MetresPerUnit = v; Canvas.Redraw(); };
        row.AddChild(mpu);
        _body.AddChild(row);
        _body.AddChild(Wide(Ui.Btn("Remove every measure", () => { Canvas.Doc.Measures.Clear(); Canvas.Commit("Measures removed"); })));
    }

    void PlaceOptions()
    {
        _s.Prints.Options(_body);
        var show = new CheckBox { Text = "Show the script's buttons", ButtonPressed = Prefs.ShowWidgets, TooltipText = "Where the piece's script draws on this face, in green" };
        show.Toggled += on => { Prefs.ShowWidgets = on; Canvas.Redraw(); };
        _body.AddChild(show);
    }

    void TextOptions()
    {
        var l = Canvas.Sel is { Kind: "text" } tl ? tl : null;
        if (l != null)
        {
            _textEdit = new TextEdit { Text = l.Text ?? "", CustomMinimumSize = new Vector2(0, 52), WrapMode = TextEdit.LineWrappingMode.Boundary, PlaceholderText = "the words" };
            _textEdit.TextChanged += () =>
            {
                l.Text = _textEdit.Text;
                Canvas.Refresh(l);
                _s.Faces.CommitSoon("Text");
            };
            _textEdit.FocusExited += () => Canvas.Commit("Text");
            _body.AddChild(_textEdit);
        }
        _body.AddChild(FontChoice(l?.Font ?? Prefs.TextFont, f =>
        {
            Prefs.TextFont = f;
            if (l != null)
            {
                l.Font = f;
                Canvas.Refresh(l);
                Canvas.Commit("Font");
            }
        }));
        var row = new HBoxContainer();
        var sz = new SpinBox { MinValue = 4, MaxValue = 600, Value = l?.Size ?? Prefs.TextSize, CustomMinimumSize = new Vector2(76, 0), TooltipText = "Size in pixels" };
        sz.ValueChanged += v =>
        {
            Prefs.TextSize = (float)v;
            if (l != null)
            {
                l.Size = (float)v;
                Canvas.Refresh(l);
                Canvas.Commit("Text size");
            }
        };
        row.AddChild(sz);
        var bold = new Button { Text = "B", ToggleMode = true, ButtonPressed = l?.Bold ?? Prefs.TextBold, FocusMode = Control.FocusModeEnum.None, TooltipText = "Bold", CustomMinimumSize = new Vector2(28, 0) };
        bold.Toggled += on =>
        {
            Prefs.TextBold = on;
            if (l != null)
            {
                l.Bold = on;
                Canvas.Refresh(l);
                Canvas.Commit("Bold");
            }
        };
        row.AddChild(bold);
        if (l != null) TextLook(row, l);
        _body.AddChild(row);
        Colours.Section(false);
    }

    void TextLook(HBoxContainer row, ArtLayer l)
    {
        var italic = new Button { Text = "I", ToggleMode = true, ButtonPressed = l.Italic, FocusMode = Control.FocusModeEnum.None, TooltipText = "Italic", CustomMinimumSize = new Vector2(28, 0) };
        italic.Toggled += on =>
        {
            l.Italic = on;
            Canvas.Refresh(l);
            Canvas.Commit("Italic");
        };
        row.AddChild(italic);
        var olc = new ColorPickerButton { Color = Hex.To(l.OutlineColor, Colors.Black), EditAlpha = true, CustomMinimumSize = new Vector2(30, 24), TooltipText = "The outline's colour" };
        var olw = new SpinBox { MinValue = 0, MaxValue = 60, Step = 0.5, Value = l.OutlineW, CustomMinimumSize = new Vector2(60, 0), TooltipText = "Outline width (0: none)" };
        void SetOutline(Color colour)
        {
            l.OutlineW = (float)olw.Value;
            l.OutlineColor = l.OutlineW > 0 ? Hex.Of(colour) : null;
            Canvas.Refresh(l);
            Canvas.Commit("Text outline");
        }
        Ui.WhenPicked(olc, SetOutline);
        olw.ValueChanged += _ => SetOutline(olc.Color);
        row.AddChild(olc);
        row.AddChild(olw);
    }

    static string[]? _systemFonts;

    static OptionButton FontChoice(string current, Action<string> set)
    {
        _systemFonts ??= OS.GetSystemFonts().Distinct().OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
        var ob = new OptionButton { FitToLongestItem = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "The font: any installed on this PC" };
        var names = new List<string> { "Segoe UI", "serif" };
        names.AddRange(_systemFonts.Where(f => f != "Segoe UI"));
        foreach (var n in names) { ob.AddItem(n == "serif" ? "Serif (Georgia)" : n); if (n == current) ob.Selected = ob.ItemCount - 1; }
        ob.ItemSelected += i => set(names[(int)i]);
        return ob;
    }

    public void FocusTextEdit()
    {
        Callable.From(() => { if (_textEdit != null && GodotObject.IsInstanceValid(_textEdit)) { _textEdit.GrabFocus(); _textEdit.SelectAll(); } }).CallDeferred();
    }

    void TransformRows()
    {
        if (Canvas.Sel is not { } l) { _body.AddChild(Small("Click a layer on the picture.")); return; }
        var g = new GridContainer { Columns = 4 };
        g.AddThemeConstantOverride("h_separation", Ui.Gap.Tight);
        void Num(string label, double v, double min, double max, double step, string tip, Action<double> set)
        {
            g.AddChild(Small(label));
            var s =
                new SpinBox { MinValue = min, MaxValue = max, Step = step, Value = Math.Round(v / step) * step + 0.0, AllowGreater = true, AllowLesser = true, CustomMinimumSize = new Vector2(70, 0),
                    TooltipText = tip, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            s.ValueChanged += x =>
            {
                set(x);
                Canvas.R.Moved(l);
                Canvas.Redraw();
                Canvas.Commit("Place");
            };
            g.AddChild(s);
        }
        Num("x", l.X, -4096, 8192, 1, "Across, in pixels", v => l.X = (float)v);
        Num("y", l.Y, -4096, 8192, 1, "Down, in pixels", v => l.Y = (float)v);
        Num("w%", Math.Abs(l.Sx) * 100, 1, 5000, 1, "Width, % of the layer's own", v => l.Sx = (float)(v / 100) * Mathf.Sign(l.Sx == 0 ? 1 : l.Sx));
        Num("h%", Math.Abs(l.Sy) * 100, 1, 5000, 1, "Height, % of the layer's own", v => l.Sy = (float)(v / 100) * Mathf.Sign(l.Sy == 0 ? 1 : l.Sy));
        Num("°", l.Rot, -360, 360, 1, "Turned, in degrees", v => l.Rot = (float)v);
        _body.AddChild(g);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Hair);
        void Ico(string icon, string tip, Action a)
        {
            var b = new Button { CustomMinimumSize = new Vector2(30, 26), TooltipText = tip, FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            b.AddChild(new StudioIcon { Kind = icon });
            b.Pressed += a;
            row.AddChild(b);
        }
        void Txt(string text, string tip, Action a)
        {
            var b = Ui.Btn(text, a, tip);
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(b);
        }
        Ico("flip-h", "Mirror left to right", () => { l.Sx = -l.Sx; Changed(); });
        Ico("flip-v", "Mirror top to bottom", () => { l.Sy = -l.Sy; Changed(); });
        Txt("−90°", "A quarter turn left", () => { l.Rot -= 90; Changed(); });
        Txt("+90°", "A quarter turn right", () => { l.Rot += 90; Changed(); });
        _body.AddChild(row);
        var row2 = new HBoxContainer();
        row2.AddThemeConstantOverride("separation", Ui.Gap.Hair);
        foreach (var (text, tip, act) in new (string, string, Action)[]
        {
            ("Centre", "Its middle on the face's middle", () => CentreLayer(l, true, true)),
            ("Fit", "As large as fits inside the face", () => FitLayer(l, cover: false)),
            ("Fill", "Covers the whole face", () => FitLayer(l, cover: true)),
        })
        {
            var b = Ui.Btn(text, act, tip);
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row2.AddChild(b);
        }
        _body.AddChild(row2);

        void Changed()
        {
            Canvas.R.Moved(l);
            Canvas.Redraw();
            Canvas.Commit("Place");
            Rebuild();
        }
    }

    Rect2 DocBounds(ArtLayer l)
    {
        var b = StudioCanvas.LocalBounds(l);
        var xf = l.Xf();
        var r = new Rect2(xf * b.Position, Vector2.Zero);
        foreach (var p in new[] { new Vector2(b.End.X, b.Position.Y), b.End, new Vector2(b.Position.X, b.End.Y) }) r = r.Expand(xf * p);
        return r;
    }

    void CentreLayer(ArtLayer l, bool x, bool y)
    {
        var c = DocBounds(l).GetCenter();
        var d = new Vector2(Canvas.Doc.W, Canvas.Doc.H) / 2 - c;
        if (x) l.X += d.X;
        if (y) l.Y += d.Y;
        Canvas.R.Moved(l);
        Canvas.Redraw();
        Canvas.Commit("Centre");
        Rebuild();
    }

    void FitLayer(ArtLayer l, bool cover)
    {
        var b = StudioCanvas.LocalBounds(l);
        if (b.Size.X < 0.01f || b.Size.Y < 0.01f) return;
        float kx = Canvas.Doc.W / b.Size.X, ky = Canvas.Doc.H / b.Size.Y;
        float k = cover ? Mathf.Max(kx, ky) : Mathf.Min(kx, ky);
        l.Rot = 0;
        l.Sx = k * Mathf.Sign(l.Sx == 0 ? 1 : l.Sx);
        l.Sy = k * Mathf.Sign(l.Sy == 0 ? 1 : l.Sy);
        var c = l.Xf() * b.GetCenter();
        l.X += Canvas.Doc.W / 2f - c.X;
        l.Y += Canvas.Doc.H / 2f - c.Y;
        Canvas.R.Moved(l);
        Canvas.Redraw();
        Canvas.Commit(cover ? "Fill the face" : "Fit");
        Rebuild();
    }

    void TrimLayer(ArtLayer l)
    {
        if (l.Src == null || Canvas.R.Img(l.Src) is not { } img) return;
        var used = img.GetUsedRect();
        if (used.Size.X <= 0 || used.Size.Y <= 0) return;
        l.Crop = new float[] { used.Position.X, used.Position.Y, used.Size.X, used.Size.Y };
        Canvas.Refresh(l);
        Canvas.Commit("Trim");
    }

    void OutlineOptions()
    {
        if (_s.Sel is not { } d || _s.CurFace is not { } f || PieceFaces.IsShapeFace(f.Key) || d.Kind == "model") return;
        int look = PieceFramer.LookOf(d, f.State);
        int target = look <= 0 || PieceFaces.OwnSize(d, look) ? look : 0;
        _body.AddChild(Small("Outline: " + _s.Framer.NameOf(d, target) + (target == look ? "" : ", look 1's")));
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", Ui.Gap.Hair);
        grid.AddThemeConstantOverride("v_separation", Ui.Gap.Hair);
        foreach (var name in PieceFramer.Outlines.Where(o => o != PieceFramer.AlongThePicture))
        {
            var n = name;
            var b = Ui.Btn(name, () => _ = _s.Framer.SetOutline(d, target, n));
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            b.ClipText = true;
            grid.AddChild(b);
        }
        _body.AddChild(grid);
        _body.AddChild(Wide(Ui.Btn(PieceFramer.AlongThePicture, () => _ = _s.Framer.SetOutline(d, target, PieceFramer.AlongThePicture), "Cut along the picture's own see-through edge")));
        _body.AddChild(Wide(Ui.Btn("Trace round the picture", () => _ = TraceOutline(), "Corners along the edge of whatever is drawn on the face, to drag where you want them")));
        _body.AddChild(Small(Canvas.Doc.Outline is { } ol ? $"{ol.Count / 2} corners · the piece is cut along them" : "Click on the face to draw your own, corner by corner"));
    }

    async Task TraceOutline()
    {
        var img = await Canvas.R.Flatten(cut: false);
        var pts = ArtGeom.TraceAlpha(img);
        if (pts.Count < 3) { _s.Flash("Nothing on the face to trace round: everything is see-through, or nothing is."); return; }
        Canvas.SetOutline(pts);
        Canvas.Commit("Outline traced");
        Rebuild();
    }
}

internal sealed class ColourChooser
{
    readonly PieceStudio _s;
    readonly ToolPanel _panel;
    bool _editInk2;

    public ColourChooser(PieceStudio s, ToolPanel panel) { _s = s; _panel = panel; }

    StudioCanvas Canvas => _s.Canvas;
    StudioPrefs Prefs => _s.Prefs;

    static readonly (string Hex, string Name)[] Palette =
    {
        ("#1a1a1aff", "pencil black"), ("#c0392bff", "red"), ("#2a6ebbff", "blue"), ("#2e8b45ff", "green"), ("#d9a21bff", "yellow"), ("#f2f2f2ff", "chalk"),
        ("#1c2f5aff", "Prussian blue"), ("#2b4a9bff", "French blue"), ("#eae6d6ff", "Austrian white"), ("#b22222ff", "British red"), ("#2f4f2fff", "Russian green"), ("#6b4e2eff", "brown"),
        ("#8a8f98ff", "grey"), ("#c9a227ff", "gold"), ("#d9cfb5ff", "parchment"), ("#7a1f1fff", "crimson"), ("#4f6d7aff", "slate"), ("#ffffffff", "white"),
    };

    public void Section(bool second)
    {
        var body = _panel.Body;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        Button Swatch(Color c, bool which, string tip)
        {
            var b = Ui.Swatch(c, new Vector2(40, 26), tip + " — click for every colour");
            b.Pressed += () => { _editInk2 = which; OpenPicker(b); };
            return b;
        }
        row.AddChild(Swatch(Canvas.Ink, false, "The colour"));
        if (second)
        {
            row.AddChild(Swatch(Canvas.Ink2, true, "The fill"));
            var swap = new Button { Text = "⇄", Flat = true, FocusMode = Control.FocusModeEnum.None, TooltipText = "Swap them   (X)" };
            swap.Pressed += () => { (Canvas.Ink, Canvas.Ink2) = (Canvas.Ink2, Canvas.Ink); SyncInk(); };
            row.AddChild(swap);
        }
        row.AddChild(ToolPanel.Small(Hex.Of(Canvas.Ink)[..7].ToUpperInvariant()));
        body.AddChild(row);
        var grid = new GridContainer { Columns = 6 };
        grid.AddThemeConstantOverride("h_separation", Ui.Gap.Hair);
        grid.AddThemeConstantOverride("v_separation", Ui.Gap.Hair);
        foreach (var (h, name) in Palette) grid.AddChild(Chip(h, name));
        body.AddChild(grid);
        var recent = Prefs.Recent.Where(r => !Palette.Any(p => p.Hex.Equals(r, StringComparison.OrdinalIgnoreCase))).Take(6).ToList();
        if (recent.Count > 0)
        {
            var rg = new GridContainer { Columns = 6 };
            rg.AddThemeConstantOverride("h_separation", Ui.Gap.Hair);
            foreach (var h in recent) rg.AddChild(Chip(h, "used lately " + h[..7]));
            body.AddChild(rg);
        }
    }

    void OpenPicker(Control from)
    {
        var pop = new PopupPanel();
        var picker = new ColorPicker
        {
            Color = _editInk2 ? Canvas.Ink2 : Canvas.Ink, EditAlpha = true, PickerShape = ColorPicker.PickerShapeType.HsvWheel,
            ColorMode = ColorPicker.ColorModeType.Hsv, PresetsVisible = false, SamplerVisible = true, ColorModesVisible = true,
            SlidersVisible = true, HexVisible = true, CanAddSwatches = false,
        };
        picker.ColorChanged += c =>
        {
            if (_editInk2) Canvas.Ink2 = c;
            else Canvas.Ink = c;
            Prefs.Ink = Hex.Of(Canvas.Ink);
            Prefs.Ink2 = Hex.Of(Canvas.Ink2);
            _s.Model?.InkChanged(Canvas.Ink);
            ApplyInkToText(c);
        };
        pop.AddChild(picker);
        pop.PopupHide += () =>
        {
            pop.QueueFree();
            Prefs.Used(_editInk2 ? Canvas.Ink2 : Canvas.Ink);
            _panel.Rebuild();
        };
        _s.Root.AddChild(pop);
        var r = from.GetGlobalRect();
        pop.Popup(new Rect2I((Vector2I)(r.Position + new Vector2(r.Size.X + 8, -10)).Round(), Vector2I.Zero));
    }

    Button Chip(string hex, string tip)
    {
        var col = Hex.To(hex, Colors.Black);
        var b = Ui.Swatch(col, new Vector2(30, 18), tip + "\nClick: the colour · right-click: the fill");
        b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        b.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true } mb) return;
            if (mb.ButtonIndex == MouseButton.Left) Canvas.Ink = col;
            else if (mb.ButtonIndex == MouseButton.Right) Canvas.Ink2 = col;
            else return;
            ApplyInkToText(col);
            SyncInk();
            b.AcceptEvent();
        };
        return b;
    }

    public void SyncInk()
    {
        Prefs.Ink = Hex.Of(Canvas.Ink);
        Prefs.Ink2 = Hex.Of(Canvas.Ink2);
        _s.Model?.InkChanged(Canvas.Ink);
        _panel.Rebuild();
    }

    void ApplyInkToText(Color c)
    {
        if (Canvas.Tool != "text" || Canvas.Sel is not { Kind: "text" } l || _editInk2) return;
        l.Color = Hex.Of(c);
        Canvas.Refresh(l);
        Canvas.Commit("Text colour");
    }
}

public partial class ShapeGlyph : Control
{
    public string Kind = "";
    public override void _Ready() { MouseFilter = MouseFilterEnum.Ignore; if (Size == Vector2.Zero) SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); }
    public override void _Draw()
    {
        float k = Mathf.Clamp(Mathf.Min(Size.X, Size.Y) / 30f, 0.5f, 2.5f);
        var mid = Size / 2;
        var c = new Color(0.9f, 0.9f, 0.88f);
        List<Vector2> pts = Kind switch
        {
            "round" => ArtGeom.RoundRect(new Vector2(-7, -5), new Vector2(7, 5), 3),
            "ngon" => ArtGeom.Ngon(Vector2.Zero, new Vector2(0, -7), 6),
            _ => ArtGeom.Star(Vector2.Zero, new Vector2(0, -7.5f), 5, 0.45f),
        };
        var a = pts.Select(p => mid + p * k).ToList();
        a.Add(a[0]);
        DrawPolyline(a.ToArray(), c, 1.6f * k, true);
    }
}
