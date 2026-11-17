using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class PieceFinish
{
    const float SharpnessFloor = 2, SharpnessSpan = 6;
    public const float GlassClear = 0.55f;

    public const float Specular = 0.5f;

    public readonly record struct Sheen(float Roughness, float Metallic);

    public static readonly Sheen Matte = new(1, 0), Metal = new(0.3f, 1);
    static readonly Sheen Plastic = new(0.24f, 0), Wood = new(0.5f, 0), Glass = new(0.1f, 0);

    public static readonly ProceduralSkyMaterial Surroundings = new()
    {
        SkyTopColor = new Color(0.5f, 0.53f, 0.58f), SkyHorizonColor = new Color(0.86f, 0.85f, 0.82f),
        GroundHorizonColor = new Color(0.42f, 0.4f, 0.37f), GroundBottomColor = new Color(0.12f, 0.12f, 0.12f),
    };

    public static Sheen Of(string? finish) => finish switch
    {
        Finishes.Plastic => Plastic,
        Finishes.Wood => Wood,
        Finishes.Metal => Metal,
        Finishes.Glass => Glass,
        _ => Matte,
    };

    public static void Wear(StandardMaterial3D m, Sheen s) => Set(m, s.Roughness, s.Metallic, Specular);

    public static void Apply(StandardMaterial3D m, PieceDef def)
    {
        if (Finishes.Code(def.Finish) > 0)
        {
            Wear(m, Of(def.Finish));
            if (def.Finish == Finishes.Cardboard) m.SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled;
            if (def.Finish == Finishes.Glass)
            {
                m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                m.AlbedoColor = m.AlbedoColor with { A = m.AlbedoColor.A * GlassClear };
            }
        }
        if (def.Shine is not { } s) return;
        m.Roughness = Mathf.Clamp(1 - ((float)s.Sharpness - SharpnessFloor) / SharpnessSpan, 0.05f, 1);
        m.MetallicSpecular = Mathf.Clamp((float)s.Strength * 2, 0, 1);
        if (s.Rim <= 0) return;
        m.RimEnabled = true;
        m.Rim = Mathf.Clamp((float)s.Rim, 0, 1);
        m.RimTint = 0.5f;
    }

    public static void Painted(StandardMaterial3D m, Texture2D finishes)
    {
        m.Roughness = 1;
        m.RoughnessTexture = finishes;
        m.RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Red;
        m.Metallic = 1;
        m.MetallicTexture = finishes;
        m.MetallicTextureChannel = BaseMaterial3D.TextureChannel.Green;
        m.MetallicSpecular = Specular;
    }

    public static Color Texel(byte code)
    {
        var s = Of(Finishes.OfCode(code));
        return new Color(s.Roughness, s.Metallic, 0);
    }

    static void Set(StandardMaterial3D m, float roughness, float metal, float specular)
    {
        m.Roughness = roughness;
        m.Metallic = metal;
        m.MetallicSpecular = specular;
    }

    public static Matter? MatterOf(PieceDef? def) => def?.Finish switch
    {
        Finishes.Wood => Matter.Wood,
        Finishes.Cardboard => Matter.Card,
        Finishes.Plastic or Finishes.Metal or Finishes.Glass => Matter.Plastic,
        _ => null,
    };
}
