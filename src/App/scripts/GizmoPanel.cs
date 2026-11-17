using System;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class GizmoPanel : HudPart
{
    public const string Key = "gizmo";
    public readonly FloatPanel Panel;
    SpinBox? _x, _z, _lift, _turn, _size, _wide, _deep, _tall;
    Label? _measured;
    string? _sheetShown, _nameShown;
    bool _syncing;

    const double PlaceStep = 0.01, TurnStep = 0.1, SizeStep = 0.01, LiftStep = 0.01, Farthest = 100000, FullTurn = 360;
    const float FieldWidth = 104;

    public GizmoPanel(HudContext ctx, IHudShell shell) : base(ctx) => Panel = shell.MakePanel(Key, "Turn / resize");

    SheetDef? SheetPicked() => T.SelectedNodes.Any() || T.SelectedSheet is not { } id ? null : S.View.Sheets.FirstOrDefault(s => s.Id == id);

    Stroke? NamePicked() => T.SelectedNodes.Any() || T.SelectedSheet != null ? null : T.NamePick.Picked;

    public void Rebuild()
    {
        if (T == null || !Panel.Visible) return;
        foreach (var c in Panel.Body.GetChildren())
        {
            Panel.Body.RemoveChild(c);
            c.QueueFree();
        }
        _x = _z = _lift = _turn = _size = _wide = _deep = _tall = null;
        _measured = null;
        _sheetShown = null;
        _nameShown = null;
        if (T.SelectionPose() is { } now)
        {
            Panel.Body.AddChild(Numbers(now));
            AddMeasured("How big the piece is in the scenario's measure");
        }
        else if (SheetPicked() is { } sheet) ShowSheet(sheet);
        else if (NamePicked() is { } name) ShowName(name);
        else Panel.Body.AddChild(Hint(T.SelectedNodes.Any() ? "Outside your zone" : "Nothing selected"));
        Panel.Fit();
    }

    void AddMeasured(string tip)
    {
        _measured = Hint("");
        _measured.TooltipText = tip;
        _measured.MouseFilter = Control.MouseFilterEnum.Pass;
        Panel.Body.AddChild(_measured);
        ShowMeasured();
    }

    void ShowSheet(SheetDef sheet)
    {
        _sheetShown = sheet.Id;
        Panel.Body.AddChild(Hint(sheet.Name.Length > 0 ? sheet.Name : sheet.Id));
        var form = Ui.Form(Ui.PanelLabels);
        _x = Field(form, "X", sheet.Pos.X, -Farthest, Farthest, PlaceStep, v => MoveSheet(s => (new Vec2(v, s.Pos.Z), s.RotDeg, null)));
        _x.Suffix = "u";
        _x.TooltipText = "Where the sheet's middle lies across the table, in table units";
        _z = Field(form, "Y", sheet.Pos.Z, -Farthest, Farthest, PlaceStep, v => MoveSheet(s => (new Vec2(s.Pos.X, v), s.RotDeg, null)));
        _z.Suffix = "u";
        _z.TooltipText = "Where the sheet's middle lies up the table, in table units";
        _turn = Field(form, "Turn", Table.Norm360(sheet.RotDeg), 0, FullTurn, TurnStep, v => MoveSheet(s => (s.Pos, v, null)));
        _turn.Suffix = "°";
        _turn.TooltipText = "Which way the sheet is turned, in degrees clockwise";
        _wide = Field(form, "Size X", sheet.Size.W, SheetLayer.LeastSide, Farthest, PlaceStep, v => MoveSheet(s => (s.Pos, s.RotDeg, Sized(s, v, null))));
        _wide.Suffix = "u";
        _wide.TooltipText = "How far the sheet lies across, side to side, in table units";
        _deep = Field(form, "Size Y", sheet.Size.H, SheetLayer.LeastSide, Farthest, PlaceStep, v => MoveSheet(s => (s.Pos, s.RotDeg, Sized(s, null, v))));
        _deep.Suffix = "u";
        _deep.TooltipText = "How far the sheet lies top to bottom, in table units";
        Ui.AlignForm(form);
        Panel.Body.AddChild(form);
        var keep = new CheckBox { Text = "Keep the proportions", ButtonPressed = Cfg.SheetKeepsShape, FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Size X and Size Y change together, so the map is not stretched" };
        keep.Toggled += on => { Cfg.SheetKeepsShape = on; Cfg.Save(); };
        Panel.Body.AddChild(Ui.UnderControls(keep, Ui.PanelLabels));
        var carry = new CheckBox { Text = "With its pieces", ButtonPressed = Cfg.SheetCarries, FocusMode = Control.FocusModeEnum.None,
            TooltipText = "The pieces standing on the sheet move, turn and spread with it, however it is moved" };
        carry.Toggled += on => { Cfg.SheetCarries = on; Cfg.Save(); };
        Panel.Body.AddChild(Ui.UnderControls(carry, Ui.PanelLabels));
        AddMeasured("How big the sheet is in the scenario's measure");
    }

    void ShowName(Stroke name)
    {
        _nameShown = name.Id;
        var names = T.NamePick;
        Panel.Body.AddChild(Hint(name.Text ?? ""));
        var form = Ui.Form(Ui.PanelLabels);
        var centre = names.Box?.Centre ?? name.Points[0];
        _x = Field(form, "X", centre.X, -Farthest, Farthest, PlaceStep, v => names.MoveTo(new Vec2(v, _z!.Value)));
        _x.Suffix = "u";
        _x.TooltipText = "Where the name's middle lies across the table, in table units";
        _z = Field(form, "Y", centre.Z, -Farthest, Farthest, PlaceStep, v => names.MoveTo(new Vec2(_x.Value, v)));
        _z.Suffix = "u";
        _z.TooltipText = "Where the name's middle lies up the table, in table units";
        _turn = Field(form, "Turn", Table.Norm360(names.TurnOf(name)), 0, FullTurn, TurnStep, names.TurnTo);
        _turn.Suffix = "°";
        _turn.TooltipText = "Which way the name runs, in degrees clockwise";
        _size = Field(form, "Size", names.LetterOf(name), Stroke.LeastLetter, Stroke.MostLetter, Stroke.LeastLetter, names.SizeTo);
        _size.Suffix = "u";
        _size.TooltipText = "How tall the letters stand, in table units";
        Ui.AlignForm(form);
        Panel.Body.AddChild(form);
    }

    void SyncName(Stroke name)
    {
        var names = T.NamePick;
        if (names.Box is { } box)
        {
            Show(_x, box.Centre.X);
            Show(_z, box.Centre.Z);
        }
        Show(_turn, Table.Norm360(names.TurnOf(name)));
        Show(_size, names.LetterOf(name));
    }

    void MoveSheet(Func<SheetDef, (Vec2 Pos, double Rot, Size2? Size)> change)
    {
        if (SheetPicked() is not { } sheet) return;
        var to = change(sheet);
        T.Sheets.Move(sheet, to.Pos, to.Rot, to.Size);
    }

    Size2 Sized(SheetDef sheet, double? wide, double? deep)
    {
        if (!Cfg.SheetKeepsShape) return new Size2(wide ?? sheet.Size.W, deep ?? sheet.Size.H);
        double shape = sheet.Size.H / Math.Max(1e-9, sheet.Size.W);
        return wide is { } w ? new Size2(w, Math.Max(SheetLayer.LeastSide, w * shape)) : new Size2(Math.Max(SheetLayer.LeastSide, deep!.Value / Math.Max(1e-9, shape)), deep.Value);
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
        if (T.SelectedNodes.FirstOrDefault(PieceLift.Can) is { } flyer)
        {
            _lift = Field(form, "Z", flyer.Piece.Aloft, 0, PieceInstance.MostAltitude, LiftStep, T.LiftSelectionTo);
            _lift.Suffix = "u";
            _lift.TooltipText = "How high it flies over the ground under it, in table units · 0 stands it on the ground";
        }
        _turn = Field(form, "Turn", Table.Norm360(now.Rot), 0, FullTurn, TurnStep, v => T.SetSelectionTransform(v, null));
        _turn.Suffix = "°";
        _turn.TooltipText = "Which way each piece faces, in degrees clockwise";
        _size = Field(form, "Size", now.Scale, Table.MinScale, Table.MaxScale, Table.MinScale, v => T.SetSelectionTransform(null, v));
        (_size.CustomArrowStep, _size.Prefix) = (SizeStep, "×");
        _size.TooltipText = "Each piece's size against the size its kind has, every side together";
        if (T.FirstMovable is { } first)
        {
            _wide = Field(form, "Size X", first.Footprint.X, 0, Farthest, PlaceStep, v => T.StretchSelectionTo(v, null));
            _wide.Suffix = "u";
            _wide.TooltipText = "How far each piece lies across, side to side, in table units · nothing else changes";
            _deep = Field(form, "Size Y", first.Footprint.Y, 0, Farthest, PlaceStep, v => T.StretchSelectionTo(null, v));
            _deep.Suffix = "u";
            _deep.TooltipText = "How far each piece lies front to back, in table units · nothing else changes";
            if (PieceStretch.HasHeight(first))
            {
                _tall = Field(form, "Size Z", first.Thickness, 0, Farthest, PlaceStep, v => T.StretchSelectionTo(null, null, v));
                _tall.Suffix = "u";
                _tall.TooltipText = "How tall each piece stands, in table units · nothing else changes";
            }
            BoundSides(first);
        }
        Ui.AlignForm(form);
        return form;
    }

    SpinBox Field(GridContainer form, string name, double value, double least, double most, double step, Action<double> typed)
    {
        form.AddChild(new Label { Text = name });
        var box = new SpinBox { MinValue = least, MaxValue = most, Step = step, Value = value, CustomMinimumSize = Ui.Px(FieldWidth, 0) };
        box.ValueChanged += v => { if (!_syncing) typed(v); };
        box.GetLineEdit().TextSubmitted += _ => box.GetLineEdit().ReleaseFocus();
        form.AddChild(box);
        return box;
    }

    void BoundSides(PieceNode first)
    {
        Bound(_wide, first.Footprint.X, first.Piece.WideBy);
        Bound(_deep, first.Footprint.Y, first.Piece.DeepBy);
        Bound(_tall, first.Thickness, first.Piece.TallBy);
    }

    void Bound(SpinBox? box, double now, double by)
    {
        if (box == null || !GodotObject.IsInstanceValid(box) || by <= 0) return;
        var (least, most) = PendingPoses.StretchReach(now, by);
        _syncing = true;
        if (Math.Abs(box.MinValue - least) > 1e-9) box.MinValue = least;
        if (Math.Abs(box.MaxValue - most) > 1e-9) box.MaxValue = most;
        _syncing = false;
    }

    void ShowMeasured()
    {
        if (_measured == null || !GodotObject.IsInstanceValid(_measured)) return;
        var nodes = T.SelectedNodes.ToList();
        var said = nodes.Count > 1 ? $"{nodes.Count} pieces" : nodes.Count == 1 ? Fmt.Measured(nodes[0].Footprint.X, nodes[0].Footprint.Y, S.View) ?? ""
            : SheetPicked() is { } sheet ? Fmt.Measured(sheet.Size.W, sheet.Size.H, S.View) ?? "" : "";
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
        var sheet = now == null ? SheetPicked() : null;
        var name = now == null && sheet == null ? NamePicked() : null;
        if ((now == null && name == null) != (_size == null) || sheet?.Id != _sheetShown || name?.Id != _nameShown)
        {
            Rebuild();
            return;
        }
        if (sheet != null) SyncSheet(sheet);
        if (name != null) SyncName(name);
        if (now is not { } pose) return;
        Show(_x, pose.Centre.X);
        Show(_z, pose.Centre.Z);
        if (T.SelectedNodes.FirstOrDefault(PieceLift.Can) is { } flyer) Show(_lift, flyer.Piece.Aloft);
        Show(_turn, Table.Norm360(pose.Rot));
        Show(_size, pose.Scale);
        if (T.FirstMovable is { } first)
        {
            BoundSides(first);
            Show(_wide, first.Footprint.X);
            Show(_deep, first.Footprint.Y);
            Show(_tall, first.Thickness);
        }
        ShowMeasured();
    }

    void SyncSheet(SheetDef sheet)
    {
        Show(_x, sheet.Pos.X);
        Show(_z, sheet.Pos.Z);
        Show(_turn, Table.Norm360(sheet.RotDeg));
        Show(_wide, sheet.Size.W);
        Show(_deep, sheet.Size.H);
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
