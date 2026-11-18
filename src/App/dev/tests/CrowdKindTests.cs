using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class CrowdKindTests
{
    [NodeTest]
    static async Task A_pack_kind_as_a_person_is_frozen_poses_of_its_mesh_at_street_distance_and_its_own_rig_near(TestRunner r)
    {
        var dir = AvatarRigTests.Scratch();
        try
        {
            var body = RigImport.Read(AvatarRigTests.Exported(dir, 1), out var error) ?? throw new TestFailure("the test figure reads: " + error);
            var kind = CrowdKinds.Build(body.Rig, new StandardMaterial3D());
            Check.Equal(4, kind.Frozen.Length, "a stand, two strides and arms up");
            var tops = kind.Frozen.Select(m => m.GetAabb()).ToList();
            Check.That(tops.Take(3).All(b => b.Size.Y is > 0.85f and < 1.15f), "standing and striding, a frozen pose is one person tall: " + string.Join(", ", tops.Select(b => b.Size.Y.ToString("0.00"))));
            Check.That(tops[CrowdKinds.ArmsUp].Size.Y > tops[CrowdKinds.Stand].Size.Y + 0.05f, "with the arms up, taller");
            Check.That(tops[CrowdKinds.StrideA].Size.Z > tops[CrowdKinds.Stand].Size.Z + 0.01f, "a stride spreads the legs");
            Check.Equal(CrowdKinds.StrideB, CrowdKinds.Pose(true, 0.8f, null), "walking picks a stride");
            Check.Equal(CrowdKinds.ArmsUp, CrowdKinds.Pose(false, 0, RigMotion.Cheer), "cheering raises the arms");
            var root = new Node3D();
            r.AddChild(root);
            var near = new CrowdNear(root);
            near.Begin();
            near.Show((0, 0), 0, CrowdWardrobe.Fresh(), Vector3.Zero, 0, 1, true, 0.3f, 0.016f, null, kind);
            near.End();
            await r.Frames(1);
            var skinned = root.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().Single();
            Check.That(skinned.Mesh == kind.Skinned && skinned.Skin != null, "near, it is its own skinned rig");
            root.QueueFree();
        }
        finally { Directory.Delete(dir, true); }
    }
}
