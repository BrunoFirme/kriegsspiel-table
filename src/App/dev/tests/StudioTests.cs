using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KTable.Client.Prefs;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server;

namespace KTable.App;

static class StudioTests
{
    internal sealed record Rig(PieceStudio Studio, PackageStore Store, string PackId)
    {
        public PackManifest OnDisk() => Json.Load<PackManifest>(Path.Combine(Store.Root, PackId, "pack.json"));
    }

    internal static async Task<Rig> Open(TestRunner r)
    {
        var root = Path.Combine(Path.GetTempPath(), "ktable-nodetest-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var packId = PackEdit.CreatePack(new PackageStore(root), "Test pieces", "Tester");
        {
            var maker = new PackageStore(root);
            var pack = maker.Pack(packId)!;
            var sha = PackEdit.AddImage(maker, pack, Godot.Image.CreateEmpty(8, 8, false, Godot.Image.Format.Rgba8));
            PackEdit.NewDef(maker, pack, "Hussars", sha, 8, 8, 1.0, "box");
            PackEdit.Save(maker, pack);
        }
        var store = new PackageStore(root);
        var s = new Session(new Assets(store));
        var g = new GameState { Name = "Test pieces", Packs = store.ListPackIds().ToList() };
        g.Layers[Layers.Umpire] = new();
        var server = new GameServer(g, store);
        void Deliver(System.Collections.Generic.List<Outgoing> outs)
        {
            foreach (var o in outs)
                if (o.Message is CommandMessage cm) s.OnCommand(cm.Command);
                else if (o.Message is JoinResult jr) s.OnJoined(jr);
        }
        s.Send = c => Deliver(server.Handle(1, c));
        s.Request = m => Deliver(server.Request(1, m));
        s.SetName("Tester");
        Deliver(server.Join(1, new JoinRequest { Name = "Tester" }));
        var cfg = new Settings();
        var table = new Table { Name = "Table" };
        r.AddChild(table);
        table.Bind(s, cfg);
        var ed = new PieceStudio { Name = "Studio" };
        r.AddChild(ed);
        ed.Bind(s, table, store, packId, cfg, () => { });
        await r.Frames(5);
        return new Rig(ed, store, packId);
    }

    [NodeTest]
    static async Task A_kind_s_script_edited_as_a_file_runs_as_saved_and_follows_changes_made_in_the_studio(TestRunner r)
    {
        var h = await Open(r);
        var d = h.Studio.Pack.Pieces[0];
        var key = "def:" + d.Id;
        h.Studio.Script.EditAsFile(d, null);
        var link = h.Studio.FileWatch.Of(key);
        Check.That(link != null, "the kind is linked to a file");
        File.WriteAllText(link!.Path, "function on_load() print('from the file') end\n");
        await r.Seconds(1.2);
        Check.Equal("function on_load() print('from the file') end\n", d.Script, "the saved file is the kind's script");
        h.Studio.Script.SetFromOutside(d.Id, "x = 1\n");
        await r.Seconds(0.8);
        Check.Equal("x = 1\n", File.ReadAllText(link.Path), "a change made in the studio is written back to the file");
        Check.That(File.ReadAllText(h.Studio.FileWatch.Files.LogPath).Contains(link.File), "console.log names the file");
        h.Studio.FileWatch.Unlink(key);
        Check.That(File.Exists(link.Path) && h.Studio.FileWatch.Of(key) == null, "stopping keeps the file");
    }

    [NodeTest]
    static async Task An_agent_gives_a_kind_a_counter_that_reaches_the_pack_on_disk_and_is_refused_a_hostile_script(TestRunner r)
    {
        var h = await Open(r);
        var agent = new AgentEndpoint(new Settings()) { Studio = h.Studio };
        r.AddChild(agent);
        static string Call(string tool, string args) => $$$"""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"{{{tool}}}","arguments":{{{args}}}}}""";
        var listed = await agent.Handle(Call("studio_list_pieces", "{}"));
        Check.That(listed != null && listed.Contains("Hussars"), "the pack's kinds are listed: " + listed);
        var d = h.Studio.Pack.Pieces[0];
        var counted = await agent.Handle(Call("studio_set_counter", $$"""{"id":"{{d.Id}}","name":"Strength","max":12,"default":8}"""));
        Check.That(counted != null && counted.Contains("Strength"), "the counter is added: " + counted);
        Check.That(h.Studio.FlushPack(), "the pack saves");
        var saved = h.OnDisk().Pieces.First(p => p.Id == d.Id);
        Check.Equal((12, 8), (saved.Counters[0].Max, saved.Counters[0].Default), "the counter is in pack.json");
        var refused = await agent.Handle(Call("studio_write_script", $$"""{"id":"{{d.Id}}","text":"WebRequest.get('x')"}"""));
        Check.That(refused != null && refused.Contains("Refused") && saved.Script == null, "a hostile script is refused");
    }

    [NodeTest]
    static async Task An_agent_places_prints_and_markers_and_its_undo_takes_them_back_one_by_one(TestRunner r)
    {
        var h = await Open(r);
        var agent = new AgentEndpoint(new Settings()) { Studio = h.Studio };
        r.AddChild(agent);
        static string Call(string tool, string args) => $$$"""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"{{{tool}}}","arguments":{{{args}}}}}""";
        var d = h.Studio.Pack.Pieces[0];
        var copied = await agent.Handle(Call("studio_duplicate_piece", $$"""{"id":"{{d.Id}}","name":"Vedette"}"""));
        var vedette = h.Studio.Pack.Pieces.FirstOrDefault(p => p.Name == "Vedette");
        Check.That(vedette != null, "the kind is copied: " + copied);
        await agent.Handle(Call("studio_set_counter", $$"""{"id":"{{d.Id}}","name":"Strength","max":12,"default":10,"side":"back","x":0.1,"y":0.5,"w":0.8,"h":0.3}"""));
        await agent.Handle(Call("studio_set_text_line", $$"""{"id":"{{d.Id}}","name":"Commander","default":"Maj. Krafft","side":"front"}"""));
        await agent.Handle(Call("studio_set_markers", $$"""{"id":"{{d.Id}}","markers":[{"kind":"{{vedette!.Id}}","x":0,"z":-1}]}"""));
        Check.Equal(("back", 0.5), (d.Counters[0].Place?.Side, d.Counters[0].Place?.Y), "the counter prints where it was put");
        Check.Equal(("front", 1), (d.TextLines[0].Place?.Side, d.Attachments.Count), "the text line prints on the front and the marker is set");
        await agent.Handle(Call("studio_undo", "{}"));
        Check.Equal(0, d.Attachments.Count, "undo takes the marker away");
        await agent.Handle(Call("studio_undo", "{}"));
        Check.Equal(0, d.TextLines.Count, "then the text line");
        await agent.Handle(Call("studio_undo", "{}"));
        Check.Equal(0, d.Counters.Count, "then the counter");
        await agent.Handle(Call("studio_delete_piece", $$"""{"id":"{{vedette.Id}}"}"""));
        Check.That(h.Studio.Pack.Pieces.All(p => p.Id != vedette.Id), "the copy is deleted");
        await agent.Handle(Call("studio_undo", "{}"));
        Check.That(h.Studio.Pack.Pieces.Any(p => p.Id == vedette.Id), "undo brings the deleted kind back");
        await agent.Handle(Call("studio_undo", "{}"));
        Check.That(h.Studio.Pack.Pieces.All(p => p.Id != vedette.Id), "and undoing the copy takes it away again");
    }

    [NodeTest]
    static async Task A_standard_die_copied_into_a_pack_is_its_own_to_resize_and_the_original_stays_as_shipped(TestRunner r)
    {
        var h = await Open(r);
        StandardPacks.Ensure(h.Store);
        var list = h.Studio.NewPieces.StandardList(h.Store.Pack(StandardPacks.Dice)!, _ => { });
        r.AddChild(list);
        list.Size = new Godot.Vector2(740, 0);
        await r.Frames(2);
        var cards = list.FindChildren("*", nameof(Godot.GridContainer), true, false).OfType<Godot.GridContainer>().First().GetChildren().OfType<Godot.Control>().ToList();
        var right = cards.Max(c => c.Position.X + c.Size.X);
        Check.That(Godot.Mathf.Abs(right - list.Size.X) < 1, $"the dice fill the dialog's width, no strip left empty: {right} of {list.Size.X}");
        var labels = list.FindChildren("*", nameof(Godot.Label), true, false).OfType<Godot.Label>().Select(l => l.Text).ToList();
        Check.That(new[] { "d4", "d6", "d8", "d10", "d12", "d20", "hit die", "direction die" }.All(labels.Contains), "the dice are offered: " + string.Join(", ", labels));
        Check.That(labels.Any(l => l.Contains("Aborted / missing")) && !labels.Contains("1 · 2 · 3 · 4 · 5 · 6"), "a picture die names its sides, a numbered one does not");
        var found = StandardPacks.Piece(h.Store, StandardPacks.Dice + ".hit-die", h.PackId);
        Check.That(found is { Def.Name: "hit die" }, "the duplicate tool finds a standard die by its id");
        Check.That(StandardPacks.Piece(h.Store, StandardPacks.Dice + ".hit-die", StandardPacks.Dice) == null, "but not to copy into the dice pack itself");
        var big = PackEdit.CopyInto(h.Store, found!.Value.Pack, found.Value.Def, h.Studio.Pack);
        Check.That(!StandardPacks.IsStandard(big.Id) && big.Faces[5].Value == "Aborted / missing", "the die is copied into the pack with its sides");
        big.Size = new Size2(1.2, 1.2);
        big.Thickness = 1.2;
        PackEdit.Save(h.Store, h.Studio.Pack);
        var saved = h.OnDisk().Pieces.First(p => p.Id == big.Id);
        Check.Equal((1.2, 1.2), (saved.Size.W, saved.Thickness), "the copy is twice the size in pack.json");
        Check.That(File.Exists(h.Store.AssetPath(h.Studio.Pack, saved.Mesh)) && File.Exists(h.Store.AssetPath(h.Studio.Pack, saved.Texture)), "its shape and picture are in the pack");
        var shipped = Json.Load<PackManifest>(Path.Combine(h.Store.Root, StandardPacks.Dice, "pack.json")).Pieces.First(p => p.Id == StandardPacks.Dice + ".hit-die");
        Check.Equal((0.6, "hit die"), (shipped.Size.W, shipped.Name), "the standard die stays as shipped");
    }

    static async Task Until(TestRunner r, System.Func<bool> done)
    {
        for (int i = 0; i < 120 && !done(); i++) await r.Frames(1);
    }

    [NodeTest]
    static async Task Ctrl_Z_takes_back_the_last_setting_and_only_that(TestRunner r)
    {
        var h = await Open(r);
        var s = h.Studio;
        var d = s.Sel!;
        double was = d.Thickness;
        s.Apply(x => x.Counters.Add(new CounterDef { Name = "Ammo" }), what: "a new counter");
        s.Apply(x => x.Thickness = was + 0.3, what: "the thickness");
        s.Apply(x => x.Thickness = was + 0.4, what: "the thickness");
        s.Undo();
        await Until(r, () => System.Math.Abs(d.Thickness - was) < 1e-9);
        Check.Equal((was, 1), (d.Thickness, d.Counters.Count), "one undo takes back the thickness typed in steps, and leaves the counter added before");
        s.Redo();
        await Until(r, () => System.Math.Abs(d.Thickness - was) > 1e-9);
        Check.Equal(was + 0.4, d.Thickness, "redo puts it back");
        s.Apply(x => x.Counters.RemoveAt(0), what: "a counter taken away");
        s.Undo();
        await Until(r, () => d.Counters.Count == 1);
        Check.Equal("Ammo", d.Counters.FirstOrDefault()?.Name, "a counter taken away comes back");
    }

    [NodeTest]
    static Task A_look_still_copied_from_look_1_follows_it_and_one_painted_on_its_own_does_not(TestRunner r)
    {
        var d = new PieceDef { Id = "k", Name = "Battery", Image = "a", Back = "b" };
        d.States.Add(new PieceStateDef { Name = "Fresh", Image = "a", Back = "b" });
        d.States.Add(new PieceStateDef { Name = "Limbered", Image = "a", Back = "b" });
        d.States.Add(new PieceStateDef { Name = "Wrecked", Image = "w", Back = "b" });
        PieceFaces.Of(d).First(f => f.Key == "front").Set(d, "a2");
        PieceFaces.Of(d).First(f => f.Key == "back").Set(d, "b2");
        Check.Equal(("a2", "a2", "w"), (d.States[0].Image, d.States[1].Image, d.States[2].Image), "the copied look follows the new front, the painted one keeps its own");
        Check.Equal(("b2", "b2"), (d.States[1].Back, d.States[2].Back), "both still had look 1's back, so both follow it");
        Check.That(PieceFaces.Follows(d, 1) && !PieceFaces.Follows(d, 2), "the Piece tab can tell which looks are copies");
        return Task.CompletedTask;
    }

    [NodeTest]
    static async Task An_agent_sets_the_pack_s_name_authors_and_description_and_they_reach_the_disk(TestRunner r)
    {
        var h = await Open(r);
        var agent = new AgentEndpoint(new Settings()) { Studio = h.Studio };
        r.AddChild(agent);
        var answer = await agent.Handle("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"studio_set_pack","arguments":{"name":"Waterloo 1815","authors":["Bruno","Ann"],"description":"Brigades of the three armies, 1:20,000."}}}""");
        Check.That(answer != null && answer.Contains("Waterloo 1815"), "the details are set: " + answer);
        Check.That(h.Studio.FlushPack(), "the pack saves");
        var disk = h.OnDisk();
        Check.Equal(("Waterloo 1815", "Bruno, Ann", "Brigades of the three armies, 1:20,000."), (disk.Name, string.Join(", ", disk.Source.Authors), disk.Description), "pack.json holds them");
    }

    [NodeTest]
    static async Task A_face_whose_picture_is_gone_opens_blank_at_the_front_s_size_and_says_so(TestRunner r)
    {
        var h = await Open(r);
        var d = h.Studio.Pack.Pieces[0];
        var gone = new PieceFaces.Face("back", "Back", new string('0', 64), (_, _) => { }, 0, true);
        var doc = h.Studio.DocForPicture(d, gone, out var note);
        Check.Equal((8, 8), (doc.W, doc.H), "a blank the front picture's size");
        Check.That(note?.Contains("missing") == true, "it says the picture is missing: " + note);
        var fresh = h.Studio.DocForPicture(d, gone with { Sha = null }, out note);
        Check.That(note == null && fresh.Layers.Count == 1, "a face with no picture yet opens blank without a word");
    }

    [NodeTest]
    static async Task A_face_whose_layers_are_damaged_opens_flat_says_so_and_keeps_a_copy_in_the_trash(TestRunner r)
    {
        var h = await Open(r);
        var sha = new string('a', 64);
        var path = ArtStore.DocPath(h.Store, h.PackId, sha);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ torn");
        var told = new System.Collections.Generic.List<string>();
        void Heard(string text) => told.Add(text);
        Problems.Told += Heard;
        try
        {
            Check.That(ArtStore.Load(h.Store, h.PackId, sha) == null, "a damaged face has no layers to open");
            Check.That(ArtStore.Load(h.Store, h.PackId, sha) == null, "nor the second time");
        }
        finally { Problems.Told -= Heard; }
        Check.Equal(1, told.Count, "said once: " + string.Join(" | ", told));
        Check.That(told[0].Contains(sha + ".json") && told[0].Contains("trash"), "names the file and where its copy is: " + told[0]);
        var bins = Path.Combine(SaveStore.Default.TrashDir, SaveStore.PackFilesDir);
        var copies = Directory.GetFiles(bins, sha + ".json", SearchOption.AllDirectories);
        Check.Equal(1, copies.Length, "one copy in the trash");
        Check.Equal("{ torn", File.ReadAllText(copies[0]), "the copy is the damaged file as it was");
        Check.Equal("{ torn", File.ReadAllText(path), "the damaged file is left where it was");
    }

    [NodeTest]
    static async Task A_piece_copied_and_taken_away_in_the_studio_is_so_in_the_pack_on_the_disk(TestRunner r)
    {
        var h = await Open(r);
        Check.Equal("Hussars", string.Join(",", h.OnDisk().Pieces.Select(p => p.Name)), "the pack on the disk at the start");
        Check.Equal("Hussars", string.Join(",", h.Studio.Pack.Pieces.Select(p => p.Name)), "the pack in the studio at the start");
        var first = h.OnDisk().Pieces[0];
        h.Studio.DuplicateSel(h.Studio.Pack.Pieces[0]);
        await r.Frames(10);
        Check.That(await h.Studio.FlushAllAsync() == null, "saved");
        var two = h.OnDisk().Pieces;
        Check.Equal(2, two.Count, "after a copy");
        Check.Equal("Hussars copy", two[1].Name, "the copy, beside its original");
        Check.That(two[1].Id != first.Id, "the copy has an id of its own");
        h.Studio.DeleteSel(h.Studio.Pack.Pieces[1]);
        await r.Frame();
        Check.Equal(2, h.Studio.Pack.Pieces.Count, "nothing taken away before it is confirmed");
        var ask = h.Studio.FindChildren("*", nameof(Godot.ConfirmationDialog), true, false).OfType<Godot.ConfirmationDialog>().Single();
        Check.Equal("Delete \"Hussars copy\"", ask.Title, "what is asked");
        ask.EmitSignal(Godot.AcceptDialog.SignalName.Confirmed);
        await r.Frames(5);
        await h.Studio.FlushAllAsync();
        Check.Equal(first.Id, string.Join(",", h.OnDisk().Pieces.Select(p => p.Id)), "the copy taken away, the first left");
    }

    static string HalvesPicture(string folder, string name, int w, int h)
    {
        var img = Godot.Image.CreateEmpty(w, h, false, Godot.Image.Format.Rgba8);
        img.FillRect(new Godot.Rect2I(0, 0, w / 2, h), Godot.Colors.Green);
        img.FillRect(new Godot.Rect2I(w / 2, 0, w - w / 2, h), Godot.Colors.Yellow);
        var path = Path.Combine(folder, name);
        img.SavePng(path);
        return path;
    }

    static (int, int, int, int) TexelAt(ModelPartView v, Godot.Vector2 uv)
    {
        int o = ((int)(uv.Y * v.H) * v.W + (int)(uv.X * v.W)) * 4;
        return (v.Paint[o], v.Paint[o + 1], v.Paint[o + 2], v.Paint[o + 3]);
    }

    static (int, int, int, int) Opaque(Godot.Color c) => (c.R8, c.G8, c.B8, 255);

    [ScreenTest]
    static async Task A_texture_made_for_an_imported_model_lands_where_its_maker_drew_it_and_undo_takes_it_off(TestRunner r)
    {
        var h = await Open(r);
        var red = Godot.Image.CreateEmpty(4, 4, false, Godot.Image.Format.Rgba8);
        red.Fill(Godot.Colors.Red);
        var m = new MeshData { Tex = { red }, MatColor = { Godot.Colors.White } };
        const int Columns = 4;
        for (int i = 0; i < Columns; i++)
        {
            float x0 = i / (float)Columns, x1 = (i + 1) / (float)Columns;
            void Corner(float x, float y) => m.Add(new Godot.Vector3(x, y, 0), Godot.Vector3.Back, new Godot.Vector2(x, 1 - y), Godot.Colors.White);
            Corner(x0, 0);
            Corner(x1, 0);
            Corner(x1, 1);
            Corner(x0, 0);
            Corner(x1, 1);
            Corner(x0, 1);
            m.Mat.Add(0);
            m.Mat.Add(0);
        }
        await h.Studio.NewModelPiece("Banner", m, "mesh");
        var model = h.Studio.Model!;
        var part = model.Doc.Parts.Single();
        Check.That(part.OwnLayout != null, "the model keeps the layout its maker drew");
        var said = model.ShapeFaces.PutPicture($"p:{part.Id}:0", HalvesPicture(h.Store.Root, "banner.png", 64, 64));
        Check.That(said.Contains("texture now"), "it says the texture is on: " + said);
        var v = model.Views[part.Id];
        int seen = 0;
        for (int t = 0; t < v.Data.Tris; t++)
        {
            var at = (v.Data.V[t * 3] + v.Data.V[t * 3 + 1] + v.Data.V[t * 3 + 2]) / 3;
            float middle = v.Data.Bounds().GetCenter().X;
            if (System.Math.Abs(at.X - middle) < 0.15f) continue;
            var uv = (v.Data.UV[t * 3] + v.Data.UV[t * 3 + 1] + v.Data.UV[t * 3 + 2]) / 3;
            Check.Equal(Opaque(at.X < middle ? Godot.Colors.Green : Godot.Colors.Yellow), TexelAt(v, uv), $"the model at x {at.X:0.00}");
            seen++;
        }
        Check.That(seen >= 4, $"texels on both halves were looked at ({seen})");
        model.Undo();
        Check.That(model.Views[part.Id].Paint.All(b => b == 0), "undo takes the texture off");
    }

    [ScreenTest]
    static async Task A_picture_put_on_a_flag_s_front_covers_it_reading_left_to_right_from_the_front(TestRunner r)
    {
        var h = await Open(r);
        await h.Studio.NewModelPiece("Colours", ModelImport.Primitive("flag"), "flag");
        var model = h.Studio.Model!;
        var part = model.Doc.Parts.Single();
        var v = model.Views[part.Id];
        int front = model.ShapeFaces.FacesOf(v).FindIndex(f => f.Name == "Front");
        Check.That(front >= 0, "the flag has a front");
        var said = model.ShapeFaces.PutPicture($"p:{part.Id}:{front}", HalvesPicture(h.Store.Root, "colours.png", 60, 40));
        Check.That(said.Contains("covers the front"), "it says the front is covered: " + said);
        int seen = 0;
        for (int t = 0; t < v.Data.Tris; t++)
        {
            var a = v.Data.V[t * 3];
            var b = v.Data.V[t * 3 + 1];
            var c = v.Data.V[t * 3 + 2];
            if ((c - a).Cross(b - a).Z >= 0) continue;
            var at = (a + b + c) / 3;
            var uv = (v.Data.UV[t * 3] + v.Data.UV[t * 3 + 1] + v.Data.UV[t * 3 + 2]) / 3;
            Check.Equal(Opaque(at.X > 0 ? Godot.Colors.Green : Godot.Colors.Yellow), TexelAt(v, uv), $"the front at x {at.X:0.00}, its left seen from the front being +x");
            seen++;
        }
        Check.Equal(2, seen, "the front's two triangles looked at");
        Check.That(part.FaceDocs?.ContainsKey(front.ToString()) == true, "the front keeps its layers for painting in 2D");
    }
}
