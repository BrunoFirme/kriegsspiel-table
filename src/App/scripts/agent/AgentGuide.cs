using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace KTable.App;

static class AgentGuide
{
    public const int InstructionsMost = 1500, DescriptionMost = 1300, ToolListMost = 142_000;

    public const string Instructions = "Kriegsspiel Table, a tabletop for umpired wargames. You act as the person at this PC, with their seat's rights; your changes are logged as agent. " +
        "Call status first: it says what is open, what the person has selected, looks at and points at, and how the work there is done, the tools and the order they go in. " +
        "For another kind of work call how_to with its topic (" + TopicList + "). " +
        "Steps that do not wait on each other's answers go together in one batch call; the person takes back all a call changed with one undo. " +
        "A call that takes long answers that it is still running: await_result waits for its answer. " +
        "Tools answer in words; look on one that makes something adds its picture. Look at what you made (snapshot, view_map, look) and correct it until it matches what was asked. " +
        "Lists are given a page at a time: pass offset for the next page. To follow what others do at the table, wait_for waits for it instead of asking again and again. " +
        "For anything no tool covers, ui_read and ui_act use the app's own controls as the person does. " +
        "A map place is x, z here and in scripts; the app's windows show the same z as Y, and height as Z. " +
        "Change this PC's settings and the person's own view only when they ask. " + KTable.Client.Agent.Hearsay.Rule;

    const string TopicList = "start, units, studio, figures, table, map, crowds, scripts, records, settings, app, help";

