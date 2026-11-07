using System.Text;
using KTable.Core;

namespace KTable.Server.Scripting;

public sealed record ApiParam(string Name, string Type, string Text, bool Optional);

public sealed record ApiEntry(string Group, string Name, string Signature, string Summary, string? ModuleCall, string Kind, string? Type, string? Returns)
{
    public string Qualified => Group switch
    {
        "piece" => "self." + Name,
        "global" or "event" or "global event" => Name,
        _ => Group + "." + Name,
    };

    public string Display => Group switch
    {
        "piece" => "self." + Signature,
        "global" or "event" or "global event" => Signature,
        _ => Group + "." + Signature,
    };

    public bool IsFunction => Kind is "function" or "event";

    public bool TakesSpec
    {
        get
        {
            int brace = Signature.IndexOf('{'), paren = Signature.IndexOf('(');
            return brace >= 0 && (paren < 0 || brace < paren);
        }
    }

    public string Heading => "`" + Qualified + "`" + (Group == "global event" ? " (global script)" : "");

    public List<ApiParam> Params { get; } = new();
    public string? ReturnText { get; set; }
    public string? Example { get; set; }
    public List<string> Notes { get; } = new();
    public List<string> See { get; } = new();
}

public sealed record ApiSection(string Title, string Intro, IReadOnlyList<ApiEntry> Entries);

public static partial class ApiDoc
{
    static readonly List<ApiEntry> _scratch = new();

    static ApiEntry Last => _scratch[^1];

    static void F(string group, string name, string type, string summary, string? moduleCall = null) =>
        _scratch.Add(new ApiEntry(group, name, name, summary, moduleCall, "field", type, null));

    static void M(string group, string signature, string summary, string? moduleCall = null, string? returns = null)
    {
        var name = signature.Split('(', '{')[0].Trim();
        _scratch.Add(new ApiEntry(group, name, signature, summary, moduleCall, "function", null, returns));
    }

    static void E(string group, string signature, string summary, string? moduleCall = null)
    {
        var name = signature.Split('(')[0].Trim();
        _scratch.Add(new ApiEntry(group, name, signature, summary, moduleCall, "event", null, null));
    }

    static void P(string name, string type, string text) => Last.Params.Add(new ApiParam(name, type, text, false));
    static void O(string name, string type, string text) => Last.Params.Add(new ApiParam(name, type, text, true));
    static void R(string text) => Last.ReturnText = text;
    static void X(string example) => Last.Example = example.Replace("\r\n", "\n").Trim('\n');
    static void Note(string text) => Last.Notes.Add(text);
    static void See(params string[] names) => Last.See.AddRange(names);

    static ApiSection Section(string title, string intro, Action fill)
    {
        _scratch.Clear();
        fill();
        return new ApiSection(title, intro, _scratch.ToList());
    }

    public static readonly IReadOnlyList<ApiSection> Sections = Build();

    static List<ApiSection> Build() => new()
    {
        Globals(),
        PieceFields(),
        PieceChanges(),
        PieceWidgets(),
        PieceScripts(),
        Board(),
        Game(),
        Ui(),
        Wait(),
        JsonCalls(),
        PieceEvents(),
        GlobalEvents(),
        LuaLibrary(),
    };

    static ApiSection Globals() => Section("The globals", "Every script sees these names. A piece's script has `self`; the global script (Scenario properties → Script) has none.", () =>
    {
        F("global", "self", "Piece", "The piece this script runs on. nil in the global script.", "self");
        Note("A script on a piece kind runs once for every piece of that kind on the table, and each run has its own `self` and its own globals.");
        See("board.find", "board.pieces");

        M("global", "print(...)", "Writes its arguments to the script console, separated by tabs.", "print");
        O("...", "any", "The values to write; each is written as `tostring` would.");
        X("""print("strength", self.counters.Strength, "at", self.pos.x, self.pos.z)""");
        Note("The console shows the line with the piece and the script it came from. Observers never see it.");
        See("log");

        M("global", "log(value, label)", "Writes a value to the script console, a table written out a few levels deep; the label goes in front.", "log");
        P("value", "any", "The value to write. A table is written out with its keys, a few levels deep.");
        O("label", "string", "Written in front of the value, followed by a colon.");
        X("""log(self.pos, "where")   -- where: { x = 12.5, z = -3 }""");
        See("print");

        F("global", "board", "table", "The umpire board: find, list and spawn pieces, measure, read the zones.");
        See("board.find", "board.pieces", "board.spawn");
        F("global", "game", "table", "The game: the clock, the umpires, the Log, dice, hotkeys, the table's menu, dialogs.");
        See("game.say", "game.roll", "game.hotkey");
        F("global", "ui", "table", "The screen panels every umpire sees, written in XML.", "UI");
        See("ui.set");
        F("global", "wait", "table", "Timers: seconds, frames, a condition.", "Wait");
        See("wait.seconds");
        F("global", "json", "table", "JSON in and out, numbers written the same on every PC.", "JSON");
        See("json.encode", "json.decode");
        F("global", "compat", "table", "The compatibility library imported scripts call; written over this API (see the guide).");
        Note("Write new scripts against this API, not against `compat`: it exists so that translated module scripts keep their shape.");
    });

    static ApiSection PieceFields() => Section("A piece: fields", "Read from `self` or from any piece `board.find` or `board.pieces` hands back. Fields are read-only: assigning to one is an error. The `set_` calls change them.", () =>
    {
        F("piece", "id", "string", "Our id for the piece (p-000123). Never changes.");
        See("board.find");
        F("piece", "guid", "string", "The GUID its module gave it; a piece made here answers with its id. board.find takes either.", "getGUID");
        Note("A piece a script spawns gets a fresh six-digit GUID unless the spawn named a free one.");
        F("piece", "def", "string", "The id of its piece definition (its kind).");
        See("self.set_def", "board.pieces");
        F("piece", "pack", "string", "The id of the pack its kind comes from.");
        F("piece", "name", "string?", "The label shown under the piece, or nil.", "getName");
        See("self.set_name");
        F("piece", "note", "string?", "Its description, shown on hover; umpires only. nil when empty.", "getDescription");
        See("self.set_note");
        F("piece", "state", "number", "Its state, from 1.", "getStateId");
        See("self.set_state", "self.states", "on_state");
        F("piece", "states", "string[]", "The names of its kind's states, in order.", "getStates");
        X("""
            for i, name in ipairs(self.states) do
              print(i, name, i == self.state and "(now)" or "")
            end
            """);
        F("piece", "faces", "Face[]", "A die's sides in order: { value =, up = { x, y, z }, rotation = { x, y, z } }; empty for every other piece.", "getRotationValues");
        F("piece", "face", "number?", "The side of a die showing, from 1; nil for every other piece.", "getValue");
        F("piece", "value", "number|string|nil", "The value of the side of a die showing; nil for every other piece.", "getRotationValue");
        F("piece", "picked_up", "table?", "Where an umpire last picked the piece up: { x =, y =, z =, rot = }; nil until then.", "pick_up_position");
        F("piece", "face_picture", "string?", "A picture shown on the piece's face in place of its kind's, set by set_face_picture; nil for none.");
        F("piece", "page_offset", "number", "For a book, what its page numbers start from, as the module set it; 0 for every other piece.", "Book.page_offset");
        F("piece", "effect_on", "boolean", "For an effect, whether it is running.");
        F("piece", "kind_name", "string", "The name of its kind.");
        F("piece", "lettering", "table?", "Text drawn on the piece in place of its picture, as 3D text from a module is: { text =, size =, color = }; nil for most pieces.", "TextTool.getValue");
        F("piece", "worth", "number", "What the piece counts for when pieces are added up; 0 for most.", "value");
        F("piece", "worth_groups", "number", "Which groups it adds up with, as bits; 0 adds up with any piece of worth.", "value_flags");
        F("piece", "pos", "Point", "Where it stands, in map units: { x =, z = }.", "getPosition");
        Note("Each read gives a new table; changing it moves nothing. Use `set_pos` or `move`.");
        See("self.set_pos", "self.move", "board.distance");
        F("piece", "rot", "number", "Its heading in degrees.", "getRotation");
        See("self.set_rot");
        F("piece", "y", "number", "How high it sits above the table (stacked pieces).");
        F("piece", "scale", "number", "Its size multiplier on the table.", "getScale");
        See("self.set_scale", "self.size");
        F("piece", "size", "Size", "Its size on the map at its state and scale: { w =, h = }.", "getBounds");
        Note("A state with a size of its own gives that size; otherwise the kind's. Both are multiplied by `scale`.");
        F("piece", "picture_size", "Size?", "Its picture's width and height in pixels at its state, or nil without a picture.");
        F("piece", "local_unit", "Size?", "For a model that came from a module, what one unit of its script's local frame is in piece units: { x =, z = }; nil for every other piece.");
        F("piece", "shape", "string", "Its outline at its state: \"box\", \"rounded\", \"circle\", \"hex\", or \"alpha\" for a token cut along its picture.");
        F("piece", "face_down", "boolean", "Turned over.", "is_face_down");
        F("piece", "interactable", "boolean", "Whether hands can take it (set_interactable).", "interactable");
        F("piece", "held_by", "Who", "The umpire carrying it now, or nil.", "held_by_color");
        F("piece", "source", "table", "For a kind converted from a module: { type =, links = { image =, mesh =, … }, top =, tag = } — the module object's type, its picture and model links, its top surface in its own units and the kind of object it was (Tile, Figurine, Board, Bag…); nil otherwise.", "name");
        F("piece", "highlight", "string", "The colour it is ringed in, or nil (set_highlight).", "highlightOn");
        F("piece", "holds", "boolean", "Whether it is a holder that other pieces can be stowed in: a piece holding some, one made a holder, or a kind tagged bag.", "getQuantity");
        See("self.flip", "on_flip");
        F("piece", "locked", "boolean", "Pinned to the table: not dragged, turned or resized by hand.", "getLock");
        See("self.set_locked");
        F("piece", "sticky", "boolean", "Carries the pieces resting on it when it is moved; attached pieces go with it either way.", "sticky");
        See("self.set_sticky");
        F("piece", "box_selected", "boolean", "Taken by a selection box dragged over it.", "drag_selectable");
        See("self.set_box_selected");
        F("piece", "measures_moves", "boolean", "Shows how far it is moved while it is held.", "measure_movement");
        See("self.set_measures_moves");
        F("piece", "tint", "string?", "Its colour as \"#RRGGBB\", or nil.", "getColorTint");
        See("self.set_tint");
        F("piece", "counters", "table", "Its kind's counters, name → value.");
        Note("Every counter the kind defines is there, at its default until something sets it.");
        X("""
            local s = self.counters.Strength
            if s and s <= 0 then self.set_state("Routed") end
            """);
        See("self.set_counter");
        F("piece", "text", "table", "Its text lines that hold a value, name → text.");
        See("self.set_text");
        F("piece", "attached", "Piece[]", "The pieces riding on it.", "getAttachments");
        See("self.attach", "self.detach");
        F("piece", "tags", "string[]", "Its own tags, or its kind's when it has none of its own.", "getTags");
        See("self.set_tags", "self.add_tag", "board.pieces");
        F("piece", "memo", "string", "A string the scripts keep on the piece; saved with the game.", "memo");
        Note("The memo travels with the piece: a copy made from its data keeps it. For a script's own state, `on_save` is the usual place.");
        See("self.set_memo");
        F("piece", "script_state", "string", "What its on_save last returned.", "script_state");
        See("on_save", "self.set_script_state");
        F("piece", "script", "string?", "The script it runs (its own, or its kind's), or nil.", "getLuaScript");
        See("self.set_script");
        F("piece", "ui", "table", "Its own panels, drawn on its face: the same calls as the global ui.", "UI");
        Note("The root element's `width` and `height` are thousandths of a module's local unit; see the guide, Panels on a piece.");
        See("ui.set");
        F("piece", "lines", "Line[]", "The lines set_lines drew round it.", "getVectorLines");
        See("self.set_lines");
        F("piece", "decals", "Decal[]", "The pictures on its faces: the ones its look carries, or those set_decals put there.", "getDecals");
        See("self.set_decals", "self.state");
        F("piece", "exists", "boolean", "False once the piece is gone (its other fields are nil then).", "isDestroyed");
        Note("A handle kept in a variable outlives the piece. Check `exists` before using one kept across turns; a `set_` call on a piece that is gone is an error.");
    });

