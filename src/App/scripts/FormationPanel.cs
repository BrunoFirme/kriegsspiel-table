using System;
using System.Linq;
using Godot;
using KTable.Client.Hand;
using KTable.Core.Model;

namespace KTable.App;

sealed class FormationPanel : HudPart
{
    public readonly FloatPanel Panel;
    readonly VBoxContainer _box;

    public FormationPanel(HudContext ctx, IHudShell shell) : base(ctx)
    {
        Panel = shell.MakePanel("formation", "Formations");
        _box = Panel.Body;
    }

    public void Rebuild()
    {
        if (_box == null || T == null || !Panel.Visible) return;
        foreach (var c in _box.GetChildren()) { _box.RemoveChild(c); c.QueueFree(); }
        AddKinds();
        AddSpacing();
        AddDepth();
        Panel.Fit();
    }

    void AddKinds()
    {
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 3);
        grid.AddThemeConstantOverride("v_separation", 3);
        var current = T.Forms.Kind;
        foreach (var kind in Formations.All)
        {
            var key = Cfg.Keys.Label("form:" + kind.Id);
            var b = new Button
            {
                ToggleMode = true, ButtonPressed = kind == current, FocusMode = Control.FocusModeEnum.None,
                TooltipText = $"{kind.Name}   ({key})\n{kind.What}", CustomMinimumSize = new Vector2(46, 42), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            b.AddChild(new FormationGlyph { Kind = kind });
            var tag =
                new Label { Text = key, Modulate = Ui.Muted, MouseFilter = Control.MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom };
            tag.AddThemeFontSizeOverride("font_size", 10);
            b.AddChild(tag);
            tag.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            tag.OffsetRight = -3;
            var k = kind;
            b.Pressed += () => T.Forms.Form(k);
            grid.AddChild(b);
        }
        _box.AddChild(grid);
    }

    HBoxContainer Row(string label, string tip)
    {
        var row = new HBoxContainer { TooltipText = tip };
        row.AddThemeConstantOverride("separation", 4);
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(62, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        _box.AddChild(row);
        return row;
    }

    const double MostSpacingPercent = 300, SpacingStep = 5;

    void AddSpacing()
    {
        var row = Row("Spacing", "The room between pieces, as a share of a piece · Ctrl + wheel on the table");
        var s =
            new HSlider { MinValue = 0, MaxValue = MostSpacingPercent, Step = SpacingStep, Value = T.Forms.Gap * 100, CustomMinimumSize = new Vector2(90, 0),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        var shown = Ui.Readout($"{T.Forms.Gap * 100:0}%", 40);
        s.ValueChanged += v => shown.Text = $"{v:0}%";
        s.DragEnded += changed => { if (changed) T.Forms.SetGap(s.Value / 100); };
        row.AddChild(s);
        row.AddChild(shown);
    }

    void AddDepth()
    {
        var kind = T.Forms.Kind;
        if (kind.Depth == FormationDepth.None) return;
        bool abreast = kind.Depth == FormationDepth.Abreast;
        var row = Row(abreast ? "Abreast" : "Ranks", (abreast ? "How many side by side" : "How many ranks deep") + " · Shift + wheel on the table");
        var spin = new SpinBox { MinValue = 1, MaxValue = Formations.MostRanks, Step = 1, Value = T.Forms.Depth(Math.Max(2, T.Forms.Count)), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        spin.ValueChanged += v => { T.Forms.SetDepth((int)v); spin.GetLineEdit().ReleaseFocus(); };
        row.AddChild(spin);
    }
}

public partial class FormationGlyph : Control
{
    public FormationKind Kind = null!;

    const float Margin = 5;
    static readonly Pitch Sample = new(1.2, 0.85);

    const int CurvePoints = 24;
    const double CurveLength = 8, CurveSway = 1.1;

    static readonly Vec2[] Curve = Enumerable.Range(0, CurvePoints + 1)
        .Select(i => { double t = i / (double)CurvePoints; return new Vec2(CurveLength * (0.5 - t), CurveSway * Math.Sin(2 * Math.PI * t)); }).ToArray();

    static (int n, int depth) Shown(string id) => id switch
    {
        "line" => (6, 1), "stretch" => (5, 0), "column" => (8, 2), "block" => (9, 0), "square" => (12, 0), "ring" => (10, 0),
        "wedge" => (10, 0), "arrowhead" or "vee" => (7, 0), "diamond" => (9, 0), "chequer" => (8, 2), "crescent" => (7, 1), "open" => (8, 2), _ => (6, 0),
    };

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        if (GetParent() is Control p) p.Resized += QueueRedraw;
    }

    public override void _Draw()
    {
        var (n, depth) = Shown(Kind.Id);
        var slots = Kind.AlongLine ? Formations.Along(Curve, n, Sample.Across).Select(Formations.AsSlot).ToList() : Formations.Slots(Kind, n, Sample, depth);
        const double w = 1, d = 0.6;
        var quads = slots.Select(q =>
        {
            double a = q.Turn * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
            Vector2 At(double x, double z) => new((float)(q.X + x * c - z * s), (float)(q.Z + x * s + z * c));
            return new[] { At(-w / 2, -d / 2), At(w / 2, -d / 2), At(w / 2, d / 2), At(-w / 2, d / 2) };
        }).ToList();
        var all = quads.SelectMany(x => x).ToList();
        float x0 = all.Min(p => p.X), x1 = all.Max(p => p.X), z0 = all.Min(p => p.Y), z1 = all.Max(p => p.Y);
        float room = Mathf.Min((Size.X - 2 * Margin) / Mathf.Max(0.01f, x1 - x0), (Size.Y - 2 * Margin - 6) / Mathf.Max(0.01f, z1 - z0));
        var mid = new Vector2((x0 + x1) / 2, (z0 + z1) / 2);
        var centre = new Vector2(Size.X / 2, (Size.Y - 6) / 2);
        var ink = GetParent() is BaseButton { ButtonPressed: true } ? Ui.AccentInk : new Color(0.9f, 0.9f, 0.88f);
        foreach (var q in quads) DrawColoredPolygon(q.Select(p => centre + (p - mid) * room).ToArray(), ink);
    }
}
