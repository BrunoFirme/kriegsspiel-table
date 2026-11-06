using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Godot;
using KTable.Core;
using KTable.Core.Model;
using KTable.Client.Effects;
using KTable.Client.Scenery;
using Json = KTable.Core.Json;

namespace KTable.App;

public static class FirmeEffects
{
    public const string PackId = EffectKinds.StandardPack;
    public const string PackName = "Firme's Effects and Props";

    public static readonly (string Name, string What, double W, double H, EffectDef Fx, string Era)[] Presets =
    {
        ("Musket volley, standard smoke", "A battalion's line firing at will: lay it along the front rank", 6, 0.6, new EffectDef { Kind = "powder" }, N),
        ("Musket volley, dense smoke", "A line firing at will, its smoke thick and hanging, drifting downwind as far as real powder smoke on the map's scale", 6, 0.6, new EffectDef { Kind = "powder", Lingers = true, Amount = 1.5, Wind = 1.4 }, N),
        ("Battalion volley, standard smoke", "The whole line firing on the word of command, every few seconds", 6, 0.6, new EffectDef { Kind = "powder", Volley = "together" }, N),
        ("Battalion volley, dense smoke", "Volleys on the word of command, the smoke banking up and carried off downwind as far as real powder smoke on the map's scale", 6, 0.6, new EffectDef { Kind = "powder", Volley = "together", Lingers = true, Amount = 1.5, Wind = 1.4 }, N),
        ("Rolling fire, standard smoke", "Fire by platoons, rippling down the line from one flank to the other", 6, 0.6, new EffectDef { Kind = "powder", Volley = "rolling" }, N),
        ("Rolling fire, dense smoke", "Fire by platoons down the line, every platoon's smoke hanging and drifting as far as real powder smoke on the map's scale", 6, 0.6, new EffectDef { Kind = "powder", Volley = "rolling", Lingers = true, Amount = 1.5, Wind = 1.4 }, N),
        ("Skirmish fire", "Light troops in open order, a puff here and there", 5, 2, new EffectDef { Kind = "powder", Amount = 0.3, Puff = 0.6, Height = 0.8 }, N),
        ("Battery firing, standard smoke", "A battery of guns firing in turn", 4, 1, new EffectDef { Kind = "cannon", Every = 4, Volley = "rolling" }, N),
        ("Battery firing, dense smoke", "Guns firing in turn, their smoke rolling away downwind as far as real powder smoke on the map's scale", 4, 1, new EffectDef { Kind = "cannon", Every = 4, Volley = "rolling", Lingers = true, Amount = 1.4, Wind = 1.4 }, N),
        ("Battery salvo", "Every gun of the battery at once", 4, 1, new EffectDef { Kind = "cannon", Every = 6 }, N),
        ("Gun firing", "A single piece of artillery", 1, 1, new EffectDef { Kind = "cannon", Every = 6, Amount = 0.8 }, N),
        ("Congreve rockets", "A rocket troop firing: erratic trails of sparks and smoke, bursts where they land", 2, 14, new EffectDef { Kind = "rocket" }, N),
        ("Powder smoke", "The smoke hanging over a line after the firing", 6, 1.5, new EffectDef { Kind = "smoke", Color = "#DEDBD4", Height = 0.35, Puff = 0.9, Amount = 1.2 }, N),
        ("Battle haze", "Smoke lying over the field after hours of fighting", 10, 6, new EffectDef { Kind = "fog", Color = "#D3CFC6" }, N),
        ("Shell burst", "A howitzer shell bursting", 1.5, 1.5, new EffectDef { Kind = "burst", Every = 6 }, N),
        ("Caisson exploding", "An ammunition wagon going up", 3, 3, new EffectDef { Kind = "burst", Every = 12, Amount = 1.6, Height = 1.5, Puff = 1.4 }, N),
        ("Burning village", "Houses alight, black smoke rising high", 5, 5, new EffectDef { Kind = "fire", Amount = 1.4, Height = 1.6 }, N),
        ("Burning farm", "A farmhouse or a barn on fire", 2, 2, new EffectDef { Kind = "fire" }, N),
        ("Lit village", "Windows and lanterns lit at night", 5, 5, new EffectDef { Kind = "lamps", Amount = 1.5 }, N),
        ("Torches", "A few torches: a column on the move by night, a sentry post", 1.5, 0.6, new EffectDef { Kind = "lamps", Amount = 0.5, Puff = 1.4 }, N),
        ("Smouldering ruins", "What is left after the fire", 3, 3, new EffectDef { Kind = "smoke", Color = "#4A4642", Amount = 0.7, Height = 0.8 }, N),
        ("Smoke column", "A fire seen from far off", 2, 2, new EffectDef { Kind = "smoke", Color = "#5A5652", Height = 2.5, Puff = 1.2 }, N),
        ("Cavalry dust", "Horses at the trot or the gallop", 4, 1.5, new EffectDef { Kind = "dust", Amount = 1.2 }, N),
        ("Column dust", "Infantry on the march along a road", 1.2, 6, new EffectDef { Kind = "dust", Amount = 0.8, Puff = 0.8 }, N),
        ("Morning mist", "Ground mist in the low ground at dawn", 12, 12, new EffectDef { Kind = "fog", Color = "#E2E4E6", Amount = 1.2, Wind = 0.4 }, N),
        ("Rain shower", "Rain over the field", 12, 12, new EffectDef { Kind = "rain" }, N),
        ("Heavy rain", "A downpour: wet powder, roads turning to mud", 12, 12, new EffectDef { Kind = "rain", Amount = 2.2, Wind = 1.5 }, N),
        ("Snowfall", "Snow drifting down with the wind", 12, 12, new EffectDef { Kind = "snow" }, N),
        ("Roundshot in water", "A ball striking a river or a pond", 0.8, 0.8, new EffectDef { Kind = "spray", Every = 3.5 }, N),
        ("Embers", "Glowing sparks: a signal fire, a marker", 1, 1, new EffectDef { Kind = "sparks" }, N),
        ("Arrow volley", "A volley falling where you lay it: the shadow of the arrows sweeps in over the back edge, then they come down and stick", 4, 3, new EffectDef { Kind = "arrows" }, A),
        ("Arrows at will", "Archers shooting as fast as they can draw, arrows falling all the time", 4, 3, new EffectDef { Kind = "arrows", Volley = "random" }, A),
        ("Volleys in turn", "Companies of archers loosing one after another along the front", 6, 3, new EffectDef { Kind = "arrows", Volley = "rolling" }, A),
        ("Fire arrows", "A volley of burning arrows, small fires where they land", 4, 3, new EffectDef { Kind = "firearrows" }, A),
        ("Melee", "The press of a fight: dust churned up and clods thrown", 3, 1.5, new EffectDef { Kind = "melee" }, A),
        ("Cavalry melee", "Horsemen at close quarters: thicker dust, thrown higher", 4, 2.5, new EffectDef { Kind = "melee", Amount = 1.5, Height = 1.4, Puff = 1.2 }, A),
        ("Trebuchet stones", "Great stones arcing in over the back edge and crashing down", 3, 3, new EffectDef { Kind = "stones" }, A),
        ("Javelin volley", "Javelins or pila thrown together before the charge, coming in low and sticking where they land", 3, 2, new EffectDef { Kind = "javelins" }, A),
        ("Javelins at will", "Skirmishers throwing as each finds a mark", 3, 2, new EffectDef { Kind = "javelins", Volley = "random" }, A),
        ("Sling stones", "Slingers shooting at will, their shot whipping in flat and kicking up the dirt", 4, 3, new EffectDef { Kind = "slingstones", Volley = "random" }, A),
        ("Sling volley", "A band of slingers loosing together", 4, 3, new EffectDef { Kind = "slingstones" }, A),
        ("Falling rocks", "Rocks dropped from a wall or tumbling off a crag one after another, rolling on towards the front edge", 2, 2, new EffectDef { Kind = "rockfall", Volley = "rolling" }, A),
        ("Arquebus volley", "Arquebusiers firing by ranks, one after another: Nagashino's teppō, a tercio's shot", 4, 0.6, new EffectDef { Kind = "powder", Volley = "rolling", Amount = 0.8, Puff = 0.8 }, A),
        ("Signal beacon", "A beacon fire on a hill or a tower, its smoke standing high", 0.8, 0.8, new EffectDef { Kind = "fire", Amount = 0.7, Height = 2.4 }, A),
        ("Artillery barrage", "Shells landing one after another over the ground", 12, 8, new EffectDef { Kind = "barrage" }, M),
        ("Gas cloud", "A cloud of gas creeping with the wind", 8, 5, new EffectDef { Kind = "fog", Color = "#C9C66A", Amount = 1.3, Wind = 1.2 }, M),
        ("Star shell", "An illuminating flare: it climbs, hangs and sinks, lighting the ground", 6, 6, new EffectDef { Kind = "flare" }, M),
        ("Smoke screen", "A wall of white smoke laid across the front", 10, 1.2, new EffectDef { Kind = "smoke", Color = "#E3E1DC", Height = 0.45, Amount = 2, Puff = 1.3, Wind = 1.2 }, M),
        ("Machine-gun tracers", "Tracer streaming forward off the piece's back edge", 0.6, 14, new EffectDef { Kind = "tracer" }, M),
        ("Signal smoke", "A coloured smoke marker", 0.8, 0.8, new EffectDef { Kind = "smoke", Color = "#D9623A", Height = 2, Puff = 1.1, Amount = 1.5 }, M),
        ("Shell splashes", "A salvo of three heavy shells falling in the sea: lay it where the fall of shot lands", 3, 1.2, new EffectDef { Kind = "column" }, S),
        ("Straddle", "A full salvo straddling a ship: columns on either side of it", 6, 2.5, new EffectDef { Kind = "column", Amount = 3, Every = 8 }, S),
        ("Secondary battery splashes", "Lighter shells falling one after another", 3, 1.5, new EffectDef { Kind = "column", Amount = 2, Height = 0.7, Every = 4, Volley = "rolling" }, S),
        ("Torpedo hit", "One great column of water thrown up against a ship's side", 1.4, 1.4, new EffectDef { Kind = "column", Amount = 0.3, Height = 1.6, Puff = 1.5, Every = 12 }, S),
        ("Main battery salvo", "Every turret firing at once off the piece's front edge", 4, 1.2, new EffectDef { Kind = "broadside", Every = 10 }, S),
        ("Turrets in turn", "Turret after turret firing down the ship", 4, 1.2, new EffectDef { Kind = "broadside", Volley = "rolling" }, S),
        ("Destroyer guns", "A destroyer's lighter guns firing in turn", 2, 0.6, new EffectDef { Kind = "broadside", Amount = 1.4, Puff = 0.7, Every = 5, Volley = "rolling" }, S),
        ("Funnel smoke screen", "Black smoke from dirty fuel burnt in the boilers, laid behind a ship: the front edge is the ship's end", 1.2, 12, new EffectDef { Kind = "screen", Color = "#060504" }, S),
        ("Chemical smoke screen", "White smoke from smoke generators, laid behind a ship: the front edge is the ship's end", 1.2, 12, new EffectDef { Kind = "screen", Color = "#ECEBE6" }, S),
        ("Ship on fire", "A ship burning, its oil sending up a thick black column", 3, 0.6, new EffectDef { Kind = "oil" }, S),
        ("Burning oil slick", "Oil burning on the water round a broken or sunk ship", 5, 3, new EffectDef { Kind = "oil", Amount = 1.3, Height = 0.8 }, S),
        ("Thick smoke column", "A tall, dense black column: a ship burning out on the horizon", 1.5, 1.5, new EffectDef { Kind = "oil", Amount = 1.6, Height = 2.2, Puff = 1.4 }, S),
        ("Torpedo track", "One torpedo running towards the front edge", 0.3, 10, new EffectDef { Kind = "wake" }, S),
        ("Torpedo spread", "A fan of four torpedoes running towards the front edge", 3, 10, new EffectDef { Kind = "wake", Amount = 4 }, S),
        ("Torpedo track, light", "One torpedo running towards the front edge, its foam half see-through", 0.3, 10, new EffectDef { Kind = "wake", Color = "#F1F5F666" }, S),
        ("Torpedo spread, light", "Four torpedoes fanning out, their foam half see-through", 3, 10, new EffectDef { Kind = "wake", Amount = 4, Color = "#F1F5F666" }, S),
        ("Torpedo track, faint", "One torpedo running towards the front edge, only a faint line of foam", 0.3, 10, new EffectDef { Kind = "wake", Color = "#F1F5F62E" }, S),
        ("Torpedo spread, faint", "Four torpedoes fanning out, only faint lines of foam", 3, 10, new EffectDef { Kind = "wake", Amount = 4, Color = "#F1F5F62E" }, S),
    };

