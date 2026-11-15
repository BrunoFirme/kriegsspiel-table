using Godot;

namespace KTable.App;

static class PieceLights
{
    const float Fade = 1.3f, ShadowBias = 0.01f, NormalBias = 0.4f, PhaseStep = 1.9f;

    public static void Build(PieceNode node)
    {
        if (node.PrintingOnly || node.Ghost || node.IsPart || node.Def?.Lights is not { Count: > 0 } lights) return;
        float scale = (float)node.Piece.Scale;
        for (int i = 0; i < lights.Count; i++)
        {
            var l = lights[i];
            node.AddChild(new EffectLight
            {
                Position = new Vector3((float)l.X, (float)l.Y, (float)l.Z) * scale, LightColor = new Color(l.Color), OmniRange = (float)l.Range * scale,
                OmniAttenuation = Fade, ShadowEnabled = l.Shadows, ShadowBias = ShadowBias * scale, ShadowNormalBias = NormalBias,
                Base = (float)l.Energy * scale * scale, Mode = l.Flickers ? 1 : 0, Phase = i * PhaseStep,
            });
        }
    }

    public static (Transform3D At, ulong Mesh) ModelPose(PieceNode node) =>
        node.Model is { Mesh: { } mesh } model && GodotObject.IsInstanceValid(model) && model.IsInsideTree() ? (model.GlobalTransform, mesh.GetInstanceId()) : (Transform3D.Identity, 0);

    public static Vector3[]? ModelFaces(PieceNode node) => node.Model is { Mesh: { } mesh } model && GodotObject.IsInstanceValid(model) ? mesh.GetFaces() : null;
}
