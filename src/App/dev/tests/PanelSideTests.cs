using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class PanelSideTests
{
    const string U = Layers.Umpire;
    const double FrameSeconds = 0.02, FlipSeconds = 0.6;
    const float CanvasSide = 400, ObjectUnit = 0.6129f / 2, PanelUnit = 0.01f;

    const string Script = """
        function pressed() self.set_memo("pressed") end
        function on_load()
          self.ui.set([[<Panel id="root" width="400" height="400">
            <Text id="front" position="-100 0 -11" rotation="180 180 0" width="100" height="100">Front</Text>
            <Button id="back" position="100 0 5" rotation="180 0 0" width="100" height="100" onClick="pressed">Back</Button>
            <Text id="count" position="100 150 5" rotation="180 0 0" width="100" height="40" color="#FFFFFF">Count</Text>
          </Panel>]])
        end
        """;

    static OfflineTable Table(TestRunner r) => OfflineTable.Make(r, pieces: 1,
        prepare: store => store.RegisterDef(new PackManifest { Id = "mod", Name = "Module" },
            new PieceDef { Id = "tile", Name = "Tile", ModuleType = "Custom_Tile", ModuleRise = 1, ModuleOrigin = 0, Size = new Size2(0.856, 0.6129), Thickness = 0.1 }),
        arrange: g => g.Layers[U][0].Def = "tile");

    static async Task Frames(OfflineTable t, TestRunner r, int n, int from = 0)
    {
        for (int frame = from + 1; frame <= from + n; frame++)
        {
            foreach (var o in t.Server.Scripts.Tick(frame * FrameSeconds))
                if (o.Message is CommandMessage cm) t.S.OnCommand(cm.Command);
            await r.Frame();
        }
    }

    static IEnumerable<T> Under<T>(Node n) where T : Node
    {
        foreach (var c in n.GetChildren())
        {
            if (c is T t) yield return t;
            foreach (var d in Under<T>(c)) yield return d;
        }
    }

    static List<string> Words(Node port) => Under<Label>(port).Where(l => l.IsVisibleInTree()).Select(l => l.Text.Trim()).ToList();

    static List<MeshInstance3D> Sheets(Node node) => node.GetChildren().OfType<MeshInstance3D>()
        .Where(m => !m.IsQueuedForDeletion() && m.Mesh is PlaneMesh && m.MaterialOverride is StandardMaterial3D { AlbedoTexture: ViewportTexture }).ToList();

    static MeshInstance3D Underside(Node node) => Sheets(node).Single(q => ((PlaneMesh)q.Mesh).FlipFaces);

    static Vec2 BackButtonOn(MeshInstance3D quad)
    {
        var size = ((PlaneMesh)quad.Mesh).Size;
        return Coords.FromWorld(quad.GlobalTransform * new Vector3((300 / CanvasSide - 0.5f) * size.X, 0, 0));
    }

    [NodeTest]
    static async Task A_module_tile_s_panel_puts_what_sits_below_it_on_its_underside_which_answers_only_once_the_tile_is_turned_over(TestRunner r)
    {
        var t = Table(r);
        await r.Frame();
        t.S.Do(new SetScript { Layer = U, Id = "p1", Text = Script });
        await Frames(t, r, 8);
        var node = t.Node("p1");
        var ports = node.GetChildren().OfType<SubViewport>().Where(v => !v.IsQueuedForDeletion()).ToList();
        var quads = Sheets(node);
        Check.Equal(2, ports.Count, "a top sheet and an underside sheet");
        var top = quads.Single(q => !((PlaneMesh)q.Mesh).FlipFaces);
        var under = Underside(node);
        Check.That(under.Position.Y < 0 && top.Position.Y > 0.1f, $"the underside sheet lies under the tile and the top one above it: {under.Position.Y}, {top.Position.Y}");
        float side = CanvasSide * PanelUnit * ObjectUnit;
        Check.That(((PlaneMesh)top.Mesh).Size.IsEqualApprox(new Vector2(side, side)), $"a panel unit is a hundredth of the tile's object unit: {((PlaneMesh)top.Mesh).Size} for {side}");
        Check.That(Words(ports[0]).Contains("Front") && !Words(ports[0]).Contains("Back") && !Words(ports[0]).Contains("Count"), $"the top sheet shows only the front: {string.Join(", ", Words(ports[0]))}");
        Check.That(Words(ports[1]).Contains("Back") && Words(ports[1]).Contains("Count") && !Words(ports[1]).Contains("Front"), $"the underside shows the controls: {string.Join(", ", Words(ports[1]))}");
        var count = Under<Label>(ports[1]).Where(l => l.Text.Trim() == "Count").ToList();
        Check.That(count.Count > 0 && count.All(l => !l.ClipText && l.MaxLinesVisible >= 1), "words turned to read from below are cut by lines, since a mirrored clip would hide them");

        Check.That(node.PanelAt(BackButtonOn(under)) is not { } early || !early.Id.Contains("back"), "the underside button does not answer through the tile's face");
        t.S.Do(new FlipPiece { Layer = U, Id = "p1", FaceDown = true });
        await r.Seconds(FlipSeconds);
        await Frames(t, r, 4, 8);
        var shown = BackButtonOn(Underside(node));
        Check.That(node.PanelAt(shown) is { } hit && hit.Id.StartsWith("1:", System.StringComparison.Ordinal), $"the turned-over tile's underside answers where its button shows: {node.PanelAt(shown)?.Id ?? "nothing"}");
        node.PressPanel(node.PanelAt(shown)!.Value);
        await Frames(t, r, 4, 12);
        var sent = t.Server.Log.SelectMany(c => c is Batch b ? b.Items : new() { c }).OfType<UiEvent>().ToList();
        Check.That(sent.Any(e => e.Event == "onClick"), "pressing it tells the script");
        Check.Equal("pressed", t.Server.State.Find(U, "p1")!.Memo, "the script ran its handler");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }
}
