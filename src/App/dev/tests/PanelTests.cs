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
        Vector2 Screen(float across, float up) => t.Table.Rig.Cam.UnprojectPosition(quad.GlobalTransform * new Vector3(across * plane.Size.X, 0, -up * plane.Size.Y));
        const float ToggleBoxAbove = 32f / 200f;

        await Click(r, Screen(-0.25f, ToggleBoxAbove));
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

    static Vector2 Middle(Control c) => c.GetGlobalRect().GetCenter();

    static async Task Move(TestRunner r, Vector2 at, MouseButtonMask held = 0)
    {
        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, ButtonMask = held });
        await r.Frame();
    }

    [NodeTest]
    static async Task Screen_panels_answer_clicks_hovers_ticks_slides_scrolls_drags_and_hide_animations_as_the_game_s_do(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        const string Xml = """
            <Panel id="box" width="600" height="400" color="#202020" allowDragging="true" hideAnimation="FadeOut" animationDuration="0.05">
              <VerticalLayout padding="10 10 10 10" spacing="6">
                <Button id="go" onClick="go" onMouseEnter="in" onMouseExit="out" textColor="#FFFFFF" fontSize="20" outline="#000000">Go <b>now</b></Button>
                <Toggle id="tick" onValueChanged="ticked">Lock</Toggle>
                <Slider id="slide" minValue="0" maxValue="10" onValueChanged="slid"/>
                <ProgressBar percentage="40"/>
                <VerticalScrollView id="list" preferredHeight="80"><VerticalLayout><Text>One</Text><Text>Two</Text></VerticalLayout></VerticalScrollView>
              </VerticalLayout>
            </Panel>
            """;
        var layout = KTable.Core.Panels.PanelBuild.Of(Xml).Layout;
        var data = KTable.Core.Panels.PanelData.Parse(Xml)!;
        t.Server.State.Ui = data;
        t.Server.State.UiLayout = layout;
        t.S.View.Ui = data.Select(n => n.Clone()).ToList();
        t.S.View.UiLayout = layout.Clone();
        var ui = new ScriptUi();
        r.AddChild(ui);
        ui.Bind(t.S, null!);
        await r.Frames(2);
        List<KTable.Core.Protocol.UiEvent> Sent() => t.Server.Log.SelectMany(c => c is KTable.Core.Protocol.Batch b ? b.Items : new() { c }).OfType<KTable.Core.Protocol.UiEvent>().ToList();
        List<Control> Takers() => ui.FindChildren("*", "Control", true, false).OfType<Control>()
            .Where(c => GodotObject.IsInstanceValid(c) && !c.IsQueuedForDeletion() && c.MouseFilter == Control.MouseFilterEnum.Stop && c.IsVisibleInTree()).ToList();
        Check.That(Takers().Count >= 3, "the button, the toggle box and the slider take the pointer");
        await Move(r, Middle(Takers()[0]));
        await Click(r, Middle(Takers()[0]));
        await Move(r, new Vector2(1, 1));
        Check.That(Sent().Any(e => e.Event == "onClick" && e.Value == "-1" && e.Path is [0, 0, 0]), "the button's click reaches its function with the left pointer");
        Check.That(Sent().Any(e => e.Event == "onMouseEnter") && Sent().Any(e => e.Event == "onMouseExit"), "and its pointer in and out");
        for (int i = 1; i <= 3; i++)
        {
            await Click(r, Middle(Takers()[i]));
            await r.Frames(2);
        }
        Check.That(Sent().Any(e => e.Event == "onValueChanged" && e.Value == "True" && e.Path is [0, 0, 1]), "the toggle tells its function it is on");
        Check.That(Sent().Any(e => e.Event == "onValueChanged" && e.Path is [0, 0, 2]), "the slider tells its function where it was set");
        var wheelAt = Middle(Takers().Last());
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true, Position = wheelAt, GlobalPosition = wheelAt });
        await r.Frame();
        var hide = new KTable.Core.Protocol.SetUiAttributes { Id = "box", Kind = KTable.Core.Panels.PanelOps.Hide };
        KTable.Core.Panels.PanelOps.Run(t.S.View.Ui, t.S.View.UiLayout, hide.Kind, hide.Id, new Dictionary<string, string>(), null);
        ui.Applied(hide);
        await r.Frames(2);
        Check.That(ui.GetChildCount() > 0, "a hiding panel stays drawn while its fade runs");
        await r.Frames(12);
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
        ui.QueueFree();
    }

    [NodeTest]
    static async Task A_screen_field_takes_typing_in_place_its_dropdown_opens_the_game_s_list_and_an_accordion_section_opens_on_its_header(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        const string Xml = """
            <VerticalLayout id="col" width="400" height="500" spacing="10" childForceExpandHeight="false">
              <InputField id="name" preferredHeight="30" onValueChanged="typed" onEndEdit="done"/>
              <Dropdown id="pick" preferredHeight="30" onValueChanged="picked"><Option>Red</Option><Option>Blue</Option></Dropdown>
              <Accordion id="acc" preferredHeight="300"><AccordionSection id="sec"><AccordionSectionHeader id="head"/>
                <AccordionSectionContent><Text>Inside</Text></AccordionSectionContent></AccordionSection></Accordion>
            </VerticalLayout>
            """;
        var layout = KTable.Core.Panels.PanelBuild.Of(Xml).Layout;
        var data = KTable.Core.Panels.PanelData.Parse(Xml)!;
        t.Server.State.Ui = data;
        t.Server.State.UiLayout = layout;
        t.S.View.Ui = data.Select(n => n.Clone()).ToList();
        t.S.View.UiLayout = layout.Clone();
        var ui = new ScriptUi();
        r.AddChild(ui);
        ui.Bind(t.S, null!);
        await r.Frames(2);
        List<KTable.Core.Protocol.UiEvent> Sent() => t.Server.Log.SelectMany(c => c is KTable.Core.Protocol.Batch b ? b.Items : new() { c }).OfType<KTable.Core.Protocol.UiEvent>().ToList();
        List<Control> Takers() => ui.FindChildren("*", "Control", true, false).OfType<Control>()
            .Where(c => GodotObject.IsInstanceValid(c) && !c.IsQueuedForDeletion() && c.MouseFilter == Control.MouseFilterEnum.Stop && c.IsVisibleInTree()).ToList();
        var field = Takers()[0];
        float fieldBottom = field.GetGlobalRect().End.Y;
        await Click(r, Middle(field));
        await r.Frames(2);
        var editor = ui.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>().FirstOrDefault(e => GodotObject.IsInstanceValid(e) && !e.IsQueuedForDeletion());
        Check.That(editor != null && editor.HasFocus(), "a click on the field puts a caret in it, in the panel itself");
        foreach (var ch in "Ney")
        {
            Input.ParseInputEvent(new InputEventKey { Pressed = true, Unicode = ch, Keycode = (Key)char.ToUpperInvariant(ch) });
            Input.ParseInputEvent(new InputEventKey { Pressed = false, Unicode = ch, Keycode = (Key)char.ToUpperInvariant(ch) });
            await r.Frame();
        }
        await r.Frames(2);
        Check.That(Sent().Any(e => e.Event == "onValueChanged" && e.Value == "Ney"), "each change tells the script the text so far");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, Pressed = true });
        await r.Frames(3);
        Check.That(Sent().Any(e => e.Event == "onEndEdit" && e.Value == "Ney"), "Enter ends the typing and says the text");
        var dropdown = Takers().First(c => c.GetGlobalRect().Position.Y >= fieldBottom && Mathf.IsEqualApprox(c.Size.Y, 30));
        float dropdownMiddle = dropdown.GetGlobalRect().GetCenter().Y;
        await Click(r, Middle(dropdown));
        await r.Frames(3);
        var rows = Takers().Where(c => c.GetGlobalRect().Position.Y > dropdownMiddle && c.Size.Y >= 29 && c.Size.Y <= 31).ToList();
        Check.That(rows.Count >= 2, "the dropdown opens its list of thirty-high rows under itself");
        await Click(r, Middle(rows.OrderBy(c => c.GetGlobalRect().Position.Y).ElementAt(1)));
        await r.Frames(3);
        Check.That(Sent().Any(e => e.Event == "onValueChanged" && e.Path is [0, 1] && e.Value == "1"),
            "a row picked tells the script its place");
        var header = Takers().OrderBy(c => c.GetGlobalRect().Position.Y).Last(c => Mathf.IsEqualApprox(c.Size.Y, 64));
        var before = header.GetParentControl()!.Size.Y;
        await Click(r, Middle(header));
        await r.Seconds(0.5);
        await r.Frames(3);
        var opened = Takers().OrderBy(c => c.GetGlobalRect().Position.Y).Last(c => Mathf.IsEqualApprox(c.Size.Y, 64)).GetParentControl()!.Size.Y;
        Check.That(Mathf.Abs(opened - before - 128) < 0.5f, $"the section grows by its content height once open ({before} to {opened})");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
        ui.QueueFree();
    }

    [NodeTest]
    static Task Panel_font_names_reach_the_bundled_open_licence_files_with_the_weight_they_name_and_others_stay_on_system_fonts(TestRunner r)
    {
        Check.That(PanelFonts.Find("Bangers/Bangers-Regular") is FontFile, "a family with one file is that file");
        Check.That(PanelFonts.Find("Fonts/Crete_Round/CreteRound-Italic") is FontFile { ResourcePath: var italic } && italic.EndsWith("CreteRound-Italic.ttf"), "the game's Fonts/ prefix and an italic file");
        var bold = PanelFonts.Find("Montserrat/Montserrat-Bold") as FontVariation;
        Check.That(bold != null && bold.VariationOpentype.Count == 1, "a weight of a variable family is a variation of its file");
        var thinItalic = PanelFonts.Find("IBM_Plex_Sans/IBMPlexSans-ThinItalic") as FontVariation;
        Check.That(thinItalic?.BaseFont is FontFile { ResourcePath: var slanted } && slanted.Contains("Italic"), "an italic weight comes from the italic file");
        Check.That(PanelFonts.Find("Fonts/Arial") == null, "fonts the game does not ship openly stay on system fonts");
        var shipped = DirAccess.GetFilesAt("res://fonts/panels");
        const int Families = 16;
        Check.Equal(Families, shipped.Count(f => f.EndsWith("-OFL.txt") || f.EndsWith("-LICENSE.txt")), "each of the sixteen families ships its licence beside its files");
        return Task.CompletedTask;
    }
}
