using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using Json = KTable.Core.Json;

namespace KTable.App;

sealed class StudioInPlace
{
    readonly Session _live;
    readonly Table _table;
    readonly Net _net;
    readonly DateTime _openedUtc = DateTime.UtcNow;
    PieceStudio? _studio;
    PackManifest _real = new();
    string _was = "";
    Dictionary<string, string> _kindsWere = new(), _kindsShown = new();

    internal StudioInPlace(Session live, Table table, Net net)
    {
        _live = live;
        _table = table;
        _net = net;
    }

    internal bool Ended { get; private set; }

    internal Table Table => _table;

    internal static string? PackToEdit(Session s, PieceDef kind) =>
        s.IsHost && s.Store.PackOfDef(kind.Id) is { } pack && !StandardPacks.IsStandard(pack.Id) ? pack.Id : null;

    internal static void Open(HudContext c, string pack, string kind) => (c.Node.GetTree().CurrentScene as Main)?.EditInPlace(pack, kind);

    internal void Begin(PieceStudio studio)
    {
        _studio = studio;
        _real = Json.Clone(studio.Pack);
        _was = Json.Serialize(studio.Pack);
        _kindsWere = Kinds(studio.Pack);
        _kindsShown = new(_kindsWere);
        _live.Knocked += Knocked;
        _table.SelectionChanged += Picked;
        studio.TreeExiting += () =>
        {
            _live.Knocked -= Knocked;
            _table.SelectionChanged -= Picked;
        };
    }

    internal void Showing(bool scenario, bool tryIt)
    {
        if (_studio is not { } studio) return;
        if (!scenario) _table.Rest(true, pause: true);
        if (!tryIt) studio.T.Rest(true);
        if (scenario) _table.Rest(false, pause: true);
        if (tryIt) studio.T.Rest(false);
    }

    internal void Show(PackManifest draft)
    {
        if (Ended) return;
        var now = Kinds(draft);
        _live.ShowDraft(Json.Clone(draft));
        Redraw(Differing(_kindsShown, now));
        _kindsShown = now;
    }

    internal int OnTable(string kind) => _live.View.Layers.Values.Sum(l => l.Count(p => p.Def == kind));

    internal bool Changed(PackManifest draft) => !Ended && Json.Serialize(draft) != _was;

    internal string? Confirm(PackageStore store, PackManifest draft)
    {
        if (!Changed(draft))
        {
            End();
            return null;
        }
        var now = Kinds(draft);
        var changed = Differing(_kindsWere, now).ToHashSet();
        var restart = changed.Where(id => ScriptsOf(_real, id) != ScriptsOf(draft, id)).ToHashSet();
        try { Json.SaveBig(PackEdit.ManifestPath(store, draft.Id), draft); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return e.Message; }
        End();
        _net.SharePack(draft.Id);
        _net.RestartScriptsOf(restart);
        Redraw(changed);
        return null;
    }

    internal void Discard(PackageStore store, PackManifest draft)
    {
        if (Ended) return;
        foreach (var (sha, asset) in draft.Assets)
        {
            if (_real.Assets.ContainsKey(sha)) continue;
            DropIfNew(Path.Combine(store.PackageDir(draft.Id), asset.File));
            DropIfNew(ArtStore.DocPath(store, draft.Id, sha));
        }
        var shown = _kindsShown;
        End();
        Redraw(Differing(_kindsWere, shown));
    }

    void End()
    {
        Ended = true;
        _live.ShowDraft(null);
    }

    void Redraw(IEnumerable<string> kinds) => Redraw(_table, _live, kinds);

    internal static void Redraw(Table table, Session s, IEnumerable<string> kinds)
    {
        if (!GodotObject.IsInstanceValid(table)) return;
        var ids = kinds.ToList();
        if (ids.Any(id => s.Def(id) == null)) { table.Rebuild(); return; }
        foreach (var id in ids) table.RebuildPiecesOf(id);
    }

    internal static void RedrawChanged(Table table, Session s, PackManifest? was, PackManifest? now)
    {
        var kinds = now?.Pieces.Select(d => d.Id).ToHashSet() ?? new();
        bool undrawn = s.View.Layers.Any(l => l.Value.Any(p => kinds.Contains(p.Def) && table.Pieces.NodeOf(l.Key, p.Id) == null));
        if (was == null || now == null || undrawn) { table.Rebuild(); return; }
        Redraw(table, s, Differing(Kinds(was), Kinds(now)));
    }

    void DropIfNew(string path)
    {
        if (File.Exists(path) && File.GetCreationTimeUtc(path) >= _openedUtc.AddSeconds(-1)) BestEffort.DeleteFile(path);
    }

    void Knocked(Knock k)
    {
        if (_studio is { } s && GodotObject.IsInstanceValid(s)) s.Flash($"{k.Name} is at the door: Confirm or Discard to let them in", 10);
    }

    void Picked()
    {
        if (_studio is not { } s || !GodotObject.IsInstanceValid(s) || s.View != "scenario") return;
        if (_table.SelectedNodes.Select(n => n.Piece.Def).Distinct().ToList() is not [var def]) return;
        if (s.Pack.Pieces.Find(d => d.Id == def) is { } kind && kind != s.Sel) s.Select(kind, frame: false);
    }

    static Dictionary<string, string> Kinds(PackManifest p)
    {
        var kinds = new Dictionary<string, string>();
        foreach (var d in p.Pieces) kinds.TryAdd(d.Id, Json.Serialize(d));
        return kinds;
    }

    static IEnumerable<string> Differing(Dictionary<string, string> a, Dictionary<string, string> b) =>
        a.Keys.Union(b.Keys).Where(k => a.GetValueOrDefault(k) != b.GetValueOrDefault(k));

    static string ScriptsOf(PackManifest p, string id) =>
        p.Pieces.Find(d => d.Id == id) is { } d ? string.Join("\n", d.States.Select(s => s.Script).Prepend(d.Script)) : "";
}
