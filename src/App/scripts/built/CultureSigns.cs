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
    const string Stepped = "<path d=\"M1 16v-3h3v-3h3V7h4v3h3v3h3v3z\"/><path d=\"M8 3h2v3H8z\"/>";
    const string Tiered = "<path d=\"M1 9c3 0 5-1 8-4 3 3 5 4 8 4l-1 1.4H2zM4 11h10v1.6H4zM2.5 16c2 0 4-1 6.5-2.6 2.500 1.600 4.500 2.600 6.500 2.600l-1 1H3.500z\"/><path d=\"M8.400 1h1.200v4H8.400z\"/>";
    const string Minaret = "<path d=\"M1 16v-5a5 5 0 0 1 10 0v5z\"/><path d=\"M13.200 16V5l1.300-4 1.300 4v11z\"/><path d=\"M12.600 7.500h3.800v1.200h-3.800z\"/>";
    const string Steeple = "<path d=\"M1 16V9l5-4 5 4v7z\"/><path d=\"M12 16V8h1V5l1.500-4L16 5v3h1v8z\"/><path d=\"M0 13.500h12v1H0z\" fill-opacity=\".001\"/>";
    const string Torii = "<path d=\"M1 3.500c5 1.400 11 1.400 16 0v2c-5 1.200-11 1.200-16 0zM3 7.500h12V9H3zM4.500 5.500h1.800V16H4.500zM11.700 5.500h1.800V16h-1.800z\"/>";
    const string Works = "<path d=\"M1 16V9l4 2.500V9l4 2.500V9l4 2.500V16z\"/><path d=\"M14 16V3h2.500v13z\"/>";
    const string TwinDomes = "<path d=\"M5 3c.3 1.400 2.200 2.300 2.200 4a2.200 2.200 0 0 1-4.400 0c0-1.700 1.900-2.600 2.200-4zM13 5c.2 1.100 1.700 1.800 1.700 3.100a1.700 1.700 0 0 1-3.400 0C11.300 6.800 12.800 6.100 13 5z\"/><path d=\"M1 16v-6h16v6z\"/>";
    const string FlatRoofs = "<path d=\"M1 16V9h7v7zM9 16V6h4v10z\"/><path d=\"M14.500 16V4h2.500v12z\"/><path d=\"M14 3h3.500v1.200H14z\"/>";
    const string Stilts = "<path d=\"M2 9 9 3l7 6v1.500H2z\"/><path d=\"M4 11h10v1.300H4zM4.500 12.300h1.200V16H4.500zM12.300 12.300h1.200V16h-1.200zM8.400 12.300h1.200V16H8.400z\"/>";

    const string Spires = "<path d=\"M1.5 16V7l2-5.5 2 5.5v9zM12.5 16V7l2-5.5 2 5.5v9z\"/><path d=\"M6 16v-6l3-3.5 3 3.5v6h-1.8v-3.2a1.2 1.6 0 0 0-2.4 0V16z\"/>";
    const string Helmet = "<path d=\"M3 16v-6h12v6z\"/><path d=\"M6.6 9.4V7h4.8v2.4z\"/><path d=\"M9 1.6c.9 1.8 3.4 2.5 3.4 4.8H5.6C5.6 4.1 8.1 3.4 9 1.6z\"/><path d=\"M8.6 0h.8v1.4h-.8z\"/>";
    const string Quincunx = "<path d=\"M1 16v-3h16v3z\"/><path d=\"M4.5 12.4a4.5 4.5 0 0 1 9 0z\"/><path d=\"M1 12.4a2 2 0 0 1 4 0zM13 12.4a2 2 0 0 1 4 0z\"/><path d=\"M8.5 3h1v4.4h-1zM7.3 4.2h3.4v.9H7.3z\"/>";
    const string Horseshoe = "<path fill-rule=\"evenodd\" d=\"M2 16V3h14v13zM5.6 16v-5.6c0-2.6 1.6-4.6 3.4-4.6s3.4 2 3.4 4.6V16z\"/>";
    const string Ger = "<path d=\"M1.5 10.6C3 7.2 6 5.4 9 5.4s6 1.8 7.5 5.2z\"/><path d=\"M2.5 11.2h13V16h-13z\"/><path d=\"M7.8 3.6h2.4v1.3H7.8z\"/>";

    static readonly Dictionary<int, Texture2D> Made = new();

    static string Shapes(int manner) => manner switch
    {
        Manners.Northern => Dome,
        Manners.Roman => Pediment,
        Manners.Egyptian => Pylon,
        Manners.Tribal => RoundHouse,
        Manners.Persian => Stepped,
        Manners.Chinese => Tiered,
        Manners.Ottoman => Minaret,
        Manners.American => Steeple,
        Manners.Japanese or Manners.Nippon => Torii,
        Manners.Western => Works,
        Manners.Soviet => TwinDomes,
        Manners.NorthAfrican => FlatRoofs,
        Manners.Pacific => Stilts,
        Manners.Gothic => Spires,
        Manners.Rus => Helmet,
        Manners.Byzantine => Quincunx,
        Manners.Saracen => Horseshoe,
        Manners.Mongol => Ger,
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
