using System.Collections.Generic;
using KTable.Client.Agent;

namespace KTable.App;

static class AgentRecipes
{
    static string Get(IReadOnlyDictionary<string, string> a, string name) => a.TryGetValue(name, out var v) ? v.Trim() : "";

    public static readonly IReadOnlyList<AgentPrompt> All = new[]
    {
        new AgentPrompt("make-unit", "Make one unit in the piece studio from a plain description.",
            new[] { new Arg("unit", "string", "the unit, in your own words: name, colours, strength, commander, states…", true) },
            a => $"Using the kriegsspiel-table tools, make this unit in the piece studio: {Get(a, "unit")}\n" +
                 "Open the studio on a fitting pack (open_studio; make a new pack if none fits). Use make_unit with the name, colours, symbol, counters, text lines and states it needs. " +
                 "Look at the picture it returns and correct anything that does not match. Tell me what you made, in one or two lines."),
        new AgentPrompt("make-units", "Make a whole order of battle in the piece studio from a list.",
            new[] { new Arg("units", "string", "the units, one a line or as a table", true) },
            a => $"Using the kriegsspiel-table tools, make these units in the piece studio:\n{Get(a, "units")}\n" +
                 "Open the studio on a fitting pack (open_studio; make a new pack if none fits) and use make_units for all of them at once, " +
                 "with the same colours for the same side. Look at the pictures and fix what does not match. Then list what you made."),
        new AgentPrompt("give-a-button", "Give a kind of piece a button that does something.",
            new[] { new Arg("piece", "string", "which kind of piece", true), new Arg("does", "string", "what the button does", true) },
            a => $"Using the kriegsspiel-table tools, give the kind \"{Get(a, "piece")}\" a button that {Get(a, "does")}.\n" +
                 "Prefer native settings when they already do it (counters, text lines, states). Otherwise read api_reference with find self.button, write the script with studio_write_script, " +
                 "fix any error it reports, and show me the piece with studio_view_piece."),
        new AgentPrompt("draw-on-a-piece", "Draw or write something on a kind's face.",
            new[] { new Arg("piece", "string", "which kind of piece", true), new Arg("draw", "string", "what to draw or write, and where", true) },
            a => $"Using the kriegsspiel-table tools, draw this on the kind \"{Get(a, "piece")}\": {Get(a, "draw")}\n" +
                 "Read the face with studio_read_face, add or change layers, write it with studio_write_face, look at the picture and adjust until it matches."),
        new AgentPrompt("set-up-a-game", "Open a scenario and lay out the pieces.",
            new[] { new Arg("game", "string", "the game: its name, sides, which units go where", true) },
            a => $"Using the kriegsspiel-table tools, set up this game: {Get(a, "game")}\n" +
                 "Host it (host_game: an existing scenario, or a new blank one named after it). Bring in the packs it needs (add_pack), find where each goes on the map with view_map, place the pieces (place_piece) " +
                 "and set their names, states and counters (set_piece_values). Write the situation into the briefing (set_briefing). Take a snapshot at the end and tell me what is where."),
    };
}
