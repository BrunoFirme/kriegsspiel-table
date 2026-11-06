using KTable.Core.Model;
using KTable.Core.Protocol;
using MoonSharp.Interpreter;
using static KTable.Server.Scripting.LuaArgs;

namespace KTable.Server.Scripting;

public sealed partial class ScriptHost
{
    readonly Dictionary<string, (PieceScript owner, DynValue fn, string kind)> _dialogs = new();
    int _nextDialog = 1;

    void InstallUiApi()
    {
        _vm.Globals["ui"] = UiTable(null, null);

        var game = _vm.Globals.Get("game");
        if (game.Type != DataType.Table) return;
        game.Table["fetch_picture"] = Fn(FetchPictureCall);
        game.Table["dialog"] = Fn(a =>
        {
            var owner = _current ?? throw new ScriptRuntimeException("game.dialog: not inside a script call");
            var who = Arg(a, 0);
            var spec = Arg(a, 1);
            var fn = Arg(a, 2);
            if (fn.Type != DataType.Function) throw new ScriptRuntimeException("game.dialog: the third argument must be a function");
            var token = $"d-{_nextDialog++:D6}";
            var d = new ShowDialog { Token = token, To = who.Type == DataType.Table && who.Table.Get("id").Type == DataType.Number ? $"p:{(long)who.Table.Get("id").Number}" : null };
            if (spec.Type == DataType.Table)
            {
                d.Kind = spec.Table.Get("kind").CastToString() ?? "memo";
                d.Title = spec.Table.Get("title").CastToString() ?? "";
                d.Text = spec.Table.Get("text").CastToString() ?? "";
                if (spec.Table.Get("options").Type == DataType.Table) foreach (var o in spec.Table.Get("options").Table.Values) d.Options.Add(o.CastToString() ?? "");
            }
            _dialogs[token] = (owner, fn, d.Kind);
            Emit(d);
            return S(token);
        });
    }

    readonly Dictionary<string, DynValue> _uiTables = new();

    internal DynValue UiTableFor(string pieceId)
    {
        if (!_uiTables.TryGetValue(pieceId, out var t)) _uiTables[pieceId] = t = UiTable(Layers.Umpire, pieceId);
        return t;
    }

    DynValue UiTable(string? layer, string? piece)
    {
        var ui = T();
        List<UiNode> Root() => _s.State.UiRoot(layer, piece) ?? throw new ScriptRuntimeException($"ui: piece {piece} is gone");
        UiNode? Node(string id) => _s.State.UiNode(layer, piece, id);
        ui.Table["set"] = Fn(a =>
        {
            Root();
            Emit(new SetUi { Layer = layer, Piece = piece, Tree = UiTrees.TreeOf(Arg(a, 0)) });
            return DynValue.Nil;
        });
        ui.Table["xml"] = Fn(_ => S(ToXml(Root())));
        ui.Table["tree"] = Fn(_ =>
        {
            var t = T();
            foreach (var n in Root()) t.Table.Append(UiTrees.NodeTable(_vm, n));
            return t;
        });
        ui.Table["set_attribute"] = Fn(a => B(SetAttrs(layer, piece, Arg(a, 0).CastToString() ?? "", new() { [Arg(a, 1).CastToString() ?? ""] = UiTrees.AttrText(Arg(a, 2)) })));
        ui.Table["set_attributes"] = Fn(a =>
        {
            var attrs = new Dictionary<string, string>();
            if (Arg(a, 1).Type == DataType.Table) foreach (var pair in Arg(a, 1).Table.Pairs) if (pair.Key.Type == DataType.String) attrs[pair.Key.String] = UiTrees.AttrText(pair.Value);
            return B(SetAttrs(layer, piece, Arg(a, 0).CastToString() ?? "", attrs));
        });
        ui.Table["get_attribute"] = Fn(a => Node(Arg(a, 0).CastToString() ?? "")?.Attributes.GetValueOrDefault(Arg(a, 1).CastToString() ?? "") is { } v ? S(v) : DynValue.Nil);
        ui.Table["show"] = Fn(a => B(SetAttrs(layer, piece, Arg(a, 0).CastToString() ?? "", new() { ["active"] = "true" })));
        ui.Table["hide"] = Fn(a => B(SetAttrs(layer, piece, Arg(a, 0).CastToString() ?? "", new() { ["active"] = "false" })));
        ui.Table["set_value"] = Fn(a => { var id = Arg(a, 0).CastToString() ?? ""; return B(SetAttrs(layer, piece, id, new() { [UiTrees.ValueKey(Node(id))] = UiTrees.AttrText(Arg(a, 1)) })); });
        ui.Table["get_value"] = Fn(a => { var n = Node(Arg(a, 0).CastToString() ?? ""); return n != null && n.Attributes.TryGetValue(UiTrees.ValueKey(n), out var v) ? S(v) : DynValue.Nil; });
        return ui;
    }

    bool SetAttrs(string? layer, string? piece, string id, Dictionary<string, string> attrs)
    {
        if (_s.State.UiNode(layer, piece, id) == null) return false;
        Emit(new SetUiAttributes { Layer = layer, Piece = piece, Id = id, Attributes = attrs });
        return true;
    }

    void UiEventReact(UiEvent e, DynValue who)
    {
        var node = _s.State.UiNode(e.Layer, e.Piece, e.Id);
        if (node == null) return;
        var attr = e.Event switch { "click" => "onClick", "end" => "onEndEdit", _ => "onValueChanged" };
        if (!node.Attributes.TryGetValue(attr, out var handler) || handler.Length == 0)
        {
            if (e.Event == "end" && node.Attributes.TryGetValue("onValueChanged", out handler) && handler.Length > 0) { }
            else return;
        }
        var (ps, fn) = e.Piece != null && !handler.Contains('/') ? HandlerOwner(e.Piece, handler) : ResolveHandler(handler);
        if (ps == null) return;
        Call(ps, fn, who, DynValue.NewString(e.Value), DynValue.NewString(e.Id));
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
        if (d.kind == "picture" && r.Value != null && IsWebLink(r.Value)) { Fetch(d.owner, d.fn, r.Value, who); return; }
        Invoke(d.owner, d.fn, r.Value == null ? DynValue.Nil : DynValue.NewString(r.Value), who);
    }

    public const int MaxUiDepth = 24, MaxUiNodes = 20_000;

    public static List<UiNode> ParseXml(string xml) => UiTrees.ParseXml(xml);

    public static string ToXml(IEnumerable<UiNode> tree) => UiTrees.ToXml(tree);
}
