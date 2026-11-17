using Godot;
using KTable.Core.Model;

namespace KTable.App;

enum Fabric { Plaster, Stone, Brick, Timber, Log, Mud, Plank, Concrete, Lime }

enum Glazing { None, Sash, Small, Arched, Screen, Works, Slit }

readonly record struct RaisedLook(Form Form, Cover Cover, Fabric Fabric, Glazing Glazing, int Storeys, float EavesM, float Pitch, Color Wall, Color Roof, bool Door, float Weather, bool Shop);

static class RaisedLooks
{
    public const float StoreyM = 3, LowStoreyM = 2.4f, PlinthM = 0.45f;
    const float WallPick = 31.7f, StoreyPick = 37.3f, TintPick = 41.9f, RoofTintPick = 43.1f, TownStoreys = 0.55f, WeatherPick = 47.9f, WeatherSpread = 0.5f, ShopPick = 59.3f, ShopShare = 0.5f;

    public static int Aged(string? mapStyle, int manner) => Manners.For(Manners.Age(mapStyle), manner);

    public static RaisedLook Of(BuiltPainter.Shape s, string? mapStyle, float shortM, float longM)
    {
        var key = s.Uv[0] * BuiltCanvas.KeyScale;
        int builders = Aged(mapStyle, s.Built), adorners = Aged(mapStyle, s.Adorned);
        float Chance(float salt) => BuiltCanvas.Chanced(key, salt);
        var (form, cover) = RoofBook.Set(s, s.Class == "church"
            ? (RoofBook.Fitted(Manners.Kin(builders) == Manners.Northern ? Form.Hip : Form.Gable, RoofBook.Church(builders, Chance(BuiltLayer.ChurchPick))),
                RoofBook.Church(builders, Chance(BuiltLayer.ChurchPick)))
            : RoofBook.Of(s.Class, builders, adorners, shortM, longM, Chance(BuiltLayer.FormPick), Chance(BuiltLayer.CoverPick), s.Town));
        var walls = FabricOf(s.Class, builders, cover, Chance(WallPick));
        int storeys = s.Storeys > 0 ? s.Storeys : StoreysOf(s.Class, builders, Chance(StoreyPick), s.Town);
        float eaves = s.Storeys > 0 ? s.Storeys * StoreyM : EavesOf(s.Class, storeys, shortM, longM);
        var glazing = GlazingOf(s.Class, builders, walls);
        if (glazing == Glazing.Slit) storeys = Mathf.Max(1, (int)(eaves / StoreyM) - 1);
        return new RaisedLook(form, cover, walls, glazing, storeys, eaves, PitchOf(cover, builders, s.Class),
            RaisedColours.Wall(walls, builders, Chance(TintPick)), RaisedColours.Roof(cover, Chance(RoofTintPick)), Doored(s.Class), Weathered(s.Class, builders, Chance(WeatherPick)),
            Shopped(s.Class, builders, s.Town, Chance(ShopPick)));
    }

    static bool Shopped(string cls, int builders, float town, float pick) =>
        cls is "shop" or "bakery" or "inn" or "tavern" || cls == "insula" && Manners.Kin(builders) == Manners.Roman || cls == "townhouse" && town >= 0 && pick < ShopShare && Manners.Named(builders).Epoch is Manners.Later or Manners.Modern;

    static float Weathered(string cls, int builders, float pick)
    {
        float age = Manners.Named(builders).Epoch switch { Manners.Ancient => 0.55f, Manners.Medieval => 0.7f, Manners.Modern => 0.35f, _ => 0.5f };
        float use = cls switch
        {
            "hut" or "cottage" or "barn" or "shed" or "byre" or "stable" or "pigsty" or "coop" or "privy" or "outshut" or "cartshed" or "fisher" or "cabin" => 1.25f,
            "townhouse" or "public" or "church" or "steeple" or "inn" or "markethall" or "basilica" or "villa" => 0.75f,
            _ => 1,
        };
        return Mathf.Clamp(age * use + (pick - 0.5f) * WeatherSpread, 0, 1);
    }

    static bool Doored(string cls) => cls is not ("shed" or "privy" or "steeple" or "tower" or "magazine" or "cartshed" or "coop" or "pigsty" or "kennel" or "outshut"
        or "silo" or "kiln" or "watchtower" or "taberna" or "tomb" or "gatehouse" or Castles.RoundTower);