    const string N = "napoleonic", M = "modern", A = "ancient", S = "naval", AnyTable = "";
    static readonly string[] RealismTags = { "extra", "realism", "swense" };
    const string StandardSmoke = ", standard smoke";

    public static string IdOf(string presetName) =>
        PackId + "." + PackEdit.Slug(presetName.EndsWith(StandardSmoke, StringComparison.Ordinal) ? presetName[..^StandardSmoke.Length] : presetName);

    public static readonly (string Name, string What, Func<PropMade> Make, string Era)[] PropList =
    {
        ("Bivouac", "Tents round their camp fires", Props.Bivouac, N),
        ("Picket post", "An outpost's shelter and fire", Props.PicketPost, N),
        ("Bivouac fire", "A single camp fire", Props.CampFire, N),
        ("Czech hedgehog", "Steel beams against tanks", Props.Hedgehog, M),
        ("Shell crater", "Ground torn up by a shell", () => Props.Crater(0.35f, 3), M),
        ("Large crater", "A heavy shell's or a mine's crater", () => Props.Crater(0.8f, 7), M),
        ("Trench", "A straight stretch of trench", () => Props.Trench(new[] { new Vector2(-1.5f, 0), new Vector2(1.5f, 0) }), M),
        ("Barricade", "A road blocked with carts and timber", Props.Barricade, N),
        ("Breastwork", "A low wall of earth and timber", Props.Breastwork, N),
        ("Earthwork rampart", "A field work's rampart", Props.Earthwork, N),
        ("Chevaux de frise", "Spiked timbers against horse", Props.ChevauxDeFrise, N),
        ("Stakes", "Sharpened stakes against horse", Props.Stakes, A),
        ("Palisade", "A wall of upright logs", Props.Palisade, A),
        ("Moat", "A ditch before a wall", Props.Moat, A),
        ("Pavises", "Large standing shields", Props.Pavises, A),
        ("Tate shields", "Japanese standing shields", Props.TateShields, A),
        ("Camp curtain", "A commander's cloth screen", Props.CampCurtain, A),
        ("Trench with traverse", "A trench stepping round a traverse",
            () =>
                Props.Trench(new[] { new Vector2(-1.6f, 0.35f), new Vector2(-0.55f, 0.35f), new Vector2(-0.55f, -0.35f), new Vector2(0.55f, -0.35f), new Vector2(0.55f, 0.35f),
                    new Vector2(1.6f, 0.35f) }),
            M),
        ("Box", "A small painted box to keep pieces in", Props.Box, AnyTable),
        ("Tree", "A broad-leaved tree standing on its own", Props.Tree, AnyTable),
        ("Chest", "An iron-bound chest to keep pieces in", Props.Chest, AnyTable),
        ("Crate", "A slatted crate to keep pieces in", Props.Crate, AnyTable),
    };

