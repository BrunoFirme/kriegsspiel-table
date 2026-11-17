using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Relief;
using KTable.Core.Model;
using Json = KTable.Core.Json;
using KTable.Core.Protocol;

namespace KTable.App;

public sealed class ReliefCache
{
    readonly Assets _assets;
    readonly Func<IReadOnlyList<SheetDef>> _sheets;
    readonly KeptGrids _grids;
    readonly HashSet<string> _loading = new(), _failed = new();
    readonly Dictionary<string, ReliefGrid> _drafts = new();
    Ground? _ground;

    public ReliefCache(Assets assets, Func<IReadOnlyList<SheetDef>> sheets)
    {
        _assets = assets;
        _sheets = sheets;
        _grids = new KeptGrids(() => _sheets().Select(s => s.Relief ?? ""));
    }

    public event Action<Rect2?>? Changed;
    public int Version { get; private set; }

    public Ground Ground => _ground ??= new Ground(_sheets(), Shaped);

    public Func<string, IReadOnlyList<BuiltThing>> Built { get; set; } = _ => [];

    public Func<double> UnitsPerMetre { get; set; } = () => 0;

    readonly Dictionary<string, (ReliefGrid Of, long Key, ReliefGrid Shaped)> _shaped = new();

    public long Works(SheetDef s) => KeyOf(Built(s.Id));

    sealed record Keyed(long Key, int Parts);

    readonly ConditionalWeakTable<BuiltThing, Keyed> _keys = new();

    public long PartsKeyed { get; private set; }

    public int TimesLaid { get; private set; }

    long KeyOf(IReadOnlyList<BuiltThing> things)
    {
        long key = 0;
        foreach (var thing in things)
        {
            if (!_keys.TryGetValue(thing, out var kept) || kept.Parts != thing.Parts.Count)
            {
                kept = new Keyed(Earthworks.Key(thing), thing.Parts.Count);
                _keys.AddOrUpdate(thing, kept);
                PartsKeyed += kept.Parts;
            }
            key = Earthworks.Keyed(key, kept.Key);
        }
        return key;
    }

    readonly Dictionary<string, (long Key, int Cols, int Rows, ReliefGrid Of, Earthworks.Laid? Laid)> _laid = new();

    public IReadOnlyList<BuiltThing> Seen(SheetDef s)
    {
        double perMetre = UnitsPerMetre(), scale = Ground.FullWidth(s) * (s.Relief is { Length: > 0 } ? s.Height : 1);
        return Earthworks.Seen(Grid(s), Built(s.Id), Ground.FullWidth(s), BuiltGeometry.FullHeight(s), perMetre, scale);
    }

    public ReliefGrid? Shaped(SheetDef s)
    {
        if (Grid(s) is not { } grid) return null;
        double perMetre = UnitsPerMetre(), scale = Ground.FullWidth(s) * (s.Relief is { Length: > 0 } ? s.Height : 1);
        if (perMetre <= 0) return grid;
        var things = Built(s.Id);
        long key = KeyOf(things) ^ BitConverter.DoubleToInt64Bits(perMetre) ^ (BitConverter.DoubleToInt64Bits(scale) * 31);
        bool drafted = _drafts.ContainsKey(s.Id) || _previews.ContainsKey(s.Id);
        if (!_laid.TryGetValue(s.Id, out var had) || had.Key != key || had.Cols != grid.Cols || had.Rows != grid.Rows || !drafted && !ReferenceEquals(had.Of, grid))
        {
            _laid[s.Id] = had = (key, grid.Cols, grid.Rows, grid, Earthworks.Lay(grid, things, Ground.FullWidth(s), BuiltGeometry.FullHeight(s), perMetre, scale));
            TimesLaid++;
            _dug.Remove(s.Id);
        }
        if (!drafted && _shaped.TryGetValue(s.Id, out var kept) && ReferenceEquals(kept.Of, grid) && kept.Key == key) return kept.Shaped;
        var dug = _dug.GetValueOrDefault(s.Id);
        if (drafted && dug != null && _shown.TryGetValue(s.Id, out var shown) && ReferenceEquals(shown.Of, grid) && shown.Key == key && dug.Cut.Length == shown.Shaped.H.Length)
        {
            if (!_touched.Remove(s.Id, out var touched)) return shown.Shaped;
            if (touched is { } box)
            {
                Earthworks.Redone(grid, had.Laid, dug, shown.Shaped, box);
                return shown.Shaped;
            }
        }
        var shaped = Earthworks.Shaped(grid, had.Laid, ref dug, drafted, Ground.FullWidth(s), perMetre, scale);
        if (dug == null) _dug.Remove(s.Id);
        else _dug[s.Id] = dug;
        if (!drafted) _shaped[s.Id] = (grid, key, shaped);
        else if (dug != null && !ReferenceEquals(shaped, grid)) _shown[s.Id] = (grid, key, shaped);
        else _shown.Remove(s.Id);
        _touched.Remove(s.Id);
        return shaped;
    }

