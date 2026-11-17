using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class GroundPhotos
{
    const string Folder = "res://textures/ground/";

    public static readonly string[] Layers =
    [
        "cobblestone_floor_001", "cobblestone_floor_03", "monastery_stone_floor", "rock_tile_floor", "dirt", "dirt_floor", "gravel_floor", "brown_mud_02", "grass_path_2", "leafy_grass",
    ];

    sealed record Made(List<Image> Albedo, Godot.Collections.Array<Vector3> Means);

    static Task<Made?>? _making;
    static bool _laid;

    public static readonly ShaderMaterial Material = new() { Shader = SurfaceBook.Shader };

    public static void Start()
    {
        if (_making != null) return;
        var albedo = new List<Image>();
        foreach (var id in Layers)
        {
            if (RaisedTextures.Read(Folder + id + "_diff.jpg") is not { } a)
            {
                _making = Task.FromResult<Made?>(null);
                return;
            }
            albedo.Add(a);
        }
        _making = Task.Run<Made?>(() => Prepared(albedo));
    }

    public static bool Lay()
    {
        if (_laid || _making is not { IsCompleted: true } making) return false;
        _laid = true;
        if (making.Result is not { } made) return false;
        var photos = new Texture2DArray();
        if (photos.CreateFromImages(new Godot.Collections.Array<Image>(made.Albedo)) != Error.Ok) return false;
        Material.SetShaderParameter("photos", photos);
        Material.SetShaderParameter("photo_means", made.Means);
        Material.SetShaderParameter("photo", true);
        return true;
    }

    static Made Prepared(List<Image> albedo)
    {
        var means = new Godot.Collections.Array<Vector3>();
        foreach (var a in albedo) means.Add(RaisedTextures.Mean(a));
        return new Made(RaisedTextures.Packed(albedo), means);
    }
}
