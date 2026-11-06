using KTable.Core;
using KTable.Core.Model;

namespace KTable.Core.Tests;

public class PackageValidationTests
{
    static PackageStore? Store()
    {
        var root = Environment.GetEnvironmentVariable("KTABLE_VALIDATE_ROOT");
        if (string.IsNullOrEmpty(root)) root = StandardRoot();
        return root != null && Directory.Exists(root) ? new PackageStore(root) : null;
    }

    static string? StandardRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "KriegsspielTable.sln"))) return Path.Combine(d.FullName, "src", "App", "standard");
        return null;
    }

    [Fact]
    public void Every_pack_asset_and_def_reference_resolves()
    {
        var store = Store();
        if (store == null) return;
        var errors = new List<string>();
        foreach (var id in store.ListPackIds())
        {
            var pack = store.Pack(id)!;
            Assert.Equal(id, pack.Id);
            foreach (var (sha, a) in pack.Assets)
            {
                if (!File.Exists(Path.Combine(store.PackageDir(id), a.File))) errors.Add($"{id}: asset file missing {a.File}");
                if (a.Kind == "image" && (a.W <= 0 || a.H <= 0)) errors.Add($"{id}: image {sha} has no size");
            }
            var ids = pack.Pieces.Select(d => d.Id).ToList();
            if (ids.Count != ids.Distinct().Count()) errors.Add($"{id}: duplicate def ids");
            foreach (var d in pack.Pieces)
            {
                foreach (var h in new[] { d.Image, d.Back, d.Mesh, d.Texture }.Concat(d.States.SelectMany(s => new[] { s.Image, s.Back, s.Mesh, s.Texture })))
                    if (h != null && !pack.Assets.ContainsKey(h)) errors.Add($"{id}/{d.Id}: asset {h} not in manifest");
                if (d.Kind == "flat" && d.Image == null && !d.Tags.Contains("effect")) errors.Add($"{id}/{d.Id}: flat def without image");
                if (d.Kind == "model" && d.Mesh == null) errors.Add($"{id}/{d.Id}: model def without mesh");
                if (d.Size.W <= 0 || d.Size.H <= 0) errors.Add($"{id}/{d.Id}: bad size {d.Size}");
                foreach (var a in d.Attachments)
                    if (!ids.Contains(a.Piece)) errors.Add($"{id}/{d.Id}: attachment {a.Piece} not in pack");
                foreach (var c in d.Counters)
                {
                    if (c.Min > c.Max) errors.Add($"{id}/{d.Id}: counter {c.Name} min > max");
                    if (c.SpawnsMarker != null && !ids.Contains(c.SpawnsMarker)) errors.Add($"{id}/{d.Id}: marker {c.SpawnsMarker} not in pack");
                }
            }
        }
        Assert.True(errors.Count == 0, string.Join("\n", errors.Take(50)));
    }

    [Fact]
    public void Every_scenario_instance_and_sheet_resolves()
    {
        var store = Store();
        if (store == null) return;
        var errors = new List<string>();
        foreach (var (sid, path) in store.ListScenarios())
        {
            var sc = Json.Load<Scenario>(path);
            foreach (var p in sc.Packs)
                if (store.Pack(p) == null) errors.Add($"{sid}: listed pack {p} not found");
            var listed = sc.Packs.ToHashSet();
            var layers = new List<(string name, List<PieceInstance> pieces)> { ("umpire", sc.UmpireBoard) };
            var allIds = new HashSet<string>();
            foreach (var (name, pieces) in layers)
            {
                var layerIds = pieces.Select(p => p.Id).ToHashSet();
                foreach (var p in pieces)
                {
                    if (!allIds.Add(p.Id)) errors.Add($"{sid}/{name}: duplicate instance id {p.Id}");
                    var def = store.Def(p.Def);
                    var pack = store.PackOfDef(p.Def);
                    if (def == null || pack == null) { errors.Add($"{sid}/{name}: {p.Id} def {p.Def} not found"); continue; }
                    if (!listed.Contains(pack.Id)) errors.Add($"{sid}/{name}: {p.Id} def {p.Def} is in unlisted pack {pack.Id}");
                    if (def.States.Count > 0 ? p.State >= def.States.Count : p.State != 0) errors.Add($"{sid}/{name}: {p.Id} state {p.State} out of range");
                    foreach (var c in p.Counters.Keys)
                        if (def.Counters.All(x => x.Name != c)) errors.Add($"{sid}/{name}: {p.Id} counter {c} not on def {def.Id}");
                    foreach (var a in p.Attached)
                        if (!layerIds.Contains(a)) errors.Add($"{sid}/{name}: {p.Id} attached {a} not in layer");
                    if (p.Scale <= 0) errors.Add($"{sid}/{name}: {p.Id} scale {p.Scale}");
                }
            }
            foreach (var s in sc.Sheets)
            {
                var dir = store.TilesDir(s);
                var idxPath = Path.Combine(dir, "index.json");
                if (!File.Exists(idxPath)) { errors.Add($"{sid}: sheet {s.Id} tiles index missing ({idxPath})"); continue; }
                var idx = Json.Load<TileIndex>(idxPath);
                if (idx.Levels.Count == 0) errors.Add($"{sid}: sheet {s.Id} has no levels");
                foreach (var l in idx.Levels)
                    foreach (var (x, y) in new[] { (0, 0), (l.Cols - 1, l.Rows - 1) })
                        if (!File.Exists(Path.Combine(dir, l.Level.ToString(), $"{x}_{y}.{idx.Ext}"))) errors.Add($"{sid}: sheet {s.Id} tile {l.Level}/{x}_{y} missing");
                var last = idx.Levels[^1];
                if (last.W > idx.TileSize || last.H > idx.TileSize) errors.Add($"{sid}: sheet {s.Id} top level larger than one tile");
                if (s.Size.W <= 0 || s.Size.H <= 0) errors.Add($"{sid}: sheet {s.Id} bad size");
            }
        }
        Assert.True(errors.Count == 0, string.Join("\n", errors.Take(50)));
    }
}

