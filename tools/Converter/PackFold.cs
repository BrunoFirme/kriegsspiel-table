using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter;

public static class PackFold
{
    public static void Into(string root, string packId, string extrasId, Scenario scenario, string scenarioDir)
    {
        var mainDir = Path.Combine(root, packId);
        var extrasDir = Path.Combine(root, extrasId);
        var extrasFile = Path.Combine(extrasDir, "pack.json");
        if (File.Exists(extrasFile))
        {
            var main = Json.Load<PackManifest>(Path.Combine(mainDir, "pack.json"));
            var extras = Json.Load<PackManifest>(extrasFile);
            var have = main.Pieces.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            main.Pieces.AddRange(extras.Pieces.Where(p => have.Add(p.Id)));
            foreach (var (sha, asset) in extras.Assets)
            {
                if (main.Assets.ContainsKey(sha)) continue;
                var from = Path.Combine(extrasDir, asset.File);
                var to = Path.Combine(mainDir, asset.File);
                if (!File.Exists(to))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    File.Copy(from, to);
                }
                main.Assets[sha] = asset;
            }
            Json.Save(Path.Combine(mainDir, "pack.json"), main);
            Directory.Delete(extrasDir, recursive: true);
        }
        scenario.Packs.RemoveAll(p => p == extrasId);
        if (!scenario.Packs.Contains(packId)) scenario.Packs.Insert(0, packId);
        Json.Save(Path.Combine(root, "scenarios", scenarioDir, "scenario.json"), scenario);
    }
}
