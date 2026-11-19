using System.Text.Json;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client.Agent;

public sealed class DoorTools
{
    readonly TableTools _t;

    public DoorTools(TableTools t) => _t = t;

    public IEnumerable<AgentTool> Tools()
    {
        yield return new AgentTool("set_door",
            "Opens or shuts one door of a building, as the door rows on its right-click menu do. Without door, lists its doors, open or shut. One undoable change.",
            Args.Schema(new Arg("id", "string", "the building's id", true), new Arg("door", "string", "the door's number from 1 or its name"),
                new Arg("open", "boolean", "true opens it, false shuts it; leave out to open")),
            SetDoor);
    }

    async Task<ToolResult> SetDoor(JsonElement a)
    {
        if (_t.Open(out var why) is not { } s) return why!;
        var id = a.Str("id") ?? "";
        if (s.View.Find(Layers.Umpire, id) is not { } p) return ToolResult.Fail($"No piece {id} on this table.");
        var name = TableTools.NameOf(s, p);
        var def = s.Def(p.Def);
        var count = def == null ? 0 : Doors.Count(def, s.Def);
        if (def == null || count == 0) return ToolResult.Fail($"{name} has no doors.");
        string Door(PieceInstance at, int k) => $"door {k + 1}, {Doors.Name(def, k, s.Def)}, {(Doors.Open(def, at.State, k, s.Def) ? "open" : "shut")}";
        string All(PieceInstance at) => string.Join("; ", Enumerable.Range(0, count).Select(k => Door(at, k)));
        var asked = (a.Str("door") ?? (a.Num("door") is { } n ? $"{(int)n}" : "")).Trim();
        if (asked.Length == 0) return new ToolResult($"{name}: {All(p)}.");
        if (!_t.CanChange(s, out why)) return why!;
        var door = int.TryParse(asked, out var number) ? number - 1
            : Enumerable.Range(0, count).FirstOrDefault(k => string.Equals(Doors.Name(def, k, s.Def), asked, StringComparison.OrdinalIgnoreCase), -1);
        if (door < 0 || door >= count) return ToolResult.Fail($"{name} has no door {asked}: {All(p)}.");
        var open = a.Flag("open") ?? true;
        if (Doors.Open(def, p.State, door, s.Def) == open) return new ToolResult($"{name}: {Door(p, door)} already.");
        if (Doors.Toggled(def, p.State, door, s.Def) is not { } to) return ToolResult.Fail($"{name} has no look with only that door {(open ? "open" : "shut")}.");
        s.Do(new SetState { Layer = Layers.Umpire, Id = id, State = to });
        await _t.Landed(s);
        _t.Log($"{(open ? "opened" : "shut")} door {door + 1} of {name} ({id})");
        return new ToolResult($"{name}: {Door(s.View.Find(Layers.Umpire, id) ?? p, door)}.");
    }
}
