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
        Assert.All(CrowdFigure.Colours(shape, look), c => Assert.StartsWith("#", c));
        motion.Pose(new RigMove(0, 0, 0, 0, 1, 0, Act: RigMotion.Kneel));
        var (knelt, _) = rig.Posed(motion.Points, motion.Turns);
        Assert.True(knelt.Max(p => p.Y) < top - 0.2f, "the crowd body kneels as an avatar body does");
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
        Assert.True(shape.Tris < 700, $"a street figure stays light ({shape.Tris} triangles)");
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