    static ApiSection PieceChanges() => Section("A piece: changes", "Each call is a change every umpire sees, taken back with Ctrl+Z by the umpire whose action ran the script. Everything one reaction changes is one step of undo.", () =>
    {
        PieceLooks();
        PieceSides();
        PieceData();
    });

    static void PieceSides()
    {
    M("piece", "set_face(n)", "Turns a die to show a side, from 1; false when it has no such side.", "setValue", "boolean");
    P("n", "number", "The side, from 1, in the order of self.faces.");
    See("self.face", "self.set_value");

    M("piece", "set_value(value)", "Turns a die to the first side with this value; false when no side has it.", "setRotationValue", "boolean");
    P("value", "number|string", "The value to show.");
    X("self.set_value(6)");
    See("self.value", "self.set_face");

    M("piece", "set_faces(list)", "Gives this die sides of its own; nil gives it back its kind's.", "setRotationValues", "boolean");
    P("list", "table?", "{ value =, up = { x, y, z } } or { value =, rotation = { x, y, z } } for each side.");
    See("self.faces");

    M("piece", "set_face_picture(link)", "Shows a picture on the piece's face in place of its kind's: a web link is fetched first; nil puts the kind's back.", "setCustomObject");
    P("link", "string?", "A web link to a picture, or a picture the table already holds.");
    See("self.face_picture");

    M("piece", "set_lettering(changes)", "Changes the text drawn on the piece: any of text, size and color; nil takes the lettering away.", "TextTool.setValue");
    P("changes", "table?", "{ text =, size =, color = }, each optional.");
    See("self.lettering");

    M("piece", "set_effect_on(on)", "Starts or stops an effect.", "AssetBundle.playLoopingEffect");
    P("on", "boolean", "true runs it; false stops it.");
    See("self.effect_on");

    M("piece", "set_worth(worth, groups)", "Sets what the piece counts for, and optionally the groups it adds up with.", "value");
    P("worth", "number", "A whole number.");
    O("groups", "number", "Groups as bits; nil keeps them.");
    See("self.worth");

    M("piece", "roll()", "Rolls a die, or turns a piece of several states to one at random; false for anything else.", "randomize", "boolean");
    See("game.roll", "self.set_face");
    }

    static void PieceLooks()
    {
    M("piece", "set_name(text)", "Sets the label under the piece; nil takes it away.", "setName");
    O("text", "string?", "The label. nil, or no argument, takes it away.");
    X("""self.set_name("2nd Bn, 95th Rifles")""");
    See("self.name", "self.set_note");

    M("piece", "set_note(text)", "Sets its description; nil takes it away.", "setDescription");
    O("text", "string?", "The description, shown to umpires on hover. nil takes it away.");
    See("self.note", "self.set_name");

    M("piece", "set_state(n_or_name)", "Changes its state: a number from 1, or a state's name. What the scripts drew belongs to the state it was drawn on.", "setState");
    P("n_or_name", "number|string", "A state number from 1, or a state's name (case does not matter).");
    X("""
        self.set_state(2)
        self.set_state("Shattered")
        """);
    Note("A name or number the kind does not have is an error.");
    Note("Widgets, menu rows and lines a script draws belong to the state they were drawn in; see the guide, States.");
    See("self.state", "self.states", "on_state");

    M("piece", "set_pos(x, z)", "Moves it, in map units; its heading stays. A place { x, z } or another piece works in place of x and z.", "setPosition");
    O("x", "number", "The new x in map units; nil keeps the current one.");
    O("z", "number", "The new z in map units; nil keeps the current one.");
    X("""
        local p = self.pos
        self.set_pos(p.x + 10, p.z)
        self.set_pos({ x = 120, z = 48 })
        """);
    See("self.move", "self.pos");

    M("piece", "set_rot(deg)", "Turns it to a heading in degrees.", "setRotation");
    P("deg", "number", "The heading in degrees.");
    See("self.move", "self.rot", "on_rotate");

    M("piece", "move(x, z, rot)", "Moves and turns it in one change. A place { x, z } or another piece works in place of x and z: move(place, rot).");
    O("x", "number", "The new x in map units; nil keeps it.");
    O("z", "number", "The new z in map units; nil keeps it.");
    O("rot", "number", "The new heading in degrees; nil keeps it.");
    X("""
        self.move(120, 48, 90)
        self.move({ x = 120, z = 48 }, 90)
        """);
    See("self.set_pos", "self.set_rot");

    M("piece", "set_scale(k)", "Sets its size multiplier.", "setScale");
    P("k", "number", "The multiplier; 1 is the kind's own size.");
    Note("Widgets keep their place and their share of the face: they are drawn in piece units.");
    See("self.scale", "self.size");

    M("piece", "flip(face_down)", "Turns it over; with no argument, to the other side.", "flip");
    O("face_down", "boolean", "true for face down, false for face up. Left out, the piece turns to its other side.");
    See("self.face_down", "on_flip");

    M("piece", "set_tint(color)", "Tints its sides: \"#RRGGBB\", a name, or { r, g, b }. nil goes back to its kind's.", "setColorTint");
    O("color", "Color?", "The tint. nil goes back to the kind's.");
    X("""self.set_tint("#8a1c1c")""");
    See("self.tint");

    M("piece", "set_locked(on)", "Pins it to the table, or lets it go.", "setLock");
    O("on", "boolean", "true, or no argument, pins it; false lets it go.");
    Note("A locked piece can still be picked out, read and deleted, and an umpire can unlock it from its right-click menu.");
    See("self.locked");

    M("piece", "set_box_selected(on)", "Whether a selection box dragged over it takes it.", "drag_selectable");
    O("on", "boolean", "true, or no argument, lets a box take it; false leaves it out.");
    See("self.box_selected");

    M("piece", "set_measures_moves(on)", "Whether it shows how far it is moved while it is held.", "measure_movement");
    O("on", "boolean", "true, or no argument, shows the distance; false hides it.");
    See("self.measures_moves");

    M("piece", "set_interactable(on)", "Whether hands can take it: off, it cannot be picked up, dragged, selected or opened, though its buttons still press.", "interactable");
    O("on", "boolean", "true, or no argument, lets hands take it; false keeps them off.");

    M("piece", "set_highlight(color)", "Rings it in a colour on every screen; nil takes the ring away.", "highlightOn");
    O("color", "Color", "The ring's colour, or nil.");

    M("piece", "set_sticky(on)", "Whether it carries the pieces resting on it when it is moved.", "sticky");
    O("on", "boolean", "true, or no argument, carries them; false leaves them where they lie.");
    See("self.sticky");
    }

    static void PieceCarrying()
    {
        M("piece", "attach(piece)", "Puts another piece on this one, to ride with it.", "addAttachment");
        P("piece", "Piece|string", "The piece to carry, or its id.");
        See("self.detach", "self.attached");

        M("piece", "detach()", "Takes this piece off the one it rides on.", "removeAttachment");
        See("self.attach");

        F("piece", "stowed", "list", "The pieces it holds off the table, first in first: { id =, name =, guid =, def =, held = }, held being how many that piece holds in turn.", "getObjects");
        M("piece", "stow(piece)", "Takes another piece off the table into this one, with whatever rides on it; a piece this one rides on is refused.", "putObject");
        M("piece", "unstow(which, place, rot)", "Puts a held piece back on the table and returns it: which is its id, guid, its number in stowed, or nothing for the next by the holder's take order (first, last or random); place is { x =, z = } or a piece, else beside this one; rot turns it.", "takeObject");
        See("self.stowed", "self.stow", "self.unstow");
    }

