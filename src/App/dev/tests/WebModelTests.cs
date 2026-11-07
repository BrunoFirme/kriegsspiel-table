using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class WebModelTests
{
    const string U = Layers.Umpire;
    static readonly string Mesh = "models/" + new string('f', 64) + ".obj";

    const string Script = """
        function on_load()
          self.set_web_model{ mesh = "https://example.test/" .. self.id .. ".obj" }
        end
        """;

    static void Deliver(OfflineTable t, double now)
    {
        foreach (var o in t.Server.Scripts.Tick(now))
            if (o.Message is CommandMessage cm) t.S.OnCommand(cm.Command);
    }

    static void WriteModel(KTable.Core.PackageStore store)
    {
        var file = Path.Combine(store.PackageDir(UploadAsset.ReliefPackage), Mesh);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, string.Join('\n', "v 0 0 0", "v 1 0 0", "v 0 1 0", "v 0 0 1", "f 1 2 3", "f 1 3 4", ""));
    }

    [NodeTest]
    static async Task A_model_fetched_from_a_link_is_drawn_on_the_piece_and_a_bad_link_leaves_a_plain_placeholder(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 2, prepare: WriteModel);
        t.Server.Scripts.FetchModel = link => Task.FromResult<string?>(link.Contains("p1") ? Mesh : null);
        t.Server.Scripts.FetchPicture = _ => Task.FromResult<string?>(null);
        await r.Frames(2);
        t.S.Do(new SetScript { Layer = U, Id = "p1", Text = Script });
        t.S.Do(new SetScript { Layer = U, Id = "p2", Text = Script });
        for (int i = 1; i <= 4; i++)
        {
            Deliver(t, i * 0.1);
            await r.Frames(1);
        }
        await r.Frames(2);
        Check.Equal(Mesh, t.S.View.Find(U, "p1")!.WebMesh ?? "", "the first piece wears the fetched model");
        Check.Equal("", t.S.View.Find(U, "p2")!.WebMesh ?? "none", "a link that gives no model leaves the placeholder");
        var model = t.Node("p1").FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().Any(m => m.Mesh is ArrayMesh);
        Check.That(model, "the fetched model is built from its file");
        Check.That(t.Node("p2").GetChildCount() > 0, "the placeholder is drawn");
        Check.That(t.Errors.Count == 0, "no command refused: " + string.Join("; ", t.Errors));
        t.Table.QueueFree();
    }
}
