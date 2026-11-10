using System;
using System.Collections.Generic;
using Godot;

namespace KTable.App;

static class PanelFonts
{
    const string Folder = "res://fonts/panels/", Prefix = "Fonts/", Italic = "Italic";
    const int Regular = 400;
    static readonly Dictionary<string, Font?> Loaded = new(StringComparer.OrdinalIgnoreCase);

    static readonly Dictionary<string, (string Upright, string? Slanted)> Files = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bangers"] = ("Bangers-Regular.ttf", null), ["Berkshire_Swash"] = ("BerkshireSwash-Regular.ttf", null), ["Cherry_Cream_Soda"] = ("CherryCreamSoda-Regular.ttf", null),
        ["Crete_Round"] = ("CreteRound-Regular.ttf", "CreteRound-Italic.ttf"), ["Flamenco"] = ("Flamenco-Regular.ttf", null), ["Griffy"] = ("Griffy-Regular.ttf", null),
        ["IBM_Plex_Sans"] = ("IBMPlexSans[wdth,wght].ttf", "IBMPlexSans-Italic[wdth,wght].ttf"), ["Life_Savers"] = ("LifeSavers-Regular.ttf", null),
        ["Lovers_Quarrel"] = ("LoversQuarrel-Regular.ttf", null), ["Montserrat"] = ("Montserrat[wght].ttf", "Montserrat-Italic[wght].ttf"), ["Orbitron"] = ("Orbitron[wght].ttf", null),
        ["Overlock_SC"] = ("OverlockSC-Regular.ttf", null), ["Sancreek"] = ("Sancreek-Regular.ttf", null), ["Shojumaru"] = ("Shojumaru-Regular.ttf", null),
        ["Sonsie_One"] = ("SonsieOne-Regular.ttf", null), ["UnifrakturCook"] = ("UnifrakturCook-Bold.ttf", null),
    };

    static readonly Dictionary<string, string> Statics = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Flamenco-Light"] = "Flamenco-Light.ttf", ["LifeSavers-Bold"] = "LifeSavers-Bold.ttf",
    };

    static readonly (string Name, int Weight)[] Weights =
    {
        ("ExtraLight", 200), ("ExtraBold", 800), ("SemiBold", 600), ("Thin", 100), ("Light", 300), ("Medium", 500), ("Bold", 700), ("Black", 900), ("Regular", Regular),
    };

    public static Font? Find(string name)
    {
        if (Loaded.TryGetValue(name, out var known)) return known;
        var font = Load(name);
        Loaded[name] = font;
        return font;
    }

    static Font? Load(string name)
    {
        var path = name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ? name[Prefix.Length..] : name;
        var slash = path.IndexOf('/');
        if (slash <= 0 || !Files.TryGetValue(path[..slash], out var files)) return null;
        var style = path[(slash + 1)..];
        if (Statics.TryGetValue(style, out var single)) return File(single);
        var dash = style.LastIndexOf('-');
        var tail = dash >= 0 ? style[(dash + 1)..] : "";
        bool slanted = tail.EndsWith(Italic, StringComparison.OrdinalIgnoreCase);
        var weightName = slanted ? tail[..^Italic.Length] : tail;
        int weight = Regular;
        foreach (var (n, w) in Weights)
            if (weightName.Equals(n, StringComparison.OrdinalIgnoreCase))
            {
                weight = w;
                break;
            }
        var file = slanted && files.Slanted != null ? files.Slanted : files.Upright;
        if (File(file) is not FontFile face) return null;
        if (!file.Contains('[')) return face;
        var axes = new Godot.Collections.Dictionary { [TextServerManager.GetPrimaryInterface().NameToTag("wght")] = weight };
        return new FontVariation { BaseFont = face, VariationOpentype = axes };
    }

    static Font? File(string file)
    {
        var path = Folder + file;
        if (!ResourceLoader.Exists(path)) return null;
        try
        {
            return ResourceLoader.Load<FontFile>(path);
        }
        catch (InvalidCastException e)
        {
            GD.PushWarning($"[panels] {path} is not a font: {e.Message}");
            return null;
        }
    }
}
