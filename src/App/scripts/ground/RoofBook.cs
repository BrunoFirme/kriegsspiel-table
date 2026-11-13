using Godot;
using KTable.Core.Model;

namespace KTable.App;

enum Form { Gable, Hip, HalfHip, Pyramid, Flat, Lean, Mansard, Valley, Dome, Cone, Atrium, Court }

enum Cover { Tile, Slate, Thatch, Shingle, Lead, Mud, Lime, Turf, Stone, Copper }

static class RoofBook
{
    static Cover Main(int manner, float pick) => manner switch
    {
        Manners.English => pick < 0.55f ? Cover.Slate : pick < 0.85f ? Cover.Tile : Cover.Thatch,
        Manners.Northern => pick < 0.45f ? Cover.Thatch : pick < 0.88f ? Cover.Shingle : Cover.Turf,
        Manners.Southern => pick < 0.68f ? Cover.Tile : Cover.Lime,
        Manners.Roman => Cover.Tile,
        Manners.Egyptian => pick < 0.72f ? Cover.Mud : Cover.Lime,
        Manners.Tribal => Cover.Thatch,
        _ => pick < 0.68f ? Cover.Tile : pick < 0.8f ? Cover.Slate : Cover.Thatch,
    };

    static Cover Humble(int manner, float pick) => manner switch
    {
        Manners.English or Manners.Continental => pick < 0.45f ? Cover.Thatch : pick < 0.9f ? Cover.Tile : Cover.Shingle,
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
            Manners.English => pick < 0.42f ? Form.Gable : pick < 0.66f ? Form.Hip : shortM >= 8 && pick < 0.9f ? Form.Valley : Form.HalfHip,
            Manners.Roman or Manners.Southern => pick < 0.5f ? Form.Hip : Form.Gable,
            Manners.Northern => pick < 0.75f ? Form.Gable : Form.Hip,
            _ => grand ? pick < 0.6f ? Form.Mansard : Form.Hip : pick < 0.5f ? Form.Gable : pick < 0.72f ? Form.Hip : pick < 0.9f || shortM < 8 ? Form.HalfHip : Form.Mansard,
        };
    }

    public static (Form Form, Cover Cover) Of(string cls, int manner, float shortM, float longM, float pick, float other)
    {
        bool square = shortM / Mathf.Max(longM, 0.01f) > 0.85f, flat = manner is Manners.Egyptian;
        switch (cls)
        {
            case "steeple":
                if (manner is Manners.English or Manners.Southern) return (Form.Flat, Cover.Lead);
                return (manner == Manners.Northern ? pick < 0.6f ? Form.Dome : Form.Pyramid : pick < 0.25f ? Form.Dome : Form.Pyramid, manner == Manners.Northern ? Cover.Shingle : other < 0.5f
                    ? Cover.Slate : Cover.Copper);
            case "tower" or "keep" or "gatehouse" or "watchtower":
                return (Form.Flat, Cover.Lead);
            case "privy" or "shed" or "coop" or "pigsty" or "kennel" or "outshut" or "cartshed" or "taberna":
                return (flat ? Form.Flat : Form.Lean, Humble(manner, pick));
            case "dovecote":
                return (flat ? Form.Dome : square && pick < 0.6f ? Form.Cone : Form.Pyramid, flat ? Cover.Mud : pick < 0.5f ? Cover.Tile : Cover.Slate);
            case "roundhouse" or "greathouse":
                return (Form.Cone, Cover.Thatch);
            case "silo" or "kiln":
                return (Form.Dome, cls == "kiln" ? Cover.Stone : Cover.Mud);
            case "domus":
                return (Form.Atrium, Cover.Tile);
            case "insula" or "cloister" or "villa":
                return (Form.Court, flat ? other < 0.5f ? Cover.Lime : Cover.Mud : manner == Manners.Southern && other < 0.3f ? Cover.Lime : Cover.Tile);
            case "baths":
                return (Form.Dome, Cover.Tile);
            case "mill" when square:
                return (manner == Manners.Northern ? Form.Pyramid : Form.Cone, manner == Manners.Northern ? Cover.Shingle : manner == Manners.Southern ? Cover.Tile : Cover.Slate);
            case "magazine":
                return (Form.Gable, Cover.Stone);
            case "warehouse" or "arsenal" or "markethall" or "basilica":
                return (flat ? Form.Flat : shortM >= 10 && cls != "markethall" && pick < 0.6f ? Form.Valley : pick < 0.5f ? Form.Hip : Form.Gable, cls == "basilica" ? Cover.Tile : Main(manner, other));
            case "barn" or "stable" or "byre" or "granary" or "mill" or "smithy" or "longhouse":
                var humble = Humble(manner, pick);
                return (flat ? Form.Flat : humble == Cover.Thatch ? other < 0.6f ? Form.HalfHip : Form.Hip : other < 0.6f ? Form.Gable : Form.HalfHip, humble);
            case "fisher":
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
