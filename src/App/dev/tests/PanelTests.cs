using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class PanelTests
{
    const string U = Layers.Umpire;

    static UiNode N(string tag, Dictionary<string, string> attrs, params UiNode[] children) => new() { Tag = tag, Attributes = attrs, Children = children.ToList() };

    static async Task Click(TestRunner r, Vector2 at)
    {
        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left });
        await r.Frame();
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        await r.Frames(3);
    }

    [NodeTest]
    static async Task A_toggle_on_a_piece_s_panel_tells_the_script_it_flipped_where_it_is_clicked_and_its_neighbour_stays(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frame();
        List<UiNode> Plate() => new()
        {
            N("Panel", new() { ["width"] = "400", ["height"] = "200", ["rotation"] = "0 0 180", ["color"] = "#202020" },
                N("Toggle", new() { ["id"] = "left", ["rectAlignment"] = "MiddleLeft", ["width"] = "200", ["height"] = "200", ["onValueChanged"] = "flipped" }),
                N("Toggle", new() { ["id"] = "right", ["rectAlignment"] = "MiddleRight", ["width"] = "200", ["height"] = "200", ["onValueChanged"] = "flipped" })),
        };
        t.Server.State.Find(U, "p1")!.Ui = Plate();
        t.S.View.Find(U, "p1")!.Ui = Plate();
        var node = t.Node("p1");
        node.SyncUi(force: true);
        await r.Frames(4);
        var quad = node.GetChildren().OfType<MeshInstance3D>().FirstOrDefault(m => m.Mesh is PlaneMesh pm && pm.Size.IsEqualApprox(new Vector2(0.4f, 0.2f)));
        Check.That(quad != null, "the panel is drawn on the piece");
        var plane = (PlaneMesh)quad!.Mesh;
        Vector2 Screen(float across) => t.Table.Rig.Cam.UnprojectPosition(quad.GlobalTransform * new Vector3(across * plane.Size.X, 0, 0));

        await Click(r, Screen(-0.25f));
        var sent = t.Server.Log.SelectMany(c => c is KTable.Core.Protocol.Batch b ? b.Items : new() { c }).OfType<KTable.Core.Protocol.UiEvent>().ToList();
        Check.That(sent.Any(e => e.Event == "onValueChanged" && e.Value == "True" && e.Path is [0, 0]), "the toggle under the click tells the script it is on");
        Check.That(!sent.Any(e => e.Path is [0, 1]), "its neighbour is untouched");
        Check.That(t.Server.State.UiNode(U, "p1", "left")?.Attributes.GetValueOrDefault("isOn") == null, "the shared panels stay as written, as in the tabletop");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }

    [NodeTest]
    static async Task Lettering_on_a_piece_is_drawn_live_and_follows_a_change(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frame();
        t.S.Do(new KTable.Core.Protocol.SetLettering { Layer = U, Id = "p1", Lettering = new KTable.Core.Model.Lettering { Text = "HQ", Size = 48, Color = "#FF0000FF" } });
        await r.Frames(3);
        Label3D? Text() => t.Node("p1").GetChildren().OfType<Label3D>().FirstOrDefault(l => l.Text is "HQ" or "Corps HQ");
        Check.Equal("HQ", Text()?.Text, "the lettering is drawn");
        Check.That(Text()?.Modulate.R > 0.9f && Text()?.Modulate.B < 0.1f, "in its colour");
        t.S.Do(new KTable.Core.Protocol.SetLettering { Layer = U, Id = "p1", Lettering = new KTable.Core.Model.Lettering { Text = "Corps HQ", Size = 48, Color = "#FF0000FF" } });
        await r.Frames(3);
        Check.Equal("Corps HQ", Text()?.Text, "a change redraws it");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }

    [NodeTest]
    static Task Rich_text_becomes_the_label_s_markup_and_inputs_keep_to_what_they_accept(TestRunner r)
    {
        Check.Equal("[b]Hold[/b] [color=#ff0000]the [lb]ridge[/color] [font_size=18]now[/font_size]",
            PanelRich.BbCode("<b>Hold</b> <color=#ff0000>the [ridge</color> <size=18>now</size>", 14), "rich text");
        Check.Equal("-12", PanelTyping.Valid("-1a2", "Integer"), "integers");
        Check.Equal("3.14", PanelTyping.Valid("3.1.4x", "Decimal"), "decimals");
        Check.Equal("abc1", PanelTyping.Valid("a b-c1", "Alphanumeric"), "letters and digits");
        return Task.CompletedTask;
    }
}
