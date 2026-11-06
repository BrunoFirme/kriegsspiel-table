using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using Json = KTable.Core.Json;
using KTable.Core.Protocol;

namespace KTable.App;

public sealed class ReliefCache
{
    readonly Assets _assets;
    readonly Func<IReadOnlyList<SheetDef>> _sheets;
    readonly Dictionary<string, ReliefGrid> _grids = new();
    readonly HashSet<string> _loading = new(), _failed = new();
    readonly Dictionary<string, ReliefGrid> _drafts = new();
    Ground? _ground;

    public ReliefCache(Assets assets, Func<IReadOnlyList<SheetDef>> sheets) { _assets = assets; _sheets = sheets; }

    public event Action<Rect2?>? Changed;
    public int Version { get; private set; }

    public Ground Ground => _ground ??= new Ground(_sheets(), Grid);

    public bool Loading => _loading.Count > 0;

    public ReliefGrid? Grid(SheetDef s)
    {
        if (_drafts.TryGetValue(s.Id, out var draft)) return draft;
        if (s.Relief is not { Length: > 0 } sha) return null;
        if (_grids.TryGetValue(sha, out var g)) return g;
        Request(s, sha);
        return null;
    }

    public ReliefGrid? Loaded(string sha) => _grids.GetValueOrDefault(sha);

    public void Remember(string sha, ReliefGrid g) { _grids[sha] = g; _failed.Remove(sha); }

    public void Draft(string sheetId, ReliefGrid? g, Rect2? where = null)
    {
        if (g == null) _drafts.Remove(sheetId);
        else _drafts[sheetId] = g;
        Invalidate(where);
    }

    const int ArrivalsPerRefresh = 16;
    int _waitingToShow;

    void GridArrived()
    {
        if (_loading.Count > 0 && ++_waitingToShow < ArrivalsPerRefresh) return;
        _waitingToShow = 0;
        Invalidate();
    }

    bool _changeDue;
    Rect2? _changedWhere;

    public void Invalidate(Rect2? where = null)
    {
        _ground = null;
        Version++;
        if (_changeDue)
        {
            _changedWhere = where is { } w && _changedWhere is { } had ? had.Merge(w) : null;
            return;
        }
        _changeDue = true;
        _changedWhere = where;
        Callable.From(() => { _changeDue = false; Changed?.Invoke(_changedWhere); }).CallDeferred();
    }

    static IEnumerable<string> Packages(SheetDef s) => new[] { s.Package, UploadAsset.ReliefPackage }.Where(p => p.Length > 0).Distinct();

    public string? Find(SheetDef s, string rel) =>
        Packages(s).Select(p => _assets.Abs(p, rel)).FirstOrDefault(File.Exists)
        ?? (_assets.Store.FindPackageWith(rel) is { } found ? _assets.Abs(found, rel) : null);

    void Fetch(SheetDef s, string rel, Action ready, Action missing)
    {
        var packages = Packages(s).ToList();
        void Try(int i)
        {
            if (i >= packages.Count) { missing(); return; }
            _assets.EnsureFile(packages[i], rel, ready, () => Try(i + 1));
        }
        if (Find(s, rel) != null) ready();
        else Try(0);
    }

    void Request(SheetDef s, string sha)
    {
        if (_loading.Contains(sha) || _failed.Contains(sha)) return;
        _loading.Add(sha);
        var sheet = Json.Clone(s);
        void Fail(string why)
        {
            _loading.Remove(sha);
            _failed.Add(sha);
            if (_loading.Count == 0 && _waitingToShow > 0) { _waitingToShow = 0; Invalidate(); }
            GD.PrintErr($"[relief] the relief of {sheet.Name} could not be read: {why}");
        }
        Fetch(sheet, ReliefFiles.HeaderRel(sha), () =>
        {
            ReliefIndex index;
            try { index = ReliefFiles.LoadIndex(Find(sheet, ReliefFiles.HeaderRel(sha))!); }
            catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException) { Fail(e.Message); return; }
            var chunks = index.Heights.Concat(index.Ground).Where(c => c.Length > 0).Distinct().Select(ReliefFiles.ChunkRel).ToList();
            int waiting = chunks.Count;
            bool lost = false;
            void Arrived()
            {
                if (--waiting > 0 || lost) return;
                Task.Run(() =>
                {
                    try
                    {
                        var g = ReliefFiles.Load(index, rel => Find(sheet, rel));
                        Callable.From(() =>
                        {
                            _loading.Remove(sha);
                            _grids[sha] = g;
                            GridArrived();
                        }).CallDeferred();
                    }
                    catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
                    {
                        Callable.From(() => Fail(e.Message)).CallDeferred();
                    }
                });
            }
            if (waiting == 0)
            {
                waiting = 1;
                Arrived();
                return;
            }
            foreach (var rel in chunks) Fetch(sheet, rel, Arrived, () => { if (!lost) { lost = true; Fail("a chunk is missing at the host"); } });
        }, () => Fail("the host does not have it"));
    }
}