    static Fabric FabricOf(string cls, int builders, Cover cover, float pick)
    {
        int kin = Manners.Kin(builders);
        bool modern = Manners.Named(builders).Epoch == Manners.Modern;
        switch (cls)
        {
            case "church" or "steeple" or "tower" or "keep" or "gatehouse" or "basilica" or "baths" or "tomb" or "magazine" or "stonehouse" or "bedesten" or Castles.RoundTower:
                return builders == Manners.Rus && cls is "keep" or "gatehouse" or "tower" ? Fabric.Log : kin == Manners.Roman && cls is "baths" or "basilica" ? Fabric.Brick
                    : RoofBook.Flat(builders) && cls != "tomb" ? Fabric.Mud : Fabric.Stone;
            case "watchtower" or "drumtower" or "belltower":
                return builders is Manners.Rus or Manners.Northern ? Fabric.Log : RoofBook.Flat(builders) ? Fabric.Mud : Fabric.Stone;
            case "flats" or "bunker" or "silo" when modern:
                return cls == "flats" && builders == Manners.Western && pick < 0.5f ? Fabric.Brick : Fabric.Concrete;
            case "factory" or "powerhouse" or "station" or "dockshed" or "warehouse" or "goodsshed" or "arsenal" or "hangar" or "garage":
                return builders == Manners.Nippon ? Fabric.Concrete : modern || kin == Manners.Continental ? Fabric.Brick : DomesticWalls(builders, pick);
            case "barn" or "stable" or "byre" or "granary" or "shed" or "cartshed" or "outshut" or "coop" or "pigsty" or "kennel" or "privy":
                return RoofBook.Flat(builders) ? Fabric.Mud : builders is Manners.Rus or Manners.Northern or Manners.Soviet ? Fabric.Log
                    : builders == Manners.American ? Fabric.Plank : pick < 0.45f ? Fabric.Timber : pick < 0.75f ? Fabric.Plank : Fabric.Stone;
            case "kiln" or "mill":
                return RoofBook.Flat(builders) ? Fabric.Mud : Fabric.Stone;
        }
        return cover is Cover.Mud or Cover.Silt ? Fabric.Mud : DomesticWalls(builders, pick);
    }

    static Fabric DomesticWalls(int builders, float pick) => builders switch
    {
        Manners.Gothic => pick < 0.55f ? Fabric.Timber : pick < 0.8f ? Fabric.Stone : Fabric.Plaster,
        Manners.Continental => pick < 0.5f ? Fabric.Plaster : pick < 0.72f ? Fabric.Stone : pick < 0.88f ? Fabric.Brick : Fabric.Timber,
        Manners.Northern or Manners.Rus => pick < 0.85f ? Fabric.Log : Fabric.Plank,
        Manners.Western => pick < 0.5f ? Fabric.Brick : pick < 0.8f ? Fabric.Plaster : Fabric.Stone,
        Manners.Soviet => pick < 0.5f ? Fabric.Log : pick < 0.8f ? Fabric.Plaster : Fabric.Brick,
        Manners.American => pick < 0.65f ? Fabric.Plank : Fabric.Brick,
        Manners.Roman => pick < 0.6f ? Fabric.Plaster : Fabric.Brick,
        Manners.Byzantine => pick < 0.55f ? Fabric.Brick : Fabric.Stone,
        Manners.Ottoman => pick < 0.6f ? Fabric.Plaster : Fabric.Timber,
        Manners.Chinese or Manners.Japanese => pick < 0.6f ? Fabric.Plaster : Fabric.Plank,
        Manners.Nippon => pick < 0.5f ? Fabric.Plank : pick < 0.8f ? Fabric.Plaster : Fabric.Concrete,
        Manners.Pacific => Fabric.Plank,
        Manners.Tribal => pick < 0.6f ? Fabric.Mud : Fabric.Plank,
        Manners.Persian or Manners.Egyptian or Manners.Mongol => Fabric.Mud,
        Manners.NorthAfrican or Manners.Saracen => pick < 0.6f ? Fabric.Lime : Fabric.Mud,
        _ => Fabric.Plaster,
    };

