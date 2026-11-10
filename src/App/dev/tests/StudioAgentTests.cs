using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KTable.Client.Prefs;

namespace KTable.App;

static class StudioAgentTests
{
    sealed record Bench(StudioTests.Rig Rig, AgentEndpoint Agent)
    {
        public async Task<string> Call(string tool, string args)
        {
            var reply = await Agent.Handle($$$"""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"{{{tool}}}","arguments":{{{args}}}}}""") ?? "";
            return reply;
        }

        public async Task<string> Ok(string tool, string args)
        {
            var reply = await Call(tool, args);
            Check.That(!Failed(reply), $"{tool} works: {reply}");
            return Text(reply);
        }

        public async Task Refused(string tool, string args, string why)
        {
            var reply = await Call(tool, args);
            Check.That(Failed(reply), $"{tool} is refused {why}: {reply}");
        }

        public string IdOf(string name) => Rig.Studio.Pack.Pieces.First(p => p.Name == name).Id;
    }

    static int Texels(string reply) =>
        System.Text.RegularExpressions.Regex.Match(reply, @"Painted ([\d,]+) texels") is { Success: true } m ? int.Parse(m.Groups[1].Value.Replace(",", "")) : -1;

    static void A_one_pixel_brush_paints_the_texel_under_it_in_full(ModelStudio m)
    {
        Check.Equal(0f, new StudioPrefs().ModelSoft, "the model brush is hard unless softened");
        m.SetTool("paint");
        m.P.Brush = 1;
        m.Ink = new Godot.Color(0, 0, 1);
        var hit = m.Tools.Ray(m.Vp.Size / 2);
        Check.That(hit != null, "the middle of the view is on the model");
        var view = m.Views[hit!.Value.part];
        var d = view.Data;
        var uv = d.UV[hit.Value.tri * 3] * (1 - hit.Value.u - hit.Value.v) + d.UV[hit.Value.tri * 3 + 1] * hit.Value.u + d.UV[hit.Value.tri * 3 + 2] * hit.Value.v;
        int o = (System.Math.Clamp((int)(uv.Y * view.H), 0, view.H - 1) * view.W + System.Math.Clamp((int)(uv.X * view.W), 0, view.W - 1)) * 4;
        m.Tools.Stamp(hit.Value);
        Check.Equal("0,0,255,255", string.Join(",", view.Paint.Skip(o).Take(4)), "the texel under the brush takes the ink, not a blend");
    }

    static bool Failed(string reply) => JsonNode.Parse(reply)?["result"]?["isError"]?.GetValue<bool>() != false;

    static string Text(string reply) => JsonNode.Parse(reply)?["result"]?["content"]?[0]?["text"]?.GetValue<string>() ?? "";

    static async Task<Bench> Open(TestRunner r)
    {
        var rig = await StudioTests.Open(r);
        var agent = new AgentEndpoint(new Settings()) { Studio = rig.Studio };
        r.AddChild(agent);
        return new Bench(rig, agent);
    }

    [ScreenTest]
    static async Task An_agent_makes_a_block_a_token_and_an_effect_and_the_pack_on_disk_holds_all_three(TestRunner r)
    {
        var b = await Open(r);
        await b.Ok("studio_make_block", """{"name":"Grenadiers","fill":"#335577","symbol":"infantry","width":2}""");
        await b.Ok("studio_new_piece", """{"name":"Marker","shape":"round","colour":"#aa3322"}""");
        await b.Ok("studio_new_effect", """{"name":"Smoke","kind":"smoke"}""");
        await b.Refused("studio_new_piece", """{"name":"Odd","shape":"star"}""", "for a shape it does not make");
        Check.That(b.Rig.Studio.FlushPack(), "the pack saves");
        var names = b.Rig.OnDisk().Pieces.Select(p => p.Name).ToList();
        foreach (var n in new[] { "Grenadiers", "Marker", "Smoke" }) Check.That(names.Contains(n), n + " is in the pack on disk: " + string.Join(", ", names));
        Check.That(!names.Contains("Odd"), "a refused kind is not made");
    }