    static readonly (string Topic, string Text)[] Topics =
    {
        ("start", "Go where the work is: open_studio (a pack's pieces, or a new pack), host_game (a scenario, or a new blank one), join_game (someone else's table), close_studio, leave_game. " +
                  "From the title screen, list_modules and import_module convert a digital tabletop module into a pack and a scenario. status says what is open and this seat's role. " +
                  "At a table this PC hosts, open_studio with a piece changes that piece's kind without leaving the game; close_studio then keeps the change for every piece of the kind, as a new kind for that piece alone, or drops it."),
        ("units", "For a unit, make_unit does it in one call: a block with its lettering and symbol, its counters, text lines, states, tags and script; look adds a picture of it. make_units makes many."),
        ("studio", "In the piece studio: start a kind (studio_make_block, studio_new_piece, studio_pieces_from_pictures, studio_import_models, studio_new_effect); " +
                   "give it native data before any script (studio_set_piece, studio_set_counter, studio_set_text_line, also floating over the piece, studio_add_state, a copy of any state, studio_set_markers, studio_set_parts, studio_set_effect); " +
                   "draw its faces as layers (studio_read_face, then studio_write_face); add behaviour with studio_write_script after reading api_reference with find. " +
                   "studio_view_piece shows a kind as a game shows it; studio_undo takes your last step back; studio_picture_size sizes a face's picture; studio_lay_out lays the pieces out again; " +
                   "studio_steps drives the studio's own tools as a person would. A pack's name, authors and description: studio_set_pack. A standard pack is copied from, never changed: studio_duplicate_piece."),
        ("figures", "For a 3D figure: studio_import_models (a model file), then studio_model_view from each side, studio_model_paint (whole, by material or by a box) and studio_model_decal; " +
                    "studio_model_add_part builds from shapes or a model file, also onto a block, where it sits centred on top, each shape matte or metal; studio_model_batch builds many parts in one go; studio_model_measure checks the gaps. " +
                    "A stand of figures is one figure kind and a base kind that carries it several times: studio_set_parts with {kind, count, ranks}."),
        ("table", "At the table: view_map shows the map from above with a grid in the coordinates pieces are placed by, so find places on it before placing; what_is_at says what lies at a point. " +
                  "add_pack, list_kinds, place_piece or place_pieces (many at once, in a formation) and form_up (pieces already there, into a line, column, square, wedge…, or along a path); " +
                  "move_piece, set_piece_values, set_piece_options (lock, tint, size, width, depth and height apart, flying, a note's writing…), delete_piece, place_markers, play_effect, throw_dice (dice pieces), decals (pictures stamped on the table or a piece). " +
                  "Effects (smoke, fire, volleys, mist) are pieces of the standard pack " + FirmeEffects.PackName + ": add_pack it if list_packs shows it is not at the table, then place them. " +
                  "copy_pieces and paste_pieces carry pieces as they stand from one scenario into another: the clipboard outlives the game, so copy before leave_game and paste after host_game. " +
                  "stow_pieces puts pieces away inside another, as a bag holds them; unstow_piece takes them out; ready_piece marks the one a holder releases and recalls with one key. " +
                  "measure and in_range measure as the ruler and the range rings do; measure can pin its legs to the person's screen. end_turn, set_clock (also steps, runs and pauses the time), set_timer, roll_dice, say, set_zone, set_briefing, undo and redo. " +
                  "list_places lists the map's places and where each player's body stands; send_to_place moves them there. travel says how long a trip takes by foot, rider, cart or coach, and its route. " +
                  "On the person's own screen, when asked: select_pieces, show_view, set_view (the map view, plates, the compass, the zones, the Heights tint, saved views). " +
                  "Pictures to hand on: capture takes the Capture tool's picture or GIF of the person's view, or a box of the map from straight above, and pov_capture the picture a piece would send home, from above out to its reach, as a viewshed, or out of its own eyes; " +
                  "both go to the person's clipboard and capture folder. read_pointers says where the umpires point; wait_for waits for what happens next."),
        ("map", "add_map_sheets (pictures, 3D terrain and heightmaps) and set_map_sheet (centre, size, turn, its place in the stack, crop, relief, its height, finer ground, a new picture, turned with its pieces), export_map_sheets (the sheets as one picture file). " +
                "set_map_scale before ground work. Lay the land's big forms first with shape_ground: hill, ridge, valley and slope whole, contours through lines of given heights, level to a height in metres; " +
                "then raise and lower by metres up to a limit, smooth and flatten by stroke with a soft or hard brush, round, square or diamond; roughen, erode and lift finish a whole sheet. " +
                "It paints ground types by stroke, fill or shape, sparse at a density (wood takes trees); ground_at reads it back. What people made is built on top, in the map's age and one of its cultures (read_scenario names them): build_road (tracks, roads, chaussées, highways, railways, trenches, wire, streams and rivers, " +
                "those the age has, through waypoints, finding their way), " +
                "build_place (a settlement from hamlet to city in the plan of its culture, the fort of the map's age and culture, or one catalogue thing, which joins the settlement it stands in), build_block (a settlement's block, lined with its houses), build_details (plot boundaries and farms along a stroke); a place can be built and adorned by other cultures and give one a quarter, and culture_mix keeps such a mix under a name; " +
                "list_built and remove_built; shape_built lays a settlement, fort, building or stretch of country afresh in another culture or mix, " +
                "a settlement with other houses, walls or plan, a star fort with other bastions or outworks, and edit_built works on one thing by hand " +
                "(move, turn, size or stretch a part, keep it through a fresh lay, fill or thin houses along a stroke, put out or light its lamps along a stroke, move a wall's corner, open a gate, name the place); list_built with an id numbers its parts. " +
                "set_light (a preset, the light, the backdrop round the table, the table's shade, the map style, contours, a snow line, the lamps of what is built at night, the host's raising of what is built and the woods in 3D and how far, the wind), set_grid, " +
                "draw (lines, arrows, names on the map placed by the ground or aligned and spaced in a colour, rectangles, ellipses, curves and washed areas, on a named drawing layer if asked), " +
                "list_drawings, erase_drawings, drawing_layers."),
        ("crowds", "set_light crowds turns them on (host): the people of what is built go about their day by the clock. crowds does the rest. " +
                   "list reads the towns, the made crowds, the groups under orders with where each is and whether all are there, and how many are down, dead, held or gone. " +
                   "make makes a mob, townsfolk, soldiers, pilgrims or refugees on laid ground. " +
                   "order gives people an order, picked by id (a town, a made crowd, an order's group), by x, z and radius, or both, the nearest first up to count; " +
                   "how takes its shape, pace and act words; target is a token or another group's order to go at. " +
                   "wait_for crowd with the order's id waits until all are there or it ends, with a piece's id until crowd people come within earshot of it. " +
                   "Outcomes are stated, never rolled: fate lays down, dead, held (by target's people, who then lead them), gone, kept or up again on the nearest first over the minutes given, " +
                   "so a fight is an attack order and the fates stated. wear makes one person a piece a player wears or the table keeps; release lets it go back to their day. " +
                   "set_piece_options act sets what a rigged piece does as it stands. Orders and made crowds go into the umpires' record (read_messages kind world). "),
        ("scripts", "write_script gives a piece its own script or sets the global script; run_lua runs Lua inside one; read_script and read_console read them; check_scripts runs every script on a copy. " +
                    "Read api_reference with find before writing. What scripts made is used as a player does: list_buttons, press_button, type_in_box, pick_menu_row, press_hotkey, press_number, " +
                    "read_panels and use_panel (a script's panels), answer_dialog (the windows game.dialog opens). A kind's script for every game its pack goes to: studio_write_script."),
        ("records", "read_log reads the Chat log everyone reads; read_messages reads the saved record behind it (talk, speech, hotline, notes, dice, turns) by kind, person, piece, turn or place; " +
                    "look_back reads a kept turn or a save against the table now; list_saves, save_game, load_save; saves does the Saves window's work " +
                    "(saves, folders, renames, the trash); save_scenario_copy makes a new scenario of the table; read_book reads a book piece's pages; read_briefing; " +
                    "read_scenario says how the scenario is set; list_people who is at the table; joining gives the invitation; the password stays with the people."),
        ("settings", "This PC's settings (graphics, sound, interface size, captures…): read_settings, and set_settings only when the person asks."),
        ("app", "ui_read lists every control the person can see in the app's windows (buttons, toggles, fields, choices, lists, tabs, menu rows) with a ref; " +
                "ui_act clicks, sets, opens or closes one by its ref, presses keys and types. " +
                "Read again after each change, since refs last only until the next read. Use a dedicated tool when there is one. The title screen is only read: its work goes through status, host_game, " +
                "join_game, open_studio and import_module, and installing, removing, exporting and the trash of packs and scenarios stay the person's; so does deleting saves for good."),
        ("help", "read_guide reads the in-app guides (the umpire's, the player's, scripting, keys and mouse, for makers) and finds a word in them; read_app_log reads the app's own log, " +
                 "its warnings and errors, and the engine's log; diagnostics gives what Copy diagnostics puts together, for finding out why something went wrong."),
    };

    public static JsonObject Of(params string[] topics) =>
        new(Topics.Where(x => topics.Contains(x.Topic)).Select(x => new KeyValuePair<string, JsonNode?>(x.Topic, x.Text)));

    public static string HowTo(string? topic, string? find)
    {
        if (string.Equals(topic?.Trim(), "crowds", StringComparison.OrdinalIgnoreCase)) return Topics.First(x => x.Topic == "crowds").Text + AgentCrowdTools.Words;
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
