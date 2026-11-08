using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Prefs;

namespace KTable.App;

static class ModelSolidTests
{
    static async Task<(StudioTests.Rig rig, ModelStudio m)> Model(TestRunner r, string first = "box")
    {
        var h = await StudioTests.Open(r);
        await h.Studio.NewModelPiece("Solid", ModelImport.Primitive(first), first);
        return (h, h.Studio.Model!);
    }

    static ModelPart Add(ModelStudio m, string kind, string name)
    {
        m.SelPartId = null;
        m.AddShape(kind, name);
        return m.SelPart!;
    }

    static void Put(ModelStudio m, ModelPart p, Vector3 pos, Vector3 size)
    {
        p.Pos = new[] { pos.X, pos.Y, pos.Z };
        p.Scl = new[] { size.X, size.Y, size.Z };
        p.Rot = new[] { 0f, 0f, 0f };
        m.RefreshSelection();
        m.Changed(mesh: true);
    }

    static bool Near(float a, float b, float tolerance = 0.02f) => Mathf.Abs(a - b) <= tolerance;

    [ScreenTest]
    static async Task A_shape_s_place_is_its_centre_and_its_size_runs_along_its_own_axes_after_it_turns(TestRunner r)
    {
        var (_, m) = await Model(r);
        var box = m.Doc.Parts.Single();
        Put(m, box, new Vector3(1, 2, 3), new Vector3(2, 1, 1));
        box.Rot = new[] { 0f, 90f, 0f };
        m.RefreshSelection();
        var b = m.BoundsOf(box)!.Value;
        Check.That(b.GetCenter().DistanceTo(new Vector3(1, 2, 3)) < 0.01f, $"the centre is where Place says: {b.GetCenter()}");
        Check.That(Near(b.Size.Z, 2) && Near(b.Size.X, 1), $"two wide along its own X, which now runs along Z: {b.Size}");
    }

    [ScreenTest]
    static Task A_model_saved_before_centred_places_reads_the_same_and_one_it_cannot_square_keeps_its_old_reading(TestRunner r)
    {
        var bounds = new Aabb(new Vector3(-0.5f, 0, -0.5f), new Vector3(1, 1, 1));
        var square = new ModelPart { Pos = new[] { 1f, 0.5f, 2f }, Rot = new[] { 0f, 90f, 0f }, Scl = new[] { 2f, 1f, 3f } };
        var sheared = new ModelPart { Pos = new[] { 0f, 0f, 0f }, Rot = new[] { 0f, 30f, 0f }, Scl = new[] { 2f, 1f, 1f } };
        var doc = new ModelDoc { Version = 1, Parts = { square, sheared } };
        square.WorldScale = sheared.WorldScale = true;
        var before = (square.Xf(), sheared.Xf());
        square.WorldScale = sheared.WorldScale = false;
        ModelTree.Upgrade(doc, _ => bounds);
        Check.Equal(ModelDoc.CurrentVersion, doc.Version, "the document is now of the current version");
        foreach (var corner in new[] { Vector3.Zero, new Vector3(0.5f, 1, 0.5f), new Vector3(-0.5f, 0.3f, 0.2f) })
        {
            Check.That((square.Xf() * corner).DistanceTo(before.Item1 * corner) < 1e-4f, $"a squared part lands where it did: {corner}");
            Check.That((sheared.Xf() * corner).DistanceTo(before.Item2 * corner) < 1e-4f, $"a part sized across a turn lands where it did: {corner}");
        }
        Check.That(!square.WorldScale && square.Pivot != null, "the squared part now has a centred place");
        Check.That(sheared.WorldScale, "the one a turn had stretched keeps its old reading");
        Check.That(square.Pos.Zip(new[] { 1f, 1f, 2f }).All(x => Near(x.First, x.Second, 1e-3f)), $"its place is now its centre: {string.Join(",", square.Pos)}");
        return Task.CompletedTask;
    }

