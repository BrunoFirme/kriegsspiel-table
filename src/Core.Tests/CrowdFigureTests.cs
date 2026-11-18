using System.Numerics;
using KTable.Core.Model;
using E = KTable.Core.Model.CrowdEntry;

namespace KTable.Core.Tests;

public class CrowdFigureTests
{
    static CrowdLook Look(string nationality, string sex, string band, string calling, int seed = 5)
    {
        var flat = CrowdDress.Flat(Nationalities.Named(nationality)!);
        var person = CrowdDress.Person(flat, 1, seed, 1_000_000_000, sex, band, calling);
        return CrowdDress.Look(person, flat, "#2b3d63");
    }

    [Fact]
    public void A_look_becomes_a_body_on_the_avatar_skeleton_that_walks_and_acts()
    {
        var look = Look("continental", E.Woman, E.Adult, CrowdBaseline.Housewife);
        var (rig, shape) = CrowdFigure.Whole(look);
        Assert.True(RigHumanoid.Walks(rig.Humanoid));
        var motion = new RigMotion(rig);
        Assert.True(motion.Walks);
        float top = rig.Points.Max(p => p.Y), bottom = rig.Points.Min(p => p.Y);
        Assert.InRange(bottom, -0.01f, 0.01f);
        Assert.InRange(top, (float)look.HeightM * 0.97f, (float)look.HeightM * 1.15f);
        Assert.InRange(shape.Palette.Count, 3, CrowdFigure.WholeColours);
        Assert.Contains(CrowdSlots.Eye, shape.Palette);
        Assert.Contains(CrowdSlots.Lip, shape.Palette);
        int eye = shape.Palette.IndexOf(CrowdSlots.Eye), skin = shape.Palette.IndexOf(CrowdSlots.Skin);
        var eyes = Enumerable.Range(0, shape.Points.Count).Where(v => shape.Colour[v] == eye).Select(v => shape.Points[v]).ToList();
        int head = shape.Bone[Enumerable.Range(0, shape.Points.Count).First(k => shape.Colour[k] == eye)];
        float face = Enumerable.Range(0, shape.Points.Count).Where(v => shape.Colour[v] == skin && shape.Bone[v] == head).Max(v => shape.Points[v].Z);
        Assert.True(eyes.Max(p => p.Z) > face * 0.7f, $"the eyes sit on the front of the face ({eyes.Max(p => p.Z):0.000} by {face:0.000})");
        Assert.All(CrowdFigure.Colours(shape, look), c => Assert.StartsWith("#", c));
        motion.Pose(new RigMove(0, 0, 0, 0, 1, 0, Act: RigMotion.Kneel));
        var (knelt, _) = rig.Posed(motion.Points, motion.Turns);
        Assert.True(knelt.Max(p => p.Y) < top - 0.2f, "the crowd body kneels as an avatar body does");
    }

    static (Vector3 Low, Vector3 High) GunSpan(Rig rig, Vector3[] points, Quaternion[] turns)
    {
        var place = new RigPlace(points[CrowdFigure.Gun], turns[CrowdFigure.Gun], 1);
        var gun = Enumerable.Range(0, rig.Points.Length).Where(v => rig.Joints[v * Rig.Influences] == CrowdFigure.Gun).Select(v => place.Point(rig.Points[v])).ToList();
        Assert.NotEmpty(gun);
        return (new Vector3(gun.Min(p => p.X), gun.Min(p => p.Y), gun.Min(p => p.Z)), new Vector3(gun.Max(p => p.X), gun.Max(p => p.Y), gun.Max(p => p.Z)));
    }

    static Vector3 Global(Rig rig, Vector3[] points, Quaternion[] turns, int bone)
    {
        var place = new RigPlace(points[bone], turns[bone], 1);
        for (int b = rig.Bones[bone].Parent; b >= 0; b = rig.Bones[b].Parent) place = place.Then(new RigPlace(points[b], turns[b], 1));
        return place.Offset;
    }

