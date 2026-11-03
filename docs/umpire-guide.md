# The umpire's guide

Hosting puts the table on your PC for your co-umpires and anyone you let in to watch. Keys below are the defaults; [Keys and mouse](keys.md) shows yours.

## Before the game

### Packs

A pack is a set of pieces. **Piece studio** on the title screen installs a `.ktpack`, imports a Tabletop
Simulator module, or makes a new pack. The standard dice are always there.

### Scenarios

A scenario is a map plus the pieces on it. **Host a game → New blank scenario…**, then add map sheets in the
**Map** drawer (PNG, JPG, TIFF, or a GIMP `.xcf`). Importing a TTS module also makes one. Right-click a card to
file, export or delete it.

### Saves

The table saves itself within a second. **Saves…** (Menu, Ctrl+S or a card's ⋯; host only) shows the scenario's
saves as folders and tiles, in `Documents\KriegsspielTable\saves\<scenario>\`.

- **Plays:** each play is a folder (**Run 1**, **Run 2 · from Turn 6**) holding every ended turn. **New play…**
  starts one from the scenario's start, the table now or a save; **Start over** on a card does the same.
- **Save** keeps the table under a name. **New folder** sorts your saves.
- **Load** puts a save on the table for everyone; play goes on in a new folder and the old one stays whole.
  **Look** shows a save without loading it.
- **Pick several** with Ctrl+click, Shift+click or Ctrl+A; Delete removes them. Title-screen lists work the same.
- **Trash**, at the foot of the folders, keeps deleted saves thirty days.
- **Co-umpires** keep the same saves. If your PC fails, they host from **Host a game → Mirrored games**.

## Hosting and the door

1. Pick the scenario, type a **Password** (or leave it empty for an open door), and **Host**.
2. **Invite…** (Menu, or the connection at the top right) shows the password and your addresses. Send them to the people you want.
   - Same network: the LAN address.
   - Internet: your public address. The app asks your router to open the UDP port; if the dialog says it
     could not, forward the port yourself.
   - Big game? **Export…** each pack listed there and post it first: people who install it download almost nothing
     from you when they join.
3. People who knock appear as a card under the top bar. Pick **Lead**, **Umpire**, **Observer** or **Refuse**.
   Their PC remembers the answer next time.
4. Once others are in, the connection at the top right also shows each umpire in their colour and how many watch.
   Click it for the list: the host and leads change roles, put people out and answer the door from there. Your
   own dot is faded while the others can't see your pointer.

### Roles

| Role | Can |
|---|---|
| Host umpire | everything; the game runs on your PC |
| Lead umpire | everything but making leads and loading saves: lets people in, sets roles, draws zones |
| Umpire | change the table; once zones exist, only pieces inside their own zone, in any way |
| Observer | watch the whole board; never sees zones, dice or pointers; changes nothing |

Change the password in **Scenario properties** (the ⋯ after the scenario's name).

## At the table

The tool strip runs down the left. Point at any tool and press **F1** for its section here.

- **Esc** puts down whatever is in hand; pressed again, it opens the menu.
- The top line says what just happened (a roll, a measurement), then goes quiet.
- Panels open beside their button. Drag them by the title bar, resize from the corner; they stay put.
- Other umpires' pointers show as gloved hands in their colour, with their names; a hand closes over what it
  carries. **Share my pointer** (Settings, or the connection at the top right) shows yours; Scenario properties
  can rule that **Umpires cannot hide their pointer**.

### Camera

| Do | To |
|---|---|
| Right-drag | orbit |
| Middle-drag, Space+drag, WASD | pan |
| Wheel | zoom (over a selected piece it turns it) |
| Home | frame the map |
| F2 | flat map view and back |

### Selecting and moving

- Drag a piece to carry it; it drops where you let go. Drag the empty table to box-select.
- **Q / E** turn 15° (Shift 1°). The wheel over a selected piece turns it 10° (Ctrl 2°, Shift ½°).
- **+ / −** resize, **Ctrl+1–9** form rows, **Ctrl+C / V** copy and paste, **Delete**, **Ctrl+Z** undo.
- **Right-click** a piece for everything else: looks, counters, markers, lock, POV, Properties.

### Turn and resize

**T** puts a ring round the selection: the round knob turns it, the square knob resizes (1/50 to 50 times).

### Pieces

- **Placing:** open the **Pieces** drawer and drag a piece onto the table (Shift keeps placing).
- **Turning over:** middle-click or **F**. Counters show where the maker put them (else on the back), with − / + to click.
- **Renaming:** click the name on the back and type; Enter keeps it.
- **Looks:** point at a piece and press **1–9** (0 is the tenth), or **[ ]** to step.
- **Label and description:** at the foot of the right-click menu. Both show on hover to everyone, observers
  included, and are in the scenario file: not a hiding place.
- **Markers:** drop a small piece on a larger one and it rides along. Drop it elsewhere to free it.
- **Locked:** right-click → **Locked** pins a piece: it can be read and deleted, not moved.
- **Effects** (fire, smoke, fog) are placed like pieces.
- **Scripts:** a piece's buttons and boxes work with a click. Right-click → **Scripting…** opens its script;
  see the [scripting guide](scripting.md).

### Dice

The **Dice** button opens a tray: tap d4 to d20 (twice for two), then **Roll**. Or drag dice from the standard
pack and throw them, or shake them in your hand before letting go; **R** rolls in place. The host decides every result, and the Log shows every roll.

### Ruler

**M**, then drag. It reads map units, or metres once the map is calibrated, with ticks and the bearing.

### Ranging

Pick the rings and click a piece: the wheel sets the reach, and a piece with a **range** counter uses its own.

### Calibrate

Drag along the map's scale bar and type what it measures. Distances then read in the scenario's unit
(Scenario properties → **Distances**: metres, yards or paces).

### Draw

Freehand, shapes (line, rectangle, ellipse, arrow) and a rubber that takes away only what it passes over. The
panel holds the ink, the pen's width (in screen pixels) and the layer.

### Layers

Drawings sit on layers, like tracing paper. The eye hides a layer **on your screen only**: handy before a
screenshot. Click a name to draw on it; the rubber works on that layer alone.

### POV screenshot

A turn report as a picture, from one piece's point of view.

1. Select the piece and press **V** (or right-click → POV screenshot from here).
2. Set how far it sees (the wheel, or the Radius field).
3. Drag to trace what blocks the view: walls, wood edges, ridges. What lies behind goes black.
4. **Enter** or **Capture**. The picture goes to the clipboard and the capture folder, stamped with the
   scenario, the clock and the side.

Nothing is sent or kept: paste it into that side's channel.

### Capture

**Ctrl+Shift+C** and drag a region: it goes to the clipboard and the capture folder. **Ctrl+Shift+X** repeats
it. Captures show the ground only, no tools.

Names too small in the picture? Raise **Settings → Capture resolution**, shrink the POV reach, or turn up
**Settings → Names on pieces**.

### Map sheets

In the **Map** drawer: add sheets, pick one (or Alt+click it), unlock to drag, nudge with the arrow keys, turn
or resize with **T**. **Crop…** trims borders; the sheet keeps its scale.

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
  wood, marsh, water, road, earth, rock, sand or snow; ✕ rubs paint out. On a sheet without relief, the first
  stroke gives it flat ground to shape. **Ctrl+wheel** sizes the brush; each stroke is one undo.
- Pieces lean with the slope under them and follow the ground when it changes.

### Umpires and zones

The **Umpires** drawer holds the zones. **Split the board…** gives ground to one umpire; a zone takes its
umpire's colour. Once any zone exists, an umpire edits only the pieces inside their own zone, and
cannot drop or place one outside it. Host and leads are never limited by zones. The **Umpires** button
and the zones button beside it, which shows or hides the zones on your screen, appear once another umpire is in.

### Views

- **2D** (F2) reads the map flat from above.
- **Frame** (Home) shows the whole map.
- The **compass** shows north; click it to turn north-up.

### Log

Who joined, rolls, turns and sheets. The console of scripts is separate (Menu → Scripting).

### Turns

**Next turn ▸** moves the clock on and keeps a snapshot. Pick a past turn under **History** to look back
(read only); pick it again to return.

### The scenario

The ⋯ after the scenario's name opens its properties: briefing, clock, light, grid, scale, wind, distances, script.

- **Briefing:** the situation you play from. Leads write it here and every umpire reads it here; **Read it…** opens it in a
  window of its own, with find. Observers never see it.
- **Light:** presets and sliders, or **Follow the game clock** for dawn, day and dusk.
- **Grid:** squares or hexes over the table. Nothing snaps to it.

## Play by post

- Close the game any time; host it again to go on. The password and the people let in stay.
- **To hand it over:** export the scenario and set **Saves** to **The game in play** (or **Every save**). The
  other umpire resumes where you left off.
- **A turn went wrong:** **History** → the turn → **Load the end of turn N…** (host). The table goes back for
  everyone and play goes on in a new folder of saves; the old one stays whole.

## If something goes wrong

| Problem | Do |
|---|---|
| Someone cannot connect | Check password, address and UDP port. "LAN only" in the top line: the router kept the port shut. |
| A piece is a grey square | Its picture is still coming from the host, or that PC lacks the pack. |
| A co-umpire's table looks wrong | They leave and rejoin; the table is sent again. |
| **NOT SAVED** in the top bar | The Log says why (disk full, folder gone). It clears once saving works. |
| The card says the save is damaged | Host continues from the newest copy; the damaged file is kept beside it. |
| You deleted something | **Host a game → Trash** or **Piece studio → Trash**: thirty days to **Restore**. |
| The host's PC is gone | An umpire who was there: **Mirrored games** → **Host from the copy**. |
| A panel is off screen | **Settings → Reset panel sizes**. |
| Text and buttons are too small | **Settings → Accessibility → Interface size**; **Match Windows** follows Windows' own display scale. |
| Colours are hard to tell apart | Umpires' colours are chosen for colour-blind eyes. For maps and pieces, Windows has **Settings → Accessibility → Colour filters**. |
