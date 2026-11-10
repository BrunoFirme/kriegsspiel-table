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

- **Menu → View**, or a right-click on the strip, lists its buttons: untick one to take it off the strip, tick it to
  bring it back. Its key still works. Map, Pieces, Umpires, Select and Log always stay.
- **Esc** puts down whatever is in hand; pressed again, it opens the menu.
- The top line says what just happened (a roll, a measurement), then goes quiet.
- Panels open beside their button and stay where you drag them by the title bar. Pieces, Log, Decals and a
  holder's window resize from the corner.
- Other umpires' pointers show as gloved hands in their colour, with their names; a hand closes over what it
  carries. **Share my pointer** (Settings → Table, or the connection at the top right) shows yours; Scenario properties
  can rule that **Umpires cannot hide their pointer**.

### Camera

| Do | To |
|---|---|
| Right-drag | orbit |
| Middle-drag, Space+drag, WASD | pan |
| Wheel | zoom (over a selected piece it turns it) |
| Home | frame the map |
| F2 | flat map view and back |
| Ctrl+1–9 | keep this view (unless carrying or with several picked); also in the right-click menu |
| Shift+1–9 | go back to a kept view |

Kept views are yours, for each scenario. Right-click one in the menu to forget it.

### Selecting and moving

- Drag a piece to carry it; it drops where you let go. Drag the empty table to box-select.
- **Q / E** turn 15° (Shift 1°) and **= / −** resize (1/50 to 50 times). The wheel over a selected piece turns it
  10° (Ctrl 2°, Shift ½°); **Alt+wheel** zooms there.
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
- **Right-click** a piece for everything else: looks, counters, markers, lock, tint, POV, Properties.

### Turn and resize

**T** puts a ring round the selection: the round knob turns it, the square knob resizes. **Shift** snaps the turn
to 15° and the size to tenths.

### Formations

**G** opens the Formations tool. With pieces selected, pick a formation from its panel or press its key. The panel
groups them, and names what each was used for, from the phalanx to the tank platoon:

- **Lines:** **R** line, **Shift+R** stretch in a line, **X** chequer, **Shift+C** crescent, **Z** / **Shift+Z**
  echelon right / left, **Shift+X** open order.
- **Columns:** **T** column (one abreast is single file, many abreast a block), **Shift+T** staggered column,
  **H** herringbone.
- **Shapes:** **Shift+B** square, **C** ring, **Y** wedge, **V** vee, **F** diamond.

- The pieces march the shortest way to their places round where they stand; markers on them ride along. One undo.
- Pick pieces as with Select: click one, **Shift+click** to add or take one out, drag a box on the empty table
  (**Shift** adds), click the empty table to let them all go. **Double-click** a piece for every piece of its kind in view.
- **Right-drag** where the front should stand: the drag sets which way it faces, and how far you drag sets how many
  stand in the front rank. For a column the right-drag is its line of march instead: it faces along the drag, its
  head where the drag ends. A right-click alone still opens the menu; **Alt+right-drag** turns the view.
- In stretch in a line, the right-drag draws the line itself, straight, curved or in an S: the pieces are strung evenly
  along it, the first at its start and the last at its end, each facing across it the way a line drawn left to right
  faces the top. Never closer than the spacing: on a line too short for them, the last run on past its end, and the
  status line says so. Draw it round on itself and they go evenly all the way round. Draw back over it to shorten
  it; the line bends at most 60° in a piece's width, so a sharp corner comes out rounded rather than folded.
- **Q / E** wheel the whole formation (Shift 1°); a drawn stretch turns whole, keeping its shape. **= / −** set the
  spacing (0 is shoulder to shoulder), a drawn stretch keeping its line; **[ ]** the ranks of a line, or how many
  abreast in a column. The wheel over the pieces turns each in place, as everywhere; off them it zooms.
- Drag one of the pieces to carry the formation as it stands.

### Pieces

- **Placing:** open the **Pieces** panel and drag a piece onto the table (Shift keeps placing).
- **Turning over:** middle-click or **F**. Counters show where the maker put them, with − / + to click.
- **Notes:** the **Note** in Firme's Effects and Props is a message form. Click its title or its lines and type:
  in the title Enter keeps it; in the lines Enter starts a new line and Ctrl+Enter keeps it; Esc leaves it as it
  was. Ctrl+click picks the note out instead.
  A module's notecards, Notes and Notebook pages come in as these notes.
