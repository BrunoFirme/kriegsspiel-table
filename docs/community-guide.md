# Modules, pieces and sharing

## Import a Tabletop Simulator module

**Piece studio → Import from Tabletop Simulator…**, then pick a module or save from the list, **Browse…** to a
`.json` (each one shows its module's picture), or paste a Steam Workshop link. The list holds what TTS has on this PC, wherever TTS keeps its mods (Documents, or the game's own
folder in any Steam library).

You get:

- a **pack** with every piece (bags opened up);
- a **scenario** laid out as in the module: boards become map sheets, the rest goes on the umpire board, and the
  module's own sky becomes its backdrop;
- a further scenario for each memory bag, with the layout it places;
- `import-report.md` beside the scenario: what did not convert. Pieces whose picture TTS cannot show either (the
  link is gone, or its server keeps answering with a web page instead of the picture) are left out and named there.
  A server that fails only once gets a stand-in picture, and the next import tries again.

| Converts | Does not |
|---|---|
| Tiles, tokens, boards, 3D models | Physics, hands (listed in the report) |
| Cards, and decks as piles | Asset-bundle models (houses, trees, ships) |
| Checkers, and six-sided picture dice | Bags as containers |
| States (formation, shattered…) | Plain TTS dice (use the app's) |
| IKS script counters and text | |
| Notecards, Notes and Notebook pages, as note cards you can type into | |
| Tints, names, attached markers, locks, face down | |
| Asset-bundle effects (smoke, fire, gun fire, mist, rain), as the closest of Firme's Effects | |
| Lua scripts and XML panels, translated | |
| Decals, and the pictures scripts set from web links | |
| Locked text and images (Lock text, Lock images) | |

Scripts keep working: see [what imported TTS scripts become](scripting.md#what-imported-tts-scripts-become).

What an import downloads stays on this PC, so a module imports again without downloading. **Settings → Storage**
on the title screen shows its size and clears it; imported packs keep their own copies.

## Export to Tabletop Simulator

**Menu → Export to Tabletop Simulator** writes the umpire board as a TTS save in your TTS Saves folder. Pictures
keep their Steam links; your own pictures load only on your PC.

## The piece studio

**Piece studio → New pack…**, or **Edit pieces** on any pack. Tools down the left, the work in the middle,
the pack's pieces and the chosen piece down the right. The top line says what the tool in hand does.
[Keys and mouse](keys.md#piece-studio) lists the studio's keys.

☰ → **Pack details…** sets the pack's name, who made it and a description, shown in the piece studio's list of packs.

### New pieces

**＋ New piece** (Ctrl+N) makes a block, a tile, a token, pieces from pictures or the clipboard, a 3D model, or an
effect (smoke, volleys, guns, fire, dust, mist; from bows to the World Wars, and at sea). Pictures and models dropped
on the window become pieces too. Every picture chooser also takes a web link.

### The views (Alt+1, 2, 3)

**3D** (Alt+1) — the piece as the table shows it.

- **Layers** lists what the piece is made of. The **Block** layer holds the piece's outline (rectangle,
  round, hex, shield, banner, along the picture and more, or **Frame…** for a border), its width, depth,
  thickness and side tint. A look can have its own size and outline.
- **Add a shape or a model** builds onto it: a box, a cylinder, a flag… or a model file (glTF, GLB, FBX, OBJ,
  STL, PLY), which sits centred on top. A model file dropped on the 3D view does the same; dropped anywhere
  else it becomes a piece of its own. A layer keeps the shape it was added as: for another, add it and delete
  the old layer. Every layer, the block and each shape, lists its **Faces** to paint in 2D. A 3D piece's width
  on the table and its tint are under **On the table**.
- **Texture…** on an imported model lays on a texture made for it, as its maker drew it; **Picture…** on a shape
  stretches a picture over one face.
- Tools: look round H, move V (drag the X, Y and Z arrows, or the shape itself), brush B, rubber E, fill G,
  colour I, decal D; **F** turns the piece over. A shape chosen in 3D opens its faces in 2D.
- Saved as one OBJ and one picture, as TTS uses.

**2D** (Alt+2) — a face laid flat, built of layers: pictures, paint, text and the unit plate (colours, label, unit
symbol).

- Pick the look and the side, **Front** or **Back**, under the picture; **F** turns it over.
- Each layer moves, turns, resizes, mirrors, crops, fades, hides or locks. The rubber touches only its layer.
- Tools: move V, brush B, rubber E, shapes U, text T, fill G, colour I; crop C, outline O, place N,
  measure M, hand H.
- Grid, snap, symmetry and measures help you draw; none of them print.
- **Outline** (O) offers the same outlines as the Block layer, or draw your own corner by corner.
- **Place** (N) gives the name, each counter and each text line a box of its own, on either side; counters
  keep their − and +. Unplaced, counters print on the back and text lines in the name band.
- **Swap front and back** trades the two sides, prints and all.

**Try it** (Alt+3) — the pack's pieces on a real table with scripts running. An effect has **Try it** and
**Picture**, the particle it is drawn with.

### The chosen piece

| Tab | Holds |
|---|---|
| Layers | what the face or the piece is made of, and the Block layer's outline and size |
| Piece | name, tags, looks, counters, text lines, markers (put down from its right-click menu at the table) |
| Script | the piece's Lua, starter snippets, **Save and run** (Ctrl+Enter) |

Looks that are still copies of look 1 follow it as you paint. Scripts draw the rest: see the
[scripting guide](scripting.md).

Every change saves at once and layers reopen as you left them; **Ctrl+Z** takes back the last one, stroke, shape
or setting. In a game, your packs
upload to the host when you place their pieces. Limits: pictures and models only, 25 MB a file, 300 MB a pack.

## Share packs and scenarios

A `.ktpack` file carries a pack or a scenario.

- **A pack:** **Export…** beside it in the piece studio.
- **A scenario:** **Export…** under its card. Carry **only the pieces it uses**, **whole packs**, or **no packs**.

To install, drop the file on the window or use **Install a shared file**. A trimmed pack never replaces a full
one you have.

### On the Steam Workshop

In the Steam release, the **Steam Workshop** page publishes packs and scenarios and lets you subscribe. Steam
keeps subscribed items up to date.
