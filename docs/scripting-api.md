# Scripting API reference

Every name a script can use: parameters, returns, an example, and the module call it replaces. How scripts work: the [scripting guide](scripting.md). **F1** on a word in a script editor opens its entry.

API version 1. Generated from `src/Server/Scripting/ApiDoc.cs` by `dotnet run --project tools/Converter -- api-reference`.

## Contents

- [Conventions](#conventions)
- [The globals](#the-globals)
- [A piece: fields](#a-piece-fields)
- [A piece: changes](#a-piece-changes)
- [A piece: widgets and its menu](#a-piece-widgets-and-its-menu)
- [A piece: other scripts](#a-piece-other-scripts)
- [board](#board)
- [game](#game)
- [ui](#ui)
- [wait](#wait)
- [json](#json)
- [Events of a piece script](#events-of-a-piece-script)
- [Events of the global script](#events-of-the-global-script)
- [Lua's own library](#luas-own-library)
- [Module calls](#module-calls)
- [Compatibility](#compatibility)

## Conventions

Calls use a dot, not a colon: `self.set_name("Guard")`. _Optional_ parameters may be left out. Calls that take a table use braces: `board.spawn{ def = "…", x = 10, z = 4 }`.

| Type | What it is |
|---|---|
| `Piece` | A piece on the umpire board: `self`, or what `board.find`, `board.pieces` and `board.spawn` give back. Its fields are read; its `set_` calls change it. |
| `Who` | An umpire: `{ id =, name =, role = }`, role `"host"`, `"lead"` or `"umpire"`. Handlers get one as `who`. |
| `Point` | A place on the map: `{ x =, z = }` in map units. |
| `Size` | `{ w =, h = }` in map units. |
| `Color` | `"#RRGGBB"`, `"#RRGGBBAA"`, a name (`black`, `white`, `red`, `green`, `blue`, `yellow`, `orange`, `purple`, `pink`, `brown`, `teal`, `grey`) or `{ r, g, b, a }` from 0 to 1. |
| `Line` | `{ points = { Point, … }, color = Color, width = number, loop = boolean }`, points in piece units. |
| `UiNode` | A panel element as a table: `{ tag =, attributes = {}, children = {}, value = }`. |
| `T[]` | A list (a table with keys 1..n) of T. |
| `T?` | A T, or nil. |

**Units.** _Map units_ are the table's own (`pos`, `set_pos`, `spawn`). _Piece units_ are map units measured on the piece's kind before the table scales it, centre (0, 0), x to the right, z down the picture (widgets, lines, `to_world`): a kind 1.2 wide runs from x = -0.6 to 0.6, and a piece scaled up carries its widgets with it. _Metres_ come from the map's scale (`board.distance`, `game.calibration`).

**Handler names.** `"fn"` is looked up in the script of the piece it is drawn on, then the global script; `"global:fn"` forces the global script; `"<id or GUID>/fn"` is that piece's.

**Errors** stop the current call and show in the console with the piece and line. The script stays loaded.

## The globals

Every script sees these names. A piece's script has `self`; the global script (Scenario properties → Script) has none.

| Name | What it does |
|---|---|
| [`self`](#self) | The piece this script runs on. nil in the global script. |
| [`print`](#print) | Writes its arguments to the script console, separated by tabs. |
| [`log`](#log) | Writes a value to the script console, a table written out a few levels deep; the label goes in front. |
| [`board`](#board) | The umpire board: find, list and spawn pieces, measure, read the zones. |
| [`game`](#game) | The game: the clock, the umpires, the Log, dice, hotkeys, the table's menu, dialogs. |
| [`ui`](#ui) | The screen panels every umpire sees, written in XML. |
| [`wait`](#wait) | Timers: seconds, frames, a condition. |
| [`json`](#json) | JSON in and out, numbers written the same on every PC. |
| [`compat`](#compat) | The compatibility library imported scripts call; written over this API (see the guide). |

### `self`

```lua
self
```

The piece this script runs on. nil in the global script.

**Type** `Piece`, read-only.

A script on a piece kind runs once for every piece of that kind on the table, and each run has its own `self` and its own globals.

**Module call** `self` · **See also** [`board.find`](#boardfind), [`board.pieces`](#boardpieces)

### `print`

```lua
print(...)
```

Writes its arguments to the script console, separated by tabs.

| Parameter | Type | Description |
|---|---|---|
| `...` | `any` | _Optional._ The values to write; each is written as `tostring` would. |

```lua
print("strength", self.counters.Strength, "at", self.pos.x, self.pos.z)
```

The console shows the line with the piece and the script it came from. Observers never see it.

**Module call** `print` · **See also** [`log`](#log)

### `log`

```lua
log(value, label)
```

Writes a value to the script console, a table written out a few levels deep; the label goes in front.

| Parameter | Type | Description |
|---|---|---|
| `value` | `any` | The value to write. A table is written out with its keys, a few levels deep. |
| `label` | `string` | _Optional._ Written in front of the value, followed by a colon. |

```lua
log(self.pos, "where")   -- where: { x = 12.5, z = -3 }
```

**Module call** `log` · **See also** [`print`](#print)

### `board`

```lua
board
```

The umpire board: find, list and spawn pieces, measure, read the zones.

**Type** `table`, read-only.

**See also** [`board.find`](#boardfind), [`board.pieces`](#boardpieces), [`board.spawn`](#boardspawn)

### `game`

```lua
game
```

The game: the clock, the umpires, the Log, dice, hotkeys, the table's menu, dialogs.

**Type** `table`, read-only.

**See also** [`game.say`](#gamesay), [`game.roll`](#gameroll), [`game.hotkey`](#gamehotkey)

### `ui`

```lua
ui
```

The screen panels every umpire sees, written in XML.

**Type** `table`, read-only.

**Module call** `UI` · **See also** [`ui.set`](#uiset)

### `wait`

```lua
wait
```

Timers: seconds, frames, a condition.

**Type** `table`, read-only.

**Module call** `Wait` · **See also** [`wait.seconds`](#waitseconds)

### `json`

```lua
json
```

JSON in and out, numbers written the same on every PC.

**Type** `table`, read-only.

**Module call** `JSON` · **See also** [`json.encode`](#jsonencode), [`json.decode`](#jsondecode)

### `compat`

```lua
compat
```

The compatibility library imported scripts call; written over this API (see the guide).

**Type** `table`, read-only.

Write new scripts against this API, not against `compat`: it exists so that translated module scripts keep their shape.

## A piece: fields

Read from `self` or from any piece `board.find` or `board.pieces` hands back. Fields are read-only: assigning to one is an error. The `set_` calls change them.

| Name | What it does |
|---|---|
| [`self.id`](#selfid) | Our id for the piece (p-000123). Never changes. |
| [`self.guid`](#selfguid) | The GUID its module gave it; a piece made here answers with its id. board.find takes either. |
| [`self.def`](#selfdef) | The id of its piece definition (its kind). |
| [`self.pack`](#selfpack) | The id of the pack its kind comes from. |
| [`self.name`](#selfname) | The label shown under the piece, or nil. |
| [`self.note`](#selfnote) | Its description, shown on hover; umpires only. nil when empty. |
| [`self.state`](#selfstate) | Its state, from 1. |
| [`self.states`](#selfstates) | The names of its kind's states, in order. |
| [`self.faces`](#selffaces) | A die's sides in order: { value =, up = { x, y, z }, rotation = { x, y, z } }; empty for every other piece. |
| [`self.face`](#selfface) | The side of a die showing, from 1; nil for every other piece. |
| [`self.value`](#selfvalue) | The value of the side of a die showing; nil for every other piece. |
| [`self.picked_up`](#selfpicked_up) | Where an umpire last picked the piece up: { x =, y =, z =, rot = }; nil until then. |
| [`self.face_picture`](#selfface_picture) | A picture shown on the piece's face in place of its kind's, set by set_face_picture; nil for none. |
| [`self.page_offset`](#selfpage_offset) | For a book, what its page numbers start from, as the module set it; 0 for every other piece. |
| [`self.effect_on`](#selfeffect_on) | For an effect, whether it is running. |
| [`self.effect_smoke`](#selfeffect_smoke) | For an effect, how thick its smoke is: -2 very fine, -1 fine, 0 standard, 1 dense, 2 very dense, or anywhere between; nil for every other piece. |
| [`self.kind_name`](#selfkind_name) | The name of its kind. |
| [`self.lettering`](#selflettering) | Text drawn on the piece in place of its picture, as 3D text from a module is: { text =, size =, color = }; nil for most pieces. |
| [`self.worth`](#selfworth) | What the piece counts for when pieces are added up; 0 for most. |
| [`self.worth_groups`](#selfworth_groups) | Which groups it adds up with, as bits; 0 adds up with any piece of worth. |
| [`self.pos`](#selfpos) | Where it stands, in map units: { x =, z = }. |
| [`self.rot`](#selfrot) | Its heading in degrees. |
| [`self.y`](#selfy) | How high it sits above the table (stacked pieces). |
| [`self.scale`](#selfscale) | Its size multiplier on the table. |
| [`self.size`](#selfsize) | Its size on the map at its state and scale: { w =, h = }. |
| [`self.middle`](#selfmiddle) | The middle of its footprint, in map units: { x =, z = }. The same as `pos`, except for a model drawn off its own centre. |
| [`self.picture_size`](#selfpicture_size) | Its picture's width and height in pixels at its state, or nil without a picture. |
| [`self.local_unit`](#selflocal_unit) | For a model that came from a module, what one unit of its script's local frame is in piece units: { x =, z = }; nil for every other piece. |
| [`self.shape`](#selfshape) | Its outline at its state: "box", "rounded", "circle", "hex", or "alpha" for a token cut along its picture. |
| [`self.face_down`](#selfface_down) | Turned over. |
| [`self.tilt_x`](#selftilt_x) | How far it leans about its own left-right axis, in degrees from -180 to 180, before its heading turns it; 0 for a piece lying flat. |
| [`self.tilt_z`](#selftilt_z) | How far it leans about its own front-back axis, in degrees from -180 to 180; 0 for a piece lying flat. |
| [`self.model`](#selfmodel) | true for a 3D model, false for a flat piece. |
| [`self.interactable`](#selfinteractable) | Whether hands can take it (set_interactable). |
| [`self.held_by`](#selfheld_by) | The umpire carrying it now, or nil. |
| [`self.source`](#selfsource) | For a kind converted from a module: { type =, links = { image =, mesh =, … }, top =, origin =, rise =, tag =, tags = } — the module object's type, its picture and model links, its top surface in its own units, for a flat piece the height of its own origin above the base and the piece units one of its own units rises, the kind of object it was (Tile, Figurine, Board, Bag…) and its own tags; for a piece of several states, `states` lists each state's { type, guid, description, gm_notes, memo, script_state, tags, script } as the module kept them; nil otherwise. |
| [`self.highlight`](#selfhighlight) | The colour it is ringed in, or nil (set_highlight). |
| [`self.book_highlight`](#selfbook_highlight) | For a book, the box marked on the page it is open at: { left =, bottom =, right =, top = }, each from 0 to 1 across the page from its lower left corner; nil for none. Turning the page clears it. |
| [`self.holds`](#selfholds) | Whether it is a holder that other pieces can be stowed in: a piece holding some, one made a holder, or a kind tagged bag. |
| [`self.locked`](#selflocked) | Pinned to the table: not dragged, turned or resized by hand. |
| [`self.sticky`](#selfsticky) | Carries the pieces resting on it when it is moved; attached pieces go with it either way. |
| [`self.box_selected`](#selfbox_selected) | Taken by a selection box dragged over it. |
| [`self.measures_moves`](#selfmeasures_moves) | Shows how far it is moved while it is held. |
| [`self.tint`](#selftint) | Its colour as "#RRGGBB", or nil. |
| [`self.counters`](#selfcounters) | Its kind's counters, name → value. |
| [`self.text`](#selftext) | Its text lines that hold a value, name → text. |
| [`self.attached`](#selfattached) | The pieces riding on it. |
| [`self.tags`](#selftags) | Its own tags, or its kind's when it has none of its own. |
| [`self.gm_notes`](#selfgm_notes) | The umpire's notes a module saved on the piece; empty when it had none. |
| [`self.memo`](#selfmemo) | A string the scripts keep on the piece; saved with the game. |
| [`self.script_state`](#selfscript_state) | What its on_save last returned. |
| [`self.script`](#selfscript) | The script it runs (its own, or its kind's), or nil. |
| [`self.ui`](#selfui) | Its own panels, drawn on its face: the same calls as the global ui. |
| [`self.lines`](#selflines) | The lines set_lines drew round it. |
| [`self.decals`](#selfdecals) | The pictures on its faces: the ones its look carries, or those set_decals put there. |
| [`self.exists`](#selfexists) | False once the piece is gone (its other fields are nil then). |

### `self.id`

```lua
self.id
```

Our id for the piece (p-000123). Never changes.

**Type** `string`, read-only.

**See also** [`board.find`](#boardfind)

### `self.guid`

```lua
self.guid
```

The GUID its module gave it; a piece made here answers with its id. board.find takes either.

**Type** `string`, read-only.

A piece a script spawns gets a fresh six-digit GUID unless the spawn named a free one.

**Module call** `getGUID`

### `self.def`

```lua
self.def
```

The id of its piece definition (its kind).

**Type** `string`, read-only.

**See also** [`self.set_def`](#selfset_def), [`board.pieces`](#boardpieces)

### `self.pack`

```lua
self.pack
```

The id of the pack its kind comes from.

**Type** `string`, read-only.

### `self.name`

```lua
self.name
```

The label shown under the piece, or nil.

**Type** `string?`, read-only.

**Module call** `getName` · **See also** [`self.set_name`](#selfset_name)

### `self.note`

```lua
self.note
```

Its description, shown on hover; umpires only. nil when empty.

**Type** `string?`, read-only.

**Module call** `getDescription` · **See also** [`self.set_note`](#selfset_note)

### `self.state`

```lua
self.state
```

Its state, from 1.

**Type** `number`, read-only.

**Module call** `getStateId` · **See also** [`self.set_state`](#selfset_state), [`self.states`](#selfstates), [`on_state`](#on_state)

### `self.states`

```lua
self.states
```

The names of its kind's states, in order.

**Type** `string[]`, read-only.

```lua
for i, name in ipairs(self.states) do
  print(i, name, i == self.state and "(now)" or "")
end
```

**Module call** `getStates`

### `self.faces`

```lua
self.faces
```

A die's sides in order: { value =, up = { x, y, z }, rotation = { x, y, z } }; empty for every other piece.

**Type** `Face[]`, read-only.

**Module call** `getRotationValues`

### `self.face`

```lua
self.face
```

The side of a die showing, from 1; nil for every other piece.

**Type** `number?`, read-only.

**Module call** `getValue`

### `self.value`

```lua
self.value
```

The value of the side of a die showing; nil for every other piece.

**Type** `number|string|nil`, read-only.

**Module call** `getRotationValue`

### `self.picked_up`

```lua
self.picked_up
```

Where an umpire last picked the piece up: { x =, y =, z =, rot = }; nil until then.

**Type** `table?`, read-only.

**Module call** `pick_up_position`

### `self.face_picture`

```lua
self.face_picture
```

A picture shown on the piece's face in place of its kind's, set by set_face_picture; nil for none.

**Type** `string?`, read-only.

### `self.page_offset`

```lua
self.page_offset
```

For a book, what its page numbers start from, as the module set it; 0 for every other piece.

**Type** `number`, read-only.

**Module call** `Book.page_offset`

### `self.effect_on`

```lua
self.effect_on
```

For an effect, whether it is running.

**Type** `boolean`, read-only.

### `self.effect_smoke`

```lua
self.effect_smoke
```

For an effect, how thick its smoke is: -2 very fine, -1 fine, 0 standard, 1 dense, 2 very dense, or anywhere between; nil for every other piece.

**Type** `number?`, read-only.

### `self.kind_name`

```lua
self.kind_name
```

The name of its kind.

**Type** `string`, read-only.

### `self.lettering`

```lua
self.lettering
```

Text drawn on the piece in place of its picture, as 3D text from a module is: { text =, size =, color = }; nil for most pieces.

**Type** `table?`, read-only.

**Module call** `TextTool.getValue`

### `self.worth`

```lua
self.worth
```

What the piece counts for when pieces are added up; 0 for most.

**Type** `number`, read-only.

**Module call** `value`

### `self.worth_groups`

```lua
self.worth_groups
```

Which groups it adds up with, as bits; 0 adds up with any piece of worth.

**Type** `number`, read-only.

**Module call** `value_flags`

### `self.pos`

```lua
self.pos
```

Where it stands, in map units: { x =, z = }.

**Type** `Point`, read-only.

Each read gives a new table; changing it moves nothing. Use `set_pos` or `move`.

**Module call** `getPosition` · **See also** [`self.set_pos`](#selfset_pos), [`self.move`](#selfmove), [`board.distance`](#boarddistance)

### `self.rot`

```lua
self.rot
```

Its heading in degrees.

**Type** `number`, read-only.

**Module call** `getRotation` · **See also** [`self.set_rot`](#selfset_rot)

### `self.y`

```lua
self.y
```

How high it sits above the table (stacked pieces).

**Type** `number`, read-only.

### `self.scale`

```lua
self.scale
```

Its size multiplier on the table.

**Type** `number`, read-only.

**Module call** `getScale` · **See also** [`self.set_scale`](#selfset_scale), [`self.size`](#selfsize)

### `self.size`

```lua
self.size
```

Its size on the map at its state and scale: { w =, h = }.

**Type** `Size`, read-only.

A state with a size of its own gives that size; otherwise the kind's. Both are multiplied by `scale`.

**Module call** `getBounds`

### `self.middle`

```lua
self.middle
```

The middle of its footprint, in map units: { x =, z = }. The same as `pos`, except for a model drawn off its own centre.

**Type** `Point`, read-only.

**Module call** `getBounds`

### `self.picture_size`

```lua
self.picture_size
```

Its picture's width and height in pixels at its state, or nil without a picture.

**Type** `Size?`, read-only.

### `self.local_unit`

```lua
self.local_unit
```

For a model that came from a module, what one unit of its script's local frame is in piece units: { x =, z = }; nil for every other piece.

**Type** `Size?`, read-only.

### `self.shape`

```lua
self.shape
```

Its outline at its state: "box", "rounded", "circle", "hex", or "alpha" for a token cut along its picture.

**Type** `string`, read-only.

### `self.face_down`

```lua
self.face_down
```

Turned over.

**Type** `boolean`, read-only.

**Module call** `is_face_down`

### `self.tilt_x`

```lua
self.tilt_x
```

How far it leans about its own left-right axis, in degrees from -180 to 180, before its heading turns it; 0 for a piece lying flat.

**Type** `number`, read-only.

### `self.tilt_z`

```lua
self.tilt_z
```

How far it leans about its own front-back axis, in degrees from -180 to 180; 0 for a piece lying flat.

**Type** `number`, read-only.

### `self.model`

```lua
self.model
```

true for a 3D model, false for a flat piece.

**Type** `boolean`, read-only.

### `self.interactable`

```lua
self.interactable
```

Whether hands can take it (set_interactable).

**Type** `boolean`, read-only.

**Module call** `interactable`

### `self.held_by`

```lua
self.held_by
```

The umpire carrying it now, or nil.

**Type** `Who`, read-only.

**Module call** `held_by_color`

### `self.source`

```lua
self.source
```

For a kind converted from a module: { type =, links = { image =, mesh =, … }, top =, origin =, rise =, tag =, tags = } — the module object's type, its picture and model links, its top surface in its own units, for a flat piece the height of its own origin above the base and the piece units one of its own units rises, the kind of object it was (Tile, Figurine, Board, Bag…) and its own tags; for a piece of several states, `states` lists each state's { type, guid, description, gm_notes, memo, script_state, tags, script } as the module kept them; nil otherwise.

**Type** `table`, read-only.

**Module call** `name`

### `self.highlight`

```lua
self.highlight
```

The colour it is ringed in, or nil (set_highlight).

**Type** `string`, read-only.

**Module call** `highlightOn`

### `self.book_highlight`

```lua
self.book_highlight
```

For a book, the box marked on the page it is open at: { left =, bottom =, right =, top = }, each from 0 to 1 across the page from its lower left corner; nil for none. Turning the page clears it.

**Type** `table`, read-only.

**Module call** `Book.setHighlight`

### `self.holds`

```lua
self.holds
```

Whether it is a holder that other pieces can be stowed in: a piece holding some, one made a holder, or a kind tagged bag.

**Type** `boolean`, read-only.

**Module call** `getQuantity` · **See also** [`self.flip`](#selfflip), [`on_flip`](#on_flip)

### `self.locked`

```lua
self.locked
```

Pinned to the table: not dragged, turned or resized by hand.

**Type** `boolean`, read-only.

**Module call** `getLock` · **See also** [`self.set_locked`](#selfset_locked)

### `self.sticky`

```lua
self.sticky
```

Carries the pieces resting on it when it is moved; attached pieces go with it either way.

**Type** `boolean`, read-only.

**Module call** `sticky` · **See also** [`self.set_sticky`](#selfset_sticky)

### `self.box_selected`

```lua
self.box_selected
```

Taken by a selection box dragged over it.

**Type** `boolean`, read-only.

**Module call** `drag_selectable` · **See also** [`self.set_box_selected`](#selfset_box_selected)

### `self.measures_moves`

```lua
self.measures_moves
```

Shows how far it is moved while it is held.

**Type** `boolean`, read-only.

**Module call** `measure_movement` · **See also** [`self.set_measures_moves`](#selfset_measures_moves)

### `self.tint`

```lua
self.tint
```

Its colour as "#RRGGBB", or nil.

**Type** `string?`, read-only.

**Module call** `getColorTint` · **See also** [`self.set_tint`](#selfset_tint)

### `self.counters`

```lua
self.counters
```

Its kind's counters, name → value.

**Type** `table`, read-only.

```lua
local s = self.counters.Strength
if s and s <= 0 then self.set_state("Routed") end
```

Every counter the kind defines is there, at its default until something sets it.

**See also** [`self.set_counter`](#selfset_counter)

### `self.text`

```lua
self.text
```

Its text lines that hold a value, name → text.

**Type** `table`, read-only.

**See also** [`self.set_text`](#selfset_text)

### `self.attached`

```lua
self.attached
```

The pieces riding on it.

**Type** `Piece[]`, read-only.

**Module call** `getAttachments` · **See also** [`self.attach`](#selfattach), [`self.detach`](#selfdetach)

### `self.tags`

```lua
self.tags
```

Its own tags, or its kind's when it has none of its own.

**Type** `string[]`, read-only.

**Module call** `getTags` · **See also** [`self.set_tags`](#selfset_tags), [`self.add_tag`](#selfadd_tag), [`board.pieces`](#boardpieces)

### `self.gm_notes`

```lua
self.gm_notes
```

The umpire's notes a module saved on the piece; empty when it had none.

**Type** `string`, read-only.

**Module call** `getGMNotes`

### `self.memo`

```lua
self.memo
```

A string the scripts keep on the piece; saved with the game.

**Type** `string`, read-only.

The memo travels with the piece: a copy made from its data keeps it. For a script's own state, `on_save` is the usual place.

**Module call** `memo` · **See also** [`self.set_memo`](#selfset_memo)

### `self.script_state`

```lua
self.script_state
```

What its on_save last returned.

**Type** `string`, read-only.

**Module call** `script_state` · **See also** [`on_save`](#on_save), [`self.set_script_state`](#selfset_script_state)

### `self.script`

```lua
self.script
```

The script it runs (its own, or its kind's), or nil.

**Type** `string?`, read-only.

**Module call** `getLuaScript` · **See also** [`self.set_script`](#selfset_script)

### `self.ui`

```lua
self.ui
```

Its own panels, drawn on its face: the same calls as the global ui.

**Type** `table`, read-only.

The root element's `width` and `height` are thousandths of a module's local unit; see the guide, Panels on a piece.

**Module call** `UI` · **See also** [`ui.set`](#uiset)

### `self.lines`

```lua
self.lines
```

The lines set_lines drew round it.

**Type** `Line[]`, read-only.

**Module call** `getVectorLines` · **See also** [`self.set_lines`](#selfset_lines)

### `self.decals`

```lua
self.decals
```

The pictures on its faces: the ones its look carries, or those set_decals put there.

**Type** `Decal[]`, read-only.

**Module call** `getDecals` · **See also** [`self.set_decals`](#selfset_decals), [`self.state`](#selfstate)

### `self.exists`

```lua
self.exists
```

False once the piece is gone (its other fields are nil then).

**Type** `boolean`, read-only.

A handle kept in a variable outlives the piece. Check `exists` before using one kept across turns; a `set_` call on a piece that is gone is an error.

**Module call** `isDestroyed`

## A piece: changes

Each call is a change every umpire sees. What a script changes in answer to an umpire's action is one step of that umpire's undo. What scripts change by themselves (a timer, a wait, on_update, on_load) the host and lead umpires take back with Ctrl+Z, all of it between two of their own changes as one step.

| Name | What it does |
|---|---|
| [`self.set_name`](#selfset_name) | Sets the label under the piece; nil takes it away. |
| [`self.set_note`](#selfset_note) | Sets its description; nil takes it away. |
| [`self.set_state`](#selfset_state) | Changes its state: a number from 1, or a state's name. What the scripts drew belongs to the state it was drawn on. |
| [`self.set_pos`](#selfset_pos) | Moves it, in map units; its heading stays. A place { x, z } or another piece works in place of x and z. |
| [`self.set_rot`](#selfset_rot) | Turns it to a heading in degrees. |
| [`self.move`](#selfmove) | Moves and turns it in one change. A place { x, z } or another piece works in place of x and z: move(place, rot). |
| [`self.set_scale`](#selfset_scale) | Sets its size multiplier. |
| [`self.set_tilt`](#selfset_tilt) | Leans it under its heading: z degrees about its front-back axis, then x about its left-right axis, as a module rotation turns an object; a face-down piece leans turned over. set_tilt(0, 0) lays it flat again. Undo takes it back. |
| [`self.flip`](#selfflip) | Turns it over; with no argument, to the other side. |
| [`self.set_tint`](#selfset_tint) | Tints its sides: "#RRGGBB", a name, or { r, g, b }. nil goes back to its kind's. |
| [`self.set_locked`](#selfset_locked) | Pins it to the table, or lets it go. |
| [`self.set_box_selected`](#selfset_box_selected) | Whether a selection box dragged over it takes it. |
| [`self.set_measures_moves`](#selfset_measures_moves) | Whether it shows how far it is moved while it is held. |
| [`self.set_interactable`](#selfset_interactable) | Whether hands can take it: off, it cannot be picked up, dragged, selected or opened, though its buttons still press. |
| [`self.set_highlight`](#selfset_highlight) | Rings it in a colour on every screen; nil takes the ring away. |
| [`self.set_book_highlight`](#selfset_book_highlight) | Marks a box on the page a book is open at, in its reader on every screen; nil clears it. Turning the page clears it too. |
| [`self.set_sticky`](#selfset_sticky) | Whether it carries the pieces resting on it when it is moved. |
| [`self.set_face`](#selfset_face) | Turns a die to show a side, from 1; false when it has no such side. |
| [`self.set_value`](#selfset_value) | Turns a die to the first side with this value; false when no side has it. |
| [`self.set_faces`](#selfset_faces) | Gives this die sides of its own; nil gives it back its kind's. |
| [`self.set_face_picture`](#selfset_face_picture) | Shows a picture on the piece's face in place of its kind's: a web link is fetched first; nil puts the kind's back. |
| [`self.set_lettering`](#selfset_lettering) | Changes the text drawn on the piece: any of text, size and color; nil takes the lettering away. |
| [`self.show_glide`](#selfshow_glide) | Shows it gliding towards a point or a heading on every screen, pulled and slowed as a module's smooth move is; the piece itself stays where it is until set_pos or set_rot puts it there. |
| [`self.set_web_model`](#selfset_web_model) | Fetches a model and its picture from web links and shows the piece as that model on every screen; a link that gives no model shows the picture flat, or a plain placeholder. |
| [`self.play_effect`](#selfplay_effect) | Plays an effect once on every screen, one round of it, whether it is running or not; false for a piece that is not an effect. |
| [`self.set_effect_on`](#selfset_effect_on) | Starts or stops an effect. |
| [`self.set_effect_smoke`](#selfset_effect_smoke) | Makes an effect's smoke finer or denser: fine leaves the pieces under it readable, dense hangs and drifts downwind as far as real powder smoke. |
| [`self.set_worth`](#selfset_worth) | Sets what the piece counts for, and optionally the groups it adds up with. |
| [`self.roll`](#selfroll) | Rolls a die, or turns a piece of several states to one at random; false for anything else. |
| [`self.set_counter`](#selfset_counter) | Sets one of its kind's counters (a whole number, kept to the counter's range). |
| [`self.set_text`](#selfset_text) | Sets one of its kind's text lines. |
| [`self.set_tags`](#selfset_tags) | Gives it its own tags; nil goes back to its kind's. |
| [`self.add_tag`](#selfadd_tag) | Adds a tag to its own. |
| [`self.remove_tag`](#selfremove_tag) | Takes a tag away. |
| [`self.set_memo`](#selfset_memo) | Sets its memo. |
| [`self.set_script_state`](#selfset_script_state) | Sets its saved script state outright (on_save's answer is the usual way). |
| [`self.set_picture`](#selfset_picture) | Wears another kind's picture; it keeps its own size, states, counters and script. nil puts its own back. |
| [`self.set_def`](#selfset_def) | Becomes another kind of piece altogether. |
| [`self.set_script`](#selfset_script) | Gives it a script of its own (nil: back to its kind's). Only from a click or menu handler, or on a piece the same call spawned. |
| [`self.reload`](#selfreload) | Starts its script again from on_load. |
| [`self.attach`](#selfattach) | Puts another piece on this one, to ride with it. |
| [`self.detach`](#selfdetach) | Takes this piece off the one it rides on. |
| [`self.stowed`](#selfstowed) | The pieces it holds off the table, first in first: { id =, name =, guid =, def =, held = }, held being how many that piece holds in turn. |
| [`self.show_contents`](#selfshow_contents) | Opens the window of what it holds on that umpire's screen, as if they had opened it; false when it holds nothing by its kind or the umpire is not here. |
| [`self.stow`](#selfstow) | Takes another piece off the table into this one, with whatever rides on it; a piece this one rides on is refused. |
| [`self.unstow`](#selfunstow) | Puts a held piece back on the table and returns it: which is its id, guid, its number in stowed, or nothing for the next by the holder's take order (first, last or random); place is { x =, z = } or a piece, else beside this one; rot turns it. |
| [`self.ready`](#selfready) | Its ready piece, the one held piece it releases and recalls with one key, held or out on the table; nil for none. |
| [`self.set_ready`](#selfset_ready) | Makes a piece it holds its ready piece, first among what it holds; nil takes the mark off. |
| [`self.release`](#selfrelease) | Puts its ready piece on the table where it stood when last recalled, turned and moved with this one; beside this one the first time. False when it is not held. |
| [`self.recall`](#selfrecall) | Takes its ready piece back from the table into this one and remembers where it stood. False when it is not out. |
| [`self.delete`](#selfdelete) | Takes the piece off the table (on_destroy runs first). |
| [`self.set_lines`](#selfset_lines) | Draws lines round the piece that move and turn with it: { { points = { {x=, z=}, … }, color =, width =, loop = }, … } in piece units; width in map units. |
| [`self.set_decals`](#selfset_decals) | Puts pictures on the piece's faces that move, turn and flip with it: { { picture =, x =, z =, w =, h =, rot =, face =, name = }, … } in piece units. |
| [`self.to_world`](#selfto_world) | A point in piece units (centre 0, x right, z down the picture) as a map point { x =, z = }. |
| [`self.to_local`](#selfto_local) | A map point as piece units. |

### `self.set_name`

```lua
self.set_name(text)
```

Sets the label under the piece; nil takes it away.

| Parameter | Type | Description |
|---|---|---|
| `text` | `string?` | _Optional._ The label. nil, or no argument, takes it away. |

```lua
self.set_name("2nd Bn, 95th Rifles")
```

**Module call** `setName` · **See also** [`self.name`](#selfname), [`self.set_note`](#selfset_note)

### `self.set_note`

```lua
self.set_note(text)
```

Sets its description; nil takes it away.

| Parameter | Type | Description |
|---|---|---|
| `text` | `string?` | _Optional._ The description, shown to umpires on hover. nil takes it away. |

**Module call** `setDescription` · **See also** [`self.note`](#selfnote), [`self.set_name`](#selfset_name)

### `self.set_state`

```lua
self.set_state(n_or_name)
```

Changes its state: a number from 1, or a state's name. What the scripts drew belongs to the state it was drawn on.

| Parameter | Type | Description |
|---|---|---|
| `n_or_name` | `number\|string` | A state number from 1, or a state's name (case does not matter). |

```lua
self.set_state(2)
self.set_state("Shattered")
```

A name or number the kind does not have is an error.

Widgets, menu rows and lines a script draws belong to the state they were drawn in; see the guide, States.

**Module call** `setState` · **See also** [`self.state`](#selfstate), [`self.states`](#selfstates), [`on_state`](#on_state)

### `self.set_pos`

```lua
self.set_pos(x, z)
```

Moves it, in map units; its heading stays. A place { x, z } or another piece works in place of x and z.

| Parameter | Type | Description |
|---|---|---|
| `x` | `number` | _Optional._ The new x in map units; nil keeps the current one. |
| `z` | `number` | _Optional._ The new z in map units; nil keeps the current one. |

```lua
local p = self.pos
self.set_pos(p.x + 10, p.z)
self.set_pos({ x = 120, z = 48 })
```

**Module call** `setPosition` · **See also** [`self.move`](#selfmove), [`self.pos`](#selfpos)

### `self.set_rot`

```lua
self.set_rot(deg)
```

Turns it to a heading in degrees.

| Parameter | Type | Description |
|---|---|---|
| `deg` | `number` | The heading in degrees. |

**Module call** `setRotation` · **See also** [`self.move`](#selfmove), [`self.rot`](#selfrot), [`on_rotate`](#on_rotate)

### `self.move`

```lua
self.move(x, z, rot)
```

Moves and turns it in one change. A place { x, z } or another piece works in place of x and z: move(place, rot).

| Parameter | Type | Description |
|---|---|---|
| `x` | `number` | _Optional._ The new x in map units; nil keeps it. |
| `z` | `number` | _Optional._ The new z in map units; nil keeps it. |
| `rot` | `number` | _Optional._ The new heading in degrees; nil keeps it. |

```lua
self.move(120, 48, 90)
self.move({ x = 120, z = 48 }, 90)
```

**See also** [`self.set_pos`](#selfset_pos), [`self.set_rot`](#selfset_rot)

### `self.set_scale`

```lua
self.set_scale(k)
```

Sets its size multiplier.

| Parameter | Type | Description |
|---|---|---|
| `k` | `number` | The multiplier; 1 is the kind's own size. |

Widgets keep their place and their share of the face: they are drawn in piece units.

**Module call** `setScale` · **See also** [`self.scale`](#selfscale), [`self.size`](#selfsize)

### `self.set_tilt`

```lua
self.set_tilt(x, z)
```

Leans it under its heading: z degrees about its front-back axis, then x about its left-right axis, as a module rotation turns an object; a face-down piece leans turned over. set_tilt(0, 0) lays it flat again. Undo takes it back.

| Parameter | Type | Description |
|---|---|---|
| `x` | `number` | _Optional._ Degrees about its left-right axis. |
| `z` | `number` | _Optional._ Degrees about its front-back axis. |

**See also** [`self.tilt_x`](#selftilt_x), [`self.tilt_z`](#selftilt_z), [`self.flip`](#selfflip)

### `self.flip`

```lua
self.flip(face_down)
```

Turns it over; with no argument, to the other side.

| Parameter | Type | Description |
|---|---|---|
| `face_down` | `boolean` | _Optional._ true for face down, false for face up. Left out, the piece turns to its other side. |

**Module call** `flip` · **See also** [`self.face_down`](#selfface_down), [`on_flip`](#on_flip)

### `self.set_tint`

```lua
self.set_tint(color)
```

Tints its sides: "#RRGGBB", a name, or { r, g, b }. nil goes back to its kind's.

| Parameter | Type | Description |
|---|---|---|
| `color` | `Color?` | _Optional._ The tint. nil goes back to the kind's. |

```lua
self.set_tint("#8a1c1c")
```

**Module call** `setColorTint` · **See also** [`self.tint`](#selftint)

### `self.set_locked`

```lua
self.set_locked(on)
```

Pins it to the table, or lets it go.

| Parameter | Type | Description |
|---|---|---|
| `on` | `boolean` | _Optional._ true, or no argument, pins it; false lets it go. |

A locked piece can still be picked out and read; a selection box leaves it out and the Delete key leaves it be, so it is deleted from its right-click menu, where an umpire can also unlock it.

**Module call** `setLock` · **See also** [`self.locked`](#selflocked)

### `self.set_box_selected`

```lua
self.set_box_selected(on)
```

Whether a selection box dragged over it takes it.

| Parameter | Type | Description |
|---|---|---|
| `on` | `boolean` | _Optional._ true, or no argument, lets a box take it; false leaves it out. |

**Module call** `drag_selectable` · **See also** [`self.box_selected`](#selfbox_selected)

### `self.set_measures_moves`

```lua
self.set_measures_moves(on)
```

Whether it shows how far it is moved while it is held.

| Parameter | Type | Description |
|---|---|---|
| `on` | `boolean` | _Optional._ true, or no argument, shows the distance; false hides it. |

**Module call** `measure_movement` · **See also** [`self.measures_moves`](#selfmeasures_moves)

### `self.set_interactable`

```lua
self.set_interactable(on)
```

Whether hands can take it: off, it cannot be picked up, dragged, selected or opened, though its buttons still press.

| Parameter | Type | Description |
|---|---|---|
| `on` | `boolean` | _Optional._ true, or no argument, lets hands take it; false keeps them off. |

**Module call** `interactable`

### `self.set_highlight`

```lua
self.set_highlight(color)
```

Rings it in a colour on every screen; nil takes the ring away.

| Parameter | Type | Description |
|---|---|---|
| `color` | `Color` | _Optional._ The ring's colour, or nil. |

**Module call** `highlightOn`

### `self.set_book_highlight`

```lua
self.set_book_highlight(box)
```

Marks a box on the page a book is open at, in its reader on every screen; nil clears it. Turning the page clears it too.

| Parameter | Type | Description |
|---|---|---|
| `box` | `table` | _Optional._ { left =, bottom =, right =, top = }, each from 0 to 1 across the page from its lower left corner; nil clears the box. |

**Module call** `Book.setHighlight`

### `self.set_sticky`

```lua
self.set_sticky(on)
```

Whether it carries the pieces resting on it when it is moved.

| Parameter | Type | Description |
|---|---|---|
| `on` | `boolean` | _Optional._ true, or no argument, carries them; false leaves them where they lie. |

**Module call** `sticky` · **See also** [`self.sticky`](#selfsticky)

### `self.set_face`

```lua
self.set_face(n)
```

Turns a die to show a side, from 1; false when it has no such side.

| Parameter | Type | Description |
|---|---|---|
| `n` | `number` | The side, from 1, in the order of self.faces. |

**Returns** `boolean`.

**Module call** `setValue` · **See also** [`self.face`](#selfface), [`self.set_value`](#selfset_value)

### `self.set_value`

```lua
self.set_value(value)
```

Turns a die to the first side with this value; false when no side has it.

| Parameter | Type | Description |
|---|---|---|
| `value` | `number\|string` | The value to show. |

**Returns** `boolean`.

```lua
self.set_value(6)
```

**Module call** `setRotationValue` · **See also** [`self.value`](#selfvalue), [`self.set_face`](#selfset_face)

### `self.set_faces`

```lua
self.set_faces(list)
```

Gives this die sides of its own; nil gives it back its kind's.

| Parameter | Type | Description |
|---|---|---|
| `list` | `table?` | { value =, up = { x, y, z } } or { value =, rotation = { x, y, z } } for each side. |

**Returns** `boolean`.

**Module call** `setRotationValues` · **See also** [`self.faces`](#selffaces)

### `self.set_face_picture`

```lua
self.set_face_picture(link)
```

Shows a picture on the piece's face in place of its kind's: a web link is fetched first; nil puts the kind's back.

| Parameter | Type | Description |
|---|---|---|
| `link` | `string?` | A web link to a picture, or a picture the table already holds. |

**Module call** `setCustomObject` · **See also** [`self.face_picture`](#selfface_picture)

### `self.set_lettering`

```lua
self.set_lettering(changes)
```

Changes the text drawn on the piece: any of text, size and color; nil takes the lettering away.

| Parameter | Type | Description |
|---|---|---|
| `changes` | `table?` | { text =, size =, color = }, each optional. |

**Module call** `TextTool.setValue` · **See also** [`self.lettering`](#selflettering)

### `self.show_glide`

```lua
self.show_glide{ x, z, rot, fast }
```

Shows it gliding towards a point or a heading on every screen, pulled and slowed as a module's smooth move is; the piece itself stays where it is until set_pos or set_rot puts it there.

| Field | Type | Description |
|---|---|---|
| `x` | `number` | _Optional._ Where it glides to, with z; leave both out to only turn. |
| `z` | `number` | _Optional._ Where it glides to, with x. |
| `rot` | `number` | _Optional._ The heading it turns to, in degrees; leave it out to keep its heading. |
| `fast` | `boolean` | _Optional._ true pulls it ten times as hard, as a module's fast smooth move does. |

**Returns**: false when neither a point nor a heading is given.

**Module call** `setPositionSmooth`

### `self.set_web_model`

```lua
self.set_web_model{ mesh, texture }
```

Fetches a model and its picture from web links and shows the piece as that model on every screen; a link that gives no model shows the picture flat, or a plain placeholder.

| Field | Type | Description |
|---|---|---|
| `mesh` | `string` | A web link to an .obj model. |
| `texture` | `string` | _Optional._ A web link to the picture wrapped on it. |

**Returns**: false when the mesh is not a web link; the model comes a moment later.

**Module call** `setCustomObject`

### `self.play_effect`

```lua
self.play_effect(index)
```

Plays an effect once on every screen, one round of it, whether it is running or not; false for a piece that is not an effect.

| Parameter | Type | Description |
|---|---|---|
| `index` | `number` | _Optional._ Which one-off it is, from 0, handed on to on_burst; 0 when left out. |

**Module call** `AssetBundle.playTriggerEffect` · **See also** [`self.set_effect_on`](#selfset_effect_on)

### `self.set_effect_on`

```lua
self.set_effect_on(on)
```

Starts or stops an effect.

| Parameter | Type | Description |
|---|---|---|
| `on` | `boolean` | true runs it; false stops it. |

**Module call** `AssetBundle.playLoopingEffect` · **See also** [`self.effect_on`](#selfeffect_on)

### `self.set_effect_smoke`

```lua
self.set_effect_smoke(thickness)
```

Makes an effect's smoke finer or denser: fine leaves the pieces under it readable, dense hangs and drifts downwind as far as real powder smoke.

| Parameter | Type | Description |
|---|---|---|
| `thickness` | `number` | _Optional._ From -2 (very fine) through -1 (fine), 0 (standard) and 1 (dense) to 2 (very dense); nil goes back to the smoke the piece was made with. |

**See also** [`self.effect_smoke`](#selfeffect_smoke)

### `self.set_worth`

```lua
self.set_worth(worth, groups)
```

Sets what the piece counts for, and optionally the groups it adds up with.

| Parameter | Type | Description |
|---|---|---|
| `worth` | `number` | A whole number. |
| `groups` | `number` | _Optional._ Groups as bits; nil keeps them. |

**Module call** `value` · **See also** [`self.worth`](#selfworth)

### `self.roll`

```lua
self.roll()
```

Rolls a die, or turns a piece of several states to one at random; false for anything else.

**Returns** `boolean`.

**Module call** `randomize` · **See also** [`game.roll`](#gameroll), [`self.set_face`](#selfset_face)

### `self.set_counter`

```lua
self.set_counter(name, value)
```

Sets one of its kind's counters (a whole number, kept to the counter's range).

| Parameter | Type | Description |
|---|---|---|
| `name` | `string` | The counter's name, as the piece studio shows it. |
| `value` | `number` | Rounded to a whole number and kept to the counter's range. |

```lua
self.set_counter("Strength", self.counters.Strength - 1)
```

**See also** [`self.counters`](#selfcounters)

### `self.set_text`

```lua
self.set_text(name, text)
```

Sets one of its kind's text lines.

| Parameter | Type | Description |
|---|---|---|
| `name` | `string` | The text line's name, as the piece studio shows it. |
| `text` | `string` | The new text. |

**See also** [`self.text`](#selftext)

### `self.set_tags`

```lua
self.set_tags(list)
```

Gives it its own tags; nil goes back to its kind's.

| Parameter | Type | Description |
|---|---|---|
| `list` | `string[]?` | _Optional._ The tags. Empty strings are dropped. nil goes back to the kind's tags. |

**Module call** `setTags` · **See also** [`self.tags`](#selftags), [`self.add_tag`](#selfadd_tag), [`self.remove_tag`](#selfremove_tag)

### `self.add_tag`

```lua
self.add_tag(tag)
```

Adds a tag to its own.

| Parameter | Type | Description |
|---|---|---|
| `tag` | `string` | The tag. Nothing changes when the piece has it already. |

A piece that wore its kind's tags gets its own list: the kind's, plus this one.

**Module call** `addTag` · **See also** [`self.remove_tag`](#selfremove_tag), [`self.tags`](#selftags)

### `self.remove_tag`

```lua
self.remove_tag(tag)
```

Takes a tag away.

| Parameter | Type | Description |
|---|---|---|
| `tag` | `string` | The tag. Nothing changes when the piece does not have it. |

**Module call** `removeTag` · **See also** [`self.add_tag`](#selfadd_tag), [`self.tags`](#selftags)

### `self.set_memo`

```lua
self.set_memo(text)
```

Sets its memo.

| Parameter | Type | Description |
|---|---|---|
| `text` | `string?` | _Optional._ The memo; nil empties it. |

**Module call** `setMemo` · **See also** [`self.memo`](#selfmemo)

### `self.set_script_state`

```lua
self.set_script_state(text)
```

Sets its saved script state outright (on_save's answer is the usual way).

| Parameter | Type | Description |
|---|---|---|
| `text` | `string?` | _Optional._ The state; nil empties it. |

**Module call** `script_state` · **See also** [`on_save`](#on_save), [`self.script_state`](#selfscript_state)

### `self.set_picture`

```lua
self.set_picture(def)
```

Wears another kind's picture; it keeps its own size, states, counters and script. nil puts its own back.

| Parameter | Type | Description |
|---|---|---|
| `def` | `string?` | _Optional._ The id of a piece definition in this game's packs. nil puts the piece's own picture back. |

A definition that is not in this game's packs is an error.

**Module call** `setCustomObject` · **See also** [`self.set_def`](#selfset_def)

### `self.set_def`

```lua
self.set_def(def, keep_script)
```

Becomes another kind of piece altogether.

| Parameter | Type | Description |
|---|---|---|
| `def` | `string` | The id of a piece definition in this game's packs. |
| `keep_script` | `boolean` | Keeps the script it runs now as its own instead of taking the new kind's; optional. |

The piece takes the new kind's picture, states, counters and script, or keeps its own script with keep_script. A definition that is not in this game's packs is an error.

**See also** [`self.set_picture`](#selfset_picture), [`self.def`](#selfdef)

### `self.set_script`

```lua
self.set_script(text)
```

Gives it a script of its own (nil: back to its kind's). Only from a click or menu handler, or on a piece the same call spawned.

| Parameter | Type | Description |
|---|---|---|
| `text` | `string?` | _Optional._ The Lua source. nil goes back to the kind's script. |

A script that reaches for the network, the files or code from text is refused with an error, as on import.

The new script starts from `on_load` at once.

**Module call** `setLuaScript` · **See also** [`self.script`](#selfscript), [`self.reload`](#selfreload)

### `self.reload`

```lua
self.reload()
```

Starts its script again from on_load.

**Module call** `reload` · **See also** [`on_load`](#on_load)

### `self.attach`

```lua
self.attach(piece)
```

Puts another piece on this one, to ride with it.

| Parameter | Type | Description |
|---|---|---|
| `piece` | `Piece\|string` | The piece to carry, or its id. |

**Module call** `addAttachment` · **See also** [`self.detach`](#selfdetach), [`self.attached`](#selfattached)

### `self.detach`

```lua
self.detach()
```

Takes this piece off the one it rides on.

**Module call** `removeAttachment` · **See also** [`self.attach`](#selfattach)

### `self.stowed`

```lua
self.stowed
```

The pieces it holds off the table, first in first: { id =, name =, guid =, def =, held = }, held being how many that piece holds in turn.

**Type** `list`, read-only.

**Module call** `getObjects`

### `self.show_contents`

```lua
self.show_contents(umpire, most)
```

Opens the window of what it holds on that umpire's screen, as if they had opened it; false when it holds nothing by its kind or the umpire is not here.

| Parameter | Type | Description |
|---|---|---|
| `umpire` | `Who` | Whose screen: an entry of game.umpires(). |
| `most` | `number` | _Optional._ Show only the first this many; leave it out for all. |

**Returns**: true when the window was sent.

**Module call** `Container.search`

### `self.stow`

```lua
self.stow(piece)
```

Takes another piece off the table into this one, with whatever rides on it; a piece this one rides on is refused.

**Module call** `putObject`

### `self.unstow`

```lua
self.unstow(which, place, rot)
```

Puts a held piece back on the table and returns it: which is its id, guid, its number in stowed, or nothing for the next by the holder's take order (first, last or random); place is { x =, z = } or a piece, else beside this one; rot turns it.

**Module call** `takeObject` · **See also** [`self.stowed`](#selfstowed), [`self.stow`](#selfstow), [`self.unstow`](#selfunstow)

### `self.ready`

```lua
self.ready
```

Its ready piece, the one held piece it releases and recalls with one key, held or out on the table; nil for none.

**Type** `piece`, read-only.

### `self.set_ready`

```lua
self.set_ready(piece)
```

Makes a piece it holds its ready piece, first among what it holds; nil takes the mark off.

| Parameter | Type | Description |
|---|---|---|
| `piece` | `piece` | A piece it holds, or nil. |

### `self.release`

```lua
self.release()
```

Puts its ready piece on the table where it stood when last recalled, turned and moved with this one; beside this one the first time. False when it is not held.

**Returns**: true when it went out.

### `self.recall`

```lua
self.recall()
```

Takes its ready piece back from the table into this one and remembers where it stood. False when it is not out.

**Returns**: true when it came back.

**See also** [`self.ready`](#selfready), [`self.set_ready`](#selfset_ready), [`self.release`](#selfrelease), [`self.recall`](#selfrecall)

### `self.delete`

```lua
self.delete()
```

Takes the piece off the table (on_destroy runs first).

The piece's own `on_destroy` and the global script's `on_destroy(piece)` run before it goes. Its handle answers `exists = false` afterwards.

**Module call** `destruct` · **See also** [`on_destroy`](#on_destroy), [`self.exists`](#selfexists)

### `self.set_lines`

```lua
self.set_lines(lines)
```

Draws lines round the piece that move and turn with it: { { points = { {x=, z=}, … }, color =, width =, loop = }, … } in piece units; width in map units.

| Parameter | Type | Description |
|---|---|---|
| `lines` | `Line[]` | The lines. Each needs two points or more; points are `{ x =, z = }` or `{ x, z }` in piece units. An empty table takes every line away. |

```lua
local ring = {}
for i = 0, 35 do
  local a = math.rad(i * 10)
  ring[#ring + 1] = { x = math.cos(a) * 3, z = math.sin(a) * 3 }
end
self.set_lines{ { points = ring, color = "#d8b766", width = 0.4, loop = true } }
```

`color` defaults to white and `width` to 0.1 map units; a colour with alpha 0 draws nothing.

**Module call** `setVectorLines` · **See also** [`self.lines`](#selflines), [`self.to_local`](#selfto_local)

### `self.set_decals`

```lua
self.set_decals(decals)
```

Puts pictures on the piece's faces that move, turn and flip with it: { { picture =, x =, z =, w =, h =, rot =, face =, name = }, … } in piece units.

| Parameter | Type | Description |
|---|---|---|
| `decals` | `Decal[]` | The decals. `picture` is a picture in this game's packs (a module's decal images come along when it is converted) or one from game.fetch_picture or a picture dialog; `face` is "front" or "back"; `w` and `h` default to 0.2, `rot` is degrees. An empty table takes every decal away. |

```lua
local arms = self.decals[1]
self.set_decals{ { picture = arms.picture, x = 0.35, z = -0.3, w = 0.2, h = 0.2, name = "Arms" } }
```

At most 64 on a piece. A picture not in this game's packs is an error. On a kind whose looks carry decals of their own, what this puts on a piece belongs to the look it was in and the other looks keep theirs, and the copy a script sets while it loads is left out, the look already carrying it.

**Module call** `setDecals` · **See also** [`self.decals`](#selfdecals), [`self.state`](#selfstate)

### `self.to_world`

```lua
self.to_world(x, z)
```

A point in piece units (centre 0, x right, z down the picture) as a map point { x =, z = }.

| Parameter | Type | Description |
|---|---|---|
| `x` | `number` | Piece units to the right of the centre. |
| `z` | `number` | Piece units down the picture from the centre. |

**Returns** `Point`.

```lua
local front = self.to_world(0, -self.size.h / self.scale)
board.spawn{ def = "std.effects.signal-smoke", x = front.x, z = front.z }
```

**Module call** `positionToWorld` · **See also** [`self.to_local`](#selfto_local)

### `self.to_local`

```lua
self.to_local(x, z)
```

A map point as piece units.

| Parameter | Type | Description |
|---|---|---|
| `x` | `number` | The map point's x. |
| `z` | `number` | The map point's z. |

**Returns** `Point`.

**Module call** `positionToLocal` · **See also** [`self.to_world`](#selfto_world)

## A piece: widgets and its menu

Widgets are drawn on a face in piece units: map units measured on the piece's kind before the table scales it, centre (0, 0), x right, z down the picture. A kind 1.2 wide and 0.6 high runs from x = -0.6 to 0.6 and z = -0.3 to 0.3. Colours are "#RRGGBB[AA]", a name or { r, g, b, a }.

| Name | What it does |
|---|---|
| [`self.button`](#selfbutton) | Draws a button and returns its index from 1. With no w and h it is a bare label that takes no clicks. on_click names a function called as fn(who, alt, index, piece). |
| [`self.input`](#selfinput) | Draws an input box and returns its index. on_change names a function called as fn(who, value, index, piece, typing) as the umpire types and when they leave the box. |
| [`self.edit`](#selfedit) | Changes a widget: any of the fields button and input take. |
| [`self.remove_widget`](#selfremove_widget) | Takes one widget away (the later ones move up). |
| [`self.clear_widgets`](#selfclear_widgets) | Takes every widget away. |
| [`self.widgets`](#selfwidgets) | The widgets as tables, in order. |
| [`self.menu_add`](#selfmenu_add) | Adds a row to the piece's right-click menu; the function is called as fn(who, piece). |
| [`self.menu_clear`](#selfmenu_clear) | Takes the script's rows out of the piece's menu. |

### `self.button`

```lua
self.button{ face, x, z, w, h, rot, label, font, color, text_color, hover_color, press_color, tooltip, on_click, kind }
```

Draws a button and returns its index from 1. With no w and h it is a bare label that takes no clicks. on_click names a function called as fn(who, alt, index, piece).

| Field | Type | Description |
|---|---|---|
| `face` | `"front"\|"back"` | _Optional._ The face it is drawn on. Default "front". |
| `x` | `number` | _Optional._ Centre, piece units right of the piece's centre. Default 0. |
| `z` | `number` | _Optional._ Centre, piece units down the picture. Default 0. |
| `w` | `number` | _Optional._ Width in piece units. Leave out `w` and `h` for a bare label. |
| `h` | `number` | _Optional._ Height in piece units. The box is exactly what can be pressed. |
| `rot` | `number` | _Optional._ Turn on the face, in degrees. Default 0. |
| `y` | `number` | _Optional._ Its height in piece units above the piece's base, seen from either side; nil draws it on the face. A module's widget takes its height from the piece's `source` origin and rise. |
| `label` | `string` | _Optional._ The text. |
| `font` | `number` | _Optional._ Letter size in piece units; a longer label runs past its button. Default 0.05. |
| `color` | `Color` | _Optional._ The box's colour. |
| `text_color` | `Color` | _Optional._ The text's colour. |
| `tooltip` | `string` | _Optional._ Shown when the pointer rests on it. |
| `hover_color` | `Color` | _Optional._ The box's colour under the pointer. Default: each of red, green and blue 0.2 nearer the middle grey. |
| `press_color` | `Color` | _Optional._ The box's colour while pressed. Default: each 0.4 nearer the middle grey. |
| `on_click` | `string` | _Optional._ The handler's name: called as fn(who, alt, index, piece); `alt` is true for a right-click. |
| `kind` | `"button"\|"input"\|"label"` | _Optional._ "label" shows it but takes no clicks or typing: locked text. A module's button or input turned face down above the piece comes across this way. `edit` can change it. |

**Returns** `number`: The widget's index, from 1, for `edit` and `remove_widget`.

```lua
function on_load()
  self.button{ x = 0, z = 0.3, w = 0.3, h = 0.12, label = "−1", font = 0.08, on_click = "hit" }
end

function hit(who, alt)
  local n = self.counters.Strength + (alt and 1 or -1)
  self.set_counter("Strength", n)
  game.say(who.name .. ": " .. (self.name or self.id) .. " at " .. n)
end
```

The handler is looked up in this script first, then in the global script. `"global:fn"` names the global script's; `"<id or GUID>/fn"` another piece's.

**Module call** `createButton` · **See also** [`self.input`](#selfinput), [`self.edit`](#selfedit), [`self.clear_widgets`](#selfclear_widgets)

### `self.input`

```lua
self.input{ face, x, z, w, h, rot, value, placeholder, font, color, text_color, align, lines, tooltip, on_change, validation, tab, kind }
```

Draws an input box and returns its index. on_change names a function called as fn(who, value, index, piece, typing) as the umpire types and when they leave the box.

| Field | Type | Description |
|---|---|---|
| `face` | `"front"\|"back"` | _Optional._ The face it is drawn on. Default "front". |
| `x` | `number` | _Optional._ Centre, piece units. Default 0. |
| `z` | `number` | _Optional._ Centre, piece units. Default 0. |
| `w` | `number` | _Optional._ Width in piece units. |
| `h` | `number` | _Optional._ Height in piece units. |
| `rot` | `number` | _Optional._ Turn on the face, in degrees. |
| `y` | `number` | _Optional._ Its height in piece units above the piece's base; nil draws it on the face. |
| `value` | `string` | _Optional._ The text in the box. |
| `placeholder` | `string` | _Optional._ Shown greyed while the box is empty. |
| `font` | `number` | _Optional._ Letter size in piece units; the box shows as much of the text as fits. Default 0.05. |
| `color` | `Color` | _Optional._ The box's colour. |
| `text_color` | `Color` | _Optional._ The text's colour. |
| `align` | `"left"\|"center"\|"right"` | _Optional._ Default "center". |
| `lines` | `number` | _Optional._ Lines of text the box holds. Default 1. |
| `tooltip` | `string` | _Optional._ Shown when the pointer rests on it. |
| `on_change` | `string` | _Optional._ The handler's name: called as fn(who, value, index, piece, typing) each time the text changes while the umpire types (typing true) and once more when they leave the box (typing false); text it returns replaces what was typed. |
| `validation` | `"integer"\|"float"\|"alphanumeric"\|"username"\|"name"` | _Optional._ Keys the box takes: digits and a leading minus; also one point; letters and digits; lower-case letters and digits; or a name, capitalised by word. Default any. |
| `tab` | `"next"\|"spaces"\|"none"` | _Optional._ What Tab does: go on to the piece's next input (the default), type four spaces, or nothing. |
| `kind` | `"button"\|"input"\|"label"` | _Optional._ "label" shows its value but takes no typing: locked text. |

**Returns** `number`: The widget's index, from 1.

```lua
self.input{ face = "back", z = -0.2, w = 0.8, h = 0.12, value = self.name or "",
            placeholder = "Regiment", on_change = "renamed" }

function renamed(who, value) self.set_name(value) end
```

The typed value stays on the widget, so it is there for everyone and in the save, whether or not a handler keeps it.

**Module call** `createInput` · **See also** [`self.button`](#selfbutton), [`self.edit`](#selfedit)

### `self.edit`

```lua
self.edit(index, fields)
```

Changes a widget: any of the fields button and input take.

| Parameter | Type | Description |
|---|---|---|
| `index` | `number` | The widget's index, from 1. |
| `fields` | `table` | The fields to change, as `button` or `input` takes them; the others stay. |

```lua
self.edit(1, { label = tostring(n), color = n > 0 and "#2f5d2f" or "#7a1f1f" })
```

An index with no widget is an error.

**Module call** `editButton` · **See also** [`self.button`](#selfbutton), [`self.widgets`](#selfwidgets)

### `self.remove_widget`

```lua
self.remove_widget(index)
```

Takes one widget away (the later ones move up).

| Parameter | Type | Description |
|---|---|---|
| `index` | `number` | The widget's index, from 1. An index with no widget does nothing. |

The widgets after it move up one: index 3 becomes 2. Redraw from scratch with `clear_widgets` when the order matters.

**Module call** `removeButton` · **See also** [`self.clear_widgets`](#selfclear_widgets)

### `self.clear_widgets`

```lua
self.clear_widgets()
```

Takes every widget away.

**Module call** `clearButtons` · **See also** [`self.button`](#selfbutton), [`self.input`](#selfinput)

### `self.widgets`

```lua
self.widgets()
```

The widgets as tables, in order.

**Returns** `table`: A list of tables with `kind` ("button" or "input") and every field `button` and `input` take.

**Module call** `getButtons` · **See also** [`self.edit`](#selfedit)

### `self.menu_add`

```lua
self.menu_add(label, fn_name)
```

Adds a row to the piece's right-click menu; the function is called as fn(who, piece).

| Parameter | Type | Description |
|---|---|---|
| `label` | `string` | The row's text. |
| `fn_name` | `string` | The handler's name, looked up as for `on_click`. |

```lua
self.menu_add("Rally", "rally")
function rally(who) self.set_state(1) end
```

**Module call** `addContextMenuItem` · **See also** [`self.menu_clear`](#selfmenu_clear), [`game.menu_add`](#gamemenu_add)

### `self.menu_clear`

```lua
self.menu_clear()
```

Takes the script's rows out of the piece's menu.

**Module call** `clearContextMenu` · **See also** [`self.menu_add`](#selfmenu_add)

## A piece: other scripts

One script talks to another through these. Calls inside calls go 50 deep at most.

| Name | What it does |
|---|---|
| [`self.call`](#selfcall) | Runs a function of that piece's script and returns what it returns. |
| [`self.get_var`](#selfget_var) | Reads a global variable of that piece's script. |
| [`self.set_var`](#selfset_var) | Writes a global variable of that piece's script. |

### `self.call`

```lua
self.call(fn_name, ...)
```

Runs a function of that piece's script and returns what it returns.

| Parameter | Type | Description |
|---|---|---|
| `fn_name` | `string` | A global function of that piece's script. |
| `...` | `any` | _Optional._ Handed to the function as they are. |

**Returns** `any`: What the function returns; nil when the piece has no script or the script no such function.

```lua
local hq = board.find("c3a91f")
if hq then hq.call("report", self.name, self.counters.Strength) end
```

**Module call** `call` · **See also** [`game.call`](#gamecall), [`self.get_var`](#selfget_var)

### `self.get_var`

```lua
self.get_var(name)
```

Reads a global variable of that piece's script.

| Parameter | Type | Description |
|---|---|---|
| `name` | `string` | The variable's name. |

**Returns** `any`: Its value, or nil.

**Module call** `getVar` · **See also** [`self.set_var`](#selfset_var), [`self.call`](#selfcall)

### `self.set_var`

```lua
self.set_var(name, value)
```

Writes a global variable of that piece's script.

| Parameter | Type | Description |
|---|---|---|
| `name` | `string` | The variable's name. |
| `value` | `any` | The new value. |

**Module call** `setVar` · **See also** [`self.get_var`](#selfget_var)

## board

The umpire board: every piece a script can reach is on it.

| Name | What it does |
|---|---|
| [`board.find`](#boardfind) | The piece with that id or module GUID, or nil. |
| [`board.pieces`](#boardpieces) | Every piece, or those that match { def =, tag =, name = }. |
| [`board.show_cast`](#boardshow_cast) | Shows a see-through ray, sphere or box sliding from one point to another on every umpire's screen, to check where a script is looking. |
| [`board.kinds`](#boardkinds) | The kinds this game's packs hold, or those that match { link =, type =, tag = }: { id =, name =, tags =, source = }. |
| [`board.spawn`](#boardspawn) | Puts a new piece of that kind on the board and returns it; its script starts at once. |
| [`board.distance`](#boarddistance) | Metres between two pieces or two { x =, z = } points by the map's scale (map units when it has none). |
| [`board.zones`](#boardzones) | The umpire zones: { id =, name =, owner =, contains = function(piece_or_point) }. |
| [`board.draw`](#boarddraw) | Draws a line on the umpire's drawing board and returns its id: { points = { {x=,z=}, ... }, color =, width =, loop = }. |
| [`board.erase`](#boarderase) | Takes a drawn line off the board. |
| [`board.strokes`](#boardstrokes) | The lines on the umpire's drawing board: { id =, points =, color =, width = }. |

### `board.find`

```lua
board.find(id_or_guid)
```

The piece with that id or module GUID, or nil.

| Parameter | Type | Description |
|---|---|---|
| `id_or_guid` | `string` | Our id (p-000123) or the module GUID the piece came with. |

**Returns** `Piece`: The piece, or nil when there is none.

```lua
local gun = board.find("a1b2c3")
if gun then gun.set_state("Limbered") end
```

**Module call** `getObjectFromGUID` · **See also** [`board.pieces`](#boardpieces), [`self.id`](#selfid), [`self.guid`](#selfguid)

### `board.pieces`

```lua
board.pieces(filter)
```

Every piece, or those that match { def =, tag =, name = }.

| Parameter | Type | Description |
|---|---|---|
| `filter.def` | `string` | _Optional._ Only pieces of this kind. |
| `filter.tag` | `string` | _Optional._ Only pieces with this tag (their own, or their kind's). |
| `filter.name` | `string` | _Optional._ Only pieces with this label; case does not matter. |

**Returns** `Piece[]`: A list; empty when nothing matches. Every field given must match.

```lua
for _, p in ipairs(board.pieces{ tag = "French" }) do
  if p.counters.Strength == 0 then p.set_state("Routed") end
end
```

**Module call** `getObjects` · **See also** [`board.find`](#boardfind), [`self.tags`](#selftags)

### `board.show_cast`

```lua
board.show_cast{ shape, from, to, size, turn, seconds }
```

Shows a see-through ray, sphere or box sliding from one point to another on every umpire's screen, to check where a script is looking.

| Field | Type | Description |
|---|---|---|
| `shape` | `string` | _Optional._ "ray", "sphere" or "box"; a ray by default. |
| `from` | `{ x, y, z }` | Where it starts. |
| `to` | `{ x, y, z }` | Where it ends. |
| `size` | `{ x, y, z }` | _Optional._ The sphere's width or the box's size. |
| `turn` | `{ x, y, z }` | _Optional._ The box's turn in degrees. |
| `seconds` | `number` | _Optional._ How long it takes, at most 60; 1 by default. |

**Returns**: true once it is sent.

**Module call** `Physics.cast`

### `board.kinds`

```lua
board.kinds(filter)
```

The kinds this game's packs hold, or those that match { link =, type =, tag = }: { id =, name =, tags =, source = }.

| Parameter | Type | Description |
|---|---|---|
| `filter.link` | `string` | _Optional._ Only kinds made from this picture or model link (one of their source links). |
| `filter.type` | `string` | _Optional._ Only kinds converted from this module object type, such as Custom_Token. |
| `filter.tag` | `string` | _Optional._ Only kinds with this tag. |

**Returns** `table[]`: A list; empty when nothing matches. `source` is the kind's module source, as a piece's `source` field gives it, or nil.

```lua
local found = board.kinds{ link = "https://example.test/infantry.png" }[1]
if found then self.set_picture(found.id) end
```

**See also** [`board.spawn`](#boardspawn), [`self.source`](#selfsource)

### `board.spawn`

```lua
board.spawn{ def, x, z, rot, label, state, scale, y, script, tags, memo, note, script_state, guid }
```

Puts a new piece of that kind on the board and returns it; its script starts at once.

| Field | Type | Description |
|---|---|---|
| `def` | `string` | The id of a piece definition in this game's packs. |
| `x` | `number` | _Optional._ Map units. Default 0. |
| `z` | `number` | _Optional._ Map units. Default 0. |
| `rot` | `number` | _Optional._ Heading in degrees. Default 0. |
| `label` | `string` | _Optional._ The label under it. |
| `state` | `number` | _Optional._ The state to start in, from 1. |
| `scale` | `number` | _Optional._ Size multiplier. Default 1. |
| `y` | `number` | _Optional._ Height above the table. |
| `script` | `string` | _Optional._ A script of its own, in place of its kind's. |
| `tags` | `string[]` | _Optional._ Tags of its own. |
| `memo` | `string` | _Optional._ Its memo. |
| `note` | `string` | _Optional._ Its description. |
| `script_state` | `string` | _Optional._ Handed to its `on_load`. |
| `guid` | `string` | _Optional._ A module GUID to give it; one already taken is replaced by a fresh one. |

**Returns** `Piece`: The new piece.

```lua
local p = self.pos
local smoke = board.spawn{ def = "std.effects.signal-smoke", x = p.x, z = p.z - 6, label = "Smoke" }
wait.seconds(30, function() if smoke.exists then smoke.delete() end end)
```

A missing `def` is an error. The global script's `on_spawn` runs for the new piece.

**Module call** `spawnObject` · **See also** [`self.delete`](#selfdelete), [`on_spawn`](#on_spawn-global-script)

### `board.distance`

```lua
board.distance(a, b)
```

Metres between two pieces or two { x =, z = } points by the map's scale (map units when it has none).

| Parameter | Type | Description |
|---|---|---|
| `a` | `Piece\|Point` | A piece, or a map point. |
| `b` | `Piece\|Point` | A piece, or a map point. |

**Returns** `number`: The distance in metres; in map units while the map has no scale; nil when either is neither a piece nor a point.

```lua
local d = board.distance(self, board.find("a1b2c3"))
if d and d < 300 then game.say("In musket range: " .. math.floor(d) .. " m") end
```

**See also** [`game.calibration`](#gamecalibration)

### `board.zones`

```lua
board.zones()
```

The umpire zones: { id =, name =, owner =, contains = function(piece_or_point) }.

**Returns** `table`: A list of zones. `contains(piece_or_point)` answers true when the piece or point lies inside.

```lua
for _, z in ipairs(board.zones()) do
  if z.contains(self) then print("in", z.name) end
end
```

**Module call** `getZones`

### `board.draw`

```lua
board.draw(stroke)
```

Draws a line on the umpire's drawing board and returns its id: { points = { {x=,z=}, ... }, color =, width =, loop = }.

**Returns** `string`.

**Module call** `Global.setVectorLines`

### `board.erase`

```lua
board.erase(id)
```

Takes a drawn line off the board.

### `board.strokes`

```lua
board.strokes()
```

The lines on the umpire's drawing board: { id =, points =, color =, width = }.

**Returns** `table[]`.

**Module call** `Global.getVectorLines`

## game

The game around the table.

| Name | What it does |
|---|---|
| [`game.turn`](#gameturn) | The turn number. |
| [`game.time`](#gametime) | The game time, as "1812-07-22T06:00". |
| [`game.calibration`](#gamecalibration) | Map units per metre, or nil before the map has a scale. |
| [`game.north`](#gamenorth) | Degrees clockwise from the top of the map to north: 0 when north is the top. |
| [`game.advance_clock`](#gameadvance_clock) | Ends the turn, as Next turn does. |
| [`game.umpires`](#gameumpires) | The umpires at the table: { { id =, name =, role = "host"\|"lead"\|"umpire" }, … }. |
| [`game.say`](#gamesay) | A line in the Log: to nil = everyone at the table, an umpire's table = that umpire, "u:" = the umpires. |
| [`game.roll`](#gameroll) | Dice rolled by the server, in the Log; the results as a list. |
| [`game.hotkey`](#gamehotkey) | A key each umpire binds in Settings → Keys; fn(who, piece_under_pointer, { x =, z = }, released). on_up: called on the release too. |
| [`game.hotkeys_clear`](#gamehotkeys_clear) | Takes this script's hotkeys away. |
| [`game.menu_add`](#gamemenu_add) | A row in the table's own right-click menu; fn(who, { x =, z = }). |
| [`game.menu_clear`](#gamemenu_clear) | Takes this script's rows out of the table's menu. |
| [`game.selected`](#gameselected) | The pieces that umpire had selected at their last hotkey. |
| [`game.hovered`](#gamehovered) | The piece under that umpire's pointer at their last hotkey, or nil. |
| [`game.select`](#gameselect) | Puts the piece into that umpire's selection on their screen, or (on = false) takes it out. |
| [`game.call`](#gamecall) | Runs a function of the global script and returns what it returns. |
| [`game.get_var`](#gameget_var) | Reads a global variable of the global script. |
| [`game.set_var`](#gameset_var) | Writes a global variable of the global script. |
| [`game.dialog`](#gamedialog) | Opens a window for one umpire (nil: everyone): kind memo, input, confirm, options, color or picture. fn(answer, who); the answer is nil when cancelled. |
| [`game.fetch_picture`](#gamefetch_picture) | Downloads a picture (PNG, JPEG, WebP or BMP, up to 16 MB) from the web on the host, for set_decals. fn(picture) gets its name, or nil when the link gave none. Links to the host PC or its local network give none. |

### `game.turn`

```lua
game.turn
```

The turn number.

**Type** `number`, read-only.

**See also** [`on_turn`](#on_turn), [`game.advance_clock`](#gameadvance_clock)

### `game.time`

```lua
game.time
```

The game time, as "1812-07-22T06:00".

**Type** `string`, read-only.

### `game.calibration`

```lua
game.calibration
```

Map units per metre, or nil before the map has a scale.

**Type** `number?`, read-only.

### `game.north`

```lua
game.north
```

Degrees clockwise from the top of the map to north: 0 when north is the top.

**Type** `number`, read-only.

**See also** [`board.distance`](#boarddistance)

### `game.advance_clock`

```lua
game.advance_clock()
```

Ends the turn, as Next turn does.

Every script's `on_turn` runs with the new turn number.

**See also** [`game.turn`](#gameturn), [`on_turn`](#on_turn)

### `game.umpires`

```lua
game.umpires()
```

The umpires at the table: { { id =, name =, role = "host"|"lead"|"umpire" }, … }.

**Returns** `Who[]`: A list of the umpires present, as `Who` tables.

```lua
for _, u in ipairs(game.umpires()) do
  if u.role == "host" then game.say("Turn over to you", { to = u }) end
end
```

**Module call** `getPlayers`

### `game.say`

```lua
game.say(text, { to, color })
```

A line in the Log: to nil = everyone at the table, an umpire's table = that umpire, "u:" = the umpires.

| Parameter | Type | Description |
|---|---|---|
| `text` | `string` | The line. |
| `opts.to` | `Who\|string` | _Optional._ An umpire (a `Who`) for a line only they see; "u:" for the umpires only; nil for everyone at the table. |
| `opts.color` | `Color` | _Optional._ The line's colour. |

```lua
game.say("The bridge is down.", { color = "#e07050" })
game.say("Only you see this.", { to = who })
```

**Module call** `broadcastToAll` · **See also** [`on_chat`](#on_chat-global-script)

### `game.roll`

```lua
game.roll(sides, count, label)
```

Dice rolled by the server, in the Log; the results as a list.

| Parameter | Type | Description |
|---|---|---|
| `sides` | `number` | _Optional._ Sides of each die. Default 6. |
| `count` | `number` | _Optional._ How many dice. Default 1. |
| `label` | `string` | _Optional._ Shown with the roll in the Log. |

**Returns** `number[]`: The results, one number per die.

```lua
local r = game.roll(6, 2, "Morale: " .. self.name)
if r[1] + r[2] < 7 then self.set_state("Shaken") end
```

### `game.hotkey`

```lua
game.hotkey(label, fn, on_up)
```

A key each umpire binds in Settings → Keys; fn(who, piece_under_pointer, { x =, z = }, released). on_up: called on the release too.

| Parameter | Type | Description |
|---|---|---|
| `label` | `string` | Shown in Settings → Keys. A label that ends in a digit starts on that number key. |
| `fn` | `function` | Called as fn(who, piece, point, released): the piece under the pointer or nil, the table point under it. |
| `on_up` | `boolean` | _Optional._ true: fn is called on the key's release too, with `released` true. |

**Returns** `number`: The hotkey's number.

```lua
game.hotkey("Roll for the piece under the pointer", function(who, piece)
  if piece then game.roll(6, 1, piece.name or piece.id) end
end)
```

Call it from `on_load`: hotkeys are not saved, and a reload registers them again.

**Module call** `addHotkey` · **See also** [`game.hotkeys_clear`](#gamehotkeys_clear), [`game.selected`](#gameselected), [`game.hovered`](#gamehovered)

### `game.hotkeys_clear`

```lua
game.hotkeys_clear()
```

Takes this script's hotkeys away.

**Module call** `clearHotkeys` · **See also** [`game.hotkey`](#gamehotkey)

### `game.menu_add`

```lua
game.menu_add(label, fn)
```

A row in the table's own right-click menu; fn(who, { x =, z = }).

| Parameter | Type | Description |
|---|---|---|
| `label` | `string` | The row's text. |
| `fn` | `function` | Called as fn(who, point) with the table point that was right-clicked. |

**Returns** `number`: The row's number.

```lua
game.menu_add("Drop a smoke marker here", function(who, at)
  board.spawn{ def = "std.effects.signal-smoke", x = at.x, z = at.z }
end)
```

**Module call** `addContextMenuItem` · **See also** [`game.menu_clear`](#gamemenu_clear), [`self.menu_add`](#selfmenu_add)

### `game.menu_clear`

```lua
game.menu_clear()
```

Takes this script's rows out of the table's menu.

**Module call** `clearContextMenu` · **See also** [`game.menu_add`](#gamemenu_add)

### `game.selected`

```lua
game.selected(who)
```

The pieces that umpire had selected at their last hotkey.

| Parameter | Type | Description |
|---|---|---|
| `who` | `Who\|number` | The umpire, or their id. |

**Returns** `Piece[]`: A list; empty when they had nothing selected.

**Module call** `getSelectedObjects` · **See also** [`game.hotkey`](#gamehotkey), [`game.select`](#gameselect)

### `game.hovered`

```lua
game.hovered(who)
```

The piece under that umpire's pointer at their last hotkey, or nil.

| Parameter | Type | Description |
|---|---|---|
| `who` | `Who\|number` | The umpire, or their id. |

**Returns** `Piece?`.

**Module call** `getHoverObject` · **See also** [`game.hotkey`](#gamehotkey)

### `game.select`

```lua
game.select(who, piece, on)
```

Puts the piece into that umpire's selection on their screen, or (on = false) takes it out.

| Parameter | Type | Description |
|---|---|---|
| `who` | `Who\|number` | The umpire, or their id. |
| `piece` | `Piece\|string` | The piece, or its id. |
| `on` | `boolean` | _Optional._ false takes it out of the selection. Default true. |

**Module call** `addToPlayerSelection` · **See also** [`game.selected`](#gameselected)

### `game.call`

```lua
game.call(fn_name, ...)
```

Runs a function of the global script and returns what it returns.

| Parameter | Type | Description |
|---|---|---|
| `fn_name` | `string` | A global function of the global script. |
| `...` | `any` | _Optional._ Handed to the function as they are. |

**Returns** `any`: What the function returns; nil when there is no global script or no such function.

**Module call** `Global.call` · **See also** [`self.call`](#selfcall)

### `game.get_var`

```lua
game.get_var(name)
```

Reads a global variable of the global script.

| Parameter | Type | Description |
|---|---|---|
| `name` | `string` | The variable's name. |

**Returns** `any`: Its value, or nil.

**Module call** `Global.getVar` · **See also** [`game.set_var`](#gameset_var)

### `game.set_var`

```lua
game.set_var(name, value)
```

Writes a global variable of the global script.

| Parameter | Type | Description |
|---|---|---|
| `name` | `string` | The variable's name. |
| `value` | `any` | The new value. |

**Module call** `Global.setVar` · **See also** [`game.get_var`](#gameget_var)

### `game.dialog`

```lua
game.dialog(who, { kind, title, text, options }, fn)
```

Opens a window for one umpire (nil: everyone): kind memo, input, confirm, options, color or picture. fn(answer, who); the answer is nil when cancelled.

| Parameter | Type | Description |
|---|---|---|
| `who` | `Who?` | The umpire who gets the window; nil for every umpire. |
| `spec.kind` | `"memo"\|"input"\|"confirm"\|"options"\|"color"\|"picture"` | _Optional._ A text to read and edit, one line, a yes or no, a pick from `options`, a player colour name, or a picture from the umpire's computer or the web, answered as a name for set_decals. Default "memo". A module's input dialog whose title asks for an image or URL opens as "picture". |
| `spec.title` | `string` | _Optional._ The window's title. |
| `spec.text` | `string` | _Optional._ The text to edit, or the question. |
| `spec.options` | `string[]` | _Optional._ The choices, for "options". |
| `fn` | `function` | Called as fn(answer, who) once the umpire answers; `answer` is nil when they cancelled. |

**Returns** `string`: The dialog's token.

```lua
function orders(who)
  game.dialog(who, { kind = "options", title = "Orders", options = { "Advance", "Hold", "Retire" } },
    function(answer)
      if answer then self.set_text("Orders", answer) end
    end)
end
```

A function that is not a function in the third place is an error.

**Module call** `showInputDialog`

### `game.fetch_picture`

```lua
game.fetch_picture(link, fn)
```

Downloads a picture (PNG, JPEG, WebP or BMP, up to 16 MB) from the web on the host, for set_decals. fn(picture) gets its name, or nil when the link gave none. Links to the host PC or its local network give none.

| Parameter | Type | Description |
|---|---|---|
| `link` | `string` | An http or https link. |
| `fn` | `function` | Called as fn(picture) once the download ends. |

```lua
game.fetch_picture("https://example.com/arms.png", function(picture)
  if picture then self.set_decals{ { picture = picture, w = 0.3, h = 0.3 } } end
end)
```

A module script's setDecals fetches its web links this way by itself.

**See also** [`self.set_decals`](#selfset_decals), [`game.dialog`](#gamedialog)

## ui

Screen panels, written in XML as a digital tabletop module writes them. A piece's own panels (`self.ui`) take the same calls. New panels land on the next frame, and every change to an element lands when the call that made it ends, so the same call still reads what was there. A click, a typed or changed value calls the function the element names, as fn(who, value, id): with no slash in the global script for screen panels or in the piece's own script for its panels, as "global/fn" in the global script, or as "guid/fn" in that piece's.

| Name | What it does |
|---|---|
| [`ui.set`](#uiset) | Replaces the panels: XML (several roots allowed) or a list of tables { tag =, attributes = {}, children = {}, value = }. They land on the next frame. |
| [`ui.xml`](#uixml) | The panels as they were written, with the changes made since: each element on its own lines. |
| [`ui.tree`](#uitree) | The panels as tables. |
| [`ui.set_attribute`](#uiset_attribute) | Changes one attribute of an element; it lands when the call ends. |
| [`ui.set_attributes`](#uiset_attributes) | Changes several attributes at once. |
| [`ui.get_attribute`](#uiget_attribute) | Reads an attribute of the element as it stands, with what the panels' Defaults give it, or nil. |
| [`ui.show`](#uishow) | Shows an element. |
| [`ui.hide`](#uihide) | Hides an element; one inside a hidden element stays as it is. |
| [`ui.set_value`](#uiset_value) | Sets the value the panels hold between an element's tags; a Text element shows it as its words. |
| [`ui.get_value`](#uiget_value) | Reads the value held between an element's tags, or nil. |
| [`ui.get_attributes`](#uiget_attributes) | Every attribute of the element as it stands, or nil. |
| [`ui.set_class`](#uiset_class) | Sets an element's classes, which take their looks from the Defaults the panels hold. |
| [`ui.pictures`](#uipictures) | The pictures panels name: a list of { type =, name =, url = }. |
| [`ui.set_pictures`](#uiset_pictures) | Replaces the pictures panels name, at once, and builds the panels again from what they hold. |
| [`ui.loading`](#uiloading) | true from setting panels until they land, and while panels that did not read are in place. |
| [`ui.assetType`](#uiassettype) | { Image = 0, AssetBundle = 1 }, also as image and assetBundle, as module scripts name picture types. |

### `ui.set`

```lua
ui.set(xml_or_list, pictures)
```

Replaces the panels: XML (several roots allowed) or a list of tables { tag =, attributes = {}, children = {}, value = }. They land on the next frame.

| Parameter | Type | Description |
|---|---|---|
| `xml_or_list` | `string\|UiNode[]` | XML, or a list of element tables; a single table that is not a list is refused. |
| `pictures` | `table` | _Optional._ { name =, url = } entries, as `ui.set_pictures` takes, set at once. |

**Returns** `boolean`: true when the panels were taken; false for nil or a table that is not a list.

```lua
ui.set([[<Panel id="clock" rectAlignment="UpperCenter" offsetXY="0 -20" width="260" height="44" color="#1b1b1bd8">
  <Text id="time" fontSize="20" color="#ffffff">06:00</Text>
</Panel>]])
```

Panels that do not read as XML leave the old panels in place, say why in the chat and keep `ui.loading` true until panels that read are set.

**Module call** `UI.setXml` · **See also** [`ui.xml`](#uixml), [`ui.set_attribute`](#uiset_attribute), [`self.ui`](#selfui)

### `ui.xml`

```lua
ui.xml()
```

The panels as they were written, with the changes made since: each element on its own lines.

**Returns** `string`.

**Module call** `UI.getXml` · **See also** [`ui.set`](#uiset)

### `ui.tree`

```lua
ui.tree()
```

The panels as tables.

**Returns** `UiNode[]`: A list of `{ tag =, attributes = {}, children = {}, value = }` tables.

**Module call** `UI.getXmlTable` · **See also** [`ui.set`](#uiset)

### `ui.set_attribute`

```lua
ui.set_attribute(id, name, value)
```

Changes one attribute of an element; it lands when the call ends.

| Parameter | Type | Description |
|---|---|---|
| `id` | `string` | The element's id, in any letter case. |
| `name` | `string` | The attribute, as in the XML (`text`, `color`, `active`, `percentage`…). `class` changes only what `ui.xml` gives back: use `ui.set_class`. |
| `value` | `string\|number\|boolean` | The value; true and false are written True and False. |

**Returns** `boolean`: true when the element is there now and both name and value are given.

```lua
ui.set_attribute("time", "text", game.time:sub(12, 16))
```

**Module call** `UI.setAttribute` · **See also** [`ui.set_attributes`](#uiset_attributes), [`ui.get_attribute`](#uiget_attribute)

### `ui.set_attributes`

```lua
ui.set_attributes(id, table)
```

Changes several attributes at once.

| Parameter | Type | Description |
|---|---|---|
| `id` | `string` | The element's id. |
| `table` | `table` | Attribute → value. |

**Returns** `boolean`: true when the element is there now.

**Module call** `UI.setAttributes` · **See also** [`ui.set_attribute`](#uiset_attribute)

### `ui.get_attribute`

```lua
ui.get_attribute(id, name)
```

Reads an attribute of the element as it stands, with what the panels' Defaults give it, or nil.

| Parameter | Type | Description |
|---|---|---|
| `id` | `string` | The element's id. |
| `name` | `string` | The attribute. |

**Returns** `string?`.

**Module call** `UI.getAttribute` · **See also** [`ui.set_attribute`](#uiset_attribute)

### `ui.show`

```lua
ui.show(id)
```

Shows an element.

| Parameter | Type | Description |
|---|---|---|
| `id` | `string` | The element's id. |

**Returns** `boolean`: true when the element is there now.

**Module call** `UI.show` · **See also** [`ui.hide`](#uihide)

### `ui.hide`

```lua
ui.hide(id)
```

Hides an element; one inside a hidden element stays as it is.

| Parameter | Type | Description |
|---|---|---|
| `id` | `string` | The element's id. |

**Returns** `boolean`: true when the element is there now.

**Module call** `UI.hide` · **See also** [`ui.show`](#uishow)

### `ui.set_value`

```lua
ui.set_value(id, value)
```

Sets the value the panels hold between an element's tags; a Text element shows it as its words.

| Parameter | Type | Description |
|---|---|---|
| `id` | `string` | The element's id. |
| `value` | `string\|number\|boolean` | The new value. |

**Returns** `boolean`: true when the element is there now and a value is given.

**Module call** `UI.setValue` · **See also** [`ui.get_value`](#uiget_value)

### `ui.get_value`

```lua
ui.get_value(id)
```

Reads the value held between an element's tags, or nil.

| Parameter | Type | Description |
|---|---|---|
| `id` | `string` | The element's id, in its exact letter case. |

**Returns** `string?`.

**Module call** `UI.getValue` · **See also** [`ui.set_value`](#uiset_value)

### `ui.get_attributes`

```lua
ui.get_attributes(id)
```

Every attribute of the element as it stands, or nil.

| Parameter | Type | Description |
|---|---|---|
| `id` | `string` | The element's id. |

**Returns** `table?`.

**Module call** `UI.getAttributes` · **See also** [`ui.get_attribute`](#uiget_attribute)

### `ui.set_class`

```lua
ui.set_class(id, classes)
```

Sets an element's classes, which take their looks from the Defaults the panels hold.

| Parameter | Type | Description |
|---|---|---|
| `id` | `string` | The element's id. |
| `classes` | `string` | Class names, separated by spaces. |

**Returns** `boolean`: true when the element is there now.

**Module call** `UI.setClass`

### `ui.pictures`

```lua
ui.pictures()
```

The pictures panels name: a list of { type =, name =, url = }.

**Returns** `table`.

**Module call** `UI.getCustomAssets` · **See also** [`ui.set_pictures`](#uiset_pictures)

### `ui.set_pictures`

```lua
ui.set_pictures(list)
```

Replaces the pictures panels name, at once, and builds the panels again from what they hold.

| Parameter | Type | Description |
|---|---|---|
| `list` | `table` | { name =, url =, type = } entries; url is a picture of a pack. |

**Returns** `boolean`.

**Module call** `UI.setCustomAssets` · **See also** [`ui.pictures`](#uipictures)

### `ui.loading`

```lua
ui.loading
```

true from setting panels until they land, and while panels that did not read are in place.

**Type** `boolean`, read-only.

**Module call** `UI.loading`

### `ui.assetType`

```lua
ui.assetType
```

{ Image = 0, AssetBundle = 1 }, also as image and assetBundle, as module scripts name picture types.

**Type** `table`, read-only.

**Module call** `UI.assetType`

## wait

Timers are not saved: a script that must go on after a reload starts its timers again in on_load. A timer belongs to the script that made it and stops with it. In a frame, the timers counting frames fire first, then those counting seconds, then the conditions, each kind in the order it was set.

| Name | What it does |
|---|---|
| [`wait.seconds`](#waitseconds) | Calls fn once, s seconds from now; returns a handle. |
| [`wait.frames`](#waitframes) | Calls fn once, n frames from now; returns a handle. |
| [`wait.condition`](#waitcondition) | Calls fn once predicate() is true, or on_timeout() if timeout_s comes first; returns a handle. |
| [`wait.stop`](#waitstop) | Stops a timer before it fires. |
| [`wait.now`](#waitnow) | The scripts' clock: seconds since the table started running scripts. |
| [`wait.frame`](#waitframe) | How many frames the scripts have run since the table started. |
| [`wait.delta`](#waitdelta) | Seconds between the last frame and the one before it. |

### `wait.seconds`

```lua
wait.seconds(s, fn)
```

Calls fn once, s seconds from now; returns a handle.

| Parameter | Type | Description |
|---|---|---|
| `s` | `number` | Seconds from now. |
| `fn` | `function` | Called with no arguments. |

**Returns** `number`: A handle for `wait.stop`.

```lua
local function tick()
  ui.set_attribute("time", "text", os.date("%H:%M:%S"))
  wait.seconds(1, tick)
end
tick()
```

**Module call** `Wait.time` · **See also** [`wait.stop`](#waitstop), [`wait.frames`](#waitframes)

### `wait.frames`

```lua
wait.frames(n, fn)
```

Calls fn once, n frames from now; returns a handle.

| Parameter | Type | Description |
|---|---|---|
| `n` | `number` | _Optional._ Frames from now. Default 1. |
| `fn` | `function` | Called with no arguments. |

**Returns** `number`: A handle for `wait.stop`.

**Module call** `Wait.frames` · **See also** [`wait.seconds`](#waitseconds)

### `wait.condition`

```lua
wait.condition(predicate, fn, timeout_s, on_timeout)
```

Calls fn once predicate() is true, or on_timeout() if timeout_s comes first; returns a handle.

| Parameter | Type | Description |
|---|---|---|
| `predicate` | `function` | Asked every frame; return true to fire. |
| `fn` | `function` | Called once the predicate answers true. |
| `timeout_s` | `number` | _Optional._ Give up after this many seconds. |
| `on_timeout` | `function` | _Optional._ Called when the timeout comes first. |

**Returns** `number`: A handle for `wait.stop`.

```lua
wait.condition(function() return not self.face_down end,
               function() game.say(self.name .. " is revealed") end, 60)
```

A predicate that errors is reported once and dropped.

**Module call** `Wait.condition` · **See also** [`wait.stop`](#waitstop)

### `wait.stop`

```lua
wait.stop(handle)
```

Stops a timer before it fires.

| Parameter | Type | Description |
|---|---|---|
| `handle` | `number` | What `seconds`, `frames` or `condition` returned. A timer that has fired already is no error. |

**Returns** `boolean`: true when a timer was waiting and is stopped now, false when there was none.

**Module call** `Wait.stop`

### `wait.now`

```lua
wait.now()
```

The scripts' clock: seconds since the table started running scripts.

**Returns** `number`.

**Module call** `Time.time`

### `wait.frame`

```lua
wait.frame()
```

How many frames the scripts have run since the table started.

**Returns** `number`.

**Module call** `Time.frame_count`

### `wait.delta`

```lua
wait.delta()
```

Seconds between the last frame and the one before it.

**Returns** `number`.

**Module call** `Time.delta_time`

## json

JSON in and out; numbers are written the same on every PC.

| Name | What it does |
|---|---|
| [`json.encode`](#jsonencode) | A value as JSON. A table whose keys are all positive numbers is an array up to its highest key, gaps as null; an empty table is []; other tables are objects with their keys in sorted order, number keys after the text ones. |
| [`json.decode`](#jsondecode) | JSON as a value; an error when it does not parse. Text after the first value is ignored, and blank text is nil. |

### `json.encode`

```lua
json.encode(value, pretty)
```

A value as JSON. A table whose keys are all positive numbers is an array up to its highest key, gaps as null; an empty table is []; other tables are objects with their keys in sorted order, number keys after the text ones.

| Parameter | Type | Description |
|---|---|---|
| `value` | `any` | A table, string, number, boolean or nil. A function, a table holding itself or a key that is neither text nor a number is an error. |
| `pretty` | `boolean` | _Optional._ true: one key a line, indented by two spaces. |

**Returns** `string`.

```lua
function on_save() return json.encode({ hits = hits, orders = orders }) end
```

Text is written as it is, apart from quotes, backslashes and control characters; NaN is null.

**Module call** `JSON.encode` · **See also** [`json.decode`](#jsondecode), [`on_save`](#on_save)

### `json.decode`

```lua
json.decode(text)
```

JSON as a value; an error when it does not parse. Text after the first value is ignored, and blank text is nil.

| Parameter | Type | Description |
|---|---|---|
| `text` | `string` | The JSON. Anything else is an error. |

**Returns** `any`.

```lua
function on_load(saved)
  local ok, data = pcall(json.decode, saved)
  if ok and type(data) == "table" then hits, orders = data.hits or 0, data.orders end
end
```

**Module call** `JSON.decode` · **See also** [`json.encode`](#jsonencode), [`on_load`](#on_load)

## Events of a piece script

Define any of these in a piece's script and the table calls it.

| Name | What it does |
|---|---|
| [`on_load`](#on_load) | The script starts: on hosting, resuming, a reload, a save loaded. saved is what on_save last returned, or "". |
| [`on_save`](#on_save) | The game is being saved, or an umpire's action has just run the script: return a string to keep. |
| [`on_update`](#on_update) | Every frame on the host. Keep it short: it runs under the same time limit as any call. If it fails again within a second, the script is off until Save and reload. |
| [`on_drop`](#on_drop) | An umpire let the piece go. |
| [`on_roll`](#on_roll) | An umpire rolled this die. |
| [`on_pick_up`](#on_pick_up) | An umpire picked the piece up. |
| [`on_page`](#on_page) | This book turned to another page. |
| [`on_effect`](#on_effect) | An umpire or a script started (0) or stopped (-1) this effect. |
| [`on_burst`](#on_burst) | This effect was played once, from its menu (index 0) or by a script's play_effect (the index it gave). |
| [`on_state`](#on_state) | Its state changed, by anyone. |
| [`on_flip`](#on_flip) | It was turned over or back. |
| [`on_rotate`](#on_rotate) | An umpire turned it in place. |
| [`on_turn`](#on_turn) | The clock advanced. |
| [`on_destroy`](#on_destroy) | The piece is about to go. |

### `on_load`

```lua
on_load(saved)
```

The script starts: on hosting, resuming, a reload, a save loaded. saved is what on_save last returned, or "".

An event: define this function and the table calls it.

| Parameter | Type | Description |
|---|---|---|
| `saved` | `string` | What `on_save` last returned, or "" the first time. |

```lua
local hits = 0

function on_load(saved)
  hits = tonumber(saved) or 0
  self.button{ z = 0.3, label = tostring(hits), font = 0.08 }
end

function on_save() return tostring(hits) end
```

Draw widgets and register hotkeys and menu rows here. `on_load` and `on_save` get ten seconds; other calls one.

**Module call** `onLoad` · **See also** [`on_save`](#on_save), [`self.reload`](#selfreload)

### `on_save`

```lua
on_save()
```

The game is being saved, or an umpire's action has just run the script: return a string to keep.

An event: define this function and the table calls it.

**Returns**: A string to keep; it comes back to `on_load`.

It runs once a second while something changed, at every ended turn, on close, and after each umpire action that ran the script, so Ctrl+Z takes the script's state back with the rest.

**Module call** `onSave` · **See also** [`on_load`](#on_load), [`json.encode`](#jsonencode)

### `on_update`

```lua
on_update()
```

Every frame on the host. Keep it short: it runs under the same time limit as any call. If it fails again within a second, the script is off until Save and reload.

An event: define this function and the table calls it.

Prefer `wait.seconds` or an event: a script that does its work every frame costs the host every frame.

**Module call** `onUpdate`

### `on_drop`

```lua
on_drop(who)
```

An umpire let the piece go.

An event: define this function and the table calls it.

| Parameter | Type | Description |
|---|---|---|
| `who` | `Who` | The umpire. |

**Module call** `onDrop` · **See also** [`on_pick_up`](#on_pick_up)

### `on_roll`

```lua
on_roll(who)
```

An umpire rolled this die.

An event: define this function and the table calls it.

| Parameter | Type | Description |
|---|---|---|
| `who` | `Who` | The umpire. |

**Module call** `onRandomize`

### `on_pick_up`

```lua
on_pick_up(who)
```

An umpire picked the piece up.

An event: define this function and the table calls it.

| Parameter | Type | Description |
|---|---|---|
| `who` | `Who` | The umpire. |

**Module call** `onPickUp` · **See also** [`on_drop`](#on_drop)

### `on_page`

```lua
on_page()
```

This book turned to another page.

An event: define this function and the table calls it.

**Module call** `onPageChange`

### `on_effect`

```lua
on_effect(index)
```

An umpire or a script started (0) or stopped (-1) this effect.

An event: define this function and the table calls it.

**Module call** `onObjectLoopingEffect`

### `on_burst`

```lua
on_burst(index)
```

This effect was played once, from its menu (index 0) or by a script's play_effect (the index it gave).

An event: define this function and the table calls it.

**Module call** `onObjectTriggerEffect`

### `on_state`

```lua
on_state(old, new, old_guid)
```

Its state changed, by anyone.

An event: define this function and the table calls it.

| Parameter | Type | Description |
|---|---|---|
| `old` | `number` | The state it left, from 1. |
| `new` | `number` | The state it is in now, from 1. |
| `old_guid` | `string` | The module GUID of the state it left, for a piece converted from a module; its own GUID otherwise. |

```lua
function on_state(old, new)
  if new == 1 then draw_name() end
end
```

What the script draws here belongs to the new state. A converted piece whose states are whole module objects changes as the module's game changes them: the leaving state's script saves (on_save) and ends (on_destroy), the arriving state wears its own name, notes, tags, lock and script, which loads (on_load), then on_spawn and on_state run.

**Module call** `onStateChange` · **See also** [`self.set_state`](#selfset_state)

### `on_flip`

```lua
on_flip(face_down)
```

It was turned over or back.

An event: define this function and the table calls it.

| Parameter | Type | Description |
|---|---|---|
| `face_down` | `boolean` | true when it is face down now. |

**See also** [`self.flip`](#selfflip)

### `on_rotate`

```lua
on_rotate(rot, old_rot, who)
```

An umpire turned it in place.

An event: define this function and the table calls it.

| Parameter | Type | Description |
|---|---|---|
| `rot` | `number` | The new heading in degrees. |
| `old_rot` | `number` | The heading before. |
| `who` | `Who` | The umpire. |

**Module call** `onRotate`

### `on_turn`

```lua
on_turn(turn)
```

The clock advanced.

An event: define this function and the table calls it.

| Parameter | Type | Description |
|---|---|---|
| `turn` | `number` | The new turn number. |

```lua
function on_turn(turn)
  self.set_counter("Fatigue", self.counters.Fatigue + 1)
end
```

**Module call** `onPlayerTurn` · **See also** [`game.turn`](#gameturn)

### `on_destroy`

```lua
on_destroy()
```

The piece is about to go.

An event: define this function and the table calls it.

**Module call** `onDestroy` · **See also** [`self.delete`](#selfdelete)

## Events of the global script

The global script gets on_load, on_save, on_update and on_turn too, and these for every piece on the table.

| Name | What it does |
|---|---|
| [`on_spawn`](#on_spawn-global-script) | A piece came onto the table. |
| [`on_destroy`](#on_destroy-global-script) | A piece is about to go. |
| [`on_drop`](#on_drop-global-script) | An umpire let a piece go. |
| [`on_state`](#on_state-global-script) | A piece's state changed, by anyone. |
| [`on_page`](#on_page-global-script) | A book turned to another page. |
| [`on_effect`](#on_effect-global-script) | An umpire or a script started (0) or stopped (-1) an effect. |
| [`on_burst`](#on_burst-global-script) | An effect was played once, from its menu or by a script. |
| [`on_roll`](#on_roll-global-script) | An umpire rolled a die, or turned a piece of several looks to one at random. |
| [`on_rotate`](#on_rotate-global-script) | An umpire turned a piece in place. |
| [`on_pick_up`](#on_pick_up-global-script) | An umpire picked a piece up. |
| [`on_player_action`](#on_player_action-global-script) | An umpire is about to delete or flip pieces: return false to refuse it, with action "delete" or "flip" and the pieces it would touch. |
| [`try_stow`](#try_stow-global-script) | A piece is about to be put into another: return false to keep it out. |
| [`on_stowed`](#on_stowed-global-script) | A piece went into another, off the table. |
| [`on_unstowed`](#on_unstowed-global-script) | A held piece came back onto the table; on_spawn runs for it as well. |
| [`on_number_typed`](#on_number_typed-global-script) | An umpire typed a number over a piece: return true to keep its state, else the state becomes n. |
| [`on_chat`](#on_chat-global-script) | An umpire wrote a line in the Log (not a script's line). |
| [`on_scripting_button_down`](#on_scripting_button_down-global-script) | Defining it gives ten hotkeys, Scripting button 1–10 (the keypad until rebound). |
| [`on_scripting_button_up`](#on_scripting_button_up-global-script) | The same, on the key's release. |

### `on_spawn` (global script)

```lua
on_spawn(piece)
```

A piece came onto the table.

An event: define this function and the table calls it.

| Parameter | Type | Description |
|---|---|---|
| `piece` | `Piece` | The new piece. |

**Module call** `onObjectSpawn` · **See also** [`board.spawn`](#boardspawn)

### `on_destroy` (global script)

```lua
on_destroy(piece)
```

A piece is about to go.

An event: define this function and the table calls it.

| Parameter | Type | Description |
|---|---|---|
| `piece` | `Piece` | The piece; its fields still answer. |

**Module call** `onObjectDestroy`

### `on_drop` (global script)

```lua
on_drop(piece, who)
```

An umpire let a piece go.

An event: define this function and the table calls it.

| Parameter | Type | Description |
|---|---|---|
| `piece` | `Piece` | The piece. |
| `who` | `Who` | The umpire. |

```lua
function on_drop(piece, who)
  for _, z in ipairs(board.zones()) do
    if z.contains(piece) then game.say((piece.name or piece.id) .. " enters " .. z.name, { to = who }) end
  end
end
```

**Module call** `onObjectDrop`

### `on_state` (global script)

```lua
on_state(piece, old, new, old_guid)
```

A piece's state changed, by anyone.

An event: define this function and the table calls it.

**Module call** `onObjectStateChange`

### `on_page` (global script)

```lua
on_page(piece)
```

A book turned to another page.

An event: define this function and the table calls it.

**Module call** `onObjectPageChange`

### `on_effect` (global script)

```lua
on_effect(piece, index)
```

An umpire or a script started (0) or stopped (-1) an effect.

An event: define this function and the table calls it.

**Module call** `onObjectLoopingEffect`

### `on_burst` (global script)

```lua
on_burst(piece, index)
```

An effect was played once, from its menu or by a script.

An event: define this function and the table calls it.

**Module call** `onObjectTriggerEffect`

### `on_roll` (global script)

```lua
on_roll(piece, who)
```

An umpire rolled a die, or turned a piece of several looks to one at random.

An event: define this function and the table calls it.

| Parameter | Type | Description |
|---|---|---|
| `piece` | `Piece` | The piece. |
| `who` | `Who` | The umpire. |

**Module call** `onObjectRandomize`

### `on_rotate` (global script)

```lua
on_rotate(piece, rot, old_rot, who)
```

An umpire turned a piece in place.

An event: define this function and the table calls it.

| Parameter | Type | Description |
|---|---|---|
| `piece` | `Piece` | The piece. |
| `rot` | `number` | The new heading in degrees. |
| `old_rot` | `number` | The heading before. |
| `who` | `Who` | The umpire. |

**Module call** `onObjectRotate`

### `on_pick_up` (global script)

```lua
on_pick_up(piece, who)
```

An umpire picked a piece up.

An event: define this function and the table calls it.

| Parameter | Type | Description |
|---|---|---|
| `piece` | `Piece` | The piece. |
| `who` | `Who` | The umpire. |

**Module call** `onObjectPickUp`

### `on_player_action` (global script)

```lua
on_player_action(who, action, pieces)
```

An umpire is about to delete or flip pieces: return false to refuse it, with action "delete" or "flip" and the pieces it would touch.

An event: define this function and the table calls it.

| Parameter | Type | Description |
|---|---|---|
| `who` | `Who` | The umpire. |
| `action` | `string` | "delete" or "flip". |
| `pieces` | `Piece[]` | The pieces the action would touch. |

**Module call** `onPlayerAction`

### `try_stow` (global script)

```lua
try_stow(holder, piece)
```

A piece is about to be put into another: return false to keep it out.

An event: define this function and the table calls it.

| Parameter | Type | Description |
|---|---|---|
| `holder` | `Piece` | The piece that would hold it. |
| `piece` | `Piece` | The piece going in. |

**Module call** `tryObjectEnterContainer`

### `on_stowed` (global script)

```lua
on_stowed(holder, piece)
```

A piece went into another, off the table.

An event: define this function and the table calls it.

| Parameter | Type | Description |
|---|---|---|
| `holder` | `Piece` | The piece holding it. |
| `piece` | `Piece` | The piece held; its fields still answer, its changes do not. |

**Module call** `onObjectEnterContainer`

### `on_unstowed` (global script)

```lua
on_unstowed(holder, piece)
```

A held piece came back onto the table; on_spawn runs for it as well.

An event: define this function and the table calls it.

| Parameter | Type | Description |
|---|---|---|
| `holder` | `Piece` | The piece that held it. |
| `piece` | `Piece` | The piece back on the table. |

**Module call** `onObjectLeaveContainer`

### `on_number_typed` (global script)

```lua
on_number_typed(piece, who, n)
```

An umpire typed a number over a piece: return true to keep its state, else the state becomes n.

An event: define this function and the table calls it.

| Parameter | Type | Description |
|---|---|---|
| `piece` | `Piece` | The piece under the pointer, or each piece of the selection. |
| `who` | `Who` | The umpire. |
| `n` | `number` | The number typed. |

**Returns**: true to keep the piece's state; anything else lets the number set it.

**Module call** `onObjectNumberTyped`

### `on_chat` (global script)

```lua
on_chat(text, who)
```

An umpire wrote a line in the Log (not a script's line).

An event: define this function and the table calls it.

| Parameter | Type | Description |
|---|---|---|
| `text` | `string` | The line. |
| `who` | `Who` | The umpire. |

```lua
function on_chat(text, who)
  if text == "!strength" then
    for _, p in ipairs(game.selected(who)) do
      game.say((p.name or p.id) .. ": " .. tostring(p.counters.Strength), { to = who })
    end
  end
end
```

**Module call** `onChat`

### `on_scripting_button_down` (global script)

```lua
on_scripting_button_down(index, who)
```

Defining it gives ten hotkeys, Scripting button 1–10 (the keypad until rebound).

An event: define this function and the table calls it.

| Parameter | Type | Description |
|---|---|---|
| `index` | `number` | Which button, 1 to 10. |
| `who` | `Who` | The umpire. |

**Module call** `onScriptingButtonDown`

### `on_scripting_button_up` (global script)

```lua
on_scripting_button_up(index, who)
```

The same, on the key's release.

An event: define this function and the table calls it.

| Parameter | Type | Description |
|---|---|---|
| `index` | `number` | Which button, 1 to 10. |
| `who` | `Who` | The umpire. |

**Module call** `onScriptingButtonUp`

## Lua's own library

Lua 5.2 (MoonSharp). Not there: io, load, loadstring, dofile, require, debug, and os beyond the clock. The full manual: https://www.lua.org/manual/5.2/

### Basic functions

| Function | What it does |
|---|---|
| `pairs(t)` | Every key and value of a table, in no set order. |
| `ipairs(t)` | t[1], t[2] … up to the first nil, in order. |
| `next(t, k)` | The key after k, and its value. |
| `select(n, ...)` | The arguments from the nth on; select("#", ...) counts them. |
| `type(v)` | "nil", "number", "string", "boolean", "table" or "function". |
| `tostring(v)` | A value as text. |
| `tonumber(v, base)` | A numeral as a number, or nil when it is not one. |
| `pcall(fn, ...)` | Calls fn and catches its error: true and the results, or false and the message. |
| `error(message)` | Stops the call with an error. |
| `assert(v, message)` | An error when v is false or nil; v otherwise. |
| `setmetatable(t, mt)` | Gives a table a metatable. |
| `getmetatable(t)` | A table's metatable. |
| `rawget(t, k)` | Reads a key without the metatable. |
| `rawset(t, k, v)` | Writes a key without the metatable. |
| `unpack(t)` | A list's values as separate values. |

### `string`

| Function | What it does |
|---|---|
| `string.byte(s, i)` | The character code at i. |
| `string.char(...)` | Characters from codes. |
| `string.find(s, pattern, init, plain)` | Where a pattern first matches: start and end. |
| `string.format(fmt, ...)` | printf-style formatting: %d, %s, %.2f… |
| `string.gmatch(s, pattern)` | An iterator over every match. |
| `string.gsub(s, pattern, repl)` | Replaces matches; the new string and the count. |
| `string.len(s)` | Its length in bytes. |
| `string.lower(s)` | In lower case. |
| `string.match(s, pattern)` | The first match, or its captures. |
| `string.rep(s, n)` | s repeated n times. |
| `string.reverse(s)` | Back to front. |
| `string.sub(s, i, j)` | The characters from i to j. |
| `string.upper(s)` | In upper case. |

### `table`

| Function | What it does |
|---|---|
| `table.concat(t, sep)` | A list's strings joined. |
| `table.insert(t, pos, v)` | Adds to a list, at the end or at pos. |
| `table.remove(t, pos)` | Takes from a list, the last or at pos, and returns it. |
| `table.sort(t, comp)` | Sorts a list in place. |
| `table.unpack(t)` | A list's values as separate values. |

### `math`

| Function | What it does |
|---|---|
| `math.abs(x)` | The absolute value. |
| `math.ceil(x)` | Rounded up. |
| `math.floor(x)` | Rounded down. |
| `math.max(...)` | The largest. |
| `math.min(...)` | The smallest. |
| `math.sqrt(x)` | The square root. |
| `math.sin(x)` | Sine of radians. |
| `math.cos(x)` | Cosine of radians. |
| `math.tan(x)` | Tangent of radians. |
| `math.atan2(y, x)` | The angle of a vector, in radians. |
| `math.deg(x)` | Radians to degrees. |
| `math.rad(x)` | Degrees to radians. |
| `math.fmod(x, y)` | The remainder of x / y. |
| `math.modf(x)` | The whole and the fractional parts. |
| `math.random(m, n)` | A random number: [0, 1), 1..m, or m..n. Not the server's dice: use game.roll for a roll the umpires see. |
| `math.randomseed(x)` | Seeds math.random. |
| `math.exp(x)` | e to the x. |
| `math.log(x)` | The natural logarithm. |
| `math.pow(x, y)` | x to the y. |
| `math.pi` | π. |
| `math.huge` | A number larger than any other. |

### `os`

| Function | What it does |
|---|---|
| `os.time(t)` | Seconds since 1970, now or for a date table. |
| `os.date(format, time)` | A time as text, or as a table with "*t". |
| `os.clock()` | Seconds of processor time; for measuring. |

### `coroutine`

| Function | What it does |
|---|---|
| `coroutine.create(fn)` | A coroutine from a function. |
| `coroutine.resume(co, ...)` | Runs it until it yields or ends. |
| `coroutine.yield(...)` | Hands control back to resume. |
| `coroutine.status(co)` | "suspended", "running", "normal" or "dead". |
| `coroutine.wrap(fn)` | A coroutine as a function to call. |
| `coroutine.running()` | The running coroutine. |

### Differences from plain Lua

- `tonumber(s, base)` gives nil for what is not a numeral in that base, as Lua does (MoonSharp raised an error nothing could catch).
- `string.rep` refuses to build more than 16 million characters.
- A coroutine runs under the time limit of the call that resumes it, and one call may make 64 at most.
- `table.sort` with a comparison and `string.gsub` with a function run that function under the same limit, so one that never returns is stopped like any other loop.

## Module calls

The converter rewrites a module's scripts into these calls by rule; this is the same map, by the name the module's scripts use. Calls are written with a dot (`self.set_name(x)`), not with a colon, and positions are map points `{ x =, z = }`. The [scripting guide](scripting.md#what-converted-module-scripts-become) says what the converter does with the rest.

| Module | Here |
|---|---|
| `addAttachment` | [`self.attach`](#selfattach) |
| `addContextMenuItem` | [`game.menu_add`](#gamemenu_add) |
| `addContextMenuItem` | [`self.menu_add`](#selfmenu_add) |
| `addHotkey` | [`game.hotkey`](#gamehotkey) |
| `addTag` | [`self.add_tag`](#selfadd_tag) |
| `addToPlayerSelection` | [`game.select`](#gameselect) |
| `AssetBundle.playLoopingEffect` | [`self.set_effect_on`](#selfset_effect_on) |
| `AssetBundle.playTriggerEffect` | [`self.play_effect`](#selfplay_effect) |
| `Book.page_offset` | [`self.page_offset`](#selfpage_offset) |
| `Book.setHighlight` | [`self.book_highlight`](#selfbook_highlight) |
| `Book.setHighlight` | [`self.set_book_highlight`](#selfset_book_highlight) |
| `broadcastToAll` | [`game.say`](#gamesay) |
| `call` | [`self.call`](#selfcall) |
| `clearButtons` | [`self.clear_widgets`](#selfclear_widgets) |
| `clearContextMenu` | [`game.menu_clear`](#gamemenu_clear) |
| `clearContextMenu` | [`self.menu_clear`](#selfmenu_clear) |
| `clearHotkeys` | [`game.hotkeys_clear`](#gamehotkeys_clear) |
| `Container.search` | [`self.show_contents`](#selfshow_contents) |
| `createButton` | [`self.button`](#selfbutton) |
| `createInput` | [`self.input`](#selfinput) |
| `destruct` | [`self.delete`](#selfdelete) |
| `drag_selectable` | [`self.box_selected`](#selfbox_selected) |
| `drag_selectable` | [`self.set_box_selected`](#selfset_box_selected) |
| `editButton` | [`self.edit`](#selfedit) |
| `flip` | [`self.flip`](#selfflip) |
| `getAttachments` | [`self.attached`](#selfattached) |
| `getBounds` | [`self.middle`](#selfmiddle) |
| `getBounds` | [`self.size`](#selfsize) |
| `getButtons` | [`self.widgets`](#selfwidgets) |
| `getColorTint` | [`self.tint`](#selftint) |
| `getDecals` | [`self.decals`](#selfdecals) |
| `getDescription` | [`self.note`](#selfnote) |
| `getGMNotes` | [`self.gm_notes`](#selfgm_notes) |
| `getGUID` | [`self.guid`](#selfguid) |
| `getHoverObject` | [`game.hovered`](#gamehovered) |
| `getLock` | [`self.locked`](#selflocked) |
| `getLuaScript` | [`self.script`](#selfscript) |
| `getName` | [`self.name`](#selfname) |
| `getObjectFromGUID` | [`board.find`](#boardfind) |
| `getObjects` | [`board.pieces`](#boardpieces) |
| `getObjects` | [`self.stowed`](#selfstowed) |
| `getPlayers` | [`game.umpires`](#gameumpires) |
| `getPosition` | [`self.pos`](#selfpos) |
| `getQuantity` | [`self.holds`](#selfholds) |
| `getRotation` | [`self.rot`](#selfrot) |
| `getRotationValue` | [`self.value`](#selfvalue) |
| `getRotationValues` | [`self.faces`](#selffaces) |
| `getScale` | [`self.scale`](#selfscale) |
| `getSelectedObjects` | [`game.selected`](#gameselected) |
| `getStateId` | [`self.state`](#selfstate) |
| `getStates` | [`self.states`](#selfstates) |
| `getTags` | [`self.tags`](#selftags) |
| `getValue` | [`self.face`](#selfface) |
| `getVar` | [`self.get_var`](#selfget_var) |
| `getVectorLines` | [`self.lines`](#selflines) |
| `getZones` | [`board.zones`](#boardzones) |
| `Global.call` | [`game.call`](#gamecall) |
| `Global.getVar` | [`game.get_var`](#gameget_var) |
| `Global.getVectorLines` | [`board.strokes`](#boardstrokes) |
| `Global.setVar` | [`game.set_var`](#gameset_var) |
| `Global.setVectorLines` | [`board.draw`](#boarddraw) |
| `held_by_color` | [`self.held_by`](#selfheld_by) |
| `highlightOn` | [`self.highlight`](#selfhighlight) |
| `highlightOn` | [`self.set_highlight`](#selfset_highlight) |
| `interactable` | [`self.interactable`](#selfinteractable) |
| `interactable` | [`self.set_interactable`](#selfset_interactable) |
| `isDestroyed` | [`self.exists`](#selfexists) |
| `is_face_down` | [`self.face_down`](#selfface_down) |
| `JSON` | [`json`](#json) |
| `JSON.decode` | [`json.decode`](#jsondecode) |
| `JSON.encode` | [`json.encode`](#jsonencode) |
| `log` | [`log`](#log) |
| `measure_movement` | [`self.measures_moves`](#selfmeasures_moves) |
| `measure_movement` | [`self.set_measures_moves`](#selfset_measures_moves) |
| `memo` | [`self.memo`](#selfmemo) |
| `name` | [`self.source`](#selfsource) |
| `onChat` | [`on_chat`](#on_chat-global-script) |
| `onDestroy` | [`on_destroy`](#on_destroy) |
| `onDrop` | [`on_drop`](#on_drop) |
| `onLoad` | [`on_load`](#on_load) |
| `onObjectDestroy` | [`on_destroy`](#on_destroy-global-script) |
| `onObjectDrop` | [`on_drop`](#on_drop-global-script) |
| `onObjectEnterContainer` | [`on_stowed`](#on_stowed-global-script) |
| `onObjectLeaveContainer` | [`on_unstowed`](#on_unstowed-global-script) |
| `onObjectLoopingEffect` | [`on_effect`](#on_effect) |
| `onObjectLoopingEffect` | [`on_effect`](#on_effect-global-script) |
| `onObjectNumberTyped` | [`on_number_typed`](#on_number_typed-global-script) |
| `onObjectPageChange` | [`on_page`](#on_page-global-script) |
| `onObjectPickUp` | [`on_pick_up`](#on_pick_up-global-script) |
| `onObjectRandomize` | [`on_roll`](#on_roll-global-script) |
| `onObjectRotate` | [`on_rotate`](#on_rotate-global-script) |
| `onObjectSpawn` | [`on_spawn`](#on_spawn-global-script) |
| `onObjectStateChange` | [`on_state`](#on_state-global-script) |
| `onObjectTriggerEffect` | [`on_burst`](#on_burst) |
| `onObjectTriggerEffect` | [`on_burst`](#on_burst-global-script) |
| `onPageChange` | [`on_page`](#on_page) |
| `onPickUp` | [`on_pick_up`](#on_pick_up) |
| `onPlayerAction` | [`on_player_action`](#on_player_action-global-script) |
| `onPlayerTurn` | [`on_turn`](#on_turn) |
| `onRandomize` | [`on_roll`](#on_roll) |
| `onRotate` | [`on_rotate`](#on_rotate) |
| `onSave` | [`on_save`](#on_save) |
| `onScriptingButtonDown` | [`on_scripting_button_down`](#on_scripting_button_down-global-script) |
| `onScriptingButtonUp` | [`on_scripting_button_up`](#on_scripting_button_up-global-script) |
| `onStateChange` | [`on_state`](#on_state) |
| `onUpdate` | [`on_update`](#on_update) |
| `Physics.cast` | [`board.show_cast`](#boardshow_cast) |
| `pick_up_position` | [`self.picked_up`](#selfpicked_up) |
| `positionToLocal` | [`self.to_local`](#selfto_local) |
| `positionToWorld` | [`self.to_world`](#selfto_world) |
| `print` | [`print`](#print) |
| `putObject` | [`self.stow`](#selfstow) |
| `randomize` | [`self.roll`](#selfroll) |
| `reload` | [`self.reload`](#selfreload) |
| `removeAttachment` | [`self.detach`](#selfdetach) |
| `removeButton` | [`self.remove_widget`](#selfremove_widget) |
| `removeTag` | [`self.remove_tag`](#selfremove_tag) |
| `script_state` | [`self.script_state`](#selfscript_state) |
| `script_state` | [`self.set_script_state`](#selfset_script_state) |
| `self` | [`self`](#self) |
| `setColorTint` | [`self.set_tint`](#selfset_tint) |
| `setCustomObject` | [`self.set_face_picture`](#selfset_face_picture) |
| `setCustomObject` | [`self.set_picture`](#selfset_picture) |
| `setCustomObject` | [`self.set_web_model`](#selfset_web_model) |
| `setDecals` | [`self.set_decals`](#selfset_decals) |
| `setDescription` | [`self.set_note`](#selfset_note) |
| `setLock` | [`self.set_locked`](#selfset_locked) |
| `setLuaScript` | [`self.set_script`](#selfset_script) |
| `setMemo` | [`self.set_memo`](#selfset_memo) |
| `setName` | [`self.set_name`](#selfset_name) |
| `setPosition` | [`self.set_pos`](#selfset_pos) |
| `setPositionSmooth` | [`self.show_glide`](#selfshow_glide) |
| `setRotation` | [`self.set_rot`](#selfset_rot) |
| `setRotationValue` | [`self.set_value`](#selfset_value) |
| `setRotationValues` | [`self.set_faces`](#selfset_faces) |
| `setScale` | [`self.set_scale`](#selfset_scale) |
| `setState` | [`self.set_state`](#selfset_state) |
| `setTags` | [`self.set_tags`](#selfset_tags) |
| `setValue` | [`self.set_face`](#selfset_face) |
| `setVar` | [`self.set_var`](#selfset_var) |
| `setVectorLines` | [`self.set_lines`](#selfset_lines) |
| `showInputDialog` | [`game.dialog`](#gamedialog) |
| `spawnObject` | [`board.spawn`](#boardspawn) |
| `sticky` | [`self.set_sticky`](#selfset_sticky) |
| `sticky` | [`self.sticky`](#selfsticky) |
| `takeObject` | [`self.unstow`](#selfunstow) |
| `TextTool.getValue` | [`self.lettering`](#selflettering) |
| `TextTool.setValue` | [`self.set_lettering`](#selfset_lettering) |
| `Time.delta_time` | [`wait.delta`](#waitdelta) |
| `Time.frame_count` | [`wait.frame`](#waitframe) |
| `Time.time` | [`wait.now`](#waitnow) |
| `tryObjectEnterContainer` | [`try_stow`](#try_stow-global-script) |
| `UI` | [`self.ui`](#selfui) |
| `UI` | [`ui`](#ui) |
| `UI.assetType` | [`ui.assetType`](#uiassettype) |
| `UI.getAttribute` | [`ui.get_attribute`](#uiget_attribute) |
| `UI.getAttributes` | [`ui.get_attributes`](#uiget_attributes) |
| `UI.getCustomAssets` | [`ui.pictures`](#uipictures) |
| `UI.getValue` | [`ui.get_value`](#uiget_value) |
| `UI.getXml` | [`ui.xml`](#uixml) |
| `UI.getXmlTable` | [`ui.tree`](#uitree) |
| `UI.hide` | [`ui.hide`](#uihide) |
| `UI.loading` | [`ui.loading`](#uiloading) |
| `UI.setAttribute` | [`ui.set_attribute`](#uiset_attribute) |
| `UI.setAttributes` | [`ui.set_attributes`](#uiset_attributes) |
| `UI.setClass` | [`ui.set_class`](#uiset_class) |
| `UI.setCustomAssets` | [`ui.set_pictures`](#uiset_pictures) |
| `UI.setValue` | [`ui.set_value`](#uiset_value) |
| `UI.setXml` | [`ui.set`](#uiset) |
| `UI.show` | [`ui.show`](#uishow) |
| `value` | [`self.set_worth`](#selfset_worth) |
| `value` | [`self.worth`](#selfworth) |
| `value_flags` | [`self.worth_groups`](#selfworth_groups) |
| `Wait` | [`wait`](#wait) |
| `Wait.condition` | [`wait.condition`](#waitcondition) |
| `Wait.frames` | [`wait.frames`](#waitframes) |
| `Wait.stop` | [`wait.stop`](#waitstop) |
| `Wait.time` | [`wait.seconds`](#waitseconds) |

## Compatibility

This is version 1 of the scripting API. A pack or scenario records the version it was made for (`apiVersion` in its pack.json or scenario.json; one without it is taken to be this version), and a build refuses one made for a newer API, or in a newer pack or scenario format, with a message to update the app, rather than run scripts that call what it does not have.

**What breaks a script**, and so takes a new version: a name removed or renamed, an argument dropped, reordered or read differently, a value returned in another type or shape, an event called at another moment or with other arguments. Adding a name, an optional argument at the end or a field to a returned table breaks nothing and keeps the version.

**Deprecation.** A name is deprecated for at least one release before it is removed: it still works, and the console notes once per game that it is going and what to use instead. The removal is what takes the new version.
