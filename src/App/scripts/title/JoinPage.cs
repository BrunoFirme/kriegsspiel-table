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
        g.AddThemeConstantOverride("h_separation", Ui.Px(Ui.Gap.Block));
        g.AddThemeConstantOverride("v_separation", Ui.Px(Ui.Gap.Block));
        v.AddChild(g);
        g.AddChild(Ui.HeadLabel("Address"));
        var addr = new LineEdit { Text = _cfg.LastAddress, CustomMinimumSize = Ui.Px(320, 0) };
        g.AddChild(addr);
        g.AddChild(Ui.HeadLabel("Password"));
        var key = AgentFence.Mark(new LineEdit { CustomMinimumSize = Ui.Px(320, 0) });
        g.AddChild(key);
        g.AddChild(Ui.HeadLabel("Port"));
        var port = new LineEdit { Text = _cfg.Port.ToString(), CustomMinimumSize = Ui.Px(100, 0), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        g.AddChild(port);
        v.AddChild(new Control { CustomMinimumSize = Ui.Px(0, 8) });
        v.AddChild(Primary("Join", () =>
        {
            if (int.TryParse(port.Text, out var p)) _cfg.Port = p;
            _cfg.Save();
            _main.StartJoin(addr.Text.Trim(), _cfg.Port, GuestName, key.Text.Trim(), null);
        }));
        key.TextSubmitted += _ => _main.StartJoin(addr.Text.Trim(), _cfg.Port, GuestName, key.Text.Trim(), null);
    }
}