    readonly Dictionary<string, (ReliefGrid Of, long Key, ReliefGrid Shaped)> _shown = new();
    readonly Dictionary<string, CellBox?> _touched = new();
    readonly Dictionary<string, Rect2?> _stroked = new();
    readonly HashSet<string> _sculpted = new();

    readonly Dictionary<string, Earthworks.Dug> _dug = new();

    public float Lift(SheetDef s) => _dug.GetValueOrDefault(s.Id)?.Lift ?? 0;

    public bool Dug(SheetDef s) => _dug.ContainsKey(s.Id);

    public bool Loading => _loading.Count > 0;

    public ReliefGrid? Grid(SheetDef s) => _previews.TryGetValue(s.Id, out var preview) ? preview : Under(s);

    public ReliefGrid? Under(SheetDef s)
    {
        if (_drafts.TryGetValue(s.Id, out var draft)) return draft;
        if (s.Relief is not { Length: > 0 } sha) return null;
        if (_grids.Get(sha) is { } g) return g;
        Request(s, sha);
        return null;
    }

    public ReliefGrid? Loaded(string sha) => _grids.Get(sha);

    public void Remember(string sha, ReliefGrid g) { _grids.Put(sha, g); _failed.Remove(sha); }

    readonly Dictionary<string, string> _asked = new();

    string? Standing(string sheetId) => _asked.GetValueOrDefault(sheetId) ?? _sheets().FirstOrDefault(s => s.Id == sheetId)?.Relief;

    public bool Stands(string sheetId, ReliefGrid? from) => Standing(sheetId) is { Length: > 0 } sha ? from != null && ReferenceEquals(from, Loaded(sha)) : from == null;

    public string Ask(string sheetId, string sha)
    {
        var on = Standing(sheetId) ?? SetSheetRelief.NoRelief;
        _asked[sheetId] = sha;
        return on;
    }

    public void Answered(string sheetId, string? relief)
    {
        if (_asked.GetValueOrDefault(sheetId) == relief) _asked.Remove(sheetId);
    }

    public bool Refused()
    {
        if (_asked.Count == 0) return false;
        foreach (var sheetId in _asked.Keys) Draft(sheetId, null);
        _asked.Clear();
        return true;
    }

    public bool Drafting => _drafts.Count > 0 || _previews.Count > 0;

    readonly Dictionary<string, ReliefGrid> _previews = new();

    public bool Previewing(string sheetId) => _previews.ContainsKey(sheetId);

    public void Draft(string sheetId, ReliefGrid? g, Rect2? where = null, CellBox? cells = null, bool sculpts = true)
    {
        if (g == null) Forget(sheetId);
        else
        {
            _drafts[sheetId] = g;
            Touch(sheetId, cells);
            _stroked[sheetId] = where is { } area && (!_stroked.TryGetValue(sheetId, out var before) || before != null) ? before?.Merge(area) ?? area : null;
            if (sculpts) _sculpted.Add(sheetId);
        }
        Invalidate(where);
    }

