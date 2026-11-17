using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class ModelBrushSizeTests
{
    const float FarAway = 3, CloseUp = 1.5f, BrushWide = 0.8f, DecalWide = 0.6f, Middle = 0.5f, WallFront = -0.5f, SamePatch = 0.05f, SameRing = 0.1f, MuchLarger = 1.5f;
    const int DecalSide = 8;

    static async Task<(ModelStudio m, ModelPart wall, float scale)> Wall(TestRunner r)
    {
        var h = await StudioTests.Open(r);
        await h.Studio.NewModelPiece("Sized", ModelImport.Primitive("box"), "box");
        var m = h.Studio.Model!;
        var wall = m.Doc.Parts.Single();
        wall.Pos = new[] { 0f, Middle, -0.6f };
        wall.Scl = new[] { 2f, 1f, 0.2f };
        m.SelPartId = null;
        m.RefreshSelection();
        await m.Settle();
        m.Camera.Spin = false;
        return (m, wall, m.Stand.Scale.X);
    }

    static async Task<Vector2> LookFrom(TestRunner r, ModelStudio m, float distance, bool flat = false)
    {
        m.Camera.Target = m.Stand.Transform * new Vector3(0, Middle, 0);
        m.Camera.Dist = distance * m.Stand.Scale.X;
        m.Camera.Ortho = flat;
        m.Camera.Face(0, 0);
        await r.Frames(2);
        return m.Cam.UnprojectPosition(m.Stand.Transform * new Vector3(0, Middle, WallFront));
    }

    static int Texels(ModelStudio m, ModelPart p) => Enumerable.Range(0, m.Views[p.Id].Paint.Length / 4).Count(i => m.Views[p.Id].Paint[i * 4 + 3] != 0);

    static bool Near(float a, float b, float share) => Mathf.Abs(a - b) <= share * Mathf.Max(a, b);

    [ScreenTest]
    static async Task A_brush_paints_the_same_patch_of_the_model_from_far_and_from_close_and_its_ring_grows_with_the_zoom(TestRunner r)
    {
        var (m, wall, scale) = await Wall(r);
        m.SetTool("paint");
        m.Ink = new Color(1, 0, 0);
        m.P.ModelBrush = BrushWide * scale;

        async Task<(int texels, float ring)> Dab(float distance, bool flat = false)
        {
            var at = await LookFrom(r, m, distance, flat);
            Array.Clear(m.Views[wall.Id].Paint);
            var hit = m.Tools.Ray(at);
            Check.That(hit is { } h && h.part == wall.Id, "the brush is on the wall");
            m.Tools.Stamp(hit!.Value);
            return (Texels(m, wall), m.Tools.BrushPx(at));
        }

        var far = await Dab(FarAway);
        var close = await Dab(CloseUp);
        Check.That(far.texels > 0 && Near(far.texels, close.texels, SamePatch), $"one dab covers the same patch from far and from close: {far.texels} and {close.texels} texels");
        Check.That(close.ring > far.ring * MuchLarger, $"the ring is larger on the screen from close: {far.ring:0.#} px far, {close.ring:0.#} px close");

        var flatFar = await Dab(FarAway, flat: true);
        var flatClose = await Dab(CloseUp, flat: true);
        Check.That(Near(far.texels, flatFar.texels, SamePatch) && Near(far.texels, flatClose.texels, SamePatch), $"and the same patch in the flat view: {flatFar.texels} and {flatClose.texels} of {far.texels} texels");
        Check.That(Near(flatClose.ring, flatFar.ring * FarAway / CloseUp, SameRing), $"where the ring grows as the view closes in: {flatFar.ring:0.#} px far, {flatClose.ring:0.#} px close");
        float across = m.P.ModelBrush / m.Cam.Size * m.Vp.Size.Y;
        Check.That(Near(flatClose.ring * 2, across, SameRing), $"and is as wide as the brush is on the model: {flatClose.ring * 2:0.#} px of {across:0.#}");
    }

    [ScreenTest]
    static async Task A_decal_covers_the_same_patch_of_the_model_from_far_and_from_close(TestRunner r)
    {
        var (m, wall, scale) = await Wall(r);
        var picture = Image.CreateEmpty(DecalSide, DecalSide, false, Image.Format.Rgba8);
        picture.Fill(new Color(1, 0.85f, 0.1f));
        m.Decal.Set(picture, "badge");
        m.Decal.Span = DecalWide * scale;

        async Task<int> Press(float distance, bool flat = false)
        {
            var at = await LookFrom(r, m, distance, flat);
            Array.Clear(m.Views[wall.Id].Paint);
            m.Decal.Project(at);
            return Texels(m, wall);
        }

        int far = await Press(FarAway), close = await Press(CloseUp), flatClose = await Press(CloseUp, flat: true);
        Check.That(far > 0 && Near(far, close, SamePatch), $"the picture covers the same patch from far and from close: {far} and {close} texels");
        Check.That(Near(far, flatClose, SamePatch), $"and in the flat view: {flatClose} of {far} texels");

        m.Decal.SpanOnScreen(m.Vp.Size.Y / 2f, await LookFrom(r, m, FarAway, flat: true));
        Check.That(Near(m.Decal.Span, m.Cam.Size / 2, SameRing), $"a size given as a share of the view is that much of the model: {m.Decal.Span:0.###} of {m.Cam.Size:0.###}");
    }
}
