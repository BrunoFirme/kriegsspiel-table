using System.Numerics;
using KTable.Core.Model;

namespace KTable.Client.Effects;

public static class EffectKinds
{
    public const string StandardPack = "std.effects";

    public static readonly (string Id, string Name, string What)[] Kinds =
    {
        ("powder", "Musketry", "Powder smoke along the piece and the flashes of the muskets: at will, by volleys or rolling by platoons"),
        ("cannon", "Cannon", "A shot every few seconds, the flash and a thick cloud thrown forward: all guns together or in turn"),
        ("burst", "Shell burst", "An explosion every few seconds: fire, earth thrown up, smoke"),
        ("fire", "Fire", "Flames, embers and dark smoke; lights the ground"),
        ("lamps", "Lamps", "Lit windows, lanterns or torches: small flickering lights"),
        ("smoke", "Smoke", "A column of smoke rising and spreading"),
        ("dust", "Dust", "Dust raised by marching men and horses, low and drifting"),
        ("fog", "Mist", "Ground mist, battle haze or gas, slow and flat"),
        ("rain", "Rain", "Rain falling over the ground, slanting with the wind"),
        ("snow", "Snow", "Snow falling and drifting with the wind"),
        ("spray", "Splash", "A ball striking water: a spout and spray every few seconds"),
        ("sparks", "Sparks", "Rising glowing motes: a signal, a marker"),
        ("rocket", "Rockets", "Congreve rockets: erratic trails of sparks and smoke flying forward, a burst where they land"),
        ("flare", "Star shell", "A flare that climbs, then hangs and sinks, lighting the ground round it"),
        ("barrage", "Barrage", "Shells bursting over the ground one after another"),
        ("tracer", "Tracers", "Machine-gun tracer streaming forward off the piece"),
        ("column", "Water columns", "Heavy shells falling in the sea: tall columns of water thrown up, a salvo together or one after another"),
        ("broadside", "Naval guns", "Big guns firing off the piece's front: a fireball, a blast ring on the water and a cloud of cordite smoke"),
        ("screen", "Smoke trail", "A dense smoke screen laid by a ship: thick at the front edge, older and higher towards the back, drifting with the wind"),
        ("oil", "Burning oil", "Oil fire: flames low on the water or the deck and a thick black column bending with the wind"),
        ("wake", "Torpedo tracks", "Torpedoes running towards the piece's front: foam tracks and the boil at their heads"),
        ("arrows", "Arrows", "A volley of arrows falling on the piece from its back edge: the shadow of the cloud sweeps over the ground, then the arrows come down and stick where they land; in volleys, in turn along the front or at will"),
        ("firearrows", "Fire arrows", "Burning arrows falling in a volley, small fires where they land"),
        ("melee", "Melee", "The press of a fight: dust churned up and clods thrown"),
        ("stones", "Siege stones", "Stones from catapults or trebuchets arcing in over the back edge and crashing down in dust and splinters"),
    };

    public static bool Fires(EffectDef e) => e.Kind is "cannon" or "burst" or "spray" or "barrage" or "rocket" or "flare" or "column" or "broadside" or "firearrows" or "stones"
        || e.Kind is "powder" or "arrows" && Mode(e) != "random";
    public static bool Burns(string kind) => kind is "fire" or "cannon" or "burst" or "powder" or "sparks" or "lamps" or "camp" or "barrage" or "flare" or "broadside" or "oil" or "firearrows";
    public static bool Smokes(string kind) => kind is "fire" or "cannon" or "burst" or "camp" or "rocket" or "barrage" or "broadside" or "oil" or "firearrows";
    public static bool Volleys(string kind) => kind is "powder" or "cannon" or "column" or "broadside" or "arrows" or "firearrows" or "stones";
    public static bool Lingers(string kind) => kind is "powder" or "cannon";
    public static string Mode(EffectDef e) => e.Volley is "random" or "together" or "rolling" ? e.Volley : e.Kind == "powder" ? "random" : "together";

    public static double Interval(EffectDef e) => e.Every > 0.05 ? e.Every : e.Kind switch
    {
        "cannon" => 5, "burst" => 6, "spray" => 3.5, "powder" => 8, "barrage" => 5, "rocket" => 5, "flare" => 14, "column" => 6, "broadside" => 8,
        "arrows" => 7, "firearrows" => 8, "stones" => 7, _ => 0,
    };

