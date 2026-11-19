using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Godot;
using KTable.Core;
using KTable.Core.Model;
using Json = KTable.Core.Json;

namespace KTable.App;

public static class StandardPacks
{
    public static string ShippedRoot => ProjectSettings.GlobalizePath("res://standard");

    public static void Ensure(PackageStore store)
    {
        foreach (var id in MovedKinds.RetiredPacks) Retire(store, id);
        if (!Directory.Exists(ShippedRoot)) return;
        foreach (var dir in Directory.GetDirectories(ShippedRoot))
        {
            var id = Path.GetFileName(dir);
            var src = Path.Combine(dir, "pack.json");
            if (!File.Exists(src)) continue;
            var dst = Path.Combine(store.PackageDir(id), "pack.json");
            if (File.Exists(dst) && Hash(src) == Hash(dst)) continue;
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    var to = Path.Combine(store.PackageDir(id), Path.GetRelativePath(dir, f));
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    File.Copy(f, to, true);
                }
                store.Forget(id);
                GD.Print($"[standard] installed {id}");
            }
            catch (Exception e) { GD.PushError($"standard pack {id}: {e.Message}"); }
        }
        FirmeEffects.Ensure(store);
    }

    static void Retire(PackageStore store, string packId)
    {
        var dir = store.PackageDir(packId);
        if (!IsStandard(packId) || !File.Exists(Path.Combine(dir, "pack.json"))) return;
        if (!BestEffort.DeleteFolder(dir)) return;
        store.Forget(packId);
        GD.Print($"[standard] removed {packId}");
    }

    const string WriteTag = "install";

    public static void Install(PackageStore store, string packId, string stamp, Func<Dictionary<string, byte[]>, PackManifest> build)
    {
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var dir = store.PackageDir(packId);
            var path = Path.Combine(dir, "pack.json");
            bool AlreadyInstalled()
            {
                var have = Json.Load<PackManifest>(path);
                return have.Source.Url == stamp && have.Assets.Values.All(a => File.Exists(Path.Combine(dir, a.File)));
            }
            if (File.Exists(path) && BestEffort.Read(AlreadyInstalled, fallback: false)) return;
            var files = new Dictionary<string, byte[]>();
            var json = Json.Serialize(build(files), indented: true);
            foreach (var (rel, bytes) in files)
            {
                var to = Path.Combine(dir, rel);
                if (!File.Exists(to)) Json.WriteAtomic(to, bytes, WriteTag);
            }
            GD.Print($"[standard] {packId}: {files.Count} files ready in {clock.ElapsedMilliseconds} ms");
            if (File.Exists(path) && File.ReadAllText(path) == json) return;
            Json.WriteAtomic(path, new UTF8Encoding(false).GetBytes(json), WriteTag);
            store.Forget(packId);
            GD.Print($"[standard] installed {packId}");
        }
        catch (Exception e) { GD.PushError($"standard pack {packId}: {e.Message}"); }
    }

    public const string Dice = "std.dice";

    public static readonly string[] Copyable = [FirmeEffects.PackId, Dice];

    public static bool IsStandard(string packId) => packId.StartsWith("std.");

    public static string NameOf(string packId) => packId == FirmeEffects.PackId ? FirmeEffects.PackName : "The standard dice";

    public static string DieWhat(PieceDef d)
    {
        var values = d.Faces.Select(f => f.Value).ToList();
        bool numbered = values.Select((v, i) => v == (i + 1).ToString(CultureInfo.InvariantCulture)).All(x => x);
        return numbered ? "" : string.Join(" · ", values.Distinct());
    }

    public static (PackManifest Pack, PieceDef Def)? Piece(PackageStore store, string defId, string intoPack)
    {
        foreach (var id in Copyable)
            if (id != intoPack && store.Pack(id) is { } pack && pack.Pieces.FirstOrDefault(d => d.Id == defId) is { } def) return (pack, def);
        return null;
    }

    static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
}
