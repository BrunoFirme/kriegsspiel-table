using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using KTable.Client.Studio;
using KTable.Core.Model;

namespace KTable.App;

internal sealed partial class ModelPanels
{
    readonly ModelStudio _m;

    public ModelPanels(ModelStudio m) => _m = m;

    internal static readonly (string name, string kind)[] AddableShapes = { ("A round base", "base"), ("Box", "box"), ("Cylinder", "cylinder"), ("Cone", "cone"), ("Sphere", "sphere"), ("Hex prism", "hex"), ("Wedge", "wedge"), ("Torus (a ring)", "torus"), ("Capsule", "capsule"), ("Flag (a thin plate)", "flag"),
        ("Turned profile", ShapeMaker.Lathe), ("Tube along a path", ShapeMaker.Sweep), ("Outline with depth", ShapeMaker.Extrude) };

    public void BuildStrip(VBoxContainer box, Dictionary<string, Button> tools)
    {
        foreach (var (key, icon, tip) in new[]
        {
            ("orbit", "orbit", "Look round   (H)\nDrag to turn the model, middle-drag to slide, the wheel to come closer. Right-drag turns it with any tool. A click picks a shape."),
            ("move", "move", "Move shapes   (V)\nDrag an arrow to move the chosen shape along X, Y or Z, or drag a shape along the ground (Shift: up and down). Its numbers are in Layers."),
            ("paint", "brush", "Paint on the model   (B)\n= and − or Ctrl+wheel size the brush. Alt-click takes a colour off the model."),
            ("erase", "erase", "Rub out paint   (E)\nThe model's own picture shows again."),
            ("fill", "fill", "Fill   (G)\nClick the model: the patch of that colour around the click takes the ink, over the seams. Shift-click paints the whole shape."),
            ("pick", "pick", "Colour picker   (I)"),
            ("decal", "image", "Decal   (D)\nA picture pressed onto the model where you hold it, like a transfer: over the seams, onto what is in front. Q and E turn it (Shift by 1°), = and − or Ctrl+wheel size it, Ctrl+V takes the clipboard's picture."),
        })
        {
            if (key == "move" && !_m.CanMove) continue;
            var k = key;
            var b = new Button { CustomMinimumSize = new Vector2(30, 30), FocusMode = Control.FocusModeEnum.None, TooltipText = tip, ToggleMode = true, ButtonPressed = _m.Tool == key };
            b.AddChild(new StudioIcon { Kind = icon });
            b.Pressed += () => _m.SetTool(k);
            box.AddChild(b);
            tools[key] = b;
        }
        box.AddChild(new HSeparator());
        void Btn(string icon, string tip, Action a, bool toggle = false, bool on = false)
        {
            var b = new Button { CustomMinimumSize = new Vector2(30, 30), FocusMode = Control.FocusModeEnum.None, TooltipText = tip, ToggleMode = toggle, ButtonPressed = on };
            b.AddChild(new StudioIcon { Kind = icon });
            b.Pressed += a;
            box.AddChild(b);
        }
        Btn("undo", "Undo   (Ctrl+Z)", _m.Studio.Undo);
        Btn("redo", "Redo   (Ctrl+Y)", _m.Studio.Redo);
        Btn("frame", "Frame the model   (Home)", _m.Frame);
        Btn("grid", "Flat view, no perspective   (5)\n1 front · 3 right · 7 top · Shift for the opposite side", () => _m.ViewKey(Godot.Key.Key5, false), true, _m.Camera.Ortho);
        Btn("play", "Turn slowly on its stand", () => _m.Camera.Spin = !_m.Camera.Spin, true, _m.Camera.Spin);
    }

