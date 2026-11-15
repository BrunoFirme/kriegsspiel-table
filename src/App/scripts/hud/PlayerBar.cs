using System;
using Godot;

namespace KTable.App;

sealed class PlayerBar : HudPart
{
    public readonly PanelContainer Panel;
    readonly IHudShell _shell;
    readonly BodyChoice _bodies;
    readonly MenuButton _body;
    readonly Button _view, _fly, _log;
    readonly HBoxContainer _walking = new();
    readonly Label _held = Ui.Readout("Waiting to be placed", 0);
    readonly AimDot _aim = new() { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
    Vector2 _screen;
    float _gap;

    public PlayerBar(HudContext ctx, IHudShell shell) : base(ctx)
    {
        _shell = shell;
        _bodies = new BodyChoice(ctx);
        Panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        Panel.AddThemeStyleboxOverride("panel", Ui.TopBar());
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        _body = _bodies.Picker();
        if (S.IsPlayer) row.AddChild(_body);
        row.AddChild(_held);
        _walking.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        var take = Ui.IconBtn("take", "Take over", "T · the piece you look at");
        take.Pressed += () => T.Walk.TakeAimed();
        _walking.AddChild(take);
        _walking.AddChild(new VSeparator());
        _walking.AddChild(_view = Toggle("eye", "Third person", "V", on => T.Walk.ThirdPerson = on));
        _walking.AddChild(_fly = Toggle("fly", "Fly", "F · up Space, down C", T.Walk.Fly));
        _walking.AddChild(new VSeparator());
        row.AddChild(_walking);
        _log = Toggle("log", "Log", "", on => { if (on != LogOpen) _shell.TogglePanel("log"); });
        if (S.IsPlayer)
        {
            var briefing = Ui.IconBtn("page", "Briefing");
            briefing.Pressed += () => BriefingWindow.Open(HudNode, S);
            row.AddChild(briefing);
            row.AddChild(_log);
        }
        else
        {
            var leave = Ui.IconBtn("leave", "Leave control");
            leave.Pressed += () => T.Walk.LeaveControl();
            row.AddChild(leave);
        }
        Panel.AddChild(row);
        Ctx.Root.AddChild(_aim);
        Ctx.Root.AddChild(Panel);
        Panel.Resized += () => Place(_screen, _gap);
        if (S.IsPlayer) _bodies.Wear(Cfg.Body);
    }

    bool LogOpen => _shell.Panels.TryGetValue("log", out var log) && log.Visible;

    static Button Toggle(string icon, string tip, string keys, Action<bool> set)
    {
        var b = Ui.Marked(Ui.IconBtn(icon, tip, keys));
        b.ToggleMode = true;
        b.Toggled += on => set(on);
        return b;
    }

    public void Sync()
    {
        Panel.Visible = S.IsPlayer || T.Walk.Walking;
        _walking.Visible = T.Walk.Walking;
        _held.Visible = S.IsPlayer && !T.Walk.Walking;
        _body.Text = $"{_bodies.Worn} ▾";
        _view.SetPressedNoSignal(T.Walk.ThirdPerson);
        _fly.SetPressedNoSignal(T.Walk.Flying);
        _log.SetPressedNoSignal(LogOpen);
        _aim.Visible = T.Walk.Immersed && !T.Walk.ThirdPerson;
    }

    public void Place(Vector2 screen, float gap)
    {
        (_screen, _gap) = (screen, gap);
        var size = Panel.GetCombinedMinimumSize();
        float left = Mathf.Round((screen.X - size.X) / 2);
        Ui.Place(Panel, Control.LayoutPreset.TopLeft, l: left, t: screen.Y - gap - size.Y, r: left + size.X, b: screen.Y - gap);
        Ui.Place(_aim, Control.LayoutPreset.TopLeft, l: screen.X / 2 - AimDot.Radius, t: screen.Y / 2 - AimDot.Radius, r: screen.X / 2 + AimDot.Radius, b: screen.Y / 2 + AimDot.Radius);
    }
}

sealed partial class AimDot : Control
{
    public const float Radius = 2.5f;

    public override void _Draw()
    {
        DrawCircle(Size / 2, Radius + 1, new Color(0, 0, 0, 0.45f));
        DrawCircle(Size / 2, Radius - 0.5f, new Color(1, 1, 1, 0.85f));
    }
}
