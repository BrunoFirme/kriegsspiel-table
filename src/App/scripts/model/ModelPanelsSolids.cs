using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

namespace KTable.App;

internal sealed partial class ModelPanels
{
    string? _measureTo;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly (string join, string name, string tip)[] JoinChoices =
    {
        ("", "Apart", "A shape of its own"),
        (ModelTree.Add, "Add", "Joined to the shapes above into one solid"),
        (ModelTree.Cut, "Cut", "Cuts its shape out of the shapes above"),
        (ModelTree.Overlap, "Overlap", "Keeps only where it overlaps the shapes above"),
    };

    string What(ModelPart p)
    {
        string kind = p.IsGroup ? $"group · {ModelTree.Children(_m.Doc, p).Count()}"
            : p.Kind == "mesh" ? $"model · {_m.Views.GetValueOrDefault(p.Id)?.Data.Tris ?? 0} tris"
            : AddableShapes.FirstOrDefault(x => x.kind == p.Kind).name?.ToLowerInvariant() ?? p.Kind;
        if (p.Join != null && ModelTree.Above(_m.Doc, p) != null) kind += " · " + JoinChoices.First(c => c.join == p.Join).name.ToLowerInvariant();
        return p.Twin ? kind + " · twin" : kind;
    }

    HBoxContainer Ends(ModelPart s)
    {
        var ends = new HBoxContainer();
        var sibs = ModelTree.Children(_m.Doc, ModelTree.ParentOf(_m.Doc, s)).ToList();
        int at = sibs.IndexOf(s);
        var up = Icon("up", "Up the list", () => _m.Edits.Shift(s, -1));
        up.Disabled = at <= 0;
        var down = Icon("down", "Down the list", () => _m.Edits.Shift(s, 1));
        down.Disabled = at < 0 || at >= sibs.Count - 1;
        ends.AddChild(up);
        ends.AddChild(down);
        return ends;
    }

    void GroupRow(GridContainer form, ModelPart s)
    {
        var groups = _m.Doc.Parts.Where(g => g.IsGroup && g != s && !ModelTree.IsInside(_m.Doc, g, s)).ToList();
        if (groups.Count == 0) return;
        form.AddChild(new Label { Text = "Group" });
        var pick = new OptionButton { FocusMode = Control.FocusModeEnum.None, TooltipText = "The group it moves with" };
        pick.AddItem("—", 0);
        for (int i = 0; i < groups.Count; i++) pick.AddItem(groups[i].Name, i + 1);
        pick.Selected = groups.IndexOf(ModelTree.ParentOf(_m.Doc, s)!) + 1;
        pick.ItemSelected += i => _m.Edits.Into(s, i == 0 ? null : groups[(int)i - 1]);
        form.AddChild(pick);
    }

    void GroupDetails(PieceStudio studio, VBoxContainer box, ModelPart g)
    {
        var ends = Ends(g);
        ends.AddChild(Icon("part", "Take its shapes out of the group", () => _m.Edits.Ungroup(g)));
        ends.AddChild(Icon("dup", "Duplicate the group and its shapes", () => _m.Edits.Duplicate(g)));
        ends.AddChild(Icon("trash", "Remove the group and its shapes   (Delete; Ctrl+Z brings them back)", () => _m.RemovePart(g)));
        var head = Inspector.Group(box, "Group", ends);
        var form = Ui.Form(Ui.PanelLabels);
        form.AddChild(new Label { Text = "Name" });
        var rename = new LineEdit { Text = g.Name };
        void Rename()
        {
            var t = rename.Text.Trim();
            if (t.Length == 0 || t == g.Name) return;
            _m.Snapshot();
            g.Name = t;
            _m.Changed(mesh: false);
            Callable.From(studio.RebuildModelPanels).CallDeferred();
        }
        rename.TextSubmitted += _ => Rename();
        rename.FocusExited += Rename;
        form.AddChild(rename);
        GroupRow(form, g);
        Ui.AlignForm(form);
        head.AddChild(form);
        JoinSection(box, g);
        PartPlace(studio, box, g);
    }

