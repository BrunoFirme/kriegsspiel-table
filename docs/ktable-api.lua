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

---@class PieceShowglideSpec
---@field x? number Where it glides to, with z; leave both out to only turn.
---@field z? number Where it glides to, with x.
---@field rot? number The heading it turns to, in degrees; leave it out to keep its heading.
---@field fast? boolean true pulls it ten times as hard, as a module's fast smooth move does.

---@class PieceSetwebmodelSpec
---@field mesh string A web link to an .obj model.
---@field texture? string A web link to the picture wrapped on it.

---@class PieceButtonSpec
---@field face? "front"|"back" The face it is drawn on. Default "front".
---@field x? number Centre, piece units right of the piece's centre. Default 0.
---@field z? number Centre, piece units down the picture. Default 0.
---@field w? number Width in piece units. Leave out w and h for a bare label.
---@field h? number Height in piece units. The box is exactly what can be pressed.
---@field rot? number Turn on the face, in degrees. Default 0.
---@field y? number Its height in piece units above the piece's base, seen from either side; nil draws it on the face. A module's widget takes its height from the piece's source origin and rise.
---@field label? string The text.
---@field font? number Letter size in piece units; a longer label runs past its button. Default 0.05.
---@field color? Color The box's colour.
---@field text_color? Color The text's colour.
---@field tooltip? string Shown when the pointer rests on it.
---@field hover_color? Color The box's colour under the pointer. Default: each of red, green and blue 0.2 nearer the middle grey.
---@field press_color? Color The box's colour while pressed. Default: each 0.4 nearer the middle grey.
---@field on_click? string The handler's name: called as fn(who, alt, index, piece); alt is true for a right-click.
---@field kind? "button"|"input"|"label" "label" shows it but takes no clicks or typing: locked text. A module's button or input turned face down above the piece comes across this way. edit can change it.

---@class PieceInputSpec
---@field face? "front"|"back" The face it is drawn on. Default "front".
---@field x? number Centre, piece units. Default 0.
---@field z? number Centre, piece units. Default 0.
---@field w? number Width in piece units.
---@field h? number Height in piece units.
---@field rot? number Turn on the face, in degrees.
---@field y? number Its height in piece units above the piece's base; nil draws it on the face.
---@field value? string The text in the box.
---@field placeholder? string Shown greyed while the box is empty.
---@field font? number Letter size in piece units; the box shows as much of the text as fits. Default 0.05.
---@field color? Color The box's colour.
---@field text_color? Color The text's colour.
---@field align? "left"|"center"|"right" Default "center".
---@field lines? number Lines of text the box holds. Default 1.
---@field tooltip? string Shown when the pointer rests on it.
---@field on_change? string The handler's name: called as fn(who, value, index, piece, typing) each time the text changes while the umpire types (typing true) and once more when they leave the box (typing false); text it returns replaces what was typed.
---@field validation? "integer"|"float"|"alphanumeric"|"username"|"name" Keys the box takes: digits and a leading minus; also one point; letters and digits; lower-case letters and digits; or a name, capitalised by word. Default any.
---@field tab? "next"|"spaces"|"none" What Tab does: go on to the piece's next input (the default), type four spaces, or nothing.
---@field kind? "button"|"input"|"label" "label" shows its value but takes no typing: locked text.

---@class PieceCallOptions
---@field ..? any Handed to the function as they are.

