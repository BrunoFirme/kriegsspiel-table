using System.Numerics;
using KTable.Core.Model;
using Xunit;
using H = KTable.Core.Model.RigHumanoid;

namespace KTable.Core.Tests;

public class RigTests
{
    static readonly string[] MixamoNames =
    [
        "mixamorig:Hips", "mixamorig:Spine", "mixamorig:Spine1", "mixamorig:Neck", "mixamorig:Head",
        "mixamorig:LeftUpLeg", "mixamorig:LeftLeg", "mixamorig:LeftFoot", "mixamorig:LeftToeBase",
        "mixamorig:RightUpLeg", "mixamorig:RightLeg", "mixamorig:RightFoot", "mixamorig:RightToeBase",
        "mixamorig:LeftShoulder", "mixamorig:LeftArm", "mixamorig:LeftForeArm", "mixamorig:LeftHand",
        "mixamorig:RightShoulder", "mixamorig:RightArm", "mixamorig:RightForeArm", "mixamorig:RightHand",
    ];

    static readonly string[] VrmNames =
    [
        "J_Bip_C_Hips", "J_Bip_C_Spine", "J_Bip_C_Chest", "J_Bip_C_Neck", "J_Bip_C_Head",
        "J_Bip_L_UpperLeg", "J_Bip_L_LowerLeg", "J_Bip_L_Foot", "J_Bip_L_ToeBase",
        "J_Bip_R_UpperLeg", "J_Bip_R_LowerLeg", "J_Bip_R_Foot", "J_Bip_R_ToeBase",
        "J_Bip_L_Shoulder", "J_Bip_L_UpperArm", "J_Bip_L_LowerArm", "J_Bip_L_Hand",
        "J_Bip_R_Shoulder", "J_Bip_R_UpperArm", "J_Bip_R_LowerArm", "J_Bip_R_Hand",
    ];

    static readonly string[] VrchatNames =
    [
        "Hips", "Spine", "Chest", "Neck", "Head",
        "Left leg", "Left knee", "Left ankle", "Left toe",
        "Right leg", "Right knee", "Right ankle", "Right toe",
        "Left shoulder", "Left arm", "Left elbow", "Left wrist",
        "Right shoulder", "Right arm", "Right elbow", "Right wrist",
    ];

    static readonly string[] UnrealNames =
    [
        "pelvis", "spine_01", "spine_02", "neck_01", "head",
        "thigh_l", "calf_l", "foot_l", "ball_l",
        "thigh_r", "calf_r", "foot_r", "ball_r",
        "clavicle_l", "upperarm_l", "lowerarm_l", "hand_l",
        "clavicle_r", "upperarm_r", "lowerarm_r", "hand_r",
    ];

    static readonly int[] Parents = [-1, 0, 1, 2, 3, 0, 5, 6, 7, 0, 9, 10, 11, 2, 13, 14, 15, 2, 17, 18, 19];

    static readonly Vector3[] Spots =
    [
        new(0, 1.0f, 0), new(0, 1.15f, 0), new(0, 1.35f, 0), new(0, 1.55f, 0), new(0, 1.65f, 0),
        new(0.1f, 0.95f, 0), new(0.1f, 0.5f, 0), new(0.1f, 0.08f, 0), new(0.1f, 0.02f, 0.12f),
        new(-0.1f, 0.95f, 0), new(-0.1f, 0.5f, 0), new(-0.1f, 0.08f, 0), new(-0.1f, 0.02f, 0.12f),
        new(0.05f, 1.48f, 0), new(0.18f, 1.48f, 0), new(0.45f, 1.48f, 0), new(0.7f, 1.48f, 0),
        new(-0.05f, 1.48f, 0), new(-0.18f, 1.48f, 0), new(-0.45f, 1.48f, 0), new(-0.7f, 1.48f, 0),
    ];

