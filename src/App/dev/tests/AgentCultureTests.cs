using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KTable.Client.Agent;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class AgentCultureTests
{
    static Task<(bool error, string text)> Ask(AgentEndpoint agent, string tool, string args) => AgentRecordTests.Ask(agent, tool, args);

    static BuiltThing Only(HudTests.Rig h, string kind) => h.S.View.Built.Single(b => b.Kind == kind);

    [NodeTest]
    static async Task The_agent_keeps_a_mix_of_cultures_lays_a_place_in_it_and_reads_it_back_by_name(TestRunner r)
    {
        var (h, agent) = await AgentGroundTests.Ground(r);
        var (error, kept) = await Ask(agent, "culture_mix", """{"name":"Border town","lays":"continental","builders":"northern","quarter":"ottoman","quarter_share":0.3}""");
        var mix = Manners.Parse(h.S.View.Cultures?.SingleOrDefault()?.Code);
        Check.That(!error && h.S.View.Cultures?.Single().Name == "Border town", "a mix is kept in the scenario under its name: " + kept);
        Check.That(mix is { Lays: Manners.Continental, Builds: Manners.Northern, Ward: Manners.Ottoman } && Math.Abs(mix.Share - 0.3) < 1e-6, "with its builders and its quarter");
        var (_, scenario) = await Ask(agent, "read_scenario", "{}");
        var read = JsonNode.Parse(scenario)!;
        Check.That((string?)read["age"]!["name"] == "later" && read["age"]!["cultures"]!.AsArray().Any(c => (string?)c == "ottoman"), "read_scenario names the map's age and its cultures");
        Check.That(read["age"]!["lines"]!.AsArray().Any(l => (string?)l == "chaussee") && (string?)read["culture_mixes"]![0]!["name"] == "Border town", "its lines and the mixes kept");

        var (laidError, laid) = await Ask(agent, "build_place", """{"kind":"settlement","x":0,"z":0,"radius":8,"seed":7,"culture":"border town"}""");
        var town = Only(h, BuiltKinds.Settlement);
        Check.That(!laidError && Manners.MixOf(town) is { Builds: Manners.Northern, Ward: Manners.Ottoman }, "a place is laid in the mix by its name: " + laid);
        var row = JsonNode.Parse(laid)!;
        Check.That((string?)row["culture"] == "continental" && (string?)row["builders"] == "northern" && (string?)row["quarter"] == "ottoman", "and its row names the cultures");
        Check.That((string?)row["plan"] == Settlements.Form(town) && row["manner"] == null, "and the plan, where numbers were");

        await Ask(agent, "culture_mix", """{"name":"border town","new_name":"March"}""");
        Check.That(h.S.View.Cultures?.Single().Name == "March", "the mix takes another name");
        await Ask(agent, "shape_built", $$"""{"id":"{{town.Id}}","adorners":"american","quarter":"none"}""");
        Check.That(Manners.MixOf(Only(h, BuiltKinds.Settlement)) is { Adorns: Manners.American, Ward: < 0 }, "the place takes other adorners and gives up its quarter");
        var (fromError, from) = await Ask(agent, "culture_mix", $$"""{"name":"Second","from":"{{town.Id}}"}""");
        Check.That(!fromError && h.S.View.Cultures?.Count == 2, "a built place's mix is kept under a name: " + from);
        await Ask(agent, "culture_mix", """{"name":"March","remove":true}""");
        Check.That(h.S.View.Cultures?.Single().Name == "Second", "and a mix is taken off the list");

        Check.That((await Ask(agent, "culture_mix", """{"name":"Plain","lays":"continental"}""")).error, "a mix of one culture is refused");
        Check.That((await Ask(agent, "culture_mix", """{"name":"Odd","builders":"martian"}""")).error, "a culture there is not is refused");
        Check.That((await Ask(agent, "culture_mix", """{"name":"Nobody","remove":true}""")).error, "and so is taking off a mix that is not kept");
        Check.That((await Ask(agent, "build_place", """{"kind":"settlement","x":0,"z":0,"radius":8,"culture":"martian"}""")).error, "a place is not laid in a culture there is not");
    }

    [NodeTest]
    static async Task The_agent_shapes_a_fort_a_building_and_the_country_after_they_are_laid(TestRunner r)
    {
        var (h, agent) = await AgentGroundTests.Ground(r);
        var (fortError, fortSaid) = await Ask(agent, "build_place", """{"kind":"fort","x":-8,"z":-8,"radius":6,"seed":3,"bastions":5}""");
        Check.That(!fortError && (string?)JsonNode.Parse(fortSaid)!["fort"] == Defences.Star, "a star fort is laid and named as one: " + fortSaid);
        var fort = Only(h, BuiltKinds.Fort).Id;
        var (shapedError, shaped) = await Ask(agent, "shape_built", $$"""{"id":"{{fort}}","bastions":7,"outworks":1,"builders":"ottoman"}""");
        var now = Only(h, BuiltKinds.Fort);
        Check.That(!shapedError && (int)now.Setting(Forts.Bastions, 0) == 7 && now.Setting(Forts.Outworks, 0) >= 1, "the fort takes other bastions and outworks: " + shaped);
        Check.That(Manners.SidesOf(now).Builds == Manners.Ottoman, "and other builders");
        Check.That((await Ask(agent, "shape_built", $$"""{"id":"{{fort}}","houses":0.5}""")).error, "a fort has no houses to set");
        Check.That((await Ask(agent, "shape_built", $$"""{"id":"{{fort}}","bastions":12}""")).error, "nor twelve bastions");
        await Ask(agent, "edit_built", $$"""{"id":"{{fort}}","action":"keep"}""");
        Check.That((await Ask(agent, "shape_built", $$"""{"id":"{{fort}}","bastions":6}""")).error, "a fort kept as it is stays");

        var (houseError, house) = await Ask(agent, "build_place", """{"kind":"building","x":12,"z":12,"class":"church","adorners":"northern"}""");
        Check.That(!houseError && Manners.SidesOf(Only(h, BuiltKinds.Building)).Adorns == Manners.Northern, "a building is laid with its adorners: " + house);
        await Ask(agent, "shape_built", $$"""{"id":"{{Only(h, BuiltKinds.Building).Id}}","culture":"ottoman"}""");
        Check.That(Manners.Of(Only(h, BuiltKinds.Building)) == Manners.Ottoman, "and passes to another culture");

        var (countryError, country) = await Ask(agent, "build_details", """{"points":[[-15,12],[0,12]],"radius":4,"buildings":0.6,"seed":5,"culture":"northern"}""");
        Check.That(!countryError && Manners.Of(Only(h, BuiltKinds.Details)) == Manners.Northern, "the country is settled in a culture: " + country);
        await Ask(agent, "shape_built", $$"""{"id":"{{Only(h, BuiltKinds.Details).Id}}","builders":"american"}""");
        Check.That(Manners.SidesOf(Only(h, BuiltKinds.Details)).Builds == Manners.American, "and takes other builders");
    }

    [NodeTest]
    static async Task The_agent_draws_walls_rings_a_town_with_one_and_gives_a_wall_a_corner_a_gate_and_a_kind(TestRunner r)
    {
        var (h, agent) = await AgentGroundTests.Ground(r);
        var (error, said) = await Ask(agent, "build_wall", """{"points":[[-12,-12],[-4,-12],[-4,-6]],"towers":"few","ditch":false}""");
        var wall = Only(h, BuiltKinds.Works);
        Check.That(!error && Works.Corners(wall).Count == 3 && wall.Parts.Count(p => p.Class == "tower") == 3, "the agent draws a wall with a tower at each corner and end: " + said);
        Check.That((await Ask(agent, "build_wall", """{"points":[[0,0],[2,0]],"class":"trench"}""")).error, "and no trench on an 18th-century map");
        var (_, added) = await Ask(agent, "edit_built", $$"""{"id":"{{wall.Id}}","action":"corner_add","corner":0,"x":-8,"z":-14}""");
        Check.Equal(4, Works.Corners(Only(h, BuiltKinds.Works)).Count, "corner_add puts a corner in: " + added);
        await Ask(agent, "edit_built", $$"""{"id":"{{wall.Id}}","action":"gate","x":-4,"z":-9}""");
        Check.That(Only(h, BuiltKinds.Works).Parts.Any(p => p.Class == "gatehouse"), "gate opens a gate in it");
        await Ask(agent, "shape_built", $$"""{"id":"{{wall.Id}}","kind":"palisade"}""");
        Check.Equal(Works.Palisade, Works.ClassOf(Only(h, BuiltKinds.Works)), "shape_built makes it a palisade");
        await Ask(agent, "build_place", """{"kind":"settlement","x":6,"z":6,"radius":4,"seed":4}""");
        var (ringError, ringed) = await Ask(agent, "build_wall", """{"points":[[1,1],[11,1],[11,11],[1,11]],"closed":true}""");
        Check.That(!ringError && Walls.Of(Only(h, BuiltKinds.Settlement)) is { Corners.Count: 4 }, "a closed wall round a town becomes its wall: " + ringed);
    }

    [NodeTest]
    static async Task The_agent_draws_a_block_and_stands_a_building_in_a_town_and_stretches_it(TestRunner r)
    {
        var (h, agent) = await AgentGroundTests.Ground(r);
        Check.That((await Ask(agent, "build_block", """{"points":[[-2,-2],[2,-2],[2,2]]}""")).error, "a block outside any settlement is refused");
        await Ask(agent, "build_road", """{"points":[[-7,0],[7,0]],"class":"street","finds_way":false}""");
        await Ask(agent, "build_place", """{"kind":"settlement","x":0,"z":0,"radius":8,"seed":4}""");
        var (error, said) = await Ask(agent, "build_block", """{"points":[[-4,0.5],[0,0.5],[0,3.5],[-4,3.5]]}""");
        var town = Only(h, BuiltKinds.Settlement);
        Check.That(!error && Traces.Blocks(town).Count == 1, "a block is drawn into the town: " + said);
        var ring = Traces.Blocks(town)[0];
        var inside = town.Parts.Where(p => p.Kind == PartKinds.Block && p.Class is "house" or "shop" or "townhouse" or "cottage").Count(p => Enclosures.Inside(ring, BuiltSite.Round(p).At));
        Check.That(inside > 0, $"and houses line it: {inside}");
        var (built, stood) = await Ask(agent, "build_place", """{"kind":"building","class":"church","x":2.5,"z":-2,"length":3}""");
        town = Only(h, BuiltKinds.Settlement);
        int church = town.Parts.FindIndex(p => p.Kept && p.Class == "church");
        Check.That(!built && church >= 0, "a church is stood in the town: " + stood);
        await Ask(agent, "edit_built", $$"""{"id":"{{town.Id}}","action":"stretch","part":{{church}},"length":4,"width":2}""");
        var sides = BuiltThings.Sides(Only(h, BuiltKinds.Settlement).Parts[church]);
        Check.That(System.Math.Abs(sides.Long - 4) < 0.01 && System.Math.Abs(sides.Deep - 2) < 0.01, $"stretch gives it its length and width: {sides.Long:0.##} by {sides.Deep:0.##}");
        int block = Only(h, BuiltKinds.Settlement).Parts.FindIndex(Traces.IsBlock);
        await Ask(agent, "edit_built", $$"""{"id":"{{town.Id}}","action":"remove_part","part":{{block}}}""");
        Check.That(Traces.Blocks(Only(h, BuiltKinds.Settlement)).Count == 0, "and a block is taken out again");
    }

    [NodeTest]
    static async Task The_agent_builds_any_age_s_buildings_and_lines_but_only_the_plans_the_culture_offers(TestRunner r)
    {
        var (h, agent) = await AgentGroundTests.Ground(r);
        var (railError, rail) = await Ask(agent, "build_road", """{"points":[[-10,-14],[10,-14]],"class":"railway"}""");
        Check.That(!railError, "a railway is laid on an 18th-century map: " + rail);
        var (oddError, odd) = await Ask(agent, "build_road", """{"points":[[-10,14],[10,14]],"class":"canal"}""");
        Check.That(oddError && odd.Contains("chaussee") && odd.Contains("highway"), "a line of no age is refused and every line is named: " + odd);
        var (hangarError, hangar) = await Ask(agent, "build_place", """{"kind":"building","x":0,"z":0,"class":"hangar"}""");
        Check.That(!hangarError, "a hangar of the modern age stands on it all the same: " + hangar);
        await Ask(agent, "remove_built", $$"""{"id":"{{Only(h, BuiltKinds.Building).Id}}"}""");
        var (planError, plan) = await Ask(agent, "build_place", """{"kind":"settlement","x":0,"z":0,"radius":8,"plan":"wards"}""");
        Check.That(planError && plan.Contains(Plans.Offered(Manners.Continental)[0]), "a plan the culture has not is refused, and its own are named: " + plan);
        await Ask(agent, "build_place", """{"kind":"settlement","x":0,"z":0,"radius":8,"seed":7}""");
        var town = Only(h, BuiltKinds.Settlement).Id;
        Check.That((await Ask(agent, "shape_built", $$"""{"id":"{{town}}","plan":"wards"}""")).error, "nor is the town laid afresh in a plan its culture has not");
        await Ask(agent, "remove_built", $$"""{"id":"{{town}}"}""");

        await Ask(agent, "set_light", """{"map_style":"modern"}""");
        await r.Frames(2);
        var (_, scenario) = await Ask(agent, "read_scenario", "{}");
        Check.That((string?)JsonNode.Parse(scenario)!["age"]!["name"] == "modern", "the map passes to the modern age");
        var (laidError, laid) = await Ask(agent, "build_road", """{"points":[[-10,14],[10,14]],"class":"chaussee"}""");
        Check.That(!laidError, "and a chaussée is laid on it all the same: " + laid);
        var (wallError, walled) = await Ask(agent, "build_place", """{"kind":"settlement","x":0,"z":0,"radius":8,"seed":7,"walls":1}""");
        Check.That(!wallError && Only(h, BuiltKinds.Settlement).Setting(Settlements.Walls, 0) < Settlements.BastionedFrom, "a modern town asked for bastions is defended, not bastioned: " + walled);
        Check.That((string?)JsonNode.Parse(walled)!["culture"] == Manners.Id(Manners.Western), "and is built in the age's own western culture");
    }

    [NodeTest]
    static async Task The_agent_restacks_map_sheets_from_the_top_down(TestRunner r)
    {
        var (h, agent) = await AgentGroundTests.Ground(r);
        var under = h.S.View.Sheets.Single();
        var over = new SheetDef { Id = "over", Name = "Over", Image = "", Package = under.Package, Size = new Size2(10, 10), Relief = under.Relief, Height = 1, Z = SheetStack.Above(h.S.View.Sheets) };
        h.S.Do(new AddSheet { Sheet = over });
        await r.Frames(4);
        var (_, listed) = await Ask(agent, "list_map_sheets", "{}");
        var rows = JsonNode.Parse(listed)!.AsArray();
        Check.That((string?)rows[0]!["id"] == "over" && (int)rows[0]!["place"]! == 0 && (int)rows[1]!["place"]! == 1, "the sheets are listed from the top down with their places: " + listed);
        var (error, restacked) = await Ask(agent, "set_map_sheet", """{"id":"g","place":0}""");
        Check.That(!error && (string?)JsonNode.Parse(restacked)![0]!["id"] == "g", "a sheet is brought to the front: " + restacked);
        Check.That(SheetStack.TopDown(h.S.View.Sheets)[0].Id == "g", "and lies on top on the table");
        await Ask(agent, "set_map_sheet", """{"id":"g","place":99,"x":2}""");
        var ground = h.S.View.Sheets.Single(x => x.Id == "g");
        Check.That(SheetStack.TopDown(h.S.View.Sheets)[^1].Id == "g" && Math.Abs(ground.Pos.X - 2) < 1e-6, "sent to the back and moved in one call, it keeps both");
    }

    [NodeTest]
    static async Task The_agent_shows_the_zones_and_heights_pins_a_measure_and_draws_a_wide_line(TestRunner r)
    {
        var (h, agent) = await AgentGroundTests.Ground(r);
        var (zoneError, zones) = await Ask(agent, "set_view", """{"zones":false}""");
        Check.That(!zoneError && !h.Table.ShowZones && zones.Contains("\"zones\":false"), "the zones are hidden on this screen: " + zones);
        await Ask(agent, "set_view", """{"zones":true}""");
        Check.That(h.Table.ShowZones, "and shown again");
        var (heightError, heights) = await Ask(agent, "set_view", """{"heights":true}""");
        Check.That(!heightError && h.Table.Tool == Tool.Relief && h.Table.Relief.Heights && heights.Contains("\"heights\":true"), "the Heights tint is shown with the Ground tool: " + heights);
        await Ask(agent, "set_view", """{"heights":false}""");
        Check.That(!h.Table.Relief.Heights, "and taken off");

        var (pinError, pinned) = await Ask(agent, "measure", """{"stops":[[0,0],[5,0],[5,5]],"pin":true}""");
        Check.That(!pinError && h.Table.Ruler.Pinned.Count == 2 && (int)JsonNode.Parse(pinned)!["pinned_lines"]! == 2, "each leg of a measure is pinned to the table: " + pinned);
        var (unpinError, unpinned) = await Ask(agent, "measure", """{"unpin":true}""");
        Check.That(!unpinError && h.Table.Ruler.Pinned.Count == 0, "and the pins are taken off: " + unpinned);
        Check.That((await Ask(agent, "measure", "{}")).error, "a measure still needs its stops");

        await Ask(agent, "draw", """{"points":[[0,0],[5,0]],"width":60}""");
        Check.That(h.S.View.DrawLayer(Layers.Umpire).Any(stroke => Math.Abs(stroke.Width - 60) < 1e-6), "a line is drawn as wide as the pen goes");
    }

    [NodeTest]
    static async Task The_agent_sets_a_pov_capture_up_from_a_piece_and_is_told_what_is_wrong_with_one(TestRunner r)
    {
        var (h, agent) = await AgentRecordTests.Open(r);
        Check.That((await Ask(agent, "pov_capture", """{"piece":"nobody"}""")).error, "a piece that is not there is refused");
        Check.That((await Ask(agent, "pov_capture", """{"piece":"p1","view":"sideways"}""")).error, "and a view the tool has not");
        Check.That((await Ask(agent, "pov_capture", """{"piece":"p1","format":"oval"}""")).error, "and a format it has not");
        Check.That((await Ask(agent, "pov_capture", """{"piece":"p1","format":"viewshed"}""")).error, "a viewshed is no format of its own");
        Check.That((await Ask(agent, "pov_capture", """{"piece":"p1","view":"first_person","viewshed":true}""")).error, "and goes with the pictures from above");
        Check.That((await Ask(agent, "pov_capture", """{"piece":"p1","gif_seconds":600}""")).error, "a GIF is no longer than the tool records");
        Check.That(h.Table.Draft == null, "nothing was begun on the person's screen by a call refused");
        var (error, said) = await Ask(agent, "pov_capture", """{"piece":"p1","format":"square","radius":12,"deliver":false,"traced":[{"points":[[-3,2],[3,2]]}]}""");
        Check.That(!error && said.Contains("POV capture from") && said.Contains("Nothing was handed over"), "a picture is set up and only looked at: " + said);
        Check.That(h.Table.Draft == null && h.Table.Tool == Tool.Select, "and the tool is put away after it");
        await r.Frames(4);
        Check.That(Math.Abs(h.S.View.VisionRadius - 12) < 1e-6, "the scenario keeps the reach, as it does for the person");
        var (_, controls) = await Ask(agent, "ui_read", """{"find":"POV capture"}""");
        Check.That(controls.Contains("POV capture"), "the POV capture button is within the agent's reach: " + controls);
        Check.That((await Ask(agent, "capture", """{"region":[0.5,0.5,0.4,0.9]}""")).error, "a capture's region runs left to right");
        Check.That((await Ask(agent, "capture", """{"gif_seconds":0}""")).error, "and its GIF lasts some time");
    }

    [NodeTest]
    static async Task The_agent_takes_a_pov_capture_in_whole_grid_cells_only_on_a_gridded_table(TestRunner r)
    {
        var (h, agent) = await AgentRecordTests.Open(r);
        Check.That((await Ask(agent, "pov_capture", """{"piece":"p1","format":"grid"}""")).error, "a grid picture needs the scenario's grid");
        var (laid, flat) = await Ask(agent, "set_grid", """{"kind":"hex","cell":4,"flat":true}""");
        Check.That(!laid && h.S.View.Grid is { FlatTop: true, Pitch: 4 } && flat.Contains("flat tops"), "the agent lays flat-topped hexes: " + flat);
        Check.That((await Ask(agent, "pov_capture", """{"piece":"p1","format":"grid","rings":1.5}""")).error, "rings are whole");
        Check.That((await Ask(agent, "pov_capture", """{"piece":"p1","format":"grid","traced":[{"points":[[-3,2],[3,2]]}]}""")).error, "and a grid picture traces no lines");
        var (error, said) = await Ask(agent, "pov_capture", """{"piece":"p1","format":"grid","rings":2,"deliver":false}""");
        Check.That(!error && said.Contains("POV capture from"), "a grid picture is set up and looked at: " + said);
        await r.Frames(4);
        Check.That(Math.Abs(h.S.View.VisionRadius - 2.5 * 4) < 1e-6, "the scenario keeps two hexes out as the reach: " + h.S.View.VisionRadius);
        await Ask(agent, "set_grid", """{"kind":"none"}""");
    }

    [ScreenTest]
    static async Task The_agent_takes_a_pov_capture_and_a_capture_of_the_view_into_the_capture_folder(TestRunner r)
    {
        var (h, agent) = await AgentRecordTests.Open(r);
        var folder = Path.Combine(Path.GetTempPath(), "ktable-agentcap-" + Guid.NewGuid().ToString("N"));
        h.Hud.Cfg.CaptureFolder = folder;
        h.Hud.Cfg.CaptureToFolder = true;
        h.Hud.Cfg.CaptureToClipboard = false;
        try
        {
            var (error, text, pictures) = await Shown(agent, "pov_capture", """{"piece":"p1","format":"square","radius":10,"hide_text":true}""");
            Check.That(!error && pictures == 1 && text.Contains("saved"), "a POV capture is taken, saved and shown to the agent: " + text);
            Check.That(Directory.GetFiles(folder, "*.png").Length == 1 && h.Table.Draft == null, "one picture is in the capture folder and the tool is put away");
            var eye = await Shown(agent, "pov_capture", """{"piece":"p1","view":"first_person","heading":90,"lens":80,"deliver":false}""");
            Check.That(!eye.error && eye.pictures == 1 && Directory.GetFiles(folder, "*.png").Length == 1, "a first-person view is only looked at: " + eye.text);
            var view = await Shown(agent, "capture", """{"region":[0.25,0.25,0.75,0.75]}""");
            Check.That(!view.error && view.text.StartsWith("Picture") && Directory.GetFiles(folder, "*.png").Length == 2, "a part of the view is captured: " + view.text);
            var gif = await Shown(agent, "pov_capture", """{"piece":"p1","format":"round","radius":10,"gif_seconds":0.5}""");
            Check.That(!gif.error && gif.text.Contains("GIF") && Directory.GetFiles(folder, "*.gif").Length == 1, "a short POV GIF is recorded and stopped: " + gif.text);
        }
        finally
        {
            if (h.Table.Draft != null) h.Table.CancelSighting();
            for (int i = 0; i < 600 && h.Hud.Captures.Recording != null; i++) await r.Frame();
            KTable.Core.BestEffort.DeleteFolder(folder);
        }
    }

    [ScreenTest]
    static async Task A_map_picture_taken_at_once_shows_what_was_just_painted_where_the_person_is_not_looking(TestRunner r)
    {
        var (h, agent) = await AgentGroundTests.Ground(r);
        await Ask(agent, "show_view", """{"x":-15,"z":15,"distance":4,"angle":90}""");
        await r.Frames(30);
        var (error, said) = await Ask(agent, "shape_ground", """{"mode":"paint","kind":"wood","shape":"rect","points":[[8,-18],[18,-8]]}""");
        Check.That(!error, "a wood is painted far from the view: " + said);
        var call = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"view_map","arguments":{"x":13,"z":-13,"size":6,"pixels":256}}}""";
        var content = JsonNode.Parse((await agent.Handle(call))!)!["result"]!["content"]!.AsArray();
        var picture = new Godot.Image();
        picture.LoadPngFromBuffer(Convert.FromBase64String((string)content.First(c => (string?)c!["type"] == "image")!["data"]!));
        var middle = picture.GetPixel(picture.GetWidth() / 2 + 9, picture.GetHeight() / 2 + 9);
        Check.That(middle.G > middle.R && middle.G > middle.B && middle.V < 0.5f, $"the picture's middle is the green of the wood, not bare paper: {middle}");
    }

    static async Task<(bool error, string text, int pictures)> Shown(AgentEndpoint agent, string tool, string args)
    {
        var call = $$$"""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"{{{tool}}}","arguments":{{{args}}}}}""";
        var result = JsonNode.Parse((await agent.Handle(call))!)!["result"]!;
        var content = result["content"]!.AsArray();
        return ((bool)result["isError"]!, (string?)content.FirstOrDefault(c => (string?)c!["type"] == "text")?["text"] ?? "", content.Count(c => (string?)c!["type"] == "image"));
    }

    [NodeTest]
    static async Task The_agent_sets_what_a_place_is_and_reads_it_at_a_spot(TestRunner r)
    {
        var (h, agent) = await AgentGroundTests.Ground(r);
        await Ask(agent, "build_place", """{"kind":"settlement","x":0,"z":0,"radius":8,"seed":7,"houses":1}""");
        var town = Only(h, BuiltKinds.Settlement);
        var sheet = h.S.View.Sheets.First(s => s.Id == town.Sheet);
        int house = town.Parts.FindIndex(p => p.Class == "house");
        var at = BuiltGeometry.ToWorld(sheet, BuiltSite.Round(town.Parts[house]).At.Stored);
        var (_, seen) = await Ask(agent, "what_is_at", FormattableString.Invariant($$"""{"x":{{at.X}},"z":{{at.Z}}}"""));
        var place = JsonNode.Parse(seen)!["place"]!;
        Check.That((string?)place["built"] == town.Id && (int?)place["part"] == house && (string?)place["culture"] == "continental" && (string?)place["state"] == "peace", "what_is_at reads the place: " + seen);
        var (error, said) = await Ask(agent, "edit_built", $$"""{"id":"{{town.Id}}","action":"place","state":"war","held_by":"northern","coat":"Blue"}""");
        town = Only(h, BuiltKinds.Settlement);
        Check.That(!error && town.Setting(PlaceZones.War, 0) >= 1 && town.Setting(PlaceZones.HeldBy, -1) == Manners.Northern && (string?)JsonNode.Parse(said)!["coat"] == "#2a6ebb",
            "a settlement goes to war, held by another culture in its coat: " + said);
        await Ask(agent, "edit_built", $$"""{"id":"{{town.Id}}","action":"place","part":{{house}},"culture":"ottoman","wealth":"rich"}""");
        Check.That(Only(h, BuiltKinds.Settlement).Parts[house] is { Culture: "ottoman", Wealth: "rich" }, "a house takes its own culture and wealth");
        (_, seen) = await Ask(agent, "what_is_at", FormattableString.Invariant($$"""{"x":{{at.X}},"z":{{at.Z}}}"""));
        place = JsonNode.Parse(seen)!["place"]!;
        Check.That((string?)place["culture"] == "ottoman" && (string?)place["wealth"] == "rich" && (string?)place["held_by"] == "northern", "and reads so: " + seen);
        Check.That((await Ask(agent, "edit_built", $$"""{"id":"{{town.Id}}","action":"place","wealth":"rich"}""")).error, "wealth takes a building part");
        Check.That((await Ask(agent, "edit_built", $$"""{"id":"{{town.Id}}","action":"place","state":"siege"}""")).error, "state is peace or war");
        h.S.Me.Role = Role.Umpire;
        Check.That((await Ask(agent, "edit_built", $$"""{"id":"{{town.Id}}","action":"place","state":"peace"}""")).error, "a plain umpire's agent cannot set it");
    }
}
