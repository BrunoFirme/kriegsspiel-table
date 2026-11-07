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
    static async Task A_field_on_a_piece_s_panel_takes_typing_and_a_click_on_it_keeps_the_typing_going_while_a_click_elsewhere_ends_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frame();
        List<UiNode> Plate() => new()
        {
            N("Panel", new() { ["width"] = "400", ["height"] = "200", ["rotation"] = "0 0 180", ["color"] = "#202020" },
                N("InputField", new() { ["id"] = "f", ["width"] = "400", ["height"] = "200", ["onValueChanged"] = "typed", ["onEndEdit"] = "done" })),
        };
        t.Server.State.Find(U, "p1")!.Ui = Plate();
        t.S.View.Find(U, "p1")!.Ui = Plate();
        var ui = new ScriptUi();
        r.AddChild(ui);
        ui.Bind(t.S, null!);
        var node = t.Node("p1");
        node.SyncUi(force: true);
        await r.Frames(4);
        var quad = node.GetChildren().OfType<MeshInstance3D>().First(m => m.Mesh is PlaneMesh pm && pm.Size.IsEqualApprox(new Vector2(0.4f, 0.2f)));
        var plane = (PlaneMesh)quad.Mesh;
        Vector2 Screen(float across, float up) => t.Table.Rig.Cam.UnprojectPosition(quad.GlobalTransform * new Vector3(across * plane.Size.X, 0, -up * plane.Size.Y));
        List<KTable.Core.Protocol.UiEvent> Sent() => t.Server.Log.SelectMany(c => c is KTable.Core.Protocol.Batch b ? b.Items : new() { c }).OfType<KTable.Core.Protocol.UiEvent>().ToList();
        LineEdit? Editor() => node.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>().FirstOrDefault(e => GodotObject.IsInstanceValid(e) && !e.IsQueuedForDeletion());
        await Click(r, Screen(0, 0));
        await r.Frames(2);
        Check.That(Editor() != null, "a click on the field starts typing in the piece's panel itself");
        foreach (var ch in "ab")
        {
            Input.ParseInputEvent(new InputEventKey { Pressed = true, Unicode = ch, Keycode = (Key)char.ToUpperInvariant(ch) });
            Input.ParseInputEvent(new InputEventKey { Pressed = false, Unicode = ch, Keycode = (Key)char.ToUpperInvariant(ch) });
            await r.Frame();
        }
        await r.Frames(2);
        Check.That(Sent().Any(e => e.Event == "onValueChanged" && e.Value == "ab"), "keys reach the piece's field");
        await Click(r, Screen(0.2f, 0.1f));
        await r.Frames(2);
        Check.That(!Sent().Any(e => e.Event == "onEndEdit"), "a second click on the same field keeps the typing going");
        Check.That(Editor() != null, "and the caret stays");
        await Click(r, new Vector2(2, 2));
        await r.Frames(3);
        Check.That(Sent().Any(e => e.Event == "onEndEdit" && e.Value == "ab"), "a click elsewhere ends it with the text");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
        ui.QueueFree();
    }

    [NodeTest]
    static async Task Tab_from_a_screen_field_reaches_a_button_on_a_piece_s_panel_and_enter_clicks_it_as_unity_searches_every_selectable(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frame();
        List<UiNode> Plate() => new()
        {
            N("Panel", new() { ["width"] = "400", ["height"] = "200", ["rotation"] = "0 0 180" },
                N("Button", new() { ["id"] = "fire", ["width"] = "400", ["height"] = "200", ["onClick"] = "fired" })),
        };
        t.Server.State.Find(U, "p1")!.Ui = Plate();
        t.S.View.Find(U, "p1")!.Ui = Plate();
        const string Xml = """<InputField id="name" width="300" height="40" rectAlignment="UpperCenter"/>""";
        var layout = KTable.Core.Panels.PanelBuild.Of(Xml).Layout;
        var data = KTable.Core.Panels.PanelData.Parse(Xml)!;
        t.Server.State.Ui = data;
        t.Server.State.UiLayout = layout;
        t.S.View.Ui = data.Select(n => n.Clone()).ToList();
        t.S.View.UiLayout = layout.Clone();
        var ui = new ScriptUi();
        r.AddChild(ui);
        ui.Bind(t.S, null!);
        t.Node("p1").SyncUi(force: true);
        await r.Frames(4);
        var field = ui.FindChildren("*", "Control", true, false).OfType<Control>().First(c => c.MouseFilter == Control.MouseFilterEnum.Stop && c.IsVisibleInTree());
        await Click(r, Middle(field));
        await r.Frames(2);
        Input.ParseInputEvent(new InputEventKey { Pressed = true, Keycode = Key.Tab });
        await r.Frames(3);
        Input.ParseInputEvent(new InputEventKey { Pressed = true, Keycode = Key.Enter });
        await r.Frames(3);
        var sent = t.Server.Log.SelectMany(c => c is KTable.Core.Protocol.Batch b ? b.Items : new() { c }).OfType<KTable.Core.Protocol.UiEvent>().ToList();
        Check.That(sent.Any(e => e.Piece == "p1" && e.Event == "onClick" && e.Path is [0, 0]), "Tab moves from the screen's field to the piece's button and Enter clicks it");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
        ui.QueueFree();
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

    [NodeTest]
    static async Task A_canvas_with_a_higher_sorting_order_takes_the_click_over_one_written_after_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        const string Xml = """
            <Canvas id="high" sortingOrder="5" rectAlignment="MiddleCenter" width="400" height="300"><Button id="top" width="300" height="200" onClick="hit"/></Canvas>
            <Canvas id="plain" rectAlignment="MiddleCenter" width="400" height="300"><Button id="under" width="300" height="200" onClick="hit"/></Canvas>
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
        await r.Frames(3);
        var button = ui.FindChildren("*", "Control", true, false).OfType<Control>()
            .First(c => c.MouseFilter == Control.MouseFilterEnum.Stop && c.IsVisibleInTree() && c.Size.IsEqualApprox(new Vector2(300, 200)));
        await Click(r, Middle(button));
        var sent = t.Server.Log.SelectMany(c => c is KTable.Core.Protocol.Batch b ? b.Items : new() { c }).OfType<KTable.Core.Protocol.UiEvent>().Where(e => e.Event == "onClick").ToList();
        Check.That(sent.Count == 1 && sent[0].Path is [0, 0], "the button on the sorted canvas takes the click, not the one written after it");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
        ui.QueueFree();
    }

    [ScreenTest]
    static async Task A_mask_cuts_its_children_all_or_nothing_at_its_picture_s_edge_as_a_stencil_does(TestRunner r)
    {
        const int Side = 128;
        const string Xml = """<Mask id="m" width="128" height="128" image="Sprites/Elements/UISprite_XmlLayout" showMaskGraphic="false"><Image color="#FF0000"/></Mask>""";
        var layout = KTable.Core.Panels.PanelBuild.Of(Xml).Layout;
        var scene = KTable.Client.Panels.PanelScene.Build(layout, new KTable.Client.Panels.V2(Side, Side), new KTable.Client.Panels.PanelContext(layout.Colors, "Black", true, true),
            new PanelMeter(_ => null));
        var port = new SubViewport { Size = new Vector2I(Side, Side), TransparentBg = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        port.AddChild(new ColorRect { Color = Colors.Black, Size = new Vector2(Side, Side) });
        var view = new PanelView(new PanelHooks((_, _, _) => { }, (_, _) => null, new Dictionary<string, string>(), () => { }));
        port.AddChild(view.Show(scene, scene.Root));
        r.AddChild(port);
        await r.Frames(6);
        var image = port.GetTexture().GetImage();
        var inside = image.GetPixel(Side / 2, Side / 2);
        const float Near = 0.02f;
        static float Gap(Color a, Color b) => Mathf.Max(Mathf.Max(Mathf.Abs(a.R - b.R), Mathf.Abs(a.G - b.G)), Mathf.Abs(a.B - b.B));
        int shown = 0, hidden = 0, between = 0;
        for (int x = 0; x < Side; x++)
            for (int y = 0; y < Side; y++)
            {
                var c = image.GetPixel(x, y);
                if (Gap(c, inside) < Near) shown++;
                else if (Gap(c, Colors.Black) < Near) hidden++;
                else between++;
            }
        Check.That(inside.R > inside.G + 0.5f, $"the child shows red inside the mask ({inside})");
        Check.That(shown > 0 && hidden > 0, $"the mask shows its child inside its picture and nothing past its rounded corners ({shown} shown, {hidden} hidden)");
        Check.Equal(0, between, "no pixel is half masked");
        port.QueueFree();
    }

    [ScreenTest]
    static async Task A_tick_used_as_a_mask_cuts_its_children_all_or_nothing_too(TestRunner r)
    {
        const int Side = 128;
        const string Xml = """<Mask width="128" height="128" image="Sprites/Elements/Checkmark" showMaskGraphic="false"><Image color="#FF0000"/></Mask>""";
        var layout = KTable.Core.Panels.PanelBuild.Of(Xml).Layout;
        var scene = KTable.Client.Panels.PanelScene.Build(layout, new KTable.Client.Panels.V2(Side, Side), new KTable.Client.Panels.PanelContext(layout.Colors, "Black", true, true),
            new PanelMeter(_ => null));
        var port = new SubViewport { Size = new Vector2I(Side, Side), TransparentBg = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        port.AddChild(new ColorRect { Color = Colors.Black, Size = new Vector2(Side, Side) });
        var view = new PanelView(new PanelHooks((_, _, _) => { }, (_, _) => null, new Dictionary<string, string>(), () => { }));
        port.AddChild(view.Show(scene, scene.Root));
        r.AddChild(port);
        await r.Frames(6);
        var image = port.GetTexture().GetImage();
        const float Near = 0.02f;
        int shown = 0, hidden = 0, between = 0;
        for (int x = 0; x < Side; x++)
            for (int y = 0; y < Side; y++)
            {
                var c = image.GetPixel(x, y);
                if (c.R > 1 - Near && c.G < Near && c.B < Near) shown++;
                else if (c.R < Near && c.G < Near && c.B < Near) hidden++;
                else between++;
            }
        Check.That(shown > 0 && hidden > 0, $"the red shows through the tick's shape only ({shown} shown, {hidden} hidden)");
        Check.Equal(0, between, "no pixel at the tick's edge is half masked");
        port.QueueFree();
    }

    [NodeTest]
    static async Task A_dragged_accordion_section_waits_for_its_gap_to_close_and_open_and_lands_where_the_gap_was(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        const string Xml = """
            <Accordion id="acc" width="300" height="400" allowSectionReordering="true" expandSectionAfterReorder="false">
              <AccordionSection id="s1"><AccordionSectionHeader id="h1"/><AccordionSectionContent/></AccordionSection>
              <AccordionSection id="s2"><AccordionSectionHeader id="h2"/><AccordionSectionContent/></AccordionSection>
              <AccordionSection id="s3"><AccordionSectionHeader id="h3"/><AccordionSectionContent/></AccordionSection>
            </Accordion>
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
        await r.Frames(3);
        List<Control> Headers() => ui.FindChildren("*", "Control", true, false).OfType<Control>()
            .Where(c => GodotObject.IsInstanceValid(c) && c.MouseFilter == Control.MouseFilterEnum.Stop && c.IsVisibleInTree() && Mathf.IsEqualApprox(c.Size.Y, 64))
            .OrderBy(c => c.GetGlobalRect().Position.Y).ToList();
        var first = Middle(Headers()[0]);
        var below = Headers()[2].GetGlobalRect().End.Y + Headers()[2].GetGlobalRect().Size.Y;
        Input.ParseInputEvent(new InputEventMouseMotion { Position = first, GlobalPosition = first });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = first, GlobalPosition = first, ButtonMask = MouseButtonMask.Left });
        await r.Frame();
        for (int i = 1; i <= 8; i++)
        {
            var at = first with { Y = first.Y + (below - first.Y) * i / 8 };
            await Move(r, at, MouseButtonMask.Left);
        }
        await r.Seconds(0.5);
        await Move(r, first with { Y = below + 1 }, MouseButtonMask.Left);
        var end = first with { Y = below + 1 };
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = end, GlobalPosition = end });
        await r.Seconds(0.5);
        await r.Frames(3);
        var view = PanelFocus.Views().First(v => v.Scene?.ById("h1") != null);
        float Top(string id) => view.Nodes[view.Scene!.ById(id)!].GetGlobalRect().Position.Y;
        Check.That(Top("h2") < Top("h1") && Top("h1") < Top("h3"), $"dropped past the last, the first lands one place short as the game counts it ({Top("h1")}, {Top("h2")}, {Top("h3")})");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
        ui.QueueFree();
    }
}
