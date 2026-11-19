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
    const float Box = 0.06f, StrafeMetres = 1.2f, StrafeSeconds = 2.5f, StrafeSettleSeconds = 1f, ShortestFrame = 1f / 240;

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

    static string Exported(string dir, float facing)
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

    static string Scratch()
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

    [NodeTest]
    static async Task A_rigged_body_steps_aside_when_its_player_strafes_instead_of_walking_forward_on_the_spot(TestRunner r)
    {
        var dir = Scratch();
        try
        {
            var body = RigImport.Read(Exported(dir, 1), out var error) ?? throw new TestFailure("the figure is not read as a body: " + error);
            string kind = "";
            var t = OfflineTable.Make(r, pieces: 1, packs: [BodyPack], prepare: store => kind = Pack(store, body),
                arrange: g => g.Layers[Layers.Umpire][0].Def = kind);
            await r.Frames(3);
            var node = t.Node("p1");
            var feet = node.GlobalPosition;
            t.Table.Avatars.Show(7, "Ann", new AvatarPose(feet, 0, 1.8f, Body: "p1"), 0, snap: true);
            int hips = body.Rig.Humanoid[RigHumanoid.Hips], left = body.Rig.Humanoid[RigHumanoid.Left + RigHumanoid.Foot], right = body.Rig.Humanoid[RigHumanoid.Right + RigHumanoid.Foot];
            var (ahead, aside) = (new Vector3(0, 0, 1), new Vector3(-1, 0, 0));
            float reach = float.MinValue, drift = 0;
            Skeleton3D? skeleton = null;
            for (float spent = 0; spent < StrafeSeconds;)
            {
                float step = Mathf.Max((float)t.Table.GetProcessDeltaTime(), ShortestFrame);
                (feet, spent) = (feet + aside * StrafeMetres * step, spent + step);
                t.Table.Avatars.Show(7, "Ann", new AvatarPose(feet, 0, 1.8f, Body: "p1"), 0);
                await r.Frames(1);
                skeleton ??= node.FindChildren("*", "Skeleton3D", true, false).OfType<Skeleton3D>().FirstOrDefault();
                if (skeleton == null || spent < StrafeSettleSeconds) continue;
                Vector3 At(int bone) => skeleton.ToGlobal(skeleton.GetBoneGlobalPose(bone).Origin);
                var middle = At(hips);
                reach = Math.Max(reach, (At(right) - middle).Dot(aside));
                drift = Math.Max(drift, Math.Max(Math.Abs((At(left) - middle).Dot(ahead)), Math.Abs((At(right) - middle).Dot(ahead))));
            }
            Check.That(skeleton != null, "the body is drawn on a skeleton while a player is in it");
            Check.That(reach > 0.25f, $"stepping right the right foot reaches out to the right ({reach:0.00} m from the hips)");
            Check.That(drift < 0.12f, $"the feet stay under the body front to back ({drift:0.00} m)");
            Check.That(t.Errors.Count == 0, "no command refused: " + string.Join("; ", t.Errors));
            t.Table.Avatars.Forget(7);
            t.Table.QueueFree();
        }
        finally { Directory.Delete(dir, true); }
    }

    [NodeTest]
    static async Task A_body_in_the_studio_plays_each_motion_on_the_spot_from_its_Motion_tab_and_stands_plain_again_when_left(TestRunner r)
    {
        var dir = Scratch();
        try
        {
            var body = RigImport.Read(Exported(dir, 1), out var error) ?? throw new TestFailure("the figure is not read as a body: " + error);
            var h = await StudioTests.Open(r);
            var s = h.Studio;
            await s.NewAvatarBody("Hero", body);
            await r.Frames(5);
            var def = s.Sel!;
            var preview = s.T.Preview;
            var id = s.Paper.Instance(def)?.Id;
            Check.That(s.View == "table" && s.Inspector.OnTab(MotionTab.Name), "a new body opens on Try it with its Motion tab up");
            Check.That(id != null && preview.Piece == id, "the preview follows the body on the table");
            PieceNode Node() => s.T.Pieces.NodeOf(Layers.Umpire, id!)!;
            Skeleton3D? Bones() => Node().FindChildren("*", "Skeleton3D", true, false).OfType<Skeleton3D>().FirstOrDefault(k => !k.IsQueuedForDeletion());
            for (int i = 0; i < 120 && Bones() == null; i++) await r.Frames(1);
            Check.That(Bones() != null && Node().Model is { Visible: false }, "standing still it is drawn on its skeleton");
            var rest = Node().Position;
            float tall = Node().Thickness;
            int thigh = body.Rig.Humanoid[RigHumanoid.Left + RigHumanoid.UpperLeg];

            var ahead = s.Inspector.Tabs.FindChildren("*", "Button", true, false).OfType<Button>().First(b => b.TooltipText.StartsWith("Ahead   "));
            ahead.EmitSignal(BaseButton.SignalName.Pressed);
            Check.That(preview.Now is { Ahead: 1, Aside: 0, Run: false, Speed: 1 }, "the Motion tab's ahead arrow walks it ahead at the game's pace");
            float swing = 0, drift = 0;
            await Play(r, () =>
            {
                if (Bones() is { } k) swing = Math.Max(swing, k.GetBonePoseRotation(thigh).AngleTo(k.GetBoneRest(thigh).Basis.GetRotationQuaternion()));
                drift = Math.Max(drift, new Vector2(Node().Position.X - rest.X, Node().Position.Z - rest.Z).Length());
            }, () => swing > Mathf.DegToRad(10) && preview.Travelled.Length() > tall);
            Check.That(swing > Mathf.DegToRad(10), $"walking, the thigh swings ({Mathf.RadToDeg(swing):0} degrees)");
            Check.That(drift < tall * StillShare, $"it walks on the spot ({drift:0.000} off its place)");
            Check.That(preview.Travelled.Length() > tall * 0.5f, $"the floor slides under its steps ({preview.Travelled.Length():0.00})");

            preview.Now = new MotionPreview.Choice(Air: MotionPreview.Jumps);
            float highest = 0;
            await Play(r, () => highest = Math.Max(highest, Node().Position.Y - rest.Y), () => highest > tall * 0.3f);
            Check.That(highest > tall * 0.3f, $"jumping, it leaves the ground ({highest / tall:0.00} of its height)");

            preview.Now = new MotionPreview.Choice(Air: MotionPreview.Flies);
            await Play(r, () => { }, () => Node().Position.Y - rest.Y > tall * 0.45f);
            Check.That(Node().Position.Y - rest.Y > tall * 0.45f, $"flying, it holds itself up ({(Node().Position.Y - rest.Y) / tall:0.00} of its height)");

            preview.Now = new MotionPreview.Choice();
            await Play(r, () => { }, () => Math.Abs(Node().Position.Y - rest.Y) < tall * StillShare);
            Check.That(Math.Abs(Node().Position.Y - rest.Y) < tall * StillShare, "it comes down and stands where it was");

            s.Select(s.Pack.Pieces.First(p => p.Name == "Hussars"), frame: false);
            await r.Frames(5);
            Check.That(preview.Piece == null && !preview.Playing, "another piece picked, the preview lets the body go");
            Check.That(Node().Model is { Visible: true } && Bones() == null, "the body is its plain model again");
            Check.That(Node().Position.IsEqualApprox(rest), "standing where it was laid out");
        }
        finally { Directory.Delete(dir, true); }
    }

    [NodeTest]
    static async Task A_body_taken_in_the_studio_walks_with_the_game_s_controls_and_Esc_hands_it_back_to_the_preview(TestRunner r)
    {
        var dir = Scratch();
        try
        {
            var body = RigImport.Read(Exported(dir, 1), out var error) ?? throw new TestFailure("the figure is not read as a body: " + error);
            var h = await StudioTests.Open(r);
            var s = h.Studio;
            await s.NewAvatarBody("Hero", body);
            await r.Frames(5);
            var def = s.Sel!;
            var id = s.Paper.Instance(def)!.Id;
            var node = s.T.Pieces.NodeOf(Layers.Umpire, id)!;
            float tall = node.Thickness;
            Button? Labelled(string text) => s.Inspector.Tabs.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text == text && !b.IsQueuedForDeletion());
            Labelled("Take control")!.EmitSignal(BaseButton.SignalName.Pressed);
            await r.Frames(3);
            Check.That(s.Driver.Driving && s.T.Avatars.Drives(node) && !s.T.Preview.Playing, "Take control hands the body to the game's walker and stops the preview");
            Check.That(Math.Abs(s.T.Walk.Height - tall) < tall * StillShare, $"the walker stands as tall as the body ({s.T.Walk.Height:0.00} for {tall:0.00})");
            Check.That(Labelled("Release control") != null && Labelled("Take control") == null, "the Motion tab offers to let go instead");
            var from = s.Paper.Instance(def)!.Pos;
            s.T.Walk.Steer = () => new Steering(s.T.Rig.Yaw, 0, new Vector2(0, 1), false);
            await Play(r, () => { }, () => (s.Paper.Instance(def)!.Pos - from).Length > tall);
            s.T.Walk.Steer = null;
            Check.That((s.Paper.Instance(def)!.Pos - from).Length > tall, "walking it carries the body over the table");
            await r.Frames(5);
            var stood = node.Position;
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            await r.Frames(5);
            Check.That(!s.Driver.Driving && !s.T.Walk.Walking, "Esc lets go");
            Check.That(s.T.Preview.Playing && s.T.Preview.Piece == id, "the preview takes the body back");
            Check.That(new Vector2(node.Position.X - stood.X, node.Position.Z - stood.Z).Length() < tall * 0.05f, "the body stays where it was walked to");
            Check.That(Labelled("Take control") != null, "the Motion tab offers control again");
        }
        finally { Directory.Delete(dir, true); }
    }

    const double PlaySeconds = 8;
    const float StillShare = 0.01f;

    static async Task Play(TestRunner r, Action look, Func<bool> done)
    {
        ulong until = Time.GetTicksMsec() + (ulong)(PlaySeconds * 1000);
        while (Time.GetTicksMsec() < until && !done())
        {
            await r.Frames(1);
            look();
        }
    }

    static string Pack(PackageStore store, RigImport.Result body) => AvatarBodyPiece.Add(store, new PackManifest { Id = BodyPack, Name = "Bodies" }, "Hero", body).Id;
}
