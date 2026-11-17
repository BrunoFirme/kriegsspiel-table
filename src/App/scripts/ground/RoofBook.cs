using Godot;
using KTable.Core.Model;

namespace KTable.App;

enum Form { Gable, Hip, HalfHip, Pyramid, Flat, Lean, Mansard, Valley, Dome, Cone, Atrium, Court, Vault }

enum Cover { Tile, Slate, Thatch, Shingle, Lead, Mud, Lime, Turf, Stone, Copper, Kawara, Tin, Tar, Palm, Plank, Sheet, Pantile, Silt, Felt }

static class RoofBook
{
    const float DomedShare = 0.24f, VaultedShare = 0.3f, Prevailing = 0.86f;

    static Cover Common(int builders, float pick, float town) =>
        town < 0 ? Main(builders, pick) : pick < Prevailing ? Main(builders, town) : Main(builders, (pick - Prevailing) / (1 - Prevailing));

    public static bool Flat(int builders) => builders is Manners.Egyptian or Manners.Persian or Manners.NorthAfrican or Manners.Saracen or Manners.Mongol;

    public static bool Level(Cover cover) => cover is Cover.Mud or Cover.Lime or Cover.Silt or Cover.Tar;

    public static bool Stacked(int builders) => builders is Manners.Continental or Manners.Northern or Manners.American or Manners.Western or Manners.Soviet or Manners.Ottoman;

    public static Cover Main(int manner, float pick) => manner switch
    {
        Manners.Persian => pick < 0.75f ? Cover.Mud : Cover.Lime,
        Manners.Chinese => pick < 0.86f ? Cover.Kawara : Cover.Thatch,
        Manners.Ottoman => pick < 0.88f ? Cover.Pantile : Cover.Stone,
        Manners.American => pick < 0.6f ? Cover.Plank : pick < 0.76f ? Cover.Sheet : pick < 0.88f ? Cover.Slate : Cover.Tin,
        Manners.Japanese => pick < 0.56f ? Cover.Kawara : pick < 0.86f ? Cover.Thatch : Cover.Plank,
        Manners.Western => pick < 0.56f ? Cover.Tile : pick < 0.8f ? Cover.Slate : pick < 0.92f ? Cover.Tar : Cover.Tin,
        Manners.Soviet => pick < 0.36f ? Cover.Plank : pick < 0.64f ? Cover.Thatch : pick < 0.84f ? Cover.Sheet : Cover.Tin,
        Manners.NorthAfrican => pick < 0.82f ? Cover.Lime : Cover.Mud,
        Manners.Pacific => pick < 0.8f ? Cover.Palm : Cover.Tin,
        Manners.Nippon => pick < 0.64f ? Cover.Kawara : pick < 0.8f ? Cover.Tin : pick < 0.92f ? Cover.Plank : Cover.Tar,
        Manners.Northern => pick < 0.5f ? Cover.Plank : pick < 0.86f ? Cover.Thatch : Cover.Turf,
        Manners.Roman => Cover.Tile,
        Manners.Egyptian => pick < 0.8f ? Cover.Silt : Cover.Lime,
        Manners.Tribal => Cover.Thatch,
        Manners.Gothic => pick < 0.18f ? Cover.Thatch : pick < 0.42f ? Cover.Shingle : pick < 0.9f ? Cover.Tile : Cover.Slate,
        Manners.Rus => pick < 0.5f ? Cover.Thatch : pick < 0.9f ? Cover.Plank : Cover.Shingle,
        Manners.Byzantine => pick < 0.9f ? Cover.Tile : Cover.Lime,
        Manners.Saracen => pick < 0.6f ? Cover.Lime : pick < 0.97f ? Cover.Mud : Cover.Pantile,
        Manners.Mongol => Cover.Mud,
        _ => pick < 0.66f ? Cover.Tile : pick < 0.82f ? Cover.Slate : Cover.Thatch,
    };

