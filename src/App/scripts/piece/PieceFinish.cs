using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class PieceFinish
{
    const float SharpnessFloor = 2, SharpnessSpan = 6, GlassClear = 0.55f;

    public static void Apply(StandardMaterial3D m, PieceDef def)
    {
        switch (def.Finish)
        {
            case Finishes.Plastic:
                Set(m, roughness: 0.45f, metal: 0, specular: 0.5f);
                break;
            case Finishes.Wood:
                Set(m, roughness: 0.8f, metal: 0, specular: 0.3f);
                break;
            case Finishes.Metal:
                Set(m, roughness: 0.35f, metal: 0.75f, specular: 0.7f);
                break;
            case Finishes.Cardboard:
                Set(m, roughness: 1, metal: 0, specular: 0);
                m.SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled;
                break;
            case Finishes.Glass:
                Set(m, roughness: 0.08f, metal: 0, specular: 0.9f);
                m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                m.AlbedoColor = m.AlbedoColor with { A = m.AlbedoColor.A * GlassClear };
                break;
        }
        if (def.Shine is not { } s) return;
        m.Roughness = Mathf.Clamp(1 - ((float)s.Sharpness - SharpnessFloor) / SharpnessSpan, 0.05f, 1);
        m.MetallicSpecular = Mathf.Clamp((float)s.Strength * 2, 0, 1);
        if (s.Rim <= 0) return;
        m.RimEnabled = true;
        m.Rim = Mathf.Clamp((float)s.Rim, 0, 1);
        m.RimTint = 0.5f;
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