    [Fact]
    public void A_musket_is_shouldered_upright_and_fired_level_from_the_right_shoulder_with_both_hands_on_it()
    {
        var look = Look("continental", E.Man, E.Adult, CrowdBaseline.Soldier);
        Assert.Equal("musket", look.Item);
        var (rig, _) = CrowdFigure.Whole(look);
        var grip = CrowdFigure.GripOf(look)!;
        float h = (float)look.HeightM;
        (Vector3[] Points, Quaternion[] Turns) Posed(string? act)
        {
            var m = new RigMotion(rig);
            m.Pose(new RigMove(0, 0, 0, 0, 1, 0, Act: act, Hold: CrowdFigure.Hold(look)));
            var (p, t) = (m.Points.ToArray(), m.Turns.ToArray());
            CrowdGrip.Hold(rig, p, t, grip, act, h);
            return (p, t);
        }
        var carried = Posed(null);
        var (low, high) = GunSpan(rig, carried.Points, carried.Turns);
        Assert.True(high.Y - low.Y > 0.5f * h, "shouldered, the musket stands upright");
        Assert.True(high.Z - low.Z < 0.4f * (high.Y - low.Y));
        var fired = Posed(RigMotion.Ranged);
        (low, high) = GunSpan(rig, fired.Points, fired.Turns);
        Assert.True(high.Z - low.Z > 0.6f * h, "fired, the barrel points ahead");
        Assert.True(high.Y - low.Y < 0.16f * h, "and lies level");
        Assert.InRange((low.Y + high.Y) / 2, 0.72f * h, 0.92f * h);
        var shoulder = Global(rig, fired.Points, fired.Turns, rig.Humanoid[RigHumanoid.Right + RigHumanoid.UpperArm]);
        Assert.True(shoulder.X < 0 && low.Z < shoulder.Z + 0.12f * h, "the butt sits at the right shoulder");
        foreach (var (hand, target) in new[] { (RigHumanoid.Left, grip.FireFore), (RigHumanoid.Right, grip.FireLock) })
        {
            var wrist = Global(rig, fired.Points, fired.Turns, rig.Humanoid[hand + RigHumanoid.Hand]);
            Assert.True(Vector3.Distance(wrist, target * h) < 0.03f * h, $"the {hand} hand reaches the stock");
        }
        var braced = Posed(RigMotion.Brace);
        (low, high) = GunSpan(rig, braced.Points, braced.Turns);
        Assert.True(high.Z - low.Z > 0.5f * h && (low.Y + high.Y) / 2 < 0.75f * h, "braced, it is levelled at the hip");
    }