    public string ToolHint => _m.Tool switch
    {
        "move" => "Drag an arrow to move along X, Y or Z · drag a shape along the ground, Shift up and down · a joined shape moves whole · 1 3 7 front, right, top",
        "paint" => "Drag to paint on the model; the stroke runs round it and over its seams · = and − or Ctrl+wheel size the brush · Alt-click takes a colour",
        "erase" => "Drag to take the paint off; the model's own picture shows again",
        "fill" => "Click to fill the patch of that colour, up to other colours and bends sharper than Stop at bends · Shift-click paints the whole shape",
        "pick" => "Click the model: that colour becomes the ink",
        "decal" => !_m.Decal.Holds ? "Choose a picture in the panel, then click the model to press it on like a transfer" : $"Click to press the picture on · turned {_m.Decal.Turn:0.#}° · Q and E turn it, Shift by 1° · = and − or Ctrl+wheel size it · Ctrl+Z takes it off",
        _ => "Drag to turn the model · middle-drag slides it · the wheel comes closer · click a shape to pick it · Home frames it · F turns it over",
    };

    public void BuildOptions(PieceStudio studio, VBoxContainer box)
    {
        switch (_m.Tool)
        {
            case "paint":
            case "erase":
            {
                box.AddChild(SliderRow("Size", 1, 300, 1, _m.P.Brush, v => $"{v:0} px", v =>
                {
                    _m.P.Brush = v;
                    _m.RedrawOverlay();
                }));
                box.AddChild(SliderRow("Soft edge", 0, 1, 0.05, _m.P.ModelSoft, v => $"{v * 100:0}%", v => _m.P.ModelSoft = v));
                if (_m.Tool == "paint") studio.ModelColour();
                break;
            }
            case "fill":
                box.AddChild(SliderRow("Reach", 0, 1, 0.01, _m.P.ModelFillMatch, v => $"{v * 100:0}%", v => _m.P.ModelFillMatch = v,
                    "How different a colour may be from the one clicked and still be filled"));
                box.AddChild(SliderRow("Stop at bends", 5, 180, 5, _m.P.ModelFillBend, v => v >= 180 ? "never" : $"over {v:0}°", v => _m.P.ModelFillBend = v,
                    "The fill stops where the surface turns more sharply than this, as at a helmet's rim or a belt's edge"));
                studio.ModelColour();
                break;
            case "pick":
                studio.ModelColour();
                break;
            case "decal":
                _m.Decal.Options(studio, box);
                break;
            case "move":
                SnapOptions(box);
                break;
            default:
                box.AddChild(new Label { Text = "Turn the model with the mouse.", Modulate = Ui.Muted });
                break;
        }
    }

    public void BuildParts(PieceStudio studio, VBoxContainer box)
    {
        var def = _m.Def;
        bool blockChosen = _m.Flat && !_m.IsPart(_m.SelPartId ?? "");
        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        if (_m.Flat && def != null) list.AddChild(ShapeRow(blockChosen, null, "Block", studio.Framer.NameOf(def, Math.Max(0, _m.FlatState)).ToLowerInvariant(), () =>
        {
            _m.SelPartId = PieceFaces.Key(Math.Max(0, _m.FlatState), false);
            _m.RefreshSelection();
            studio.RebuildModelPanels();
        }));
        foreach (var p in ModelTree.Ordered(_m.Doc)) list.AddChild(PartRow(studio, p));
        box.AddChild(list);
        box.AddChild(AddMenu(studio));
        if (blockChosen && def != null) BlockDetails(studio, box, def);
        else if (_m.SelPart is { IsGroup: true } g) GroupDetails(studio, box, g);
        else if (_m.SelPart is { } s) PartDetails(studio, box, s);
        if (!_m.Flat) ModelSize(studio, box);
    }

    const int ModelFileId = 100;

    MenuButton AddMenu(PieceStudio studio)
    {
        var add = new MenuButton { Text = "＋  Add a shape or a model ▾", FocusMode = Control.FocusModeEnum.None, Flat = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = _m.Flat ? "Built onto the piece: it stays the same piece, its faces, looks and buttons as they were. A model file can also be dropped on the 3D view." : "A shape to build with, or a model file (also by dropping it on the 3D view)" };
        var pop = add.GetPopup();
        var shapes = AddableShapes;
        for (int i = 0; i < shapes.Length; i++) if (!(_m.Flat && shapes[i].kind == "base")) pop.AddItem(shapes[i].name, i);
        pop.AddSeparator();
        pop.AddItem("A model from a file…   glTF, GLB, FBX, OBJ, STL, PLY, Collada", ModelFileId);
        pop.IdPressed += id =>
        {
            if (id == ModelFileId) { studio.PickModelPart(); return; }
            var (name, kind) = shapes[id];
            _m.AddShape(kind, name);
        };
        return add;
    }