    void JoinSection(VBoxContainer box, ModelPart p)
    {
        if (ModelTree.Above(_m.Doc, p) is not { } above) return;
        var g = Inspector.Group(box, "Join");
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        foreach (var (join, name, tip) in JoinChoices)
        {
            bool on = (p.Join ?? "") == join;
            var b = new Button { Text = name, ToggleMode = true, ButtonPressed = on, FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TooltipText = tip + (join.Length > 0 ? $" (above it: {above.Name})" : "") };
            var j = join;
            b.Pressed += () => _m.Edits.SetJoin(p, j.Length == 0 ? null : j);
            row.AddChild(b);
        }
        g.AddChild(row);
        if (p.Join == null) return;
        var form = Ui.Form(Ui.PanelLabels);
        form.AddChild(new Label { Text = "Blend" });
        bool can = ModelEdits.CanBlend(_m.Doc, p);
        var blend = new SpinBox { MinValue = 0, MaxValue = 10000, Step = 0.01, AllowGreater = true, Value = p.Blend, Editable = can,
            TooltipText = can ? "Rounds the join: 0 is a sharp edge" : "Rounds joins between shapes; a model from a file or a group joins with a sharp edge" };
        blend.ValueChanged += v => _m.Edits.SetJoin(p, p.Join, (float)v);
        form.AddChild(blend);
        Ui.AlignForm(form);
        g.AddChild(form);
    }

    void ShapeSection(VBoxContainer box, ModelPart p)
    {
        if (!ShapeMaker.IsSpec(p.Kind) || p.Spec is not { } spec) return;
        var g = Inspector.Group(box, spec.Kind switch { ShapeMaker.Sweep => "Path", ShapeMaker.Extrude => "Outline", _ => "Profile" });
        var edit = new ModelProfileEdit { Spec = spec };
        g.AddChild(edit);
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", Ui.Gap.Tight);
        grid.AddThemeConstantOverride("v_separation", Ui.Gap.Tight);
        g.AddChild(grid);
        void Numbers()
        {
            foreach (var c in grid.GetChildren()) { grid.RemoveChild(c); c.QueueFree(); }
            var sp = edit.Spec;
            int i = edit.Chosen;
            if (i < 0 || i >= sp.Count) return;
            var names = sp.Kind switch { ShapeMaker.Sweep => new[] { "X", "Y", "Z", "R" }, ShapeMaker.Lathe => new[] { "R", "Y" }, _ => new[] { "X", "Y" } };
            grid.AddChild(Ui.RowLabel($"Point {i + 1}", Ui.PanelLabels));
            for (int k = 0; k < names.Length; k++)
            {
                if (k == 2) grid.AddChild(Ui.RowLabel("", Ui.PanelLabels));
                int kk = k;
                var sb = new SpinBox { MinValue = names[k] == "R" ? 0 : -10000, MaxValue = 10000, Step = 0.001, AllowGreater = true, AllowLesser = names[k] != "R", Prefix = names[k],
                    Value = sp.Points[i * sp.Stride + k], SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                    TooltipText = names[k] switch { "R" => sp.Kind == ShapeMaker.Lathe ? "Distance from the upright it turns about" : "The tube's radius here", _ => $"Along {names[k]}" } };
                sb.GetLineEdit().AddThemeConstantOverride("minimum_character_width", 3);
                sb.ValueChanged += v =>
                {
                    var next = edit.Spec.Copy();
                    next.Points[i * next.Stride + kk] = (float)v;
                    _m.Edits.Reshape(p, next, panels: false);
                    edit.Spec = next;
                };
                grid.AddChild(sb);
            }
        }
        edit.Picked = Numbers;
        edit.Changed = next =>
        {
            _m.Edits.Reshape(p, next, panels: false);
            Numbers();
        };
        Numbers();
        if (spec.Kind != ShapeMaker.Extrude) return;
        var form = Ui.Form(Ui.PanelLabels);
        form.AddChild(new Label { Text = "Depth" });
        var depth = new SpinBox { MinValue = 0.001, MaxValue = 10000, Step = 0.01, AllowGreater = true, Value = spec.Depth, TooltipText = "How thick the outline is, front to back" };
        depth.ValueChanged += v =>
        {
            var next = edit.Spec.Copy();
            next.Depth = (float)v;
            _m.Edits.Reshape(p, next, panels: false);
        };
        form.AddChild(depth);
        Ui.AlignForm(form);
        g.AddChild(form);
    }