    static Cover Humble(int manner, float pick) => manner switch
    {
        Manners.Persian => Cover.Mud,
        Manners.NorthAfrican => pick < 0.5f ? Cover.Mud : Cover.Lime,
        Manners.Chinese or Manners.Japanese => Cover.Thatch,
        Manners.Pacific => Cover.Palm,
        Manners.Soviet => pick < 0.5f ? Cover.Thatch : Cover.Plank,
        Manners.American or Manners.Northern => pick < 0.3f ? Cover.Thatch : Cover.Plank,
        Manners.Nippon => pick < 0.5f ? Cover.Tin : Cover.Plank,
        Manners.Western => pick < 0.4f ? Cover.Tin : pick < 0.8f ? Cover.Tile : Cover.Tar,
        Manners.Ottoman => Cover.Pantile,
        Manners.Continental => pick < 0.45f ? Cover.Thatch : pick < 0.9f ? Cover.Tile : Cover.Shingle,
        Manners.Egyptian => Cover.Silt,
        Manners.Tribal => Cover.Thatch,
        Manners.Gothic => pick < 0.7f ? Cover.Thatch : Cover.Shingle,
        Manners.Rus => pick < 0.5f ? Cover.Thatch : Cover.Plank,
        Manners.Saracen => Cover.Mud,
        Manners.Mongol => Cover.Felt,
        _ => Cover.Tile,
    };

    static Form Shaped(int manner, Cover cover, float pick, float shortM, bool grand)
    {
        if (Level(cover)) return Form.Flat;
        if (cover is Cover.Thatch or Cover.Turf or Cover.Palm) return manner == Manners.Pacific ? Form.Gable : pick < 0.55f ? Form.Hip : pick < 0.8f ? Form.HalfHip : Form.Gable;
        return manner switch
        {
            Manners.Chinese or Manners.Japanese or Manners.Nippon => pick < 0.45f ? Form.Hip : pick < 0.85f ? Form.HalfHip : Form.Gable,
            Manners.Ottoman => pick < 0.85f ? Form.Hip : Form.Gable,
            Manners.American => pick < 0.72f ? Form.Gable : pick < 0.9f || !grand ? Form.Hip : Form.Mansard,
            Manners.Soviet or Manners.Pacific => pick < 0.75f ? Form.Gable : Form.Hip,
            Manners.Roman => pick < 0.5f ? Form.Hip : Form.Gable,
            Manners.Northern => pick < 0.75f ? Form.Gable : Form.Hip,
            Manners.Gothic => pick < 0.6f ? Form.Gable : pick < 0.85f || shortM < 8 ? Form.HalfHip : Form.Hip,
            Manners.Rus => pick < 0.85f ? Form.Gable : Form.Hip,
            Manners.Byzantine => pick < 0.6f ? Form.Hip : Form.Gable,
            _ => grand ? pick < 0.6f ? Form.Mansard : Form.Hip : pick < 0.5f ? Form.Gable : pick < 0.72f ? Form.Hip : pick < 0.9f || shortM < 8 ? Form.HalfHip : Form.Mansard,
        };
    }

    static Cover Fine(int builders, float pick) => builders switch
    {
        Manners.Gothic => pick < 0.65f ? Cover.Tile : Cover.Slate,
        Manners.Rus => Cover.Shingle,
        Manners.Northern => pick < 0.5f ? Cover.Copper : Cover.Plank,
        Manners.American or Manners.Western => pick < 0.7f ? Cover.Slate : Cover.Tile,
        Manners.Continental => pick < 0.5f ? Cover.Slate : Cover.Tile,
        _ => Main(builders, pick),
    };

    public static Cover Church(int manner, float pick) =>
        manner switch
        {
            Manners.Northern or Manners.American or Manners.Soviet => Cover.Plank,
            Manners.Persian => Cover.Mud,
            Manners.Egyptian => Cover.Silt,
            Manners.Tribal => Cover.Thatch,
            Manners.Pacific => pick < 0.5f ? Cover.Palm : Cover.Tin,
            Manners.Roman => Cover.Tile,
            Manners.Ottoman => Cover.Lead,
            Manners.NorthAfrican or Manners.Saracen => Cover.Lime,
            Manners.Gothic => pick < 0.5f ? Cover.Lead : Cover.Slate,
            Manners.Rus => Cover.Lead,
            Manners.Byzantine => pick < 0.6f ? Cover.Tile : Cover.Lead,
            Manners.Mongol => Cover.Felt,
            Manners.Chinese or Manners.Japanese or Manners.Nippon => Cover.Kawara,
            _ => pick < 0.6f ? Cover.Tile : Cover.Slate,
        };

    public static Form Fitted(Form form, Cover cover) => cover switch
    {
        Cover.Mud or Cover.Lime or Cover.Silt => form is Form.Flat or Form.Court or Form.Atrium or Form.Dome or Form.Vault ? form : form == Form.Cone ? Form.Dome : Form.Flat,
        Cover.Tar => form is Form.Court ? form : Form.Flat,
        Cover.Thatch or Cover.Turf or Cover.Palm => form switch { Form.Dome => Form.Cone, Form.Mansard or Form.Pyramid or Form.Flat or Form.Vault => Form.Hip, _ => form },
        _ => form == Form.Vault && cover is not (Cover.Tin or Cover.Sheet) ? Form.Gable : form,
    };