    static int StoreysOf(string cls, int builders, float pick, float town)
    {
        bool urban = town >= 0, tall = builders is Manners.Gothic or Manners.Continental or Manners.Western or Manners.Ottoman or Manners.Byzantine or Manners.Saracen;
        return cls switch
        {
            "townhouse" => tall ? 3 + (pick < 0.4f ? 1 : 0) : 2 + (pick < 0.5f ? 1 : 0),
            "terrace" => pick < 0.6f ? 2 : 3,
            "flats" => builders == Manners.Soviet ? 5 : builders == Manners.Nippon ? 3 : 4 + (pick < 0.4f ? 1 : 0),
            "insula" => pick < 0.35f ? 2 : pick < 0.88f ? 3 : 4,
            "public" or "inn" or "tavern" or "shop" or "bakery" or "arsenal" or "barracks" or "warehouse" or "goodsshed" => pick < 0.6f ? 2 : 3,
            "house" when urban && tall => pick < TownStoreys ? 2 : 3,
            "house" or "farm" or "villa" => tall || builders is Manners.American or Manners.Nippon or Manners.Rus ? pick < 0.6f ? 2 : 1 : 1,
            _ => 1,
        };
    }

    static float EavesOf(string cls, int storeys, float shortM, float longM) => cls switch
    {
        "steeple" => Mathf.Clamp(longM * 3.2f, 16, 42),
        "tower" or "keep" or Castles.RoundTower => Mathf.Clamp(Mathf.Max(shortM, longM) * 1.6f, 12, 26),
        "watchtower" or "drumtower" or "belltower" => Mathf.Clamp(shortM * 2.2f, 9, 14),
        "gatehouse" => 10,
        "church" => Mathf.Clamp(shortM * 0.9f, 7, 15),
        "basilica" or "markethall" or "baths" or "bedesten" => Mathf.Clamp(shortM * 0.55f, 6, 13),
        "factory" or "hangar" or "dockshed" or "station" => Mathf.Clamp(shortM * 0.35f, 6, 11),
        "powerhouse" => 12,
        "silo" => Mathf.Clamp(shortM * 2.5f, 8, 22),
        "mill" => shortM / Mathf.Max(longM, 0.01f) > 0.85f ? 10 : 7,
        "dovecote" => 5.5f,
        "kiln" => 3.5f,
        "barn" or "stable" or "byre" or "granary" or "longhouse" => 4.2f,
        "shed" or "privy" or "coop" or "pigsty" or "kennel" or "outshut" or "cartshed" or "taberna" or "hut" or "fisher" => LowStoreyM,
        "bunker" => 2.2f,
        "tomb" => Mathf.Clamp(shortM * 0.6f, 3, 8),
        _ => storeys * StoreyM,
    };

    static float PitchOf(Cover cover, int builders, string cls)
    {
        float degrees = cover switch
        {
            Cover.Thatch or Cover.Palm => 50,
            Cover.Shingle => 46,
            Cover.Tile => builders == Manners.Gothic ? 52 : Manners.Kin(builders) == Manners.Roman ? 24 : 40,
            Cover.Slate => cls == "steeple" ? 70 : 38,
            Cover.Plank => builders == Manners.Rus ? 42 : 34,
            Cover.Turf => 32,
            Cover.Kawara => 30,
            Cover.Pantile => 24,
            Cover.Copper or Cover.Lead => cls == "steeple" ? 72 : 32,
            Cover.Tin or Cover.Sheet => 20,
            Cover.Stone => 30,
            Cover.Felt => 28,
            _ => 0,
        };
        return Mathf.DegToRad(degrees);
    }

    static Glazing GlazingOf(string cls, int builders, Fabric walls)
    {
        if (cls is "barn" or "stable" or "byre" or "granary" or "shed" or "privy" or "coop" or "pigsty" or "kennel" or "outshut" or "cartshed" or "silo" or "kiln" or "mill"
            or "dovecote" or "magazine" or "bunker" or "tomb" or "hut") return Glazing.None;
        if (cls is "tower" or "keep" or "gatehouse" or "watchtower" or "steeple" or Castles.RoundTower) return Glazing.Slit;
        if (cls is "factory" or "powerhouse" or "dockshed" or "hangar" or "station" or "warehouse" or "goodsshed") return Glazing.Works;
        return builders switch
        {
            Manners.Chinese or Manners.Japanese or Manners.Nippon when walls != Fabric.Concrete => Glazing.Screen,
            Manners.Tribal or Manners.Mongol or Manners.Pacific => Glazing.None,
            Manners.Gothic or Manners.Northern or Manners.Rus or Manners.Roman or Manners.Persian or Manners.Egyptian => Glazing.Small,
            Manners.Byzantine or Manners.Saracen or Manners.NorthAfrican => Glazing.Arched,
            _ => Glazing.Sash,
        };
    }
}
