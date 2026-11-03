using System;
using System.IO;
using System.Security.Cryptography;
using Godot;
using KTable.Core;

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

    public static bool IsStandard(string packId) => packId.StartsWith("std.");

    static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
}
