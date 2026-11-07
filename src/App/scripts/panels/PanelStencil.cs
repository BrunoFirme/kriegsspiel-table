using System.Collections.Generic;
using Godot;

namespace KTable.App;

static class PanelStencil
{
    const float Cut = 0.001f;
    static readonly Dictionary<ulong, Texture2D> Made = new();

    public static Texture2D Hard(Texture2D source)
    {
        var id = source.GetInstanceId();
        if (Made.TryGetValue(id, out var known) && GodotObject.IsInstanceValid(known)) return known;
        if (source.GetImage() is not { } image) return source;
        if (image.IsCompressed() && image.Decompress() != Error.Ok) return source;
        image.Convert(Image.Format.Rgba8);
        for (int y = 0; y < image.GetHeight(); y++)
            for (int x = 0; x < image.GetWidth(); x++)
            {
                var c = image.GetPixel(x, y);
                image.SetPixel(x, y, c with { A = c.A > Cut ? 1 : 0 });
            }
        var hard = ImageTexture.CreateFromImage(image);
        Made[id] = hard;
        return hard;
    }
}