    [ScreenTest]
    static async Task A_cylinder_set_to_cut_takes_a_hole_out_of_the_box_above_it_and_both_hide_inside_one_solid(TestRunner r)
    {
        var (h, m) = await Model(r);
        var box = m.Doc.Parts.Single();
        Put(m, box, new Vector3(0, 0.5f, 0), Vector3.One);
        var drill = Add(m, "cylinder", "Drill");
        Put(m, drill, new Vector3(0, 0.5f, 0), new Vector3(0.4f, 2, 0.4f));
        m.Edits.SetJoin(drill, ModelTree.Cut);
        await m.Settle();
        var key = ModelTree.SolidKey(box.Id);
        Check.That(m.Views.ContainsKey(key), "the two make one solid");
        var solid = m.Views[key];
        Check.That(!m.Views[box.Id].Node.Visible, "the box hides inside it");
        Check.That(m.IsGhost(m.Views[drill.Id]) && !m.Drawn.Contains(m.Views[drill.Id]), "the chosen drill shows only as a see-through outline");
        Check.That(solid.Node.Visible && solid.Data.Tris > 12, $"the solid shows with its hole's walls ({solid.Data.Tris} triangles)");
        bool wall = solid.Data.V.Any(v => new Vector2(v.X, v.Z).Length() < 0.21f && v.Y > 0.2f && v.Y < 0.8f);
        Check.That(wall, "there is surface inside the box, round the hole");
        var b = solid.Data.Bounds();
        Check.That(Near(b.Size.Y, 1) && Near(b.Size.X, 1), $"the cutter does not add to it: {b.Size}");
        m.Undo();
        await m.Settle();
        Check.That(!m.Views.ContainsKey(key) && m.Views[box.Id].Node.Visible && m.Views[drill.Id].Node.Visible, "undo parts them again");
        _ = h;
    }

    [ScreenTest]
    static async Task Two_spheres_added_with_a_blend_flow_into_one_rounded_solid(TestRunner r)
    {
        var (_, m) = await Model(r, "sphere");
        var a = m.Doc.Parts.Single();
        Put(m, a, new Vector3(-0.3f, 0.5f, 0), Vector3.One);
        var b = Add(m, "sphere", "Other");
        Put(m, b, new Vector3(0.3f, 0.5f, 0), Vector3.One);
        m.Edits.SetJoin(b, ModelTree.Add, 0.3f);
        Check.That(ModelEdits.CanBlend(m.Doc, b), "two spheres can blend");
        await m.Settle();
        var solid = m.Views[ModelTree.SolidKey(a.Id)];
        Check.That(solid.Data.Tris > 200, $"the blended solid is meshed ({solid.Data.Tris} triangles)");
        var box = solid.Data.Bounds();
        Check.That(Near(box.Size.X, 1.6f, 0.08f), $"it spans both spheres: {box.Size.X:0.00}");
        float waist = solid.Data.V.Where(v => Mathf.Abs(v.X) < 0.02f).Select(v => new Vector2(v.Y - 0.5f, v.Z).Length()).DefaultIfEmpty(0).Max();
        Check.That(waist > 0.42f, $"the blend fills the waist between them ({waist:0.000} against 0.40 for a sharp join)");
    }

    [ScreenTest]
    static async Task Paint_on_a_joined_solid_stays_on_it_when_a_part_moves_and_after_the_model_is_opened_again(TestRunner r)
    {
        var (h, m) = await Model(r);
        var box = m.Doc.Parts.Single();
        Put(m, box, new Vector3(0, 0.5f, 0), Vector3.One);
        var lump = Add(m, "sphere", "Lump");
        Put(m, lump, new Vector3(0, 1, 0), new Vector3(0.6f, 0.6f, 0.6f));
        m.Edits.SetJoin(lump, ModelTree.Add);
        await m.Settle();
        var key = ModelTree.SolidKey(box.Id);
        var solid = m.Views[key];
        m.BeginStroke();
        for (int i = 0; i < solid.Paint.Length; i += 4) { solid.Paint[i] = 200; solid.Paint[i + 3] = 255; }
        solid.Repaint();
        m.EndStroke();
        m.Snapshot();
        lump.Pos[0] = 0.1f;
        m.RefreshSelection();
        m.Changed(mesh: true);
        await m.Settle();
        var moved = m.Views[key];
        Check.That(moved != solid, "the solid was built again");
        float red = Painted(moved);
        Check.That(red > 0.6f, $"most of it is still painted ({red:P0})");
        await m.Bake();
        Check.That(box.SolidPaint != null && box.SolidMesh != null, "the solid's paint is kept with the model");
        var def = h.Studio.Sel!;
        await h.Studio.NewModelPiece("Other", ModelImport.Primitive("box"), "box");
        Check.That(await m.Open(def), "the model opens again");
        await m.Settle();
        Check.That(m.Views.TryGetValue(key, out var back) && Painted(back) > 0.6f, "the paint is on it after opening");
    }

