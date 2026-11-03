using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KTable.Client.Agent;
using KTable.Core.Model;

namespace KTable.App;

sealed partial class StudioTools
{

    IEnumerable<AgentTool> PackTools()
    {
        yield return new AgentTool("studio_set_look",
            "Changes one look of a kind as the Block layer does with that look shown: its name, its own size (a routed look smaller, a column longer; width or height alone keeps the proportions) and its own outline. " +
            "Look 1 is the kind itself: its size and outline are the kind's (studio_set_piece).",
            Args.Schema(new Arg("id", "string", "the kind's id", true), new Arg("look", "number", "its number from 1", true), new Arg("name", "string", "new name"),
                new Arg("width", "number", "map units"), new Arg("height", "number", "map units"), new Arg("outline", "string", OutlineList),
                new Arg("same_size", "boolean", "true: back to look 1's size and outline")),
            SetLook);
        yield return new AgentTool("studio_swap_sides",
            "Swaps a flat kind's front and back, with everything printed on them, as the studio's Swap front and back does.",
            Args.Schema(new Arg("id", "string", "the kind's id", true)), SwapSides);
        yield return new AgentTool("studio_set_pack",
            "Sets the pack's name (its id stays), who made it, and its description (what it holds, its period and scale, how its pieces are used).",
            Args.Schema(new Arg("name", "string", "the pack's name"), new Arg("authors", "array", "who made it"), new Arg("description", "string", "a few lines; empty clears it")), SetPack);
        yield return new AgentTool("studio_tidy_pack",
            "Moves the pack's pictures and files that nothing uses any more to the trash, as Remove pictures nothing uses does.",
            Args.Schema(), _ => TidyPack());
        yield return new AgentTool("studio_export_pack",
            "Saves the pack open in the studio as a .ktpack file to share or keep.",
            Args.Schema(new Arg("path", "string", "where to write it (a folder or a full .ktpack path; default the Documents folder)")), ExportPack);
    }

    Task<ToolResult> SetLook(JsonElement a) => Change(a, "changed a look", async (s, d) =>
    {
        int i = (int)(a.Num("look") ?? 0) - 1;
        if (d.States.Count == 0) return ToolResult.Fail($"{d.Name} has one look: add another with studio_add_look first.");
        if (i < 0 || i >= d.States.Count) return ToolResult.Fail($"{d.Name} has {d.States.Count} looks.");
        var said = a.Str("outline") ?? a.Str("shape");
        var outline = OutlineNamed(said);
        if (said != null && outline == null) return ToolResult.Fail("outline is one of " + OutlineList + ".");
        if (d.Kind == "model" && (outline != null || a.Num("width") != null || a.Num("height") != null)) return ToolResult.Fail("A model's looks share its size and have no outline.");
        var w = a.Num("width"); var h = a.Num("height");
        bool same = a.Flag("same_size") == true;
        if (i == 0 && (w != null || h != null || outline != null)) return ToolResult.Fail("Look 1 is the kind itself: change its size and outline with studio_set_piece.");
        Edit(s, d, x =>
        {
            var st = x.States[i];
            if (a.Str("name") is { Length: > 0 } name) st.Name = name.Trim();
            if (same) { st.Size = null; st.Shape = null; }
            else if (w is > 0 || h is > 0) { st.Size = Proportional(st.Size ?? x.Size, w, h); st.Shape ??= x.Shape; }
        });
        if (outline != null && !same) await s.Framer.SetOutline(d, i, outline);
        return new ToolResult(LookLine(s, d, i), false, Pictures(FacePng(s, PieceFaces.Of(d).FirstOrDefault(f => f.Key == PieceFaces.Key(i, false))?.Sha)));
    });

    Task<ToolResult> SwapSides(JsonElement a) => Change(a, "swapped the sides", async (s, d) =>
    {
        if (d.Kind == "model") return ToolResult.Fail($"{d.Name} is a 3D kind: it has no front and back to swap.");
        if (!await Select(s, d)) return ToolResult.Fail($"{d.Name} could not be selected.");
        await s.SwapSidesAsync();
        return new ToolResult($"Swapped the front and back of {d.Name}.", false, Pictures(FacePng(s, PieceFaces.Of(d).FirstOrDefault()?.Sha)));
    });

    Task<ToolResult> SetPack(JsonElement a) => On(s =>
    {
        var name = a.Str("name")?.Trim();
        var authors = Strings(a, "authors");
        var about = a.Str("description");
        if (string.IsNullOrEmpty(name) && authors == null && about == null) return Task.FromResult(ToolResult.Fail("Give name, authors or description."));
        if (!string.IsNullOrEmpty(name)) s.Pack.Name = name;
        if (authors != null) s.Pack.Source.Authors = authors.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToList();
        if (about != null) s.Pack.Description = about.Trim().Length == 0 ? null : about.Trim();
        s.SavePack();
        s.Top.ShowPackName();
        _a.Log($"set the details of the pack {s.Pack.Name}");
        return Task.FromResult(new ToolResult(JsonSerializer.Serialize(new { s.Pack.Id, s.Pack.Name, Authors = s.Pack.Source.Authors, s.Pack.Description }, Opts)));
    });

    Task<ToolResult> TidyPack() => On(async s =>
    {
        if (await s.SettleWork() is { } problem) return ToolResult.Fail("The studio could not save what is open first: " + problem);
        int n;
        try { n = s.Menu.RemoveUnusedNow(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ToolResult.Fail("Could not clear out the pack: " + e.Message); }
        _a.Log($"moved {n} unused file{(n == 1 ? "" : "s")} of {s.Pack.Name} to the trash");
        return new ToolResult(n == 0 ? "Nothing to remove: every file is in use." : $"Moved {n} file{(n == 1 ? "" : "s")} nothing uses to the trash.");
    });

    Task<ToolResult> ExportPack(JsonElement a) => On(async s =>
    {
        if (await s.SettleWork() is { } problem) return ToolResult.Fail("The studio could not save what is open first: " + problem);
        if (!s.FlushPack()) return ToolResult.Fail("The pack could not be saved, so it was not exported.");
        var given = a.Str("path");
        var file = given switch
        {
            null or "" => Path.Combine(KTable.Core.DataPaths.Documents, s.Pack.Id + ".ktpack"),
            _ when Directory.Exists(given) => Path.Combine(given, s.Pack.Id + ".ktpack"),
            _ => given.EndsWith(".ktpack", StringComparison.OrdinalIgnoreCase) ? given : given + ".ktpack",
        };
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
            var bytes = await Task.Run(() => WorkshopContent.ExportFile(s.Store, "pack", s.Pack.Id, WorkshopContent.PackMode.None, file));
            _a.Log($"exported {s.Pack.Name}");
            return new ToolResult(FormattableString.Invariant($"Wrote {file} ({bytes / 1024.0 / 1024.0:0.0} MB)."));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { return ToolResult.Fail("Export failed: " + e.Message); }
    });
}
