using System;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class GizmoPanel : HudPart
{
    public const string Key = "gizmo";
    public readonly FloatPanel Panel;
    SpinBox? _x, _z, _turn, _size, _wide, _deep;
    Label? _measured;
    string? _sheetShown;
    bool _syncing;

    const double PlaceStep = 0.01, TurnStep = 0.1, SizeStep = 0.01, Farthest = 100000, FullTurn = 360;
    const float FieldWidth = 104;
    const string MapPage = "map";

    public GizmoPanel(HudContext ctx, IHudShell shell) : base(ctx) => Panel = shell.MakePanel(Key, "Turn / resize");

    SheetDef? SheetPicked() => T.SelectedNodes.Any() || T.SelectedSheet is not { } id ? null : S.View.Sheets.FirstOrDefault(s => s.Id == id);

    public void Rebuild()
    {
        if (T == null || !Panel.Visible) return;
        foreach (var c in Panel.Body.GetChildren())
        {
            Panel.Body.RemoveChild(c);
            c.QueueFree();
        }
        _x = _z = _turn = _size = _wide = _deep = null;
        _measured = null;
        _sheetShown = null;
        if (T.SelectionPose() is { } now)
        {
            Panel.Body.AddChild(Numbers(now));
            _measured = Hint("");
            _measured.TooltipText = "How big the piece is in the scenario's measure";
            _measured.MouseFilter = Control.MouseFilterEnum.Pass;
            Panel.Body.AddChild(_measured);
            ShowMeasured();
        }
        else if (SheetPicked() is { } sheet) ShowSheet(sheet);
        else Panel.Body.AddChild(Hint(T.SelectedNodes.Any() ? "Locked" : "No piece selected"));
        Panel.Fit();
    }

    void ShowSheet(SheetDef sheet)
    {
        _sheetShown = sheet.Id;
        Panel.Body.AddChild(Hint(sheet.Name.Length > 0 ? sheet.Name : sheet.Id));
        Panel.Body.AddChild(Btn("Its numbers in the Map page", () => { if (HudNode is Hud hud) hud.Drawer.ShowPage(MapPage); },
            "A sheet's place, turn and width are typed in the Map page"));
    }

    GridContainer Numbers((Vec2 Centre, double Rot, double Scale) now)
    {
        var form = Ui.Form(Ui.PanelLabels);
        _x = Field(form, "X", now.Centre.X, -Farthest, Farthest, PlaceStep, v => T.MoveSelectionTo(new Vec2(v, _z!.Value)));
        _x.Suffix = "u";
        _x.TooltipText = "Where the selection stands across the map, in table units · with several pieces, their middle";
        _z = Field(form, "Y", now.Centre.Z, -Farthest, Farthest, PlaceStep, v => T.MoveSelectionTo(new Vec2(_x.Value, v)));
        _z.Suffix = "u";
        _z.TooltipText = "Where the selection stands up the map, in table units · with several pieces, their middle";
        _turn = Field(form, "Turn", Table.Norm360(now.Rot), 0, FullTurn, TurnStep, v => T.SetSelectionTransform(v, null));
        _turn.Suffix = "°";
        _turn.TooltipText = "Which way each piece faces, in degrees clockwise";
        _size = Field(form, "Size", now.Scale, Table.MinScale, Table.MaxScale, SizeStep, v => T.SetSelectionTransform(null, v));
        _size.Prefix = "×";
        _size.TooltipText = "Each piece's size against the size its kind has, width and depth together";
        if (T.FirstMovable is { } first)
        {
            _wide = Field(form, "Width", first.Footprint.X, 0, Farthest, PlaceStep, v => T.StretchSelectionTo(v, null));
            _wide.Suffix = "u";
            _wide.TooltipText = "How wide each piece is, in table units · its depth stays";
            _deep = Field(form, "Depth", first.Footprint.Y, 0, Farthest, PlaceStep, v => T.StretchSelectionTo(null, v));
            _deep.Suffix = "u";
            _deep.TooltipText = "How deep each piece is, front to back, in table units · its width stays";
            Bound(_wide, first.Footprint.X, first.Piece.WideBy);
            Bound(_deep, first.Footprint.Y, first.Piece.DeepBy);
        }
        Ui.AlignForm(form);
        return form;
    }

    SpinBox Field(GridContainer form, string name, double value, double least, double most, double step, Action<double> typed)
    {
        form.AddChild(new Label { Text = name });
        var box = new SpinBox { MinValue = least, MaxValue = most, Step = step, Value = value, CustomMinimumSize = new Vector2(FieldWidth, 0) };
        box.ValueChanged += v => { if (!_syncing) typed(v); };
        box.GetLineEdit().TextSubmitted += _ => box.GetLineEdit().ReleaseFocus();
        form.AddChild(box);
        return box;
    }

    void Bound(SpinBox? box, double now, double by)
    {
        if (box == null || !GodotObject.IsInstanceValid(box) || by <= 0) return;
        double own = now / by, least = own * PieceInstance.LeastStretch, most = own * PieceInstance.MostStretch;
        _syncing = true;
        if (Math.Abs(box.MinValue - least) > 1e-9) box.MinValue = least;
        if (Math.Abs(box.MaxValue - most) > 1e-9) box.MaxValue = most;
        _syncing = false;
    }

    void ShowMeasured()
    {
        if (_measured == null || !GodotObject.IsInstanceValid(_measured)) return;
        var nodes = T.SelectedNodes.ToList();
        var said = nodes.Count > 1 ? $"{nodes.Count} pieces" : nodes.Count == 1 ? Fmt.Measured(nodes[0].Footprint.X, nodes[0].Footprint.Y, S.View) ?? "" : "";
        bool shown = said.Length > 0;
        if (_measured.Text != said) _measured.Text = said;
        if (_measured.Visible == shown) return;
        _measured.Visible = shown;
        Panel.Fit();
    }

    public void Sync()
    {
        if (T == null || !Panel.Visible) return;
        var now = T.SelectionPose();
        if (now == null != (_x == null) || (now == null ? SheetPicked()?.Id : null) != _sheetShown)
        {
            Rebuild();
            return;
        }
        if (now is not { } pose) return;
        Show(_x, pose.Centre.X);
        Show(_z, pose.Centre.Z);
        Show(_turn, Table.Norm360(pose.Rot));
        Show(_size, pose.Scale);
        if (T.FirstMovable is { } first)
        {
            Bound(_wide, first.Footprint.X, first.Piece.WideBy);
            Bound(_deep, first.Footprint.Y, first.Piece.DeepBy);
            Show(_wide, first.Footprint.X);
            Show(_deep, first.Footprint.Y);
        }
        ShowMeasured();
    }

    void Show(SpinBox? box, double value)
    {
        if (box == null || !GodotObject.IsInstanceValid(box) || box.GetLineEdit().HasFocus()) return;
        if (Math.Abs(box.Value - value) < box.Step / 2) return;
        _syncing = true;
        box.Value = value;
        _syncing = false;
    }
}