- **Looks:** point at a piece and press **1–9** (0 is the tenth), or **[ ]** to step.
- **Books:** a PDF brought in from a module, or made in the piece studio, is a book. Double-click it, or
  right-click → **Read…**, to turn its pages in a window of your own; **Show on the table** turns the piece
  itself to the page you are on, for everyone.
- **Label and description:** at the foot of the right-click menu. Both show on hover to everyone, observers
  included, and are in the scenario file: not a hiding place.
- **Markers:** drop a small piece on a larger one and it rides along. Drop it elsewhere to free it.
- **Attached pieces:** in the **Pieces** panel, **On the table** lists what rides on what. Drag rows onto another
  piece's row and they ride on it: moving it carries them and everything riding on them, however far away they
  stand, while one moved alone goes alone and stays attached. Drop rows between two rows to ride on what those
  ride on, or below the list to free them. With several picked, right-click → **Attach the others** does the same.
- **Holding:** a piece that holds pieces keeps them off the table, as a bag does. Hold pieces over it a moment: a
  ring fills round it and they sink towards it; let go then and they go in, or move off first to keep them out. Pieces
  dropped on its window go in too. Right-click → **Open** shows what it holds beside the piece, until it leaves the
  view: drag one out onto the table, double-click to set it beside, **Take all out** for a row. Right-click →
  **Holds pieces** switches it on or off for any piece; the piece studio's **Holds pieces** sets where a kind starts,
  and the box, chest and crate of Firme's Effects and Props start on. A held piece keeps everything it had. A holder
  goes inside a holder seven deep and no deeper, as a matter of taste: nothing good is kept further down.
- **Ready piece:** one held piece can be its holder's ready piece, smoke or dust for instance: right-click it in the
  holder's window → **Ready piece**. It is listed first. **B** on the holder releases it onto the table; **B** on the
  holder or on the piece recalls it. Recalled, it remembers where it stood, so it comes out there again, moved and
  turned with the holder. Also in the right-click menu.
- **Locked:** right-click → **Locked** pins a piece: it can be read, not moved. A selection box leaves it out and the
  Delete key leaves it be; right-click → **Delete** removes it.
- **Tint:** right-click → **Tint** colours every selected piece: a swatch, **+** for any colour, **Reset** for the
  colour it was made with. Where it shows follows the module: a token's picture, a tile's sides; decals keep their colours.
- **Effects** (smoke, fire, volleys, melee, mist) and props are pieces of **Firme's Effects and Props**; resized, an effect
  covers more ground. Right-click → **Running** stops or starts one, and **Play once** plays it through once on every
  screen. To change one, copy it into a pack of your own in the piece studio (＋ New piece).
- **Worth:** pieces a module gave a worth (chips, supply) add up at the head of the right-click menu when several are chosen.
- **Scripts:** a piece's buttons, boxes and panels work with a click. Right-click → **Scripting…** opens its script;
  see the [scripting guide](scripting.md).

### Dice

The **Dice** button shows or hides the dice panel: tap d4 to d20 (twice for two), then **Roll**. The result stays in the
panel for the next roll. Or drag dice from the standard pack and throw them, shake them in your hand before letting go,
or press **R** to roll in place. The host's PC decides every result and the Log shows it; a die set to **Thrown for
real** in its right-click menu counts the face it lands on instead. Hover a die for its result;
right-click it for its **Tint**. To turn a die by hand, point at it and press the side's number.
Right-click several dice to see what they show and their total. For bigger dice or dice of your own, copy them into a
pack of your own in the piece studio (＋ New piece, **Dice**).

### Ruler

**M**, then drag. It reads map units, or metres once the map is calibrated, with ticks, the bearing and, over
relief, the rise or fall.

### Ranging

Pick the rings and click a piece: type the reach in the **Ranging** panel or set it with the wheel, and a piece with a **range** counter uses its own.

### Calibrate

Drag along the map's scale bar and type what it measures. Distances then read in the scenario's unit
(Scenario properties → **Map** → **Distances**: metres, yards or paces).

### Draw

Freehand, shapes (line, arrow, curve, curved arrow, area, rectangle, ellipse), a name and a rubber that takes away
only what it passes over. The panel holds the ink, how see-through it is, the pen's width (in screen pixels) and
the layer. A curve or an area is clicked bend by bend: double-click or **Enter** ends it, **Backspace** takes a
point back, and an area is washed in the ink.

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