    static void PieceData()
    {
    M("piece", "set_counter(name, value)", "Sets one of its kind's counters (a whole number, kept to the counter's range).");
    P("name", "string", "The counter's name, as the piece studio shows it.");
    P("value", "number", "Rounded to a whole number and kept to the counter's range.");
    X("""self.set_counter("Strength", self.counters.Strength - 1)""");
    See("self.counters");

    M("piece", "set_text(name, text)", "Sets one of its kind's text lines.");
    P("name", "string", "The text line's name, as the piece studio shows it.");
    P("text", "string", "The new text.");
    See("self.text");

    M("piece", "set_tags(list)", "Gives it its own tags; nil goes back to its kind's.", "setTags");
    O("list", "string[]?", "The tags. Empty strings are dropped. nil goes back to the kind's tags.");
    See("self.tags", "self.add_tag", "self.remove_tag");

    M("piece", "add_tag(tag)", "Adds a tag to its own.", "addTag");
    P("tag", "string", "The tag. Nothing changes when the piece has it already.");
    Note("A piece that wore its kind's tags gets its own list: the kind's, plus this one.");
    See("self.remove_tag", "self.tags");

    M("piece", "remove_tag(tag)", "Takes a tag away.", "removeTag");
    P("tag", "string", "The tag. Nothing changes when the piece does not have it.");
    See("self.add_tag", "self.tags");

    M("piece", "set_memo(text)", "Sets its memo.", "setMemo");
    O("text", "string?", "The memo; nil empties it.");
    See("self.memo");

    M("piece", "set_script_state(text)", "Sets its saved script state outright (on_save's answer is the usual way).", "script_state");
    O("text", "string?", "The state; nil empties it.");
    See("on_save", "self.script_state");

    M("piece", "set_picture(def)", "Wears another kind's picture; it keeps its own size, states, counters and script. nil puts its own back.", "setCustomObject");
    O("def", "string?", "The id of a piece definition in this game's packs. nil puts the piece's own picture back.");
    Note("A definition that is not in this game's packs is an error.");
    See("self.set_def");

    M("piece", "set_def(def)", "Becomes another kind of piece altogether.");
    P("def", "string", "The id of a piece definition in this game's packs.");
    Note("The piece takes the new kind's picture, states, counters and script. A definition that is not in this game's packs is an error.");
    See("self.set_picture", "self.def");

    M("piece", "set_script(text)", "Gives it a script of its own (nil: back to its kind's). Only from a click or menu handler, or on a piece the same call spawned.", "setLuaScript");
    O("text", "string?", "The Lua source. nil goes back to the kind's script.");
    Note("A script that reaches for the network, the files or code from text is refused with an error, as on import.");
    Note("The new script starts from `on_load` at once.");
    See("self.script", "self.reload");

    M("piece", "reload()", "Starts its script again from on_load.", "reload");
    See("on_load");

    PieceCarrying();

    M("piece", "delete()", "Takes the piece off the table (on_destroy runs first).", "destruct");
    Note("The piece's own `on_destroy` and the global script's `on_destroy(piece)` run before it goes. Its handle answers `exists = false` afterwards.");
    See("on_destroy", "self.exists");

    M("piece", "set_lines(lines)", "Draws lines round the piece that move and turn with it: { { points = { {x=, z=}, … }, color =, width =, loop = }, … } in piece units; width in map units.", "setVectorLines");
    P("lines", "Line[]", "The lines. Each needs two points or more; points are `{ x =, z = }` or `{ x, z }` in piece units. An empty table takes every line away.");
    X("""
        local ring = {}
        for i = 0, 35 do
          local a = math.rad(i * 10)
          ring[#ring + 1] = { x = math.cos(a) * 3, z = math.sin(a) * 3 }
        end
        self.set_lines{ { points = ring, color = "#d8b766", width = 0.4, loop = true } }
        """);
    Note("`color` defaults to white and `width` to 0.1 map units; a colour with alpha 0 draws nothing.");
    See("self.lines", "self.to_local");

    M("piece", "set_decals(decals)", "Puts pictures on the piece's faces that move, turn and flip with it: { { picture =, x =, z =, w =, h =, rot =, face =, name = }, … } in piece units.", "setDecals");
    P("decals", "Decal[]", "The decals. `picture` is a picture in this game's packs (a module's decal images come along when it is converted) or one from game.fetch_picture or a picture dialog; `face` is \"front\" or \"back\"; `w` and `h` default to 0.2, `rot` is degrees. An empty table takes every decal away.");
    X("""
        local arms = self.decals[1]
        self.set_decals{ { picture = arms.picture, x = 0.35, z = -0.3, w = 0.2, h = 0.2, name = "Arms" } }
        """);
    Note("At most 64 on a piece. A picture not in this game's packs is an error. On a kind whose looks carry decals of their own, what this puts on a piece belongs to the look it was in and the other looks keep theirs, and the copy a script sets while it loads is left out, the look already carrying it.");
    See("self.decals", "self.state");

    M("piece", "to_world(x, z)", "A point in piece units (centre 0, x right, z down the picture) as a map point { x =, z = }.", "positionToWorld", "Point");
    P("x", "number", "Piece units to the right of the centre.");
    P("z", "number", "Piece units down the picture from the centre.");
    X("""
        local front = self.to_world(0, -self.size.h / self.scale)
        board.spawn{ def = "std.effects.signal-smoke", x = front.x, z = front.z }
        """);
    See("self.to_local");

    M("piece", "to_local(x, z)", "A map point as piece units.", "positionToLocal", "Point");
    P("x", "number", "The map point's x.");
    P("z", "number", "The map point's z.");
    See("self.to_world");
    }

    static ApiSection PieceWidgets() => Section("A piece: widgets and its menu", "Widgets are drawn on a face in piece units: map units measured on the piece's kind before the table scales it, centre (0, 0), x right, z down the picture. A kind 1.2 wide and 0.6 high runs from x = -0.6 to 0.6 and z = -0.3 to 0.3. Colours are \"#RRGGBB[AA]\", a name or { r, g, b, a }.", () =>
    {
        M("piece", "button{ face, x, z, w, h, rot, label, font, color, text_color, hover_color, press_color, tooltip, on_click, kind }", "Draws a button and returns its index from 1. With no w and h it is a bare label that takes no clicks. on_click names a function called as fn(who, alt, index, piece).", "createButton", "number");
        O("face", "\"front\"|\"back\"", "The face it is drawn on. Default \"front\".");
        O("x", "number", "Centre, piece units right of the piece's centre. Default 0.");
        O("z", "number", "Centre, piece units down the picture. Default 0.");
        O("w", "number", "Width in piece units. Leave out `w` and `h` for a bare label.");
        O("h", "number", "Height in piece units. The box is exactly what can be pressed.");
        O("rot", "number", "Turn on the face, in degrees. Default 0.");
        O("label", "string", "The text.");
        O("font", "number", "Letter size in piece units; a longer label runs past its button. Default 0.05.");
        O("color", "Color", "The box's colour.");
        O("text_color", "Color", "The text's colour.");
        O("tooltip", "string", "Shown when the pointer rests on it.");
        O("hover_color", "Color", "The box's colour under the pointer. Default: each of red, green and blue 0.2 nearer the middle grey.");
        O("press_color", "Color", "The box's colour while pressed. Default: each 0.4 nearer the middle grey.");
        O("on_click", "string", "The handler's name: called as fn(who, alt, index, piece); `alt` is true for a right-click.");
        O("kind", "\"button\"|\"input\"|\"label\"", "\"label\" shows it but takes no clicks or typing: locked text. A module's button or input turned face down above the piece comes across this way. `edit` can change it.");
        R("The widget's index, from 1, for `edit` and `remove_widget`.");
        X("""
            function on_load()
              self.button{ x = 0, z = 0.3, w = 0.3, h = 0.12, label = "−1", font = 0.08, on_click = "hit" }
            end

            function hit(who, alt)
              local n = self.counters.Strength + (alt and 1 or -1)
              self.set_counter("Strength", n)
              game.say(who.name .. ": " .. (self.name or self.id) .. " at " .. n)
            end
            """);
        Note("The handler is looked up in this script first, then in the global script. `\"global:fn\"` names the global script's; `\"<id or GUID>/fn\"` another piece's.");
        See("self.input", "self.edit", "self.clear_widgets");

        M("piece", "input{ face, x, z, w, h, rot, value, placeholder, font, color, text_color, align, lines, tooltip, on_change, validation, tab, kind }", "Draws an input box and returns its index. on_change names a function called as fn(who, value, index, piece, typing) as the umpire types and when they leave the box.", "createInput", "number");
        O("face", "\"front\"|\"back\"", "The face it is drawn on. Default \"front\".");
        O("x", "number", "Centre, piece units. Default 0.");
        O("z", "number", "Centre, piece units. Default 0.");
        O("w", "number", "Width in piece units.");
        O("h", "number", "Height in piece units.");
        O("rot", "number", "Turn on the face, in degrees.");
        O("value", "string", "The text in the box.");
        O("placeholder", "string", "Shown greyed while the box is empty.");
        O("font", "number", "Letter size in piece units; the box shows as much of the text as fits. Default 0.05.");
        O("color", "Color", "The box's colour.");
        O("text_color", "Color", "The text's colour.");
        O("align", "\"left\"|\"center\"|\"right\"", "Default \"center\".");
        O("lines", "number", "Lines of text the box holds. Default 1.");
        O("tooltip", "string", "Shown when the pointer rests on it.");
        O("on_change", "string", "The handler's name: called as fn(who, value, index, piece, typing) each time the text changes while the umpire types (typing true) and once more when they leave the box (typing false); text it returns replaces what was typed.");
        O("validation", "\"integer\"|\"float\"|\"alphanumeric\"|\"username\"|\"name\"", "Keys the box takes: digits and a leading minus; also one point; letters and digits; lower-case letters and digits; or a name, capitalised by word. Default any.");
        O("tab", "\"next\"|\"spaces\"|\"none\"", "What Tab does: go on to the piece's next input (the default), type four spaces, or nothing.");
        O("kind", "\"button\"|\"input\"|\"label\"", "\"label\" shows its value but takes no typing: locked text.");
        R("The widget's index, from 1.");
        X("""
            self.input{ face = "back", z = -0.2, w = 0.8, h = 0.12, value = self.name or "",
                        placeholder = "Regiment", on_change = "renamed" }

            function renamed(who, value) self.set_name(value) end
            """);
        Note("The typed value stays on the widget, so it is there for everyone and in the save, whether or not a handler keeps it.");
        See("self.button", "self.edit");

        M("piece", "edit(index, fields)", "Changes a widget: any of the fields button and input take.", "editButton");
        P("index", "number", "The widget's index, from 1.");
        P("fields", "table", "The fields to change, as `button` or `input` takes them; the others stay.");
        X("""self.edit(1, { label = tostring(n), color = n > 0 and "#2f5d2f" or "#7a1f1f" })""");
        Note("An index with no widget is an error.");
        See("self.button", "self.widgets");

        M("piece", "remove_widget(index)", "Takes one widget away (the later ones move up).", "removeButton");
        P("index", "number", "The widget's index, from 1. An index with no widget does nothing.");
        Note("The widgets after it move up one: index 3 becomes 2. Redraw from scratch with `clear_widgets` when the order matters.");
        See("self.clear_widgets");

        M("piece", "clear_widgets()", "Takes every widget away.", "clearButtons");
        See("self.button", "self.input");

        M("piece", "widgets()", "The widgets as tables, in order.", "getButtons", "table");
        R("A list of tables with `kind` (\"button\" or \"input\") and every field `button` and `input` take.");
        See("self.edit");

        M("piece", "menu_add(label, fn_name)", "Adds a row to the piece's right-click menu; the function is called as fn(who, piece).", "addContextMenuItem");
        P("label", "string", "The row's text.");
        P("fn_name", "string", "The handler's name, looked up as for `on_click`.");
        X("""
            self.menu_add("Rally", "rally")
            function rally(who) self.set_state(1) end
            """);
        See("self.menu_clear", "game.menu_add");

        M("piece", "menu_clear()", "Takes the script's rows out of the piece's menu.", "clearContextMenu");
        See("self.menu_add");
    });

