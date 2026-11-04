using Godot;

using KTable.Client;

namespace KTable.App;

sealed class PovPanel : HudPart
{
    public readonly FloatPanel Panel;
    readonly VBoxContainer _box;

    readonly IHudShell Shell;
    public PovPanel(HudContext ctx, IHudShell shell) : base(ctx)
    {
        Shell = shell;
        Panel = shell.MakePanel("sighting", "POV screenshot");
        _box = Panel.Body;
    }

    public void Rebuild()
    {
        foreach (var c in _box.GetChildren()) { _box.RemoveChild(c); c.QueueFree(); }
        if (T.Draft != null) { Build(); Shell.OpenPanel("sighting"); }
        else Shell.ClosePanel("sighting");
    }

    void Build()
    {
        var d = T.Draft!;
        var grid = Ui.Form(76);
        _box.AddChild(grid);
        AddRadius(grid, d);
        AddFormat(grid, d);
        grid.AddChild(new Label { Text = "Note" });
        var note = new LineEdit { Text = d.Note, PlaceholderText = "printed on the stamp, e.g. dust to the south-east" };
        note.TextChanged += v => d.Note = v;
        grid.AddChild(note);
        Ui.AlignForm(grid);
        AddTraced();
        _box.AddChild(Hint($"{d.Included.Count} pieces in the picture"));
        _box.AddChild(Ui.Actions(
            Ui.Primary("Capture", () => _ = Shell.CaptureSighting(), "The picture goes to the clipboard and the capture folder"),
            Btn("Cancel", T.CancelSighting)));
        Panel.Fit();
    }

    void AddRadius(GridContainer grid, SightingDraft d)
    {
        var cal = S.View.Calibration;
        bool scaled = cal.UnitsPerMeter is { } upm && upm > 0;
        grid.AddChild(new Label { Text = "Radius" });
        var rad = new SpinBox
        {
            MinValue = scaled ? 1 : 0.5, MaxValue = scaled ? 200000 : 5000, Step = scaled ? 10 : 0.5,
            Value = cal.FromTableUnits(d.Radius), Suffix = cal.UnitName, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        grid.AddChild(rad);
        grid.AddChild(new Label { Text = "", Visible = scaled });
        var radRead = Ui.Readout(Fmt.Distance(d.Radius, cal), 0);
        radRead.HorizontalAlignment = HorizontalAlignment.Left;
        radRead.Visible = scaled;
        rad.ValueChanged += v =>
        {
            d.Radius = cal.ToTableUnits(v);
            radRead.Text = Fmt.Distance(d.Radius, cal);
            d.Recompute(T);
            T.ShowDraft();
        };
        grid.AddChild(radRead);
    }

    void AddFormat(GridContainer grid, SightingDraft d)
    {
        grid.AddChild(new Label { Text = "Format" });
        var fmt = new HBoxContainer();
        fmt.AddThemeConstantOverride("separation", 2);
        Button? roundB = null, squareB = null;
        void SetFormat(bool square)
        {
            d.Square = square;
            Cfg.SightingSquare = square;
            Cfg.Save();
            roundB!.SetPressedNoSignal(!square);
            squareB!.SetPressedNoSignal(square);
            d.Recompute(T);
            T.ShowDraft();
        }
        roundB =
            new Button { Text = "Round", ToggleMode = true, ButtonPressed = !d.Square, FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TooltipText = "A circle around the piece: what it can see, as far as it can see" };
        squareB =
            new Button { Text = "Square", ToggleMode = true, ButtonPressed = d.Square, FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TooltipText = "A square around the piece, reaching as far at its edge: the shape a screenshot is" };
        roundB.Pressed += () => SetFormat(false);
        squareB.Pressed += () => SetFormat(true);
        fmt.AddChild(roundB);
        fmt.AddChild(squareB);
        grid.AddChild(fmt);
    }

    void AddTraced()
    {
        var traced = new HBoxContainer();
        traced.AddThemeConstantOverride("separation", 4);
        var undoW = Ui.IconBtn("undo", $"Undo the last line traced   ({Cfg.Keys.Label("undo")})", "Takes back the last thing you traced");
        var redoW = Ui.IconBtn("redo", $"Put it back   ({Cfg.Keys.Label("redo")})", "Traces the last line you took back again");
        undoW.CustomMinimumSize = redoW.CustomMinimumSize = new Vector2(28, 24);
        undoW.Disabled = T.WallsTraced == 0;
        redoW.Disabled = T.WallsTakenBack == 0;
        undoW.Pressed += T.UndoWall;
        redoW.Pressed += T.RedoWall;
        traced.AddChild(undoW);
        traced.AddChild(redoW);
        var tracedCount =
            new Label { Text = T.WallsTraced == 1 ? "1 line in the way" : $"{T.WallsTraced} lines in the way", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, Modulate = Ui.Muted };
        tracedCount.AddThemeFontSizeOverride("font_size", 12);
        traced.AddChild(tracedCount);
        var clearW = Btn("Clear", T.ClearOutline, "Back to the whole picture; the arrow puts it back");
        clearW.Disabled = T.WallsTraced == 0;
        traced.AddChild(clearW);
        _box.AddChild(traced);
    }
}
