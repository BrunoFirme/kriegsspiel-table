using KTable.Core.Model;
using KTable.Core.Panels;
using KTable.Core.Protocol;
using MoonSharp.Interpreter;

namespace KTable.Server.Scripting;

internal sealed record UiOp(string Kind, string Id, Dictionary<string, string> Attributes, string? Value)
{
    public bool WhileLoading { get; init; }
}

internal sealed class UiOwner
{
    public string? Layer;
    public string? Piece;
    public string? PendingXml;
    public long ReadyFrame;
    public long LoadedFrame = -1;
    public List<UiAsset>? Assets;
    public bool Stuck;
    public readonly List<UiOp> Ops = new();
    public bool Loading => PendingXml != null;
}

internal sealed partial class PanelQueue(ScriptHost host)
{
    public const string ErrorColor = "#f87", WarningColor = "#fd6";
    readonly Dictionary<string, UiOwner> _ui = new();
    GameState State => host.Server.State;
    long Frame => host.Frame;

    public UiOwner OwnerOf(string key)
    {
        if (!_ui.TryGetValue(key, out var o)) _ui[key] = o = new UiOwner { Piece = key.Length == 0 ? null : key, Layer = key.Length == 0 ? null : Layers.Umpire };
        return o;
    }

    public bool Loading(string key, UiLayout? live) => (_ui.TryGetValue(key, out var o) && o.Loading) || live?.Broken != null;

    public void Queue(string? layer, string? piece, UiOp op)
    {
        var o = OwnerOf(piece ?? "");
        o.Layer = layer;
        o.Ops.Add(op with { WhileLoading = o.Loading });
    }

    public void StartLoad(string? layer, string? piece, string xml)
    {
        var o = OwnerOf(piece ?? "");
        o.Layer = layer;
        if (!o.Loading) o.ReadyFrame = Frame + 1;
        o.PendingXml = xml;
        o.Stuck = false;
        for (int i = 0; i < o.Ops.Count; i++) o.Ops[i] = o.Ops[i] with { WhileLoading = true };
    }

    public bool Busy => _ui.Values.Any(o => (o.Loading && !o.Stuck) || (o.Ops.Count > 0 && !Broken(o) && !o.Stuck));

    public void Land()
    {
        foreach (var (key, o) in _ui.ToList())
        {
            if (!o.Loading || o.Stuck || o.ReadyFrame > Frame) continue;
            if (!PanelState.Loads(o.Assets ?? AssetsOf(key, o.Layer, o.Piece)))
            {
                o.Stuck = true;
                continue;
            }
            var xml = o.PendingXml!;
            o.PendingXml = null;
            if (o.Piece != null && State.Find(o.Layer ?? Layers.Umpire, o.Piece) == null) { _ui.Remove(key); continue; }
            var (layout, said) = PanelBuild.Of(xml);
            host.Guard(o.Piece ?? "", () => host.Emit(new SetUi { Layer = o.Layer, Piece = o.Piece, Xml = xml, Assets = o.Assets?.Select(a => a.Clone()).ToList() }));
            o.LoadedFrame = Frame;
            foreach (var line in said) host.SayRaw(line, line.Contains("Warning") ? WarningColor : ErrorColor);
            if (layout.Broken != null) host.SayRaw($"Error Building Xml UI on {ScriptName(o.Piece)}: {layout.Broken}", ErrorColor);
        }
    }

    public void Flush()
    {
        foreach (var o in _ui.Values.ToList())
        {
            while (o.Ops.Count > 0)
            {
                var op = o.Ops[0];
                if (o.Loading || Broken(o)) break;
                if (op.WhileLoading && Frame <= o.LoadedFrame) break;
                o.Ops.RemoveAt(0);
                if (o.Piece != null && State.Find(o.Layer ?? Layers.Umpire, o.Piece) == null) continue;
                if (op.Kind == PanelOps.SetAttribute && op.Attributes.ContainsKey("class")) host.SayRaw(PanelOps.ClassWarning, WarningColor);
                host.Guard(o.Piece ?? "", () => host.Emit(new SetUiAttributes { Layer = o.Layer, Piece = o.Piece, Id = op.Id, Kind = op.Kind, Attributes = op.Attributes, Value = op.Value }));
            }
        }
    }

    bool Broken(UiOwner o) => (o.Piece == null ? State.UiLayout : State.Find(o.Layer ?? Layers.Umpire, o.Piece)?.UiLayout)?.Broken != null;

    public string ScriptName(string? piece) => piece == null ? "Global" : host.NameOf(piece);

    public void Forget(string pieceId) => _ui.Remove(pieceId);

    public const int PictureType = 0;

    public bool Set(string? layer, string? piece, DynValue what, DynValue assets, Func<List<UiNode>> data)
    {
        data();
        string? xml;
        if (what.Type == DataType.Table)
        {
            if (what.Table.Get(1).IsNil())
            {
                host.SayRaw($"[{ScriptName(piece)}] Lua Error <UI.setXmlTable>: table was null", ErrorColor);
                return false;
            }
            xml = UiTrees.FromTable(what.Table);
        }
        else xml = UiTrees.ClrText(what);
        if (assets.Type == DataType.Table) OwnerOf(piece ?? "").Assets = AssetList(assets);
        if (xml == null) return false;
        StartLoad(layer, piece, xml);
        return true;
    }

    public static List<UiAsset> AssetList(DynValue v)
    {
        var list = new List<UiAsset>();
        if (v.Type != DataType.Table) return list;
        foreach (var e in v.Table.Values)
            if (e.Type == DataType.Table)
                list.Add(new UiAsset { Name = e.Table.Get("name").CastToString() ?? "", Url = e.Table.Get("url").CastToString() ?? "", Type = (int)(e.Table.Get("type").CastToNumber() ?? PictureType) });
        return list;
    }

    public List<UiAsset> AssetsOf(string key, string? layer, string? piece)
    {
        if (OwnerOf(key).Assets is { } pending) return pending;
        if (piece == null) return State.UiAssets ?? PanelState.Assets(State.UiPictures);
        return State.Find(layer ?? Layers.Umpire, piece)?.UiAssets ?? new();
    }

    public DynValue Pictures(List<UiAsset> assets)
    {
        var t = DynValue.NewTable(host.Vm);
        foreach (var a in assets)
        {
            var e = DynValue.NewTable(host.Vm);
            e.Table["type"] = a.Type;
            e.Table["name"] = a.Name;
            e.Table["url"] = a.Url;
            t.Table.Append(e);
        }
        return t;
    }

}
