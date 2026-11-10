using KTable.Core.Model;
using KTable.Core.Protocol;
using MoonSharp.Interpreter;

namespace KTable.Server.Scripting;

public sealed partial class ScriptHost
{
    PanelQueue? _panelQueue;
    internal PanelQueue Panels => _panelQueue ??= new PanelQueue(this);
    internal long Frame => _timers.Frame;
    internal PieceScript? GlobalScript => _global;
    internal PieceScript? ScriptOf(string id) => _pieces.TryGetValue(id, out var ps) ? ps : null;

    internal void SayRaw(string text, string color)
    {
        Print(text, "error", "");
        if (_reaction != null) Core.BestEffort.Try(() => Emit(new Say { Text = text, To = "u:", Color = color }));
    }

    (PieceScript? ps, string fn) ResolveHandler(string handler)
    {
        var slash = handler.IndexOf('/');
        if (slash < 0) return (_global, handler);
        var target = handler[..slash];
        var fn = handler[(slash + 1)..];
        var p = _s.State.Layer(Layers.Umpire).FirstOrDefault(x => x.Id == target || x.Guid == target);
        if (p == null || !_pieces.TryGetValue(p.Id, out var ps)) return (null, fn);
        return (ps, fn);
    }

    void DialogReact(DialogResult r, DynValue who)
    {
        if (!_dialogs.Remove(r.Token, out var d)) return;
        if (!d.owner.Enabled || !IsCurrent(d.owner)) return;
        Web.NoteTyped(r.Value);
        if (d.kind == "picture" && r.Value != null && IsWebLink(r.Value)) { Fetch(d.owner, d.fn, r.Value, who); return; }
        Invoke(d.owner, d.fn, r.Value == null ? DynValue.Nil : DynValue.NewString(r.Value), who);
    }
}