    [ScreenTest]
    static async Task An_agent_resizes_a_face_s_picture_and_lays_the_pieces_out_again(TestRunner r)
    {
        var b = await Open(r);
        await b.Ok("studio_make_block", """{"name":"Dragoons"}""");
        var id = b.IdOf("Dragoons");
        var said = await b.Ok("studio_picture_size", $$"""{"id":"{{id}}","width":640,"height":320}""");
        Check.That(said.Contains("640×320"), "the face is the size asked: " + said);
        var face = await b.Ok("studio_read_face", $$"""{"id":"{{id}}"}""");
        Check.That(face.Contains("\"W\":640") && face.Contains("\"H\":320"), "the face reads back at that size: " + face[..System.Math.Min(200, face.Length)]);
        await b.Refused("studio_picture_size", $$"""{"id":"{{id}}","width":4,"height":320}""", "for a side under the smallest");
        Check.That((await b.Ok("studio_lay_out", "{}")).Contains("Laid out"), "the pieces are laid out again");
    }

    [ScreenTest]
    static async Task An_agent_sizes_tags_letters_and_gives_looks_to_a_kind_and_reads_them_back(TestRunner r)
    {
        var b = await Open(r);
        await b.Ok("studio_make_block", """{"name":"Fusiliers"}""");
        var id = b.IdOf("Fusiliers");
        await b.Ok("studio_set_piece", $$"""{"id":"{{id}}","width":3,"tint":"#112233","tags":["line","french"]}""");
        var holding = await b.Ok("studio_set_piece", $$"""{"id":"{{id}}","holds":true}""");
        Check.That(holding.Contains("\"holds\": true"), "the kind holds pieces: " + holding);
        Check.That(!(await b.Ok("studio_set_piece", $$"""{"id":"{{id}}","holds":false}""")).Contains("\"holds\": true"), "and holds none again");
        await b.Ok("studio_set_text_line", $$"""{"id":"{{id}}","name":"Regiment","default":"1st"}""");
        await b.Ok("studio_add_look", $$"""{"id":"{{id}}","name":"Shattered"}""");
        await b.Ok("studio_rename_look", $$"""{"id":"{{id}}","look":2,"name":"Broken"}""");
        await b.Ok("studio_set_look", $$"""{"id":"{{id}}","look":2,"width":1.5}""");
        await b.Ok("studio_set_markers", $$"""{"id":"{{id}}","markers":[]}""");
        var read = await b.Ok("studio_read_piece", $$"""{"id":"{{id}}"}""");
        foreach (var said in new[] { "Regiment", "Broken", "french" }) Check.That(read.Contains(said), $"the kind reads back with {said}: {read}");
        await b.Ok("studio_remove", $$"""{"id":"{{id}}","text_line":"Regiment"}""");
        Check.That(!(await b.Ok("studio_read_piece", $$"""{"id":"{{id}}"}""")).Contains("Regiment"), "the text line is gone");
        await b.Refused("studio_set_piece", """{"id":"no-such-kind","width":1}""", "for a kind the pack does not have");
    }

    [ScreenTest]
    static async Task An_agent_copies_a_kind_deletes_the_copy_and_takes_a_rename_back_with_undo(TestRunner r)
    {
        var b = await Open(r);
        var hussars = b.IdOf("Hussars");
        await b.Ok("studio_duplicate_piece", $$"""{"id":"{{hussars}}","name":"Hussars copy"}""");
        var copy = b.IdOf("Hussars copy");
        Check.That(copy != hussars, "the copy is a kind of its own");
        await b.Ok("studio_delete_piece", $$"""{"id":"{{copy}}"}""");
        Check.That(b.Rig.Studio.Pack.Pieces.All(p => p.Name != "Hussars copy"), "the copy is deleted");
        await b.Ok("studio_set_piece", $$"""{"id":"{{hussars}}","name":"Dragoons"}""");
        Check.Equal("Dragoons", b.Rig.Studio.Pack.Pieces.First(p => p.Id == hussars).Name, "the kind is renamed");
        await b.Ok("studio_undo", "{}");
        Check.Equal("Hussars", b.Rig.Studio.Pack.Pieces.First(p => p.Id == hussars).Name, "undo takes the rename back");
    }