    const string MarkerWhat = "Marks a torpedo's run; a look for spent", NoteWhat = "A card with a name and a note";

    public static string What(PieceDef d) =>
        Presets.FirstOrDefault(p => IdOf(p.Name) == d.Id).What
        ?? PropList.FirstOrDefault(p => PackId + "." + PackEdit.Slug(p.Name) == d.Id).What
        ?? (d.Tags.Contains("marker") ? MarkerWhat : d.Tags.Contains(NoteCard.Tag) ? NoteWhat : "");

    public static PackManifest Pack(Dictionary<string, byte[]> files)
    {
        var pack = new PackManifest
        {
            Id = PackId, Name = PackName,
            Source = new PackSource { Authors = new() { "Firme" }, License = "standard pack, included with the app", Url = Stamp },
        };
        foreach (var (name, _, w, h, fx, era) in Presets)
        {
            var d = Def(IdOf(name), name, w, h, fx);
            d.Tags.Add(era);
            if (fx.Lingers) d.Tags.AddRange(RealismTags);
            pack.Pieces.Add(d);
        }
        string Add(byte[] bytes, string ext, string kind, int iw = 0, int ih = 0)
        {
            var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var file = $"assets/{sha}.{ext}";
            files[file] = bytes;
            pack.Assets[sha] = new AssetEntry { File = file, Kind = kind, W = iw, H = ih };
            return sha;
        }
        var atlas = Props.Atlas();
        var atlasSha = Add(atlas.SavePngToBuffer(), "png", "image", atlas.GetWidth(), atlas.GetHeight());
        foreach (var (name, _, make, era) in PropList)
        {
            var made = make();
            var b = made.Mesh.Bounds();
            var thumb = Props.Thumb(made.Mesh);
            var d = new PieceDef
            {
                Id = PackId + "." + PackEdit.Slug(name), Name = name, Kind = "model",
                Mesh = Add(Encoding.UTF8.GetBytes(made.Mesh.Obj()), "obj", "mesh"), Texture = atlasSha,
                Image = Add(thumb.SavePngToBuffer(), "png", "image", thumb.GetWidth(), thumb.GetHeight()),
                Size = new Size2(Math.Round(b.Size.X, 3), Math.Round(b.Size.Z, 3)), Shape = "box", Thickness = Math.Round(b.Size.Y, 3),
                Tags = new[] { "prop", era }.Where(t => t.Length > 0).ToList(),
            };
            if (made.Fires.Count > 0)
            {
                double s = Math.Clamp(Math.Sqrt(d.Size.W * d.Size.H), 0.4, 40);
                d.Effect = new EffectDef
                {
                    Kind = "camp", Puff = Math.Round(0.2 / (0.05 * s), 3), Amount = 1.4,
                    At = made.Fires.SelectMany(f => new[] { Math.Round(-f.X, 3), Math.Round(-f.Y, 3) }).ToList(),
                };
            }
            pack.Pieces.Add(d);
        }
        foreach (var m in NavalMarkers.All())
        {
            string Png(Image img) => Add(img.SavePngToBuffer(), "png", "image", img.GetWidth(), img.GetHeight());
            var looks = m.Looks.Select(l => new PieceStateDef { Name = l.Name, Image = Png(l.Face) }).ToList();
            pack.Pieces.Add(new PieceDef
            {
                Id = PackId + "." + PackEdit.Slug(m.Name), Name = m.Name, Kind = "flat", Image = looks[0].Image, States = looks,
                Size = new Size2(m.W, m.H), Shape = "box", Thickness = MarkerThickness, Tags = new() { "marker", S },
            });
        }
        var (rgba, cw, ch) = NoteCard.Paint();
        var card = Image.CreateFromData(cw, ch, false, Image.Format.Rgba8, rgba);
        pack.Pieces.Add(new PieceDef
        {
            Id = PackId + "." + PackEdit.Slug(NoteCard.DefaultName), Name = NoteCard.DefaultName, Kind = "flat", Image = Add(card.SavePngToBuffer(), "png", "image", cw, ch),
            Size = new Size2(NoteCard.Width, NoteCard.Height), Shape = "rounded", Thickness = NoteCard.Thickness, Tags = new() { NoteCard.Tag, "prop" },
            TextLines = NoteCard.TextLines(),
        });
        return pack;
    }

