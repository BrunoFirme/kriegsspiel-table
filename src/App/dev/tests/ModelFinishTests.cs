using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class ModelFinishTests
{
    const float ViewDistance = 3, Reach = 0.15f;
    static readonly byte MetalCode = Finishes.Code(Finishes.Metal), GlassCode = Finishes.Code(Finishes.Glass);

    static async Task<(StudioTests.Rig h, ModelStudio m, ModelPart box)> Box(TestRunner r, string name)
    {
        var h = await StudioTests.Open(r);
        await h.Studio.NewModelPiece(name, ModelImport.Primitive("box"), "box");
        var m = h.Studio.Model!;
        var box = m.Doc.Parts.Single();
        m.SelPartId = null;
        m.RefreshSelection();
        await m.Settle();
        return (h, m, box);
    }

    static async Task<(string part, int tri, Vector3 local, Vector3 world, float u, float v)> Aim(TestRunner r, ModelStudio m, string tool)
    {
        var bounds = m.BoundsOf(m.Doc.Parts.Single())!.Value;
        var aim = m.Stand.Transform * new Vector3(bounds.GetCenter().X, bounds.GetCenter().Y, bounds.End.Z);
        m.Camera.Target = aim;
        m.Camera.Dist = ViewDistance;
        m.Camera.Face(0, 0);
        m.SetTool(tool);
        await r.Frames(2);
        m.P.ModelBrush = 2 * Reach * m.Stand.Scale.X;
        return m.Tools.Ray(m.Cam.UnprojectPosition(aim))!.Value;
    }

    static Image Asset(StudioTests.Rig h, string sha)
    {
        var pack = h.Store.Pack(h.PackId)!;
        var img = Image.LoadFromFile(Path.Combine(h.Store.PackageDir(pack.Id), pack.Assets[sha].File));
        if (img.IsCompressed()) img.Decompress();
        img.Convert(Image.Format.Rgba8);
        return img;
    }

    static string Obj(StudioTests.Rig h, string sha)
    {
        var pack = h.Store.Pack(h.PackId)!;
        return File.ReadAllText(Path.Combine(h.Store.PackageDir(pack.Id), pack.Assets[sha].File));
    }

    [ScreenTest]
    static async Task A_brush_laid_in_metal_shines_where_it_went_in_the_studio_and_on_the_table_and_erasing_takes_the_shine_off(TestRunner r)
    {
        var (h, m, box) = await Box(r, "Breastplate");
        var hit = await Aim(r, m, "paint");
        m.P.ModelFinish = Finishes.Metal;
        m.Ink = new Color(0.6f, 0.6f, 0.65f);
        m.BeginStroke();
        m.Tools.Stamp(hit);
        m.EndStroke();
        var v = m.Views[box.Id];
        int metal = v.Finish.Count(c => c == MetalCode);
        Check.That(metal > 0 && metal < v.Finish.Length, $"the stroke lays metal where it went and nowhere else: {metal} of {v.Finish.Length}");
        Check.That(v.Finish.All(c => c == 0 || c == MetalCode), "the texels the stroke missed carry no finish");
        await r.Frames(2);
        Check.That(v.FinishImg.GetData().Any(c => c == MetalCode), "the studio view is shown the metal");

        await m.Bake();
        Check.That(m.Problem == null, $"the model saves: {m.Problem}");
        var d = h.Studio.Sel!;
        Check.That(d.FinishTexture != null, "the piece carries the finishes beside its colours");
        Check.That(box.FinishPaint != null, "the shape keeps its laid finish to edit again");
        var sheen = Asset(h, d.FinishTexture!).GetData();
        var shines = PieceFinish.Texel(MetalCode);
        bool Holds(Color c) => Enumerable.Range(0, sheen.Length / 4).Any(i => sheen[i * 4] == (byte)c.R8 && sheen[i * 4 + 1] == (byte)c.G8);
        Check.That(Holds(shines) && Holds(PieceFinish.Texel(0)), "the saved finishes hold metal where it was laid and matte elsewhere");
        var basis = new StandardMaterial3D();
        PieceFinish.Painted(basis, ImageTexture.CreateFromImage(Asset(h, d.FinishTexture!)));
        Check.Equal((1f, 1f, BaseMaterial3D.TextureChannel.Red, BaseMaterial3D.TextureChannel.Green),
            (basis.Roughness, basis.Metallic, basis.RoughnessTextureChannel, basis.MetallicTextureChannel), "the table reads roughness and metal from the finishes");

        hit = await Aim(r, m, "erase");
        m.BeginStroke();
        m.Tools.Stamp(hit);
        m.EndStroke();
        Check.That(v.Finish.All(c => c == 0), "rubbing out the paint rubs out its finish");
        m.Undo();
        Check.That(v.Finish.Count(c => c == MetalCode) == metal, "undo brings the metal back");
        m.Redo();
        await m.Bake();
        Check.That(d.FinishTexture == null && box.FinishPaint == null, "with nothing laid the piece carries no finishes");
    }

    [ScreenTest]
    static async Task A_shape_filled_with_glass_is_see_through_in_the_studio_and_on_the_table(TestRunner r)
    {
        var (h, m, box) = await Box(r, "Lantern");
        m.P.ModelFinish = Finishes.Glass;
        m.Ink = new Color(0.8f, 0.9f, 1f, 0.3f);
        m.BeginStroke();
        m.Tools.FillPart(m.Views[box.Id]);
        m.EndStroke();
        var v = m.Views[box.Id];
        Check.That(v.Finish.All(c => c == GlassCode), "the whole shape is glass");
        await r.Frames(2);
        Check.That(v.Glassy && v.Mat.Shader.Code.Contains("ALPHA"), "the studio draws it see-through");
        await m.Bake();
        Check.That(m.Problem == null, $"the model saves: {m.Problem}");
        var d = h.Studio.Sel!;
        var obj = Obj(h, d.Mesh!);
        Check.That(obj.Contains(SurfaceDraw.BlendStatement.Trim()), "the saved model marks its glass faces see-through");
        var pack = h.Store.Pack(h.PackId)!;
        var mesh = ObjLoader.Load(Path.Combine(h.Store.PackageDir(pack.Id), pack.Assets[d.Mesh!].File))!;
        var draws = Enumerable.Range(0, mesh.GetSurfaceCount()).Select(s => ObjLoader.Surface(mesh, s).Draw).ToList();
        Check.That(draws.Any(x => x.Alpha == SurfaceAlpha.Blend), $"the table reads a see-through surface: {string.Join(", ", draws)}");
        var clear = PieceArt.Drawn(new StandardMaterial3D(), draws.First(x => x.Alpha == SurfaceAlpha.Blend));
        Check.Equal(BaseMaterial3D.TransparencyEnum.Alpha, clear.Transparency, "and draws it blended");
        var colours = Asset(h, d.Texture!);
        var region = box.Region!;
        Check.That(colours.GetPixel(region[0] + region[2] / 2, region[1] + region[3] / 2).A < 0.9f, "the glass lets the light through in the saved colours");
    }

    [ScreenTest]
    static async Task A_shape_made_metal_before_finishes_were_painted_opens_as_metal_paint(TestRunner r)
    {
        var (h, m, _) = await Box(r, "Old helmet");
        await h.Studio.NewModelPiece("Other", ModelImport.Primitive("box"), "box");
        Check.That(await m.Open(h.Studio.Pack.Pieces.Single(p => p.Name == "Other")), "another piece opens between");
        var helmet = h.Studio.Pack.Pieces.Single(p => p.Name == "Old helmet");
        var old = ModelStudio.LoadDoc(h.Store, h.PackId, helmet.Mesh)!;
        old.Parts.Single().Finish = Finishes.Metal;
        KTable.Core.Json.WriteAtomic(ModelStudio.DocPath(h.Store, h.PackId, helmet.Mesh!), System.Text.Json.JsonSerializer.Serialize(old, ModelStudio.Opts));
        Check.That(await m.Open(helmet) && m.Def == helmet, "the old piece opens again");
        var opened = m.Doc.Parts.Single();
        Check.That(m.Views[opened.Id].Finish.All(c => c == MetalCode), $"its shape is metal paint all over: {m.Views[opened.Id].Finish.Count(c => c == MetalCode)} of {m.Views[opened.Id].Finish.Length}");
        m.Changed(mesh: false);
        await m.Bake();
        Check.That(opened.FinishPaint != null && helmet.FinishTexture != null, "saving keeps the metal as painted finish");
        Check.That(opened.Finish == null, "and drops the old whole-shape finish");
    }
}
