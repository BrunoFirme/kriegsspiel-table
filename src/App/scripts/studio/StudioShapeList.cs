using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client.Studio;

namespace KTable.App;

sealed class ShapeList
{
    public const string Title = "Drawn";
    const int MostShown = 6;
    const float RowHeight = 22;
    static readonly Vector2 Chip = new(12, 12), Cross = new(20, RowHeight), Name = new(84, RowHeight), Step = new(30, RowHeight + 2);

    readonly PieceStudio _s;
    public ShapeList(PieceStudio s) => _s = s;

    StudioCanvas Canvas => _s.Canvas;

    public void Build(VBoxContainer body)
    {
        if (Canvas.Sel is not { Visible: true, Locked: false } l) return;
        var shapes = OpPick.Objects(l.Ops);
        if (shapes.Count == 0) return;
        body.AddChild(Ui.Header(Title));
        var rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", Ui.Gap.Hair);
        var picked = Canvas.Picked.At;
        for (int i = shapes.Count - 1; i >= 0; i--) rows.AddChild(Row(l, shapes[i], shapes[i].SequenceEqual(picked)));
        if (shapes.Count > MostShown)
        {
            var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, MostShown * (RowHeight + Ui.Gap.Hair)), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            scroll.AddChild(rows);
            body.AddChild(scroll);
        }
        else body.AddChild(rows);
        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", Ui.Gap.Hair);
        foot.AddChild(StepButton("up", "Bring the picked shape forward", forward: true));
        foot.AddChild(StepButton("down", "Send the picked shape back", forward: false));
        body.AddChild(foot);
        int freehand = OpPick.Freehand(l.Ops);
        if (freehand > 0) body.AddChild(ToolPanel.Small(freehand == 1 ? "1 freehand stroke" : $"{freehand} freehand strokes"));
    }

    HBoxContainer Row(ArtLayer l, List<int> at, bool picked)
    {
        var group = at.Select(i => l.Ops[i]).ToList();
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        var chip = Ui.Swatch(Hex.To(OpPick.Tint(group), Colors.Black), Chip, "");
        chip.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        chip.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(chip);
        var pick = new Button
        {
            Text = OpPick.Named(group), ToggleMode = true, ButtonPressed = picked, ClipText = true, Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = Name, TooltipText = picked ? "Let it go" : "Pick this shape",
        };
        pick.Pressed += () =>
        {
            if (picked) Canvas.Picked.Drop();
            else Canvas.Picked.Take(l, at, loadLook: true);
        };
        row.AddChild(pick);
        var remove = new Button { Text = "✕", Flat = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = Cross, TooltipText = "Remove this shape" };
        remove.AddThemeFontSizeOverride("font_size", 11);
        remove.Pressed += () =>
        {
            Canvas.Picked.Take(l, at, loadLook: false);
            Canvas.Picked.Delete();
        };
        row.AddChild(remove);
        return row;
    }

    Button StepButton(string icon, string tip, bool forward)
    {
        bool can = Canvas.Picked.CanRestack(forward);
        var b = new Button { CustomMinimumSize = Step, TooltipText = tip, FocusMode = Control.FocusModeEnum.None, Disabled = !can, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        b.AddChild(new StudioIcon { Kind = icon });
        b.Pressed += () => { if (Canvas.Picked.Restack(forward)) _s.RebuildToolPanel(); };
        return b;
    }
}
