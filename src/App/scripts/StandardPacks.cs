using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Godot;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

public static class StandardPacks
{
    public static string ShippedRoot => ProjectSettings.GlobalizePath("res://standard");

    public static void Ensure(PackageStore store)
    {
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
