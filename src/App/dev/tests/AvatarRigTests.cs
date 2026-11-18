using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class AvatarRigTests
{
    const string BodyPack = "bodies";
    const float Box = 0.06f;

    static readonly string[] Names =
    [
        "Hips", "Spine", "Chest", "Neck", "Head",
        "Left leg", "Left knee", "Left ankle", "Left toe",
        "Right leg", "Right knee", "Right ankle", "Right toe",
        "Left shoulder", "Left arm", "Left elbow", "Left wrist",
        "Right shoulder", "Right arm", "Right elbow", "Right wrist",
    ];

    static readonly int[] Parents = [-1, 0, 1, 2, 3, 0, 5, 6, 7, 0, 9, 10, 11, 2, 13, 14, 15, 2, 17, 18, 19];

    static Vector3[] Spots(float facing) =>
    [
        new(0, 1.0f, 0), new(0, 1.15f, 0), new(0, 1.35f, 0), new(0, 1.55f, 0), new(0, 1.65f, 0),
        new(0.1f, 0.95f, 0), new(0.1f, 0.5f, 0), new(0.1f, 0.08f, 0), new(0.1f, 0.03f, 0.12f * facing),
        new(-0.1f, 0.95f, 0), new(-0.1f, 0.5f, 0), new(-0.1f, 0.08f, 0), new(-0.1f, 0.03f, 0.12f * facing),
        new(0.05f, 1.48f, 0), new(0.18f, 1.48f, 0), new(0.45f, 1.48f, 0), new(0.7f, 1.48f, 0),
        new(-0.05f, 1.48f, 0), new(-0.18f, 1.48f, 0), new(-0.45f, 1.48f, 0), new(-0.7f, 1.48f, 0),
    ];

    internal static string Exported(string dir, float facing)
    {
        var spots = Spots(facing);
        var root = new Node3D { Name = "Figure" };
        var skeleton = new Skeleton3D { Name = "Skeleton3D" };
        root.AddChild(skeleton);
        skeleton.Owner = root;
        for (int i = 0; i < Names.Length; i++)
        {
            skeleton.AddBone(Names[i]);
            skeleton.SetBoneParent(i, Parents[i]);
            skeleton.SetBoneRest(i, new Transform3D(Basis.Identity, Parents[i] < 0 ? spots[i] : spots[i] - spots[Parents[i]]));
        }
        skeleton.ResetBonePoses();
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetSkinWeightCount(SurfaceTool.SkinWeightCount.Skin4Weights);
        var box = new BoxMesh { Size = Vector3.One * Box };
        var cube = box.GetMeshArrays();
        var cv = cube[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var cn = cube[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var ci = cube[(int)Mesh.ArrayType.Index].AsInt32Array();
        for (int b = 0; b < Names.Length; b++)
            foreach (var k in ci)
            {
                st.SetNormal(cn[k]);
                st.SetBones(new[] { b, 0, 0, 0 });
                st.SetWeights(new[] { 1f, 0, 0, 0 });
                st.AddVertex(cv[k] + spots[b]);
            }
        var body = new MeshInstance3D { Name = "Body", Mesh = st.Commit() };
        skeleton.AddChild(body);
        body.Owner = root;
        body.Skeleton = "..";
        body.Skin = skeleton.CreateSkinFromRestTransforms();
        var anim = new Animation { Length = 1 };
        int track = anim.AddTrack(Animation.TrackType.Rotation3D);
        anim.TrackSetPath(track, "Skeleton3D:Left leg");
        anim.RotationTrackInsertKey(track, 0, Quaternion.Identity);
        anim.RotationTrackInsertKey(track, 1, new Quaternion(Vector3.Right, 0.5f));
        var library = new AnimationLibrary();
        library.AddAnimation("Walk", anim);
        var player = new AnimationPlayer { Name = "AnimationPlayer" };
        root.AddChild(player);
        player.Owner = root;
        player.AddAnimationLibrary("", library);
        var doc = new GltfDocument();
        var state = new GltfState();
        Check.Equal(Error.Ok, doc.AppendFromScene(root, state), "the test figure is laid out for writing");
        var path = Path.Combine(dir, $"figure{(facing > 0 ? "" : "-back")}.glb");
        Check.Equal(Error.Ok, doc.WriteToFilesystem(state, path), "the test figure is written");
        root.Free();
        return path;
    }

    internal static string Scratch()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ktable-rigtest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [NodeTest]
    static async Task A_rigged_figure_comes_in_as_a_body_standing_relaxed_facing_forward_with_its_walk_kept(TestRunner r)
    {
        var dir = Scratch();
        try
        {
            foreach (var facing in new[] { 1f, -1f })
            {
                var body = RigImport.Read(Exported(dir, facing), out var error);
                Check.That(body != null, "the figure is read as a body: " + error);
                var motion = new RigMotion(body!.Rig);
                Check.That(motion.Walks, "its skeleton is known as a human one");
                Check.Equal(Names.Length, body.Rig.Bones.Count, "every bone is kept");
                Check.That(body.Rig.Clips.Any(c => c.Name.Contains("Walk")), "its own walk is kept");
                Check.That(Math.Abs(body.Metres - (1.65f + Box / 2)) < 0.05f, $"it keeps its height in metres ({body.Metres:0.00})");
                var b = body.Shape.Bounds();
                Check.That(b.Size.X < 0.75f, $"its arms hang down instead of out ({b.Size.X:0.00} wide)");
                Check.That(Math.Abs(b.Position.Y) < 1e-3f, "it stands on the ground");
                var toes = body.Shape.V.Where(v => v.Y < 0.01f).Average(v => v.Z);
                var heels = body.Shape.V.Where(v => v.Y is > 0.07f and < 0.12f).Average(v => v.Z);
                Check.That(toes > heels + 0.05f, $"its toes point to the piece's front (facing {facing}: toes {toes:0.00}, heels {heels:0.00})");
                Check.That(body.Rig.Triangles.Length / 3 == body.Shape.Tris, "the moving body and the still one have the same triangles");
            }
            var bad = Path.Combine(dir, "broken.glb");
            File.WriteAllBytes(bad, [1, 2, 3, 4]);
            Check.That(RigImport.Read(bad, out var why) == null && why != null, "a damaged file says why and makes nothing");
        }
        finally { Directory.Delete(dir, true); }
        await r.Frames(1);
    }

    [NodeTest]
    static async Task A_rigged_body_walks_its_legs_when_its_player_moves_and_its_plain_model_comes_back_when_let_go(TestRunner r)
    {
        var dir = Scratch();
        Rig? rig = null;
        try
        {
            var file = Exported(dir, 1);
            var body = RigImport.Read(file, out var error) ?? throw new TestFailure("the figure is not read as a body: " + error);
            rig = body.Rig;
            string kind = "";
            var t = OfflineTable.Make(r, pieces: 1, packs: [BodyPack], prepare: store => kind = Pack(store, body),
                arrange: g => g.Layers[Layers.Umpire][0].Def = kind);
            await r.Frames(3);
            var node = t.Node("p1");
            var feet = node.GlobalPosition;
            t.Table.Avatars.Show(7, "Ann", new AvatarPose(feet, 0, 1.8f, Body: "p1"), 0, snap: true);
            int thigh = rig.Humanoid[RigHumanoid.Left + RigHumanoid.UpperLeg];
            float most = 0;
            Skeleton3D? skeleton = null;
            for (int i = 0; i < 90; i++)
            {
                feet += new Vector3(0, 0, 0.04f);
                t.Table.Avatars.Show(7, "Ann", new AvatarPose(feet, 0, 1.8f, Body: "p1"), 0);
                await r.Frames(1);
                skeleton ??= node.FindChildren("*", "Skeleton3D", true, false).OfType<Skeleton3D>().FirstOrDefault();
                if (skeleton != null) most = Math.Max(most, skeleton.GetBonePoseRotation(thigh).AngleTo(skeleton.GetBoneRest(thigh).Basis.GetRotationQuaternion()));
            }
            Check.That(skeleton != null, "the body is drawn on a skeleton while a player is in it");
            Check.That(skeleton!.FindChildren("*", "MeshInstance3D", false, false).OfType<MeshInstance3D>().Any(m => m.Skin != null), "the body's skin follows the skeleton");
            Check.That(node.Model is { Visible: false }, "the plain model is hidden behind the moving one");
            Check.That(most > Mathf.DegToRad(10), $"the thigh swings as the player walks ({Mathf.RadToDeg(most):0} degrees)");
            t.Table.Avatars.Forget(7);
            await r.Frames(2);
            Check.That(node.Model is { Visible: true }, "the plain model is back when the player leaves");
            Check.That(!node.FindChildren("*", "Skeleton3D", true, false).Any(s => GodotObject.IsInstanceValid(s) && !s.IsQueuedForDeletion()), "the skeleton goes with the player");
            Check.That(t.Errors.Count == 0, "no command refused: " + string.Join("; ", t.Errors));
            t.Table.QueueFree();
        }
        finally { Directory.Delete(dir, true); }
    }

    static Button? CardRow(HudTests.Rig h, string text) =>
        WindowTests.Descendants(h.Hud.Ctx.Root).OfType<PopupPanel>().Where(p => p.Visible).SelectMany(WindowTests.Descendants).OfType<Button>()
            .FirstOrDefault(b => WindowTests.Descendants(b).OfType<Label>().Any(l => l.Text == text));

    [NodeTest]
    static async Task A_rigged_piece_nobody_wears_holds_the_act_picked_on_its_card_and_a_block_offers_none(TestRunner r)
    {
        var dir = Scratch();
        try
        {
            var body = RigImport.Read(Exported(dir, 1), out var error) ?? throw new TestFailure("the figure is not read as a body: " + error);
            string kind = "";
            var h = await HudTests.Host(r, pieces: 1, packs: [BodyPack], prepare: store => kind = Pack(store, body));
            h.S.Do(new SpawnPiece { Layer = Layers.Umpire, Id = "hero", Def = kind, Pos = new Vec2(3, 0) });
            await r.Frames(3);
            var block = h.Table.Pieces.NodeOf(Layers.Umpire, "p1") ?? throw new TestFailure("the block is on the table");
            var hero = h.Table.Pieces.NodeOf(Layers.Umpire, "hero") ?? throw new TestFailure("the body is on the table");
            h.Table.Selection.Set(new[] { "p1" });
            h.Hud.Menus.Show(new Vector2(300, 300), block);
            await r.Frames(2);
            Check.That(CardRow(h, "Rotate 180°") != null, "the block's card is up");
            Check.That(CardRow(h, "Act · None") == null, "a block has no act");
            foreach (var p in WindowTests.Descendants(h.Hud.Ctx.Root).OfType<PopupPanel>().Where(p => p.Visible).ToList()) p.Hide();
            h.Table.Selection.Set(new[] { "hero" });
            h.Hud.Menus.Show(new Vector2(300, 300), hero);
            await r.Frames(2);
            var page = CardRow(h, "Act · None") ?? throw new TestFailure("the body's card offers its act");
            page.EmitSignal(BaseButton.SignalName.Pressed);
            await r.Frames(2);
            foreach (var (_, name, _) in RigMotion.Acts) Check.That(CardRow(h, name) != null, $"{name} is on the act page");
            CardRow(h, "Kneel")!.EmitSignal(BaseButton.SignalName.Pressed);
            await r.Frames(2);
            Check.Equal(RigMotion.Kneel, h.S.View.Find(Layers.Umpire, "hero")!.Act, "the act is stored on the piece");
            Skeleton3D? skeleton = null;
            for (int i = 0; i < 120 && skeleton == null; i++)
            {
                await r.Frames(1);
                skeleton = hero.FindChildren("*", "Skeleton3D", true, false).OfType<Skeleton3D>().FirstOrDefault(s => !s.IsQueuedForDeletion());
            }
            Check.That(skeleton != null, "a body nobody wears is drawn on its skeleton while it keeps an act");
            await r.Frames(3);
            int hips = body.Rig.Humanoid[RigHumanoid.Hips];
            Check.That(skeleton!.GetBonePosePosition(hips).Y < skeleton.GetBoneRest(hips).Origin.Y - 0.1f, $"kneeling lowers the hips ({skeleton.GetBonePosePosition(hips).Y:0.00})");
            Check.That(hero.Model is { Visible: false }, "the plain model is hidden behind the kneeling one");
            Check.Equal(1, h.Table.Avatars.Acted, "one body is held in an act");
            h.S.Do(new SetPieceAct { Layer = Layers.Umpire, Id = "hero", Act = null });
            await r.Frames(25);
            Check.That(hero.Model is { Visible: true }, "the plain model is back when the act is cleared");
            Check.Equal(0, h.Table.Avatars.Acted, "no body is held in an act");
            h.S.Do(new SetPieceAct { Layer = Layers.Umpire, Id = "p1", Act = RigMotion.Sit });
            await r.Frames(25);
            Check.Equal(0, h.Table.Avatars.Acted, "a block given an act by a script stays a block");
        }
        finally { Directory.Delete(dir, true); }
    }

    static string Pack(PackageStore store, RigImport.Result body) => AvatarBodyPiece.Add(store, new PackManifest { Id = BodyPack, Name = "Bodies" }, "Hero", body).Id;
}
