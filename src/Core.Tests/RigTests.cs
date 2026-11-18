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

    static Rig Figure(string[] names, int extraPoints = 0) => Figure(names, Parents, Spots, extraPoints);

    static Rig Figure(string[] names, int[] parents, Vector3[] spots, int extraPoints = 0)
    {
        var bones = new List<RigBone>();
        for (int i = 0; i < names.Length; i++)
            bones.Add(new RigBone(names[i], parents[i], parents[i] < 0 ? spots[i] : spots[i] - spots[parents[i]], Quaternion.Identity, 1));
        var points = spots.Concat(Enumerable.Range(0, extraPoints).Select(k => new Vector3(0, 1 + k * 0.001f, 0))).ToArray();
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Stepping_sideways_opens_and_closes_the_feet_across_the_way_without_crossing_them(bool running)
    {
        var rig = Figure(VrmNames);
        var motion = new RigMotion(rig);
        (Vector3 Left, Vector3 Right) Feet(float phase, float aside)
        {
            motion.Pose(running ? new RigMove(0, 0, 1, phase, 0, 0, Ahead: 0, Aside: aside) : new RigMove(1, phase, 0, 0, 0, 0, Ahead: 0, Aside: aside));
            var (points, _) = rig.Posed(motion.Points, motion.Turns);
            return (points[7], points[11]);
        }
        float widest = 0, narrowest = float.MaxValue, rightmost = 0, leftmost = 0;
        for (float phase = 0; phase < 1; phase += 0.0625f)
        {
            var (left, right) = Feet(phase, 1);
            float apart = running ? new Vector2(left.X - right.X, left.Z - right.Z).Length() : left.X - right.X;
            (widest, narrowest, rightmost) = (MathF.Max(widest, left.X - right.X), MathF.Min(narrowest, apart), MathF.Min(rightmost, right.X));
            if (!running) Assert.True(MathF.Abs(left.Z) < 0.12f && MathF.Abs(right.Z) < 0.12f, $"at {phase} the feet stay under the body front to back: {left} {right}");
            leftmost = MathF.Max(leftmost, Feet(phase, -1).Left.X);
        }
        if (running)
        {
            Feet(0, 1);
            var (joints, _) = rig.Posed(motion.Points, motion.Turns);
            Assert.True(joints[5].Z > joints[9].Z + 0.05f, $"running aside the hips turn toward the way: {joints[5]} {joints[9]}");
            Assert.True(MathF.Abs(joints[14].Z - joints[18].Z) < 0.06f, $"and the shoulders stay square: {joints[14]} {joints[18]}");
        }
        Assert.True(rightmost < -0.25f, $"stepping right the right foot reaches out to the right: {rightmost}");
        Assert.True(leftmost > 0.25f, $"stepping left the left foot reaches out to the left: {leftmost}");
        Assert.True(widest - narrowest > 0.3f, $"the feet open to {widest} and close to {narrowest}");
        Assert.True(narrowest > 0.06f, $"closing, the feet never cross: {narrowest}");
        float run = running ? 1 : 0;
        Assert.True(motion.CycleLength(run, 0, 1, 0) < motion.CycleLength(run, -1, 0, 0) && motion.CycleLength(run, -1, 0, 0) < motion.CycleLength(run, 1, 0, 0),
            "steps aside and back are shorter");
    }

    [Theory]
    [InlineData(1, 0, false)]
    [InlineData(1, 0, true)]
    [InlineData(0, 1, false)]
    [InlineData(-1, 0, false)]
    [InlineData(0.7071f, -0.7071f, false)]
    public void A_planted_foot_holds_still_on_the_ground_while_the_body_walks_over_it(float ahead, float aside, bool running)
    {
        var rig = Figure(VrmNames);
        var motion = new RigMotion(rig);
        const float Tall = 1.63f;
        float cycle = motion.CycleLength(running ? 1 : 0, ahead, aside, 0) * Tall;
        var travel = new Vector3(-aside, 0, ahead);
        const int Steps = 64;
        var heights = new float[Steps];
        var held = new List<(float Phase, float Along)>();
        for (int i = 0; i < Steps; i++)
        {
            float phase = i / (float)Steps;
            motion.Pose(running ? new RigMove(0, 0, 1, phase, 0, 0, Ahead: ahead, Aside: aside) : new RigMove(1, phase, 0, 0, 0, 0, Ahead: ahead, Aside: aside));
            var (points, _) = rig.Posed(motion.Points, motion.Turns);
            heights[i] = new[] { 7, 8, 11, 12 }.Min(j => points[j].Y);
            if (points[7].Y < points[11].Y - 0.01f && phase is > 0.3f and < 0.55f) held.Add((phase, Vector3.Dot(points[7], travel) + phase * cycle));
        }
        if (running) Assert.True(heights.Max() - heights.Min() > 0.02f, $"a run leaves the ground between steps: {heights.Min()} to {heights.Max()}");
        else Assert.True(heights.Max() - heights.Min() < 0.005f, $"the lower foot stays on the ground: {heights.Min()} to {heights.Max()}");
        Assert.True(held.Count > 4, "the left foot is planted for a stretch of the stride");
        Assert.True(held.Max(h => h.Along) - held.Min(h => h.Along) < 0.04f * Tall, $"the planted foot holds still: {string.Join(", ", held.Select(h => $"{h.Phase:0.00}:{h.Along:0.000}"))}");
    }

    [Fact]
    public void Walking_backward_swings_the_legs_and_arms_the_other_way_and_a_diagonal_between_them()
    {
        var rig = Figure(VrmNames);
        var motion = new RigMotion(rig);
        motion.Pose(new RigMove(1, 0.25f, 0, 0, 0, 0, Ahead: -1));
        var (points, _) = rig.Posed(motion.Points, motion.Turns);
        Assert.True(points[7].Z < -0.15f && points[11].Z > 0.15f, $"feet {points[7]} {points[11]}");
        Assert.True(points[16].Z > points[20].Z, $"hands {points[16]} {points[20]}");
        motion.Pose(new RigMove(1, 0.25f, 0, 0, 0, 0, Ahead: 0.7071f, Aside: -0.7071f));
        (points, _) = rig.Posed(motion.Points, motion.Turns);
        Assert.True(points[7].Z > 0.08f && points[7].X > 0.2f, $"ahead and to the left, the left foot leads that way: {points[7]}");
    }

    [Fact]
    public void A_body_plays_its_own_back_and_side_motions_and_without_them_turns_its_walk_back_or_borrows_its_run_to_the_side()
    {
        var rig = Figure(VrmNames);
        Quaternion Bent(float angle) => Quaternion.CreateFromAxisAngle(Vector3.UnitX, angle);
        RigClip Held(string name, float angle) => new(name, 2, [new RigTrack(5, RigChannel.Rotation, [0, 2], [.. Q(Bent(angle)), .. Q(Bent(angle))])]);
        static float[] Q(Quaternion q) => [q.X, q.Y, q.Z, q.W];
        rig.Clips.Add(new RigClip("Walk", 2, [new RigTrack(5, RigChannel.Rotation, [0, 2], [.. Q(Quaternion.Identity), .. Q(Bent(0.8f))])]));
        rig.Clips.Add(Held("Run", 0.1f));
        rig.Clips.Add(Held("Run_Left", 0.3f));
        rig.Clips.Add(Held("Walk_Strafe_Right", 0.5f));
        rig.Clips.Add(Held("Jump_Left", 0.9f));
        var motion = new RigMotion(rig);
        Quaternion Thigh(RigMove move)
        {
            motion.Pose(move);
            return motion.Turns[5];
        }
        bool Same(Quaternion a, Quaternion b) => MathF.Abs(Quaternion.Dot(a, b)) > 0.9999f;
        Assert.True(Same(Thigh(new RigMove(1, 0.75f, 0, 0, 0, 0)), Thigh(new RigMove(1, 0.25f, 0, 0, 0, 0, Ahead: -1))), "walking back plays the walk backwards");
        Assert.True(Same(Bent(0.5f), Thigh(new RigMove(1, 0.4f, 0, 0, 0, 0, Ahead: 0, Aside: 1))), "its own side walk");
        Assert.True(Same(Bent(0.3f), Thigh(new RigMove(1, 0.4f, 0, 0, 0, 0, Ahead: 0, Aside: -1))), "no side walk that way, so its run that way");
        Assert.True(Same(Bent(0.5f), Thigh(new RigMove(0, 0, 1, 0.4f, 0, 0, Ahead: 0, Aside: 1))), "no side run that way, so its walk that way");
        Assert.True(Same(Bent(0.1f), Thigh(new RigMove(0, 0, 1, 0.4f, 0, 0, Ahead: -1))), "no back run, so its run backwards");
        rig.Clips.Clear();
        rig.Clips.Add(Held("Walk", 0.2f));
        var plain = new RigMotion(rig);
        plain.Pose(new RigMove(1, 0.25f, 0, 0, 0, 0, Ahead: 0, Aside: 1));
        Assert.False(Same(Bent(0.2f), plain.Turns[5]), "with no motion to the side at all, a body it knows steps aside by itself");
    }

    [Fact]
    public void Motions_to_the_side_and_back_are_found_by_their_names()
    {
        var rig = Figure(MixamoNames);
        foreach (var name in new[] { "Walk", "Walking_Backwards", "Running_Strafe_Left", "Left Strafe Walking", "Run_Right", "Jump_Right", "Walk_Back_Melee", "Run_Back" })
            rig.Clips.Add(new RigClip(name, 1, []));
        Assert.Equal("Walking_Backwards", rig.Toward("walk", "back")!.Name);
        Assert.Equal("Left Strafe Walking", rig.Toward("walk", "left")!.Name);
        Assert.Equal("Running_Strafe_Left", rig.Toward("run", "left")!.Name);
        Assert.Equal("Run_Right", rig.Toward("run", "right")!.Name);
        Assert.Equal("Run_Back", rig.Toward("run", "back")!.Name);
        Assert.Null(rig.Toward("walk", "right"));
        Assert.Equal("Walk", rig.Clip("walk")!.Name);
    }

    [Fact]
    public void A_relaxed_hand_curls_its_fingers_toward_the_palm()
    {
        string[] fingers = ["J_Bip_L_Index1", "J_Bip_L_Index2", "J_Bip_L_Index3", "J_Bip_L_Little1", "J_Bip_L_Little2", "J_Bip_L_Little3", "J_Bip_L_Thumb1", "J_Bip_L_Thumb2"];
        int[] parents = [16, 21, 22, 16, 24, 25, 16, 27];
        Vector3[] spots =
        [
            new(0.74f, 1.48f, 0.02f), new(0.78f, 1.48f, 0.02f), new(0.81f, 1.48f, 0.02f), new(0.73f, 1.48f, -0.03f), new(0.76f, 1.48f, -0.03f), new(0.78f, 1.48f, -0.03f),
            new(0.72f, 1.47f, 0.05f), new(0.74f, 1.47f, 0.08f),
        ];
        var rig = Figure([.. VrmNames, .. fingers], [.. Parents, .. parents], [.. Spots, .. spots]);
        var (straight, _) = new RigMotion(rig).RelaxedShape();
        var root = straight[21];
        var tip = straight[23];
        var hand = straight[16];
        var reach = Vector3.Normalize(root - hand);
        var bent = tip - root - Vector3.Dot(tip - root, reach) * reach;
        Assert.True(bent.X < -0.005f, $"the index curls in toward the palm, which faces the body when the arm hangs: {hand} {root} {tip}");
        Assert.True(Vector3.Distance(straight[28], hand) > 0.03f, "the thumb stays a thumb");
    }

    [Fact]
    public void Standing_shifts_weight_from_leg_to_leg_with_both_feet_kept_where_they_stand()
    {
        var rig = Figure(VrmNames);
        var motion = new RigMotion(rig);
        var feet = new List<(Vector3 Left, Vector3 Right, Vector3 Hips)>();
        for (float seconds = 0; seconds < 11; seconds += 0.5f)
        {
            motion.Pose(new RigMove(0, 0, 0, 0, 1, 0, Seconds: seconds));
            var (points, _) = rig.Posed(motion.Points, motion.Turns);
            feet.Add((points[7], points[11], points[0]));
        }
        foreach (var (left, right, _) in feet)
        {
            Assert.True(Vector2.Distance(new(left.X, left.Z), new(0.1f, 0)) < 0.01f, $"the left foot stays put: {left}");
            Assert.True(Vector2.Distance(new(right.X, right.Z), new(-0.1f, 0)) < 0.01f, $"the right foot stays put: {right}");
        }
        Assert.True(feet.Max(f => f.Hips.X) - feet.Min(f => f.Hips.X) > 0.04f, "the hips sway over one foot and then the other");
    }

    [Fact]
    public void Feet_rest_on_a_slope_with_the_uphill_foot_higher()
    {
        var rig = Figure(VrmNames);
        var motion = new RigMotion(rig);
        motion.Pose(new RigMove(0, 0, 0, 0, 1, 0, Tilt: 0.35f));
        var (points, _) = rig.Posed(motion.Points, motion.Turns);
        Assert.True(points[11].Y - points[7].Y > 0.05f, $"with the ground rising to the right the right foot stands higher: {points[7]} {points[11]}");
        motion.Pose(new RigMove(1, 0.25f, 0, 0, 0, 0, Grade: 0.35f));
        (points, _) = rig.Posed(motion.Points, motion.Turns);
        Assert.True(points[7].Y > points[11].Y + 0.08f, $"walking uphill the leading foot lands higher: {points[7]} {points[11]}");
    }

    [Fact]
    public void A_short_legged_body_takes_quicker_shorter_strides_at_the_same_speed()
    {
        var shortened = Spots.Select(s => s.Y < 1 ? s with { Y = 0.08f + (s.Y - 0.08f) * 0.5f } : s with { Y = s.Y - 0.46f }).ToArray();
        var tall = new RigMotion(Figure(VrmNames));
        var stubby = new RigMotion(Figure(VrmNames, Parents, shortened));
        float speed = 0.8f;
        Assert.True(stubby.CycleLength(0, 1, 0, speed) < tall.CycleLength(0, 1, 0, speed) * 0.9f,
            $"its stride is shorter for its height: {stubby.CycleLength(0, 1, 0, speed)} against {tall.CycleLength(0, 1, 0, speed)}");
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
}
