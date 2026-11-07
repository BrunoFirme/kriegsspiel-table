# Scripting guide

Scripts give a piece what the piece studio cannot: its own buttons, a number on the face, panels, reactions to
clicks, drops and turns. They are Lua 5.2, with an API built for this table.

Look names up in the [API reference](scripting-api.md); **F1** on a word in a script editor opens it there.

## Studio first

Counters, text lines, names, looks, markers and tags need no script: set them in the
[piece studio](community-guide.md#the-piece-studio). They print the same everywhere and cannot break.

Script only for: buttons and input boxes of your own, lines round a piece, screen panels, and pieces
that react.

## Quick start

A block that shows its strength, with a button to take a loss (right-click to restore one).

1. **Piece studio** → your pack → **Edit pieces** → the piece → **Script** tab.
2. Paste:

```lua
local strength = 12

function on_load(saved)
  strength = tonumber(saved) or strength
  draw()
end

function on_save()
  return tostring(strength)
end

function draw()
  self.clear_widgets()
  self.button{ z = -0.12, label = tostring(strength), font = 0.12, text_color = "white" }
  self.button{ z = 0.14, w = 0.3, h = 0.1, label = "Loss", font = 0.06, on_click = "loss" }
end

function loss(who, alt)
  strength = math.max(0, strength + (alt and 1 or -1))
  draw()
  game.say(who.name .. ": " .. (self.name or "block") .. " at " .. strength)
end
```

3. **Save and run** (Ctrl+Enter), then try it in the **Try it** view (Alt+3).

`on_load` gets back what `on_save` returned, so the number survives saves. The first button has no `w`/`h`, so
it is a plain label. Each click is one change; Ctrl+Z undoes it.

## Where scripts live

| Script | Written in | Runs for |
|---|---|---|
| Kind script | Piece studio → **Script** tab | every piece of that kind, each with its own `self` |
| Piece script | Right-click a piece → **Scripting…** | that piece only, instead of its kind's |
| Global script | Scenario properties → **Script** | the whole table; `self` is nil |

Scripts run on the host only. Their changes reach others like any umpire's change, so observers never see more
than they should.

## The life of a script

| When | The table calls |
|---|---|
| Hosting, resuming, reload, a save loaded | every script's top-level code, then every `on_load(saved)`; the global script first each time |
| A click, typing, a menu row, a hotkey | the handler it names |
| Drop, pick up, turn, turn over, state change | `on_drop`, `on_pick_up`, `on_rotate`, `on_flip`, `on_state` |
| A roll, a book's page turned, an effect started or stopped | `on_roll`, `on_page`, `on_effect` |
| Next turn | `on_turn(turn)` |
| Saving, and after each umpire action | `on_save()` |
| Deletion | `on_destroy()` |
| Every frame | `on_update()`, if defined |

**Keep state with `on_save`.** Return a string; it comes back in `on_load`. Tables go through `json`:

```lua
local data = { hits = 0, orders = "Hold" }

function on_load(saved)
  local ok, t = pcall(json.decode, saved)
  if ok and type(t) == "table" then data = t end
end

function on_save() return json.encode(data) end
```

Because `on_save` runs after each umpire action, Ctrl+Z also undoes the script's state.

**Draw and register in `on_load`.** Widgets, hotkeys, menu rows and timers are rebuilt on every load.

## Units

| Unit | Used by | Meaning |
|---|---|---|
| Map units | `pos`, `set_pos`, `move`, `spawn` | the table's coordinates |
| Piece units | widgets, `set_lines`, `to_world` | map units on the unscaled piece; centre (0, 0), x right, z down the picture |
| Metres | `board.distance` | map units ÷ `game.calibration` |
| Degrees | `rot`, `set_rot` | headings |

A kind 1.2 wide runs from x = −0.6 to 0.6. Widgets scale with the piece. In the studio's 2D view,
Ctrl+right-click copies the point under the pointer in piece units.

## Drawing on a piece

```lua
self.button{ z = -0.2, label = "3rd Line", font = 0.06 }                          -- a label
self.button{ x = 0.3, z = 0.2, w = 0.2, h = 0.1, label = "+", on_click = "more" }  -- a button
self.input{ face = "back", w = 0.4, h = 0.06, value = name, on_change = "renamed" } -- a text box
self.menu_add("Rally", "rally")                                                   -- right-click row
self.set_lines{ { points = { { x = 0, z = 0 }, { x = 0, z = -4 } }, color = "#d8b766" } }  -- a line
```

- A button without `w` and `h` is a label and takes no clicks.
- `self.edit(i, fields)` changes widget `i`; `self.clear_widgets()` removes them all.
- An input keeps its typed value, for everyone, even without a handler.

### Handlers

| Handler | Called as |
|---|---|
| button `on_click` | `fn(who, alt, index, piece)`, `alt` true on right-click |
| input `on_change` | `fn(who, value, index, piece)` |
| piece menu row | `fn(who, piece)` |

`"fn"` is looked up in the drawing script, then the global script. `"global:fn"` forces the global script;
`"<id>/fn"` another piece's.

## States

> **What a script draws belongs to the state it was drawn in.** Widgets, menu rows and lines show only in that
> state, and come back when the piece returns to it.

- What `on_load` draws belongs to state 1.
- To draw on another state, draw from `on_state(old, new)` or a click while in it.

Converted scripts expect this: in their module each state was its own object.

## Reading and changing the table

```lua
local gun = board.find("a1b2c3")                  -- by id or module GUID
for _, p in ipairs(board.pieces{ tag = "French" }) do print(p.name, p.state) end

self.set_name("1st Bn, 52nd Foot")
self.set_state("Shaken")                          -- name or number
self.move(120, 48, 90)                            -- x, z, heading
self.set_counter("Strength", self.counters.Strength - 1)

local smoke = board.spawn{ def = "std.effects.signal-smoke", x = self.pos.x, z = self.pos.z - 6 }
local d = board.distance(self, gun)               -- metres

game.say("The bridge is down.", { color = "#e07050" })
local r = game.roll(6, 2, "Morale")               -- server dice, shown in the Log
```

- Fields are read-only; `set_` calls change them. One reaction is one undo step.
- A kept handle can outlive its piece: check `p.exists`.
- Use `game.roll` for rolls umpires should see; `math.random` for the script's own choices.

## Working with umpires

| Want | Use |
|---|---|
| A key each umpire binds | `game.hotkey(label, fn)`: `fn(who, piece, point, released)` |
| A row on the table's right-click menu | `game.menu_add(label, fn)`: `fn(who, point)` |
| Their selection or hovered piece | `game.selected(who)`, `game.hovered(who)` |
| Ask them something | `game.dialog(who, { kind = "options", options = {…} }, fn)` |
| A private line in the Log | `game.say(text, { to = who })` |

Global script only: `on_number_typed`, `on_chat`, `on_scripting_button_down/up`.

## Panels on the screen

Panels are written in XML, the same converted modules bring. Clicks and edits call the function the element names.

```lua
ui.set([[<Panel id="sheet" rectAlignment="UpperRight" offsetXY="-20 -60" width="300" height="120" color="#1b1b1bd8">
  <VerticalLayout padding="12 12 12 12" spacing="6">
    <Text fontSize="18" color="#ffffff">Supply</Text>
    <ProgressBar id="supply" percentage="65"/>
    <Button onClick="resupply">Resupply</Button>
  </VerticalLayout>
</Panel>]])

function resupply(who) ui.set_attribute("supply", "percentage", 100) end
```

- Elements: Panel, Vertical/Horizontal/GridLayout, TableLayout (Row, Cell), Text, Button, InputField, Toggle,
  Dropdown (Option), ProgressBar, Slider, Image.
- Place with `rectAlignment` and `offsetXY`; size with `width`/`height`.
- `self.ui` puts panels on the piece itself, with the same calls.
- Limits: 24 levels deep, 20,000 elements.

## Other scripts and timers

```lua
local answer = board.find("c3a91f").call("report", self.name)   -- another piece's function
game.call("log_casualty", self.name)                           -- the global script's

local h = wait.seconds(5, function() game.say("Five seconds") end)
wait.condition(function() return not self.face_down end, reveal, 60)
wait.stop(h)
```

Timers are not saved: start them again in `on_load`.

## Recipes

### Fatigue that grows each turn

```lua
local fatigue = 0

function on_load(saved)
  fatigue = tonumber(saved) or 0
  self.menu_add("Rest", "rest")
  draw()
end

function on_save() return tostring(fatigue) end

function draw()
  self.clear_widgets()
  if fatigue > 0 then self.button{ x = 0.4, z = -0.2, label = "F" .. fatigue, font = 0.08 } end
end

function on_turn() fatigue = fatigue + 1; draw() end
function rest() fatigue = 0; draw() end
```

### Range rings from the right-click menu

```lua
local shown = false

function on_load() self.menu_add("Range rings", "toggle") end

local function ring(metres)
  local r, pts = metres * (game.calibration or 1) / self.scale, {}
  for i = 0, 71 do
    local a = math.rad(i * 5)
    pts[#pts + 1] = { x = math.cos(a) * r, z = math.sin(a) * r }
  end
  return { points = pts, color = "#d8b766", width = 0.15, loop = true }
end

function toggle()
  shown = not shown
  self.set_lines(shown and { ring(100), ring(200), ring(400) } or {})
end
```

### Morale test on a key (global script)

```lua
function on_load()
  game.hotkey("Morale test", function(who, piece)
    if not piece then return end
    local r = game.roll(6, 2, "Morale: " .. (piece.name or piece.id))
    if r[1] + r[2] < 7 then piece.set_state("Shaken") end
  end)
end
```

### A turn clock for every umpire (global script)

```lua
function on_load()
  ui.set([[<Panel rectAlignment="UpperCenter" offsetXY="0 -12" width="240" height="40" color="#151412e0">
    <Text id="time" fontSize="18" color="#e9e5db"></Text>
  </Panel>]])
  show()
end

function show() ui.set_attribute("time", "text", "Turn " .. game.turn .. " · " .. game.time:sub(12, 16)) end
function on_turn() show() end
```

### Tell the mover when a piece enters a zone (global script)

```lua
function on_drop(piece, who)
  for _, z in ipairs(board.zones()) do
    if z.contains(piece) then game.say((piece.name or piece.id) .. " is in " .. z.name, { to = who }) end
  end
end
```

### Orders from a dialog

Needs a text line "Orders" on the kind.

```lua
function on_load() self.menu_add("Orders…", "ask") end

function ask(who)
  game.dialog(who, { kind = "options", title = "Orders", options = { "Advance", "Hold", "Retire" } },
    function(answer) if answer then self.set_text("Orders", answer) end end)
end
```

## Tools

| Tool | Where | For |
|---|---|---|
| Scripts window | Menu → Scripting → Scripts… (Ctrl+Shift+L) | every script, its state, error line, console; Ctrl+S reloads |
| Script console | Menu → Scripting → Script console (`) | prints and errors; the host and leads can run Lua |
| Completion | any script editor | names after `self.`, `board.`…; hover for help; F1 for the reference |
| Checks | a pack's or scenario's ⋯ → Check its scripts… | runs every script offline and lists errors |
| Ids on hover | Settings → Tools for script makers | id, GUID and kind under the pointer |
| VS Code | API reference → **API file for VS Code…** | typed completion in VS Code |
| Edit as a file | beside **Save and run** | the script as a `.lua` file for any editor; each save runs here, errors go to `console.log` |

## When a script goes wrong

- An **error** stops that call; the console shows the piece and line. The script stays loaded.
- A script that **overruns** its time or memory is switched off until reloaded.
- Only that script stops; the table carries on.

Guard risky code with `pcall`.

## Limits and the sandbox

| Limit | Value |
|---|---|
| One call | 0.1 s |
| `on_load`, `on_save` | 10 s |
| Memory per call | 256 MB |
| Calls inside calls | 50 deep |
| Coroutines per call | 64 |

A script cannot reach the network, files or other programs, nor load code from text (`load`, `require`, `io`,
`debug`). Every script is checked on import, install and run. A pack from a stranger can draw on its pieces,
never touch your PC.

## What converted module scripts become

The converter rewrites a module's Lua into this API and puts it in the pack, where you can edit it. The import
report lists lines it could not translate. The [module call table](scripting-api.md#module-calls)
gives each direct match.

- **Direct:** `self.setName(x)` → `self.set_name(x)`, `getObjectFromGUID` → `board.find`, `UI.setXml` → `ui.set`,
  `onLoad` → `on_load`, and so on. XML panels are kept as they are.
- **Through `compat`:** module buttons, positions, player colours, `spawnObject`, `getData`/`spawnObjectData` and
  `Wait.time` work through a compatibility library over this API. Buttons are converted to piece units (one module
  unit is half the piece's height).
- **Pictures:** a module object's decals belong to the state they are on, so each look of an imported piece carries
  its own and they change with it. `setDecals` on such a piece changes the look it is in, and the
  other looks keep theirs; a sheet's editor redraws a symbol it is given. What a script sets while it loads is the copy
  it saved, so the look keeps its own. Decals on web links are downloaded by the host. An input dialog asking for an image or URL
  offers a picture from this computer or a web link. A button or input turned face down above the piece shows
  but takes no clicks, as locked text does.
- **Bags:** `putObject`, `takeObject` (by guid or index, at a position and rotation, with its callback), `getObjects`
  and `getQuantity` run on the piece's own `stow`, `unstow` and `stowed`, so a bag script places and recalls its pieces.
- **Vector and Color:** the module classes come over whole: every method and operator, `v[1]`, named colours, `Color.fromString`,
  `fromHex` and `toHex`; `getColorTint()` answers a Color. The colour and table helpers (`hexToColor`, `copyLuaTable`, `getGlobalVar`…),
  the script state calls and the tag calls run through `compat` too.
- **Panels, dice, books, notes, text, effects:** a save's own XML panels and a piece's come over with the pictures and fonts they
  name, with their layouts, scrolling, dropdowns and inputs; a die's sides answer `getValue`, `setRotationValues` and `randomize`; a book turns by `Book.setPage`; `Notes`
  reads and writes the note cards the module's notes became; 3D text answers `TextTool`; an effect answers `AssetBundle`
  as one looping effect. A picture link a script builds while it runs is fetched by the host and shown on the piece.
- **The engine:** each object's script keeps its own Lua state, so a helper one adds to `string` or `table` stays in it.
  Every script is asked about a player action and any one can refuse it (`onPlayerAction`); `Physics.cast` sweeps
  against the pieces' bounds, `setPositionSmooth` glides on every screen, `highlightOn` rings the piece, and
  `getLuaScript` answers the translated script the piece runs.
- **Noted:** calls with no meaning here (`addForce`, dealing cards, scripting zones) become `compat.todo(...)`, which prints once;
  the import report names each under "Not taken from the module exactly".

Handlers keep their module parameters, bound in a first line:

```lua
function ctr1up(__who, __alt, __index, __piece)
  local obj, color, alt = __piece or self, compat.color_of(__who), __alt
```

Saved state, tags and memos come across; the module's Global script becomes the scenario's.
