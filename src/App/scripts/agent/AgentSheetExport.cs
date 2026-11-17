using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KTable.Client.Agent;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

sealed class AgentSheetExport
{
    const double StartWaitSeconds = 120, StartPoll = 0.2;

    readonly AgentTableTools _tools;
    readonly AgentEndpoint _a;

    public AgentSheetExport(AgentTableTools tools, AgentEndpoint a) => (_tools, _a) = (tools, a);

    public IEnumerable<AgentTool> Tools()
    {
        yield return new AgentTool("export_map_sheets",
            "Saves pictured map sheets as one picture file, as Export map sheets does (umpires): laid as on the table, at the sharpest sheet's detail, built places drawn. " +
            $"PNG, or TIFF past {SheetImporter.GodotImageMax} px a side. It never writes over a file unless replace is set.",
            Args.Schema(new Arg("sheets", "array", "sheet ids (every pictured sheet if left out)"),
                new Arg("path", "string", "a folder or a .png, .tif or .tiff path (the exports folder if left out)"),
                new Arg("replace", "boolean", "write over a file already at that path")),
            ExportSheets);
    }

    async Task<ToolResult> ExportSheets(JsonElement a)
    {
        if (_tools.Table(out var why) is not var (s, hud)) return why!;
        if (!s.IsUmpire) return ToolResult.Fail("Only umpires export the map sheets.");
        var all = SheetExportDialog.Exportable(hud.Ctx);
        if (all.Count == 0) return ToolResult.Fail("No map sheet on the table has a picture to export.");
        var wanted = a.Words("sheets");
        var chosen = wanted == null ? all : all.Where(x => wanted.Contains(x.Sheet.Id)).ToList();
        if (wanted?.FirstOrDefault(id => all.All(x => x.Sheet.Id != id)) is { } missing)
            return ToolResult.Fail($"No map sheet {missing} with a picture: these can be exported: {string.Join(", ", all.Select(x => x.Sheet.Id))}.");
        if (SheetExportDialog.Running is { IsCompleted: false }) return ToolResult.Fail("A map is still being exported; wait for it to finish.");
        var frame = SheetExport.FrameOf(chosen);
        string ext = Math.Max(frame.W, frame.H) > SheetImporter.GodotImageMax ? SheetExportFile.Tiff : SheetExportFile.Png;
        var stem = string.Concat((chosen.Count == 1 ? (chosen[0].Sheet.Name.Length > 0 ? chosen[0].Sheet.Name : chosen[0].Sheet.Id) : s.View.Name).Split(Path.GetInvalidFileNameChars())).Trim();
        var named = (stem.Length > 0 ? stem : "map") + ext;
        var given = a.Str("path");
        var path = given switch
        {
            null or "" => Path.Combine(DataPaths.Exports, named),
            _ when Directory.Exists(given) => Path.Combine(given, named),
            _ => SheetExportFile.Named(given),
        };
        if (File.Exists(path) && a.Flag("replace") != true) return ToolResult.Fail($"{path} is there already: pass replace to write over it, or another path.");
        try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return ToolResult.Fail("The folder cannot be made: " + e.Message); }
        var before = SheetExportDialog.Running;
        var since = DateTime.UtcNow;
        SheetExportDialog.Export(hud.Ctx, chosen, frame, path);
        for (double waited = 0; waited < StartWaitSeconds && SheetExportDialog.Running == before; waited += StartPoll) await _a.Delay(StartPoll);
        if (SheetExportDialog.Running is not { } running || running == before)
            return ToolResult.Fail("The export did not start: some of the sheets' map tiles are not on this PC (read_log says which).");
        await running;
        var file = new FileInfo(path);
        if (!file.Exists || file.LastWriteTimeUtc < since) return ToolResult.Fail("The export failed; read_log has the reason.");
        _a.Log("exported the map sheets");
        return new ToolResult(FormattableString.Invariant($"Wrote {path}: {frame.W} × {frame.H} px, {file.Length / 1048576.0:0.0} MB, {chosen.Count} sheet{(chosen.Count == 1 ? "" : "s")}."));
    }
}
