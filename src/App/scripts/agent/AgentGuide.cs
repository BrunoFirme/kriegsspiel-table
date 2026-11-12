using System;
using System.Linq;

namespace KTable.App;

static class AgentGuide
{
    public const int InstructionsMost = 1500, DescriptionMost = 2000;

    public const string Instructions = "Kriegsspiel Table, a tabletop for umpired wargames. You act as the person at this PC, with their seat's rights; your changes are logged as agent. " +
        "Call status first. Before the first step of a kind of work, call how_to with its topic (" + TopicList + "): it names the tools that do it and the order they go in. " +
        "Steps that do not wait on each other's answers go together in one batch call. " +
        "Look at what you made (snapshot, view_map, the pictures the tools return) and correct it until it matches what was asked. " +
        "Lists are given a page at a time: pass offset for the next page. To follow what others do at the table, wait_for waits for it instead of asking again and again. " +
        "For anything no tool covers, ui_read and ui_act use the app's own controls as the person does. " +
        "Change this PC's settings and the person's own view only when they ask.";

    const string TopicList = "start, units, studio, figures, table, map, scripts, records, settings, app, help";

    static readonly (string Topic, string Text)[] Topics =
    {
        ("start", "Go where the work is: open_studio (a pack's pieces, or a new pack), host_game (a scenario, or a new blank one), join_game (someone else's table), close_studio, leave_game. " +
                  "From the title screen, list_modules and import_module convert a digital tabletop module into a pack and a scenario. status says what is open and this seat's role."),
        ("units", "For a unit, make_unit does it in one call: a block with its lettering and symbol, its counters, text lines, looks, tags and script, with a picture of it. make_units makes many."),
        ("studio", "In the piece studio: start a kind (studio_make_block, studio_new_piece, studio_pieces_from_pictures, studio_import_models, studio_new_effect); " +
                   "give it native data before any script (studio_set_piece, studio_set_counter, studio_set_text_line, studio_add_look, studio_set_markers, studio_set_parts, studio_set_effect); " +
                   "draw its faces as layers (studio_read_face, then studio_write_face); add behaviour with studio_write_script after reading api_reference with find. " +
                   "studio_view_piece shows a kind as a game shows it; studio_undo takes your last step back; studio_picture_size sizes a face's picture; studio_lay_out lays the pieces out again; " +
                   "studio_steps drives the studio's own tools as a person would. A pack's name, authors and description: studio_set_pack. A standard pack is copied from, never changed: studio_duplicate_piece."),
        ("figures", "For a 3D figure: studio_import_models (a model file), then studio_model_view from each side, studio_model_paint (whole, by material or by a box) and studio_model_decal; " +
                    "studio_model_add_part builds from shapes or a model file, also onto a block, where it sits centred on top; studio_model_batch builds many parts in one go; studio_model_measure checks the gaps. " +
                    "A stand of figures is one figure kind and a base kind that carries it several times: studio_set_parts with {kind, count, ranks}."),
        ("table", "At the table: view_map shows the map from above with a grid in the coordinates pieces are placed by, so find places on it before placing; what_is_at says what lies at a point. " +
                  "add_pack, list_kinds, place_piece or place_pieces (many at once, in a formation) and form_up (pieces already there, into a line, column, square, wedge…, or along a path); " +
                  "move_piece, set_piece_values, set_piece_options, delete_piece, place_markers, play_effect, throw_dice (dice pieces), decals (pictures stamped on the table or a piece). " +
                  "Effects (smoke, fire, volleys, mist) are pieces of the standard pack " + FirmeEffects.PackName + ": add_pack it if list_packs shows it is not at the table, then place them. " +
                  "copy_pieces and paste_pieces carry pieces as they stand from one scenario into another: the clipboard outlives the game, so copy before leave_game and paste after host_game. " +
                  "stow_pieces puts pieces away inside another, as a bag holds them; unstow_piece takes them out; ready_piece marks the one a holder releases and recalls with one key. " +
                  "measure and in_range measure as the ruler and the range rings do. end_turn, set_clock, roll_dice, say, set_zone, set_briefing, undo and redo. " +
                  "On the person's own screen, when asked: select_pieces, show_view, set_view. read_pointers says where the umpires point; wait_for waits for what happens next."),
        ("map", "add_map_sheets (pictures, 3D terrain and heightmaps) and set_map_sheet (place, size, turn, crop, relief, its height, a new picture, turned with its pieces). " +
                "set_map_scale before ground work: shape_ground raises, lowers, smooths and flattens, and paints ground types by stroke, fill or shape, sparse at a density " +
                "(wood takes trees); ground_at reads it back. What people made is built on top: build_road (tracks, roads, chaussées, streams and rivers through waypoints, finding their way), " +
                "build_place (a settlement from hamlet to walled city, a fort, a single building), build_details (plot boundaries and farms along a stroke); list_built and remove_built. " +
                "set_light (a preset, the light, the backdrop round the table, the table's shade, the map style, contours, a snow line, streams, the wind), set_grid, " +
                "draw (lines, arrows, names on the map, rectangles, ellipses, curves and washed areas, on a named drawing layer if asked), list_drawings, erase_drawings, drawing_layers."),
        ("scripts", "write_script gives a piece its own script or sets the global script; run_lua runs Lua inside one; read_script and read_console read them; check_scripts runs every script on a copy. " +
                    "Read api_reference with find before writing. What scripts made is used as a player does: list_buttons, press_button, type_in_box, pick_menu_row, press_hotkey, press_number, " +
                    "read_panels and use_panel (a script's panels), answer_dialog (the windows game.dialog opens). A kind's script for every game its pack goes to: studio_write_script."),
        ("records", "read_log reads the Log everyone reads; look_back reads a kept turn or a save against the table now; list_saves, save_game, load_save; saves does the Saves window's work " +
                    "(saves, folders, renames, the trash); save_scenario_copy makes a new scenario of the table; read_book reads a book piece's pages; read_briefing; " +
                    "read_scenario says how the scenario is set; list_people who is at the table; joining gives the invitation; the password stays with the people."),
        ("settings", "This PC's settings (graphics, sound, interface size, captures…): read_settings, and set_settings only when the person asks."),
        ("app", "ui_read lists every control the person can see in the app's windows (buttons, toggles, fields, choices, lists, tabs, menu rows) with a ref; " +
                "ui_act clicks, sets, opens or closes one by its ref, presses keys and types. " +
                "Read again after each change, since refs last only until the next read. Use a dedicated tool when there is one. The title screen is only read: its work goes through status, host_game, " +
                "join_game, open_studio and import_module, and installing, removing, exporting, converting back and the trash of packs and scenarios stay the person's; so does deleting saves for good."),
        ("help", "read_guide reads the in-app guides (the umpire's, the player's, scripting, keys and mouse, for makers) and finds a word in them; read_app_log reads the app's own log, " +
                 "its warnings and errors, and the engine's log; diagnostics gives what Copy diagnostics puts together, for finding out why something went wrong."),
    };

    public static string HowTo(string? topic, string? find)
    {
        if (topic?.Trim() is { Length: > 0 } t)
        {
            var hit = Topics.FirstOrDefault(x => string.Equals(x.Topic, t, StringComparison.OrdinalIgnoreCase));
            return hit.Text ?? $"No topic {t}: {TopicList}.";
        }
        if (find?.Trim() is { Length: > 0 } word)
        {
            var found = Topics.Where(x => x.Text.Contains(word, StringComparison.OrdinalIgnoreCase)).ToList();
            return found.Count == 0 ? $"No topic mentions {word}: {TopicList}." : string.Join("\n\n", found.Select(x => $"{x.Topic}: {x.Text}"));
        }
        return string.Join("\n\n", Topics.Select(x => $"{x.Topic}: {x.Text}"));
    }
}