    static Rig Figure(string[] names, int extraPoints = 0)
    {
        var bones = new List<RigBone>();
        for (int i = 0; i < names.Length; i++)
            bones.Add(new RigBone(names[i], Parents[i], Parents[i] < 0 ? Spots[i] : Spots[i] - Spots[Parents[i]], Quaternion.Identity, 1));
        var points = Spots.Concat(Enumerable.Range(0, extraPoints).Select(k => new Vector3(0, 1 + k * 0.001f, 0))).ToArray();
        var joints = new ushort[points.Length * Rig.Influences];
        var weights = new float[points.Length * Rig.Influences];
        for (int v = 0; v < points.Length; v++)
        {
            joints[v * Rig.Influences] = (ushort)Math.Min(v, names.Length - 1);
            weights[v * Rig.Influences] = 1;
        }
        var rig = new Rig
        {
            Bones = bones, Points = points, Normals = points.Select(_ => Vector3.UnitY).ToArray(), Coords = points.Select(_ => Vector2.Zero).ToArray(),
            Joints = joints, Weights = weights, Triangles = [0, 1, 2],
        };
        foreach (var (role, bone) in H.Map(bones)) rig.Humanoid[role] = bone;
        return rig;
    }

    public static IEnumerable<object[]> Conventions() => new[] { MixamoNames, VrmNames, VrchatNames, UnrealNames }.Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(Conventions))]
    public void Bodies_from_the_usual_rigs_are_known_as_human_and_their_limbs_found(string[] names)
    {
        var map = H.Map(Figure(names).Bones);
        Assert.True(H.Walks(map));
        Assert.Equal(0, map[H.Hips]);
        Assert.Equal(4, map[H.Head]);
        Assert.Equal((5, 6, 7, 8), (map[H.Left + H.UpperLeg], map[H.Left + H.LowerLeg], map[H.Left + H.Foot], map[H.Left + H.Toes]));
        Assert.Equal((9, 10, 11), (map[H.Right + H.UpperLeg], map[H.Right + H.LowerLeg], map[H.Right + H.Foot]));
        Assert.Equal((13, 14, 15, 16), (map[H.Left + H.Shoulder], map[H.Left + H.UpperArm], map[H.Left + H.LowerArm], map[H.Left + H.Hand]));
        Assert.Equal((18, 19, 20), (map[H.Right + H.UpperArm], map[H.Right + H.LowerArm], map[H.Right + H.Hand]));
    }

    [Fact]
    public void Twist_bones_and_fingers_are_not_taken_for_limbs_and_a_rig_without_hips_is_not_human()
    {
        var names = (string[])MixamoNames.Clone();
        names[15] = "mixamorig:LeftForeArmTwist";
        var map = H.Map(Figure(names).Bones);
        Assert.False(map.ContainsKey(H.Left + H.LowerArm));
        names = (string[])VrchatNames.Clone();
        names[0] = "Root";
        Assert.False(H.Walks(H.Map(Figure(names).Bones)));
    }

    [Fact]
    public void A_rig_reads_back_as_it_was_written()
    {
        var rig = Figure(VrmNames);
        rig.Root = new RigPlace(new Vector3(0.5f, 0, -1), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f), 0.01f);
        rig.Clips.Add(new RigClip("Walk", 1, [new RigTrack(5, RigChannel.Rotation, [0, 1], [0, 0, 0, 1, 0, 0.3826834f, 0, 0.9238795f])]));
        var back = RigFile.Read(RigFile.Write(rig));
        Assert.Equal(rig.Bones, back.Bones);
        Assert.Equal(rig.Root, back.Root);
        Assert.Equal(rig.Points, back.Points);
        Assert.Equal(rig.Joints, back.Joints);
        Assert.Equal(rig.Weights, back.Weights);
        Assert.Equal(rig.Triangles, back.Triangles);
        Assert.Equal(rig.Humanoid.OrderBy(h => h.Key), back.Humanoid.OrderBy(h => h.Key));
        Assert.Equal("Walk", Assert.Single(back.Clips).Name);
        Assert.Equal(rig.Clips[0].Tracks[0].Values, back.Clips[0].Tracks[0].Values);
    }

    [Fact]
    public void A_damaged_or_oversized_rig_file_is_refused_instead_of_read()
    {
        var bytes = RigFile.Write(Figure(MixamoNames, extraPoints: 2000));
        Assert.Throws<FormatException>(() => RigFile.Read(bytes[..(bytes.Length / 2)]));
        Assert.Throws<FormatException>(() => RigFile.Read([1, 2, 3, 4, 5, 6, 7, 8]));
        Assert.Throws<TooLargeException>(() => RigFile.Read(bytes, most: 1000));
        var wrong = Figure(MixamoNames);
        wrong.Triangles[2] = 999;
        Assert.Throws<FormatException>(() => RigFile.Read(RigFile.Write(wrong)));
    }

    [Fact]
    public void A_figure_in_t_pose_stands_with_its_arms_down()
    {
        var rig = Figure(MixamoNames);
        var (points, _) = new RigMotion(rig).RelaxedShape();
        foreach (var hand in new[] { 16, 20 })
        {
            Assert.True(points[hand].Y < 1.0f, $"hand {hand} at {points[hand]}");
            Assert.True(MathF.Abs(points[hand].X) < 0.35f, $"hand {hand} at {points[hand]}");
        }
        Assert.True(Vector3.Distance(Spots[4], points[4]) < 1e-5f);
        Assert.True(Vector3.Distance(Spots[7], points[7]) < 1e-5f);
    }

    [Fact]
    public void Walking_swings_one_leg_forward_while_the_other_goes_back_and_the_arms_swing_against_them()
    {
        var rig = Figure(VrmNames);
        var motion = new RigMotion(rig);
        Assert.True(motion.Walks);
        motion.Pose(new RigMove(1, 0.25f, 0, 0, 0, 0));
        var (points, _) = rig.Posed(motion.Points, motion.Turns);
        Assert.True(points[7].Z > 0.15f, $"left foot {points[7]}");
        Assert.True(points[11].Z < -0.15f, $"right foot {points[11]}");
        Assert.True(points[16].Z < points[20].Z, $"hands {points[16]} {points[20]}");
        motion.Pose(new RigMove(1, 0.75f, 0, 0, 0, 0));
        (points, _) = rig.Posed(motion.Points, motion.Turns);
        Assert.True(points[7].Z < -0.15f && points[11].Z > 0.15f, $"feet {points[7]} {points[11]}");
    }

    [Fact]
    public void Each_built_in_walk_moves_a_body_its_own_way_and_choosing_one_sets_the_models_own_walk_aside()
    {
        var rig = Figure(VrmNames);
        var hands = new List<Vector3>();
        foreach (var id in RigMotion.Gaits.Select(g => g.Id).Append(RigMotion.March).Append(RigMotion.Sneak))
        {
            var motion = new RigMotion(rig, id);
            motion.Pose(new RigMove(1, 0.25f, 0, 0, 0, 0));
            var (points, _) = rig.Posed(motion.Points, motion.Turns);
            Assert.True(points[7].Z > points[11].Z, $"{id}: the left foot leads at a quarter stride, {points[7]} {points[11]}");
            Assert.All(hands, h => Assert.True(Vector3.Distance(h, points[16]) > 0.005f, $"{id} holds its hand where another walk does"));
            hands.Add(points[16]);
        }
        Assert.True(RigMotion.Gaits.Count >= 4 && RigMotion.Gaits[0].Id == RigMotion.Natural);
        Assert.DoesNotContain(RigMotion.Gaits, g => g.Id is RigMotion.March or RigMotion.Sneak);
        var turned = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f);
        rig.Clips.Add(new RigClip("Walk", 2, [new RigTrack(5, RigChannel.Rotation, [0, 2], [turned.X, turned.Y, turned.Z, turned.W, turned.X, turned.Y, turned.Z, turned.W])]));
        var own = new RigMotion(rig);
        own.Pose(new RigMove(1, 0.5f, 0, 0, 0, 0));
        Assert.True(Quaternion.Dot(turned, own.Turns[5]) > 0.9999f, "left to itself the body plays its own walk");
        var chosen = new RigMotion(rig, RigMotion.March);
        chosen.Pose(new RigMove(1, 0.5f, 0, 0, 0, 0));
        Assert.True(Quaternion.Dot(turned, chosen.Turns[5]) < 0.999f, "a chosen walk replaces the model's own");
    }

    [Fact]
    public void A_flying_body_leans_into_its_flight_and_tucks_on_a_pulse()
    {
        var rig = Figure(VrmNames);
        var motion = new RigMotion(rig);
        motion.Pose(new RigMove(0, 0, 0, 0, 0, 0, Fly: 1, Glide: 0));
        var (hover, _) = rig.Posed(motion.Points, motion.Turns);
        motion.Pose(new RigMove(0, 0, 0, 0, 0, 0, Fly: 1, Glide: 1));
        var (glide, _) = rig.Posed(motion.Points, motion.Turns);
        Assert.True(glide[4].Z > hover[4].Z + 0.3f, $"gliding puts the head well ahead of the hips: {hover[4]} then {glide[4]}");
        Assert.True(glide[4].Y < hover[4].Y - 0.1f, $"and lower: {hover[4]} then {glide[4]}");
        motion.Pose(new RigMove(0, 0, 0, 0, 0, 0, Fly: 1, Glide: 0, Pulse: 1));
        var (tucked, _) = rig.Posed(motion.Points, motion.Turns);
        Assert.True(tucked[7].Y > hover[7].Y + 0.1f, $"a pulse draws the feet up: {hover[7]} then {tucked[7]}");
    }

    [Fact]
    public void A_body_faces_its_toes_even_when_its_rig_was_turned_on_import()
    {
        var rig = Figure(UnrealNames);
        Assert.Equal(Vector3.UnitZ, new RigMotion(rig).FacingInModel());
        rig.Root = new RigPlace(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2), 1);
        var f = new RigMotion(rig).FacingInModel();
        Assert.True(Vector3.Distance(f, Vector3.UnitX) < 1e-4f, f.ToString());
    }

    [Fact]
    public void A_body_with_its_own_walk_plays_it_and_one_without_a_skeleton_it_knows_stands_still()
    {
        var names = (string[])MixamoNames.Clone();
        names[0] = "Root";
        var rig = Figure(names);
        Assert.False(new RigMotion(rig).Moves);
        var turned = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f);
        rig.Clips.Add(new RigClip("Armature|Walk", 2, [new RigTrack(5, RigChannel.Rotation, [0, 2], [turned.X, turned.Y, turned.Z, turned.W, turned.X, turned.Y, turned.Z, turned.W])]));
        var motion = new RigMotion(rig);
        Assert.True(motion.Moves);
        motion.Pose(new RigMove(1, 0.5f, 0, 0, 0, 0));
        Assert.True(Quaternion.Dot(turned, motion.Turns[5]) > 0.9999f);
        motion.Pose(RigMove.Still);
        Assert.Equal(Quaternion.Identity, motion.Turns[5]);
    }

    [Fact]
    public void A_body_with_many_motions_walks_runs_and_idles_with_the_plain_ones()
    {
        var rig = Figure(MixamoNames);
        foreach (var name in new[] { "2H_Melee_Idle", "Walking_Backwards", "Idle", "Jump_Idle", "Running_Strafe_Left", "Walking_B", "Walking_A", "Running_A", "Armature|Run" })
            rig.Clips.Add(new RigClip(name, 1, []));
        Assert.Equal(("Idle", "Walking_A", "Armature|Run"), (rig.Clip("idle")!.Name, rig.Clip("walk")!.Name, rig.Clip("run")!.Name));
    }

    static Vector3[] Acting(Rig rig, string? act, float walk = 0, float phase = 0, float seconds = 0)
    {
        var motion = new RigMotion(rig);
        motion.Pose(new RigMove(walk, phase, 0, 0, 1 - walk, 0, Act: act, ActSeconds: seconds));
        return rig.Posed(motion.Points, motion.Turns).Points;
    }

    [Fact]
    public void Each_of_the_fourteen_acts_holds_the_body_its_own_way()
    {
        var rig = Figure(VrmNames);
        Assert.Equal(14, RigMotion.Acts.Count);
        Assert.Equal(6, RigMotion.Acts.Count(a => a.Whole));
        var shapes = new List<(string Act, Vector3[] Points)> { ("stand", Acting(rig, null)) };
        foreach (var (id, _, _) in RigMotion.Acts)
        {
            var points = Acting(rig, id, seconds: 0.3f);
            Assert.All(shapes, other => Assert.True(Enumerable.Range(0, points.Length).Max(i => Vector3.Distance(points[i], other.Points[i])) > 0.05f, $"{id} looks like {other.Act}"));
            shapes.Add((id, points));
        }
    }

    [Fact]
    public void Whole_acts_lower_the_body_and_arm_acts_raise_the_hands()
    {
        var rig = Figure(VrmNames);
        var stand = Acting(rig, null);
        Assert.True(Acting(rig, RigMotion.Sit)[0].Y < stand[0].Y - 0.2f, "sitting lowers the hips");
        Assert.True(Acting(rig, RigMotion.Sit)[6].Z > 0.3f, "and brings the knees forward");
        Assert.True(Acting(rig, RigMotion.Kneel)[0].Y < stand[0].Y - 0.15f, "kneeling lowers the hips");
        Assert.True(Acting(rig, RigMotion.Lie)[4].Y < 0.3f, "lying puts the head near the ground");
        foreach (var act in new[] { RigMotion.Yield, RigMotion.Cheer })
        {
            var up = Acting(rig, act);
            Assert.True(up[16].Y > stand[4].Y - 0.1f && up[20].Y > stand[4].Y - 0.1f, $"{act} lifts both hands to the head or above: {up[16]} {up[20]}");
        }
        var carry = Acting(rig, RigMotion.Carry);
        Assert.True(carry[16].Z > 0.15f && carry[20].Z > 0.15f, "carrying holds the hands in front");
    }

    [Fact]
    public void An_arm_act_lies_over_the_walk_and_a_whole_act_does_not_walk()
    {
        var rig = Figure(VrmNames);
        var walking = Acting(rig, RigMotion.Carry, walk: 1, phase: 0.25f);
        Assert.True(walking[7].Z > 0.15f && walking[11].Z < -0.15f, $"the legs still stride: {walking[7]} {walking[11]}");
        Assert.True(walking[16].Z > 0.15f, "while the arms carry");
        Assert.True(RigMotion.WholeAct(RigMotion.Sit) && !RigMotion.WholeAct(RigMotion.Carry) && !RigMotion.WholeAct(null));
        Assert.True(RigMotion.KnownAct(null) && RigMotion.KnownAct(RigMotion.Guard) && !RigMotion.KnownAct("dance"));
    }

    [Fact]
    public void A_body_with_its_own_clip_for_an_act_plays_it_and_idles_never_take_an_act_clip()
    {
        var rig = Figure(MixamoNames);
        var turned = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f);
        rig.Clips.Add(new RigClip("Armature|Sit_Idle", 2, [new RigTrack(5, RigChannel.Rotation, [0, 2], [turned.X, turned.Y, turned.Z, turned.W, turned.X, turned.Y, turned.Z, turned.W])]));
        rig.Clips.Add(new RigClip("Kneel_Idle", 1, []));
        Assert.Null(rig.Clip("idle"));
        var motion = new RigMotion(rig);
        motion.Pose(new RigMove(0, 0, 0, 0, 1, 0, Act: RigMotion.Sit, ActSeconds: 0.5f));
        Assert.True(Quaternion.Dot(turned, motion.Turns[5]) > 0.9999f, "the body's own sit wins");
    }
}