    [ScreenTest]
    static async Task An_agent_makes_units_with_counters_lines_and_looks_in_one_call_each(TestRunner r)
    {
        var b = await Open(r);
        await b.Ok("make_unit", """
            {"name":"1st Line","fill":"#223344","symbol":"infantry","counters":[{"name":"Strength","min":0,"max":12,"default":10}],
             "text_lines":[{"name":"Colonel","default":"Ney"}],"looks":["Shattered"],"tags":["brigade"]}
            """);
        await b.Ok("make_units", """{"units":[{"name":"2nd Line","symbol":"infantry"},{"name":"Guns","symbol":"artillery"}]}""");
        var read = await b.Ok("studio_read_piece", $$"""{"id":"{{b.IdOf("1st Line")}}"}""");
        foreach (var said in new[] { "Strength", "Colonel", "Shattered" }) Check.That(read.Contains(said), $"the unit has {said}: {read}");
        Check.That(b.Rig.Studio.FlushPack(), "the pack saves");
        var names = b.Rig.OnDisk().Pieces.Select(p => p.Name).ToList();
        Check.That(names.Contains("2nd Line") && names.Contains("Guns"), "every unit of the batch is on disk: " + string.Join(", ", names));
    }

    [ScreenTest]
    static async Task A_face_read_by_an_agent_writes_back_as_it_was(TestRunner r)
    {
        var b = await Open(r);
        await b.Ok("studio_make_block", """{"name":"Cuirassiers","symbol":"cavalry"}""");
        var id = b.IdOf("Cuirassiers");
        var face = await b.Ok("studio_read_face", $$"""{"id":"{{id}}"}""");
        var layers = face[(face.IndexOf('\n') + 1)..];
        Check.That(JsonNode.Parse(layers) is JsonObject, "the face comes back as its layers: " + face);
        await b.Ok("studio_write_face", $$"""{"id":"{{id}}","doc":{{layers}}}""");
        await b.Refused("studio_write_face", $$"""{"id":"{{id}}","doc":"not layers"}""", "for a face that is not layers");
    }

    [ScreenTest]
    static async Task An_agent_builds_paints_renames_and_takes_apart_a_model_on_a_block(TestRunner r)
    {
        var b = await Open(r);
        await b.Ok("studio_make_block", """{"name":"Battery"}""");
        var id = b.IdOf("Battery");
        await b.Ok("studio_model_add_part", $$"""{"id":"{{id}}","shape":"cylinder","name":"Barrel","pos":[0,0.2,0],"size":[0.2,0.2,1]}""");
        await b.Refused("studio_model_add_part", $$"""{"id":"{{id}}","shape":"dodecahedron"}""", "for a shape it does not add");
        var parts = await b.Ok("studio_model_parts", $$"""{"id":"{{id}}"}""");
        Check.That(parts.Contains("Barrel"), "the part is listed: " + parts);
        await b.Ok("studio_model_set_part", $$"""{"id":"{{id}}","part":"Barrel","name":"Gun","tint":"#444444"}""");
        int whole = Texels(await b.Ok("studio_model_paint", $$"""{"id":"{{id}}","colour":"#806040","part":"Gun"}"""));
        int cap = Texels(await b.Ok("studio_model_paint", $$"""{"id":"{{id}}","colour":"#c03020","part":"Gun","from":[0.5,1,0.5],"bend":30}"""));
        Check.That(cap > 0 && cap < whole / 2, $"a fill from the barrel's top stops at its rim: {cap} of {whole} texels");
        Check.That(Texels(await b.Ok("studio_model_paint", $$"""{"id":"{{id}}","colour":"#c03020","part":"Gun","from":[0.5,0.5,0],"tolerance":1}""")) >= whole - cap,
            "a fill that lets every colour in runs over the whole barrel");
        await b.Refused("studio_model_paint", $$$"""{"id":"{{{id}}}","colour":"#c03020","part":"Gun","from":[0.5,0.5,1],"box":{"min":[0,0,0],"max":[1,1,1]}}""", "for a fill given a box as well");
        await b.Ok("studio_model_view", $$"""{"id":"{{id}}","side":"three-quarter"}""");
        A_one_pixel_brush_paints_the_texel_under_it_in_full(b.Rig.Studio.Model!);
        await b.Ok("studio_model_remove_part", $$"""{"id":"{{id}}","part":"Gun"}""");
        Check.That(!(await b.Ok("studio_model_parts", $$"""{"id":"{{id}}"}""")).Contains("Gun"), "the part is gone");
    }
}