    static float Painted(ModelPartView v, int channel = 0)
    {
        int n = 0, red = 0;
        for (int t = 0; t < v.Data.Tris; t++)
        {
            var uv = (v.Data.UV[t * 3] + v.Data.UV[t * 3 + 1] + v.Data.UV[t * 3 + 2]) / 3;
            int o = (Mathf.Clamp((int)(uv.Y * v.H), 0, v.H - 1) * v.W + Mathf.Clamp((int)(uv.X * v.W), 0, v.W - 1)) * 4;
            n++;
            if (v.Paint[o + 3] > 128 && v.Paint[o + channel] > 150) red++;
        }
        return n == 0 ? 0 : red / (float)n;
    }

    [ScreenTest]
    static async Task A_group_carries_its_shapes_and_taking_them_out_leaves_them_where_they_stood(TestRunner r)
    {
        var (_, m) = await Model(r);
        var box = m.Doc.Parts.Single();
        Put(m, box, new Vector3(1, 0.5f, 0), Vector3.One);
        m.Edits.Group(box);
        var g = m.SelPart!;
        Check.That(g.IsGroup && box.Parent == g.Id, "the box is in a new group");
        g.Rot = new[] { 0f, 90f, 0f };
        g.Pos = new[] { g.Pos[0], g.Pos[1] + 1, g.Pos[2] };
        m.RefreshSelection();
        var moved = m.BoundsOf(box)!.Value.GetCenter();
        Check.That(moved.DistanceTo(new Vector3(1, 1.5f, 0)) < 0.01f, $"it rose with the group, turning about the group's centre: {moved}");
        m.Edits.Ungroup(g);
        Check.That(box.Parent == null && !m.Doc.Parts.Any(p => p.IsGroup), "the group is gone, the box stays");
        Check.That(m.BoundsOf(box)!.Value.GetCenter().DistanceTo(moved) < 0.01f, "the box stays where it stood");
    }

    [ScreenTest]
    static async Task A_twin_mirrors_a_shape_across_the_middle_and_the_saved_piece_holds_both(TestRunner r)
    {
        var (h, m) = await Model(r);
        var body = m.Doc.Parts.Single();
        Put(m, body, new Vector3(0, 0.5f, 0), Vector3.One);
        var arm = Add(m, "capsule", "Arm");
        Put(m, arm, new Vector3(0.8f, 0.5f, 0), new Vector3(0.4f, 1, 0.4f));
        arm.Twin = true;
        m.RefreshSelection();
        var b = m.BoundsOf(arm)!.Value;
        Check.That(Near(b.GetCenter().X, 0) && b.Size.X > 1.6f, $"the arm and its twin sit either side: {b}");
        await m.Bake();
        var def = h.Studio.Sel!;
        var mesh = ModelImport.Obj(System.IO.Path.Combine(h.Store.PackageDir(h.PackId), h.Studio.Pack.Assets[def.Mesh!].File));
        int expected = m.Views[body.Id].Data.Tris + 2 * m.Views[arm.Id].Data.Tris;
        Check.Equal(expected, mesh.Tris, "the piece's mesh has the body, the arm and its twin");
    }

    [ScreenTest]
    static async Task A_turned_profile_reshapes_from_its_points_and_keeps_its_paint(TestRunner r)
    {
        var (_, m) = await Model(r);
        var vase = Add(m, ShapeMaker.Lathe, "Vase");
        var v = m.Views[vase.Id];
        m.BeginStroke();
        for (int i = 0; i < v.Paint.Length; i += 4) { v.Paint[i + 2] = 220; v.Paint[i + 3] = 255; }
        v.Repaint();
        m.EndStroke();
        var wide = vase.Spec!.Copy();
        for (int i = 0; i < wide.Points.Length; i += 2) wide.Points[i] *= 2;
        float was = v.Data.Bounds().Size.X;
        m.Edits.Reshape(vase, wide);
        var now = m.Views[vase.Id];
        Check.That(Near(now.Data.Bounds().Size.X, was * 2, 0.02f), $"twice as wide ({was:0.00} to {now.Data.Bounds().Size.X:0.00})");
        Check.That(Painted(now, 2) > 0.8f, $"its paint came with it ({Painted(now, 2):P0})");
        m.Undo();
        Check.That(Near(m.Views[vase.Id].Data.Bounds().Size.X, was), "undo brings the old profile back");
    }

