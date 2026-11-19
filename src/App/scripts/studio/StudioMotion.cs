using System;
using System.Linq;
using Godot;
using KTable.Core.Model;
using Choice = KTable.App.MotionPreview.Choice;

namespace KTable.App;

internal sealed class MotionTab
{
    public const string Name = "Motion";
    public const string Hint = "The body moves on the spot as picked in Motion, over a floor that slides under its steps · Take control walks it with the game's keys";
    public const string DriveHint = "W S walk · A D turn · Q E strafe · Shift run · Space jump · F fly · X sneak · C march · right-drag looks · wheel zooms · Esc lets go";

    static readonly (int Ahead, int Aside, string Tip)[] Ways =
    [
        (1, -1, "Ahead and left"), (1, 0, "Ahead   (W)"), (1, 1, "Ahead and right"),
        (0, -1, "Left   (Q)"), (0, 0, "Still"), (0, 1, "Right   (E)"),
        (-1, -1, "Back and left"), (-1, 0, "Back   (S)"), (-1, 1, "Back and right"),
    ];

    readonly PieceStudio _s;
    VBoxContainer _body = null!;

    public MotionTab(PieceStudio s) => _s = s;

    MotionPreview Preview => _s.T.Preview;

    public void Build(VBoxContainer page)
    {
        _body = page;
        Fill();
    }

    void Fill()
    {
        PieceStudio.Clear(_body);
        var d = _s.Sel!;
        var now = Preview.Now;
        bool rigged = d.Rig != null;
        var g = Ui.Form(Ui.PanelLabels);
        _body.AddChild(g);
        if (rigged) WalkRow(g, d);
        if (_s.Driver.Driving)
        {
            Ui.AlignForm(g);
            _body.AddChild(Ui.UnderControls(Ui.Btn("Release control", () => _s.Driver.Drive(false), "Esc"), Ui.PanelLabels));
            return;
        }
        g.AddChild(new Label { Text = "Way" });
        g.AddChild(Ui.IconPad(Ways.Select(w => (w.Ahead == 0 && w.Aside == 0 ? "still" : ToolIcon.WayPrefix + Degrees(w.Ahead, w.Aside), now.Ahead == w.Ahead && now.Aside == w.Aside, w.Tip,
            (Action)(() => Set(now with { Ahead = w.Ahead, Aside = w.Aside })))).ToList(), 3));
        Row(g, "Pace", [("Walk", !now.Run, "", () => Set(now with { Run = false })),
            ("Run", now.Run, now.Stance == RigMotion.March ? "The double quick   (Shift)" : "Shift", () => Set(now with { Run = true }))]);
        if (rigged)
            Row(g, "Stance", [("Normal", now.Stance == null, "", () => Set(now with { Stance = null })),
                ("Sneak", now.Stance == RigMotion.Sneak, "X", () => Set(now with { Stance = RigMotion.Sneak })),
                ("March", now.Stance == RigMotion.March, "C", () => Set(now with { Stance = RigMotion.March }))]);
        Row(g, "Turn", [("Off", now.Turn == 0, "", () => Set(now with { Turn = 0 })), ("Left", now.Turn > 0, "A", () => Set(now with { Turn = 1 })),
            ("Right", now.Turn < 0, "D", () => Set(now with { Turn = -1 }))]);
        Row(g, "Air", [("Off", now.Air == MotionPreview.Off, "", () => Set(now with { Air = MotionPreview.Off })),
            ("Jump", now.Air == MotionPreview.Jumps, "Space", () => Set(now with { Air = MotionPreview.Jumps })),
            ("Fly", now.Air == MotionPreview.Flies, "F · Run boosts", () => Set(now with { Air = MotionPreview.Flies }))]);
        ReliefPanel.Row(g, _s.Cfg, "Speed", 0, 1, 0.05, now.Speed, v => $"{v:0.##}×", v => Preview.Now = Preview.Now with { Speed = (float)v },
            "Slower to see each step");
        Ui.AlignForm(g);
        _body.AddChild(Ui.UnderControls(Ui.Btn("Take control", () => _s.Driver.Drive(true), "Walk it with the game's keys"), Ui.PanelLabels));
    }

    static int Degrees(int ahead, int aside) => (int)Mathf.PosMod(Mathf.RadToDeg(Mathf.Atan2(aside, ahead)), 360);

    void Set(Choice c)
    {
        Preview.Now = c;
        Fill();
        _s.RefreshHint();
    }

    static void Row(GridContainer g, string label, (string Text, bool Lit, string Tip, Action Open)[] items)
    {
        g.AddChild(new Label { Text = label });
        var seg = Ui.Segments(items);
        seg.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        g.AddChild(seg);
    }

    void WalkRow(GridContainer form, PieceDef d)
    {
        form.AddChild(new Label { Text = "Walk" });
        var walk = new OptionButton { FitToLongestItem = false, TooltipText = "Its own animations (Natural when the model has none), or one of the built-in walks" };
        walk.AddItem("Its own");
        foreach (var (_, name) in RigMotion.Gaits) walk.AddItem(name);
        walk.Selected = d.Gait == null ? 0 : Math.Max(0, RigMotion.Gaits.ToList().FindIndex(g => g.Id == d.Gait)) + 1;
        walk.ItemSelected += i => _s.Apply(x => x.Gait = i == 0 ? null : RigMotion.Gaits[(int)i - 1].Id, what: "the walk");
        form.AddChild(walk);
    }
}

internal sealed class BodyDriver(PieceStudio s)
{
    bool _driving, _took;

    Table T => s.T;

    string? Shown => s.View == "table" && MotionPreview.Moves(s.Sel) ? s.Paper.Instance(s.Sel)?.Id : null;

    public bool Driving => s.View == "table" && T.Walk.InControl && T.Walk.BodyPiece is { } body && body == s.Paper.Instance(s.Sel)?.Id;

    public string Hint => Driving ? MotionTab.DriveHint : MotionTab.Hint;

    public void Drive(bool on)
    {
        if (!on)
        {
            if (Driving) T.Walk.LeaveControl();
            return;
        }
        if (Shown is not { } id || T.Pieces.NodeOf(Layers.Umpire, id) is not { } node || s.Markers.Active) return;
        s.ReleaseTyping();
        T.Preview.Follow(null);
        T.Walk.TakeControl(id, node.TablePosition());
        T.Walk.SizeTo(node.Thickness);
        _took = true;
    }

    public bool LetGoOn(InputEvent e)
    {
        if (!Driving || e is not InputEventKey { Keycode: Key.Escape, Pressed: true, Echo: false }) return false;
        T.Walk.LeaveControl();
        return true;
    }

    public void LetGo()
    {
        if (_took && T.Walk.InControl) T.Walk.LeaveControl();
    }

    public void Follow()
    {
        if (_driving && !Driving && _took && T.Walk.InControl) T.Walk.LeaveControl();
        if (Driving != _driving)
        {
            _driving = Driving;
            if (!_driving) _took = false;
            if (!_driving && s.Paper.Instance(s.Sel) is { } shown)
                T.Rig.Frame(Coords.ToWorld(shown.Pos), (float)Math.Max(MinFrame, Math.Max(s.Sel!.Size.W, s.Sel.Size.H) * FrameSizes));
            s.RebuildInspector();
            s.RefreshHint();
        }
        T.Preview.Follow(s.Markers.Active || T.Walk.Walking ? null : Shown);
    }

    const float MinFrame = 6, FrameSizes = 4;
}