    static ApiSection PieceScripts() => Section("A piece: other scripts", "One script talks to another through these. Calls inside calls go 50 deep at most.", () =>
    {
        M("piece", "call(fn_name, ...)", "Runs a function of that piece's script and returns what it returns.", "call", "any");
        P("fn_name", "string", "A global function of that piece's script.");
        O("...", "any", "Handed to the function as they are.");
        R("What the function returns; nil when the piece has no script or the script no such function.");
        X("""
            local hq = board.find("c3a91f")
            if hq then hq.call("report", self.name, self.counters.Strength) end
            """);
        See("game.call", "self.get_var");

        M("piece", "get_var(name)", "Reads a global variable of that piece's script.", "getVar", "any");
        P("name", "string", "The variable's name.");
        R("Its value, or nil.");
        See("self.set_var", "self.call");

        M("piece", "set_var(name, value)", "Writes a global variable of that piece's script.", "setVar");
        P("name", "string", "The variable's name.");
        P("value", "any", "The new value.");
        See("self.get_var");
    });

    static ApiSection Board() => Section("board", "The umpire board: every piece a script can reach is on it.", () =>
    {
        M("board", "find(id_or_guid)", "The piece with that id or module GUID, or nil.", "getObjectFromGUID", "Piece");
        P("id_or_guid", "string", "Our id (p-000123) or the module GUID the piece came with.");
        R("The piece, or nil when there is none.");
        X("""
            local gun = board.find("a1b2c3")
            if gun then gun.set_state("Limbered") end
            """);
        See("board.pieces", "self.id", "self.guid");

        M("board", "pieces(filter)", "Every piece, or those that match { def =, tag =, name = }.", "getObjects", "Piece[]");
        O("filter.def", "string", "Only pieces of this kind.");
        O("filter.tag", "string", "Only pieces with this tag (their own, or their kind's).");
        O("filter.name", "string", "Only pieces with this label; case does not matter.");
        R("A list; empty when nothing matches. Every field given must match.");
        X("""
            for _, p in ipairs(board.pieces{ tag = "French" }) do
              if p.counters.Strength == 0 then p.set_state("Routed") end
            end
            """);
        See("board.find", "self.tags");

        M("board", "spawn{ def, x, z, rot, label, state, scale, y, script, tags, memo, note, script_state, guid }", "Puts a new piece of that kind on the board and returns it; its script starts at once.", "spawnObject", "Piece");
        P("def", "string", "The id of a piece definition in this game's packs.");
        O("x", "number", "Map units. Default 0.");
        O("z", "number", "Map units. Default 0.");
        O("rot", "number", "Heading in degrees. Default 0.");
        O("label", "string", "The label under it.");
        O("state", "number", "The state to start in, from 1.");
        O("scale", "number", "Size multiplier. Default 1.");
        O("y", "number", "Height above the table.");
        O("script", "string", "A script of its own, in place of its kind's.");
        O("tags", "string[]", "Tags of its own.");
        O("memo", "string", "Its memo.");
        O("note", "string", "Its description.");
        O("script_state", "string", "Handed to its `on_load`.");
        O("guid", "string", "A module GUID to give it; one already taken is replaced by a fresh one.");
        R("The new piece.");
        X("""
            local p = self.pos
            local smoke = board.spawn{ def = "std.effects.signal-smoke", x = p.x, z = p.z - 6, label = "Smoke" }
            wait.seconds(30, function() if smoke.exists then smoke.delete() end end)
            """);
        Note("A missing `def` is an error. The global script's `on_spawn` runs for the new piece.");
        See("self.delete", "on_spawn");

        M("board", "distance(a, b)", "Metres between two pieces or two { x =, z = } points by the map's scale (map units when it has none).", null, "number");
        P("a", "Piece|Point", "A piece, or a map point.");
        P("b", "Piece|Point", "A piece, or a map point.");
        R("The distance in metres; in map units while the map has no scale; nil when either is neither a piece nor a point.");
        X("""
            local d = board.distance(self, board.find("a1b2c3"))
            if d and d < 300 then game.say("In musket range: " .. math.floor(d) .. " m") end
            """);
        See("game.calibration");

        M("board", "zones()", "The umpire zones: { id =, name =, owner =, contains = function(piece_or_point) }.", "getZones", "table");

        M("board", "draw(stroke)", "Draws a line on the umpire's drawing board and returns its id: { points = { {x=,z=}, ... }, color =, width =, loop = }.", "Global.setVectorLines", "string");
        M("board", "erase(id)", "Takes a drawn line off the board.", null);
        M("board", "strokes()", "The lines on the umpire's drawing board: { id =, points =, color =, width = }.", "Global.getVectorLines", "table[]");
        R("A list of zones. `contains(piece_or_point)` answers true when the piece or point lies inside.");
        X("""
            for _, z in ipairs(board.zones()) do
              if z.contains(self) then print("in", z.name) end
            end
            """);
    });

    static ApiSection Game() => Section("game", "The game around the table.", () =>
    {
        GameRound();
        GameUmpires();
    });

    static void GameRound()
    {
    F("game", "turn", "number", "The turn number.");
    See("on_turn", "game.advance_clock");
    F("game", "time", "string", "The game time, as \"1812-07-22T06:00\".");
    F("game", "calibration", "number?", "Map units per metre, or nil before the map has a scale.");
    See("board.distance");

    M("game", "advance_clock()", "Ends the turn, as Next turn does.");
    Note("Every script's `on_turn` runs with the new turn number.");
    See("game.turn", "on_turn");

    M("game", "umpires()", "The umpires at the table: { { id =, name =, role = \"host\"|\"lead\"|\"umpire\" }, … }.", "getPlayers", "Who[]");
    R("A list of the umpires present, as `Who` tables.");
    X("""
        for _, u in ipairs(game.umpires()) do
          if u.role == "host" then game.say("Turn over to you", { to = u }) end
        end
        """);

    M("game", "say(text, { to, color })", "A line in the Log: to nil = everyone at the table, an umpire's table = that umpire, \"u:\" = the umpires.", "broadcastToAll");
    P("text", "string", "The line.");
    O("opts.to", "Who|string", "An umpire (a `Who`) for a line only they see; \"u:\" for the umpires only; nil for everyone at the table.");
    O("opts.color", "Color", "The line's colour.");
    X("""
        game.say("The bridge is down.", { color = "#e07050" })
        game.say("Only you see this.", { to = who })
        """);
    See("on_chat");

    M("game", "roll(sides, count, label)", "Dice rolled by the server, in the Log; the results as a list.", null, "number[]");
    O("sides", "number", "Sides of each die. Default 6.");
    O("count", "number", "How many dice. Default 1.");
    O("label", "string", "Shown with the roll in the Log.");
    R("The results, one number per die.");
    X("""
        local r = game.roll(6, 2, "Morale: " .. self.name)
        if r[1] + r[2] < 7 then self.set_state("Shaken") end
        """);

    M("game", "hotkey(label, fn, on_up)", "A key each umpire binds in Settings → Keys; fn(who, piece_under_pointer, { x =, z = }, released). on_up: called on the release too.", "addHotkey", "number");
    P("label", "string", "Shown in Settings → Keys. A label that ends in a digit starts on that number key.");
    P("fn", "function", "Called as fn(who, piece, point, released): the piece under the pointer or nil, the table point under it.");
    O("on_up", "boolean", "true: fn is called on the key's release too, with `released` true.");
    R("The hotkey's number.");
    X("""
        game.hotkey("Roll for the piece under the pointer", function(who, piece)
          if piece then game.roll(6, 1, piece.name or piece.id) end
        end)
        """);
    Note("Call it from `on_load`: hotkeys are not saved, and a reload registers them again.");
    See("game.hotkeys_clear", "game.selected", "game.hovered");

    M("game", "hotkeys_clear()", "Takes this script's hotkeys away.", "clearHotkeys");
    See("game.hotkey");

    M("game", "menu_add(label, fn)", "A row in the table's own right-click menu; fn(who, { x =, z = }).", "addContextMenuItem", "number");
    P("label", "string", "The row's text.");
    P("fn", "function", "Called as fn(who, point) with the table point that was right-clicked.");
    R("The row's number.");
    X("""
        game.menu_add("Drop a smoke marker here", function(who, at)
          board.spawn{ def = "std.effects.signal-smoke", x = at.x, z = at.z }
        end)
        """);
    See("game.menu_clear", "self.menu_add");

    M("game", "menu_clear()", "Takes this script's rows out of the table's menu.", "clearContextMenu");
    See("game.menu_add");
    }

