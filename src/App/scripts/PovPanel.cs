using System;
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
        AddView(grid, d);
        AddRadius(grid, d);
        if (d.FirstPerson)
        {
            AddFacing(grid, d);
            AddLens(grid, d);
        }
        else AddFormat(grid, d);
        AddText(grid, d);
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

    void Keep(Action remember, Action apply)
    {
        remember();
        Cfg.Save();
        apply();
    }

    void AddView(GridContainer grid, SightingDraft d) =>
        AddChoice(grid, "View", d.FirstPerson, on => Keep(() => Cfg.SightingFirstPerson = on, () => T.SetSightingFirstPerson(on)),
            ("2D", "The map from straight above, framed on the reach: trace what blocks the view here"),
            ("First person", "From the piece's own eyes: drag to look around, hold a direction, capture what it sees"));

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

    void AddFormat(GridContainer grid, SightingDraft d) =>
        AddChoice(grid, "Format", d.Square, square => Keep(() => Cfg.SightingSquare = square, () => SetSquare(d, square)),
            ("Round", "A circle around the piece: what it can see, as far as it can see"),
            ("Square", "A square around the piece, reaching as far at its edge: the shape a screenshot is"));

    void SetSquare(SightingDraft d, bool square)
    {
        d.Square = square;
        d.Recompute(T);
        T.ShowDraft();
    }

    void AddFacing(GridContainer grid, SightingDraft d)
    {
        grid.AddChild(new Label { Text = "Facing" });
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 2);
        foreach (var cardinal in Sighting.CardinalNames)
        {
            var name = cardinal;
            row.AddChild(Choice(name, $"Look {Compass(name)} and hold it · dragging lets go", d.Facing == name, () => T.FaceSighting(name)));
        }
        grid.AddChild(row);
    }

    static string Compass(string cardinal) => cardinal switch { "N" => "north", "E" => "east", "S" => "south", _ => "west" };

    void AddLens(GridContainer grid, SightingDraft d) =>
        AddChoice(grid, "Lens", d.Wide, on => Keep(() => Cfg.SightingWide = on, () => T.SetSightingWide(on)),
            ("Normal", $"{CameraRig.NormalLens:0}° across: a steady look ahead"),
            ("Wide", $"{CameraRig.WideLens:0}° across: the four directions make the whole circle"));

    void AddText(GridContainer grid, SightingDraft d) =>
        AddChoice(grid, "Text", d.HideText, on => Keep(() => Cfg.SightingHideText = on, () => T.SetSightingHideText(on)),
            ("Shown", "Names, counters, notes and faces as the table shows them"),
            ("Hidden", "No names, counters, notes or studio lettering in the picture; words in imported pictures stay"));

    static void AddChoice(GridContainer grid, string label, bool second, Action<bool> pick, (string text, string tip) first, (string text, string tip) other)
    {
        grid.AddChild(new Label { Text = label });
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 2);
        Button? a = null, b = null;
        void Pick(bool wantOther)
        {
            a!.SetPressedNoSignal(!wantOther);
            b!.SetPressedNoSignal(wantOther);
            pick(wantOther);
        }
        a = Choice(first.text, first.tip, !second, () => Pick(false));
        b = Choice(other.text, other.tip, second, () => Pick(true));
        row.AddChild(a);
        row.AddChild(b);
        grid.AddChild(row);
    }

    static Button Choice(string text, string tip, bool on, Action press)
    {
        var b = new Button { Text = text, ToggleMode = true, ButtonPressed = on, FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = tip };
        b.Pressed += press;
        return b;
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