    [ScreenTest]
    static async Task Sit_on_centre_on_and_measure_work_from_the_shapes_bounds(TestRunner r)
    {
        var (_, m) = await Model(r);
        var table = m.Doc.Parts.Single();
        Put(m, table, new Vector3(0, 0.25f, 0), new Vector3(2, 0.5f, 2));
        var cup = Add(m, "cylinder", "Cup");
        Put(m, cup, new Vector3(3, 3, 1), new Vector3(0.2f, 0.4f, 0.2f));
        Check.That(m.Edits.SitOn(cup, table), "the cup sits on the table");
        var c = m.BoundsOf(cup)!.Value;
        Check.That(Near(c.Position.Y, 0.5f, 1e-3f) && Near(c.GetCenter().X, 0, 1e-3f) && Near(c.GetCenter().Z, 0, 1e-3f), $"on top, in the middle: {c}");
        cup.Pos[0] = 2;
        Check.That(m.Edits.CentreOn(cup, table, "x"), "it centres across");
        Check.That(Near(m.BoundsOf(cup)!.Value.GetCenter().X, 0, 1e-3f), "back in the middle across");
        var (apart, gaps) = m.Edits.Measure(cup, table)!.Value;
        Check.That(Near(gaps.Y / m.Doc.Scale, 0, 1e-3f) && gaps.X < 0, $"it touches the top and overlaps across: {gaps}");
        Check.That(apart > 0, "the centres are apart");
    }

    [ScreenTest]
    static async Task An_agent_builds_a_figure_in_one_batch_with_a_group_a_cut_and_a_twin_and_measures_it(TestRunner r)
    {
        var rig = await StudioTests.Open(r);
        var agent = new AgentEndpoint(new Settings()) { Studio = rig.Studio };
        r.AddChild(agent);
        async Task<string> Call(string tool, string args) =>
            await agent.Handle($$$"""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"{{{tool}}}","arguments":{{{args}}}}}""") ?? "";
        static bool Failed(string reply) => JsonNode.Parse(reply)?["result"]?["isError"]?.GetValue<bool>() != false;
        await rig.Studio.NewModelPiece("Figure", ModelImport.Primitive("box"), "box");
        var id = rig.Studio.Sel!.Id;
        var built = await Call("studio_model_batch", $$"""
            {"id":"{{id}}","add":[
              {"shape":"capsule","name":"Body","pos":[0,0.5,0],"size":[1,1,1]},
              {"shape":"sphere","name":"Head","pos":[0,1.2,0],"size":[0.4,0.4,0.4],"join":"add","blend":0.05},
              {"shape":"group","name":"Arms"},
              {"shape":"capsule","name":"Arm","pos":[0.4,0.6,0],"size":[0.3,0.8,0.3],"parent":"Arms","twin":true},
              {"shape":"lathe","name":"Helmet","points":[0,0, 0.3,0, 0.25,0.2, 0,0.3],"sit_on":"Head"},
              {"shape":"cylinder","name":"Visor","pos":[0,1.2,0.2],"size":[0.1,0.1,0.3],"join":"cut"}
            ],"remove":["Box"],"side":"turnaround"}
            """);
        Check.That(!Failed(built), "the batch builds: " + built);
        var m = rig.Studio.Model!;
        Check.That(!m.Doc.Parts.Any(p => p.Name == "Box"), "the box is gone");
        Check.That(m.Doc.Parts.First(p => p.Name == "Arm").Parent == m.Doc.Parts.First(p => p.Name == "Arms").Id, "the arm is in its group");
        Check.That(m.Doc.Parts.First(p => p.Name == "Helmet").Spec?.Count == 4, "the helmet has its own profile");
        Check.That(m.Views.ContainsKey(ModelTree.SolidKey(m.Doc.Parts.First(p => p.Name == "Body").Id)), "body and head make one solid");
        var parts = await Call("studio_model_parts", $$"""{"id":"{{id}}"}""");
        var listed = JsonNode.Parse(parts)?["result"]?["content"]?[0]?["text"]?.GetValue<string>() ?? "";
        Check.That(listed.Contains("\"parent\"") && listed.Contains("Arms"), "the parts list says where the arm is: " + listed[..System.Math.Min(400, listed.Length)]);
        var measured = await Call("studio_model_measure", $$"""{"id":"{{id}}","a":"Helmet","b":"Head"}""");
        Check.That(!Failed(measured) && measured.Contains("gaps"), "the measure answers: " + measured);
        await Task.CompletedTask;
    }
}