    const int Indent = 14;

    static PanelContainer ShapeRow(bool on, Button? eye, string name, string what, Action choose, int depth = 0)
    {
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        var style = Ui.Box(on ? new Color(1, 1, 1, 0.13f) : new Color(1, 1, 1, 0.03f), 3, 4, 3);
        style.ContentMarginLeft += depth * Indent;
        panel.AddThemeStyleboxOverride("panel", style);
        var row = new HBoxContainer();
        panel.AddChild(row);
        row.AddChild(eye ?? (Control)new Control { CustomMinimumSize = new Vector2(24, 24), MouseFilter = Control.MouseFilterEnum.Ignore });
        row.AddChild(new Label { Text = name, ClipText = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        row.AddChild(new Label { Text = what, Modulate = Ui.Muted, MouseFilter = Control.MouseFilterEnum.Ignore });
        panel.GuiInput += e => { if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) { panel.AcceptEvent(); choose(); } };
        return panel;
    }

    static Button Icon(string kind, string tip, Action press)
    {
        var b = new Button { Flat = true, CustomMinimumSize = new Vector2(26, 24), FocusMode = Control.FocusModeEnum.None, TooltipText = tip };
        b.AddChild(new StudioIcon { Kind = kind });
        b.Pressed += press;
        return b;
    }

    static GridContainer Buttons(int columns, params Button[] buttons)
    {
        var g = new GridContainer { Columns = columns };
        g.AddThemeConstantOverride("h_separation", Ui.Gap.Tight);
        g.AddThemeConstantOverride("v_separation", Ui.Gap.Tight);
        foreach (var b in buttons)
        {
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            b.ClipText = true;
            g.AddChild(b);
        }
        return g;
    }

    const int DrawOwnId = 100, FrameId = 101;

    void BlockDetails(PieceStudio studio, VBoxContainer box, PieceDef def)
    {
        int look = _m.FlatState;
        bool own = look <= 0 || PieceFaces.OwnSize(def, look);
        int target = own ? look : 0;
        var g1 = Inspector.Group(box, look > 0 ? $"Outline and size · look {look + 1}" : "Outline and size");
        if (look > 0)
        {
            var mine = new CheckBox { Text = "Its own size and outline", ButtonPressed = own, TooltipText = "Off: this look has look 1's size and outline" };
            mine.Toggled += on => studio.Apply(x =>
            {
                var st = x.States[look];
                st.Size = on ? x.Size : null;
                st.Shape = on ? x.Shape : null;
            }, what: "a look's own size");
            g1.AddChild(mine);
        }
        var outline = new MenuButton { Text = "Outline: " + studio.Framer.NameOf(def, target) + " ▾", FocusMode = Control.FocusModeEnum.None, Flat = false, Disabled = !own, TooltipText = "The piece's outline. Its pictures stay on its faces." };
        var pop = outline.GetPopup();
        for (int i = 0; i < PieceFramer.Outlines.Length; i++) pop.AddItem(PieceFramer.Outlines[i], i);
        pop.AddSeparator();
        pop.AddItem("Draw your own in 2D…", DrawOwnId);
        pop.AddItem("Frame…", FrameId);
        pop.IdPressed += id =>
        {
            if (id < PieceFramer.Outlines.Length) _ = studio.Framer.SetOutline(def, target, PieceFramer.Outlines[id]);
            else if (id == DrawOwnId) studio.Framer.DrawOwn(def, target);
            else studio.AskFrame(def);
        };
        g1.AddChild(outline);
        var g = Ui.Form(Ui.PanelLabels);
        var size = (look > 0 && own ? def.States[look].Size : null) ?? def.Size;
        var metres = SizeForm.Rows(g, studio.Prefs, size, "Depth", v => studio.Apply(x => { if (look > 0 && own) x.States[look].Size = v; else x.Size = v; }, what: "the size"), own);
        g.AddChild(new Label { Text = "Thickness" });
        var th = new SpinBox { MinValue = 0.01, MaxValue = 5, Step = 0.01, Value = def.Thickness, Suffix = "u" };
        th.ValueChanged += v => studio.Apply(x => x.Thickness = v, what: "the thickness");
        g.AddChild(th);
        g.AddChild(new Label { Text = "Tint" });
        g.AddChild(SizeForm.Tint(def.Tint, "The colour of the piece's sides", t => studio.Apply(x => x.Tint = t, what: "the tint")));
        g.AddChild(new Label());
        var pictures = new CheckBox { Text = "Pictures too", ButtonPressed = def.TintsPictures, TooltipText = "The tint colours the piece's pictures as well: a white picture takes the colour" };
        pictures.Toggled += on => studio.Apply(x => x.TintsPictures = on, what: "the tint of the pictures");
        g.AddChild(pictures);
        Ui.AlignForm(g);
        g1.AddChild(g);
        g1.AddChild(metres);
        var faces = Inspector.Group(box, "Faces");
        foreach (var f in PieceFaces.Of(def).Where(x => !PieceFaces.IsShapeFace(x.Key)))
        {
            bool shown = f.State == _m.FlatState;
            var key = f.Key;
            int state = f.State;
            faces.AddChild(FaceRow(studio, key, f.Label, f.Sha == null ? (f.Back ? "the front's" : "blank") : null, shown, shown && key == _m.SelPartId,
                shown ? "Double-click to paint it laid flat" : "Click to show this look on the block", null, () =>
                {
                    _m.SelPartId = key;
                    if (state != _m.FlatState) _m.ShowState(state);
                    else { _m.RefreshSelection(); studio.RebuildModelPanels(); }
                }));
        }
    }

    static PanelContainer FaceRow(PieceStudio studio, string key, string label, string? note, bool shown, bool on, string tip, Button? picture, Action? choose)
    {
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.PointingHand, TooltipText = tip };
        panel.AddThemeStyleboxOverride("panel", Ui.Box(on ? new Color(1, 1, 1, 0.13f) : new Color(1, 1, 1, 0.03f), 3, 6, 2));
        var row = new HBoxContainer();
        panel.AddChild(row);
        row.AddChild(new Label { Text = label, ClipText = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = shown ? Colors.White : Ui.Muted });
        if (note != null) row.AddChild(new Label { Text = note, Modulate = Ui.Muted, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore });
        if (picture != null) row.AddChild(picture);
        row.AddChild(Ui.Btn("Paint", () => studio.PaintIn2D(key), "Paint this face laid flat in 2D: layers, text, rulers, symmetry"));
        panel.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb) return;
            panel.AcceptEvent();
            if (mb.DoubleClick) studio.PaintIn2D(key);
            else choose?.Invoke();
        };
        return panel;
    }

    Control PartRow(PieceStudio studio, ModelPart p)
    {
        bool on = p.Id == _m.SelPartId;
        var eye = new Button { Flat = true, CustomMinimumSize = new Vector2(24, 24), FocusMode = Control.FocusModeEnum.None, TooltipText = p.Visible ? "Shown: click to hide" : "Hidden: click to show" };
        eye.AddChild(new StudioIcon { Kind = p.Visible ? "eye" : "eye-off", Color = p.Visible ? Ui.Ink : Ui.Muted });
        var part = p;
        eye.Pressed += () =>
        {
            _m.Snapshot();
            part.Visible = !part.Visible;
            _m.RefreshSelection();
            _m.Changed(mesh: true);
            studio.RebuildModelPanels();
        };
        return ShapeRow(on, eye, p.Name, What(p), () =>
        {
            _m.SelPartId = part.Id;
            _m.RefreshSelection();
            studio.RebuildModelPanels();
        }, ModelTree.Depth(_m.Doc, p));
    }

    void PartDetails(PieceStudio studio, VBoxContainer box, ModelPart s)
    {
        var ends = Ends(s);
        ends.AddChild(Icon("merge", "Put it in a new group", () => _m.Edits.Group(s)));
        ends.AddChild(Icon("dup", "Duplicate this shape, paint and all", () => _m.Edits.Duplicate(s)));
        if (_m.Flat || _m.Doc.Parts.Count > 1) ends.AddChild(Icon("trash", "Remove this shape   (Delete; Ctrl+Z brings it back)", () => _m.RemovePart(s)));
        var g = Inspector.Group(box, s.Kind == "mesh" ? "Model" : "Shape", ends);
        var form = Ui.Form(Ui.PanelLabels);
        form.AddChild(new Label { Text = "Name" });
        var rename = new LineEdit { Text = s.Name, TooltipText = "The shape's name" };
        void Rename()
        {
            var t = rename.Text.Trim();
            if (t.Length == 0 || t == s.Name) return;
            _m.Snapshot();
            s.Name = t;
            _m.Changed(mesh: false);
            Callable.From(studio.RebuildModelPanels).CallDeferred();
        }
        rename.TextSubmitted += _ => Rename();
        rename.FocusExited += Rename;
        form.AddChild(rename);
        GroupRow(form, s);
        Ui.AlignForm(form);
        g.AddChild(form);
        JoinSection(box, s);
        ShapeSection(box, s);
        ShapeFaces(studio, box, s);
        PartPlace(studio, box, s);
        PartLook(box, s);
    }

    void ShapeFaces(PieceStudio studio, VBoxContainer box, ModelPart p)
    {
        if (!_m.Views.TryGetValue(p.Id, out var v)) return;
        var faces = _m.ShapeFaces.FacesOf(v);
        if (faces.Count == 0) return;
        var g = Inspector.Group(box, "Faces");
        bool texture = p.Kind == "mesh";
        for (int i = 0; i < faces.Count; i++)
        {
            var key = $"p:{p.Id}:{i}";
            var picture = Ui.Btn(texture ? "Texture…" : "Picture…", () => studio.PickFiles(ImageFiles.Filter, files =>
            {
                if (files.Length == 0) return;
                studio.Say(_m.ShapeFaces.PutPicture(key, files[0]));
                studio.RebuildModelPanels();
            }, texture ? "A texture made for this model" : "A picture over the whole face"),
                texture ? "A texture made for this model, laid on as its maker drew it (PNG, JPEG, WebP, TGA…)" : "A picture stretched over the whole face; Paint places it more finely");
            g.AddChild(FaceRow(studio, key, faces[i].Name, null, true, false, "Double-click to paint it laid flat", picture, null));
        }
    }

    static readonly string[] Axes = { "X", "Y", "Z" };

    void PartPlace(PieceStudio studio, VBoxContainer box, ModelPart s)
    {
        var g = Inspector.Group(box, "Place");
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", Ui.Gap.Tight);
        grid.AddThemeConstantOverride("v_separation", Ui.Gap.Tight);
        void Vec(string label, float[] arr, float step, string suffix, bool scale = false)
        {
            grid.AddChild(Ui.RowLabel(label, Ui.PanelLabels));
            for (int i = 0; i < 3; i++)
            {
                int ii = i;
                var tip = label == "Place" ? $"Its centre along {Axes[i]}" : $"{label} along {Axes[i]}";
                var sb =
                    new SpinBox { MinValue = scale ? 0.001 : -10000, MaxValue = 10000, Step = step, Value = Math.Round(arr[i] / step) * step + 0.0, AllowGreater = true, AllowLesser = !scale,
                        Prefix = Axes[i], Suffix = suffix, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 0), TooltipText = tip };
                sb.GetLineEdit().AddThemeConstantOverride("minimum_character_width", 3);
                sb.ValueChanged += v =>
                {
                    _m.Snapshot();
                    arr[ii] = (float)v;
                    _m.RefreshSelection();
                    _m.Changed(mesh: true);
                };
                grid.AddChild(sb);
            }
        }
        Vec("Place", s.Pos, 0.01f, "");
        Vec("Turn", s.Rot, 1, "°");
        Vec("Scale", s.Scl, 0.01f, "", scale: true);
        g.AddChild(grid);
        void Moved()
        {
            _m.RefreshSelection();
            _m.Changed(mesh: true);
            studio.RebuildModelPanels();
        }
        float floor = _m.Flat ? (float)Math.Clamp(_m.Def?.Thickness ?? 0, 0.02, 2) : 0;
        g.AddChild(Buttons(2,
            Ui.Btn(_m.Flat ? "On the block" : "On the ground", () =>
            {
                if (_m.BoundsOf(s) is not { } a) return;
                _m.Snapshot();
                _m.Edits.Move(s, new Vector3(0, floor - a.Position.Y, 0));
                Moved();
            }, _m.Flat ? "Its lowest point on the block's top" : "Its lowest point on the stand"),
            Ui.Btn("Mirror", () =>
            {
                _m.Snapshot();
                s.Scl[0] = -s.Scl[0];
                Moved();
            }, "Mirror it left to right"),
            Ui.Btn("Turn 90°", () =>
            {
                _m.Snapshot();
                s.Rot[1] = (s.Rot[1] + 90) % 360;
                Moved();
            }, "A quarter turn about the upright"),
            Ui.Btn("Stand up", () =>
            {
                _m.Snapshot();
                s.Rot[0] = (s.Rot[0] - 90) % 360;
                Moved();
            }, "A quarter turn forwards: for models made lying down (STL is usually Z up)")));
        Arrange(g, s);
    }

    void PartLook(VBoxContainer box, ModelPart s)
    {
        var g = Inspector.Group(box, "Colour");
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Row);
        var tint = new ColorPickerButton { Color = Hex.To(s.Tint, Colors.White), CustomMinimumSize = new Vector2(48, 26), EditAlpha = false, TooltipText = "Multiplies the shape's colours" };
        Ui.WhenPicked(tint, c =>
        {
            _m.Snapshot();
            s.Tint = Hex.Of(c);
            _m.RefreshSelection();
            _m.Changed(mesh: false);
        });
        row.AddChild(Ui.RowLabel("Tint", Ui.PanelLabels));
        row.AddChild(tint);
        var clear = Ui.Btn("Clear its paint", () =>
        {
            _m.BeginStroke();
            var v = _m.Views[s.Id];
            Array.Clear(v.Paint);
            v.Repaint();
            _m.EndStroke();
        });
        clear.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(clear);
        g.AddChild(row);
    }

    void ModelSize(PieceStudio studio, VBoxContainer box)
    {
        var g0 = Inspector.Group(box, "On the table");
        var g = Ui.Form(Ui.PanelLabels);
        var span = _m.Views.Count == 0 ? 1 : _m.FootprintWide();
        g.AddChild(new Label { Text = "Width" });
        var row = new HBoxContainer();
        var fp =
            new SpinBox { MinValue = 0.05, MaxValue = 200, Step = 0.05, Value = Math.Round(span * _m.Doc.Scale, 3), Suffix = "u", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TooltipText = "How wide the model stands on the table, in table units (a block in a converted module is 1.4–2)" };
        fp.ValueChanged += v => _m.SetWidth(v);
        row.AddChild(fp);
        row.AddChild(Ui.Btn("Save now", () => _ = _m.Bake(), "It saves itself a moment after each change"));
        g.AddChild(row);
        if (_m.Def is { } def)
        {
            g.AddChild(new Label { Text = "Tint" });
            g.AddChild(SizeForm.Tint(def.Tint, "Multiplies the model's colours", t => studio.Apply(x => x.Tint = t, what: "the tint")));
        }
        Ui.AlignForm(g);
        g0.AddChild(g);
    }
}