    [Theory]
    [InlineData("continental", CrowdBaseline.Soldier)]
    [InlineData("continental", CrowdBaseline.Merchant)]
    [InlineData("ottoman", CrowdBaseline.Soldier)]
    [InlineData("gothic", CrowdBaseline.Soldier)]
    public void A_near_figure_wears_its_hat_above_its_head(string nationality, string calling)
    {
        var look = Look(nationality, E.Man, E.Adult, calling);
        Assert.NotNull(look.Part(Wardrobe.Hat));
        var (rig, shape) = CrowdFigure.Whole(look);
        Assert.All(rig.Points, p => Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)));
        string slot = look.Part(Wardrobe.Hat)!.Slot ?? CrowdSlots.Hat;
        int hat = shape.Palette.IndexOf(slot);
        var hatPoints = Enumerable.Range(0, rig.Points.Length).Where(v => shape.Colour[v] == hat).Select(v => rig.Points[v]).ToList();
        Assert.True(hatPoints.Count > 20, $"{nationality} {calling}: {hatPoints.Count} hat points, palette {string.Join(",", shape.Palette)}");
        var head = CrowdFigure.Headwear(look);
        Assert.Contains(slot, head.Colour.Select(head.SlotOf));
        Assert.True(hatPoints.Max(p => p.Y) > (float)look.HeightM * 0.95f, $"{hatPoints.Max(p => p.Y)} {look.HeightM}");
    }

    [Fact]
    public void A_hat_takes_the_colour_its_garment_names_so_a_mobcap_is_linen_and_a_tricorne_is_felt()
    {
        var look = Look("continental", E.Woman, E.Adult, CrowdBaseline.Housewife);
        look.Wear(new Garment { Part = Wardrobe.Hat, Kind = "mobcap", Slot = CrowdSlots.Linen });
        var (rig, shape) = CrowdFigure.Whole(look);
        int top = Enumerable.Range(0, rig.Points.Length).MaxBy(v => rig.Points[v].Y);
        Assert.Equal(CrowdSlots.Linen, shape.SlotOf(shape.Colour[top]));
        look.Wear(new Garment { Part = Wardrobe.Hat, Kind = "tricorne", Slot = CrowdSlots.Hat });
        (rig, shape) = CrowdFigure.Whole(look);
        top = Enumerable.Range(0, rig.Points.Length).MaxBy(v => rig.Points[v].Y);
        Assert.Equal(CrowdSlots.Hat, shape.SlotOf(shape.Colour[top]));
    }

    [Fact]
    public void Outfits_that_differ_only_in_colour_share_a_mesh()
    {
        var a = Look("gothic", E.Woman, E.Adult, CrowdBaseline.Housewife, 5);
        var b = a.Clone();
        b.Colours[CrowdSlots.Outer] = "#123456";
        Assert.Equal(CrowdFigure.BodyKey(a), CrowdFigure.BodyKey(b));
        Assert.Equal(CrowdFigure.HeadKey(a), CrowdFigure.HeadKey(b));
        var c = a.Clone();
        c.Wear(Garment.Of(Wardrobe.Robe, "gown", CrowdSlots.Outer, 0.4, 1.1, "shoulder"));
        Assert.NotEqual(CrowdFigure.BodyKey(a), CrowdFigure.BodyKey(c));
        Assert.Equal(CrowdFigure.HeadKey(a), CrowdFigure.HeadKey(c));
    }

    [Fact]
    public void The_street_figure_swings_its_legs_and_arms_from_the_hips_and_shoulders()
    {
        var shape = CrowdFigure.Body(Look("modern", E.Man, E.Adult, CrowdBaseline.Labourer));
        var legs = Enumerable.Range(0, shape.Points.Count).Where(i => shape.Limb[i] is CrowdShape.LeftLeg or CrowdShape.RightLeg).ToList();
        var arms = Enumerable.Range(0, shape.Points.Count).Where(i => shape.Limb[i] is CrowdShape.LeftArm or CrowdShape.RightArm).ToList();
        Assert.NotEmpty(legs);
        Assert.NotEmpty(arms);
        Assert.All(legs, i => Assert.True(shape.Points[i].Y <= shape.Pivot[i] + 0.01f));
        Assert.All(arms, i => Assert.InRange(shape.Pivot[i], 0.75f, 0.9f));
        Assert.InRange(shape.Tris, 200, 1100);
    }

    [Theory]
    [InlineData("continental", E.Man, CrowdBaseline.Labourer)]
    [InlineData("continental", E.Woman, CrowdBaseline.Housewife)]
    [InlineData("continental", E.Man, CrowdBaseline.Soldier)]
    [InlineData("gothic", E.Man, CrowdBaseline.Merchant)]
    [InlineData("ottoman", E.Man, CrowdBaseline.Soldier)]
    [InlineData("japanese", E.Woman, CrowdBaseline.Housewife)]
    public void A_street_figure_is_lofted_over_the_body_within_its_budget_and_its_clothes_clear_the_body(string nationality, string sex, string calling)
    {
        var look = Look(nationality, sex, E.Adult, calling);
        var body = CrowdFigure.Body(look);
        var head = CrowdFigure.Headwear(look);
        Assert.InRange(body.Tris, 200, 1100);
        Assert.InRange(head.Tris, 0, 500);
        Assert.InRange(body.Palette.Count, 2, CrowdShape.MostColours);
        Assert.All(body.Colour, c => Assert.True(c < body.Palette.Count || c >= CrowdShape.MostColours));
        Assert.All(body.Points, p => Assert.InRange(p.Y, -0.01f, 1.6f));
        var (rig, whole) = CrowdFigure.Whole(look);
        Assert.True(whole.Tris > body.Tris + head.Tris, "the near person is finer than the street figure");
        Assert.Equal(whole.Points.Count, rig.Points.Length);
    }

    [Fact]
    public void Every_shipped_nationality_dresses_bodies_that_build()
    {
        foreach (var n in Nationalities.All)
            foreach (var (sex, calling) in new[] { (E.Man, CrowdBaseline.Labourer), (E.Man, CrowdBaseline.Soldier), (E.Man, CrowdBaseline.Merchant), (E.Woman, CrowdBaseline.Housewife) })
                foreach (var band in CrowdBaseline.Bands)
                {
                    var (rig, shape) = CrowdFigure.Whole(Look(n.Id, sex, band, calling));
                    Assert.True(rig.Points.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)), $"{n.Id} {sex} {band} {calling}");
                    Assert.Equal(shape.Points.Count * Rig.Influences, rig.Joints.Length);
                    Assert.True(shape.Colour.All(c => c < shape.Palette.Count));
                }
    }
}
