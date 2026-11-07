using KTable.Core.Model;
using KTable.Core.Panels;
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
        var key = piece ?? "";
        List<UiNode> Data() => _s.State.UiRoot(layer, piece) ?? throw new ScriptRuntimeException($"ui: piece {piece} is gone");
        UiLayout? Live() => piece == null ? _s.State.UiLayout : _s.State.Find(layer ?? Layers.Umpire, piece)?.UiLayout;
        UiElement? Element(DynValue id) => Live()?.ById(UiTrees.ClrText(id) ?? throw new ScriptRuntimeException("Value cannot be null.\nParameter name: key"));
        DynValue Queue(DynValue id, string kind, Dictionary<string, string> attrs, string? value)
        {
            if (Element(id) == null) return DynValue.False;
            Panels.Queue(layer, piece, new UiOp(kind, UiTrees.ClrText(id)!, attrs, value));
            return DynValue.True;
        }
        ui.Table["set"] = Fn(a => B(Panels.Set(layer, piece, Arg(a, 0), Arg(a, 1), Data)));
        ui.Table["xml"] = Fn(_ => S(PanelData.Write(Data())));
        ui.Table["tree"] = Fn(_ => UiTrees.Tables(_vm, Data()));
        ui.Table["set_attribute"] = Fn(a =>
        {
            var name = UiTrees.ClrText(Arg(a, 1));
            var value = UiTrees.ClrText(Arg(a, 2));
            if (Element(Arg(a, 0)) == null || name == null || value == null) return DynValue.False;
            return Queue(Arg(a, 0), PanelOps.SetAttribute, new Dictionary<string, string> { [name] = value }, null);
        });
        ui.Table["set_attributes"] = Fn(a =>
        {
            if (Element(Arg(a, 0)) == null) return DynValue.False;
            var attrs = new Dictionary<string, string>();
            if (Arg(a, 1).Type == DataType.Table)
                foreach (var pair in Arg(a, 1).Table.Pairs) attrs.Add(UiTrees.ClrText(pair.Key)!, UiTrees.ClrText(pair.Value)!);
            return Queue(Arg(a, 0), PanelOps.SetAttributes, attrs, null);
        });
        ui.Table["get_attribute"] = Fn(a => Element(Arg(a, 0))?.Get(UiTrees.ClrText(Arg(a, 1)) ?? throw new ScriptRuntimeException("Value cannot be null.")) is { } v ? S(v) : DynValue.Nil);
        ui.Table["get_attributes"] = Fn(a => Element(Arg(a, 0)) is { } e ? UiTrees.Attributes(_vm, e.Attributes) : DynValue.Nil);
        ui.Table["show"] = Fn(a => Queue(Arg(a, 0), PanelOps.Show, new(), null));
        ui.Table["hide"] = Fn(a => Queue(Arg(a, 0), PanelOps.Hide, new(), null));
        ui.Table["set_class"] = Fn(a => Queue(Arg(a, 0), PanelOps.SetClass, new(), UiTrees.ClrText(Arg(a, 1)) ?? ""));
        ui.Table["set_value"] = Fn(a => UiTrees.ClrText(Arg(a, 1)) is { } v ? Queue(Arg(a, 0), PanelOps.SetValue, new(), v) : DynValue.False);
        ui.Table["get_value"] = Fn(a => PanelData.ById(Data(), UiTrees.ClrText(Arg(a, 0)) ?? "")?.Value is { } v ? S(v) : DynValue.Nil);
        ui.Table["pictures"] = Fn(_ => Panels.Pictures(Panels.AssetsOf(key, layer, piece)));
        ui.Table["set_pictures"] = Fn(a =>
        {
            Panels.OwnerOf(key).Assets = PanelQueue.AssetList(Arg(a, 0));
            Panels.StartLoad(layer, piece, PanelData.Write(Data()));
            return DynValue.True;
        });
        var types = T();
        types.Table["Image"] = UiPictureType;
        types.Table["AssetBundle"] = UiPictureType + 1;
        types.Table["image"] = UiPictureType;
        types.Table["assetBundle"] = UiPictureType + 1;
        ui.Table["assetType"] = types;
        var meta = T();
        meta.Table["__index"] = DynValue.NewCallback((_, a) => Arg(a, 1).CastToString() == "loading" ? B(Panels.Loading(key, Live())) : DynValue.Nil);
        ui.Table.MetaTable = meta.Table;
        return ui;
    }

    const int UiPictureType = PanelQueue.PictureType;

    public static List<UiNode> ParseXml(string xml) => PanelData.Parse(xml) ?? throw new ScriptRuntimeException("ui.set: the XML does not parse");

    public static string ToXml(IEnumerable<UiNode> tree) => PanelData.Write(tree.ToList());
}
