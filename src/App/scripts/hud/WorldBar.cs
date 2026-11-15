using Godot;

namespace KTable.App;

sealed class WorldBar : HudPart
{
    public const string Key = "world";
    const int StateSize = 12;
    public readonly FloatPanel Panel;
    readonly Button _walls, _lower, _higher;
    readonly Label _wallsSaid, _storeySaid;
    readonly HBoxContainer _storeys;

    public WorldBar(HudContext ctx, IHudShell shell) : base(ctx)
    {
        Panel = shell.MakePanel(Key, "View");
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        _walls = Ui.Marked(Ui.IconBtn("walls", "Walls", $"Up, cut away or down · {Cfg.Keys.Label("storeyDown")} and {Cfg.Keys.Label("storeyUp")} pick the storey"));
        _walls.ToggleMode = true;
        _walls.Toggled += _ => T.Cutaway.Next();
        row.AddChild(_walls);
        row.AddChild(_wallsSaid = State());
        _storeys = new HBoxContainer();
        _storeys.AddThemeConstantOverride("separation", Ui.Gap.Hair);
        _lower = Btn("▾", () => T.Cutaway.Step(-1), "Storey below");
        _higher = Btn("▴", () => T.Cutaway.Step(1), "Storey above");
        _storeys.AddChild(_lower);
        _storeys.AddChild(_storeySaid = State());
        _storeys.AddChild(_higher);
        row.AddChild(_storeys);
        row.AddChild(new VSeparator());
        row.AddChild(GoTo());
        Panel.Body.AddChild(row);
        T.Cutaway.Changed += Sync;
        Panel.TreeExiting += () => T.Cutaway.Changed -= Sync;
        Sync();
        shell.OpenPanel(Key);
    }

    static Label State()
    {
        var l = new Label { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", StateSize);
        l.AddThemeColorOverride("font_color", Ui.Muted);
        return l;
    }

    void Sync()
    {
        if (!GodotObject.IsInstanceValid(_walls)) return;
        var cut = T.Cutaway;
        _walls.SetPressedNoSignal(cut.Mode != WallView.Up);
        _wallsSaid.Text = cut.Mode switch { WallView.Cutaway => "Cut away", WallView.Down => "Walls down", _ => "Walls up" };
        _storeys.Visible = cut.Mode != WallView.Up && cut.Storeys > 1;
        _storeySaid.Text = $"Storey {cut.Storey + 1}";
        _lower.Disabled = cut.Storey <= 0;
        _higher.Disabled = cut.Storey >= cut.Storeys - 1;
    }

    MenuButton GoTo()
    {
        var go = new MenuButton { Text = "Go to ▾", Flat = false, FocusMode = Control.FocusModeEnum.None, TooltipText = "Fly the camera to a player's figure" };
        var menu = go.GetPopup();
        var peers = new System.Collections.Generic.List<long>();
        go.AboutToPopup += () =>
        {
            menu.Clear();
            peers.Clear();
            foreach (var (peer, name) in T.Avatars.Named())
            {
                peers.Add(peer);
                menu.AddItem(name, peers.Count - 1);
            }
            if (peers.Count == 0)
            {
                menu.AddItem("No one walking", -1);
                menu.SetItemDisabled(0, true);
            }
            if (!T.FlewTo) return;
            menu.AddSeparator();
            menu.AddItem("Back", int.MaxValue);
        };
        menu.IdPressed += id =>
        {
            if (id == int.MaxValue) T.FlyBack();
            else if (id >= 0 && id < peers.Count) T.FlyToFigure(peers[(int)id]);
        };
        return go;
    }
}
