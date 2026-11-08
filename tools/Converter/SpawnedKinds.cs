using System.Globalization;
using System.Text.Json.Nodes;
using KTable.Core.Model;
using KTable.Server.Scripting.Translate;

namespace KTable.Converter;

public sealed partial class DefFactory
{
    static readonly (string Param, string Field)[] PictureFields = [("image", "ImageURL"), ("image_bottom", "ImageSecondaryURL"), ("image_secondary", "ImageSecondaryURL"), ("image_scalar", "ImageScalar")];
    static readonly (string Param, string Field)[] FlatFields = [("type", "Type"), ("thickness", "Thickness"), ("stackable", "Stackable"), ("merge_distance", "MergeDistancePixels"), ("stand_up", "StandUp")];
    static readonly (string Param, string Field)[] MeshFields =
        [("mesh", "MeshURL"), ("diffuse", "DiffuseURL"), ("normal", "NormalURL"), ("collider", "ColliderURL"), ("convex", "Convex"), ("type", "TypeIndex"), ("material", "MaterialIndex"), ("cast_shadows", "CastShadows")];
    static readonly (string Param, string Field)[] ShaderFields = [("specular_intensity", "SpecularIntensity"), ("specular_sharpness", "SpecularSharpness"), ("fresnel_strength", "FresnelStrength")];
    static readonly (string Param, string Field)[] BundleFields = [("assetbundle", "AssetbundleURL"), ("assetbundle_secondary", "AssetbundleSecondaryURL"), ("type", "TypeIndex"), ("material", "MaterialIndex")];

    PieceDef? SpawnedKind(PackOut pack, SpawnLook look, string where)
    {
        var o = SpawnedObject(look);
        if (!IsPiece(o) && o.StockSides == null && Boxes.For(o) == null)
        {
            _rep.Note($"{where}: a script spawns the tabletop's own {look.Type}, which has no counterpart here, so nothing comes of it");
            return null;
        }
        return DefFor(o, pack)?.Def;
    }

    public static ModuleObj SpawnedObject(SpawnLook look)
    {
        var type = look.Fields.ContainsKey("mesh") ? (look.Type is "Custom_Model" or "Custom_Model_Bag" ? look.Type : "Custom_Model")
            : look.Fields.ContainsKey("assetbundle") ? "Custom_Assetbundle" : look.Type;
        var j = new JsonObject
        {
            ["Name"] = type,
            ["Transform"] = new JsonObject { ["scaleX"] = look.X, ["scaleY"] = look.Y, ["scaleZ"] = look.Z },
        };
        if (!type.StartsWith("Custom_", StringComparison.Ordinal) && type != Figurine) return new ModuleObj(j);
        switch (type)
        {
            case "Custom_Model" or "Custom_Model_Bag":
                var mesh = Filled(look.Fields, MeshFields);
                var shader = Filled(look.Fields, ShaderFields);
                if (shader.Count > 0) mesh["CustomShader"] = shader;
                j["CustomMesh"] = mesh;
                break;
            case "Custom_Assetbundle":
                j["CustomAssetbundle"] = Filled(look.Fields, BundleFields);
                break;
            default:
                var image = Filled(look.Fields, PictureFields);
                var flat = Filled(look.Fields, FlatFields);
                if (type == "Custom_Tile") flat.TryAdd("Stretch", true);
                if (type is "Custom_Tile" or "Custom_Token") flat.TryAdd("Thickness", ModuleObj.PrefabThickness);
                if (type == "Custom_Token") flat.TryAdd("MergeDistancePixels", ModuleObj.PrefabMergeDistance);
                if (flat.Count > 0) image[type == "Custom_Token" ? "CustomToken" : type == "Custom_Dice" ? "CustomDice" : "CustomTile"] = flat;
                j["CustomImage"] = image;
                break;
        }
        return new ModuleObj(j);
    }

    static JsonObject Filled(IReadOnlyDictionary<string, string> fields, (string Param, string Field)[] map)
    {
        var o = new JsonObject();
        foreach (var (param, field) in map)
            if (fields.TryGetValue(param, out var v)) o[field] = Value(v);
        return o;
    }

    static JsonNode Value(string v) =>
        v is "true" or "false" ? JsonValue.Create(v == "true")
        : double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? JsonValue.Create(n)
        : JsonValue.Create(v);
}
