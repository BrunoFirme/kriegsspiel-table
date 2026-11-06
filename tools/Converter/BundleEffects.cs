using System.Text.RegularExpressions;
using KTable.Client.Effects;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter;

public sealed class BundleEffects
{
    const double ReachByName = 3;
    const double Digits = 4;

    static readonly Dictionary<string, (string Preset, double Reach)> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["B89A13D24198F5D661BB3327FB2A516BD4B00EE8"] = ("powder-smoke", 6),
        ["936CFA9C80F455F7CD5560613ED189A82476DF17"] = ("powder-smoke", 8),
        ["F00D2016A0B4205FD9CE2CA105D5FD1B1B21C698"] = ("bivouac-fire", 4),
        ["103ED7C68BD6B03789A610904FB2931CABCF86AF"] = ("burning-farm", 2.7),
        ["B2D57713BFFD619F77490690B49B2A9A990CDF9C"] = ("burning-farm", 2),
        ["1743CD5CF4C5925B97A1B71868D931C12C240DEE"] = ("gun-firing", 4),
        ["3259CD2646D7C3C9EC7F1103F80567181AAB20E5"] = ("shell-burst", 4),
        ["AD4FD523D8560A54AC88CA26C716C3FD187134FB"] = ("shell-burst", 2),
        ["A80B03D01C9FED5B2192926BD0FEEDECDC6F267D"] = ("roundshot-in-water", 3),
        ["835F8AF098C7FD1746671D9215DAF89BF2493554"] = ("torches", 3),
        ["B0CFC98AC32260E7DD6B541C0EE3A0AC695E1FDC"] = ("burning-village", 4),
        ["24FCEE3A1444E8547A92A175B79239A9C4359CD3"] = ("burning-village", 3),
        ["EBB552BFB00C5E69E7355FCD1CCA326DBF2C1B99"] = ("burning-village", 3),
        ["A1C785BE57822DE882402288E86870688EE4AA2F"] = ("burning-village", 3),
        ["560653E1E89DE78650125B30EDBFE0C9757ECBE9"] = ("burning-village", 4),
        ["0B3D5F79BFA4C3F4509FA5DD9E2F3B91F4B389A0"] = ("burning-village", 2),
        ["75880F9A59E3C08B51A3B737B62B3D684495FE24"] = ("burning-farm", 2),
        ["B0F3235BDB34CEAFDBAC47823E9094F78304BDAC"] = ("burning-farm", 2),
        ["7FD60F00A58950F4DF5517C3A6800DBF182F9433"] = ("burning-farm", 2),
        ["4BD684A7AB4C90A025C98621E690E23582E92A59"] = ("burning-farm", 2),
        ["57C1819AE1A7EC8C428A88DA749AFD3F71B0C56B"] = ("burning-farm", 2.5),
        ["9D3FD46C05BA806727DF2355BDF22131B862C76C"] = ("burning-farm", 1.5),
        ["D2C93C6379A5D4CE701FA9FB97ADB19DD3225281"] = ("burning-farm", 1),
        ["8EE6C351C97E8C1016A4D610696B3D8E212F3138"] = ("burning-farm", 1),
        ["B815CB61843B16A88FAA665DCFCE825009953284"] = ("burning-farm", 1),
        ["80B7AA8E982CF16D8098FC99030C6E64878853E2"] = ("burning-farm", 3),
        ["517F3625489E7A80F31469D45AD4850D574063B7"] = ("burning-farm", 2),
        ["FF30A7D55B32EA292A5C6104EFC4ECBAC1E7296B"] = ("burning-farm", 2),
        ["C919A48589E0DE463F834855EDDAACBD2CED8E44"] = ("morning-mist", 12),
        ["F61DB8155A9F53B4AE89CD6FFF758F1B9EA9E0E5"] = ("morning-mist", 8),
        ["3C3ADA0562A7796BAC79D849E43C75A722272235"] = ("rain-shower", 4),
        ["C99214EE263A75A5CBDF7194C3DE121FBBC49F53"] = ("rain-shower", 12),
        ["04F7E144E71CBFA242E5C2C0B6CAE40D7AD59DD1"] = ("embers", 3),
        ["079837F7F8F156CF5B79A4DC554973210CD4CBA9"] = ("embers", 3),
        ["6E8EBE0510E96850480FB787A8606F6145B24230"] = ("embers", 1),
        ["0D98971C3BE35FEFB955FA778E13419B6078985F"] = ("embers", 4),
        ["ED410C2D038E3A96FF75EAEF2E4EA477B076F8D6"] = ("embers", 3),
        ["030E20EE8CDFC806EFE88789809827FB49D06AB0"] = ("embers", 3),
    };

    static readonly Dictionary<string, string> PresetOfKind = new()
    {
        ["powder"] = "musket-volley", ["cannon"] = "gun-firing", ["burst"] = "shell-burst", ["fire"] = "burning-farm",
        ["lamps"] = "torches", ["smoke"] = "powder-smoke", ["dust"] = "cavalry-dust", ["fog"] = "battle-haze",
        ["rain"] = "rain-shower", ["snow"] = "snowfall", ["spray"] = "roundshot-in-water", ["sparks"] = "embers",
        ["rocket"] = "congreve-rockets", ["flare"] = "star-shell", ["barrage"] = "artillery-barrage", ["tracer"] = "machine-gun-tracers",
        ["column"] = "shell-splashes", ["broadside"] = "main-battery-salvo", ["screen"] = "chemical-smoke-screen", ["oil"] = "ship-on-fire",
        ["wake"] = "torpedo-track",
    };

    readonly Dictionary<string, PieceDef> _presets;

    BundleEffects(Dictionary<string, PieceDef> presets) => _presets = presets;

    public static BundleEffects Load(string packagesRoot)
    {
        var path = Path.Combine(packagesRoot, EffectKinds.StandardPack, "pack.json");
        var presets = File.Exists(path)
            ? Json.Load<PackManifest>(path).Pieces.Where(p => p.Effect != null).ToDictionary(p => p.Id)
            : new Dictionary<string, PieceDef>();
        return new BundleEffects(presets);
    }

    public bool Installed => _presets.Count > 0;

    public static bool IsEffect(ModuleObj o) => Match(o) != null;

    public (PieceDef Def, double Scale)? For(ModuleObj o)
    {
        if (Match(o) is not { } match) return null;
        var (preset, reach) = match;
        if (!_presets.TryGetValue(EffectKinds.StandardPack + "." + preset, out var def)) return null;
        double across = Math.Sqrt(def.Size.W * def.Size.H);
        double moduleScale = (Math.Abs(o.ScaleX) + Math.Abs(o.ScaleZ)) / 2;
        return across <= 0 ? null : (def, Math.Round(reach * moduleScale / across, (int)Digits));
    }

    static (string Preset, double Reach)? Match(ModuleObj o)
    {
        if (o.BundleUrl is not { } url) return null;
        if (Regex.Match(url, "[0-9A-Fa-f]{40}") is { Success: true } sha && Known.TryGetValue(sha.Value, out var known)) return known;
        var name = o.Nickname.Length > 0 ? o.Nickname : FileWords(url);
        return EffectKinds.KindOf(name) is { } kind && PresetOfKind.TryGetValue(kind, out var preset) ? (preset, ReachByName) : null;
    }

    static string FileWords(string url)
    {
        var file = Uri.UnescapeDataString(url.Split('?')[0].TrimEnd('/'));
        file = file[(file.LastIndexOf('/') + 1)..];
        return Regex.IsMatch(file, "^[0-9A-Fa-f]{40}$") ? "" : Path.GetFileNameWithoutExtension(file);
    }
}
