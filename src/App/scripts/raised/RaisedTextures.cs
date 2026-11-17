using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class RaisedTextures
{
    const int Side = 512, Samples = 16, GrainSide = 128, GrainCells = 32;
    const string Folder = "res://textures/raised/";

    public static readonly string[] Layers =
    [
        "white_rough_plaster", "castle_brick_01", "red_brick_03", "wood_plank_wall", "clay_plaster", "weathered_plank_siding", "concrete_wall_004", "white_stucco_02",
        "clay_roof_tiles_02", "ceramic_roof_01", "roof_slates_03", "roof_slates_02", "thatch_roof_angled", "reed_roof_04", "reed_roof_03", "grey_roof_tiles",
        "corrugated_iron", "rusty_corrugated_iron", "roof_planks", "red_slate_roof_tiles_01", "weathered_planks", "bark_brown_02",
        "worn_plaster_wall", "brick_wall_02", "rustic_stone_wall", "clay_roof_tiles",
    ];

    sealed record Made(List<Image> Albedo, List<Image> Normal, Godot.Collections.Array<Vector3> Means, Image Grain);

    static Task<Made?>? _making;
    static bool _laid;

    public static void Start()
    {
        if (_making != null) return;
        var albedo = new List<Image>();
        var normal = new List<Image>();
        foreach (var id in Layers)
        {
            if (Read(Folder + id + "_diff.jpg") is not { } a || Read(Folder + id + "_nor.jpg") is not { } n)
            {
                _making = Task.FromResult<Made?>(null);
                return;
            }
            albedo.Add(a);
            normal.Add(n);
        }
        _making = Task.Run<Made?>(() => Prepared(albedo, normal));
    }

    public static void Lay(params ShaderMaterial[] materials)
    {
        if (_laid || _making is not { IsCompleted: true } making) return;
        _laid = true;
        if (making.Result is not { } made) return;
        var albedos = new Texture2DArray();
        var normals = new Texture2DArray();
        if (albedos.CreateFromImages(new Godot.Collections.Array<Image>(made.Albedo)) != Error.Ok || normals.CreateFromImages(new Godot.Collections.Array<Image>(made.Normal)) != Error.Ok) return;
        var grain = ImageTexture.CreateFromImage(made.Grain);
        foreach (var material in materials)
        {
            material.SetShaderParameter("albedos", albedos);
            material.SetShaderParameter("normals", normals);
            material.SetShaderParameter("means", made.Means);
            material.SetShaderParameter("grain", grain);
            material.SetShaderParameter("textured", true);
        }
    }

    static Made Prepared(List<Image> albedo, List<Image> normal)
    {
        var means = new Godot.Collections.Array<Vector3>();
        foreach (var a in albedo) means.Add(Mean(a));
        return new Made(Packed(albedo), Packed(normal), means, Grain());
    }

    internal static List<Image> Packed(List<Image> images)
    {
        var packed = new List<Image>(images.Count);
        foreach (var image in images)
        {
            var rgba = (Image)image.Duplicate();
            rgba.Convert(Image.Format.Rgba8);
            if (BlockCompress.Compress(rgba, false) is not { } done) return images;
            packed.Add(done);
        }
        return packed;
    }

    internal static Image? Read(string path)
    {
        if (!ResourceLoader.Exists(path) || GD.Load<Texture2D>(path)?.GetImage() is not { } image) return null;
        if (image.IsCompressed()) image.Decompress();
        image.Convert(Image.Format.Rgb8);
        if (image.GetWidth() != Side || image.GetHeight() != Side) image.Resize(Side, Side, Image.Interpolation.Lanczos);
        image.GenerateMipmaps();
        return image;
    }

    internal static Vector3 Mean(Image image)
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

    static Image Grain()
    {
        var cells = new float[GrainCells * GrainCells];
        for (int i = 0; i < cells.Length; i++) cells[i] = BuiltCanvas.Chanced(new Vector2(i % GrainCells, i / GrainCells), 3.7f);
        var data = new byte[GrainSide * GrainSide];
        float per = GrainSide / (float)GrainCells;
        for (int y = 0; y < GrainSide; y++)
            for (int x = 0; x < GrainSide; x++)
            {
                float fx = x / per, fy = y / per;
                int x0 = (int)fx, y0 = (int)fy;
                float ux = Mathf.SmoothStep(0, 1, fx - x0), uy = Mathf.SmoothStep(0, 1, fy - y0);
                float Cell(int cx, int cy) => cells[(cy % GrainCells) * GrainCells + cx % GrainCells];
                float v = Mathf.Lerp(Mathf.Lerp(Cell(x0, y0), Cell(x0 + 1, y0), ux), Mathf.Lerp(Cell(x0, y0 + 1), Cell(x0 + 1, y0 + 1), ux), uy);
                data[y * GrainSide + x] = (byte)Mathf.Clamp(v * 255, 0, 255);
            }
        var image = Image.CreateFromData(GrainSide, GrainSide, false, Image.Format.R8, data);
        image.GenerateMipmaps();
        return image;
    }
}