    static void GameUmpires()
    {
    M("game", "selected(who)", "The pieces that umpire had selected at their last hotkey.", "getSelectedObjects", "Piece[]");
    P("who", "Who|number", "The umpire, or their id.");
    R("A list; empty when they had nothing selected.");
    See("game.hotkey", "game.select");

    M("game", "hovered(who)", "The piece under that umpire's pointer at their last hotkey, or nil.", "getHoverObject", "Piece?");
    P("who", "Who|number", "The umpire, or their id.");
    See("game.hotkey");

    M("game", "select(who, piece, on)", "Puts the piece into that umpire's selection on their screen, or (on = false) takes it out.", "addToPlayerSelection");
    P("who", "Who|number", "The umpire, or their id.");
    P("piece", "Piece|string", "The piece, or its id.");
    O("on", "boolean", "false takes it out of the selection. Default true.");
    See("game.selected");

    M("game", "call(fn_name, ...)", "Runs a function of the global script and returns what it returns.", "Global.call", "any");
    P("fn_name", "string", "A global function of the global script.");
    O("...", "any", "Handed to the function as they are.");
    R("What the function returns; nil when there is no global script or no such function.");
    See("self.call");

    M("game", "get_var(name)", "Reads a global variable of the global script.", "Global.getVar", "any");
    P("name", "string", "The variable's name.");
    R("Its value, or nil.");
    See("game.set_var");

    M("game", "set_var(name, value)", "Writes a global variable of the global script.", "Global.setVar");
    P("name", "string", "The variable's name.");
    P("value", "any", "The new value.");
    See("game.get_var");

    M("game", "dialog(who, { kind, title, text, options }, fn)", "Opens a window for one umpire (nil: everyone): kind memo, input, confirm, options, color or picture. fn(answer, who); the answer is nil when cancelled.", "showInputDialog", "string");
    P("who", "Who?", "The umpire who gets the window; nil for every umpire.");
    O("spec.kind", "\"memo\"|\"input\"|\"confirm\"|\"options\"|\"color\"|\"picture\"", "A text to read and edit, one line, a yes or no, a pick from `options`, a player colour name, or a picture from the umpire's computer or the web, answered as a name for set_decals. Default \"memo\". A module's input dialog whose title asks for an image or URL opens as \"picture\".");
    O("spec.title", "string", "The window's title.");
    O("spec.text", "string", "The text to edit, or the question.");
    O("spec.options", "string[]", "The choices, for \"options\".");
    P("fn", "function", "Called as fn(answer, who) once the umpire answers; `answer` is nil when they cancelled.");
    R("The dialog's token.");
    X("""
        function orders(who)
          game.dialog(who, { kind = "options", title = "Orders", options = { "Advance", "Hold", "Retire" } },
            function(answer)
              if answer then self.set_text("Orders", answer) end
            end)
        end
        """);
    Note("A function that is not a function in the third place is an error.");

    M("game", "fetch_picture(link, fn)", "Downloads a picture (PNG, JPEG, WebP or BMP, up to 16 MB) from the web on the host, for set_decals. fn(picture) gets its name, or nil when the link gave none.");
    P("link", "string", "An http or https link.");
    P("fn", "function", "Called as fn(picture) once the download ends.");
    X("""
        game.fetch_picture("https://example.com/arms.png", function(picture)
          if picture then self.set_decals{ { picture = picture, w = 0.3, h = 0.3 } } end
        end)
        """);
    Note("A module script's setDecals fetches its web links this way by itself.");
    See("self.set_decals", "game.dialog");
    }

    static ApiSection Ui() => Section("ui", "Screen panels, written in XML. A piece's own panels (`self.ui`) take the same calls. A click, a finished edit or a changed value calls the function the element names, as fn(who, value, id): in the global script, or as \"id/fn\" in that piece's.", () =>
    {
        M("ui", "set(xml_or_table)", "Replaces the panels: XML (several roots allowed) or tables { tag =, attributes = {}, children = {}, value = }.", "UI.setXml");
        P("xml_or_table", "string|UiNode[]", "XML, or a list of element tables.");
        X("""
            ui.set([[<Panel id="clock" rectAlignment="UpperCenter" offsetXY="0 -20" width="260" height="44" color="#1b1b1bd8">
              <Text id="time" fontSize="20" color="#ffffff">06:00</Text>
            </Panel>]])
            """);
        Note("A tree deeper than 24 levels, larger than 20,000 elements, or one that holds itself is refused with an error.");
        See("ui.xml", "ui.set_attribute", "self.ui");

        M("ui", "xml()", "The panels as XML, to add to and set again.", "UI.getXml", "string");
        See("ui.set");

        M("ui", "tree()", "The panels as tables.", "UI.getXmlTable", "UiNode[]");
        R("A list of `{ tag =, attributes = {}, children = {}, value = }` tables.");
        See("ui.set");

        M("ui", "set_attribute(id, name, value)", "Sets one attribute of an element; false when there is no such element.", "UI.setAttribute", "boolean");
        P("id", "string", "The element's id.");
        P("name", "string", "The attribute, as in the XML (`text`, `color`, `active`, `percentage`…).");
        P("value", "string|number|boolean", "The value; numbers and booleans are written as the XML would hold them.");
        R("true when the element exists.");
        X("""ui.set_attribute("time", "text", game.time:sub(12, 16))""");
        See("ui.set_attributes", "ui.get_attribute");

        M("ui", "set_attributes(id, table)", "Sets several attributes at once.", "UI.setAttributes", "boolean");
        P("id", "string", "The element's id.");
        P("table", "table", "Attribute → value.");
        R("true when the element exists.");
        See("ui.set_attribute");

        M("ui", "get_attribute(id, name)", "Reads an attribute, or nil.", "UI.getAttribute", "string?");
        P("id", "string", "The element's id.");
        P("name", "string", "The attribute.");
        See("ui.set_attribute");

        M("ui", "show(id)", "Shows an element (active = true).", "UI.show", "boolean");
        P("id", "string", "The element's id.");
        R("true when the element exists.");
        See("ui.hide");

        M("ui", "hide(id)", "Hides an element (active = false).", "UI.hide", "boolean");
        P("id", "string", "The element's id.");
        R("true when the element exists.");
        See("ui.show");

        M("ui", "set_value(id, value)", "Sets an element's value: the text, a toggle's isOn, a slider's or dropdown's value.", "UI.setValue", "boolean");
        P("id", "string", "The element's id.");
        P("value", "string|number|boolean", "The new value.");
        R("true when the element exists.");
        See("ui.get_value");

        M("ui", "get_value(id)", "Reads an element's value.", "UI.getValue", "string?");
        P("id", "string", "The element's id.");
        See("ui.set_value");

        M("ui", "get_attributes(id)", "Every attribute of an element, or nil.", "UI.getAttributes", "table?");
        P("id", "string", "The element's id.");
        See("ui.get_attribute");

        M("ui", "set_class(id, classes)", "Sets an element's classes, which take their looks from the Defaults the panels hold.", "UI.setClass", "boolean");
        P("id", "string", "The element's id.");
        P("classes", "string", "Class names, separated by spaces.");
        R("true when the element exists.");

        M("ui", "pictures()", "The pictures panels name: a list of { name =, url =, type = }.", "UI.getCustomAssets", "table");
        See("ui.set_pictures");

        M("ui", "set_pictures(list)", "Replaces the pictures panels name. An `image` attribute holding a name shows that picture.", "UI.setCustomAssets", "boolean");
        P("list", "table", "{ name =, url = } entries; url is a picture of a pack.");
        See("ui.pictures");

        F("ui", "loading", "boolean", "false: panel pictures come with their packs.", "UI.loading");
        F("ui", "assetType", "table", "{ Image = 0, AssetBundle = 1 }, as module scripts name picture types.", "UI.assetType");
    });

    static ApiSection Wait() => Section("wait", "Timers are not saved: a script that must go on after a reload starts its timers again in on_load. A timer belongs to the script that made it and stops with it. In a frame, the timers counting frames fire first, then those counting seconds, then the conditions, each kind in the order it was set.", () =>
    {
        M("wait", "seconds(s, fn)", "Calls fn once, s seconds from now; returns a handle.", "Wait.time", "number");
        P("s", "number", "Seconds from now.");
        P("fn", "function", "Called with no arguments.");
        R("A handle for `wait.stop`.");
        X("""
            local function tick()
              ui.set_attribute("time", "text", os.date("%H:%M:%S"))
              wait.seconds(1, tick)
            end
            tick()
            """);
        See("wait.stop", "wait.frames");

        M("wait", "frames(n, fn)", "Calls fn once, n frames from now; returns a handle.", "Wait.frames", "number");
        O("n", "number", "Frames from now. Default 1.");
        P("fn", "function", "Called with no arguments.");
        R("A handle for `wait.stop`.");
        See("wait.seconds");

        M("wait", "condition(predicate, fn, timeout_s, on_timeout)", "Calls fn once predicate() is true, or on_timeout() if timeout_s comes first; returns a handle.", "Wait.condition", "number");
        P("predicate", "function", "Asked every frame; return true to fire.");
        P("fn", "function", "Called once the predicate answers true.");
        O("timeout_s", "number", "Give up after this many seconds.");
        O("on_timeout", "function", "Called when the timeout comes first.");
        R("A handle for `wait.stop`.");
        X("""
            wait.condition(function() return not self.face_down end,
                           function() game.say(self.name .. " is revealed") end, 60)
            """);
        Note("A predicate that errors is reported once and dropped.");
        See("wait.stop");

        M("wait", "stop(handle)", "Stops a timer before it fires.", "Wait.stop", "boolean");
        P("handle", "number", "What `seconds`, `frames` or `condition` returned. A timer that has fired already is no error.");
        R("true when a timer was waiting and is stopped now, false when there was none.");

        M("wait", "now()", "The scripts' clock: seconds since the table started running scripts.", "Time.time", "number");

        M("wait", "frame()", "How many frames the scripts have run since the table started.", "Time.frame_count", "number");

        M("wait", "delta()", "Seconds between the last frame and the one before it.", "Time.delta_time", "number");
    });

