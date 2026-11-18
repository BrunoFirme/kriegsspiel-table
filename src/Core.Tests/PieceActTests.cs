using KTable.Core.Model;
using KTable.Core.Protocol;
using Xunit;

namespace KTable.Core.Tests;

public class PieceActTests
{
    const string U = Layers.Umpire;

    [Fact]
    public void An_act_is_stored_on_the_piece_and_undone_in_one_step()
    {
        var g = CoreSamples.State();
        var p = g.Find(U, "p-000001")!;
        Assert.Null(p.Act);
        Assert.Null(StateMachine.Apply(g, new SetPieceAct { Layer = U, Id = p.Id, Act = RigMotion.Kneel }, out var back));
        Assert.Equal(RigMotion.Kneel, p.Act);
        Assert.Null(StateMachine.Apply(g, new SetPieceAct { Layer = U, Id = p.Id, Act = RigMotion.Cheer }, out var toKneel));
        Assert.Null(StateMachine.Apply(g, toKneel!, out _));
        Assert.Equal(RigMotion.Kneel, p.Act);
        Assert.Null(StateMachine.Apply(g, back!, out _));
        Assert.Null(p.Act);
    }

    [Fact]
    public void An_unknown_act_or_piece_is_refused_and_changes_nothing()
    {
        var g = CoreSamples.State();
        Assert.NotNull(StateMachine.Apply(g, new SetPieceAct { Layer = U, Id = "p-000001", Act = "dance" }, out _));
        Assert.NotNull(StateMachine.Apply(g, new SetPieceAct { Layer = U, Id = "nobody", Act = RigMotion.Sit }, out _));
        Assert.Null(g.Find(U, "p-000001")!.Act);
    }

    [Fact]
    public void A_body_act_and_an_arm_act_join_into_one_act_and_nothing_else_joins()
    {
        string kneelAndFire = RigMotion.Combined(RigMotion.Kneel, RigMotion.Ranged)!;
        Assert.Equal("kneel+ranged", kneelAndFire);
        Assert.Equal((RigMotion.Kneel, RigMotion.Ranged), RigMotion.Parts(kneelAndFire));
        Assert.Equal((RigMotion.Brace, (string?)null), RigMotion.Parts(RigMotion.Brace));
        Assert.Equal(((string?)null, RigMotion.Cheer), RigMotion.Parts(RigMotion.Cheer));
        Assert.Null(RigMotion.Combined(null, null));
        Assert.True(RigMotion.KnownAct("brace+guard"));
        Assert.True(RigMotion.WholeAct("brace+guard"));
        Assert.False(RigMotion.WholeAct(RigMotion.Guard));
        Assert.Equal("Kneel · Aim", RigMotion.NameOf(kneelAndFire));
        foreach (var odd in new[] { "ranged+kneel", "kneel+sit", "melee+ranged", "kneel+", "+ranged", "kneel+ranged+cheer" }) Assert.False(RigMotion.KnownAct(odd), odd);
        var g = CoreSamples.State();
        Assert.Null(StateMachine.Apply(g, new SetPieceAct { Layer = U, Id = "p-000001", Act = kneelAndFire }, out _));
        Assert.Equal(kneelAndFire, g.Find(U, "p-000001")!.Act);
        Assert.NotNull(StateMachine.Apply(g, new SetPieceAct { Layer = U, Id = "p-000001", Act = "ranged+kneel" }, out _));
    }

    [Fact]
    public void A_joined_act_bends_the_legs_like_its_body_act_and_the_arms_like_its_arm_act()
    {
        var flat = CrowdDress.Flat(Nationalities.Named("continental")!);
        var look = CrowdDress.Look(CrowdDress.Person(flat, 1, 5, 0, CrowdEntry.Man, CrowdEntry.Adult, CrowdBaseline.Soldier), flat, "#2b3d63");
        var (rig, _) = CrowdFigure.Whole(look);
        System.Numerics.Quaternion[] Turns(string act)
        {
            var m = new RigMotion(rig);
            m.Pose(new RigMove(0, 0, 0, 0, 1, 0, Act: act));
            return m.Turns.ToArray();
        }
        var both = Turns("kneel+ranged");
        var kneel = Turns(RigMotion.Kneel);
        var aim = Turns(RigMotion.Ranged);
        int leg = rig.Humanoid[RigHumanoid.Left + RigHumanoid.UpperLeg], arm = rig.Humanoid[RigHumanoid.Right + RigHumanoid.UpperArm];
        Assert.True(System.Numerics.Quaternion.Dot(both[leg], kneel[leg]) > 0.9999f);
        Assert.True(System.Numerics.Quaternion.Dot(both[arm], aim[arm]) > 0.9999f);
        Assert.True(System.Numerics.Quaternion.Dot(both[leg], aim[leg]) < 0.99f);
        Assert.True(System.Numerics.Quaternion.Dot(both[arm], kneel[arm]) < 0.99f);
    }

    [Fact]
    public void A_spear_thrusts_at_melee_and_a_shield_stays_on_its_arm_at_guard()
    {
        System.Numerics.Quaternion Arm(string nationality, string? act, int hold, string bone)
        {
            var flat = CrowdDress.Flat(Nationalities.Named(nationality)!);
            var look = CrowdDress.Look(CrowdDress.Person(flat, 1, 5, 0, CrowdEntry.Man, CrowdEntry.Adult, CrowdBaseline.Soldier), flat, "#2b3d63");
            var (rig, _) = CrowdFigure.Whole(look);
            var m = new RigMotion(rig);
            m.Pose(new RigMove(0, 0, 0, 0, 1, 0, Act: act, ActSeconds: 0.3f, Hold: hold));
            return m.Turns[rig.Humanoid[bone]];
        }
        const string forearm = RigHumanoid.Right + RigHumanoid.LowerArm, shieldArm = RigHumanoid.Left + RigHumanoid.UpperArm;
        Assert.True(System.Numerics.Quaternion.Dot(Arm("gothic", RigMotion.Melee, CrowdHolds.Spear, forearm), Arm("gothic", RigMotion.Melee, CrowdHolds.None, forearm)) < 0.99f);
        Assert.True(System.Numerics.Quaternion.Dot(Arm("roman", RigMotion.Guard, CrowdHolds.Shield, shieldArm), Arm("roman", null, CrowdHolds.Shield, shieldArm)) > 0.9999f);
    }

    [Fact]
    public void A_piece_keeps_its_act_through_a_save_and_a_copy()
    {
        var g = CoreSamples.State();
        Assert.Null(StateMachine.Apply(g, new SetPieceAct { Layer = U, Id = "p-000001", Act = RigMotion.Lie }, out _));
        var again = Json.Deserialize<GameState>(Json.Serialize(g));
        Assert.Equal(RigMotion.Lie, again.Find(U, "p-000001")!.Act);
        Assert.Equal(RigMotion.Lie, g.Find(U, "p-000001")!.Clone().Act);
        Assert.DoesNotContain("\"act\"", Json.Serialize(g.Find(U, "p-000002")!));
    }
}
