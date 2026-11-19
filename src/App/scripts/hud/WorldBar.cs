using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class WorldBar : HudPart
{
    const ulong LookEveryMs = 1000;
    const int DotWidth = 8, StateSize = 12, ReadingGap = 12, ReadingWidth = 160, ReadingSize = 15, ReadingOutline = 8;
    static readonly string[] IconStates = ["icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_hover_pressed_color", "icon_focus_color"];

    public readonly PanelContainer Panel;
    readonly IHudShell _shell;
    readonly Action<Vector2, string, long> _openPlayer;
    readonly Label _held = Ui.Readout("Waiting to be placed", 0), _wallsSaid = State(), _storeySaid = State(), _reading = Reading();
    readonly HBoxContainer _own = Group(Ui.Gap.Tight), _party = Group(Ui.Gap.Tight), _walls = Group(Ui.Gap.Tight), _storeys = Group(Ui.Gap.Hair);
    readonly HBoxContainer _walking = Group(Ui.Gap.Tight), _tools = Group(Ui.Gap.Tight), _ends = Group(Ui.Gap.Tight);
    readonly List<(VSeparator Line, HBoxContainer Group)> _groups = new();
    readonly Button _wallsButton, _lower, _higher, _view, _god, _names, _sneak, _march, _fly, _log, _back, _leave, _dice, _measure, _ping;
    readonly MenuButton _everyone;
    readonly MenuButton _walks = new() { Text = "Walk ▾", Flat = false, FocusMode = Control.FocusModeEnum.None };
    readonly Button _boundsButton;
    readonly DiceTray _tray;
    readonly ColorRect _veil = new() { Color = Ui.Bar with { A = 1 }, MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
    readonly AimDot _aim = new() { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
    Vector2 _screen;
    float _gap;
    string? _partyNow;
    int _players;
    bool _buildings;
    ulong _lookedAt;

    public WorldBar(HudContext ctx, IHudShell shell, Action<Vector2, string, long> openPlayer) : base(ctx)
    {
        _shell = shell;
        _openPlayer = openPlayer;
        Panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        Panel.AddThemeStyleboxOverride("panel", Ui.TopBar());
        _own.AddChild(_held);
        _names = Toggle("people", "Name tags", "", on =>
        {
            Cfg.NameTags = on;
            Cfg.Save();
        });
        _wallsButton = Ui.IconBtn("walls", "Walls", $"Up, cut away or down · {Cfg.Keys.Label("storeyDown")} and {Cfg.Keys.Label("storeyUp")} pick the storey");
        _wallsButton.ToggleMode = true;
        _wallsButton.Toggled += _ => T.Cutaway.Next();
        _lower = Btn("▾", () => T.Cutaway.Step(-1), "Storey below");
        _higher = Btn("▴", () => T.Cutaway.Step(1), "Storey above");
        _storeys.AddChild(_lower);
        _storeys.AddChild(_storeySaid);
        _storeys.AddChild(_higher);
        _walls.AddChild(_wallsButton);
        _walls.AddChild(_wallsSaid);
        _walls.AddChild(_storeys);
        _walking.AddChild(_view = Toggle("eye", "Third person", "V", on => T.Walk.ThirdPerson = on));
        _walking.AddChild(_fly = Toggle("fly", "Fly", "F · up Space, down Ctrl, boost Shift", T.Walk.Fly));
        _walking.AddChild(_god = Toggle("map", "God view", "Tab", T.Walk.SeeAsGod));
        _walking.AddChild(_sneak = Stance("Sneak", "X", RigMotion.Sneak));
        _walking.AddChild(_march = Stance("March", "C · Shift for the double quick", RigMotion.March));
        _walking.AddChild(_walks);
        var walks = _walks.GetPopup();
        walks.AddItem("Its own", 0);
        for (int i = 0; i < RigMotion.Gaits.Count; i++) walks.AddItem(RigMotion.Gaits[i].Name, i + 1);
        walks.IdPressed += id => T.Walk.WalkChoice = id == 0 ? null : RigMotion.Gaits[(int)id - 1].Id;
        _back = Btn("Back", () => T.FlyBack(), "Back to where the camera was");
        _leave = Ui.IconBtn("leave", "Release control");
        _leave.Pressed += () => T.Walk.LeaveControl();
        _log = Toggle("log", LogPanel.Title, "", on => { if (on != LogOpen) _shell.TogglePanel("log"); });
        _everyone = Everyone();
        _boundsButton = Toggle("zones", "Bounds", "Where each player may stand", on => { if (on != BoundsOpen) _shell.TogglePanel(BoundsPanel.Key); });
        _tray = new DiceTray(ctx);
        _dice = Ui.IconBtn("dice", "Dice");
        _dice.Pressed += () => _tray.Open(_dice);
        _measure = Toggle("ruler", "Measure", "M · from your feet to where you look", on => T.Walk.Measuring = on, tool: true);
        _ping = Ui.IconBtn("pin", "Ping", "Middle click · where you look, for everyone");
        _ping.Pressed += () => T.Walk.PingAimed();
        _tools.AddChild(_dice);
        _tools.AddChild(_measure);
        _tools.AddChild(_ping);
        Ends();
        var row = Group(Ui.Gap.Tight);
        foreach (var group in new[] { _own, _party, _walls, _walking, _tools, _ends })
        {
            var line = new VSeparator();
            row.AddChild(line);
            row.AddChild(group);
            _groups.Add((line, group));
        }
        Panel.AddChild(row);
        Ctx.Root.AddChild(_veil);
        Ctx.Root.MoveChild(_veil, 0);
        _veil.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        Ctx.Root.AddChild(_aim);
        Ctx.Root.AddChild(_reading);
        Ctx.Root.AddChild(Panel);
        Panel.Resized += () => Place(_screen, _gap);
        Panel.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }) return;
            var hide = new PopupMenu();
            hide.AddItem("Hide the bar");
            hide.IdPressed += _ =>
            {
                Cfg.BarShown = false;
                Cfg.Save();
                Say("Menu → Bottom bar brings it back");
            };
            hide.PopupHide += hide.QueueFree;
            Ctx.Root.AddChild(hide);
            hide.Popup(new Rect2I((Vector2I)Panel.GetGlobalMousePosition(), Vector2I.Zero));
        };
        T.Cutaway.Changed += SyncWalls;
        Panel.TreeExiting += () => T.Cutaway.Changed -= SyncWalls;
        SyncWalls();
    }

    void Ends()
    {
        if (!S.IsPlayer)
        {
            _ends.AddChild(_back);
            _ends.AddChild(_leave);
            return;
        }
        var briefing = Ui.IconBtn("page", "Briefing");
        briefing.Pressed += () => BriefingWindow.Open(HudNode, S);
        _ends.AddChild(briefing);
        _ends.AddChild(_log);
    }

    static HBoxContainer Group(int gap)
    {
        var group = new HBoxContainer();
        group.AddThemeConstantOverride("separation", Ui.Px(gap));
        return group;
    }

    static Label Reading()
    {
        var l = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        l.AddThemeConstantOverride("outline_size", Ui.Px(ReadingOutline));
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        l.AddThemeColorOverride("font_color", Ui.Ink);
        l.AddThemeFontSizeOverride("font_size", Ui.Px(ReadingSize));
        return l;
    }

    MenuButton Everyone()
    {
        var everyone = new MenuButton { Text = "Everyone ▾", Flat = false, FocusMode = Control.FocusModeEnum.None, TooltipText = "What every player may do, now and when they join" };
        var menu = everyone.GetPopup();
        menu.HideOnCheckableItemSelection = false;
        everyone.AboutToPopup += () =>
        {
            menu.Clear();
            RightsRows.Add(menu, S.View.Everyone ?? new PlayerRights());
        };
        menu.IdPressed += id =>
        {
            S.Do(RightsRows.Flipped(S.View, null, id));
            menu.SetItemChecked(menu.GetItemIndex((int)id), !menu.IsItemChecked(menu.GetItemIndex((int)id)));
        };
        return everyone;
    }

    static Label State()
    {
        var l = new Label { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", Ui.Px(StateSize));
        l.AddThemeColorOverride("font_color", Ui.Muted);
        return l;
    }

    bool LogOpen => _shell.Panels.TryGetValue("log", out var log) && log.Visible;
    bool BoundsOpen => _shell.Panels.TryGetValue(BoundsPanel.Key, out var bounds) && bounds.Visible;

    Button Stance(string name, string keys, string stance)
    {
        var b = Btn(name, () => { }, keys);
        b.ToggleMode = true;
        b.Toggled += on => T.Walk.Stand(on ? stance : null);
        return b;
    }

    static Button Toggle(string icon, string tip, string keys, Action<bool> set, bool tool = false)
    {
        var b = Ui.IconBtn(icon, tip, keys);
        if (tool) Ui.MarkTool(b);
        b.ToggleMode = true;
        b.Toggled += on => set(on);
        return b;
    }

    public void Sync()
    {
        bool walking = T.Walk.Walking, player = S.IsPlayer;
        Look();
        _own.Visible = player;
        _held.Visible = _veil.Visible = player && !walking;
        _party.Visible = !player && (_players > 0 || _buildings || walking || S.View.BoundLayers != null);
        _walls.Visible = !player && _buildings;
        _walking.Visible = walking;
        _back.Visible = !player && T.FlewTo;
        _leave.Visible = !player && walking;
        var allowed = T.Walk.Allowed;
        (_dice.Visible, _measure.Visible, _ping.Visible) = (player && allowed.Dice, walking && allowed.Measure, walking && allowed.Ping);
        _tools.Visible = _dice.Visible || _measure.Visible || _ping.Visible;
        _ends.Visible = player || _back.Visible || _leave.Visible;
        bool any = false;
        foreach (var (line, group) in _groups)
        {
            line.Visible = group.Visible && any;
            any |= group.Visible;
        }
        Panel.Visible = any && Cfg.BarShown;
        (_view.Visible, _fly.Visible) = (allowed.View, allowed.Fly);
        _god.Visible = !player && T.Walk.InControl;
        _god.SetPressedNoSignal(T.Walk.GodView);
        _sneak.Visible = _march.Visible = _walks.Visible = allowed.Walk && T.Walk.Styled;
        _sneak.SetPressedNoSignal(T.Walk.Stance == RigMotion.Sneak);
        _march.SetPressedNoSignal(T.Walk.Stance == RigMotion.March);
        var tagsHome = player ? _own : _party;
        if (_names.GetParent() != tagsHome)
        {
            _names.GetParent()?.RemoveChild(_names);
            tagsHome.AddChild(_names);
        }
        _names.SetPressedNoSignal(Cfg.NameTags);
        _measure.SetPressedNoSignal(T.Walk.Measuring);
        (_reading.Text, _reading.Visible) = (T.Measure.Reading ?? "", T.Measure.Reading != null);
        if (_reading.Visible) _reading.Position = Pointer.In(_reading.GetViewport()) + new Vector2(Ui.Px(ReadingGap), -Ui.Px(ReadingGap));
        _view.SetPressedNoSignal(T.Walk.ThirdPerson);
        _fly.SetPressedNoSignal(T.Walk.Flying);
        _log.SetPressedNoSignal(LogOpen);
        _boundsButton.SetPressedNoSignal(BoundsOpen);
        _aim.Visible = false;
        if (_screen != Vector2.Zero && !Panel.Size.IsEqualApprox(Panel.GetCombinedMinimumSize())) Place(_screen, _gap);
    }

    void SyncWalls()
    {
        if (!GodotObject.IsInstanceValid(_wallsButton)) return;
        var cut = T.Cutaway;
        _wallsButton.SetPressedNoSignal(cut.Mode != WallView.Up);
        _wallsSaid.Text = cut.Mode switch { WallView.Cutaway => "Cut away", WallView.Down => "Walls down", _ => "Walls up" };
        _storeys.Visible = cut.Mode != WallView.Up && cut.Storeys > 1;
        _storeySaid.Text = $"Storey {cut.Storey + 1}";
        _lower.Disabled = cut.Storey <= 0;
        _higher.Disabled = cut.Storey >= cut.Storeys - 1;
    }

    void Look()
    {
        ulong now = Time.GetTicksMsec();
        if (S.IsPlayer || now - _lookedAt < LookEveryMs) return;
        _lookedAt = now;
        _buildings = T.Pieces.Nodes.Any(n => n.Def?.Tags.Contains(WallCutaway.BuildingTag) == true);
        var waiting = T.Waiting();
        var players = S.Peers.Where(p => p.Role == Role.Player).OrderBy(p => p.Name)
            .Select(p => (p.PeerId, p.Name, Waits: waiting.Contains(p.Name), Hue: UmpireHue.Of(S, p) ?? UmpireHue.Unassigned)).ToList();
        string party = string.Join("|", players.Select(p => $"{p.PeerId}:{p.Name}:{p.Waits}:{p.Hue.ToHtml()}"));
        _players = players.Count;
        if (party == _partyNow) return;
        _partyNow = party;
        foreach (var old in _party.GetChildren().Where(c => c != _everyone && c != _boundsButton && c != _names)) old.QueueFree();
        foreach (var (peer, name, waits, hue) in players) _party.AddChild(Chip(peer, name, waits, hue));
        if (_everyone.GetParent() == null) _party.AddChild(_everyone);
        _party.MoveChild(_everyone, -1);
        if (_boundsButton.GetParent() == null) _party.AddChild(_boundsButton);
        _party.MoveChild(_boundsButton, -1);
        if (_names.GetParent() == _party) _party.MoveChild(_names, -1);
    }

    Button Chip(long peer, string name, bool waits, Color hue)
    {
        var chip = new Button { Text = waits ? $"{name} · waits ▾" : $"{name} ▾", Icon = Ui.Dot, FocusMode = Control.FocusModeEnum.None };
        chip.AddThemeConstantOverride("icon_max_width", Ui.Px(DotWidth));
        foreach (var state in IconStates) chip.AddThemeColorOverride(state, hue);
        if (waits) chip.AddThemeStyleboxOverride("normal", Ui.Box(new Color(Ui.Accent, 0.30f), 3, 10, 4));
        void Open() => _openPlayer(chip.GlobalPosition, name, peer);
        chip.Pressed += Open;
        chip.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }) Open(); };
        return chip;
    }

    public void Place(Vector2 screen, float gap)
    {
        (_screen, _gap) = (screen, gap);
        var size = Panel.GetCombinedMinimumSize();
        float left = Mathf.Round((screen.X - size.X) / 2);
        Ui.Place(Panel, Control.LayoutPreset.TopLeft, l: left, t: screen.Y - gap - size.Y, r: left + size.X, b: screen.Y - gap);
        float dot = Ui.Pxf(AimDot.Radius + 1), aside = Ui.Px(ReadingGap);
        Ui.Place(_aim, Control.LayoutPreset.TopLeft, l: screen.X / 2 - dot, t: screen.Y / 2 - dot, r: screen.X / 2 + dot, b: screen.Y / 2 + dot);
        Ui.Place(_reading, Control.LayoutPreset.TopLeft, l: screen.X / 2 + aside, t: screen.Y / 2 - aside, r: screen.X / 2 + aside + Ui.Px(ReadingWidth), b: screen.Y / 2 + aside);
    }
}

sealed partial class AimDot : Control
{
    public const float Radius = 2.5f;

    public override void _Draw()
    {
        DrawCircle(Size / 2, Ui.Pxf(Radius + 1), new Color(0, 0, 0, 0.45f));
        DrawCircle(Size / 2, Ui.Pxf(Radius - 0.5f), new Color(1, 1, 1, 0.85f));
    }
}