    static ApiSection JsonCalls() => Section("json", "JSON in and out; numbers are written the same on every PC.", () =>
    {
        M("json", "encode(value, pretty)", "A value as JSON. A table whose keys are all positive numbers is an array up to its highest key, gaps as null; an empty table is []; other tables are objects with their keys in sorted order, number keys after the text ones.", "JSON.encode", "string");
        P("value", "any", "A table, string, number, boolean or nil. A function, a table holding itself or a key that is neither text nor a number is an error.");
        O("pretty", "boolean", "true: one key a line, indented by two spaces.");
        X("""
            function on_save() return json.encode({ hits = hits, orders = orders }) end
            """);
        Note("Text is written as it is, apart from quotes, backslashes and control characters; NaN is null.");
        See("json.decode", "on_save");

        M("json", "decode(text)", "JSON as a value; an error when it does not parse. Text after the first value is ignored, and blank text is nil.", "JSON.decode", "any");
        P("text", "string", "The JSON. Anything else is an error.");
        X("""
            function on_load(saved)
              local ok, data = pcall(json.decode, saved)
              if ok and type(data) == "table" then hits, orders = data.hits or 0, data.orders end
            end
            """);
        See("json.encode", "on_load");
    });

    static ApiSection PieceEvents() => Section("Events of a piece script", "Define any of these in a piece's script and the table calls it.", () =>
    {
        E("event", "on_load(saved)", "The script starts: on hosting, resuming, a reload, a save loaded. saved is what on_save last returned, or \"\".", "onLoad");
        P("saved", "string", "What `on_save` last returned, or \"\" the first time.");
        X("""
            local hits = 0

            function on_load(saved)
              hits = tonumber(saved) or 0
              self.button{ z = 0.3, label = tostring(hits), font = 0.08 }
            end

            function on_save() return tostring(hits) end
            """);
        Note("Draw widgets and register hotkeys and menu rows here. `on_load` and `on_save` get ten seconds; other calls a tenth of one.");
        See("on_save", "self.reload");

        E("event", "on_save()", "The game is being saved, or an umpire's action has just run the script: return a string to keep.", "onSave");
        R("A string to keep; it comes back to `on_load`.");
        Note("It runs once a second while something changed, at every ended turn, on close, and after each umpire action that ran the script, so Ctrl+Z takes the script's state back with the rest.");
        See("on_load", "json.encode");

        E("event", "on_update()", "Every frame on the host. Keep it short: it runs under the same time limit as any call.", "onUpdate");
        Note("Prefer `wait.seconds` or an event: a script that does its work every frame costs the host every frame.");

        E("event", "on_drop(who)", "An umpire let the piece go.", "onDrop");
        E("event", "on_roll(who)", "An umpire rolled this die.", "onRandomize");
        P("who", "Who", "The umpire.");
        See("on_pick_up");

        E("event", "on_pick_up(who)", "An umpire picked the piece up.", "onPickUp");
        P("who", "Who", "The umpire.");
        See("on_drop");

        E("event", "on_state(old, new)", "Its state changed, by anyone.", "onObjectStateChange");
        E("event", "on_page()", "This book turned to another page.", "onPageChange");
        E("event", "on_effect(index)", "An umpire started (0) or stopped (-1) this effect.", "onObjectLoopingEffect");
        P("old", "number", "The state it left, from 1.");
        P("new", "number", "The state it is in now, from 1.");
        X("""
            function on_state(old, new)
              if new == 1 then draw_name() end
            end
            """);
        Note("What the script draws here belongs to the new state.");
        See("self.set_state");

        E("event", "on_flip(face_down)", "It was turned over or back.");
        P("face_down", "boolean", "true when it is face down now.");
        See("self.flip");

        E("event", "on_rotate(rot, old_rot, who)", "An umpire turned it in place.", "onRotate");
        P("rot", "number", "The new heading in degrees.");
        P("old_rot", "number", "The heading before.");
        P("who", "Who", "The umpire.");

        E("event", "on_turn(turn)", "The clock advanced.", "onPlayerTurn");
        P("turn", "number", "The new turn number.");
        X("""
            function on_turn(turn)
              self.set_counter("Fatigue", self.counters.Fatigue + 1)
            end
            """);
        See("game.turn");

        E("event", "on_destroy()", "The piece is about to go.", "onDestroy");
        See("self.delete");
    });

    static ApiSection GlobalEvents() => Section("Events of the global script", "The global script gets on_load, on_save, on_update and on_turn too, and these for every piece on the table.", () =>
    {
        E("global event", "on_spawn(piece)", "A piece came onto the table.", "onObjectSpawn");
        P("piece", "Piece", "The new piece.");
        See("board.spawn");

        E("global event", "on_destroy(piece)", "A piece is about to go.", "onObjectDestroy");
        P("piece", "Piece", "The piece; its fields still answer.");

        E("global event", "on_drop(piece, who)", "An umpire let a piece go.", "onObjectDrop");
        E("global event", "on_state(piece, old, new)", "A piece's state changed, by anyone.", "onObjectStateChange");
        E("global event", "on_page(piece)", "A book turned to another page.", "onObjectPageChange");
        E("global event", "on_effect(piece, index)", "An umpire started (0) or stopped (-1) an effect.", "onObjectLoopingEffect");
        E("global event", "on_roll(piece, who)", "An umpire rolled a die, or turned a piece of several looks to one at random.", "onObjectRandomize");
        P("piece", "Piece", "The piece.");
        P("who", "Who", "The umpire.");
        X("""
            function on_drop(piece, who)
              for _, z in ipairs(board.zones()) do
                if z.contains(piece) then game.say((piece.name or piece.id) .. " enters " .. z.name, { to = who }) end
              end
            end
            """);

        E("global event", "on_rotate(piece, rot, old_rot, who)", "An umpire turned a piece in place.", "onObjectRotate");
        P("piece", "Piece", "The piece.");
        P("rot", "number", "The new heading in degrees.");
        P("old_rot", "number", "The heading before.");
        P("who", "Who", "The umpire.");

        E("global event", "on_pick_up(piece, who)", "An umpire picked a piece up.", "onObjectPickUp");
        P("piece", "Piece", "The piece.");
        P("who", "Who", "The umpire.");

        E("global event", "on_player_action(who, action, pieces)", "An umpire is about to delete or flip pieces: return false to refuse it, with action \"delete\" or \"flip\" and the pieces it would touch.", "onPlayerAction");
        P("who", "Who", "The umpire.");
        P("action", "string", "\"delete\" or \"flip\".");
        P("pieces", "Piece[]", "The pieces the action would touch.");

        E("global event", "try_stow(holder, piece)", "A piece is about to be put into another: return false to keep it out.", "tryObjectEnterContainer");
        P("holder", "Piece", "The piece that would hold it.");
        P("piece", "Piece", "The piece going in.");
        E("global event", "on_stowed(holder, piece)", "A piece went into another, off the table.", "onObjectEnterContainer");
        P("holder", "Piece", "The piece holding it.");
        P("piece", "Piece", "The piece held; its fields still answer, its changes do not.");
        E("global event", "on_unstowed(holder, piece)", "A held piece came back onto the table; on_spawn runs for it as well.", "onObjectLeaveContainer");
        P("holder", "Piece", "The piece that held it.");
        P("piece", "Piece", "The piece back on the table.");

        E("global event", "on_number_typed(piece, who, n)", "An umpire typed a number over a piece: return true to keep its state, else the state becomes n.", "onObjectNumberTyped");
        P("piece", "Piece", "The piece under the pointer, or each piece of the selection.");
        P("who", "Who", "The umpire.");
        P("n", "number", "The number typed.");
        R("true to keep the piece's state; anything else lets the number set it.");

        E("global event", "on_chat(text, who)", "An umpire wrote a line in the Log (not a script's line).", "onChat");
        P("text", "string", "The line.");
        P("who", "Who", "The umpire.");
        X("""
            function on_chat(text, who)
              if text == "!strength" then
                for _, p in ipairs(game.selected(who)) do
                  game.say((p.name or p.id) .. ": " .. tostring(p.counters.Strength), { to = who })
                end
              end
            end
            """);

        E("global event", "on_scripting_button_down(index, who)", "Defining it gives ten hotkeys, Scripting button 1–10 (the keypad until rebound).", "onScriptingButtonDown");
        P("index", "number", "Which button, 1 to 10.");
        P("who", "Who", "The umpire.");

        E("global event", "on_scripting_button_up(index, who)", "The same, on the key's release.", "onScriptingButtonUp");
        P("index", "number", "Which button, 1 to 10.");
        P("who", "Who", "The umpire.");
    });

