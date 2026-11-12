# Modules, pieces and sharing

## Convert a digital tabletop module

**Piece studio → Convert from…**, then pick a module or save from the list, **Browse…** to a
`.json` (each one shows its module's picture), or paste a Steam Workshop link. The list holds the modules and saves on this PC, wherever the digital tabletop keeps them
(Documents, its folder in a Steam library, or on Linux its own, Flatpak or Proton folder).

You get:

- a **pack** with every piece, the contents of bags included;
- a **scenario** laid out as in the module: boards become map sheets, the rest goes on the umpire board, and the
  module's own sky becomes its backdrop; a bag becomes a box, chest or crate of Firme's Effects and Props, by its name
  and how much it holds, in the bag's colour, holding its contents off the table and opened from its right-click card, and a memory bag places and recalls its layout from there;
- `import-report.md` beside the scenario: what did not convert, and a list of everything the module uses that is
  not taken from the module exactly, with what to do instead. Pieces whose picture the module cannot show either (the
  link is gone, or its server keeps answering with a web page instead of the picture) are left out and named there.
  A server that fails only once gets a stand-in picture, and the next conversion tries again.

Converting a module again updates its pack in place: pieces already on your tables take their kind's new look, and
a kind the module no longer has stays in the pack, left out of **Pieces**, so the pieces placed from it still show.

| Converts | Does not |
|---|---|
| Tiles, tokens, boards, 3D models | Physics, hands, hidden zones, grid, lighting, turns (listed in the report) |
| Cards, and decks as piles | Infinite bags (place pieces from the Pieces panel) |
| Checkers, and picture dice of 4, 6, 8, 10, 12 and 20 sides | |
| Plain dice, as the app's standard dice where each stood, at its size and face | |
| Bags, as boxes, chests and crates holding their contents | |
| States (formation, shattered…) | |
| IKS script counters and text | |
| Notecards, Notes and Notebook pages, as note cards you can type into | |
| PDFs, as books: every page comes over, and double-clicking one at the table opens it to read | Cards dealt by script (listed in the report) |
| Tints, names, attached markers, locks, face down | |
| Figurines, their picture standing in a round base | |
| Glued parts, as part of their piece in every look | |
| Mirrored and tilted pieces, as the module left them | |
| Stacks of chips, tiles or tokens, as that many pieces | |
| 3D text, as lettering on its piece | |
| Asset-bundle figures, as 3D pieces in the module's own pack | |
| Asset-bundle effects (smoke, fire, gun fire, mist, rain), as the closest of Firme's Effects | |
| Lua scripts, translated, and XML panels, in the fonts they name | |
| Decals, the module's own and those stamped on the table, and the pictures scripts set from web links | |
| Locked text and images (Lock text, Lock images) | |

Scripts keep working: see [what converted module scripts become](scripting.md#what-converted-module-scripts-become).

What a conversion downloads stays on this PC, so a module converts again without downloading. **Settings → Storage**
on the title screen shows its size and clears it; converted packs keep their own copies.

## Convert to a digital tabletop save

**Menu → Convert to a digital tabletop save → The umpire board…** writes the umpire board as one, with your other saves. Pictures
keep their Steam links; your own pictures load only on your PC.

## The piece studio

**Piece studio → New pack…**, or **Edit pieces** on any pack. Tools down the left, the work in the middle,
the pack's pieces and the chosen piece down the right. The top line says what the tool in hand does.
[Keys and mouse](keys.md#piece-studio) lists the studio's keys.

The pack's name is the field in the top line. ☰ → **Pack details…** sets who made it and a description, shown in
the piece studio's list of packs.

### New pieces

**＋ New piece** (Ctrl+N) makes a block, a tile, a token, a figure on a base, pieces from pictures or the clipboard, a 3D model, a book from a PDF, or
copies any effect, prop or marker of **Firme's Effects and Props**, or one of the standard dice, into the pack, to change like any piece of your own;
the originals stay as they are. Pictures and models dropped on the window become pieces too. Every picture chooser also
takes a web link.

### The views (Alt+1, 2, 3)

**3D** (Alt+1) — the piece as the table shows it.

- **Layers** lists what the piece is made of. The **Block** layer holds the piece's outline (rectangle,
  round, hex, shield, banner, along the picture and more, or **Frame…** for a border), its width, depth,
  thickness and side tint. A look can have its own size and outline.
- **Add a shape or a model** builds onto it: a box, a cylinder, a flag… or a model file (glTF, GLB, FBX, OBJ,
  STL, PLY, Collada), which sits centred on top. A model file dropped on the 3D view does the same; dropped anywhere
  else it becomes a piece of its own. A layer keeps the shape it was added as: for another, add it and delete
  the old layer. Every layer, the block and each shape, lists its **Faces** to paint in 2D. A 3D piece's width
  on the table and its tint are under **On the table**.
- **A piece of this pack**, in the same menu, stands another piece of the pack on this one as a part: a base
  with ten figures is one piece on the table. Its layer gives its place (**Across**, **Ahead**, **Up**), **Turn**,
  **Size**, **Tint** and **Look**; **Repeat** lays **Count** of them in **Ranks** over the block. Parts are linked: a
  change to the figure shows on every piece built from it. A part's script, counters, text and markers stay off.
- A shape's **Place** is its centre. **Turned profile**, **Tube along a path** and **Outline with depth** take their
  form from points: drag one, double-click a line to add one, right-click one to take it out.
- **Join** sets how a shape meets the shapes above it in its list: **Add**, **Cut** or **Overlap** makes one solid,
  and **Blend** rounds the join. Paint on a solid stays on it as its shapes move. **Mirrored twin** copies a shape
  across the middle; the arrows change the order; a **group** carries its shapes with it.
- **Sit on**, **Centre on** and **Measure to** place a shape against another; the move tool's **Snap** lands drags on
  steps.
- **Texture…** on an imported model lays on a texture made for it, as its maker drew it; **Picture…** on a shape
  stretches a picture over one face.
- Tools: look round H, move V (drag the X, Y and Z arrows, or the shape itself), turn R (drag a ring, or the shape
  itself to spin it where it stands; Shift steps by 15°), brush B, rubber E, fill G, colour I, decal D; **= / −** or Ctrl+wheel size the brush or the decal, **Q / E** turn a decal or, with turn, the chosen shape (Shift 1°); **F** turns the piece over; 1, 3 and 7 look from the front, right and top (Shift: the other
  side), 5 is the flat view. A shape chosen in 3D opens its faces in 2D.
- The brush paints hard edges unless **Soft edge** is raised. Fill takes the patch of the colour clicked, over the
  seams: **Reach** lets in nearby shades, **Stop at bends** ends it at sharp edges such as a helmet's rim, and
  **Shift-click** paints the whole shape.
- Saved as one OBJ and one picture.

**2D** (Alt+2) — a face laid flat, built of layers: pictures, paint, text and the unit plate (colours, label, unit
symbol).

- Pick the look and the side, **Front** or **Back**, under the picture; **F** turns it over.
- Each layer moves, turns, resizes, mirrors, crops, fades, hides or locks. The rubber touches only its layer.
- Tools: move V, brush B, rubber E, shapes U, text T, fill G, colour I; crop C, outline O, place N,
  measure M, hand H.
- Grid, snap, symmetry and measures help you draw; none of them print.
- **Outline** (O) offers the same outlines as the Block layer, or draw your own corner by corner.
- **Place** (N) gives each counter and each text line a box of its own, on either side; counters keep their
  − and +. What is not placed is not printed.
- **Text lines** print in the colour set beside each in the Piece tab; a new one on a block starts in the block's
  label colour.
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
or setting.

In a game, an umpire's packs upload to the host when their pieces are placed. Limits: pictures and models only,
25 MB a file, 300 MB a pack, 2 GB a person a sitting. A pack already on the host changes only when its author
sends it, or whoever sent it earlier that sitting; otherwise the host removes it first (Piece studio → the pack's
⋯ → **Remove from this PC…**).

## Share packs and scenarios

A `.ktpack` file carries a pack or a scenario.

- **A pack:** its ⋯ in the piece studio → **Export…**.
- **A scenario:** its card's ⋯ → **Export…**. Carry **only the pieces it uses**, **whole packs**, or **no packs**.
  **Table** chooses what the file shows: the table as it stands now, the setup, or the map only. Once the scenario
  has a game it starts on the setup, so a file handed to players does not show where the pieces are; **Saves** adds
  the save on the table or every save, to [hand a game over](umpire-guide.md#play-by-post).

To install, drop the file on the window or use **Install a .ktpack file…**. A trimmed pack never replaces a full
one you have.

### On the Steam Workshop

In the Steam release, the **Steam Workshop** page publishes packs and scenarios and lets you subscribe. Steam
keeps subscribed items up to date.
