using System.Text.Json;
using System.Text.Json.Nodes;

namespace KTable.Client.Agent;

public static class ToolEffects
{
    public static readonly IReadOnlySet<string> Reads = new HashSet<string>
    {
        "status", "how_to", "api_reference", "snapshot", "list_scenarios", "list_packs", "check_scripts", "list_pieces", "read_piece", "read_script", "read_console", "list_kinds",
        "read_briefing", "list_drawings", "list_buttons", "read_panels", "read_scenario", "list_people", "wait_for", "studio_list_pieces", "studio_read_piece", "studio_model_view",
        "studio_model_parts", "studio_model_measure", "studio_read_face", "list_map_sheets", "view_map", "list_saves", "ground_at", "list_built", "measure", "in_range", "read_log", "read_messages",
        "read_book", "list_places", "what_is_at", "read_pointers", "read_settings", "list_modules", "read_guide", "read_app_log", "diagnostics", "ui_read", Mcp.AwaitName,
    };

    public static readonly IReadOnlySet<string> Destroys = new HashSet<string>
    {
        "delete_piece", "erase_drawings", "remove_built", "studio_delete_piece", "studio_remove", "studio_model_remove_part", "studio_tidy_pack", "leave_game", "load_save",
        "set_map_sheet", "saves", "import_module", "run_lua", "ui_act", "set_zone", "decals", "drawing_layers", Mcp.BatchName,
    };

    public static readonly IReadOnlyList<string> ReadingStarts = ["list_", "read_"];

    public static readonly IReadOnlySet<string> Shows = new HashSet<string>
    {
        "snapshot", "view_map", "read_book", "studio_view_piece", "studio_model_view", "studio_read_face", "pov_capture", Mcp.BatchName, Mcp.AwaitName,
    };

    public static readonly IReadOnlySet<string> Looks = new HashSet<string>
    {
        "make_unit", "make_units", "studio_read_piece", "studio_make_block", "studio_new_piece", "studio_duplicate_piece", "studio_set_piece", "studio_frame", "studio_set_state",
        "studio_swap_sides", "studio_write_face", "studio_picture_size", "studio_model_add_part", "studio_model_set_part", "studio_model_batch", "studio_model_remove_part",
        "studio_model_paint", "studio_model_decal",
    };

    public const string Look = "look", LookWords = "add a picture of it to the answer (words alone if left out)";

    public static bool Looked(JsonElement args) => args.ValueKind == JsonValueKind.Object && args.TryGetProperty(Look, out var look) && look.ValueKind == JsonValueKind.True;

    public static ToolResult Seen(string tool, JsonElement args, ToolResult result) => result.Images == null || Shows.Contains(tool) || Looked(args) ? result : result with { Images = null };

    public static JsonNode Schema(AgentTool tool)
    {
        var schema = JsonNode.Parse(tool.Schema.ToJsonString())!;
        if (Looks.Contains(tool.Name) && schema["properties"] is JsonObject args && !args.ContainsKey(Look)) args[Look] = new JsonObject { ["type"] = "boolean", ["description"] = LookWords };
        return schema;
    }

    public static JsonObject Marks(string tool) => Reads.Contains(tool) ? new() { ["readOnlyHint"] = true } : new() { ["destructiveHint"] = Destroys.Contains(tool) };
}
