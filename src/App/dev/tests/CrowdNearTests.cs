using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class CrowdNearTests
{
    [NodeTest]
    static async Task A_near_person_faces_their_heading_and_reaches_forward_when_firing(TestRunner r)
    {
        var root = new Node3D();
        r.AddChild(root);
        var near = new CrowdNear(root);
        var look = CrowdWardrobe.Fresh();
        near.Begin();
        near.Show((0, 0), 0, look, Vector3.Zero, Mathf.Pi, 1, false, 0, 0.016f, RigMotion.Ranged);
        near.End();
        await r.Frames(2);
        var skeleton = root.FindChildren("*", "Skeleton3D", true, false).OfType<Skeleton3D>().Single();
        Vector3 At(string bone) => skeleton.ToGlobal(skeleton.GetBoneGlobalPose(skeleton.FindBone(bone)).Origin);
        var chest = At("Chest");
        var hand = At("RightHand");
        var toes = At("LeftToes");
        var ankle = At("LeftFoot");
        Check.That(toes.Z < ankle.Z, $"the toes point the way they face, map north ({toes.Z:0.00} before {ankle.Z:0.00})");
        var mesh = root.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().Single().Mesh;
        var colours = (Color[])mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Color];
        var eye = Coords.ParseColor(look.Colours[CrowdSlots.Eye], Colors.Gray);
        Check.That(colours.Any(c => c.IsEqualApprox(eye)), "a near person has eyes");
        Check.That(hand.Z < chest.Z - 0.2f, $"firing, the hands reach the way they face ({hand.Z:0.00} against the chest's {chest.Z:0.00})");
        root.QueueFree();
    }

    [NodeTest]
    static async Task A_near_place_takes_a_new_body_when_someone_else_stands_in_it(TestRunner r)
    {
        var root = new Node3D();
        r.AddChild(root);
        var near = new CrowdNear(root);
        var first = CrowdWardrobe.Fresh();
        var second = CrowdWardrobe.Fresh();
        second.Wear(new Garment { Part = Wardrobe.Hat, Kind = "tricorne", Slot = CrowdSlots.Hat });
        Mesh Shown() => root.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().Single(m => !m.IsQueuedForDeletion()).Mesh;
        near.Begin();
        near.Show((0, 0), 0, first, Vector3.Zero, 0, 1, false, 0, 0.016f);
        near.End();
        await r.Frames(1);
        var before = Shown();
        near.Begin();
        near.Show((0, 0), 0, second, Vector3.Zero, 0, 1, false, 0, 0.016f);
        near.End();
        await r.Frames(2);
        Check.That(!ReferenceEquals(before, Shown()), "a crowd removed and another made in its place is drawn as the new people, not the old");
        root.QueueFree();
    }
}
