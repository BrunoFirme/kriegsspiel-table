---@meta
-- Kriegsspiel Table scripting API, for the Lua language server (VS Code's Lua extension).
-- Written from src/Server/Scripting/ApiDoc.cs by `dotnet run --project tools/Converter -- api-reference`; do not edit by hand.

---@alias Color string|number[]|{r: number, g: number, b: number, a: number}

---@class Who
---@field id number
---@field name string
---@field role "host"|"lead"|"umpire"

---@class Point
---@field x number
---@field z number

---@class Size
---@field w number
---@field h number

---@class Line
---@field points Point[]
---@field color? Color
---@field width? number
---@field loop? boolean

---@class Decal
---@field picture string
---@field name? string
---@field face? "front"|"back"
---@field x? number
---@field z? number
---@field w? number
---@field h? number
---@field rot? number

---@class UiNode
---@field tag string
---@field attributes table<string, string>
---@field children UiNode[]
---@field value? string

---@class Zone
---@field id string
---@field name string
---@field owner string
---@field contains fun(piece_or_point: Piece|Point): boolean

---@class GlobalPrintOptions
---@field ..? any The values to write; each is written as tostring would.

---@class PieceButtonSpec
---@field face? "front"|"back" The face it is drawn on. Default "front".
---@field x? number Centre, piece units right of the piece's centre. Default 0.
---@field z? number Centre, piece units down the picture. Default 0.
---@field w? number Width in piece units. Leave out w and h for a bare label.
---@field h? number Height in piece units. The box is exactly what can be pressed.
---@field rot? number Turn on the face, in degrees. Default 0.
---@field label? string The text.
---@field font? number Letter size in piece units; a longer label runs past its button. Default 0.05.
---@field color? Color The box's colour.
---@field text_color? Color The text's colour.
---@field tooltip? string Shown when the pointer rests on it.
---@field on_click? string The handler's name: called as fn(who, alt, index, piece); alt is true for a right-click.
---@field kind? "button"|"input"|"label" "label" shows it but takes no clicks or typing: locked text. A module's button or input turned face down above the piece comes across this way. edit can change it.

---@class PieceInputSpec
---@field face? "front"|"back" The face it is drawn on. Default "front".
---@field x? number Centre, piece units. Default 0.
---@field z? number Centre, piece units. Default 0.
---@field w? number Width in piece units.
---@field h? number Height in piece units.
---@field rot? number Turn on the face, in degrees.
---@field value? string The text in the box.
---@field placeholder? string Shown greyed while the box is empty.
---@field font? number Letter size in piece units; the box shows as much of the text as fits. Default 0.05.
---@field color? Color The box's colour.
---@field text_color? Color The text's colour.
---@field align? "left"|"center"|"right" Default "center".
---@field lines? number Lines of text the box holds. Default 1.
---@field tooltip? string Shown when the pointer rests on it.
---@field on_change? string The handler's name: called as fn(who, value, index, piece) when the umpire finishes typing.
---@field kind? "button"|"input"|"label" "label" shows its value but takes no typing: locked text.

---@class PieceCallOptions
---@field ..? any Handed to the function as they are.

---@class BoardPiecesOptions
---@field def? string Only pieces of this kind.
---@field tag? string Only pieces with this tag (their own, or their kind's).
---@field name? string Only pieces with this label; case does not matter.

---@class BoardSpawnSpec
---@field def string The id of a piece definition in this game's packs.
---@field x? number Map units. Default 0.
---@field z? number Map units. Default 0.
---@field rot? number Heading in degrees. Default 0.
---@field label? string The label under it.
---@field state? number The state to start in, from 1.
---@field scale? number Size multiplier. Default 1.
---@field y? number Height above the table.
---@field script? string A script of its own, in place of its kind's.
---@field tags? string[] Tags of its own.
---@field memo? string Its memo.
---@field note? string Its description.
---@field script_state? string Handed to its on_load.
---@field guid? string A module GUID to give it; one already taken is replaced by a fresh one.

---@class GameSayOptions
---@field to? Who|"u:" An umpire (a Who) for a line only they see; "u:" for the umpires only; nil for everyone at the table.
---@field color? Color The line's colour.

---@class GameCallOptions
---@field ..? any Handed to the function as they are.

---@class GameDialogOptions
---@field kind? "memo"|"input"|"confirm"|"options"|"color"|"picture" A text to read and edit, one line, a yes or no, a pick from options, a player colour name, or a picture from the umpire's computer or the web, answered as a name for set_decals. Default "memo". A module's input dialog whose title asks for an image or URL opens as "picture".
---@field title? string The window's title.
---@field text? string The text to edit, or the question.
---@field options? string[] The choices, for "options".

---@class Piece
---@field id string Our id for the piece (p-000123). Never changes.
---@field guid string The GUID its module gave it; a piece made here answers with its id. board.find takes either.
---@field def string The id of its piece definition (its kind).
---@field pack string The id of the pack its kind comes from.
---@field name string|nil The label shown under the piece, or nil.
---@field note string|nil Its description, shown on hover; umpires only. nil when empty.
---@field state number Its state, from 1.
---@field states string[] The names of its kind's states, in order.
---@field pos Point Where it stands, in map units: { x =, z = }.
---@field rot number Its heading in degrees.
---@field y number How high it sits above the table (stacked pieces).
---@field scale number Its size multiplier on the table.
---@field size Size Its size on the map at its state and scale: { w =, h = }.
---@field picture_size Size|nil Its picture's width and height in pixels at its state, or nil without a picture.
---@field local_unit Size|nil For a model that came from a module, what one unit of its script's local frame is in piece units: { x =, z = }; nil for every other piece.
---@field shape string Its outline at its state: "box", "rounded", "circle", "hex", or "alpha" for a token cut along its picture.
---@field face_down boolean Turned over.
---@field locked boolean Pinned to the table: not dragged, turned or resized by hand.
---@field sticky boolean Carries the pieces resting on it when it is moved; attached pieces go with it either way.
---@field box_selected boolean Taken by a selection box dragged over it.
---@field measures_moves boolean Shows how far it is moved while it is held.
---@field tint string|nil Its colour as "#RRGGBB", or nil.
---@field counters table Its kind's counters, name → value.
---@field text table Its text lines that hold a value, name → text.
---@field attached Piece[] The pieces riding on it.
---@field tags string[] Its own tags, or its kind's when it has none of its own.
---@field memo string A string the scripts keep on the piece; saved with the game.
---@field script_state string What its on_save last returned.
---@field script string|nil The script it runs (its own, or its kind's), or nil.
---@field ui table Its own panels, drawn on its face: the same calls as the global ui.
---@field lines Line[] The lines set_lines drew round it.
---@field decals Decal[] The pictures on its faces: the ones its look carries, or those set_decals put there.
---@field exists boolean False once the piece is gone (its other fields are nil then).
---@field stowed list The pieces it holds off the table, first in first: { id =, name =, guid =, def =, held = }, held being how many that piece holds in turn.
local Piece = {}

---Sets the label under the piece; nil takes it away.
---@param text? string|nil The label. nil, or no argument, takes it away.
function Piece.set_name(text) end

---Sets its description; nil takes it away.
---@param text? string|nil The description, shown to umpires on hover. nil takes it away.
function Piece.set_note(text) end

---Changes its state: a number from 1, or a state's name. What the scripts drew belongs to the state it was drawn on.
---@param n_or_name number|string A state number from 1, or a state's name (case does not matter).
function Piece.set_state(n_or_name) end

---Moves it, in map units; its heading stays. A place { x, z } or another piece works in place of x and z.
---@param x? number The new x in map units; nil keeps the current one.
---@param z? number The new z in map units; nil keeps the current one.
function Piece.set_pos(x, z) end

---Turns it to a heading in degrees.
---@param deg number The heading in degrees.
function Piece.set_rot(deg) end

---Moves and turns it in one change. A place { x, z } or another piece works in place of x and z: move(place, rot).
---@param x? number The new x in map units; nil keeps it.
---@param z? number The new z in map units; nil keeps it.
---@param rot? number The new heading in degrees; nil keeps it.
function Piece.move(x, z, rot) end

---Sets its size multiplier.
---@param k number The multiplier; 1 is the kind's own size.
function Piece.set_scale(k) end

---Turns it over; with no argument, to the other side.
---@param face_down? boolean true for face down, false for face up. Left out, the piece turns to its other side.
function Piece.flip(face_down) end

---Tints its sides: "#RRGGBB", a name, or { r, g, b }. nil goes back to its kind's.
---@param color? Color|nil The tint. nil goes back to the kind's.
function Piece.set_tint(color) end

---Pins it to the table, or lets it go.
---@param on? boolean true, or no argument, pins it; false lets it go.
function Piece.set_locked(on) end

---Whether a selection box dragged over it takes it.
---@param on? boolean true, or no argument, lets a box take it; false leaves it out.
function Piece.set_box_selected(on) end

---Whether it shows how far it is moved while it is held.
---@param on? boolean true, or no argument, shows the distance; false hides it.
function Piece.set_measures_moves(on) end

---Whether it carries the pieces resting on it when it is moved.
---@param on? boolean true, or no argument, carries them; false leaves them where they lie.
function Piece.set_sticky(on) end

---Sets one of its kind's counters (a whole number, kept to the counter's range).
---@param name string The counter's name, as the piece studio shows it.
---@param value number Rounded to a whole number and kept to the counter's range.
function Piece.set_counter(name, value) end

---Sets one of its kind's text lines.
---@param name string The text line's name, as the piece studio shows it.
---@param text string The new text.
function Piece.set_text(name, text) end

---Gives it its own tags; nil goes back to its kind's.
---@param list? string[]|nil The tags. Empty strings are dropped. nil goes back to the kind's tags.
function Piece.set_tags(list) end

---Adds a tag to its own.
---@param tag string The tag. Nothing changes when the piece has it already.
function Piece.add_tag(tag) end

---Takes a tag away.
---@param tag string The tag. Nothing changes when the piece does not have it.
function Piece.remove_tag(tag) end

---Sets its memo.
---@param text? string|nil The memo; nil empties it.
function Piece.set_memo(text) end

---Sets its saved script state outright (on_save's answer is the usual way).
---@param text? string|nil The state; nil empties it.
function Piece.set_script_state(text) end

---Wears another kind's picture; it keeps its own size, states, counters and script. nil puts its own back.
---@param def? string|nil The id of a piece definition in this game's packs. nil puts the piece's own picture back.
function Piece.set_picture(def) end

---Becomes another kind of piece altogether.
---@param def string The id of a piece definition in this game's packs.
function Piece.set_def(def) end

---Gives it a script of its own (nil: back to its kind's). Only from a click or menu handler, or on a piece the same call spawned.
---@param text? string|nil The Lua source. nil goes back to the kind's script.
function Piece.set_script(text) end

---Starts its script again from on_load.
function Piece.reload() end

---Puts another piece on this one, to ride with it.
---@param piece Piece|string The piece to carry, or its id.
function Piece.attach(piece) end

---Takes this piece off the one it rides on.
function Piece.detach() end

---Takes another piece off the table into this one, with whatever rides on it; a piece this one rides on is refused.
function Piece.stow() end

---Puts a held piece back on the table and returns it: which is its id, guid, its number in stowed, or nothing for the next by the holder's take order (first, last or random); place is { x =, z = } or a piece, else beside this one; rot turns it.
function Piece.unstow() end

---Takes the piece off the table (on_destroy runs first).
function Piece.delete() end

---Draws lines round the piece that move and turn with it: { { points = { {x=, z=}, … }, color =, width =, loop = }, … } in piece units; width in map units.
---@param lines Line[] The lines. Each needs two points or more; points are { x =, z = } or { x, z } in piece units. An empty table takes every line away.
function Piece.set_lines(lines) end

---Puts pictures on the piece's faces that move, turn and flip with it: { { picture =, x =, z =, w =, h =, rot =, face =, name = }, … } in piece units.
---@param decals Decal[] The decals. picture is a picture in this game's packs (a module's decal images come along when it is converted) or one from game.fetch_picture or a picture dialog; face is "front" or "back"; w and h default to 0.2, rot is degrees. An empty table takes every decal away.
function Piece.set_decals(decals) end

---A point in piece units (centre 0, x right, z down the picture) as a map point { x =, z = }.
---@param x number Piece units to the right of the centre.
---@param z number Piece units down the picture from the centre.
---@return Point
function Piece.to_world(x, z) end

---A map point as piece units.
---@param x number The map point's x.
---@param z number The map point's z.
---@return Point
function Piece.to_local(x, z) end

---Draws a button and returns its index from 1. With no w and h it is a bare label that takes no clicks. on_click names a function called as fn(who, alt, index, piece).
---@param spec PieceButtonSpec
---@return number # The widget's index, from 1, for edit and remove_widget.
function Piece.button(spec) end

---Draws an input box and returns its index. on_change names a function called as fn(who, value, index, piece) when the umpire has typed.
---@param spec PieceInputSpec
---@return number # The widget's index, from 1.
function Piece.input(spec) end

---Changes a widget: any of the fields button and input take.
---@param index number The widget's index, from 1.
---@param fields table The fields to change, as button or input takes them; the others stay.
function Piece.edit(index, fields) end

---Takes one widget away (the later ones move up).
---@param index number The widget's index, from 1. An index with no widget does nothing.
function Piece.remove_widget(index) end

---Takes every widget away.
function Piece.clear_widgets() end

---The widgets as tables, in order.
---@return table # A list of tables with kind ("button" or "input") and every field button and input take.
function Piece.widgets() end

---Adds a row to the piece's right-click menu; the function is called as fn(who, piece).
---@param label string The row's text.
---@param fn_name string The handler's name, looked up as for on_click.
function Piece.menu_add(label, fn_name) end

---Takes the script's rows out of the piece's menu.
function Piece.menu_clear() end

---Runs a function of that piece's script and returns what it returns.
---@param fn_name string A global function of that piece's script.
---@param ... any Handed to the function as they are.
---@return any # What the function returns; nil when the piece has no script or the script no such function.
function Piece.call(fn_name, ...) end

---Reads a global variable of that piece's script.
---@param name string The variable's name.
---@return any # Its value, or nil.
function Piece.get_var(name) end

---Writes a global variable of that piece's script.
---@param name string The variable's name.
---@param value any The new value.
function Piece.set_var(name, value) end

---@type Piece
self = nil

---@class KTBoard
board = {}

---The piece with that id or module GUID, or nil.
---@param id_or_guid string Our id (p-000123) or the module GUID the piece came with.
---@return Piece # The piece, or nil when there is none.
function board.find(id_or_guid) end

---Every piece, or those that match { def =, tag =, name = }.
---@param filter? BoardPiecesOptions
---@return Piece[] # A list; empty when nothing matches. Every field given must match.
function board.pieces(filter) end

---Puts a new piece of that kind on the board and returns it; its script starts at once.
---@param spec BoardSpawnSpec
---@return Piece # The new piece.
function board.spawn(spec) end

---Metres between two pieces or two { x =, z = } points by the map's scale (map units when it has none).
---@param a Piece|Point A piece, or a map point.
---@param b Piece|Point A piece, or a map point.
---@return number # The distance in metres; in map units while the map has no scale; nil when either is neither a piece nor a point.
function board.distance(a, b) end

---The umpire zones: { id =, name =, owner =, contains = function(piece_or_point) }.
---@return table
function board.zones() end

---Draws a line on the umpire's drawing board and returns its id: { points = { {x=,z=}, ... }, color =, width =, loop = }.
---@return string
function board.draw() end

---Takes a drawn line off the board.
function board.erase() end

---The lines on the umpire's drawing board: { id =, points =, color =, width = }.
---@return table[] # A list of zones. contains(piece_or_point) answers true when the piece or point lies inside.
function board.strokes() end

---@class KTGame
---@field turn number The turn number.
---@field time string The game time, as "1812-07-22T06:00".
---@field calibration number|nil Map units per metre, or nil before the map has a scale.
game = {}

---Ends the turn, as Next turn does.
function game.advance_clock() end

---The umpires at the table: { { id =, name =, role = "host"|"lead"|"umpire" }, … }.
---@return Who[] # A list of the umpires present, as Who tables.
function game.umpires() end

---A line in the Log: to nil = everyone at the table, an umpire's table = that umpire, "u:" = the umpires.
---@param text string The line.
---@param opts? GameSayOptions
function game.say(text, opts) end

---Dice rolled by the server, in the Log; the results as a list.
---@param sides? number Sides of each die. Default 6.
---@param count? number How many dice. Default 1.
---@param label? string Shown with the roll in the Log.
---@return number[] # The results, one number per die.
function game.roll(sides, count, label) end

---A key each umpire binds in Settings → Keys; fn(who, piece_under_pointer, { x =, z = }, released). on_up: called on the release too.
---@param label string Shown in Settings → Keys. A label that ends in a digit starts on that number key.
---@param fn function Called as fn(who, piece, point, released): the piece under the pointer or nil, the table point under it.
---@param on_up? boolean true: fn is called on the key's release too, with released true.
---@return number # The hotkey's number.
function game.hotkey(label, fn, on_up) end

---Takes this script's hotkeys away.
function game.hotkeys_clear() end

---A row in the table's own right-click menu; fn(who, { x =, z = }).
---@param label string The row's text.
---@param fn function Called as fn(who, point) with the table point that was right-clicked.
---@return number # The row's number.
function game.menu_add(label, fn) end

---Takes this script's rows out of the table's menu.
function game.menu_clear() end

---The pieces that umpire had selected at their last hotkey.
---@param who Who|number The umpire, or their id.
---@return Piece[] # A list; empty when they had nothing selected.
function game.selected(who) end

---The piece under that umpire's pointer at their last hotkey, or nil.
---@param who Who|number The umpire, or their id.
---@return Piece|nil
function game.hovered(who) end

---Puts the piece into that umpire's selection on their screen, or (on = false) takes it out.
---@param who Who|number The umpire, or their id.
---@param piece Piece|string The piece, or its id.
---@param on? boolean false takes it out of the selection. Default true.
function game.select(who, piece, on) end

---Runs a function of the global script and returns what it returns.
---@param fn_name string A global function of the global script.
---@param ... any Handed to the function as they are.
---@return any # What the function returns; nil when there is no global script or no such function.
function game.call(fn_name, ...) end

---Reads a global variable of the global script.
---@param name string The variable's name.
---@return any # Its value, or nil.
function game.get_var(name) end

---Writes a global variable of the global script.
---@param name string The variable's name.
---@param value any The new value.
function game.set_var(name, value) end

---Opens a window for one umpire (nil: everyone): kind memo, input, confirm, options, color or picture. fn(answer, who); the answer is nil when cancelled.
---@param who Who|nil The umpire who gets the window; nil for every umpire.
---@param spec? GameDialogOptions
---@param fn function Called as fn(answer, who) once the umpire answers; answer is nil when they cancelled.
---@return string # The dialog's token.
function game.dialog(who, spec, fn) end

---Downloads a picture (PNG, JPEG, WebP or BMP, up to 16 MB) from the web on the host, for set_decals. fn(picture) gets its name, or nil when the link gave none.
---@param link string An http or https link.
---@param fn function Called as fn(picture) once the download ends.
function game.fetch_picture(link, fn) end

---@class KTUi
ui = {}

---Replaces the panels: XML (several roots allowed) or tables { tag =, attributes = {}, children = {}, value = }.
---@param xml_or_table string|UiNode[] XML, or a list of element tables.
function ui.set(xml_or_table) end

---The panels as XML, to add to and set again.
---@return string
function ui.xml() end

---The panels as tables.
---@return UiNode[] # A list of { tag =, attributes = {}, children = {}, value = } tables.
function ui.tree() end

---Sets one attribute of an element; false when there is no such element.
---@param id string The element's id.
---@param name string The attribute, as in the XML (text, color, active, percentage…).
---@param value string|number|boolean The value; numbers and booleans are written as the XML would hold them.
---@return boolean # true when the element exists.
function ui.set_attribute(id, name, value) end

---Sets several attributes at once.
---@param id string The element's id.
---@param table table Attribute → value.
---@return boolean # true when the element exists.
function ui.set_attributes(id, table) end

---Reads an attribute, or nil.
---@param id string The element's id.
---@param name string The attribute.
---@return string|nil
function ui.get_attribute(id, name) end

---Shows an element (active = true).
---@param id string The element's id.
---@return boolean # true when the element exists.
function ui.show(id) end

---Hides an element (active = false).
---@param id string The element's id.
---@return boolean # true when the element exists.
function ui.hide(id) end

---Sets an element's value: the text, a toggle's isOn, a slider's or dropdown's value.
---@param id string The element's id.
---@param value string|number|boolean The new value.
---@return boolean # true when the element exists.
function ui.set_value(id, value) end

---Reads an element's value.
---@param id string The element's id.
---@return string|nil
function ui.get_value(id) end

---@class KTWait
wait = {}

---Calls fn once, s seconds from now; returns a handle.
---@param s number Seconds from now.
---@param fn function Called with no arguments.
---@return number # A handle for wait.stop.
function wait.seconds(s, fn) end

---Calls fn once, n frames from now; returns a handle.
---@param n? number Frames from now. Default 1.
---@param fn function Called with no arguments.
---@return number # A handle for wait.stop.
function wait.frames(n, fn) end

---Calls fn once predicate() is true, or on_timeout() if timeout_s comes first; returns a handle.
---@param predicate function Asked every frame; return true to fire.
---@param fn function Called once the predicate answers true.
---@param timeout_s? number Give up after this many seconds.
---@param on_timeout? function Called when the timeout comes first.
---@return number # A handle for wait.stop.
function wait.condition(predicate, fn, timeout_s, on_timeout) end

---Stops a timer before it fires.
---@param handle number What seconds, frames or condition returned. A timer that has fired already is no error.
function wait.stop(handle) end

---@class KTJson
json = {}

---A value as JSON. A table whose keys are 1..n is an array.
---@param value any A table, string, number, boolean or nil.
---@return string
function json.encode(value) end

---JSON as a value; an error when it does not parse.
---@param text string The JSON.
---@return any
function json.decode(text) end

---Writes its arguments to the script console, separated by tabs.
---@param ... any The values to write; each is written as tostring would.
function print(...) end

---Writes a value to the script console, a table written out a few levels deep; the label goes in front.
---@param value any The value to write. A table is written out with its keys, a few levels deep.
---@param label? string Written in front of the value, followed by a colon.
function log(value, label) end

-- Events: define these in a script and the table calls them.

---The script starts: on hosting, resuming, a reload, a save loaded. saved is what on_save last returned, or "".
---@param saved string What on_save last returned, or "" the first time.
function on_load(saved) end

---The game is being saved, or an umpire's action has just run the script: return a string to keep.
function on_save() end

---Every frame on the host. Keep it short: it runs under the same time limit as any call.
function on_update() end

---An umpire let the piece go.
---@param who Who The umpire.
function on_drop(who) end

---An umpire picked the piece up.
---@param who Who The umpire.
function on_pick_up(who) end

---Its state changed, by anyone.
---@param old number The state it left, from 1.
---@param new number The state it is in now, from 1.
function on_state(old, new) end

---It was turned over or back.
---@param face_down boolean true when it is face down now.
function on_flip(face_down) end

---An umpire turned it in place.
---@param rot number The new heading in degrees.
---@param old_rot number The heading before.
---@param who Who The umpire.
function on_rotate(rot, old_rot, who) end

---The clock advanced.
---@param turn number The new turn number.
function on_turn(turn) end

---The piece is about to go.
function on_destroy() end

---A piece came onto the table.
---@param piece Piece The new piece.
function on_spawn(piece) end

---An umpire is about to delete or flip pieces: return false to refuse it, with action "delete" or "flip" and the pieces it would touch.
---@param who Who The umpire.
---@param action string "delete" or "flip".
---@param pieces Piece[] The pieces the action would touch.
function on_player_action(who, action, pieces) end

---A piece is about to be put into another: return false to keep it out.
---@param holder Piece The piece that would hold it.
---@param piece Piece The piece going in.
function try_stow(holder, piece) end

---A piece went into another, off the table.
---@param holder Piece The piece holding it.
---@param piece Piece The piece held; its fields still answer, its changes do not.
function on_stowed(holder, piece) end

---A held piece came back onto the table; on_spawn runs for it as well.
---@param holder Piece The piece that held it.
---@param piece Piece The piece back on the table.
function on_unstowed(holder, piece) end

---An umpire typed a number over a piece: return true to keep its state, else the state becomes n.
---@param piece Piece The piece under the pointer, or each piece of the selection.
---@param who Who The umpire.
---@param n number The number typed.
function on_number_typed(piece, who, n) end

---An umpire wrote a line in the Log (not a script's line).
---@param text string The line.
---@param who Who The umpire.
function on_chat(text, who) end

---Defining it gives ten hotkeys, Scripting button 1–10 (the keypad until rebound).
---@param index number Which button, 1 to 10.
---@param who Who The umpire.
function on_scripting_button_down(index, who) end

---The same, on the key's release.
---@param index number Which button, 1 to 10.
---@param who Who The umpire.
function on_scripting_button_up(index, who) end

