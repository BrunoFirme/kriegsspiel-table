using System.Text.Json.Nodes;
using KTable.Core;
using SkiaSharp;

namespace KTable.Converter.Tests;

public abstract class ConverterHarness : IDisposable
{
    protected readonly string _root = Path.Combine(Path.GetTempPath(), "ktable-converter-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        BestEffort.DeleteFolder(_root);
    }

    protected string Packages => Path.Combine(_root, "packages");

    protected Config TempConfig()
    {
        var cfgPath = WriteConfig();
        return Config.Load(cfgPath);
    }

    protected string WriteConfig(JsonArray? packs = null)
    {
        var cfg = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "converter.json")))!.AsObject();
        if (packs != null) cfg["packs"] = packs;
        cfg["modsPath"] = Path.Combine(_root, "modules", "Mods");
        cfg["cachePath"] = Path.Combine(_root, "cache");
        cfg["outputPath"] = Packages;
        Directory.CreateDirectory(Path.Combine(_root, "modules", "Mods", "Workshop"));
        var path = Path.Combine(_root, "converter.json");
        File.WriteAllText(path, cfg.ToJsonString());
        return path;
    }

    protected static JsonObject Obj(string type, string nick, double scale, JsonObject? extra = null)
    {
        var o = new JsonObject
        {
            ["Name"] = type, ["GUID"] = Guid.NewGuid().ToString("N")[..6], ["Nickname"] = nick, ["Description"] = "", ["GMNotes"] = "",
            ["Transform"] = new JsonObject { ["posX"] = 0, ["posY"] = 1, ["posZ"] = 0, ["rotX"] = 0, ["rotY"] = 180, ["rotZ"] = 0, ["scaleX"] = scale, ["scaleY"] = 1, ["scaleZ"] = scale },
            ["ColorDiffuse"] = new JsonObject { ["r"] = 0.5, ["g"] = 0.25, ["b"] = 0.75 },
        };
        if (extra != null) foreach (var (k, v) in extra) o[k] = v?.DeepClone();
        return o;
    }

    protected string Module(string name, params JsonObject[] objects)
    {
        var save = new JsonObject { ["SaveName"] = name, ["LuaScript"] = "", ["LuaScriptState"] = "", ["ObjectStates"] = new JsonArray(objects.Select(o => (JsonNode)o).ToArray()) };
        var path = Path.Combine(_root, "modules", name + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, save.ToJsonString());
        return path;
    }

    protected void LocalPicture(Config cfg, string url, int w, int h, Func<int, int, SKColor> colour)
    {
        var images = Path.Combine(cfg.ModsPath, "Images");
        Directory.CreateDirectory(images);
        using var bmp = new SKBitmap(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) bmp.SetPixel(x, y, colour(x, y));
        using var png = bmp.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(images, AssetStore.Key(url) + ".png"), png.ToArray());
    }
}