    public static (Form Form, Cover Cover) Set(BuiltPainter.Shape s, (Form Form, Cover Cover) ruled)
    {
        if (s.Roof == null && s.Cover == null) return ruled;
        var cover = s.Cover != null && System.Enum.TryParse(s.Cover, true, out Cover covered) ? covered : ruled.Cover;
        var form = s.Roof != null && System.Enum.TryParse(s.Roof, true, out Form formed) ? formed : ruled.Form;
        return (Fitted(form, cover), cover);
    }

    public static (Form Form, Cover Cover) Of(string cls, int builds, int adorns, float shortM, float longM, float pick, float other, float town = -1)
    {
        var (form, cover) = Unfitted(cls, builds, adorns, shortM, longM, pick, other, town);
        return (Fitted(form, cover), cover);
    }

    static Cover Works(int builders, float pick) => builders switch
    {
        Manners.Pacific or Manners.Nippon or Manners.Soviet => pick < 0.7f ? Cover.Tin : Cover.Sheet,
        Manners.NorthAfrican => Cover.Lime,
        Manners.Western => pick < 0.5f ? Cover.Slate : pick < 0.8f ? Cover.Tin : Cover.Tar,
        _ => Main(builders, pick),
    };

    static (Form Form, Cover Cover) Monument(string cls) => cls switch
    {
        "rotunda" => (Form.Dome, Cover.Lead),
        "mausoleum" => (Form.Dome, Cover.Stone),
        _ => (Form.Flat, Cover.Stone),
    };

