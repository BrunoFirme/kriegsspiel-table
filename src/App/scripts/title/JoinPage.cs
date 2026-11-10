using Godot;
using static KTable.App.TitleStyle;

namespace KTable.App;

sealed class JoinPage
{
    readonly TitleScreen _t;
    readonly Main _main;
    readonly Settings _cfg;

    public JoinPage(TitleScreen t, Main main, Settings cfg)
    {
        _t = t;
        _main = main;
        _cfg = cfg;
    }

    public const string Guest = "Guest";

    string GuestName => _t.PlayerName.Text.Trim().Length > 0 ? _t.PlayerName.Text.Trim() : Guest;

    public void Show()
    {
        var v = _t.NewPage("Join a game", "");
        var g = new GridContainer { Columns = 2 };
        g.AddThemeConstantOverride("h_separation", Ui.Gap.Block);
        g.AddThemeConstantOverride("v_separation", Ui.Gap.Block);
        v.AddChild(g);
        g.AddChild(Caps("Address"));
        var addr = new LineEdit { Text = _cfg.LastAddress, CustomMinimumSize = new Vector2(320, 0) };
        g.AddChild(addr);
        g.AddChild(Caps("Password"));
        var key = AgentFence.Mark(new LineEdit { PlaceholderText = "the game's password, if it has one", CustomMinimumSize = new Vector2(320, 0) });
        g.AddChild(key);
        g.AddChild(Caps("Port"));
        var port = new LineEdit { Text = _cfg.Port.ToString(), CustomMinimumSize = new Vector2(100, 0), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        g.AddChild(port);
        v.AddChild(new Label { Text = "You knock; the host or a lead umpire lets you in and gives you a role. Once let in, this PC comes straight back in later.", Modulate = Muted, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(440, 0) });
        v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        v.AddChild(Primary("Join", () =>
        {
            if (int.TryParse(port.Text, out var p)) _cfg.Port = p;
            _cfg.Save();
            _main.StartJoin(addr.Text.Trim(), _cfg.Port, GuestName, key.Text.Trim(), null);
        }));
        key.TextSubmitted += _ => _main.StartJoin(addr.Text.Trim(), _cfg.Port, GuestName, key.Text.Trim(), null);
    }
}
