using System.Text.Json;
using KTable.Core;

namespace KTable.Converter;

public sealed class PackCfg
{
    public string WorkshopId { get; set; } = "";
    public string Id { get; set; } = "";
    public string Short { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class ClockCfg
{
    public int Turn { get; set; }
    public string GameTime { get; set; } = "1800-01-01T06:00";
    public int IncrementMinutes { get; set; } = 15;
}

public sealed class ScenarioCfg
{
    public string Id { get; set; } = "";
    public string Dir { get; set; } = "";
    public string Name { get; set; } = "";
    public string Master { get; set; } = "";
    public string ExtrasPack { get; set; } = "";
    public string ExtrasShort { get; set; } = "extras";
    public ClockCfg Clock { get; set; } = new();
    public double? UnitsPerMeter { get; set; }
}

public sealed class BaseSizes
{
    public double CustomTile { get; set; } = 2.0;
    public double CustomTokenArea { get; set; } = 14.0;
    public double CustomBoard { get; set; } = 16.8345;
    public double BlockSquare { get; set; } = 1.0;
}

public sealed class TilesCfg
{
    public int TileSize { get; set; } = 256;
    public int WebpQuality { get; set; } = 85;
    public double PngMaxMegapixels { get; set; } = 4;
}

public sealed class Config
{
    public string ModsPath { get; set; } = "";
    public string CachePath { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public string SteamHost { get; set; } = "https://steamusercontent-a.akamaihd.net/";
    public int MaxParallelDownloads { get; set; } = 4;
    public List<string> ExcludedWorkshopIds { get; set; } = new();
    public List<PackCfg> Packs { get; set; } = new();
    public ScenarioCfg Scenario { get; set; } = new();
    public BaseSizes BaseSizes { get; set; } = new();
    public List<string> IgnoreTypes { get; set; } = new();
    public List<string> ContainerTypes { get; set; } = new();
    public List<string> IgnoreBundles { get; set; } = new();
    public TilesCfg Tiles { get; set; } = new();

    public bool IsTableWall(string? bundleUrl) => bundleUrl is { } u && IgnoreBundles.Any(x => u.Contains(x, StringComparison.OrdinalIgnoreCase));

    public string WorkshopDir => Path.Combine(ModsPath, "Workshop");
    public string SavesDir
    {
        get
        {
            var beside = Path.Combine(Path.GetDirectoryName(ModsPath.TrimEnd('\\', '/')) ?? ModsPath, "Saves");
            return Directory.Exists(beside) ? beside : ModuleFolders.HomeFolders().Select(h => Path.Combine(h, "Saves")).FirstOrDefault(Directory.Exists) ?? beside;
        }
    }

    public void ResolveModsFolder()
    {
        if (Directory.Exists(ModsPath)) return;
        if (ModuleFolders.MostModules(ModuleFolders.ModsFolders(ModuleFolders.SteamRoot())) is { } found) ModsPath = found;
    }
    public string Cache => Environment.ExpandEnvironmentVariables(CachePath.Length > 0 ? CachePath : DefaultCache);
    static string DefaultCache => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataPaths.Name, "cache", "tts");
    public string Output => OutputPath.Length > 0 ? Environment.ExpandEnvironmentVariables(OutputPath) : PackageStore.DefaultRoot;

    public static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, WriteIndented = true,
    };

    public static Config Load(string? path)
    {
        path ??= new[] { "converter.json", Path.Combine(AppContext.BaseDirectory, "converter.json") }.FirstOrDefault(File.Exists)
                 ?? throw new FileNotFoundException("converter.json not found");
        var c = JsonSerializer.Deserialize<Config>(File.ReadAllText(path), Opts) ?? throw new InvalidDataException(path);
        c.ResolveModsFolder();
        return c;
    }
}
