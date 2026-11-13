# The umpire's guide

Hosting puts the table on your PC for your co-umpires and anyone you let in to watch. Keys below are the defaults; [Keys and mouse](keys.md) shows yours.

## Before the game

### Packs

A pack is a set of pieces. **Piece studio** on the title screen installs a `.ktpack`, converts a digital
tabletop module, or makes a new pack. The standard dice are always there. The order you give packs there is the
order the table's pieces panel lists them in.

### Scenarios

A scenario is a map plus the pieces on it. **Scenarios → New blank scenario…** (the first card), then add map
sheets in the **Map** drawer (PNG, JPG, WebP, BMP, TIFF, or a GIMP `.xcf`; only TIFF and `.xcf` go past 16,384
pixels a side). Converting a module also makes one. Right-click a
card to file, export or delete it. **Open** puts the table on your PC only: nobody joins until you invite them, so
preparing a scenario and hosting it are the same step.

Scenario and pack cards stay where you drag them. Drop one on a folder to file it, on the path above the cards to
take it out, or on **Trash** to delete it; **New folder** makes a folder in the one shown, folders hold folders, and
each shows the first four of what it holds. Trash keeps what you delete for thirty days, and its tile can sit wherever you drag it.

### Saves

The table saves itself within a second. **Saves…** (Menu, Ctrl+S or a card's ⋯; host only) shows the scenario's
saves as tiles in folders, in `%LOCALAPPDATA%\KriegsspielTable\saves\<scenario>\`.

- **A save is a whole game:** the table and every turn it ended. The one on the table is marked and is written as
  you play. **New save…** starts another from the scenario's start, or from the save or turn chosen;
  **New save** on a card starts one from the scenario's start.
- **Save** keeps a copy of the table under a name. **New folder** sorts your saves.
- **Load** puts a save on the table for everyone and play goes on in it; the save that was on the table is written
  first and stays as it is. **Look** shows a save without loading it.
- **Turns** lists a save's ended turns with their pictures: **Look** at one, or **Load** it as a new save beside it.
- **Right-click** a save to rename or move it, or drag it onto a folder. The save on the table stays where it is
  until another is loaded.
- **Pick several** with Ctrl+click, Shift+click or Ctrl+A; Delete or Backspace removes them. Title-screen lists work
  the same.
- **Trash**, at the foot of the folders, keeps deleted saves thirty days, to **Restore**.
- **Co-umpires** keep a copy of the game on the table. If your PC fails, they host from
  **Scenarios → Mirrored games**, with the packs and map their PC fetched.

## Hosting and the door

1. **Open** the scenario; the button reads **Resume** once it has a game.
2. **Invite…** (Menu, or the connection at the top right) shows your addresses and the **Password**: type one
   there, or leave it empty for an open door. Send them to the people you want.
   - Same network: the LAN address.
   - Internet: your public address. The app asks your router to open the UDP port; if the dialog says it
     could not, forward the port yourself.
   - Big game? **Export…** each pack listed there and post it first: people who install it download almost nothing
     from you when they join.
3. People who knock appear as a card under the top bar. Pick **Lead**, **Umpire**, **Observer** or **Refuse**.
   Their PC remembers the answer next time, also when your address changes or a co-umpire hosts the mirror. On a
   new PC, **Returning as** lets someone back in as who they were: same name, role and zones.
4. Once others are in, the connection at the top right also shows each umpire in their colour and how many watch.
   Click it for the list: the host and leads change roles, put people out and answer the door from there. Your
   own dot is faded while the others can't see your pointer. Leads also see everyone let in who is **Away**;
   **Forget…** ends that person's way back in, so they knock like anyone new.

### Roles

| Role | Can |
|---|---|
| Host umpire | everything; the game runs on your PC |
| Lead umpire | everything but the saves and other leads' roles: lets people in, sets roles, draws zones |
| Umpire | change the table; once zones exist, only pieces inside their own zone, in any way |
| Observer | watch the whole board; never sees zones, pointers or dice-panel rolls; changes nothing |

A table seats 40, as many as 32 of them umpires.

## At the table

The tool strip runs down the left. Point at any tool and press **F1** for its section here.

- **Menu → Sidebar**, or a right-click on the strip, lists its buttons: untick one to take it off the strip, tick it to
  bring it back. Its key still works. Map, Pieces, Umpires, Select and Log always stay.
- **Esc** puts down whatever is in hand; pressed again, it opens the menu.
- The top line says what just happened (a roll, a measurement), then goes quiet.
- Panels open beside their button and stay where you drag them by the title bar. Pieces, Log, Decals and a
  holder's window resize from the corner.
- Other umpires' pointers show as gloved hands in their colour, with their names; a hand closes over what it
  carries. **Share my pointer** (the people at the top right) shows yours; Scenario properties
  can rule that **Umpires cannot hide their pointer**.

### Camera

| Do | To |
|---|---|
| Right-drag | orbit |
| Middle-drag, Space+drag, WASD | pan |
| Wheel | zoom (over a selected piece it turns it) |
| Point at the compass | the zoom shows under it: click it and type a distance, or point at it and drag its slider; the wheel over the compass zooms |
| Home | frame the map |
| N | turn the view north-up, as a click on the compass does |
| F2 | flat map view and back |
| Ctrl+1–9 | keep this view (unless carrying or with several picked); also in the right-click menu and Menu → Views |
| Shift+1–9 | go back to a kept view; 1–9 alone only change a piece's state |

Kept views are yours, for each scenario. Forget one with Menu → Views → Forget, or a right-click on it in the right-click menu.

### Selecting and moving

- Drag a piece to carry it; it drops where you let go. Drag the empty table to box-select.
- **Right-click** a piece or a map sheet for its name, its size and what can be done to it.
- **Arrows** nudge what is picked a step to that side of the screen: 0.05 units, **Ctrl** 0.01, **Shift** a whole unit.
- **Undo** and **Redo** are at the foot of the tool strip, and on **Ctrl+Z** and **Ctrl+Y**.
- **Q / E** turn 15° (Shift 1°) and **= / −** resize (1/50 to 50 times). The wheel over a selected piece turns it
  10° (Ctrl 2°, Shift ½°); **Alt+wheel** zooms there. With **Wheel turns the piece under the pointer** on
  (Settings → Table), it turns any piece you may move without selecting it first.
- The same keys mean the same in every tool: **Q / E** turn, **= / −** size, **[ ]** step one less or one more,
  and the wheel zooms, except over selected pieces. **Ctrl+wheel** sizes what a tool holds: the brush, a decal, the
  POV reach.
- **Ctrl+1–9** form rows while carrying or with several picked, **Ctrl+A** selects all, **Ctrl+X / C / V** cut,
  copy and paste, **Delete** deletes.
- **Ctrl+Z / Ctrl+Y** undo and redo your own changes. If another umpire has changed the same thing since, the undo
  is refused, says who, and stops there. The host and lead umpires also undo what scripts did by themselves since
  their own last change.
- What you copy waits on the clipboard until you copy again, so **Ctrl+V** pastes it into another scenario, or after the app
  is closed and opened. Pieces keep their counters, text, notes and scripts, and the packs they need come into that game
  with them; pieces whose pack is not on the PC are left out and the hint says which.
- **Right-click** a piece for everything else: states, counters, markers, lock, tint, POV, Properties. Under its name
  the window says how big the piece is. The ticks seldom changed are under **More options**, which stays open once
  opened.
- **Settings → Table** has **Lift**, how high a held piece rises, and a tick that lights the piece an attached piece
  rides on when you point at the attached one.

### Turn and resize

**T** puts a ring round the selection: the round knob turns it, the square knob resizes. **Shift** snaps the turn
to 15° and the size to tenths. From the ring's middle, the arrow lettered **X** slides the selection along X and
the one lettered **Y** along Y, and nowhere else (**Shift**: whole units). The upright arrow, **Z**, lifts a piece
off the table: let go and it is **Flying** at that height (**Shift**: half units); pulled back down it stops on the
ground it stands on, never under it, and lands. Seen from straight above there is no upright arrow. Its window takes exact numbers:
where the selection stands (**X**, **Y**, the middle of several pieces), **Z**, how high it flies (0 stands it on
the ground), its **Turn** and its **Size**, and its **Width** and **Depth** in table units, each apart, to
make a piece or an effect wider or deeper than its own shape (smoke over an area, a longer wall). Once the map has a
scale, the piece's size in the scenario's measure shows under them. With a map sheet picked the window takes the
sheet's **X**, **Y**, **Turn**, **Width**, **Depth** and **Order**, which sheet lies on top where two overlap.
**Keep the proportions** holds width and depth together; **With its pieces** moves, turns and spreads the pieces
standing on the sheet with it, however it is moved.

### Formations

**G** opens the Formations tool. With pieces selected, pick a formation from its panel or press its key. The panel
groups them, and names what each was used for, from the phalanx to the tank platoon:

- **Lines:** **L** line, **Shift+L** stretch in a line, **X** chequer, **Shift+C** crescent, **Z** / **Shift+Z**
  echelon right / left, **Shift+X** open order.
- **Columns:** **I** column (one abreast is single file, many abreast a block), **Shift+I** staggered column,
  **H** herringbone.
- **Shapes:** **Shift+B** square, **C** ring, **Y** wedge, **U** vee, **Shift+Y** diamond.

- The pieces march the shortest way to their places round where they stand; markers on them ride along. One undo.
- Pick pieces as with Select: click one, **Shift+click** to add or take one out, drag a box on the empty table
  (**Shift** adds), click the empty table to let them all go. **Double-click** a piece for every piece of its kind in view.
- **Right-drag** where the front should stand: the drag sets which way it faces, and how far you drag sets how many
  stand in the front rank. For a column the right-drag is its line of march instead: it faces along the drag, its
  head where the drag ends. A right-click alone still opens the menu; **Alt+right-drag** turns the view.
- In stretch in a line, the right-drag draws the line itself, straight, curved or in an S: the pieces are strung evenly
  along it, the first at its start and the last at its end, each facing across it the way a line drawn left to right
  faces the top. Never closer than the interval: on a line too short for them, the last run on past its end, and the
  status line says so. Draw it round on itself and they go evenly all the way round. Draw back over it to shorten
  it; the line bends at most 60° in a piece's width, so a sharp corner comes out rounded rather than folded.
- Until the tool has formed the selected pieces up, **Q / E**, **= / −** and **[ ]** act on them as at the table,
  and no formation key is also a key of the table. Once it has:
- In stretch in a line, tick **Stop at the interval** in the panel and the pieces follow the line as it is drawn,
  closing up on a short one, until they stand the interval apart; drawn longer, the line stops there, where it
  would otherwise spread them to its ends however far.
- **Q / E** wheel the whole formation (Shift 1°); a drawn stretch turns whole, keeping its shape. **= / −** set the
  interval between pieces side by side (0 is shoulder to shoulder), a drawn stretch keeping its line, and
  **Shift+= / Shift+−** the distance between ranks; both are counted in a piece's width, so 100% is full interval or
  full distance. The panel has a slider for each. **[ ]** the ranks of a line, or how many abreast in a column. The wheel over the pieces turns each in place, as everywhere; off them it zooms.
- Drag one of the pieces to carry the formation as it stands.

### Pieces

- **Placing:** open the **Pieces** panel and drag a piece onto the table (Shift keeps placing).
- **Turning over:** middle-click or **F**. F turns over the piece under the pointer; with the pointer on a selected
  piece or on nothing, it turns the selection over. Counters show where the maker put them, with − / + to click.
- **Notes:** the **Note** in Firme's Effects and Props is a message form. Click its title or its lines and type:
  in the title Enter keeps it; in the lines Enter starts a new line and Ctrl+Enter keeps it; Esc leaves it as it
  was. Ctrl+click picks the note out instead.
  A short note is written a line to a rule, a longer one two lines to a rule; right-click → **Writing** sets it
  yourself: **Large** is a line to a rule, **Small** two, **Auto** as large as fits. Notes take the digital tabletop's
  formatting codes: `[b]`, `[i]`, `[u]`, `[s]`, `[sub]`, `[sup]`, and a colour such as `[ff0000]` until `[-]`.
  Point at a note to read all of it.
  A module's notecards, Notes and Notebook pages come in as these notes.
- **States:** point at a piece and press **1–9** (0 is the tenth), or **[ ]** to step.
- **Books:** a PDF brought in from a module, or made in the piece studio, is a book. Double-click it, or
  right-click → **Read…**, to turn its pages in a window of your own; **Show on the table** turns the piece
  itself to the page you are on, for everyone.
- **Label and description:** at the foot of the right-click menu. Both show on hover to everyone, observers
  included, and are in the scenario file: not a hiding place.
- **Markers:** drop a piece on one at least its own size and it rides along, however deep that one already rides. Drop
  it elsewhere to free it.
- **Attached pieces:** in the **Pieces** panel, **On the table** lists what rides on what. Drag rows onto another
  piece's row and they ride on it: moving it carries them and everything riding on them, however far away they
  stand, while one moved alone goes alone and stays attached. Drop rows between two rows to ride on what those
  ride on, or below the list to free them. With several picked, right-click → **Attach the others** does the same.
  The rows picked in the list are the pieces picked on the table, and the other way round.
- **Holding:** a piece that holds pieces keeps them off the table, as a bag does. Hold pieces, effects too, over it a moment: a
  ring fills round it and they sink towards it; let go then and they go in, or move off first to keep them out. Pieces
  dropped on its window go in too. Right-click → **Open** shows what it holds beside the piece, until it leaves the
  view: drag one out onto the table, double-click to set it beside, **Take all out** for a row. Right-click →
  **Holds pieces** switches it on or off for any piece; the piece studio's **Holds pieces** sets where a kind starts,
  and the box, chest and crate of Firme's Effects and Props start on. A held piece keeps everything it had. A holder
  goes inside a holder seven deep and no deeper, as a matter of taste: nothing good is kept further down.
- **Ready piece:** one held piece can be its holder's ready piece, smoke or dust for instance. The first piece put
  into an empty holder becomes it; right-click another in the holder's window → **Ready piece** to change it. It is
  listed first. **B** with the pointer over the holder (or with it selected) releases it onto the table; dragging it
  out of the holder's window does too. **B** over the holder or the piece recalls it. Out on the table it is attached
  to its holder until recalled, moving and turning with it; Formations leave it be, and a selection box takes it only
  with its holder. A holder with states can have a ready piece for each: right-click a held piece → **Ready in state n**,
  or **Not ready** to clear it from every state; a state without its own takes the plain ready piece. Turning the
  holder to another state puts the old state's ready piece away and brings the new state's out where it stood, if it
  was out when that state was left. Recalled, it remembers where it stood, so it
  comes out there again, moved and turned with the holder. Also in the right-click menu.
- **Locked:** right-click → **Locked** pins a piece: it can be read, not moved. A selection box leaves it out and the
  Delete key leaves it be; right-click → **Delete** removes it.
- **Tint:** right-click → **Tint** colours every selected piece: a swatch, **+** for any colour, **Reset** for the
  colour it was made with. Where it shows follows the module: a token's picture, a tile's sides; decals keep their colours.
- **Effects** (smoke, fire, volleys, melee, mist) and props are pieces of **Firme's Effects and Props**; resized, an effect
  covers more ground. Right-click → **Running** stops or starts one, and **Play once** plays it through once on every
  screen. Right-click → **Smoke** slides an effect's smoke from very fine, which leaves the pieces under it readable,
  through fine, standard and dense to very dense; dense hangs and trails downwind, at most as far as real powder smoke goes in the
  scenario's wind. Dust has the same slider, named **Dust**; with smoke and dust selected together, one slider moves
  both. Right-click → **Intensity**, when only effects that have one are selected, makes them play slacker or harder:
  shots, volleys, bursts and falling stones come further apart or closer together; fire at will, tracers, rain, snow
  and a melee show less or more. To change one for good, copy it into a pack of your own in the piece studio (＋ New piece).
- **Flying:** right-click → **Flying** puts a piece in the air: it is drawn at its **Altitude** over the ground, with its
  shadow straight down and, while it is picked, a line to the ground; pieces pass under it and are never stood on it. Untick to land it; it keeps its
  altitude for the next take-off. Air units fight each other at the umpire's word.
- **Worth:** pieces a module gave a worth (chips, supply) add up at the head of the right-click menu when several are chosen.
- **Scripts:** a piece's buttons, boxes and panels work with a click. Right-click → **Scripting…** opens its script;
  see the [scripting guide](scripting.md).
- **Changing a kind:** at a table you host, right-click a piece of a pack of your own → **Edit in the piece studio…**.
  The studio opens in 3D on that piece where it stands on the table, among its neighbours, with its state, size and
  facing; turning the view turns the table with it. The last button on the left switches to the studio's plain
  background and back. Only that kind can be changed, and the game goes on for everyone else. Your pieces of that kind
  change as you work; only you see them. **Confirm** sends the changes to everyone at the table; **Discard**, or leaving without
  confirming, puts the pieces back as they were. A standard piece is copied into a pack of your own first
  (＋ New piece).

### Dice

The **Dice** button shows or hides the dice panel: tap d4 to d20 (twice for two), then **Roll**. The result stays in the
panel for the next roll. Or drag dice from the standard pack and throw them, shake them in your hand before letting go,
or press **R** to roll in place. The host's PC decides every result and the Log shows it; a die set to **Thrown for
real** in its right-click menu counts the face it lands on instead. Hover a die for its result;
right-click it for its **Tint**. To turn a die by hand, point at it and press the side's number.
Right-click several dice to see what they show and their total. For bigger dice or dice of your own, copy them into a
pack of your own in the piece studio (＋ New piece, **Dice**).

### Ruler

**M**, then drag. It reads in the scenario's one measure (table units until the map has a scale or the measure is
grid cells), with ticks, the bearing and, over relief, the rise or fall. Hold **Shift** while dragging to keep the
line on the nearest of the eight compass points. The pin beside the reading keeps that line on the table through any tool, while you
measure others, until you click its pin again. Pinned lines are yours alone, kept on this PC
for this table.

### Ranging

Pick the rings and click a piece: type the reach in the **Ranging** panel or set it with the wheel, and a piece with a **range** counter uses its own.

### Map scale

**Map scale…** in the Map page takes the scale tool in hand: drag along the map's scale bar and type what it
measures. Distances then read in the scenario's one measure
(Scenario properties → **Map** → **Distances**: metres, kilometres, yards, miles, paces, or cells of the grid, which
need no scale). Every length is written in that measure, short or long.

### Draw

Freehand, shapes (line, arrow, curve, curved arrow, area, rectangle, ellipse), a name and a rubber that takes away
only what it passes over. The panel holds the ink, how see-through it is, the pen's width (in screen pixels) and
the layer. A curve or an area is clicked bend by bend: double-click or **Enter** ends it, **Backspace** takes a
point back, and an area is washed in the ink. A click with the pen leaves a dot as wide as the pen. With **Shift**
a drawn or ruled line keeps to steps of 15°, an ellipse is a circle and a rectangle a square. With the ellipse, a click
lays a circle of the **Radius** set in the panel, in the scenario's distances. Hold **Tab** to have
the ruler in hand while drawing; let go and the pen is back.

**Name** writes on the map in the map style's hand: click, or drag a path, type the name and press **Enter**
(**Esc** drops it). Where the click lands decides the hand: beside a town a place name, on a river a name along
its course, on a patch of ground spaced capitals across it, on bare paper a plain name. Double-click a name to
retype it, drag it to move it; the rubber takes a name whole. A name is a drawing: it sits on the layer picked
and undoes like a stroke.

### Decals

Pictures stamped on the table or on a piece: blood where a fight is, arrows, marks. The panel holds the
scenario's decals; **Add a decal…** takes a picture file or a web link, and right-click takes one out. Pick a
decal and click to stamp it: on a piece it rides with the piece, elsewhere it lies on the table; 3D models, dice
and effects take none, so there it lands on the table. **Q / E** turn it 15° (Shift 1°), **= / −** or
**Ctrl+wheel** size it, the status line shows its turn and size, and clicking a stamped decal lifts it off.
**Lift every stamp off** clears the table and leaves the pieces' decals. A converted module brings its decals and
those already stamped.

### Layers

The Draw panel lists its layers at its foot. Drawings sit on layers, like tracing paper. The eye hides a layer
**on your screen only**: handy before a screenshot. Click a name to draw on it; the rubber works on that layer
alone. **+** adds a layer, a double-click renames it, **✕** removes it with its drawings, and the rubber beside
**+** rubs out everything on the layer drawn on.

### POV capture

A turn report as a picture, from one piece's point of view.

1. Select the piece and press **V** (or right-click → POV capture from here).
2. Set how far it sees (**Ctrl+wheel**, **= / −**, or the Radius field); the wheel zooms in to trace. **Sharp to**
   pulls a ring in from the edge: past it the picture blurs, gently at first and hard at the edge, as distance takes
   detail; all the way right is sharp everywhere. **Edge blur** sets how strong it gets at the edge. The reach, the
   ring and the blur are kept with the scenario. **Format** makes the picture round, square or drawn.
3. Drag to trace what blocks the view: walls, wood edges, ridges. What lies behind goes black. **Ctrl+Z** takes
   back the last line, **Clear** all of them.
   In **Drawn**, nothing is traced: drag round what the piece sees, as with a freehand snip, and the stroke closes
   into the picture's outline. The picture is cut to that outline and drawn finer the smaller it is; a new stroke
   replaces it, **Ctrl+Z** goes back to the one before. **Blur in** sets how far in from the drawn edge the picture
   blurs. Lines traced in Round or Square wait there until you switch back.
4. **Enter** or **Capture**. The picture goes to the clipboard and the capture folder (**Settings → Captures**
   chooses which), its file named for the scenario, the turn and the time.

The picture holds the pieces in sight at the moment it is taken, wherever they stood when the tool came out, and
a GIF follows them as they move. Zones and other umpires' pointers are never in it.

**View → First person** looks out from the piece's front edge, the way it faces: drag to look around, or pick
**N E S W** under **Eye → Facing** to hold a direction (dragging lets go). **Lens** sets how many degrees the picture
takes in across, 20° to 120°; at 90° the four directions make the whole circle. **Height** slides the viewpoint between
the ground at the piece's feet and well over its head: drop it to look out from behind a crest. There is nothing to trace from the eye; **Ctrl+wheel** or
**= / −** still set the reach.
Lines traced in 2D still cut off what lies behind them. Each Capture is one picture.

**Text → Hidden** leaves names, counters, notes and the studio's own lettering out of the picture, and off the
table while the tool is out. Words baked into imported pictures stay.

**Shot → GIF** records a moving picture instead. **Record** or **Enter** starts it, **Stop** or **Enter** ends it,
**Cancel** drops it. From above it is 720 × 720, from the eye 768 × 432, 10 frames a second, and it stops by
itself at 60 s or 20 MB, Discord's upload limit. **Size → Large** doubles both sides for a sharper, shorter GIF.
It goes to the capture folder, and to the clipboard as a file: Ctrl+V in Discord uploads it. While it records
the reach stays as it was.

Nothing is sent: paste it into that side's channel.

### Capture

**Ctrl+Shift+C** and drag a region: it goes to the clipboard and the capture folder. **Ctrl+Shift+X** repeats
it. Tools, zones and pointers are left out.

For a moving picture, choose **Shot → GIF** in the Capture panel, then drag the region: it records, 10 frames a
second, while you keep playing (move pieces, roll dice, turn the view). **Stop** in the panel or **Enter** ends it
and hands it over like a picture; **Cancel** drops it. It stops by itself at 60 s or 20 MB; **Size → Large**
records at twice the size, for a shorter GIF.

Names too small in the picture? Keep **Settings → Captures → Resolution** at 4×, shrink the POV reach, or turn up
**Settings → Table → Text size**.

### POV profiles

Tabs above the POV capture panel keep sets of its settings for this scenario, such as one for player reports
and one for the after-action report. **+** keeps the settings in use as a new profile and asks its name; a
tab puts them all back: view, format, shot, GIF size, text, lens, eye height, reach, sharp ring and edge blur. The
tab whose settings are in use is lit. Right-click a tab to save the settings in use over it, rename it or delete it;
double-click renames too. Up to six per scenario, kept on this PC.

### Map sheets

In the **Map** page: add sheets (files or web links), hide one on your screen with its eye, lock one with its
padlock, and set the **Map scale…**. A sheet just added is picked and free: drag it into place, then lock it.

- With **Map** open, a click picks a free sheet and a drag moves it; with Map shut, a click or drag on the map is
  for pieces. A locked sheet is ground: pick it with **Alt+click**, or right-click → **Pick it**.
- Right-click a sheet for its size, **Locked**, **Hide on my screen**, **Frame it** and **Remove…**, and for the
  sheets lying under it.
- A picked sheet has a thin outline, and only its corners while it moves, so the seam with its neighbour stays in
  sight. Dragged, its edges meet the other sheets' from a few pixels away, and a white line marks the seam: zoom
  in to place it finer, or hold **Alt** to place it freely.
- **Arrows** nudge it, **Q / E** turn it, **= / −** size it, and **T** opens its numbers.

**Opacity** lets what lies under a sheet show through it, to line an old map up over another.
**Crop…** trims borders; the sheet keeps its scale. **Left 90°**, **180°** and **Right 90°** turn the
sheet, every sheet joined to it and the pieces on them as one, about the map's middle.

### Relief

A sheet can carry hills as well as a picture. Pieces, drawings, zones, other sheets and the ruler all follow the
ground.

- **Add map sheets…** takes 3D terrain (glTF/GLB, FBX, OBJ, STL, PLY, Collada; Blender files when Blender is
  installed) and heightmaps (16-bit PNG, GeoTIFF, .asc, .hgt, .xyz).
- Picked with pictures, each relief gets a row to choose its picture: the model's own texture, a picture (a
  matching name is chosen for you), another file, or none. Tiles cut from one map meet without a step.
- Or add the relief alone, then the map picture as an ordinary sheet over it: the picture drapes over the hills.
- **New blank ground…** adds flat ground of any size, to build terrain from nothing. **Detail** sets how fine
  its cells are: **Usual**, **Fine** (twice as many each way) or **Finest** (four times), for a small close map.
- The selected sheet's **Relief** group: **Height ×** (0 flat, 1 true proportions), **Add / Replace / Remove
  relief…** and **Picture…**. **Cells** shows how many cells the ground has and their size in metres; **Finer**
  doubles them each way, so shaping and paint can be finer. The ground and its paint stay as they are, and
  one undo brings the coarser ground back.
- The **Terrain** window (the hill on the tool strip) has three tabs. **Shape** raises, lowers, smooths,
  flattens and sets heights; **Paint** lays grass, field, wood, marsh, water, earth, rock, sand or snow, and **Rub out** takes paint
  off; **Build** is below. The paint follows the brush and shows at once.
  On a sheet without relief, the first stroke gives it flat ground to shape. **Raise** and **Lower** work in
  metres: hold the button and the ground under the brush climbs or sinks at the **Rate**, the same for any brush
  size; **Up to** stops a rise at a height, **Down to** stops a cut; **Ctrl** turns one into the other. The top
  line shows the height and slope under the pointer, and the inner ring marks half strength. **Set height**
  lays an exact height in metres, typed or taken from the map with **Alt+click**; **Smooth** reaches further
  with a wider brush; **Hardness** runs from a soft bell to flat across with a short edge. Four forms are laid
  whole, at a **Height** in metres and a **Profile** (Round, Sharp, Flat): **Hill**, dragged from its summit
  to its foot; **Ridge** and **Valley**, clicked along their line as a road is; and **Slope**, dragged from
  top to foot for one even grade between the two heights. **Contours** follows a map: click along a contour
  line, end it on its last point or close it on its first, and it takes the **Height**, which then moves on by
  the **Step**; **Raise the ground** fits the relief through every line drawn and uses them up. Three passes
  finish a whole sheet at a click on it: **Roughen** makes the ground naturally uneven, **Erode** cuts gullies
  where the relief gathers water, and **Lift all** raises or lowers everything, so a valley can be cut into
  ground that was flat. **Heights**, ticked, tints the ground in bands by height while the Shape tab is open,
  and red where it is steeper than thirty degrees and nothing can be built. **= / −** or
  **Ctrl+wheel** size the brush; each stroke is one undo. Roads, towns and everything else people made are laid with **Build**.
- It needs the map's scale: set it with **Map scale…** in the Map page, let **New blank ground…** set it, or add a GeoTIFF,
  `.asc` or `.hgt` with **Set the map's scale from the file** ticked. Painted ground is
  drawn at that scale, in the style's own hand, with its own natural signs, afresh every time it is painted. Wood
  takes **Trees**, open wood to closed forest; the row shows under Density when wood is in hand. Roads, towns, plots
  and farms painted in an earlier version become built things when the host opens the map: the same roads and
  places, drawn afresh. One undo gives the painted ground back.
- Painting: **Fill** colours the whole patch clicked, up to other ground types (a bare sheet whole), so a map starts
  with one click of grass; on its own kind it lays the patch afresh. The **rectangle** and **ellipse** beside it paint a field, a wood or a pond in one drag
  (**Shift** for a square or a circle, lined up with the view); the **polygon** paints the shape of its corners,
  clicked in turn and closed on the first or with a double-click (**Esc** drops it). Each shape is one undo.
  **Route** lays ground that finds its own way: click the waypoints, then the last again or double-click, and
  between them the ground takes the easiest path over the relief at the brush's width: dear through wood, marsh
  and rock, round wide water. With the water swatch the first click is a spring: a river runs down by the steepest descent, widening
  as it goes, until it meets water, the sheet's edge, or a hollow, where it ends in a pond. One route, one undo.
  **Density** below 100% lays the ground in clumps: sparse wood, scattered rock, broken
  marsh; with ✕ it thins a wood. **Shift+click** runs straight on from the last stroke, for rivers and
  ridges. **Alt+click** picks up the ground under the cursor.
- **Sea** is painted beside water, as open water with no current: fine coast lines in the survey style, wave
  signs with the odd ship and sea beast in the ancient, darker steps from the shore in the modern. No road is
  bridged across it, and a place on it gets a harbour.
- Close to, every ground is painted as it lies: turf with clover and flowers, marsh tussocks and pools, dunes
  with rippled hollows, cracked earth, bedded rock with stones and lichen, drifted snow, crowns over a forest
  floor, swell and surf on the sea. The map's signs fade as that comes in.
- **Map → Ground** in the scenario's properties sets, for everyone, the **Style** painted ground is drawn
  in (**Ancient**, **18th-century survey**, **Modern topographic** or the one-ink **Staff sheet**) and turns on **contour lines** over every
  relief: redrawn as the ground is shaped, stepping in round heights once the map's scale is calibrated, every fifth
  line numbered. **Snow line** whitens every ground above a height in metres, once the map's scale is set.
  **Streams** draws brooks where the relief gathers water, heavier the more it gathers, over every ground but
  painted water, which swallows them; it needs the map's scale, and floors flatter than a fifth of a percent
  stay dry. Painted water under 100 m wide
  shows its current wherever the relief falls: strokes and an arrow pointed downhill, ticks across the flow
  where it drops two in a hundred, a step mark where it drops five metres in a stride.
- Pieces lean with the slope under them and follow the ground when it changes.
- The table's light dims and shades painted ground but never tints it: a style keeps its colours under any light.

### Build

Roads, settlements, forts, buildings and the details of the country are built, not painted. Each is a thing laid
on the map over the ground, drawn in the map style's hand, generated once and kept as laid. Everything built
is true to size in metres at the map's scale: a road is as wide as a road, a wall as thick as a wall, a tree
as wide as its crown. From afar a thing is never drawn smaller than it takes to be seen; close up it shows its
detail, the two slopes of a roof and its chimney, the stakes of a palisade, the posts of a fence, the bushes of
a hedge, the ruts of a road, the graves in a churchyard. So a map can be a whole country at 50 m to the unit
or one town at 5, and the same detail is there when you come near. A place is furnished as one would be: a
cobbled market square with stalls, barrels, a cart, the well and its trough, and in a town a fountain and
the stocks; an inn with its signboard and a smithy on the street; doors on the houses, some roofs hipped;
woodpiles, privies and beehives in the yards; a jetty and boats where the place meets the water; a keep in
a walled town, battlements on its wall, a gun on each bastion of a fort; rocks and, outside a town, the
gallows. Each is a thing of its own that Pick can move or remove. The ground shows
through a place: its yards, gardens and squares are washes over the land, never a patch on top of it. The
**Build** tab of the **Terrain** window needs the map's scale, as painting does.

- **Places → Settlement**: drag from its middle outward; a ghost of the plan follows, and letting go lays it. It
  takes the road that crosses it as its main street or lays a spine of its own, grows lanes, fronts them with
  houses, and keeps a square with buildings on every side, a well and a church in its yard. Houses come plain or with a wing, with sheds,
  fenced crofts and orchards behind them; barns and ricks stand at the lane ends, with a pond, wayside shrines,
  lone trees, a windmill on the high ground and a watermill where a built stream runs by. Nothing of it stands
  on a built watercourse, on a steep slope, or inside another place. **Houses** runs from scattered houses with
  gardens to packed streets. The drag's size says hamlet, village, town or city.
- **Plan** is the form the place grew in. **Grown**: a clustered village of lanes off the road. **Street**: one
  street, a row of houses each side, a long strip plot behind every house. **Green**: the street parts round a
  common green that holds the pond and the church. **Round**: a horseshoe of farmsteads on a small green with
  one way in, wedge plots behind, the church outside. **Grid**: streets laid out square. A green looks for dry
  ground near where you dragged.
- **Walls** go round what is built, never a plain circle. **Walled** fences a village with a palisade round
  its houses and crofts; a town (large, or with **Houses** high) gets a wall with towers, a gatehouse between two towers where each
  road or street passes and a ditch outside, and the plots the wall would cut are left out. The wall keeps to dry,
  buildable ground. **Bastioned** fits a trace of bastions round the town, with moat, ravelins and glacis.
- **Places → Fort**: a star of four to eight **Bastions** with **Outworks**: **None**, **Glacis** or
  **Ravelins**, with barracks, a magazine, quarters, a well and its flag inside. On a map in the **Ancient**
  style the fort is a camp instead: a rampart shaped like a playing card with a ditch, four gates, two
  crossing streets, the headquarters where they meet and rows of barracks; and a settlement there is walled
  or not, never bastioned. **Places → Redoubt**: a plain square earthwork, with or without a glacis. For both the drag
  sets its size and the way it faces. **Places → Building**: choose any building from the catalogue, by
  section (Homes, Farm, Trade, Arms, Sacred), each with its picture; a click stands it, a drag gives its length
  and facing.
- A place takes its living from the land round it. On a river: a fishermen's row with boats, nets and racks,
  a ferry where the water is wide and unbridged, the mill where the stream falls, tanner and dyer by the bank.
  On the sea: a harbour, with a stone quay, piers, boats and a ship, a crane, warehouses, and for a large place
  a mole with its beacon, or the beacon on a headland; salt pans and a dune fence on a sandy shore. By a wood:
  woodcutters' huts, log stacks, a sawpit and charcoal clamps. By a marsh: peat cuttings, reed stacks and a
  drain. By rock: a quarry with its track, cut stone and a lime kiln. The church takes the higher side of the
  square and vines the slope that faces the sun. Each is a part like any other: Pick moves or removes it, and
  the catalogue stands more. New buildings keep off a road that is already there.
- The ground answers what is built on it, and heals when the thing is moved or removed: trees stand back from
  buildings and roads, with stumps in the felled margin; grass is trodden by the houses; field strips and marsh
  stop at the closes. Nothing is repainted in the relief.
- Earthworks are real ground: a rampart is raised, a ditch, a moat and a quarry are cut, so pieces stand on
  the bank and the ruler measures it. The stored relief is not changed, and the earthwork moves with its thing.
  On coarse ground a bank is soft; **Finer** in the sheet's Relief group sharpens it. A cut stops at the table.
- Close to, built things show what they are made of: setts on a street and a square, rutted earth on a road
  and a yard, flagstones in the Roman manner, each roof covering laid course by course, planked jetties, decks
  and crates, hewn stone, cloth awnings, leafy crowns, rows in a garden.
- **Manner** is how the place is built, and with it what its buildings are. On a map of later times:
  **Continental** (plaster under tile, some slate and thatch, steep roofs, a spire), **English** (brick and
  slate, chimney stacks with their pots at the gable ends, a square church tower), **Northern** (timber under
  thatch, shingle and turf, a wooden church with domes, conifers) and **Southern** (low tiled roofs and flat
  whitewashed ones, courtyards, cypresses, a threshing floor and vines). On a map in the **Ancient** style:
  **Roman** (houses round an atrium, blocks round a court, a paved forum with its basilica, a temple on its
  podium, baths, an arena outside a town), **Egyptian** (flat mud-brick roofs with their stairs and awnings,
  a temple behind its pylon with two obelisks, grain bins, palms) and **Tribal** (round thatched houses, a
  great house, raised granaries, a ring of standing stones). Close to, every building shows its roof: gabled,
  hipped, half-hipped, mansard, double-ridged, flat, domed or conical, in tile, slate, thatch, shingle, lead,
  mud, lime or turf. A place also fills itself with its trades (shops under awnings, bakery, tavern, cooper,
  wheelwright, potter with a kiln, weaver, fuller), and its yards with coops and hens, sties and pigs, dung
  heaps, washing and jars; barns come as stables, byres, granaries and cart sheds with paddocks of sheep,
  cattle or horses; a bastioned town has its barracks, parade, arsenal and magazine. A fort, a single
  building and a stretch of country take a Manner too.
- **Name**: a settlement or fort is lettered with its name beside it. **Generated** makes one up in the
  **Tongue** chosen (German, French, English, Spanish, Italian or Russian); **Typed** opens a box at the place
  for yours, and **Enter** lays it; **None** leaves it bare. The name moves and goes with its place.
- **Roads**: pick Track, Lane, Street, Road, Chaussée, Stream or River and click the waypoints, then the last again or
  double-click; **Esc** drops it. With **Finds its way** the line takes the easiest path over the relief between
  waypoints, and a stream or river runs downhill from the first click. A road bridges the water it crosses; a
  track fords a narrow stream.
- **Details**: drag the brush over the country and it settles what it passes, plot boundaries by **Plots** and
  farmsteads by **Buildings**, each farm joined to the nearest road by a track, with its barns, orchard, ricks
  and trees; hedges and ditches carry trees along them and stop at a settlement or fort. **= / −** or **Ctrl+wheel** size
  the brush.
- **Pick**: click a built thing, or anywhere inside a settlement, to pick it; what a click would take is outlined
  under the pointer. Click it again for one part of it. A drag moves what is picked, **Delete** removes it, and **Again** lays the whole thing afresh another way; a part moved by hand stays.
  A picked settlement or fort shows its **Name** to retype, and **Generate** makes up another.
  A picked settlement also shows its **Manner**, **Houses**, **Walls** and **Plan**, and a star fort its **Bastions** and
  **Outworks**: change one and the place is laid again on the same seed, keeping what you moved or added.
  **Q / E** turn what is picked, a whole place or one part; **= / −** size a part.
  From afar a picked place shows what shapes the whole of it. **Dive in** brings the view close, and the panel
  then shows the catalogue under **Add** instead: seven sections (Homes, Farm, Trade, Arms, Sacred, Water,
  Green), each thing with its picture as the map draws it. Choose one and click in the place to stand it there
  as part of it. Zoom out and the place's own settings are back.
  **Keep** on a part holds it where it is when the place is laid again; whatever you move, turn, size or add is
  kept already. **Keep** on a whole place holds all of it: **Again** and its settings go until you let it go.
  Under **Shape**, **Fill** and **Thin** are brushes: drag **Fill** over streets to front them with packed
  houses, in the place or along a road outside it; drag **Thin** over houses to take about half of them and
  leave gardens. Both stay when the place is laid again. **= / −** or **Ctrl+wheel** size the brush.
  A picked place with a wall or palisade shows a ring at each corner of it: drag one and the wall follows.
  **Shape → Gate** then a click on the wall opens a gate there, and a click on a gate closes it; a gate where a
  street passes stays open. A road laid through a wall opens a gate where it passes. A wall changed by hand is kept, and the place is laid again inside it.
  **Alt+click** anywhere in the Terrain window's tools takes what is under the pointer: a built thing is picked,
  painted ground becomes the paint in hand.
- Nothing is built on water, marsh or a slope over thirty degrees. Each laying, move and removal is one undo.

### Umpires and zones

The **Zones** page holds the zones. **Split the board…** gives ground to one umpire; a zone takes its
umpire's colour. Once any zone exists, an umpire edits only the pieces inside their own zone, and
cannot drop or place one outside it. Host and leads are never limited by zones. The **Zones** button appears
once another umpire is in; the eye in the page's head shows or hides the zones on your screen.

### Views

- **3D** (F2) switches between the table in three dimensions and the map read flat from above.
- **Aa** (P) puts plates behind the labels pieces write beside themselves, so they read on a busy map; text on the
  piece itself is left alone.
- **Frame** (Home) shows the whole map.
- The **compass** shows north, as the scenario sets it; click it, or press **N**, to turn north-up. Point at it and
  the zoom shows under it, in table units: click it and type a distance for the same view every time, or point at
  the zoom for a slider that dials it smoothly, closer to the right. The wheel over the compass zooms too.

### Log

Who joined, rolls, turns and sheets. The console of scripts is separate (Menu → Scripting).

### Turns

**Next turn ▸** moves the clock on and keeps a snapshot. Click the clock to set how long a turn lasts, in minutes,
hours or days. Pick a past turn under **History** to look back (read only); pick it again to return.

### The scenario

The scenario's name in the top bar, or **Menu → Scenario properties…**, opens its properties: **Game** (pointers,
clock, the global script), **Briefing**, **Map** (distances, north, grid, ground), **Light & surroundings** and **Wind**.

- **Briefing:** the situation you play from. Leads write it here and every umpire reads it here; **Read it…** opens it in a
  window of its own, with find. Observers never see it.
- **Light & surroundings:** presets and sliders, or **Follow the game clock** for dawn, day and dusk, the sun rising
  in the east and setting in the west. Under it, **Daylight** says when: type **Sunrise** and **Sunset** (as 05:30),
  or pick **Latitude and date** and give the **Latitude** (north above zero), and the game's date sets how long the
  day is, how high the noon sun stands and whether it passes to the south or the north; **From** and **Height** place the sun (click or drag the rose, Shift: 1°), and a
  low sun from one side brings out hills, which cast **Shadows** with the pieces; a backdrop past
  the table's edge (a room, a garden, a coast, open country, a sky by day, at sundown or at night, a solid colour or
  your own panorama; a sky sets its light); the table under the map, which grows to hold what is on it, from paper to
  black or see-through, which leaves the sheets and pieces over the backdrop alone.
- **North:** for a map drawn turned, drag the rose until its N points where north is on the map, or turn it with
  Q and E (Shift: 1°). The compass, bearings, the wind, the sun and the sighting buttons follow it.
- **Grid:** squares or hexes over the table. With **Pieces snap to the cells** ticked, a piece set down by hand lands on the centre of the nearest cell (the smaller cells when squares are divided); a group keeps its shape.
- **Ground:** the style, contour lines, snow line and streams of [painted ground](umpire-guide.md#relief).
- **Wind:** where it blows from and how strong. Smoke drifts with it.

## Play by post

- Close the game any time; host it again to go on. The password and the people let in stay.
- **To hand it over:** leave the table, **Export…** the scenario from its card and set **Saves** to
  **The save on the table** (or **Every save**). The other umpire resumes where you left off.
- **A turn went wrong:** **History** → the turn → **Load the end of turn N…** (host). The table goes back for
  everyone and play goes on in a new save beside the old one, which stays as it is.

## If something goes wrong

| Problem | Do |
|---|---|
| Someone cannot connect | Check password, address and UDP port (the port: title screen **Settings → Inviting**). "LAN only" at the top right, or "not open" in **Invite**: the router kept the port shut. |
| A piece is a grey square | Its picture is still coming from the host, or that PC lacks the pack. |
| A co-umpire's table looks wrong | They leave and rejoin; the table is sent again. |
| **NOT SAVED** in the top bar | The Log says why (disk full, folder gone). It clears once saving works. Disk full: **Settings → Storage** on the title screen clears what imports and joins downloaded. |
| The card says **Save damaged** | **Resume** continues from the newest copy; the damaged file is kept beside it. |
| You deleted something | **Scenarios → Trash** or **Piece studio → Trash**: thirty days to **Restore**. |
| The host's PC is gone | An umpire who was there: **Mirrored games** → **Host from the copy**. |
| A panel is off screen | **Settings → Interface → Put the panels back**. |
| The table stutters on a big map | **Settings → Graphics**: lower Render scale, Smooth edges and Map pictures. **Show the frame rate** puts frames a second in the top bar. |
| The app closed and says **The graphics driver stopped** | Update the graphics card's driver. If it happens again, **Restart through Direct3D 12** on that page (at the table: **Settings → Graphics → Draw through**). On a laptop, power from the wall and a **Frame limit** keep the card cooler. |
| Text and buttons are too small | **Settings → Interface → Interface size** sets them larger or smaller. The interface is sharpest at 100%; it doubles by itself on a 4K screen and follows Windows' own display scale. |
| Colours are hard to tell apart | Umpires' colours are chosen for colour-blind eyes. For maps and pieces, Windows has **Settings → Accessibility → Colour filters**. |