    const double MarkerThickness = 0.04;

    public static PieceDef Def(string id, string name, double w, double h, EffectDef fx) => new()
    {
        Id = id, Name = name, Kind = "flat", Size = new Size2(w, h), Shape = "box", Thickness = 0.02,
        Tags = new() { "effect", fx.Kind }, Effect = fx.Clone(),
    };

    static string Stamp => _stamp ??= "firme:" + PropsVersion + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("|", Presets.Select(p => $"{p.Name};{p.W};{p.H};{p.Era};{Json.Serialize(p.Fx)}")) + "|" + string.Join("|", PropList.Select(p => p.Name + p.Era)))))[..16];
    static string? _stamp;

    const int PropsVersion = 8;
    const string WriteTag = "install";

    public static void Ensure(PackageStore store)
    {
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var dir = store.PackageDir(PackId);
            var path = Path.Combine(dir, "pack.json");
            bool AlreadyInstalled()
            {
                var have = Json.Load<PackManifest>(path);
                return have.Source.Url == Stamp && have.Assets.Values.All(a => File.Exists(Path.Combine(dir, a.File)));
            }
            if (File.Exists(path) && BestEffort.Read(AlreadyInstalled, fallback: false)) return;
            var files = new Dictionary<string, byte[]>();
            var json = Json.Serialize(Pack(files), indented: true);
            foreach (var (rel, bytes) in files)
            {
                var to = Path.Combine(dir, rel);
                if (!File.Exists(to)) Json.WriteAtomic(to, bytes, WriteTag);
            }
            GD.Print($"[standard] {PackId}: {files.Count} prop files ready in {clock.ElapsedMilliseconds} ms");
            if (File.Exists(path) && File.ReadAllText(path) == json) return;
            Json.WriteAtomic(path, new UTF8Encoding(false).GetBytes(json), WriteTag);
            store.Forget(PackId);
            GD.Print($"[standard] installed {PackId}");
        }
        catch (Exception e) { GD.PushError($"standard pack {PackId}: {e.Message}"); }
    }
}
