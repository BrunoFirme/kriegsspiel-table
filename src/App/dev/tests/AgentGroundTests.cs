using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KTable.Client.Agent;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class AgentGroundTests
{
    const double Side = 40, UnitsPerMetre = 0.02, Within = 0.08;
    const int SettleFrames = 8;

    static Task<(bool error, string text)> Ask(AgentEndpoint agent, string tool, string args) => AgentRecordTests.Ask(agent, tool, args);

    static async Task<(HudTests.Rig h, AgentEndpoint agent)> Ground(TestRunner r)
    {
        var (h, agent) = await AgentRecordTests.Open(r);
        var flat = ReliefGrid.Flat(Side, Side);
        var (sha, _) = ReliefImport.Save(flat, h.S.Store.Root);
        h.S.Relief.Remember(sha, flat);
        h.S.Do(new AddSheet { Sheet = new SheetDef { Id = "g", Name = "Ground", Image = "", Package = UploadAsset.ReliefPackage, Size = new Size2(Side, Side), Relief = sha, Height = 1 } });
        h.S.Do(new SetCalibration { UnitsPerMeter = UnitsPerMetre });
        await r.Frames(SettleFrames);
        return (h, agent);
    }

    static async Task<double> Metres(AgentEndpoint agent, double x, double z)
    {
        var (_, text) = await Ask(agent, "ground_at", FormattableString.Invariant($$"""{"points":[[{{x}},{{z}}]]}"""));
        return (double)JsonNode.Parse(text)![0]!["height"]! / UnitsPerMetre;
    }

    static bool About(double metres, double wanted) => Math.Abs(metres - wanted) <= Math.Max(1, Math.Abs(wanted) * Within);

    [NodeTest]
    static async Task The_agent_levels_the_ground_to_a_height_with_a_hard_square_brush(TestRunner r)
    {
        var (_, agent) = await Ground(r);
        var (error, said) = await Ask(agent, "shape_ground", """{"mode":"level","points":[[0,0]],"radius":4,"height":100,"hardness":0.9,"tip":"square"}""");
        Check.That(!error, "the ground is levelled: " + said);
        Check.That(About(await Metres(agent, 0, 0), 100), "the middle stands at a hundred metres");
        Check.That(About(await Metres(agent, 3, 3), 100), "a square brush reaches its corner, where a round one does not");
        Check.That(About(await Metres(agent, 10, 10), 0), "the ground beyond the brush is as it was");
        Check.That((await Ask(agent, "shape_ground", """{"mode":"level","points":[[0,0]],"radius":4}""")).error, "level without a height is refused");
        Check.That((await Ask(agent, "shape_ground", """{"mode":"level","points":[[0,0]],"radius":4,"height":5,"tip":"star"}""")).error, "a tip the brush has not is refused");
        Check.That((await Ask(agent, "shape_ground", """{"mode":"dig","points":[[0,0]],"radius":4}""")).error, "a mode the tool has not is refused");
    }

    [NodeTest]
    static async Task The_agent_lays_a_hill_a_ridge_a_valley_and_contours_and_finishes_a_whole_sheet(TestRunner r)
    {
        var (_, agent) = await Ground(r);
        var (hillError, hill) = await Ask(agent, "shape_ground", """{"mode":"hill","points":[[-10,-10],[-10,-4]],"height":50}""");
        Check.That(!hillError && About(await Metres(agent, -10, -10), 50), "a hill stands fifty metres at its summit: " + hill);
        Check.That(About(await Metres(agent, -10, -2), 0), "and has fallen to the ground past its foot");
        var (ridgeError, ridge) = await Ask(agent, "shape_ground", """{"mode":"ridge","points":[[5,-15],[15,-15]],"radius":2,"height":30}""");
        Check.That(!ridgeError && About(await Metres(agent, 10, -15), 30), "a ridge runs thirty metres high along its line: " + ridge);
        var (liftError, lift) = await Ask(agent, "shape_ground", """{"mode":"lift","sheet":"g","height":10}""");
        Check.That(!liftError && About(await Metres(agent, 18, 18), 10), "lift raises the whole sheet: " + lift);
        var (valleyError, valley) = await Ask(agent, "shape_ground", """{"mode":"valley","points":[[5,15],[15,15]],"radius":2,"height":6}""");
        Check.That(!valleyError && About(await Metres(agent, 10, 15), 4), "a valley is cut six metres into it: " + valley);
        var (contourError, contours) = await Ask(agent, "shape_ground",
            """{"mode":"contours","lines":[{"points":[[-18,10],[-10,10]],"height":40},{"points":[[-18,16],[-10,16]],"height":80}]}""");
        Check.That(!contourError && About(await Metres(agent, -14, 16), 80) && About(await Metres(agent, -14, 10), 40), "the ground passes through each contour at its height: " + contours);
        double between = await Metres(agent, -14, 13);
        Check.That(between is > 45 and < 75, $"and climbs between them ({between:0} m)");
        Check.That(!(await Ask(agent, "shape_ground", """{"mode":"roughen","points":[[0,0]],"height":3,"grain":100}""")).error, "roughen goes over the sheet under a point");
        Check.That(!(await Ask(agent, "shape_ground", """{"mode":"erode","sheet":"g","height":4}""")).error, "erode goes over a named sheet");
        Check.That((await Ask(agent, "shape_ground", """{"mode":"hill","points":[[0,0]],"height":50}""")).error, "a hill needs its foot");
        Check.That((await Ask(agent, "shape_ground", """{"mode":"ridge","points":[[0,0],[5,0]],"height":50}""")).error, "a ridge needs its width");
        Check.That((await Ask(agent, "shape_ground", """{"mode":"contours","lines":[{"points":[[0,0]],"height":40}]}""")).error, "a contour needs two points");
        Check.That((await Ask(agent, "shape_ground", """{"mode":"lift","sheet":"nowhere","height":10}""")).error, "a sheet that is not there is refused");
    }

    [NodeTest]
    static async Task The_agent_makes_a_sheet_s_ground_finer_and_hides_the_sheet_on_this_screen(TestRunner r)
    {
        var (h, agent) = await Ground(r);
        var before = h.S.Relief.Grid(h.S.View.Sheets.Single())!;
        var (error, said) = await Ask(agent, "set_map_sheet", """{"id":"g","finer":true}""");
        var after = h.S.Relief.Grid(h.S.View.Sheets.Single());
        Check.That(!error && after != null && after.Cols == before.Cols * 2 - 1, "the ground has twice the cells each way: " + said);
        Check.That((int)JsonNode.Parse(said)![0]!["cells"]![0]! == after!.Cols, "and the sheet's row gives its cells");
        await Ask(agent, "set_map_sheet", """{"id":"g","hidden":true}""");
        Check.That(h.Table.HiddenSheets.Contains("g"), "the sheet is hidden on this screen");
        var (_, shown) = await Ask(agent, "set_map_sheet", """{"id":"g","hidden":false}""");
        Check.That(!h.Table.HiddenSheets.Contains("g") && shown.Contains("\"hidden_here\": false"), "and shown again");
    }

    static BuiltThing Town(HudTests.Rig h) => h.S.View.Built.Single(b => b.Kind == BuiltKinds.Settlement);

    [NodeTest]
    static async Task The_agent_sculpts_a_settlement_part_by_part_and_along_its_wall(TestRunner r)
    {
        var (h, agent) = await Ground(r);
        var (laidError, laid) = await Ask(agent, "build_place", """{"kind":"settlement","x":0,"z":0,"radius":8,"houses":0.6,"walls":0.5,"seed":7}""");
        Check.That(!laidError, "a walled settlement is laid: " + laid);
        var id = Town(h).Id;
        var (_, full) = await Ask(agent, "list_built", $$"""{"id":"{{id}}"}""");
        var row = JsonNode.Parse(full[..(full.LastIndexOf('}') + 1)])!;
        var corners = row["wall_corners"]?.AsArray();
        Check.That(row["parts"]!.AsArray().Count > 0 && corners is { Count: >= 3 }, "its parts are numbered and its wall's corners given");

        int house = Town(h).Parts.FindIndex(p => p.Kind == PartKinds.Block && !p.Kept);
        await Ask(agent, "edit_built", $$"""{"id":"{{id}}","action":"keep","part":{{house}}}""");
        Check.That(Town(h).Parts[house].Kept, "a part is kept");
        await Ask(agent, "edit_built", $$"""{"id":"{{id}}","action":"free","part":{{house}}}""");
        Check.That(!Town(h).Parts[house].Kept, "and let go");
        await Ask(agent, "edit_built", $$"""{"id":"{{id}}","action":"keep"}""");
        Check.That(BuiltThings.Fixed(Town(h)), "the whole place is kept");
        await Ask(agent, "edit_built", $$"""{"id":"{{id}}","action":"free"}""");
        Check.That(!BuiltThings.Fixed(Town(h)), "and let go");

        var sheet = h.S.View.Sheets.Single();
        var (movedError, moved) = await Ask(agent, "edit_built", $$"""{"id":"{{id}}","action":"move","part":{{house}},"x":1.5,"z":-2.5}""");
        var stands = BuiltGeometry.ToWorld(sheet, BuiltSite.Round(Town(h).Parts[house]).At.Stored);
        Check.That(!movedError && Math.Abs(stands.X - 1.5) < 0.01 && Math.Abs(stands.Z + 2.5) < 0.01 && Town(h).Parts[house].Kept, "a house is moved to a point and kept there: " + moved);
        var was = Town(h).Parts[house].At.ToList();
        await Ask(agent, "edit_built", $$"""{"id":"{{id}}","action":"turn","part":{{house}},"degrees":90}""");
        Check.That(!Town(h).Parts[house].At.SequenceEqual(was), "it is turned");
        was = Town(h).Parts[house].At.ToList();
        await Ask(agent, "edit_built", $$"""{"id":"{{id}}","action":"size","part":{{house}},"by":2}""");
        Check.That(!Town(h).Parts[house].At.SequenceEqual(was), "and made larger");
        int count = Town(h).Parts.Count;
        await Ask(agent, "edit_built", $$"""{"id":"{{id}}","action":"remove_part","part":{{house}}}""");
        Check.That(Town(h).Parts.Count == count - 1, "a part is taken out");

        int houses = Town(h).Parts.Count(p => p.Kind == PartKinds.Block);
        var (thinError, thinned) = await Ask(agent, "edit_built", $$"""{"id":"{{id}}","action":"thin","points":[[-6,0],[6,0]],"radius":4}""");
        Check.That(!thinError && Town(h).Parts.Count(p => p.Kind == PartKinds.Block) < houses, "houses are thinned along a stroke: " + thinned);
        houses = Town(h).Parts.Count(p => p.Kind == PartKinds.Block);
        var (fillError, filled) = await Ask(agent, "edit_built", $$"""{"id":"{{id}}","action":"fill","points":[[-6,0],[6,0]],"radius":4}""");
        Check.That(!fillError && Town(h).Parts.Count(p => p.Kind == PartKinds.Block) > houses, "and filled in again: " + filled);

        var corner = corners![0]!.AsArray();
        double cx = (double)corner[0]!, cz = (double)corner[1]!;
        var (cornerError, cornered) = await Ask(agent, "edit_built", FormattableString.Invariant($$"""{"id":"{{id}}","action":"wall_corner","corner":0,"x":{{cx * 1.2}},"z":{{cz * 1.2}}}"""));
        var now = Walls.Handles(Town(h))!.Corners.Select(c => BuiltGeometry.ToWorld(sheet, c.Stored)).ToList();
        Check.That(!cornerError && now.Any(c => Math.Abs(c.X - cx * 1.2) < 0.05 && Math.Abs(c.Z - cz * 1.2) < 0.05), "a corner of the wall is moved out: " + cornered);
        int gates = Walls.Of(Town(h))!.Gates.Count;
        var mid = new Vec2((now[0].X + now[1].X) / 2, (now[0].Z + now[1].Z) / 2);
        var (gateError, gated) = await Ask(agent, "edit_built", FormattableString.Invariant($$"""{"id":"{{id}}","action":"gate","x":{{mid.X}},"z":{{mid.Z}}}"""));
        Check.That(!gateError && Walls.Of(Town(h))!.Gates.Count != gates, "a gate is opened or closed in the wall: " + gated);

        await Ask(agent, "edit_built", $$"""{"id":"{{id}}","action":"name","name":"Aubach"}""");
        Check.That(Town(h).Name == "Aubach", "the place is named");
        await Ask(agent, "shape_built", $$"""{"id":"{{id}}","manner":"english"}""");
        Check.That((int)Town(h).Setting(Manners.Setting, -1) == Manners.Of("english"), "and built in another manner");
        var (wholeError, whole) = await Ask(agent, "edit_built", $$"""{"id":"{{id}}","action":"move","x":5,"z":5}""");
        var middle = JsonNode.Parse(whole)!;
        Check.That(!wholeError && Math.Abs((double)middle["x"]! - 5) < 0.05 && Math.Abs((double)middle["z"]! - 5) < 0.05, "the whole place is moved: " + whole);

        Check.That((await Ask(agent, "edit_built", $$"""{"id":"{{id}}","action":"size","by":2}""")).error, "sizing needs a part");
        Check.That((await Ask(agent, "edit_built", $$"""{"id":"{{id}}","action":"move","part":99999,"x":0,"z":0}""")).error, "a part that is not there is refused");
        Check.That((await Ask(agent, "edit_built", $$"""{"id":"{{id}}","action":"raze"}""")).error, "an action it has not is refused");
        Check.That((await Ask(agent, "shape_built", $$"""{"id":"{{id}}","manner":"martian"}""")).error, "a manner it has not is refused");
    }

    [NodeTest]
    static async Task Every_agent_tool_is_marked_reading_or_changing_and_every_list_argument_says_what_it_holds(TestRunner r)
    {
        var (_, agent) = await AgentRecordTests.Open(r);
        var tools = JsonNode.Parse((await agent.Handle("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}"""))!)!["result"]!["tools"]!.AsArray();
        var names = tools.Select(t => (string)t!["name"]!).ToHashSet();
        var strangers = ToolEffects.Reads.Concat(ToolEffects.Destroys).Where(n => !names.Contains(n)).ToList();
        Check.That(strangers.Count == 0, "every marked name is a tool: " + string.Join(", ", strangers));
        var unmarked = names.Where(n => ToolEffects.ReadingStarts.Any(n.StartsWith) && !ToolEffects.Reads.Contains(n)).ToList();
        Check.That(unmarked.Count == 0, "every list_ and read_ tool is marked as reading: " + string.Join(", ", unmarked));
        var bare = tools.SelectMany(t => t!["inputSchema"]!["properties"]!.AsObject().Where(p => (string?)p.Value!["type"] == "array" && p.Value["items"] == null)
            .Select(p => $"{t["name"]}.{p.Key}")).ToList();
        Check.That(bare.Count == 0, "every list argument says what it holds: " + string.Join(", ", bare));
        Check.That(names.Contains("edit_built") && !(bool)tools.First(t => (string?)t!["name"] == "edit_built")!["annotations"]!["readOnlyHint"]!, "edit_built is there and changes things");
    }

    [NodeTest]
    static async Task The_agent_sets_the_settings_the_settings_window_holds_for_the_hand(TestRunner r)
    {
        var (_, agent) = await AgentRecordTests.Open(r);
        var (error, said) = await Ask(agent, "set_settings",
            """{"values":{"lift":4,"wheel_turns_hovered":false,"hover_lights_carrier":false,"system_file_browser":true,"map_picture_memory":"512","hosting_port":7790}}""");
        var cfg = agent.Cfg;
        Check.That(!error, "they are set: " + said);
        Check.That(cfg is { LiftHeight: 4, WheelTurnsHovered: false, HoverLightsCarrier: false, SystemFileDialog: true, MapPictureMemory: 512, Port: 7790 }, "and kept as given");
        Check.That((await Ask(agent, "set_settings", """{"values":{"lift":40}}""")).error, "a lift past its range is refused");
        var (_, read) = await Ask(agent, "read_settings", "{}");
        Check.That(read.Contains("\"lift\"") && read.Contains("\"hosting_port\""), "read_settings names them");
    }
}