    void Touch(string sheetId, CellBox? cells) =>
        _touched[sheetId] = cells is { } box && (!_touched.TryGetValue(sheetId, out var had) || had != null) ? (had ?? CellBox.None).Union(box) : null;

    readonly Dictionary<string, byte[]> _pending = new();

    public byte[]? Pending(string sheetId) => _pending.GetValueOrDefault(sheetId);

    public void Preview(string sheetId, ReliefGrid g, Rect2? where, CellBox? cells, byte[] pending)
    {
        if (!_previews.TryGetValue(sheetId, out var had) || !ReferenceEquals(had, g)) _shown.Remove(sheetId);
        _previews[sheetId] = g;
        _pending[sheetId] = pending;
        Touch(sheetId, cells);
        Invalidate(where);
    }

    public void Unpreview(string sheetId, ReliefGrid g, Rect2? where)
    {
        if (!_previews.TryGetValue(sheetId, out var shown) || !ReferenceEquals(shown, g)) return;
        _previews.Remove(sheetId);
        _pending.Remove(sheetId);
        _shown.Remove(sheetId);
        _touched.Remove(sheetId);
        Invalidate(where);
    }

    void Forget(string sheetId)
    {
        _drafts.Remove(sheetId);
        _shown.Remove(sheetId);
        _touched.Remove(sheetId);
        _stroked.Remove(sheetId);
        _sculpted.Remove(sheetId);
    }

    public bool Settle(string sheetId, ReliefGrid? landed)
    {
        if (landed == null || !_drafts.TryGetValue(sheetId, out var draft) || !ReferenceEquals(draft, landed)) return false;
        var where = _stroked.GetValueOrDefault(sheetId);
        var sheet = _sheets().FirstOrDefault(s => s.Id == sheetId);
        if (where != null && sheet != null && !_previews.ContainsKey(sheetId)) KeepAsShown(sheet, landed);
        bool asLaid = !_sculpted.Contains(sheetId) || where is { } stroked && sheet != null && !ReadByEarthworks(sheet, landed, stroked);
        if (asLaid && _laid.TryGetValue(sheetId, out var laid)) _laid[sheetId] = laid with { Of = landed };
        Forget(sheetId);
        Invalidate(where);
        return where != null;
    }

    bool ReadByEarthworks(SheetDef sheet, ReliefGrid grid, Rect2 stroked)
    {
        var corners = new[] { stroked.Position, new Vector2(stroked.End.X, stroked.Position.Y), stroked.End, new Vector2(stroked.Position.X, stroked.End.Y) }
            .Select(c => Spot.Of(BuiltGeometry.ToSheet(sheet, Coords.FromWorld(new Vector3(c.X, 0, c.Y))))).ToList();
        Spot low = new(corners.Min(c => c.X), corners.Min(c => c.Y)), high = new(corners.Max(c => c.X), corners.Max(c => c.Y));
        double perMetre = UnitsPerMetre(), cell = Ground.FullWidth(sheet) / Math.Max(1, grid.Cols - 1);
        return Built(sheet.Id).Any(thing => Earthworks.Reads(thing, low, high, perMetre, cell));
    }

    void KeepAsShown(SheetDef sheet, ReliefGrid landed)
    {
        var shaped = Shaped(sheet);
        if (_sculpted.Contains(sheet.Id) || !_shown.TryGetValue(sheet.Id, out var shown) || !ReferenceEquals(shown.Shaped, shaped)) return;
        if (_dug.TryGetValue(sheet.Id, out var dug) && dug.Water == Earthworks.Waters(shown.Shaped)) _shaped[sheet.Id] = (landed, shown.Key, shown.Shaped);
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

    public string? Find(SheetDef s, string rel)
    {
        var file = Packages(s).Select(p => _assets.Abs(p, rel)).FirstOrDefault(File.Exists)
            ?? (_assets.Store.FindPackageWith(rel) is { } found ? _assets.Abs(found, rel) : null);
        if (file != null) KTable.Client.Caches.Seen(file);
        return file;
    }

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
                            _grids.Put(sha, g);
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
