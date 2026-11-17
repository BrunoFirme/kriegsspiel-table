using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Prefs;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class MapBoxTests
{
    const string Scenario = "feud";
    const double Close = 0.02;
    const int MostFrames = 900;

    static async Task<HudTests.Rig> Boxed(TestRunner r)
    {
        var h = await HudTests.Host(r, scenario: Scenario);
        h.Hud.Cfg.CaptureBoxed = true;
        h.Table.Rig.Frame(Coords.ToWorld(new Vec2(0, 0)), 6);
        h.Table.SetTool(Tool.Capture);
        await r.Frames(2);
        return h;
    }

    static List<SpinBox> Fields(HudTests.Rig h) => WindowTests.Descendants(h.Hud.Capture.Panel.Body).OfType<SpinBox>().ToList();

    static List<string> Words(HudTests.Rig h) => WindowTests.Descendants(h.Hud.Capture.Panel.Body).OfType<Label>().Where(l => l.GetParent() is not SpinBox).Select(l => l.Text).ToList();

    static Button? Named(HudTests.Rig h, string text) => WindowTests.Descendants(h.Hud.Capture.Panel.Body).OfType<Button>().FirstOrDefault(b => b.Text == text);

    static Vector2 Screen(HudTests.Rig h, Vec2 p) => h.Table.Rig.Cam.UnprojectPosition(Coords.ToWorld(p));

    static InputEventMouseButton Left(Vector2 at, bool pressed) =>
        new() { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = at, GlobalPosition = at, ButtonMask = pressed ? MouseButtonMask.Left : 0 };

    static async Task Drag(TestRunner r, HudTests.Rig h, Vec2 from, Vec2 to)
    {
        const int Steps = 4;
        h.Table.Router.OnMouseButton(Left(Screen(h, from), true));
        for (int i = 1; i <= Steps; i++) h.Table.Box.Drag(new Vec2(from.X + (to.X - from.X) * i / Steps, from.Z + (to.Z - from.Z) * i / Steps));
        h.Table.Router.OnMouseButton(Left(Screen(h, to), false));
        await r.Frames(2);
    }

    static bool Is(MapArea? a, double x, double z, double wide, double deep) =>
        a is { } b && Math.Abs(b.Centre.X - x) < Close && Math.Abs(b.Centre.Z - z) < Close && Math.Abs(b.Wide - wide) < Close && Math.Abs(b.Deep - deep) < Close;

    static string Said(MapArea? a) => a is { } b ? FormattableString.Invariant($"{b.Centre.X:0.###}, {b.Centre.Z:0.###}, {b.Wide:0.###} × {b.Deep:0.###}") : "no box";

    [NodeTest]
    static async Task Dragging_on_the_map_lays_a_box_whose_numbers_are_where_it_was_dragged_and_it_stays_for_the_scenario(TestRunner r)
    {
        var h = await Boxed(r);
        Check.That(h.Hud.Capture.Panel.Visible && Fields(h).Count == 0 && Words(h).Contains("No box yet"), "with no box the window has no numbers yet");
        await Drag(r, h, new Vec2(-2, -1), new Vec2(2, 1));
        var box = h.Table.Box;
        Check.That(Is(box.Area, 0, 0, 4, 2), "the box lies where the drag went, in map units: " + Said(box.Area));
        Check.Equal(Tool.Capture, h.Table.Tool, "the tool stays up for the numbers and the knobs");
        var f = Fields(h);
        Check.Equal(5, f.Count, "X, Y, Size X, Size Y and Pixels");
        Check.Equal("Area,X,Y,Size X,Size Y,Pixels", string.Join(",", Words(h)), "the rows are named as the app names them");
        Check.That(Math.Abs(f[0].Value) < Close && Math.Abs(f[1].Value) < Close && Math.Abs(f[2].Value - 4) < Close && Math.Abs(f[3].Value - 2) < Close,
            $"the numbers say the box: {f[0].Value}, {f[1].Value}, {f[2].Value} × {f[3].Value}");
        Check.Equal((double)MapBox.UsualPixels, f[4].Value, "the picture's size starts at the usual one");
        var kept = h.Hud.Cfg.CaptureBoxes[Scenario];
        Check.That(Math.Abs(kept[2] - 4) < Close && Math.Abs(kept[3] - 2) < Close && kept[4] == MapBox.UsualPixels, "the box is kept for the scenario: " + string.Join(" ", kept));
        h.Table.SetTool(Tool.Select);
        await r.Frames(2);
        Check.That(!h.Hud.Capture.Panel.Visible, "Escape's way out closes the window");
        h.Table.SetTool(Tool.Capture);
        await r.Frames(2);
        Check.That(Is(box.Area, 0, 0, 4, 2) && Fields(h).Count == 5, "the box is still there when the tool comes back: " + Said(box.Area));
        var beside = Screen(h, new Vec2(0, 4));
        h.Table.Router.OnMouseButton(Left(beside, true));
        h.Table.Router.OnMouseButton(Left(beside, false));
        await r.Frames(2);
        Check.That(Is(box.Area, 0, 0, 4, 2), "a click beside it leaves it alone: " + Said(box.Area));
        await Drag(r, h, new Vec2(5, 3), new Vec2(8, 5));
        Check.That(Is(box.Area, 6.5, 4, 3, 2), "a drag outside it lays it anew: " + Said(box.Area));
        h.Table.SetTool(Tool.Select);
    }

    [NodeTest]
    static async Task Typed_numbers_move_and_size_the_box_and_clear_takes_it_away(TestRunner r)
    {
        var h = await Boxed(r);
        var box = h.Table.Box;
        box.Set(new MapArea(new Vec2(1, 2), 4, 2));
        await r.Frames(2);
        var f = Fields(h);
        Check.That(f.Count == 5 && Math.Abs(f[0].Value - 1) < Close && Math.Abs(f[1].Value - 2) < Close, "the numbers follow a box set from elsewhere");
        f[0].Value = 5;
        f[1].Value = -3;
        Check.That(Is(box.Area, 5, -3, 4, 2), "typed X and Y move its middle and keep its size: " + Said(box.Area));
        f[2].Value = 10;
        f[3].Value = 6;
        Check.That(Is(box.Area, 5, -3, 10, 6), "typed Size X and Size Y size it about its middle: " + Said(box.Area));
        f[2].Value = 0.1;
        Check.That(Is(box.Area, 5, -3, MapBox.LeastSide, 6), "a side is never under the least: " + Said(box.Area));
        f[4].Value = 1000;
        Check.Equal(1000, box.Pixels, "typed Pixels set the picture's longer side");
        f[4].Value = 99999;
        Check.Equal(MapBox.MostPixels, box.Pixels, "and never past the most");
        Check.Equal("5 -3 0.5 6 4096", string.Join(" ", h.Hud.Cfg.CaptureBoxes[Scenario].Select(MapView.Num)), "what was typed is kept for the scenario");
        Named(h, "Clear")!.EmitSignal(Button.SignalName.Pressed);
        await r.Frames(2);
        Check.That(box.Area == null && !h.Hud.Cfg.CaptureBoxes.ContainsKey(Scenario), "Clear takes the box away and forgets it");
        Check.That(Fields(h).Count == 0 && Named(h, "Capture") == null && Words(h).Contains("No box yet"), "and the window is back to no box");
        Named(h, "Screen")!.EmitSignal(Button.SignalName.Pressed);
        await r.Frames(2);
        Check.That(!h.Hud.Cfg.CaptureBoxed && Named(h, "GIF") != null && !Words(h).Contains("No box yet"), "Screen brings back the picture and GIF choice");
        h.Table.SetTool(Tool.Select);
    }

    static Button? Plus(HudTests.Rig h) => h.Hud.Capture.Panel.FindChildren("*", nameof(Button), true, false).OfType<Button>().SingleOrDefault(b => b.Text == "+" && b.IsVisibleInTree());

    static List<Button> Tabs(HudTests.Rig h) => Plus(h)?.GetParent().GetChildren().OfType<Button>().ToList() ?? [];

    static async Task KeepBox(TestRunner r, HudTests.Rig h)
    {
        var row = Plus(h)!.GetParent();
        Plus(h)!.EmitSignal(Button.SignalName.Pressed);
        await r.Frame();
        var edit = row.GetChildren().OfType<LineEdit>().Single();
        edit.EmitSignal(LineEdit.SignalName.TextSubmitted, edit.Text);
        await r.Frames(2);
    }

    [NodeTest]
    static async Task Map_boxes_are_kept_as_profiles_in_the_Capture_window_s_tabs_and_a_tab_puts_its_box_back(TestRunner r)
    {
        var h = await Boxed(r);
        var box = h.Table.Box;
        box.Set(new MapArea(new Vec2(1, 2), 4, 2));
        box.SetPixels(800);
        h.Hud.Capture.Rebuild();
        await r.Frames(2);
        await KeepBox(r, h);
        box.Set(new MapArea(new Vec2(-6, 3), 10, 5));
        box.SetPixels(2000);
        h.Hud.Capture.Rebuild();
        await r.Frames(2);
        await KeepBox(r, h);
        var kept = CaptureProfiles.Of(h.Hud.Cfg, Scenario, boxes: true);
        Check.Equal("Profile 1,Profile 2", string.Join(",", kept.Select(p => p.Name)), "each box is kept as a profile of the scenario");
        Check.That(CaptureProfiles.Of(h.Hud.Cfg, Scenario).Count == 0, "apart from the POV profiles");
        Check.That(Tabs(h).Single(b => b.Text == "Profile 2").ButtonPressed, "the tab of the box shown is lit");
        Tabs(h).Single(b => b.Text == "Profile 1").EmitSignal(Button.SignalName.Pressed);
        await r.Frames(2);
        Check.That(Is(box.Area, 1, 2, 4, 2) && box.Pixels == 800, "a tab puts its box and its pixels back: " + Said(box.Area));
        Check.That(Fields(h)[0].Value == 1 && Tabs(h).Single(b => b.Text == "Profile 1").ButtonPressed, "the numbers and the lit tab follow");
        Named(h, "Screen")!.EmitSignal(Button.SignalName.Pressed);
        await r.Frames(2);
        Check.That(Plus(h) == null, "the tabs are the map box's alone");
        h.Table.SetTool(Tool.Select);
    }

    [NodeTest]
    static async Task Pulling_a_knob_moves_one_side_only_and_a_drag_inside_moves_the_whole_box(TestRunner r)
    {
        var h = await Boxed(r);
        var box = h.Table.Box;
        h.Table.Rig.Frame(Coords.ToWorld(new Vec2(0, 0)), 10);
        box.Set(new MapArea(new Vec2(0, 0), 8, 4));
        await r.Frames(2);
        await Drag(r, h, new Vec2(4, 0), new Vec2(5, 0.7));
        Check.That(Is(box.Area, 0.5, 0, 9, 4), "the east knob moves the east side and nothing else: " + Said(box.Area));
        await Drag(r, h, new Vec2(0.5, 2), new Vec2(0.2, 3));
        Check.That(Is(box.Area, 0.5, 0.5, 9, 5), "the north knob moves the north side and nothing else: " + Said(box.Area));
        await Drag(r, h, new Vec2(-4, 0.5), new Vec2(20, 0.5));
        Check.That(Is(box.Area, 5 - MapBox.LeastSide / 2, 0.5, MapBox.LeastSide, 5), "a side pulled past the other stops at the least size: " + Said(box.Area));
        box.Set(new MapArea(new Vec2(0, 0), 40, 30));
        await Drag(r, h, new Vec2(-1.5, -0.3), new Vec2(-0.5, 0.2));
        Check.That(Is(box.Area, 1, 0.5, 40, 30), "a drag inside moves the box and keeps its size: " + Said(box.Area));
        var f = Fields(h);
        Check.That(Math.Abs(f[0].Value - 1) < Close && Math.Abs(f[1].Value - 0.5) < Close && Math.Abs(f[2].Value - 40) < Close, "and the numbers follow the knobs");
        Check.That(Math.Abs(h.Hud.Cfg.CaptureBoxes[Scenario][0] - 1) < Close, "where it was left is kept");
        h.Table.SetTool(Tool.Select);
    }

    static string Folder(HudTests.Rig h)
    {
        var folder = Path.Combine(Path.GetTempPath(), "ktable-mapbox-" + Guid.NewGuid().ToString("N"));
        h.Hud.Cfg.CaptureFolder = folder;
        h.Hud.Cfg.CaptureToFolder = true;
        h.Hud.Cfg.CaptureToClipboard = false;
        h.Hud.Cfg.CaptureGif = false;
        return folder;
    }

    static async Task<Image> Taken(TestRunner r, string folder)
    {
        string[] made = Array.Empty<string>();
        for (int i = 0; i < MostFrames && made.Length == 0; i++)
        {
            await r.Frame();
            made = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.png") : made;
        }
        Check.Equal(1, made.Length, "one picture lands in the capture folder");
        await r.Frames(2);
        var img = Image.LoadFromFile(made[0]);
        File.Delete(made[0]);
        return img;
    }

    static bool Differ(Image a, Image b, int x, int y)
    {
        Color p = a.GetPixel(x, y), q = b.GetPixel(x, y);
        return Mathf.Abs(p.R - q.R) + Mathf.Abs(p.G - q.G) + Mathf.Abs(p.B - q.B) > 0.12f;
    }

    static (Vector2 centre, int count) Changed(Image a, Image b, int fromX, int toX)
    {
        var sum = Vector2.Zero;
        int count = 0;
        for (int y = 0; y < a.GetHeight(); y++)
            for (int x = fromX; x < toX; x++)
                if (Differ(a, b, x, y))
                {
                    sum += new Vector2(x + 0.5f, y + 0.5f);
                    count++;
                }
        return (count > 0 ? sum / count : Vector2.Zero, count);
    }

    [ScreenTest]
    static async Task Capture_takes_the_box_from_straight_above_at_the_size_asked_wherever_the_view_is(TestRunner r)
    {
        var h = await Boxed(r);
        var folder = Folder(h);
        try
        {
            var area = new MapArea(new Vec2(0, 0), 20, 10);
            h.Table.Box.Set(area);
            h.Table.Box.SetPixels(512);
            await r.Frames(2);
            h.Table.Keys.OnKey(new InputEventKey { Keycode = Key.Enter, Pressed = true });
            var first = await Taken(r, folder);
            Check.That(first.GetWidth() == 512 && first.GetHeight() == 256, $"the longer side is the pixels asked and the other follows the box: {first.GetWidth()}×{first.GetHeight()}");
            Check.That(h.Table.Tool == Tool.Select && h.Table.Box.Area == area, "the tool is put away and the box stays");
            var to = new Vec2(-6, 3);
            h.S.Do(new MovePiece { Id = "p1", Pos = to });
            await r.Frames(4);
            h.Table.Rig.Frame(Coords.ToWorld(new Vec2(40, -30)), 5);
            await r.Frames(4);
            h.Table.Router.CaptureAgain();
            var second = await Taken(r, folder);
            var picture = new Vector2I(second.GetWidth(), second.GetHeight());
            var want = MapView.Pixel(to, area.Centre, area.Wide, area.Deep, picture);
            var (centre, count) = Changed(first, second, 0, picture.X / 3);
            Check.That(count > 20 && centre.DistanceTo(want) < 6, $"the piece moved to {to.X}, {to.Z} shows at its pixel {want}: {count} pixels changed about {centre}");
            Check.Equal(0, Changed(first, second, picture.X * 3 / 4, picture.X).count, "and the far side of the picture is as it was");
            h.Table.Rig.Frame(Coords.ToWorld(new Vec2(-3, 2)), 30);
            h.Table.Rig.ToggleTopDown();
            await r.Frames(4);
            h.Table.Router.CaptureAgain();
            var third = await Taken(r, folder);
            int moved = Changed(second, third, 0, picture.X).count;
            Check.That(third.GetWidth() == 512 && moved < picture.X * picture.Y / 500, $"from another view the picture is the same: {moved} pixels differ");
        }
        finally { KTable.Core.BestEffort.DeleteFolder(folder); }
    }

    [ScreenTest]
    static async Task A_region_dragged_on_the_screen_is_still_captured_and_capture_again_repeats_whichever_was_last(TestRunner r)
    {
        var h = await HudTests.Host(r, scenario: Scenario);
        var folder = Folder(h);
        try
        {
            h.Table.SetTool(Tool.Capture);
            await r.Frames(2);
            Check.That(Named(h, "Screen") is { ButtonPressed: true } && Named(h, "Picture") != null && Fields(h).Count == 0, "the window opens on the screen's way, as before");
            h.Table.Router.OnMouseButton(Left(new Vector2(300, 200), true));
            h.Table.Router.OnMouseButton(Left(new Vector2(600, 400), false));
            var screen = await Taken(r, folder);
            Check.That(Math.Abs(screen.GetWidth() * 2 - screen.GetHeight() * 3) <= 3 && h.Table.Tool == Tool.Select, $"the dragged region is captured: {screen.GetWidth()}×{screen.GetHeight()}");
            h.Table.Router.CaptureAgain();
            var again = await Taken(r, folder);
            Check.That(again.GetWidth() == screen.GetWidth() && again.GetHeight() == screen.GetHeight(), "capture again repeats the region");
            h.Hud.Cfg.CaptureBoxed = true;
            h.Table.SetTool(Tool.Capture);
            h.Table.Box.Set(new MapArea(new Vec2(0, 0), 6, 12));
            h.Table.Box.SetPixels(300);
            await r.Frames(2);
            Named(h, "Capture")!.EmitSignal(Button.SignalName.Pressed);
            var boxed = await Taken(r, folder);
            Check.That(boxed.GetWidth() == 150 && boxed.GetHeight() == 300, $"the Capture button takes the box, taller than wide: {boxed.GetWidth()}×{boxed.GetHeight()}");
            h.Table.Router.CaptureAgain();
            var boxedAgain = await Taken(r, folder);
            Check.That(boxedAgain.GetWidth() == 150 && boxedAgain.GetHeight() == 300, "capture again now repeats the box");
        }
        finally { KTable.Core.BestEffort.DeleteFolder(folder); }
    }

    static async Task<(string text, bool error)> Ask(AgentEndpoint agent, string args)
    {
        var call = $$$"""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"capture","arguments":{{{args}}}}}""";
        var res = JsonNode.Parse((await agent.Handle(call))!)!["result"]!;
        return ((string?)res["content"]!.AsArray()[0]!["text"] ?? "", (bool)res["isError"]!);
    }

    [ScreenTest]
    static async Task The_agent_captures_a_map_box_profile_by_its_name(TestRunner r)
    {
        var h = await HudTests.Host(r, scenario: Scenario);
        var agent = new AgentEndpoint(new Settings()) { TableSession = () => h.S, TableHud = () => h.Hud };
        r.AddChild(agent);
        var folder = Folder(h);
        try
        {
            var made = CaptureProfiles.Add(h.Hud.Cfg, Scenario, new CaptureProfile { Box = [0, 0, 6, 12, 300] }, boxes: true)!;
            CaptureProfiles.Rename(h.Hud.Cfg, Scenario, 0, "Ford", boxes: true);
            var asked = Ask(agent, """{"profile":"ford"}""");
            var img = await Taken(r, folder);
            var (text, error) = await asked;
            Check.That(!error && img.GetWidth() == 150 && img.GetHeight() == 300, $"the profile's box is taken at its pixels: {img.GetWidth()}×{img.GetHeight()} {text}");
            var (missing, refused) = await Ask(agent, """{"profile":"Bridge"}""");
            Check.That(refused && missing.Contains("Ford"), "an unknown name is refused, naming the profiles there are: " + missing);
            Check.That(made.Name.Length > 0, "kept");
        }
        finally { KTable.Core.BestEffort.DeleteFolder(folder); }
    }

    [ScreenTest]
    static async Task The_agent_captures_a_box_of_the_map_in_map_units_and_leaves_the_person_s_box_alone(TestRunner r)
    {
        var h = await HudTests.Host(r, scenario: Scenario);
        var agent = new AgentEndpoint(new Settings()) { TableSession = () => h.S, TableHud = () => h.Hud };
        r.AddChild(agent);
        var folder = Folder(h);
        try
        {
            var before = h.Table.Rig.Cam.GlobalTransform;
            var asked = Ask(agent, """{"map_box":[0,0,20,10],"pixels":400}""");
            var img = await Taken(r, folder);
            var (text, error) = await asked;
            Check.That(!error && text.StartsWith("Picture 400×200", StringComparison.Ordinal), "the agent is told what was handed over: " + text);
            Check.That(img.GetWidth() == 400 && img.GetHeight() == 200, $"the picture is the box at the pixels asked: {img.GetWidth()}×{img.GetHeight()}");
            Check.That(h.Table.Box.Area == null && h.Table.Tool == Tool.Select && h.Table.Rig.Cam.GlobalTransform.IsEqualApprox(before), "the person's own box, tool and view are untouched");
            Check.That((await Ask(agent, """{"map_box":[0,0,20]}""")).error, "a box is four numbers");
            Check.That((await Ask(agent, """{"map_box":[0,0,20,0]}""")).error, "with a width and a depth");
            Check.That((await Ask(agent, """{"map_box":[0,0,20,10],"pixels":100}""")).error, "pixels stay within the picture's limits");
            Check.That((await Ask(agent, """{"map_box":[0,0,20,10],"gif_seconds":1}""")).error, "and a box is a still picture");
            Check.Equal(0, Directory.GetFiles(folder).Length, "nothing is handed over for a call that is refused");
        }
        finally { KTable.Core.BestEffort.DeleteFolder(folder); }
    }
}
