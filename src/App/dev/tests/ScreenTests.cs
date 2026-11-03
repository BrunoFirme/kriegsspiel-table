using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class ScreenTests
{
    static string Call(string tool, string args) => $$$"""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"{{{tool}}}","arguments":{{{args}}}}}""";

    static async Task<(StudioTests.Rig rig, AgentEndpoint agent)> Open(TestRunner r)
    {
        var h = await StudioTests.Open(r);
        var agent = new AgentEndpoint(new Settings()) { Studio = h.Studio };
        r.AddChild(agent);
        return (h, agent);
    }

    static (string text, bool error, int pictures) Read(string? answer)
    {
        var res = JsonNode.Parse(answer!)!["result"]!;
        var content = res["content"]!.AsArray();
        return ((string?)content[0]!["text"] ?? "", (bool)res["isError"]!, content.Count(c => (string?)c!["type"] == "image"));
    }

    static Color FrontPixel(StudioTests.Rig h, PieceDef d, float fx, float fy)
    {
        var path = h.Store.AssetPath(h.Studio.Pack, PieceFaces.Of(d)[0].Sha)!;
        var img = Image.LoadFromFile(path);
        if (img.IsCompressed()) img.Decompress();
        return img.GetPixel((int)(img.GetWidth() * fx), (int)(img.GetHeight() * fy));
    }

    static bool Near(Color a, Color b) => Mathf.Abs(a.R - b.R) < 0.08f && Mathf.Abs(a.G - b.G) < 0.08f && Mathf.Abs(a.B - b.B) < 0.08f;

    static float Light(Color c) => (c.R + c.G + c.B) / 3;

    [ScreenTest]
    static async Task The_map_seen_from_above_has_its_grid_where_the_coordinates_say_and_the_view_stays(TestRunner r)
    {
        var h = await HudTests.Host(r);
        var agent = new AgentEndpoint(new Settings()) { TableSession = () => h.S, TableHud = () => h.Hud };
        r.AddChild(agent);
        var before = h.Table.Rig.Cam.GlobalTransform;
        var answer = await agent.Handle(Call("view_map", """{"x":0,"z":0,"size":20,"grid":5,"pixels":512}"""));
        var (text, error, pictures) = Read(answer);
        Check.That(!error && pictures == 1, "a picture comes back: " + text);
        Check.That(text.Contains("x -10 to 10") && text.Contains("every 5 map units") && text.Contains("\"p1\""), "it names the area, the grid and the pieces in it: " + text);
        Check.That(h.Table.Rig.Cam.GlobalTransform.IsEqualApprox(before), "the person's own view did not move");
        var data = (string)JsonNode.Parse(answer!)!["result"]!["content"]!.AsArray().First(c => (string?)c!["type"] == "image")!["data"]!;
        var img = new Image();
        img.LoadPngFromBuffer(System.Convert.FromBase64String(data));
        Check.That(img.GetWidth() == 512, "the picture is the size asked for");
        var line = MapView.Pixel(new KTable.Core.Model.Vec2(5, -2.5), new KTable.Core.Model.Vec2(0, 0), 20, 512);
        float onLine = Enumerable.Range(-1, 3).Min(d => Light(img.GetPixel((int)line.X + d, (int)line.Y))), beside = Light(img.GetPixel((int)line.X + 9, (int)line.Y));
        Check.That(onLine < beside - 0.1f, $"the grid line at x 5 is drawn there ({onLine:0.00} against {beside:0.00} beside it)");
        var whole = Read(await agent.Handle(Call("view_map", "{}")));
        Check.That(!whole.error && whole.pictures == 1 && whole.text.Contains("The 2 pieces"), "without an area it frames what is on the table: " + whole.text);
    }

    [ScreenTest]
    static async Task A_snapshot_from_table_height_comes_back_and_the_view_stays(TestRunner r)
    {
        var h = await HudTests.Host(r);
        var agent = new AgentEndpoint(new Settings()) { TableSession = () => h.S, TableHud = () => h.Hud };
        r.AddChild(agent);
        var before = h.Table.Rig.Cam.GlobalTransform;
        var (text, error, pictures) = Read(await agent.Handle(Call("snapshot", """{"x":0,"z":0,"distance":12,"angle":10,"heading":90}""")));
        Check.That(!error && pictures == 1, "a picture comes back: " + text);
        Check.That(h.Table.Rig.Cam.GlobalTransform.IsEqualApprox(before), "the person's own view is back as it was");
    }

    [ScreenTest]
    static async Task A_block_made_by_the_agent_has_its_colour_and_comes_back_with_a_picture(TestRunner r)
    {
        var (h, agent) = await Open(r);
        var (text, error, pictures) = Read(await agent.Handle(Call("studio_make_block", """{"name":"Fusiliers","fill":"#2b4c8c","symbol":"infantry"}""")));
        Check.That(!error && pictures == 1, "the block is made with a picture: " + text);
        var d = h.Studio.Pack.Pieces.First(p => p.Name == "Fusiliers");
        Check.That(Near(FrontPixel(h, d, 0.08f, 0.5f), Color.FromHtml("#2b4c8c")), "the face is the fill colour near its edge");
    }

    [ScreenTest]
    static async Task Units_made_together_keep_their_size_thickness_tint_and_first_look(TestRunner r)
    {
        var (h, agent) = await Open(r);
        var (text, error, _) = Read(await agent.Handle(Call("make_units", """{"units":[{"name":"1st Brigade","width":400,"height":4,"thickness":2,"tint":"#aa2222","border_width":3,"first_look":"Formed","looks":["Square"]}]}""")));
        var d = h.Studio.Pack.Pieces.FirstOrDefault(p => p.Name == "1st Brigade");
        Check.That(!error && d != null, "the unit is made: " + text);
        Check.Equal((400.0, 4.0, 2.0, "#aa2222"), (d!.Size.W, d.Size.H, d.Thickness, d.Tint.ToLowerInvariant()), "its size, thickness and tint are as asked");
        Check.Equal("Formed, Square", string.Join(", ", d.States.Select(s => s.Name)), "its first look has the name asked for");
    }

    [ScreenTest]
    static async Task A_face_written_as_layers_is_drawn_onto_the_kind(TestRunner r)
    {
        var (h, agent) = await Open(r);
        var d = h.Studio.Pack.Pieces[0];
        const string Doc = """{"W":64,"H":64,"Bg":"#ffffffff","Layers":[{"Name":"Paint","Kind":"paint","Ops":[{"K":"shape","P":[0,0,64,0,64,64,0,64],"W":0,"Fc":"#c0392bff","C":"#c0392bff"}]},{"Name":"Label","Kind":"text","X":32,"Y":32,"Text":"7","Size":20,"Color":"#ffffffff"}]}""";
        var (text, error, pictures) = Read(await agent.Handle(Call("studio_write_face", $$"""{"id":"{{d.Id}}","face":"front","doc":{{Doc}}}""")));
        Check.That(!error && pictures == 1, "the face is drawn with a picture: " + text);
        Check.That(Near(FrontPixel(h, d, 0.1f, 0.1f), Color.FromHtml("#c0392b")), "the corner is the shape's red");
        var (read, _, readPictures) = Read(await agent.Handle(Call("studio_read_face", $$"""{"id":"{{d.Id}}"}""")));
        Check.That(read.Contains("\"Text\":\"7\"") && readPictures == 1, "reading it back gives the same layers and a picture");
    }

    [ScreenTest]
    static async Task A_picture_file_becomes_a_kind(TestRunner r)
    {
        var (h, agent) = await Open(r);
        var file = Path.Combine(h.Store.Root, "emblem.png");
        var img = Image.CreateEmpty(40, 40, false, Image.Format.Rgba8);
        img.Fill(new Color(0.1f, 0.6f, 0.2f));
        img.SavePng(file);
        var (text, error, _) = Read(await agent.Handle(Call("studio_pieces_from_pictures", $$"""{"paths":["{{file.Replace("\\", "/")}}"]}""")));
        Check.That(!error && h.Studio.Pack.Pieces.Any(p => p.Name == "Emblem"), "the picture is a new kind: " + text);
    }

    [ScreenTest]
    static async Task Shapes_built_onto_a_block_are_painted_by_region_and_take_a_decal(TestRunner r)
    {
        var (h, agent) = await Open(r);
        var d = h.Studio.Pack.Pieces[0];
        var added = Read(await agent.Handle(Call("studio_model_add_part", $$"""{"id":"{{d.Id}}","shape":"cylinder","name":"Banner pole","tint":"#ffffff"}""")));
        Check.That(!added.error && added.pictures == 1, "a shape is built onto the block, with a picture: " + added.text);
        var view = Read(await agent.Handle(Call("studio_model_view", $$"""{"id":"{{d.Id}}","side":"front"}""")));
        Check.That(view.pictures == 1 && view.text.Contains("Banner pole"), "the 3D view shows it and lists the part: " + view.text);
        var painted = Read(await agent.Handle(Call("studio_model_paint", $$$"""{"id":"{{{d.Id}}}","part":"Banner pole","colour":"#1a3c8c","box":{"min":[0,0.5,0],"max":[1,1,1]}}""")));
        Check.That(!painted.error && painted.text.Contains("texels"), "its top half is painted: " + painted.text);
        var file = Path.Combine(h.Store.Root, "badge.png");
        var img = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
        img.Fill(new Color(1, 0.85f, 0.1f));
        img.SavePng(file);
        var decal = Read(await agent.Handle(Call("studio_model_decal", $$"""{"id":"{{d.Id}}","path":"{{file.Replace("\\", "/")}}","side":"front","size":0.3}""")));
        Check.That(!decal.error && decal.pictures == 1, "a decal is pressed onto it: " + decal.text);
        var undone = Read(await agent.Handle(Call("studio_undo", "{}")));
        Check.That(!undone.error, "the decal undoes: " + undone.text);
    }
}