    static (Form Form, Cover Cover) Unfitted(string cls, int builders, int adorners, float shortM, float longM, float pick, float other, float town)
    {
        int manner = Manners.Kin(builders), adorns = Manners.Kin(adorners);
        bool square = shortM / Mathf.Max(longM, 0.01f) > 0.85f, flat = Flat(builders), grey = builders is Manners.Chinese or Manners.Japanese or Manners.Nippon;
        bool modern = Manners.Named(builders).Epoch == Manners.Modern;
        switch (cls)
        {
            case "steeple" when adorners == Manners.Gothic:
                return (Form.Pyramid, other < 0.6f ? Cover.Slate : Cover.Lead);
            case "steeple":
                bool domed = adorners is Manners.Northern or Manners.Soviet, plain = adorns == Manners.Northern && !domed;
                return (domed ? pick < 0.6f ? Form.Dome : Form.Pyramid : plain || pick >= 0.25f ? Form.Pyramid : Form.Dome,
                    adorners == Manners.American ? Cover.Lime : domed || plain ? Cover.Plank : other < 0.5f ? Cover.Slate : Cover.Copper);
            case "keep" when grey:
                return (Form.HalfHip, Cover.Kawara);
            case "watchtower" or "gatehouse" when grey:
                return (Form.Hip, Cover.Kawara);
            case "keep" or "gatehouse" or "watchtower" when builders == Manners.Rus:
                return (Form.Pyramid, Cover.Plank);
            case "tower" or "keep" or "gatehouse" or "watchtower" or Castles.RoundTower:
                return (Form.Flat, flat ? Cover.Mud : Cover.Lead);
            case "garage":
                return (Form.Flat, modern ? Works(builders, other) : Cover.Plank);
            case "bunker":
                return (Form.Flat, Cover.Tar);
            case "bedesten":
                return (Form.Dome, Cover.Lead);
            case "drumtower" or "belltower":
                return (Form.Hip, Cover.Kawara);
            case "rotunda" or "mausoleum" or "arch":
                return Monument(cls);
            case "tomb":
                return flat
                    || builders == Manners.Ottoman ? (Form.Dome, builders == Manners.Ottoman ? Cover.Lead : Cover.Lime) : manner == Manners.Roman ? (Form.Pyramid, Cover.Stone)
                        : (Form.Gable, Cover.Stone);
            case "flats":
                return (Form.Flat, other < 0.6f ? Cover.Tar : Cover.Tin);
            case "barracks" when modern:
                return (Form.Gable, other < 0.75f ? Cover.Tar : Cover.Plank);
            case "terrace":
                return (Form.Gable, builders == Manners.Western ? other < 0.65f ? Cover.Slate : Cover.Tile : Common(builders, other, town));
            case "privy" or "shed" or "coop" or "pigsty" or "kennel" or "outshut" or "cartshed" or "taberna":
                return (flat ? Form.Flat : grey && cls == "shed" ? Form.Hip : Form.Lean, grey && cls == "shed" ? Cover.Kawara : Humble(builders, pick));
            case "dovecote":
                return (flat ? Form.Dome : square && pick < 0.6f ? Form.Cone : Form.Pyramid, flat ? Cover.Mud : pick < 0.5f ? Cover.Tile : Cover.Slate);
            case "greathouse" when builders == Manners.Pacific:
                return (Form.Hip, Cover.Palm);
            case "roundhouse" or "greathouse" when builders == Manners.Mongol:
                return (Form.Cone, Cover.Felt);
            case "roundhouse" or "greathouse":
                return (Form.Cone, manner == Manners.Tribal ? Cover.Thatch : Main(builders, pick));
            case "silo" when modern:
                return (Form.Dome, Cover.Tin);
            case "silo" or "kiln":
                return (Form.Dome, cls == "kiln" ? Cover.Stone : builders == Manners.Egyptian ? Cover.Silt : Cover.Mud);
            case "domus":
                return (Form.Atrium, manner == Manners.Roman && !grey ? Cover.Tile : Main(builders, pick));
            case "insula" or "cloister" or "villa":
                return (Form.Court, flat ? builders == Manners.Egyptian ? Cover.Silt : other < 0.5f ? Cover.Lime : Cover.Mud : manner == Manners.Tribal ? Cover.Thatch : grey ? Cover.Kawara
                    : builders == Manners.Ottoman ? cls == "cloister" ? Cover.Lead : Cover.Pantile : builders == Manners.Rus ? Cover.Plank : builders == Manners.Western
                        && other < 0.4f ? Cover.Slate : Cover.Tile);
            case "baths" when grey:
                return (Form.Hip, Cover.Kawara);
            case "baths":
                return (Form.Dome, builders == Manners.Ottoman ? Cover.Lead : flat ? Cover.Mud : manner == Manners.Tribal ? Cover.Thatch : Cover.Tile);
            case "mill" when square:
                return (manner == Manners.Northern ? Form.Pyramid : Form.Cone, manner == Manners.Northern ? Cover.Plank : Cover.Slate);
            case "magazine":
                return (Form.Gable, Cover.Stone);
            case "warehouse" or "goodsshed" or "arsenal" or "markethall" or "basilica":
                var hall = cls == "basilica" && manner == Manners.Roman ? Cover.Tile : modern ? Works(builders, other) : Main(builders, other);
                return (flat ? Form.Flat : shortM >= 10 && cls != "markethall" && pick < 0.6f ? Form.Valley : pick < 0.5f ? Form.Hip : Form.Gable, hall);
            case "barn" or "stable" or "byre" or "granary" or "mill" or "smithy" or "longhouse":
                var humble = modern && builders is Manners.Soviet or Manners.Western && other < 0.5f ? Cover.Tin : Humble(builders, pick);
                return (flat ? Form.Flat : humble is Cover.Thatch or Cover.Palm ? other < 0.6f ? Form.HalfHip : Form.Hip : other < 0.6f ? Form.Gable : Form.HalfHip, humble);
            case "cabin":
                return (pick < 0.6f ? Form.Gable : Form.Hip, pick < 0.7f ? Cover.Plank : Cover.Thatch);
            case "stonehouse":
                return (pick < 0.6f ? Form.Gable : Form.Hip, Cover.Stone);
            case "townhouse":
                var fine = Fine(builders, pick);
                return (Shaped(builders, fine, other, shortM, true), fine);
            case "fisher" or "hut" or "cottage":
                var poor = Humble(builders, pick);
                return (Shaped(builders, poor, other, shortM, false), poor);
            case "shop" when builders == Manners.American:
                return (Form.Flat, other < 0.6f ? Cover.Tin : Cover.Tar);
            case "public":
                var grand = builders == Manners.Continental ? Cover.Slate : builders == Manners.American ? Cover.Sheet : Main(builders, pick);
                return (Shaped(builders, grand, other, shortM, true), grand);
        }
        var cover = Common(builders, pick, town);
        if (builders == Manners.Persian && square && other < DomedShare) return (Form.Dome, cover);
        if (builders == Manners.Persian && !square && other > 1 - VaultedShare) return (Form.Vault, cover);
        return (Shaped(builders, cover, other, shortM, false), cover);
    }
}
