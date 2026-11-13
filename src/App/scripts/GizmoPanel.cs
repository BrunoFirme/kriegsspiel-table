using System;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class GizmoPanel : HudPart
{
    public const string Key = "gizmo";
    public readonly FloatPanel Panel;
    SpinBox? _x, _z, _turn, _size;
    Label? _measures;

    void ShowMeasures()
    {
        if (_measures == null || !GodotObject.IsInstanceValid(_measures)) return;
        var nodes = T.SelectedNodes.ToList();
        var said = nodes.Count == 1 ? Fmt.Footprint(nodes[0].Footprint.X, nodes[0].Footprint.Y, S.View) : $"{nodes.Count} pieces";
        if (_measures.Text != said) _measures.Text = said;
    }

    const double PlaceStep = 0.01, TurnStep = 0.1, SizeStep = 0.01, Farthest = 100000, FullTurn = 360;
    const float FieldWidth = 104;

    public GizmoPanel(HudContext ctx, IHudShell shell) : base(ctx) => Panel = shell.MakePanel(Key, "Turn / resize");

    public void Rebuild()
    {
        if (T == null || !Panel.Visible) return;
        foreach (var c in Panel.Body.GetChildren())
        {
            Panel.Body.RemoveChild(c);
            c.QueueFree();
        }
        _x = _z = _turn = _size = null;
        _measures = null;
        if (T.SelectionPose() is { } now)
        {
            Panel.Body.AddChild(Numbers(now));
            _measures = Hint("");
            Panel.Body.AddChild(_measures);
            ShowMeasures();
        }
        else Panel.Body.AddChild(Hint(T.SelectedNodes.Any() ? "Locked" : "No piece selected"));
        Panel.Fit();
    }

    GridContainer Numbers((Vec2 Centre, double Rot, double Scale) now)
    {
        var form = Ui.Form(Ui.PanelLabels);
        _x = Field(form, "X", now.Centre.X, -Farthest, Farthest, PlaceStep, v => T.MoveSelectionTo(new Vec2(v, _z!.Value)));
        _x.Suffix = "u";
        _z = Field(form, "Y", now.Centre.Z, -Farthest, Farthest, PlaceStep, v => T.MoveSelectionTo(new Vec2(_x.Value, v)));
        _z.Suffix = "u";
        _turn = Field(form, "Turn", Table.Norm360(now.Rot), 0, FullTurn, TurnStep, v => T.SetSelectionTransform(v, null));
        _turn.Suffix = "°";
        _size = Field(form, "Size", now.Scale, Table.MinScale, Table.MaxScale, SizeStep, v => T.SetSelectionTransform(null, v));
        _size.Prefix = "×";
        Ui.AlignForm(form);
        return form;
    }

    static SpinBox Field(GridContainer form, string name, double value, double least, double most, double step, Action<double> typed)
    {
        form.AddChild(new Label { Text = name });
        var box = new SpinBox { MinValue = least, MaxValue = most, Step = step, Value = value, CustomMinimumSize = new Vector2(FieldWidth, 0) };
        box.ValueChanged += v => typed(v);
        box.GetLineEdit().TextSubmitted += _ => box.GetLineEdit().ReleaseFocus();
        form.AddChild(box);
        return box;
    }

    public void Sync()
    {
        if (T == null || !Panel.Visible) return;
        var now = T.SelectionPose();
        if (now == null != (_x == null))
        {
            Rebuild();
            return;
        }
        if (now is not { } pose) return;
        Show(_x, pose.Centre.X);
        Show(_z, pose.Centre.Z);
        Show(_turn, Table.Norm360(pose.Rot));
        Show(_size, pose.Scale);
        ShowMeasures();
    }

    static void Show(SpinBox? box, double value)
    {
        if (box == null || !GodotObject.IsInstanceValid(box) || box.GetLineEdit().HasFocus()) return;
        if (Math.Abs(box.Value - value) >= box.Step / 2) box.SetValueNoSignal(value);
    }
}