    List<ModelPart> Others(ModelPart s) => ModelTree.Ordered(_m.Doc).Where(o => o != s && !ModelTree.IsInside(_m.Doc, o, s) && !ModelTree.IsInside(_m.Doc, s, o) && _m.BoundsOf(o) != null).ToList();

    MenuButton PartMenu(string text, string tip, List<ModelPart> parts, Action<ModelPart> chosen)
    {
        var mb = new MenuButton { Text = text + " ▾", Flat = false, FocusMode = Control.FocusModeEnum.None, TooltipText = tip, Disabled = parts.Count == 0,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var pop = mb.GetPopup();
        for (int i = 0; i < parts.Count; i++) pop.AddItem(parts[i].Name, i);
        pop.IdPressed += id => chosen(parts[(int)id]);
        return mb;
    }

    void Arrange(VBoxContainer g, ModelPart s)
    {
        var others = Others(s);
        g.AddChild(Buttons(2,
            PartMenu("Sit on", "Its lowest point on top of another shape, centred on it", others, o => _m.Edits.SitOn(s, o)),
            PartMenu("Centre on", "Its centre on another shape's, across and front to back", others, o => _m.Edits.CentreOn(s, o, "xz")),
            PartMenu("Measure to", "Distance and gaps to another shape, in table units", others, o =>
            {
                _measureTo = o.Id;
                _m.Studio.RebuildModelPanels();
            })));
        if (!s.IsGroup)
        {
            var twin = new CheckBox { Text = "Mirrored twin", ButtonPressed = s.Twin, FocusMode = Control.FocusModeEnum.None, TooltipText = "A mirror copy on the other side of the model's middle, left to right, with the same paint" };
            twin.Toggled += on =>
            {
                _m.Snapshot();
                s.Twin = on;
                _m.RefreshSelection();
                _m.Changed(mesh: true);
                _m.Studio.RebuildModelPanels();
            };
            g.AddChild(twin);
        }
        if (_measureTo == null || _m.Doc.Parts.FirstOrDefault(x => x.Id == _measureTo) is not { } to || to == s || _m.Edits.Measure(s, to) is not var (apart, gaps)) return;
        g.AddChild(Ui.Fact("Centres", string.Format(Inv, "{0:0.###} u to {1}", apart, to.Name), Ui.PanelLabels));
        g.AddChild(Ui.Fact("Gaps", string.Format(Inv, "X {0:0.###} · Y {1:0.###} · Z {2:0.###}", gaps.X, gaps.Y, gaps.Z), Ui.PanelLabels));
    }

    void SnapOptions(VBoxContainer box)
    {
        var form = Ui.Form(Ui.PanelLabels);
        form.AddChild(new Label { Text = "Snap" });
        var snap = new SpinBox { MinValue = 0, MaxValue = 1000, Step = 0.01, AllowGreater = true, Value = _m.P.ModelSnap, TooltipText = "Dragged shapes land on steps of this size; 0 moves freely" };
        snap.ValueChanged += v => _m.P.ModelSnap = (float)v;
        form.AddChild(snap);
        Ui.AlignForm(form);
        box.AddChild(form);
    }

    HBoxContainer SoftRow()
    {
        var row = new HBoxContainer();
        var lab = new Label { Text = "Soft edge", Modulate = Ui.Muted, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        lab.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(lab);
        var s = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = _m.P.Soft, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        var l = Ui.ValueBox(s, v => $"{v * 100:0}%", 48);
        s.ValueChanged += v => _m.P.Soft = (float)v;
        row.AddChild(s);
        row.AddChild(l);
        return row;
    }
}