---@class BoardPiecesOptions
---@field def? string Only pieces of this kind.
---@field tag? string Only pieces with this tag (their own, or their kind's).
---@field name? string Only pieces with this label; case does not matter.

---@class BoardShowcastSpec
---@field shape? string "ray", "sphere" or "box"; a ray by default.
---@field from { x, y, z } Where it starts.
---@field to { x, y, z } Where it ends.
---@field size? { x, y, z } The sphere's width or the box's size.
---@field turn? { x, y, z } The box's turn in degrees.
---@field seconds? number How long it takes, at most 60; 1 by default.

---@class BoardKindsOptions
---@field link? string Only kinds made from this picture or model link (one of their source links).
---@field type? string Only kinds converted from this module object type, such as Custom_Token.
---@field tag? string Only kinds with this tag.

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
---@field faces Face[] A die's sides in order: { value =, up = { x, y, z }, rotation = { x, y, z } }; empty for every other piece.
---@field face number|nil The side of a die showing, from 1; nil for every other piece.
---@field value number|string|nil The value of the side of a die showing; nil for every other piece.
---@field picked_up table|nil Where an umpire last picked the piece up: { x =, y =, z =, rot = }; nil until then.
---@field face_picture string|nil A picture shown on the piece's face in place of its kind's, set by set_face_picture; nil for none.
---@field page_offset number For a book, what its page numbers start from, as the module set it; 0 for every other piece.
---@field effect_on boolean For an effect, whether it is running.
---@field kind_name string The name of its kind.
---@field lettering table|nil Text drawn on the piece in place of its picture, as 3D text from a module is: { text =, size =, color = }; nil for most pieces.
---@field worth number What the piece counts for when pieces are added up; 0 for most.
---@field worth_groups number Which groups it adds up with, as bits; 0 adds up with any piece of worth.
---@field pos Point Where it stands, in map units: { x =, z = }.
---@field rot number Its heading in degrees.
---@field y number How high it sits above the table (stacked pieces).
---@field scale number Its size multiplier on the table.
---@field size Size Its size on the map at its state and scale: { w =, h = }.
---@field picture_size Size|nil Its picture's width and height in pixels at its state, or nil without a picture.
---@field local_unit Size|nil For a model that came from a module, what one unit of its script's local frame is in piece units: { x =, z = }; nil for every other piece.
---@field shape string Its outline at its state: "box", "rounded", "circle", "hex", or "alpha" for a token cut along its picture.
---@field face_down boolean Turned over.
---@field tilt_x number How far it leans about its own left-right axis, in degrees from -180 to 180, before its heading turns it; 0 for a piece lying flat.
---@field tilt_z number How far it leans about its own front-back axis, in degrees from -180 to 180; 0 for a piece lying flat.
---@field model boolean true for a 3D model, false for a flat piece.
---@field interactable boolean Whether hands can take it (set_interactable).
---@field held_by Who The umpire carrying it now, or nil.
---@field source table For a kind converted from a module: { type =, links = { image =, mesh =, … }, top =, origin =, rise =, tag =, tags = } — the module object's type, its picture and model links, its top surface in its own units, for a flat piece the height of its own origin above the base and the piece units one of its own units rises, the kind of object it was (Tile, Figurine, Board, Bag…) and its own tags; for a piece of several states, states lists each state's { type, guid, description, gm_notes, memo, script_state, tags, script } as the module kept them; nil otherwise.
---@field highlight string The colour it is ringed in, or nil (set_highlight).
---@field book_highlight table For a book, the box marked on the page it is open at: { left =, bottom =, right =, top = }, each from 0 to 1 across the page from its lower left corner; nil for none. Turning the page clears it.
---@field holds boolean Whether it is a holder that other pieces can be stowed in: a piece holding some, one made a holder, or a kind tagged bag.
---@field locked boolean Pinned to the table: not dragged, turned or resized by hand.
---@field sticky boolean Carries the pieces resting on it when it is moved; attached pieces go with it either way.
---@field box_selected boolean Taken by a selection box dragged over it.
---@field measures_moves boolean Shows how far it is moved while it is held.
---@field tint string|nil Its colour as "#RRGGBB", or nil.
---@field counters table Its kind's counters, name → value.
---@field text table Its text lines that hold a value, name → text.
---@field attached Piece[] The pieces riding on it.
---@field tags string[] Its own tags, or its kind's when it has none of its own.
---@field gm_notes string The umpire's notes a module saved on the piece; empty when it had none.
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

---Leans it under its heading: z degrees about its front-back axis, then x about its left-right axis, as a module rotation turns an object; a face-down piece leans turned over. set_tilt(0, 0) lays it flat again. Undo takes it back.
---@param x? number Degrees about its left-right axis.
---@param z? number Degrees about its front-back axis.
function Piece.set_tilt(x, z) end

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

---Whether hands can take it: off, it cannot be picked up, dragged, selected or opened, though its buttons still press.
---@param on? boolean true, or no argument, lets hands take it; false keeps them off.
function Piece.set_interactable(on) end

---Rings it in a colour on every screen; nil takes the ring away.
---@param color? Color The ring's colour, or nil.
function Piece.set_highlight(color) end

---Marks a box on the page a book is open at, in its reader on every screen; nil clears it. Turning the page clears it too.
---@param box? table { left =, bottom =, right =, top = }, each from 0 to 1 across the page from its lower left corner; nil clears the box.
function Piece.set_book_highlight(box) end

---Whether it carries the pieces resting on it when it is moved.
---@param on? boolean true, or no argument, carries them; false leaves them where they lie.
function Piece.set_sticky(on) end

---Turns a die to show a side, from 1; false when it has no such side.
---@param n number The side, from 1, in the order of self.faces.
---@return boolean
function Piece.set_face(n) end

---Turns a die to the first side with this value; false when no side has it.
---@param value number|string The value to show.
---@return boolean
function Piece.set_value(value) end

---Gives this die sides of its own; nil gives it back its kind's.
---@param list table|nil { value =, up = { x, y, z } } or { value =, rotation = { x, y, z } } for each side.
---@return boolean
function Piece.set_faces(list) end

---Shows a picture on the piece's face in place of its kind's: a web link is fetched first; nil puts the kind's back.
---@param link string|nil A web link to a picture, or a picture the table already holds.
function Piece.set_face_picture(link) end

---Changes the text drawn on the piece: any of text, size and color; nil takes the lettering away.
---@param changes table|nil { text =, size =, color = }, each optional.
function Piece.set_lettering(changes) end

---Shows it gliding towards a point or a heading on every screen, pulled and slowed as a module's smooth move is; the piece itself stays where it is until set_pos or set_rot puts it there.
---@param spec PieceShowglideSpec
function Piece.show_glide(spec) end

---Fetches a model and its picture from web links and shows the piece as that model on every screen; a link that gives no model shows the picture flat, or a plain placeholder.
---@param spec PieceSetwebmodelSpec
function Piece.set_web_model(spec) end

---Plays an effect once on every screen, one round of it, whether it is running or not; false for a piece that is not an effect.
---@param index? number Which one-off it is, from 0, handed on to on_burst; 0 when left out.
function Piece.play_effect(index) end

---Starts or stops an effect.
---@param on boolean true runs it; false stops it.
function Piece.set_effect_on(on) end

---Sets what the piece counts for, and optionally the groups it adds up with.
---@param worth number A whole number.
---@param groups? number Groups as bits; nil keeps them.
function Piece.set_worth(worth, groups) end

---Rolls a die, or turns a piece of several states to one at random; false for anything else.
---@return boolean
function Piece.roll() end

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
---@param keep_script boolean Keeps the script it runs now as its own instead of taking the new kind's; optional.
function Piece.set_def(def, keep_script) end

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

---Opens the window of what it holds on that umpire's screen, as if they had opened it; false when it holds nothing by its kind or the umpire is not here.
---@param umpire Who Whose screen: an entry of game.umpires().
---@param most? number Show only the first this many; leave it out for all.
function Piece.show_contents(umpire, most) end

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

---Draws an input box and returns its index. on_change names a function called as fn(who, value, index, piece, typing) as the umpire types and when they leave the box.
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

---Shows a see-through ray, sphere or box sliding from one point to another on every umpire's screen, to check where a script is looking.
---@param spec BoardShowcastSpec
function board.show_cast(spec) end

---The kinds this game's packs hold, or those that match { link =, type =, tag = }: { id =, name =, tags =, source = }.
---@param filter? BoardKindsOptions
---@return table[] # A list; empty when nothing matches. source is the kind's module source, as a piece's source field gives it, or nil.
function board.kinds(filter) end

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

---Downloads a picture (PNG, JPEG, WebP or BMP, up to 16 MB) from the web on the host, for set_decals. fn(picture) gets its name, or nil when the link gave none. Links to the host PC or its local network give none.
---@param link string An http or https link.
---@param fn function Called as fn(picture) once the download ends.
function game.fetch_picture(link, fn) end

---@class KTUi
---@field loading boolean true from setting panels until they land, and while panels that did not read are in place.
---@field assetType table { Image = 0, AssetBundle = 1 }, also as image and assetBundle, as module scripts name picture types.
ui = {}

---Replaces the panels: XML (several roots allowed) or a list of tables { tag =, attributes = {}, children = {}, value = }. They land on the next frame.
---@param xml_or_list string|UiNode[] XML, or a list of element tables; a single table that is not a list is refused.
---@param pictures? table { name =, url = } entries, as ui.set_pictures takes, set at once.
---@return boolean # true when the panels were taken; false for nil or a table that is not a list.
function ui.set(xml_or_list, pictures) end

---The panels as they were written, with the changes made since: each element on its own lines.
---@return string
function ui.xml() end

---The panels as tables.
---@return UiNode[] # A list of { tag =, attributes = {}, children = {}, value = } tables.
function ui.tree() end

---Changes one attribute of an element; it lands when the call ends.
---@param id string The element's id, in any letter case.
---@param name string The attribute, as in the XML (text, color, active, percentage…). class changes only what ui.xml gives back: use ui.set_class.
---@param value string|number|boolean The value; true and false are written True and False.
---@return boolean # true when the element is there now and both name and value are given.
function ui.set_attribute(id, name, value) end

---Changes several attributes at once.
---@param id string The element's id.
---@param table table Attribute → value.
---@return boolean # true when the element is there now.
function ui.set_attributes(id, table) end

---Reads an attribute of the element as it stands, with what the panels' Defaults give it, or nil.
---@param id string The element's id.
---@param name string The attribute.
---@return string|nil
function ui.get_attribute(id, name) end

---Shows an element.
---@param id string The element's id.
---@return boolean # true when the element is there now.
function ui.show(id) end

---Hides an element; one inside a hidden element stays as it is.
---@param id string The element's id.
---@return boolean # true when the element is there now.
function ui.hide(id) end

---Sets the value the panels hold between an element's tags; a Text element shows it as its words.
---@param id string The element's id.
---@param value string|number|boolean The new value.
---@return boolean # true when the element is there now and a value is given.
function ui.set_value(id, value) end

---Reads the value held between an element's tags, or nil.
---@param id string The element's id, in its exact letter case.
---@return string|nil
function ui.get_value(id) end

---Every attribute of the element as it stands, or nil.
---@param id string The element's id.
---@return table|nil
function ui.get_attributes(id) end

---Sets an element's classes, which take their looks from the Defaults the panels hold.
---@param id string The element's id.
---@param classes string Class names, separated by spaces.
---@return boolean # true when the element is there now.
function ui.set_class(id, classes) end

---The pictures panels name: a list of { type =, name =, url = }.
---@return table
function ui.pictures() end

---Replaces the pictures panels name, at once, and builds the panels again from what they hold.
---@param list table { name =, url =, type = } entries; url is a picture of a pack.
---@return boolean
function ui.set_pictures(list) end

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
---@return boolean # true when a timer was waiting and is stopped now, false when there was none.
function wait.stop(handle) end

---The scripts' clock: seconds since the table started running scripts.
---@return number
function wait.now() end

---How many frames the scripts have run since the table started.
---@return number
function wait.frame() end

---Seconds between the last frame and the one before it.
---@return number
function wait.delta() end

---@class KTJson
json = {}

---A value as JSON. A table whose keys are all positive numbers is an array up to its highest key, gaps as null; an empty table is []; other tables are objects with their keys in sorted order, number keys after the text ones.
---@param value any A table, string, number, boolean or nil. A function, a table holding itself or a key that is neither text nor a number is an error.
---@param pretty? boolean true: one key a line, indented by two spaces.
---@return string
function json.encode(value, pretty) end

---JSON as a value; an error when it does not parse. Text after the first value is ignored, and blank text is nil.
---@param text string The JSON. Anything else is an error.
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
function on_drop() end

---An umpire rolled this die.
---@param who Who The umpire.
function on_roll(who) end

---An umpire picked the piece up.
---@param who Who The umpire.
function on_pick_up(who) end

---Its state changed, by anyone.
function on_state() end

---This book turned to another page.
function on_page() end

---An umpire or a script started (0) or stopped (-1) this effect.
function on_effect() end

---This effect was played once, from its menu (index 0) or by a script's play_effect (the index it gave).
---@param old number The state it left, from 1.
---@param new number The state it is in now, from 1.
---@param old_guid string The module GUID of the state it left, for a piece converted from a module; its own GUID otherwise.
function on_burst(old, new, old_guid) end

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