    public static string DefaultColor(string kind) => kind switch
    {
        "powder" => "#ebe8e1",
        "cannon" => "#e6e3dc",
        "fire" => "#3d3936",
        "burst" or "barrage" => "#4d4843",
        "dust" => "#94784f",
        "fog" => "#dcdcd8",
        "spray" => "#e4eef5",
        "sparks" => "#ffbe5c",
        "rain" => "#aab6c2",
        "snow" => "#f4f6f9",
        "lamps" => "#ffb347",
        "camp" => "#45413e",
        "rocket" => "#cfcac2",
        "flare" => "#eef3ff",
        "tracer" => "#ff7a3a",
        "column" => "#eef1f2",
        "broadside" => "#3a2a1e",
        "screen" => "#060504",
        "oil" => "#070606",
        "wake" => "#f1f5f6",
        "arrows" => "#2b241c",
        "firearrows" => "#4a433c",
        "melee" => "#7a6748",
        "stones" => "#857a6b",
        _ => "#8f8b85",
    };

    public static string Recolour(string rgb, string? old) => old is { Length: 9 } ? rgb[..7] + old[7..] : rgb[..7];

    public static EffectDef FromName(string name)
    {
        var kind = OldWarKindOf(name) ?? KindOf(name) ?? "sparks";
        return new EffectDef { Kind = kind, Smoke = kind != "fire" || name.Contains("smoke", StringComparison.OrdinalIgnoreCase) };
    }

    static string? OldWarKindOf(string name)
    {
        var n = name.ToLowerInvariant();
        bool Has(params string[] words) => words.Any(n.Contains);
        return
            Has("fire arrow", "flaming arrow", "burning arrow") ? "firearrows" :
            Has("arrow", "archer", "longbow", "bowmen", "crossbow") ? "arrows" :
            Has("catapult", "trebuchet", "mangonel", "onager", "siege stone") ? "stones" :
            Has("melee", "mêlée", "hand to hand", "hand-to-hand", "clash") ? "melee" : null;
    }

    public static string? KindOf(string name)
    {
        var n = name.ToLowerInvariant();
        bool Has(params string[] words) => words.Any(n.Contains);
        return
            Has("torpedo track", "torpedo wake", "torpedoes running") ? "wake" :
            Has("smoke screen", "smokescreen", "smoke trail") ? "screen" :
            Has("water column", "splashes", "torpedo hit") ? "column" :
            Has("broadside", "turret", "naval gun", "main battery") ? "broadside" :
            Has("burning oil", "oil fire", "oil slick", "ship on fire", "burning ship") ? "oil" :
            Has("rocket", "congreve") ? "rocket" :
            Has("flare", "star shell", "illuminat") ? "flare" :
            Has("tracer") ? "tracer" :
            Has("barrage", "bombard") ? "barrage" :
            Has("magic", "rune", "soul", "ghost", "lightning", "energy") ? "sparks" :
            Has("bomb", "explo", "flash", "shell") ? "burst" :
            Has("musket", "volley", "skirmish") ? "powder" :
            Has("cannon", "gun", "shot") ? "cannon" :
            Has("bivouac", "campfire", "camp fire", "camp") ? "fire" :
            Has("lamp", "lantern", "torch", "candle", "window") ? "lamps" :
            Has("fire", "flame", "blaze", "burn") ? "fire" :
            Has("snow", "blizzard") ? "snow" :
            Has("rain", "drizzle", "downpour") ? "rain" :
            Has("smoke") ? "smoke" :
            Has("dust") ? "dust" :
            Has("fog", "cloud", "mist", "storm", "gas") ? "fog" :
            Has("water", "spray", "wave", "splash") ? "spray" : null;
    }

    public static int Seed(float phase) => (int)(phase * 100000) + 7;

    public static Vector3[] Scatter(Random rng, int n, float hx, float hz, float y)
    {
        int cols = Math.Max(1, (int)Math.Round(Math.Sqrt(n * hx / Math.Max(0.01f, hz))));
        int rows = (n + cols - 1) / cols;
        var pts = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            int cx = i % cols, cz = i / cols;
            float fx = (cx + 0.5f + ((float)rng.NextDouble() - 0.5f) * 0.7f) / cols, fz = (cz + 0.5f + ((float)rng.NextDouble() - 0.5f) * 0.7f) / rows;
            pts[i] = new Vector3((fx * 2 - 1) * hx, (float)rng.NextDouble() * y, (fz * 2 - 1) * hz);
        }
        return pts;
    }
}