public class DamagedPackTests
{
    [Fact]
    public void A_damaged_pack_reads_as_missing_and_says_why()
    {
        var root = Path.Combine(Path.GetTempPath(), "ktable-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var good = CoreSamples.Pack();
            Json.Save(Path.Combine(root, good.Id, "pack.json"), good);
            Directory.CreateDirectory(Path.Combine(root, "cut.short"));
            File.WriteAllText(Path.Combine(root, "cut.short", "pack.json"), "{\"id\":\"cut.short\",\"name\":\"Cut sh");
            Directory.CreateDirectory(Path.Combine(root, "no.pieces"));
            File.WriteAllText(Path.Combine(root, "no.pieces", "pack.json"), "{\"id\":\"no.pieces\",\"pieces\":null}");

            var store = new PackageStore(root);
            Assert.Equal(3, store.ListPackIds().Count());
            Assert.Null(store.Pack("cut.short"));
            Assert.NotNull(store.Problem("cut.short"));
            Assert.Null(store.Pack("no.pieces"));
            Assert.NotNull(store.Problem("no.pieces"));
            store.LoadPacks(store.ListPackIds());
            Assert.NotNull(store.Pack(good.Id));
            Assert.Null(store.Problem(good.Id));
            Assert.NotNull(store.Def(good.Pieces[0].Id));
            Assert.Null(store.Pack("not.here"));
            Assert.Null(store.Problem("not.here"));

            var mended = CoreSamples.Pack();
            mended.Id = "cut.short";
            Json.Save(Path.Combine(root, "cut.short", "pack.json"), mended);
            Assert.NotNull(store.Pack("cut.short"));
            Assert.Null(store.Problem("cut.short"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
