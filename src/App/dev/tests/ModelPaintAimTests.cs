using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class ModelPaintAimTests
{
    const float ViewDistance = 3, BrushReach = 0.2f, VeilSide = 0.5f, FromTheRight = 0.5f, RailLength = 6, RailThickness = 0.1f, Middle = 0.5f;

    static async Task<(ModelStudio m, ModelPart back)> Wall(TestRunner r)
    {
        var h = await StudioTests.Open(r);
        await h.Studio.NewModelPiece("Aimed", ModelImport.Primitive("box"), "box");
        var m = h.Studio.Model!;
        var back = m.Doc.Parts.Single();
        back.Pos = new[] { 0f, 0.5f, -0.6f };
        back.Scl = new[] { 2f, 1f, 0.2f };
        return (m, back);
    }

    static ModelPart Add(ModelStudio m, string name, float[] pos, float[] size)
    {
        m.SelPartId = null;
        m.AddShape("box", name);
        var p = m.SelPart!;
        p.Pos = pos;
        p.Scl = size;
        return p;
    }

    static async Task Look(TestRunner r, ModelStudio m, float yaw)
    {
        m.SelPartId = null;
        m.RefreshSelection();
        await m.Settle();
        m.Camera.Target = m.Stand.Transform * new Vector3(0, Middle, 0);
        m.Camera.Dist = ViewDistance;
        m.Camera.Face(yaw, 0);
        m.SetTool("paint");
        m.Ink = new Color(1, 0, 0);
        await r.Frames(2);
    }

    static void BrushOf(ModelStudio m, float reach) => m.P.ModelBrush = 2 * reach * m.Stand.Scale.X;

    static bool Painted(ModelStudio m, ModelPart p) => m.Views[p.Id].Paint.Any(b => b != 0);

    static bool PaintedAt(ModelStudio m, Vector3 world)
    {
        var hit = m.Tools.Ray(m.Cam.UnprojectPosition(world))!.Value;
        var view = m.Views[hit.part];
        var d = view.Data;
        var uv = d.UV[hit.tri * 3] * (1 - hit.u - hit.v) + d.UV[hit.tri * 3 + 1] * hit.u + d.UV[hit.tri * 3 + 2] * hit.v;
        int o = (Mathf.Clamp((int)(uv.Y * view.H), 0, view.H - 1) * view.W + Mathf.Clamp((int)(uv.X * view.W), 0, view.W - 1)) * 4;
        return view.Paint[o + 3] != 0;
    }

    [ScreenTest]
    static async Task A_brush_aimed_just_past_the_edge_of_a_nearer_shape_paints_the_shape_behind_and_not_the_nearer_one(TestRunner r)
    {
        var (m, back) = await Wall(r);
        var front = Add(m, "Front", new[] { -0.6f, 0.5f, 0.1f }, new[] { 1f, 1f, 1f });
        await Look(r, m, FromTheRight);
        var aim = m.Stand.Transform * new Vector3(-0.12f, Middle, -0.5f);
        var hit = m.Tools.Ray(m.Cam.UnprojectPosition(aim));
        Check.That(hit is { } h && h.part == back.Id, "the point aimed at is on the shape behind, just past the nearer one's edge");
        BrushOf(m, BrushReach);
        m.Tools.Stamp(hit!.Value);
        Check.That(Painted(m, back), "the shape aimed at takes the paint");
        Check.That(!Painted(m, front), "the nearer shape standing in front of the aim takes none, though the brush reaches its side");
    }

    [ScreenTest]
    static async Task A_brush_on_a_long_thin_shape_reaches_no_further_along_it_than_its_ring(TestRunner r)
    {
        var (m, _) = await Wall(r);
        float face = RailThickness / 2;
        var rail = Add(m, "Rail", new[] { 0f, Middle, 0f }, new[] { RailLength, RailThickness, RailThickness });
        await Look(r, m, 0);
        var aim = m.Stand.Transform * new Vector3(0, Middle, face);
        var hit = m.Tools.Ray(m.Cam.UnprojectPosition(aim));
        Check.That(hit is { } h && h.part == rail.Id, "the brush is on the rail");
        float reach = BrushReach / 2;
        BrushOf(m, reach);
        m.Tools.Stamp(hit!.Value);
        Check.That(PaintedAt(m, m.Stand.Transform * new Vector3(reach / 2, Middle, face)), "inside the ring the rail is painted");
        Check.That(!PaintedAt(m, m.Stand.Transform * new Vector3(reach * 2, Middle, face)), "twice the ring's reach along the rail it is not, stretched as the rail is");
    }

    [ScreenTest]
    static async Task A_shape_closer_than_the_camera_can_see_is_passed_through_to_the_one_behind(TestRunner r)
    {
        var (m, back) = await Wall(r);
        await Look(r, m, FromTheRight);
        var inFront = m.Cam.GlobalPosition + m.Cam.GlobalBasis.Z * -(m.Cam.Near / 3);
        var place = m.Stand.Transform.AffineInverse() * inFront;
        Add(m, "Veil", new[] { place.X, place.Y, place.Z }, new[] { VeilSide, VeilSide, 0.0005f });
        m.SelPartId = null;
        m.RefreshSelection();
        await m.Settle();
        m.Camera.Apply();
        await r.Frames(2);
        var hit = m.Tools.Ray((Vector2)m.Vp.Size / 2);
        Check.That(hit is { } h && h.part == back.Id, $"the press lands on the shape the camera shows, not on one it cuts away: {hit?.part}");
    }
}