Drawings sit on layers, like tracing paper. The eye hides a layer **on your screen only**: handy before a
screenshot. Click a name to draw on it; the rubber works on that layer alone. **+** adds a layer, a double-click
renames it, and **✕** removes it with its drawings.

### POV capture

A turn report as a picture, from one piece's point of view.

1. Select the piece and press **V** (or right-click → POV capture from here).
2. Set how far it sees (**Ctrl+wheel**, **= / −**, or the Radius field); the wheel zooms in to trace. **Sharp to**
   pulls a ring in from the edge: past it the picture blurs, gently at first and hard at the edge, as distance takes
   detail; all the way right is sharp everywhere. **Edge blur** sets how strong it gets at the edge. The reach, the
   ring and the blur are kept with the scenario. **Format** makes the picture round or square.
3. Drag to trace what blocks the view: walls, wood edges, ridges. What lies behind goes black. **Ctrl+Z** takes
   back the last line, **Clear** all of them.
4. **Enter** or **Capture**. The picture goes to the clipboard and the capture folder (**Settings → Captures**
   chooses which), its file named for the scenario, the turn and the time.

The picture holds the pieces in sight at the moment it is taken, wherever they stood when the tool came out, and
a GIF follows them as they move. Zones and other umpires' pointers are never in it.

**View → First person** looks out from the piece's front edge, the way it faces: drag to look around, or pick
**N E S W** under **Facing** to hold a direction (dragging lets go). **Lens → Wide** takes 90° across, so the four
directions make the whole circle. **Eye** slides the viewpoint between the ground at the piece's feet and well over
its head: drop it to look out from behind a crest. There is nothing to trace from the eye; **Ctrl+wheel** or
**= / −** still set the reach.
Lines traced in 2D still cut off what lies behind them. Each Capture is one picture.

**Text → Hidden** leaves names, counters, notes and the studio's own lettering out of the picture, and off the
table while the tool is out. Words baked into imported pictures stay.

**Shot → GIF** records a moving picture instead. **Record** or **Enter** starts it, **Stop** or **Enter** ends it,
**Cancel** drops it. From above it is 720 × 720, from the eye 768 × 432, 10 frames a second, and it stops by
itself at 60 s or 9 MB, under Discord's upload limit. It goes to the capture folder, and to the clipboard as a
file: Ctrl+V in Discord uploads it. While it records the reach stays as it was.

Nothing is sent: paste it into that side's channel.

### Capture

**Ctrl+Shift+C** and drag a region: it goes to the clipboard and the capture folder. **Ctrl+Shift+X** repeats
it. Tools, zones and pointers are left out.

Names too small in the picture? Keep **Settings → Captures → Resolution** at 4×, shrink the POV reach, or turn up
**Settings → Table → Text size**.

### Map sheets

In the **Map** drawer: add sheets (files or web links), pick one (or Alt+click it), unlock to drag, nudge with the arrow keys, turn
or resize with **T**. **Opacity** lets what lies under a sheet show through it, to line an old map up over another.
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
- **New blank ground…** adds flat ground of any size, to build terrain from nothing.
- The selected sheet's **Relief** group: **Height ×** (0 flat, 1 true proportions), **Add / Replace / Remove
  relief…** and **Picture…**.
- The **Relief** tool (the hill on the tool strip) raises, lowers, smooths and flattens, and paints grass, field,
  wood, marsh, water, road, earth, rock, sand, snow or town; ✕ rubs paint out. A road drawn across water is a
  bridge, a road through a town its street. On a sheet without relief, the first stroke gives it flat ground to
  shape. **= / −** or **Ctrl+wheel** size the brush; each stroke is one undo.
- The Relief tool needs the map's scale: calibrate first, let **New blank ground…** set it, or add a GeoTIFF,
  `.asc` or `.hgt` with **Set the map's scale from the file** ticked. Painted ground is
  drawn at that scale, in the style's own hand: each stroke draws its own plots, hedges, farms, trees, streets and
  walls, afresh every time it is painted. Field and grass take **Plots** and **Buildings**, wood **Trees**, road
  **Road** (a track, a country road or a chaussée; a bridge keeps its road's), town **Houses** and **Walls** (none,
  a wall with towers, a bastioned fort); the rows show under Density for the ground in hand. Town is the one brush
  for village, town and city: its size says which.
