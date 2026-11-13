using System.Collections.Generic;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class CultureSigns
{
    const float Drawn = 4;
    const string Head = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 18 18\" fill=\"#fff\">", Foot = "</svg>";
    const string Gable = "<path d=\"M2 10 7 3l5 7v6H2z\"/><path d=\"M13 16V8l1.5-6L16 8v8z\"/>";
    const string Dome = "<path d=\"M1 11 6.5 7 12 11v5H1z\"/><path d=\"M13.6 16V9.6h1.8V16z\"/><path d=\"M14.5 2c.3 1.6 2.4 2.6 2.4 4.6a2.4 2.4 0 0 1-4.8 0c0-2 2.1-3 2.4-4.6z\"/>";
    const string Pediment = "<path d=\"M2 7 9 2l7 5z\"/><path d=\"M2 7.6h14V9H2zM3 9.6h1.6V15H3zM6.5 9.6h1.6V15H6.5zM9.9 9.6h1.6V15H9.9zM13.4 9.6H15V15h-1.6zM2 15.2h14v1.3H2z\"/>";
    const string Pylon = "<path d=\"M1 16 2.6 4h5L8 16zM10 16l.4-12h5L17 16z\"/><path d=\"M8 10h2v6H8z\"/>";
    const string RoundHouse = "<path d=\"M1.5 11 9 2l7.5 9z\"/><path d=\"M4 11.6h10V16H4z\"/>";

    static readonly Dictionary<int, Texture2D> Made = new();

    static string Shapes(int manner) => manner switch
    {
        Manners.Northern => Dome,
        Manners.Roman => Pediment,
        Manners.Egyptian => Pylon,
        Manners.Tribal => RoundHouse,
        _ => Gable,
    };

    public static Texture2D Of(int manner)
    {
        manner = Manners.Known(manner);
        if (Made.TryGetValue(manner, out var made)) return made;
        var image = new Image();
        if (image.LoadSvgFromString(Head + Shapes(manner) + Foot, Drawn) != Error.Ok) image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        return Made[manner] = ImageTexture.CreateFromImage(image);
    }
}
