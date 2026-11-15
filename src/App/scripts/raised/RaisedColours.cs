using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class RaisedColours
{
    const float Spread = 0.16f;

    static readonly Color[] Plasters = [new(0.86f, 0.82f, 0.72f), new(0.83f, 0.74f, 0.58f), new(0.8f, 0.7f, 0.62f), new(0.88f, 0.86f, 0.8f), new(0.76f, 0.72f, 0.62f)];
    static readonly Color[] SouthernPlasters = [new(0.86f, 0.72f, 0.54f), new(0.82f, 0.62f, 0.48f), new(0.9f, 0.84f, 0.72f), new(0.78f, 0.66f, 0.5f)];
    static readonly Color[] Stones = [new(0.62f, 0.6f, 0.55f), new(0.7f, 0.64f, 0.53f), new(0.55f, 0.54f, 0.52f), new(0.68f, 0.62f, 0.5f)];
    static readonly Color[] Bricks = [new(0.55f, 0.3f, 0.22f), new(0.6f, 0.36f, 0.26f), new(0.48f, 0.28f, 0.22f), new(0.64f, 0.44f, 0.32f)];
    static readonly Color[] Logs = [new(0.42f, 0.33f, 0.24f), new(0.36f, 0.29f, 0.22f), new(0.48f, 0.38f, 0.27f)];
    static readonly Color[] Muds = [new(0.7f, 0.58f, 0.42f), new(0.66f, 0.52f, 0.38f), new(0.74f, 0.64f, 0.48f)];
    static readonly Color[] Planks = [new(0.44f, 0.38f, 0.31f), new(0.52f, 0.44f, 0.34f), new(0.38f, 0.34f, 0.3f)];
    static readonly Color[] PaintedPlanks = [new(0.86f, 0.84f, 0.78f), new(0.55f, 0.24f, 0.2f), new(0.8f, 0.76f, 0.62f), new(0.5f, 0.48f, 0.42f)];
    static readonly Color[] Concretes = [new(0.6f, 0.6f, 0.58f), new(0.66f, 0.64f, 0.6f), new(0.54f, 0.54f, 0.53f)];
    static readonly Color[] Limes = [new(0.9f, 0.88f, 0.82f), new(0.86f, 0.84f, 0.76f), new(0.92f, 0.9f, 0.86f)];

    public static Color Wall(Fabric walls, int builders, float pick) => walls switch
    {
        Fabric.Plaster or Fabric.Timber => Picked(RoofBook.Flat(builders) || builders is Manners.Roman or Manners.Ottoman or Manners.Byzantine ? SouthernPlasters : Plasters, pick),
        Fabric.Stone => Picked(Stones, pick),
        Fabric.Brick => Picked(Bricks, pick),
        Fabric.Log => Picked(Logs, pick),
        Fabric.Mud => Picked(Muds, pick),
        Fabric.Plank => Picked(builders == Manners.American ? PaintedPlanks : Planks, pick),
        Fabric.Concrete => Picked(Concretes, pick),
        _ => Picked(Limes, pick),
    };

    public static Color Roof(Cover cover, float pick)
    {
        var colour = cover switch
        {
            Cover.Tile => new Color(0.6f, 0.3f, 0.2f).Lerp(new Color(0.48f, 0.3f, 0.24f), pick),
            Cover.Pantile => new Color(0.68f, 0.38f, 0.24f),
            Cover.Slate => new Color(0.3f, 0.32f, 0.35f),
            Cover.Thatch => new Color(0.56f, 0.46f, 0.3f).Lerp(new Color(0.44f, 0.4f, 0.32f), pick),
            Cover.Palm => new Color(0.56f, 0.5f, 0.32f),
            Cover.Shingle => new Color(0.42f, 0.34f, 0.26f).Lerp(new Color(0.4f, 0.38f, 0.34f), pick),
            Cover.Plank => new Color(0.4f, 0.34f, 0.27f),
            Cover.Lead => new Color(0.5f, 0.52f, 0.54f),
            Cover.Copper => new Color(0.38f, 0.56f, 0.48f),
            Cover.Kawara => new Color(0.24f, 0.25f, 0.27f),
            Cover.Tin => new Color(0.52f, 0.52f, 0.5f).Lerp(new Color(0.5f, 0.32f, 0.22f), pick * 0.6f),
            Cover.Sheet => new Color(0.5f, 0.52f, 0.52f),
            Cover.Tar => new Color(0.2f, 0.2f, 0.2f),
            Cover.Turf => new Color(0.32f, 0.38f, 0.22f),
            Cover.Stone => new Color(0.52f, 0.5f, 0.46f),
            Cover.Felt => new Color(0.84f, 0.8f, 0.72f),
            Cover.Mud or Cover.Silt => new Color(0.64f, 0.52f, 0.38f),
            _ => new Color(0.84f, 0.82f, 0.76f),
        };
        return Varied(colour, pick);
    }

    public static readonly Color Timber = new(0.25f, 0.19f, 0.14f);
    public static readonly Color Plinth = new(0.45f, 0.43f, 0.4f);
    public static readonly Color Leaves = new(0.27f, 0.34f, 0.18f);
    public static readonly Color Hedge = new(0.24f, 0.31f, 0.16f);

    static Color Picked(Color[] colours, float pick) => Varied(colours[Mathf.Clamp((int)(pick * colours.Length), 0, colours.Length - 1)], pick * 7.3f % 1);

    static Color Varied(Color colour, float pick)
    {
        float tone = (pick - 0.5f) * Spread;
        return tone > 0 ? colour.Lightened(tone) : colour.Darkened(-tone);
    }
}
