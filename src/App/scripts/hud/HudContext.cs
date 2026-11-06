using System;
using Godot;

namespace KTable.App;

sealed class HudContext
{
    readonly Action<string> _log, _say;
    readonly Action<string?> _showScriptWindow;

    public HudContext(Session s, Table t, Net n, Settings cfg, CanvasLayer node, Action<string> log, Action<string> say, Action<string?> showScriptWindow)
    {
        S = s;
        T = t;
        N = n;
        Cfg = cfg;
        Node = node;
        _log = log;
        _say = say;
        _showScriptWindow = showScriptWindow;
    }

    public Session S { get; }
    public Table T { get; }
    public Net N { get; }
    public Settings Cfg { get; }

    public CanvasLayer Node { get; }

    public Control Root { get; set; } = null!;

    public void Log(string bb) => _log(bb);
    public void Say(string text) => _say(text);
    public void ShowScriptWindow(string? select) => _showScriptWindow(select);

    public Vector2 PanelSize(string key, float w, float h) =>
        Cfg.PanelSize.TryGetValue(key, out var wh) && wh.Length == 2 ? new Vector2(wh[0], wh[1]) : new Vector2(w, h);
}