    static (string group, string sig, string text)[] LuaOwn => new[]
    {
        ("global", "pairs(t)", "Every key and value of a table, in no set order."),
        ("global", "ipairs(t)", "t[1], t[2] … up to the first nil, in order."),
        ("global", "next(t, k)", "The key after k, and its value."),
        ("global", "select(n, ...)", "The arguments from the nth on; select(\"#\", ...) counts them."),
        ("global", "type(v)", "\"nil\", \"number\", \"string\", \"boolean\", \"table\" or \"function\"."),
        ("global", "tostring(v)", "A value as text."),
        ("global", "tonumber(v, base)", "A numeral as a number, or nil when it is not one."),
        ("global", "pcall(fn, ...)", "Calls fn and catches its error: true and the results, or false and the message."),
        ("global", "error(message)", "Stops the call with an error."),
        ("global", "assert(v, message)", "An error when v is false or nil; v otherwise."),
        ("global", "setmetatable(t, mt)", "Gives a table a metatable."),
        ("global", "getmetatable(t)", "A table's metatable."),
        ("global", "rawget(t, k)", "Reads a key without the metatable."),
        ("global", "rawset(t, k, v)", "Writes a key without the metatable."),
        ("global", "unpack(t)", "A list's values as separate values."),
        ("string", "byte(s, i)", "The character code at i."),
        ("string", "char(...)", "Characters from codes."),
        ("string", "find(s, pattern, init, plain)", "Where a pattern first matches: start and end."),
        ("string", "format(fmt, ...)", "printf-style formatting: %d, %s, %.2f…"),
        ("string", "gmatch(s, pattern)", "An iterator over every match."),
        ("string", "gsub(s, pattern, repl)", "Replaces matches; the new string and the count."),
        ("string", "len(s)", "Its length in bytes."),
        ("string", "lower(s)", "In lower case."),
        ("string", "match(s, pattern)", "The first match, or its captures."),
        ("string", "rep(s, n)", "s repeated n times."),
        ("string", "reverse(s)", "Back to front."),
        ("string", "sub(s, i, j)", "The characters from i to j."),
        ("string", "upper(s)", "In upper case."),
        ("table", "concat(t, sep)", "A list's strings joined."),
        ("table", "insert(t, pos, v)", "Adds to a list, at the end or at pos."),
        ("table", "remove(t, pos)", "Takes from a list, the last or at pos, and returns it."),
        ("table", "sort(t, comp)", "Sorts a list in place."),
        ("table", "unpack(t)", "A list's values as separate values."),
        ("math", "abs(x)", "The absolute value."),
        ("math", "ceil(x)", "Rounded up."),
        ("math", "floor(x)", "Rounded down."),
        ("math", "max(...)", "The largest."),
        ("math", "min(...)", "The smallest."),
        ("math", "sqrt(x)", "The square root."),
        ("math", "sin(x)", "Sine of radians."),
        ("math", "cos(x)", "Cosine of radians."),
        ("math", "tan(x)", "Tangent of radians."),
        ("math", "atan2(y, x)", "The angle of a vector, in radians."),
        ("math", "deg(x)", "Radians to degrees."),
        ("math", "rad(x)", "Degrees to radians."),
        ("math", "fmod(x, y)", "The remainder of x / y."),
        ("math", "modf(x)", "The whole and the fractional parts."),
        ("math", "random(m, n)", "A random number: [0, 1), 1..m, or m..n. Not the server's dice: use game.roll for a roll the umpires see."),
        ("math", "randomseed(x)", "Seeds math.random."),
        ("math", "exp(x)", "e to the x."),
        ("math", "log(x)", "The natural logarithm."),
        ("math", "pow(x, y)", "x to the y."),
        ("os", "time(t)", "Seconds since 1970, now or for a date table."),
        ("os", "date(format, time)", "A time as text, or as a table with \"*t\"."),
        ("os", "clock()", "Seconds of processor time; for measuring."),
        ("coroutine", "create(fn)", "A coroutine from a function."),
        ("coroutine", "resume(co, ...)", "Runs it until it yields or ends."),
        ("coroutine", "yield(...)", "Hands control back to resume."),
        ("coroutine", "status(co)", "\"suspended\", \"running\", \"normal\" or \"dead\"."),
        ("coroutine", "wrap(fn)", "A coroutine as a function to call."),
        ("coroutine", "running()", "The running coroutine."),
    };

    static ApiSection LuaLibrary() => Section("Lua's own library", "Lua 5.2 (MoonSharp). Not there: io, load, loadstring, dofile, require, debug, and os beyond the clock. The full manual: https://www.lua.org/manual/5.2/", () =>
    {
        foreach (var (group, sig, text) in LuaOwn) M(group, sig, text);
        F("math", "pi", "number", "π.");
        F("math", "huge", "number", "A number larger than any other.");
    });

    public static IEnumerable<ApiEntry> All => Sections.SelectMany(s => s.Entries);

    public static IEnumerable<ApiEntry> MembersOf(string receiver)
    {
        var group = receiver switch
        {
            "board" or "game" or "ui" or "wait" or "json" or "string" or "table" or "math" or "os" or "coroutine" => receiver,
            _ when receiver.EndsWith(".ui") => "ui",
            _ => "piece",
        };
        return All.Where(e => e.Group == group);
    }

    public static IEnumerable<ApiEntry> TopLevel => All.Where(e => e.Group is "global" or "event" or "global event").GroupBy(e => e.Name).Select(g => g.First());

    public static ApiEntry? Lookup(string? receiver, string name)
    {
        if (receiver == null) return TopLevel.FirstOrDefault(e => e.Name == name);
        return MembersOf(receiver).FirstOrDefault(e => e.Name == name);
    }

    public static IEnumerable<string> Keywords => new[] { "self", "board", "game", "ui", "wait", "json", "compat", "print", "log" };

    public const string Regenerate = "dotnet run --project tools/Converter -- api-reference";

    public const string File = "scripting-api.md";

    static bool IsLua(ApiSection s) => s.Title.StartsWith("Lua", StringComparison.Ordinal);

    static readonly Lazy<Dictionary<ApiEntry, string>> _anchors = new(Anchors);

    public static string AnchorOf(ApiEntry e) => _anchors.Value.TryGetValue(e, out var a) ? a : HeadingSlugs.Slug(SectionOf(e)?.Title ?? "");

    public static ApiSection? SectionOf(ApiEntry e) => Sections.FirstOrDefault(s => s.Entries.Contains(e));

    static Dictionary<ApiEntry, string> Anchors()
    {
        var slugs = new HeadingSlugs();
        var map = new Dictionary<ApiEntry, string>(ReferenceEqualityComparer.Instance);
        foreach (var h in FixedHeadingsBefore) slugs.Next(h);
        foreach (var s in Sections)
        {
            slugs.Next(s.Title);
            if (IsLua(s)) continue;
            foreach (var e in s.Entries) map[e] = slugs.Next(e.Heading);
        }
        return map;
    }

    static readonly string[] FixedHeadingsBefore = { "Scripting API reference", "Contents", "Conventions" };

    static ApiEntry? Find(string name) =>
        All.FirstOrDefault(e => e.Heading == "`" + name + "`")
        ?? All.FirstOrDefault(e => e.Qualified == name && !IsLua(SectionOf(e)!));

    static string Cell(string s) => s.Replace("|", "\\|");

    static string Link(string name) => Find(name) is { } e ? $"[`{name}`](#{AnchorOf(e)})" : $"`{name}`";

    public static string Markdown()
    {
        var sb = new StringBuilder();
        sb.Append("# Scripting API reference\n\n");
        sb.Append("Every name a script can use: parameters, returns, an example, and the module call it replaces. ");
        sb.Append("How scripts work: the [scripting guide](scripting.md). **F1** on a word in a script editor opens its entry.\n\n");
        sb.Append($"API version {ScriptApi.Version}. Generated from `src/Server/Scripting/ApiDoc.cs` by `{Regenerate}`.\n\n");
        sb.Append("## Contents\n\n");
        sb.Append("- [Conventions](#conventions)\n");
        foreach (var s in Sections) sb.Append("- [").Append(s.Title).Append("](#").Append(HeadingSlugs.Slug(s.Title)).Append(")\n");
        sb.Append("- [Module calls](#module-calls)\n");
        sb.Append("- [Compatibility](#compatibility)\n\n");
        Conventions(sb);
        foreach (var s in Sections)
        {
            sb.Append("## ").Append(s.Title).Append("\n\n");
            if (s.Intro.Length > 0) sb.Append(s.Intro).Append("\n\n");
            if (IsLua(s)) { LuaTables(sb, s); continue; }
            sb.Append("| Name | What it does |\n|---|---|\n");
            foreach (var e in s.Entries) sb.Append("| [`").Append(e.Qualified).Append("`](#").Append(AnchorOf(e)).Append(") | ").Append(Cell(e.Summary)).Append(" |\n");
            sb.Append('\n');
            foreach (var e in s.Entries) Entry(sb, e);
        }
        ModuleCalls(sb);
        Compatibility(sb);
        return sb.ToString();
    }

    static void Conventions(StringBuilder sb)
    {
        sb.Append("## Conventions\n\n");
        sb.Append("Calls use a dot, not a colon: `self.set_name(\"Guard\")`. _Optional_ parameters may be left out. ");
        sb.Append("Calls that take a table use braces: `board.spawn{ def = \"…\", x = 10, z = 4 }`.\n\n");
        sb.Append("| Type | What it is |\n|---|---|\n");
        sb.Append("| `Piece` | A piece on the umpire board: `self`, or what `board.find`, `board.pieces` and `board.spawn` give back. Its fields are read; its `set_` calls change it. |\n");
        sb.Append("| `Who` | An umpire: `{ id =, name =, role = }`, role `\"host\"`, `\"lead\"` or `\"umpire\"`. Handlers get one as `who`. |\n");
        sb.Append("| `Point` | A place on the map: `{ x =, z = }` in map units. |\n");
        sb.Append("| `Size` | `{ w =, h = }` in map units. |\n");
        sb.Append("| `Color` | `\"#RRGGBB\"`, `\"#RRGGBBAA\"`, a name (`black`, `white`, `red`, `green`, `blue`, `yellow`, `orange`, `purple`, `pink`, `brown`, `teal`, `grey`) or `{ r, g, b, a }` from 0 to 1. |\n");
        sb.Append("| `Line` | `{ points = { Point, … }, color = Color, width = number, loop = boolean }`, points in piece units. |\n");
        sb.Append("| `UiNode` | A panel element as a table: `{ tag =, attributes = {}, children = {}, value = }`. |\n");
        sb.Append("| `T[]` | A list (a table with keys 1..n) of T. |\n");
        sb.Append("| `T?` | A T, or nil. |\n\n");
        sb.Append("**Units.** _Map units_ are the table's own (`pos`, `set_pos`, `spawn`). _Piece units_ are map units measured on the piece's kind before the table scales it, ");
        sb.Append("centre (0, 0), x to the right, z down the picture (widgets, lines, `to_world`): a kind 1.2 wide runs from x = -0.6 to 0.6, and a piece scaled up carries its widgets with it. ");
        sb.Append("_Metres_ come from the map's scale (`board.distance`, `game.calibration`).\n\n");
        sb.Append("**Handler names.** `\"fn\"` is looked up in the drawing script, then the global script; `\"global:fn\"` forces the global script; ");
        sb.Append("`\"<id or GUID>/fn\"` is that piece's.\n\n");
        sb.Append("**Errors** stop the current call and show in the console with the piece and line. The script stays loaded.\n\n");
    }

