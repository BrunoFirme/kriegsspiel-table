using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core;
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
        Check.Equal(PieceFinish.Specular, basis.MetallicSpecular, "the table gives every finish the one specular the studio gives it");
        Check.That(v.Mat.Shader.Code.Contains($"SPECULAR = FINISH_ROUGH[f] < 1.0 ? {PieceFinish.Specular.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture)} : 0.0;"), "the studio draws that same specular on every finish that shines, and none on matte");

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

    const float PlainlyApart = 0.2f;

    static void Painted(PackageStore store)
    {
        var dir = Path.Combine(store.Root, "finished", "assets");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "ball.obj"), ModelImport.ToObj(ModelImport.Primitive("sphere")));
        var sheen = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
        sheen.Fill(PieceFinish.Texel(Finishes.Code(Finishes.Plastic)));
        sheen.SavePng(Path.Combine(dir, "sheen.png"));
        var pack = new PackManifest { Id = "finished", Name = "Finished" };
        pack.Assets["ball"] = new AssetEntry { File = "assets/ball.obj", Kind = "mesh" };
        pack.Assets["sheen"] = new AssetEntry { File = "assets/sheen.png", Kind = "image", W = 8, H = 8 };
        store.RegisterDef(pack, new PieceDef { Id = "painted", Name = "Painted", Kind = "model", Mesh = "ball", FinishTexture = "sheen", Size = new Size2(1, 1), Thickness = 1, Shape = "box" });
        store.RegisterDef(pack, new PieceDef { Id = "bare", Name = "Bare", Kind = "model", Mesh = "ball", Size = new Size2(1, 1), Thickness = 1, Shape = "box" });
    }

    static StandardMaterial3D Worn(PieceNode n) =>
        (StandardMaterial3D)(n.GetChildren().OfType<MeshInstance3D>().FirstOrDefault(m => m.Mesh is ArrayMesh) ?? throw new TestFailure("the model is drawn")).GetSurfaceOverrideMaterial(0);

    [NodeTest]
    static async Task A_painted_piece_on_the_table_wears_its_finishes_and_the_table_gives_gloss_surroundings_to_reflect_without_showing_them(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0, prepare: Painted, arrange: g =>
        {
            g.Layers[Layers.Umpire].Add(new PieceInstance { Id = "a", Def = "painted", Pos = new Vec2(-2, 0) });
            g.Layers[Layers.Umpire].Add(new PieceInstance { Id = "b", Def = "bare", Pos = new Vec2(2, 0) });
        });
        await r.Frames(5);
        for (int i = 0; i < 600 && Worn(t.Node("a")).RoughnessTexture == null; i++) await r.Frames(1);
        var painted = Worn(t.Node("a"));
        Check.That(painted.RoughnessTexture != null && painted.MetallicTexture == painted.RoughnessTexture, "the painted piece reads roughness and metal from its finishes");
        Check.Equal((PieceFinish.Specular, BaseMaterial3D.SpecularModeEnum.SchlickGgx), (painted.MetallicSpecular, painted.SpecularMode), "and reflects with the finishes' specular");
        var bare = Worn(t.Node("b"));
        Check.Equal((1f, 0f, PieceFinish.Specular), (bare.Roughness, bare.Metallic, bare.MetallicSpecular), "a piece with no finish stays matte");
        Check.That(bare.RoughnessTexture == null, "and reads no finishes");

        var env = t.Table.FindChildren("*", nameof(WorldEnvironment), true, false).OfType<WorldEnvironment>().Single().Environment;
        Check.Equal(Godot.Environment.ReflectionSource.Sky, env.ReflectedLightSource, "gloss and metal on the table have surroundings to reflect");
        Check.That(env.Sky?.SkyMaterial != null, "which the table's sky draws");
        Check.Equal(Sky.RadianceSizeEnum.Size64, env.Sky!.RadianceSize, "kept small");
        Check.Equal((Godot.Environment.BGMode.Color, TableBackdrop.Plain), (env.BackgroundMode, env.BackgroundColor), "the backdrop stays the flat colour");
        Check.Equal(Godot.Environment.AmbientSource.Color, env.AmbientLightSource, "and the light on the table stays the scenario's own");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
        t.Table.QueueFree();
    }

    [NodeTest]
    static Task The_finishes_are_plainly_different_materials(TestRunner r)
    {
        PieceFinish.Sheen Of(string finish) => PieceFinish.Of(finish);
        var (plastic, wood, card, metal, glass) = (Of(Finishes.Plastic), Of(Finishes.Wood), Of(Finishes.Cardboard), Of(Finishes.Metal), Of(Finishes.Glass));
        Check.That(glass.Roughness + PlainlyApart / 2 <= plastic.Roughness, $"glass is smoother than plastic: {glass.Roughness} against {plastic.Roughness}");
        Check.That(plastic.Roughness + PlainlyApart <= wood.Roughness, $"plastic is glossy where wood is dull: {plastic.Roughness} against {wood.Roughness}");
        Check.That(wood.Roughness + PlainlyApart <= card.Roughness, $"wood keeps a sheen where cardboard has none: {wood.Roughness} against {card.Roughness}");
        Check.Equal((1f, 0f), (card.Roughness, card.Metallic), "cardboard is dead matte");
        Check.That(metal.Metallic == 1 && new[] { plastic, wood, card, glass }.All(s => s.Metallic == 0), "only metal is metal");
        foreach (var finish in Finishes.All)
        {
            var texel = PieceFinish.Texel(Finishes.Code(finish));
            Check.That(Mathf.IsEqualApprox(texel.R, Of(finish).Roughness) && Mathf.IsEqualApprox(texel.G, Of(finish).Metallic), $"{finish} is saved for the table as the studio draws it");
        }
        return Task.CompletedTask;
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
    static async Task A_shape_filled_with_planks_shows_their_grain_under_its_paint_in_the_studio_and_on_the_table(TestRunner r)
    {
        var (h, m, box) = await Box(r, "Crate");
        m.P.ModelFinish = Finishes.Planks;
        m.Ink = new Color(0.5f, 0.35f, 0.2f);
        m.BeginStroke();
        m.Tools.FillPart(m.Views[box.Id]);
        m.EndStroke();
        var v = m.Views[box.Id];
        byte planks = Finishes.Code(Finishes.Planks);
        Check.That(v.Finish.All(c => c == planks) && v.Slotted, "the whole shape is planks");
        await r.Frames(2);
        Check.That(v.Mat.Shader.Code.Contains("grained(") && v.Mat.GetShaderParameter("grain_0").Obj is Texture2D, "the studio draws the grain under the paint");
        await m.Bake();
        Check.That(m.Problem == null, $"the model saves: {m.Problem}");
        var d = h.Studio.Sel!;
        Check.That(Obj(h, d.Mesh!).Contains("grain " + Finishes.Planks), "the saved model marks its faces as planks");
        var pack = h.Store.Pack(h.PackId)!;
        var mesh = ObjLoader.Load(Path.Combine(h.Store.PackageDir(pack.Id), pack.Assets[d.Mesh!].File))!;
        var draws = Enumerable.Range(0, mesh.GetSurfaceCount()).Select(s => ObjLoader.Surface(mesh, s).Draw).ToList();
        Check.That(draws.Any(x => x.Grain == Finishes.Planks), $"the table reads a surface of planks: {string.Join(", ", draws)}");
        var grained = PieceArt.Drawn(new StandardMaterial3D(), draws.First(x => x.Grain == Finishes.Planks));
        Check.That(grained.DetailEnabled && grained.DetailAlbedo != null && grained.UV2Triplanar && grained.DetailBlendMode == BaseMaterial3D.BlendModeEnum.Mul,
            "and lays the grain over its paint, all round the shape");
        const float TableUnitsPerMeshUnit = 2;
        FinishGrain.Fit([grained], TableUnitsPerMeshUnit);
        Check.That(Mathf.IsEqualApprox(grained.UV2Scale.X, TableUnitsPerMeshUnit / FinishGrain.Across(Finishes.Planks)), "at the same size on the table as in the studio");
        var plain = PieceArt.Drawn(new StandardMaterial3D(), SurfaceDraw.Plain with { Alpha = SurfaceAlpha.Blend });
        Check.That(!plain.DetailEnabled, "a surface with no grain takes none");
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
