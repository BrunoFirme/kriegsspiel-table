using System.Text.Json.Nodes;

namespace KTable.Client.Agent;

public static class ToolEffects
{
    public static readonly IReadOnlySet<string> Reads = new HashSet<string>
    {
        "status", "how_to", "api_reference", "snapshot", "list_scenarios", "list_packs", "check_scripts", "list_pieces", "read_piece", "read_script", "read_console", "list_kinds",
        "read_briefing", "list_drawings", "list_buttons", "read_panels", "read_scenario", "list_people", "wait_for", "studio_list_pieces", "studio_read_piece", "studio_model_view",
        "studio_model_parts", "studio_model_measure", "studio_read_face", "list_map_sheets", "view_map", "list_saves", "ground_at", "list_built", "measure", "in_range", "read_log",
        "read_book", "what_is_at", "read_pointers", "read_settings", "list_modules", "read_guide", "read_app_log", "diagnostics", "ui_read",
    };

    public static readonly IReadOnlySet<string> Destroys = new HashSet<string>
    {
        "delete_piece", "erase_drawings", "remove_built", "studio_delete_piece", "studio_remove", "studio_model_remove_part", "studio_tidy_pack", "leave_game", "load_save",
        "set_map_sheet", "saves", "import_module", "run_lua", "ui_act", "set_zone", "decals", "drawing_layers", Mcp.BatchName,
    };

    public static readonly IReadOnlyList<string> ReadingStarts = ["list_", "read_"];

    public static JsonObject Marks(string tool) => new() { ["readOnlyHint"] = Reads.Contains(tool), ["destructiveHint"] = Destroys.Contains(tool) };
}
