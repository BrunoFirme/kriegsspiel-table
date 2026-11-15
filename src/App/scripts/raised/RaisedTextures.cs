using System.Collections.Generic;
using Godot;

namespace KTable.App;

static class RaisedTextures
{
    const int Side = 512, Samples = 16;
    const string Folder = "res://textures/raised/";

    public static readonly string[] Layers =
    [
        "white_rough_plaster", "castle_brick_01", "red_brick_03", "wood_plank_wall", "clay_plaster", "weathered_plank_siding", "concrete_wall_004", "white_stucco_02",
        "clay_roof_tiles_02", "ceramic_roof_01", "roof_slates_03", "roof_slates_02", "thatch_roof_angled", "reed_roof_04", "reed_roof_03", "grey_roof_tiles",
        "corrugated_iron", "rusty_corrugated_iron", "roof_planks", "red_slate_roof_tiles_01", "weathered_planks", "bark_brown_02",
    ];

    static bool _tried;

    public static void Lay(ShaderMaterial material)
    {
        if (_tried) return;
        _tried = true;
        var albedo = new List<Image>();
        var normal = new List<Image>();
        var means = new Godot.Collections.Array<Vector3>();
        foreach (var id in Layers)
        {
            if (Read(id + "_diff.jpg") is not { } a || Read(id + "_nor.jpg") is not { } n) return;
            albedo.Add(a);
            normal.Add(n);
            means.Add(Mean(a));
        }
        var albedos = new Texture2DArray();
        var normals = new Texture2DArray();
        if (albedos.CreateFromImages(new Godot.Collections.Array<Image>(albedo)) != Error.Ok || normals.CreateFromImages(new Godot.Collections.Array<Image>(normal)) != Error.Ok) return;
        material.SetShaderParameter("albedos", albedos);
        material.SetShaderParameter("normals", normals);
        material.SetShaderParameter("means", means);
        material.SetShaderParameter("textured", true);
    }

    static Image? Read(string file)
    {
        if (!ResourceLoader.Exists(Folder + file) || GD.Load<Texture2D>(Folder + file)?.GetImage() is not { } image) return null;
        if (image.IsCompressed()) image.Decompress();
        image.Convert(Image.Format.Rgb8);
        if (image.GetWidth() != Side || image.GetHeight() != Side) image.Resize(Side, Side, Image.Interpolation.Lanczos);
        image.GenerateMipmaps();
        return image;
    }

    static Vector3 Mean(Image image)
    {
        var sum = Vector3.Zero;
        for (int y = 0; y < Samples; y++)
            for (int x = 0; x < Samples; x++)
            {
                var c = image.GetPixel((x * 2 + 1) * Side / (Samples * 2), (y * 2 + 1) * Side / (Samples * 2)).SrgbToLinear();
                sum += new Vector3(c.R, c.G, c.B);
            }
        return sum / (Samples * Samples);
    }
}