- Painting: **Fill** colours the whole patch clicked, up to other ground types (a bare sheet whole), so a map starts
  with one click of grass; on its own kind it lays the patch afresh. The **rectangle** and **ellipse** beside it paint a field, a wood or a pond in one drag
  (**Shift** for a square or a circle, lined up with the view); the **polygon** paints the shape of its corners,
  clicked in turn and closed on the first or with a double-click (**Esc** drops it). Each shape is one undo.
  **Route** lays ground that finds its own way: click the waypoints, then the last again or double-click, and
  between them the ground takes the easiest path over the relief at the brush's width, as a road would: cheap
  along roads and into towns, dear through wood, marsh and rock, over narrow water as a bridge and round wide
  water. With the water swatch the first click is a spring: a river runs down by the steepest descent, widening
  as it goes, until it meets water, the sheet's edge, or a hollow, where it ends in a pond. One route, one undo.
  **Density** below 100% lays the ground in clumps: sparse wood, scattered rock, broken
  marsh; with ✕ it thins a wood. **Shift+click** runs straight on from the last stroke, for roads, rivers and
  ridges. **Alt+click** picks up the ground under the cursor.
- **Map → Ground** in the scenario's properties sets, for everyone, the **Style** painted ground is drawn
  in (**Ancient**, **18th-century survey** or **Modern topographic**) and turns on **contour lines** over every
  relief: redrawn as the ground is shaped, stepping in round heights once the map's scale is calibrated, every fifth
  line numbered. **Snow line** whitens every ground above a height in metres, once the map's scale is set.
  **Streams** draws brooks where the relief gathers water, heavier the more it gathers, over every ground but
  painted water, which swallows them; it needs the map's scale, and floors flatter than a fifth of a percent
  stay dry. Where a road crosses a brook, culvert ticks mark it either side. Painted water under 100 m wide
  shows its current wherever the relief falls: strokes and an arrow pointed downhill, ticks across the flow
  where it drops two in a hundred, a step mark where it drops five metres in a stride.
- Pieces lean with the slope under them and follow the ground when it changes.

### Umpires and zones

The **Umpires** drawer holds the zones. **Split the board…** gives ground to one umpire; a zone takes its
umpire's colour. Once any zone exists, an umpire edits only the pieces inside their own zone, and
cannot drop or place one outside it. Host and leads are never limited by zones. The **Umpires** button
and the zones button beside it, which shows or hides the zones on your screen, appear once another umpire is in.

### Views

- **3D** (F2) switches between the table in three dimensions and the map read flat from above.
- **Aa** (N) puts plates behind the labels pieces write beside themselves, so they read on a busy map; text on the
  piece itself is left alone.
- **Frame** (Home) shows the whole map.
- The **compass** shows north; click it to turn north-up.

### Log

Who joined, rolls, turns and sheets. The console of scripts is separate (Menu → Scripting).

### Turns

**Next turn ▸** moves the clock on and keeps a snapshot. Click the clock to set how long a turn lasts, in minutes,
hours or days. Pick a past turn under **History** to look back (read only); pick it again to return.

### The scenario

The scenario's name in the top bar, or **Menu → Scenario properties…**, opens its properties: **Game** (pointers,
clock, the global script), **Briefing**, **Map** (distances, grid, ground), **Light & surroundings** and **Wind**.

- **Briefing:** the situation you play from. Leads write it here and every umpire reads it here; **Read it…** opens it in a
  window of its own, with find. Observers never see it.
- **Light & surroundings:** presets and sliders, or **Follow the game clock** for dawn, day and dusk; a backdrop past
  the table's edge (a room, a garden, a coast, open country, a sky by day, at sundown or at night, a solid colour or
  your own panorama; a sky sets its light); the table under the map, which grows to hold what is on it, from paper to
  black or see-through, which leaves the sheets and pieces over the backdrop alone.
- **Grid:** squares or hexes over the table. Nothing snaps to it.
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
| The table stutters on a big map | **Settings → Graphics**: lower Render scale, Smooth edges and Map pictures. |
| Text and buttons are too small | The interface grows with the window and the screen; **Settings → Interface → Interface size** sets it larger or smaller on top. |
| Colours are hard to tell apart | Umpires' colours are chosen for colour-blind eyes. For maps and pieces, Windows has **Settings → Accessibility → Colour filters**. |
