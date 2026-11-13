using Godot;
using KTable.Core.Model;

namespace KTable.App;

enum Form { Gable, Hip, HalfHip, Pyramid, Flat, Lean, Mansard, Valley, Dome, Cone, Atrium, Court }

enum Cover { Tile, Slate, Thatch, Shingle, Lead, Mud, Lime, Turf, Stone, Copper }

static class RoofBook
{
    public static Cover Main(int manner, float pick) => manner switch
    {
        Manners.Northern => pick < 0.45f ? Cover.Thatch : pick < 0.88f ? Cover.Shingle : Cover.Turf,
        Manners.Roman => Cover.Tile,
        Manners.Egyptian => pick < 0.72f ? Cover.Mud : Cover.Lime,
        Manners.Tribal => Cover.Thatch,
        _ => pick < 0.68f ? Cover.Tile : pick < 0.8f ? Cover.Slate : Cover.Thatch,
    };

    static Cover Humble(int manner, float pick) => manner switch
    {
        Manners.Continental => pick < 0.45f ? Cover.Thatch : pick < 0.9f ? Cover.Tile : Cover.Shingle,
        Manners.Northern => pick < 0.5f ? Cover.Thatch : Cover.Shingle,
        Manners.Egyptian => Cover.Mud,
        Manners.Tribal => Cover.Thatch,
        _ => Cover.Tile,
    };

    static Form Shaped(int manner, Cover cover, float pick, float shortM, bool grand)
    {
        if (cover is Cover.Mud or Cover.Lime) return Form.Flat;
        if (cover is Cover.Thatch or Cover.Turf) return pick < 0.55f ? Form.Hip : pick < 0.8f ? Form.HalfHip : Form.Gable;
        return manner switch
        {
            Manners.Roman => pick < 0.5f ? Form.Hip : Form.Gable,
            Manners.Northern => pick < 0.75f ? Form.Gable : Form.Hip,
            _ => grand ? pick < 0.6f ? Form.Mansard : Form.Hip : pick < 0.5f ? Form.Gable : pick < 0.72f ? Form.Hip : pick < 0.9f || shortM < 8 ? Form.HalfHip : Form.Mansard,
        };
    }

    public static Cover Church(int manner, float pick) =>
        manner switch { Manners.Northern => Cover.Shingle, Manners.Egyptian => Cover.Mud, Manners.Tribal => Cover.Thatch, Manners.Roman => Cover.Tile, _ => pick < 0.6f ? Cover.Tile : Cover.Slate };

    public static Form Fitted(Form form, Cover cover) => cover switch
    {
        Cover.Mud or Cover.Lime => form is Form.Flat or Form.Court or Form.Atrium or Form.Dome ? form : form == Form.Cone ? Form.Dome : Form.Flat,
        Cover.Thatch or Cover.Turf => form switch { Form.Dome => Form.Cone, Form.Mansard or Form.Pyramid or Form.Flat => Form.Hip, _ => form },
        _ => form,
    };

    public static (Form Form, Cover Cover) Of(string cls, int builds, int adorns, float shortM, float longM, float pick, float other)
    {
        var (form, cover) = Unfitted(cls, builds, adorns, shortM, longM, pick, other);
        return (Fitted(form, cover), cover);
    }

    static (Form Form, Cover Cover) Unfitted(string cls, int manner, int adorns, float shortM, float longM, float pick, float other)
    {
        bool square = shortM / Mathf.Max(longM, 0.01f) > 0.85f, flat = manner is Manners.Egyptian;
        switch (cls)
        {
            case "steeple":
                return (adorns == Manners.Northern ? pick < 0.6f ? Form.Dome : Form.Pyramid : pick < 0.25f ? Form.Dome : Form.Pyramid, adorns == Manners.Northern ? Cover.Shingle : other < 0.5f
                    ? Cover.Slate : Cover.Copper);
            case "tower" or "keep" or "gatehouse" or "watchtower":
                return (Form.Flat, Cover.Lead);
            case "privy" or "shed" or "coop" or "pigsty" or "kennel" or "outshut" or "cartshed" or "taberna":
                return (flat ? Form.Flat : Form.Lean, Humble(manner, pick));
            case "dovecote":
                return (flat ? Form.Dome : square && pick < 0.6f ? Form.Cone : Form.Pyramid, flat ? Cover.Mud : pick < 0.5f ? Cover.Tile : Cover.Slate);
            case "roundhouse" or "greathouse":
                return (Form.Cone, manner == Manners.Tribal ? Cover.Thatch : Main(manner, pick));
            case "silo" or "kiln":
                return (Form.Dome, cls == "kiln" ? Cover.Stone : Cover.Mud);
            case "domus":
                return (Form.Atrium, manner == Manners.Roman ? Cover.Tile : Main(manner, pick));
            case "insula" or "cloister" or "villa":
                return (Form.Court, flat ? other < 0.5f ? Cover.Lime : Cover.Mud : manner == Manners.Tribal ? Cover.Thatch : Cover.Tile);
            case "baths":
                return (Form.Dome, flat ? Cover.Mud : manner == Manners.Tribal ? Cover.Thatch : Cover.Tile);
            case "mill" when square:
                return (manner == Manners.Northern ? Form.Pyramid : Form.Cone, manner == Manners.Northern ? Cover.Shingle : Cover.Slate);
            case "magazine":
                return (Form.Gable, Cover.Stone);
            case "warehouse" or "arsenal" or "markethall" or "basilica":
                var hall = cls == "basilica" && manner == Manners.Roman ? Cover.Tile : Main(manner, other);
                return (flat ? Form.Flat : shortM >= 10 && cls != "markethall" && pick < 0.6f ? Form.Valley : pick < 0.5f ? Form.Hip : Form.Gable, hall);
            case "barn" or "stable" or "byre" or "granary" or "mill" or "smithy" or "longhouse":
                var humble = Humble(manner, pick);
                return (flat ? Form.Flat : humble == Cover.Thatch ? other < 0.6f ? Form.HalfHip : Form.Hip : other < 0.6f ? Form.Gable : Form.HalfHip, humble);
            case "cabin":
                return (pick < 0.6f ? Form.Gable : Form.Hip, pick < 0.7f ? Cover.Shingle : Cover.Thatch);
            case "stonehouse":
                return (pick < 0.6f ? Form.Gable : Form.Hip, Cover.Stone);
            case "fisher" or "hut":
                var poor = Humble(manner, pick);
                return (Shaped(manner, poor, other, shortM, false), poor);
            case "public":
                var grand = manner == Manners.Continental ? Cover.Slate : Main(manner, pick);
                return (Shaped(manner, grand, other, shortM, true), grand);
        }
        var cover = Main(manner, pick);
        return (Shaped(manner, cover, other, shortM, false), cover);
    }
}