    static void Entry(StringBuilder sb, ApiEntry e)
    {
        sb.Append("### ").Append(e.Heading).Append("\n\n");
        sb.Append("```lua\n").Append(e.Display).Append("\n```\n\n");
        sb.Append(e.Summary).Append("\n\n");
        if (e.Kind == "field") sb.Append("**Type** `").Append(e.Type ?? "any").Append("`, read-only.\n\n");
        if (e.Kind == "event") sb.Append("An event: define this function and the table calls it.\n\n");
        if (e.Params.Count > 0)
        {
            sb.Append(e.TakesSpec ? "| Field | Type | Description |\n|---|---|---|\n" : "| Parameter | Type | Description |\n|---|---|---|\n");
            foreach (var p in e.Params)
                sb.Append("| `").Append(p.Name).Append("` | `").Append(Cell(p.Type)).Append("` | ").Append(p.Optional ? "_Optional._ " : "").Append(Cell(p.Text)).Append(" |\n");
            sb.Append('\n');
        }
        if (e.Returns != null || e.ReturnText != null)
            sb.Append("**Returns**").Append(e.Returns != null ? " `" + e.Returns + "`" : "").Append(e.ReturnText != null ? ": " + e.ReturnText : ".").Append("\n\n");
        if (e.Example != null) sb.Append("```lua\n").Append(e.Example).Append("\n```\n\n");
        foreach (var n in e.Notes) sb.Append(n).Append("\n\n");
        var tail = new List<string>();
        if (e.ModuleCall != null) tail.Add("**Module call** `" + e.ModuleCall + "`");
        if (e.See.Count > 0) tail.Add("**See also** " + string.Join(", ", e.See.Select(Link)));
        if (tail.Count > 0) sb.Append(string.Join(" · ", tail)).Append("\n\n");
    }

    static void LuaTables(StringBuilder sb, ApiSection s)
    {
        foreach (var g in s.Entries.GroupBy(e => e.Group))
        {
            sb.Append("### ").Append(g.Key == "global" ? "Basic functions" : "`" + g.Key + "`").Append("\n\n");
            sb.Append("| Function | What it does |\n|---|---|\n");
            foreach (var e in g) sb.Append("| `").Append(g.Key == "global" ? e.Signature : g.Key + "." + e.Signature).Append("` | ").Append(Cell(e.Summary)).Append(" |\n");
            sb.Append('\n');
        }
        sb.Append("### Differences from plain Lua\n\n");
        sb.Append("- `tonumber(s, base)` gives nil for what is not a numeral in that base, as Lua does (MoonSharp raised an error nothing could catch).\n");
        sb.Append("- `string.rep` refuses to build more than 16 million characters.\n");
        sb.Append("- A coroutine runs under the time limit of the call that resumes it, and one call may make 64 at most.\n");
        sb.Append("- `table.sort` with a comparison and `string.gsub` with a function run that function under the same limit, so one that never returns is stopped like any other loop.\n\n");
    }

    static void ModuleCalls(StringBuilder sb)
    {
        sb.Append("## Module calls\n\n");
        sb.Append("The converter rewrites a module's scripts into these calls by rule; this is the same map, by the name the module's scripts use. ");
        sb.Append("Calls are written with a dot (`self.set_name(x)`), not with a colon, and positions are map points `{ x =, z = }`. ");
        sb.Append("The [scripting guide](scripting.md#what-converted-module-scripts-become) says what the converter does with the rest.\n\n");
        sb.Append("| Module | Here |\n|---|---|\n");
        foreach (var e in All.Where(e => e.ModuleCall != null).OrderBy(e => e.ModuleCall, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Qualified, StringComparer.Ordinal))
            sb.Append("| `").Append(e.ModuleCall).Append("` | [`").Append(e.Qualified).Append("`](#").Append(AnchorOf(e)).Append(") |\n");
        sb.Append('\n');
    }

    static void Compatibility(StringBuilder sb)
    {
        sb.Append("## Compatibility\n\n");
        sb.Append($"This is version {ScriptApi.Version} of the scripting API. A pack or scenario records the version it was made for (`apiVersion` in ");
        sb.Append("its pack.json or scenario.json; one without it is taken to be this version), and a build refuses one made for a newer API, or in a ");
        sb.Append("newer pack or scenario format, with a message to update the app, rather than run scripts that call what it does not have.\n\n");
        sb.Append("**What breaks a script**, and so takes a new version: a name removed or renamed, an argument dropped, reordered or read differently, ");
        sb.Append("a value returned in another type or shape, an event called at another moment or with other arguments. Adding a name, an optional ");
        sb.Append("argument at the end or a field to a returned table breaks nothing and keeps the version.\n\n");
        sb.Append("**Deprecation.** A name is deprecated for at least one release before it is removed: it still works, and the console notes once ");
        sb.Append("per game that it is going and what to use instead. The removal is what takes the new version.\n");
    }

    public static string Stubs()
    {
        var sb = new StringBuilder();
        sb.Append("---@meta\n");
        sb.Append("-- Kriegsspiel Table scripting API, for the Lua language server (VS Code's Lua extension).\n");
        sb.Append($"-- Written from src/Server/Scripting/ApiDoc.cs by `{Regenerate}`; do not edit by hand.\n\n");
        sb.Append("---@alias Color string|number[]|{r: number, g: number, b: number, a: number}\n\n");
        sb.Append("---@class Who\n---@field id number\n---@field name string\n---@field role \"host\"|\"lead\"|\"umpire\"\n\n");
        sb.Append("---@class Point\n---@field x number\n---@field z number\n\n");
        sb.Append("---@class Size\n---@field w number\n---@field h number\n\n");
        sb.Append("---@class Line\n---@field points Point[]\n---@field color? Color\n---@field width? number\n---@field loop? boolean\n\n");
        sb.Append("---@class Decal\n---@field picture string\n---@field name? string\n---@field face? \"front\"|\"back\"\n---@field x? number\n---@field z? number\n---@field w? number\n---@field h? number\n---@field rot? number\n\n");
        sb.Append("---@class UiNode\n---@field tag string\n---@field attributes table<string, string>\n---@field children UiNode[]\n---@field value? string\n\n");
        sb.Append("---@class Zone\n---@field id string\n---@field name string\n---@field owner string\n---@field contains fun(piece_or_point: Piece|Point): boolean\n\n");
        foreach (var e in All.Where(e => e.Params.Any(p => p.Name.Contains('.')) || (e.TakesSpec && e.Params.Count > 0)))
        {
            sb.Append("---@class ").Append(SpecClass(e)).Append('\n');
            foreach (var p in e.Params.Where(p => e.TakesSpec || p.Name.Contains('.')))
                sb.Append("---@field ").Append(p.Name.Contains('.') ? p.Name[(p.Name.IndexOf('.') + 1)..] : p.Name).Append(p.Optional ? "? " : " ").Append(LuaType(p.Type)).Append(' ').Append(Flat(p.Text)).Append('\n');
            sb.Append('\n');
        }
        sb.Append("---@class Piece\n");
        foreach (var e in All.Where(e => e.Group == "piece" && e.Kind == "field"))
            sb.Append("---@field ").Append(e.Name).Append(' ').Append(LuaType(e.Type)).Append(' ').Append(Flat(e.Summary)).Append('\n');
        sb.Append("local Piece = {}\n\n");
        foreach (var e in All.Where(e => e.Group == "piece" && e.Kind == "function")) Function(sb, e, "Piece." + e.Name);
        sb.Append("---@type Piece\nself = nil\n\n");
        foreach (var g in new[] { "board", "game", "ui", "wait", "json" })
        {
            sb.Append("---@class ").Append(Cap(g)).Append('\n');
            foreach (var e in All.Where(e => e.Group == g && e.Kind == "field"))
                sb.Append("---@field ").Append(e.Name).Append(' ').Append(LuaType(e.Type)).Append(' ').Append(Flat(e.Summary)).Append('\n');
            sb.Append(g).Append(" = {}\n\n");
            foreach (var e in All.Where(e => e.Group == g && e.Kind == "function")) Function(sb, e, g + "." + e.Name);
        }
        foreach (var e in All.Where(e => e.Group == "global" && e.Name is "print" or "log")) Function(sb, e, e.Name);
        sb.Append("-- Events: define these in a script and the table calls them.\n\n");
        foreach (var e in All.Where(e => e.Kind == "event").GroupBy(e => e.Name).Select(g => g.First())) Function(sb, e, e.Name);
        return sb.ToString();
    }

    static void Function(StringBuilder sb, ApiEntry e, string name)
    {
        sb.Append("---").Append(Flat(e.Summary)).Append('\n');
        var names = new List<string>();
        if (e.TakesSpec)
        {
            names.Add("spec");
            sb.Append("---@param spec ").Append(SpecClass(e)).Append('\n');
        }
        else
        {
            foreach (var p in e.Params)
            {
                var dot = p.Name.IndexOf('.');
                if (dot > 0)
                {
                    var head = p.Name[..dot];
                    if (names.Contains(head)) continue;
                    names.Add(head);
                    bool optional = e.Params.Where(q => q.Name.StartsWith(head + ".", StringComparison.Ordinal)).All(q => q.Optional);
                    sb.Append("---@param ").Append(head).Append(optional ? "? " : " ").Append(SpecClass(e)).Append('\n');
                    continue;
                }
                names.Add(p.Name);
                sb.Append("---@param ").Append(p.Name).Append(p.Optional && p.Name != "..." ? "? " : " ").Append(LuaType(p.Type)).Append(' ').Append(Flat(p.Text)).Append('\n');
            }
        }
        if (e.Returns != null) sb.Append("---@return ").Append(LuaType(e.Returns)).Append(e.ReturnText != null ? " # " + Flat(e.ReturnText) : "").Append('\n');
        sb.Append("function ").Append(name).Append('(').Append(string.Join(", ", names)).Append(") end\n\n");
    }

    static string SpecClass(ApiEntry e) => Cap(e.Group == "piece" ? "piece" : e.Group).Replace("KT", "") + char.ToUpperInvariant(e.Name[0]) + e.Name[1..].Replace("_", "") + (e.TakesSpec ? "Spec" : "Options");

    static string Flat(string s) => s.Replace("\n", " ").Replace("`", "");

    static string Cap(string s) => "KT" + char.ToUpperInvariant(s[0]) + s[1..];

    static string LuaType(string? t) => t switch
    {
        null => "any",
        "{x, z}" => "Point",
        "{w, h}" => "Size",
        "Who|string" => "Who|\"u:\"",
        _ => t.EndsWith('?') && !t.Contains('|') ? t[..^1] + "|nil" : t.Replace("?", "|nil"),
    };
}
