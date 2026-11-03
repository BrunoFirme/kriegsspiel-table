# The tools

For people who change the code: what each script under `tools/` and `tests/lock/` does, how to run it, and the
reasons behind what it does that the code cannot show. [architecture.md](architecture.md) is the map of the code itself.

## `tools/check.ps1`: the gate

```
powershell -ExecutionPolicy Bypass -File tools\check.ps1 [-Quick] [-Lock <git ref>]
```

Everything a change must pass: the solution, the app (twice, once with Steam built in so its code keeps compiling
while it is switched off), every test, the app's node tests (headless, with every folder the app could write moved
into a temporary one, five minutes at most), the app's screen tests (the same runner in a real 1280×720 window,
for what needs one; the window shows for a moment), and `codecheck.py`. Without `-Quick` it also lists packages
with known vulnerabilities (reported, not failed on).

`-Lock <ref>` also runs the behaviour lock against that commit: the ref is checked out beside the repo for the run,
and both the app and the converter are compared with it. The lock needs no hands: it never moves the pointer, never
types into other windows and leaves the clipboard alone (see below), so the PC stays usable while it runs.

## `tools/codecheck.py`: size and hygiene

No class over 1,500 lines (partial classes counted together), no method over 100, and no comments in any code the
repository holds: C#, Python, PowerShell, MSBuild files, shaders, Lua, the lock scripts and `.editorconfig`. Names and
structure say what the code does; reasons that cannot be said in code belong in these docs. Left out: test fixtures
(they stand for other people's files), the generated `docs/ktable-api.lua`, and Godot's own `project.godot` header.
`--report` lists the largest classes and longest methods.

## `tools/export-windows.ps1`: a release

```
powershell -ExecutionPolicy Bypass -File tools\export-windows.ps1 [-Out <folder>] [-Zip] [-Steam]
```

Needs Godot .NET (`winget install GodotEngine.GodotEngine.Mono`), its export templates for the same version (Godot
editor → Editor → Manage Export Templates, or the `.tpz` unpacked into `%APPDATA%\Godot\export_templates\<version>.mono\`)
and the .NET SDK. The preset is `src\App\export_presets.cfg` ("Windows Desktop").

- The engine leaves `standard\` (it has a `.gdignore`) and the native Steam libraries out of the `.pck`, so they are
  copied next to the exe, where an exported build looks for them.
- The guides that ship are a fixed list, so nothing else that lands in `docs\` ships by accident.
- The converter is published self-contained, so players need no .NET either, and its `ttsModsPath` is emptied so it
  looks for the player's own Tabletop Simulator folder.
- Steam is left out unless `-Steam` is given: the build then has no Steamworks code and no Valve libraries.

## `tools/net-test.ps1` and `tools/NetRelay`: playing over a bad connection on one PC

```
powershell -ExecutionPolicy Bypass -File tools\net-test.ps1 [-Clients good,mobile,"dsl,loss=3"] [-Scenario <id>]
    [-Packages <folder>] [-HostUpKbps <kbit/s>] [-Port 7790] [-Engine <godot.exe>] [-Keep]
```

Opens a host and one client per entry in `-Clients`, tiled on the screen, with NetRelay between each client and the
host. Each client gets its own relay port, and that port behaves like the connection named: ping, jitter, loss in
bursts, duplicates, late packets, a bandwidth limit with a queue that fills and then drops. `-HostUpKbps` limits the
host's upload, shared by everyone, as a home connection does when a pack goes out to a full table. Let the players in
on the host as usual; the relay's own window takes commands while it runs (`cut 7792 10` drops a client for ten
seconds, `set all awful` makes everyone's line bad, `help` lists them). Closing the host closes everything.

- The scenario and only the packs it lists are copied from `-Packages` (the dev build's packages unless given), and
  every window gets its own user folders inside a temporary run folder, removed at the end (kept with `-Keep` or
  when something failed). Nothing is written back to the folder the scenario came from.
- `-Engine` runs the windows on another Godot binary of the same version.
- The test host is an ordinary host, so it asks the router for its port through UPnP like any other, and gives it
  back when it closes.
- Traffic on one PC never reaches the router, the firewall or the ISP. Whether people outside can join (port mapping,
  firewall, carrier-grade NAT) needs a real outside connection, such as a phone's.
- Everything runs on one CPU and one GPU, so a window's frame rate says little about a real player's.

NetRelay also runs on its own (`dotnet run --project tools\NetRelay -- --help`), between any client and host, and
with `--open` takes clients from other computers. Clients on one relay port share its line, like a household.
Windows only wakes a sleeping thread every 15.6 ms unless asked, so the relay asks for 1 ms timers and spins the
last 2 ms before a packet is due; otherwise every delay would be rounded up by as much as a frame.

## The behaviour lock: `tests/lock/app_lock.py`

```
python -I tests/lock/app_lock.py --old <worktree> --new <worktree> [--quick] [--only <glob>] [--runs N] [--no-build]
python -I tests/lock/app_lock.py --one <script> --wt <worktree> [--out <dir>] [--trace]
```

Plays every script in `tests/lock/app/*.lock` on an OLD and a NEW build of the app and compares what comes out. Each
run starts from a fresh live folder: a copy of `tests/lock/app/seed` as the packages root (built locally by
`make_seed.py`, never committed: it holds third-party packs), an empty user folder standing in for every place the app
keeps the person's data, and the fixtures. Every run uses the same live path, so paths shown on screen agree.

**No hands needed.** The app's harness (`src/App/dev/LockHarness.cs`, `--lock`) feeds the app its input as engine
events, and the app reads the pointer through `Pointer`, which the harness drives, never the real cursor. The app's
clipboard is kept in the app for the run (`AppClipboard.KeepInApp`). A lock window that comes to the front, as Windows
lets a new window do, hands the focus straight back to the window that had it when the script started. Real input
that reaches a lock window is swallowed; a real key or button press, or the window losing focus while the run holds a
button, marks the run disturbed and it is played again.

A build from before this, used as `--old`, still reads the real cursor and the real clipboard: the runner tells it not
to move the cursor and blanks the clipboard before its runs only. Checkpoints that depend on where the pointer is can
then differ against such a build; against any build since, they do not.

**Compared at each checkpoint:** `wire.jsonl`, `state.json`, `ui.txt`, `files.txt` and `steps.txt` exactly, and
`shot.png` within a tolerance (a pixel counts when a channel moves by more than 40, a picture differs when more than
0.2% of its pixels do: GPU rounding moves a few edge pixels by a step or two). Left out of the pictures: wall-clock
text, a die tumbling by physics, and with two PCs the top bar, whose layout moves with Godot's random peer number.
File hashes take out wall-clock times, private join tokens, pasted pieces' GUIDs and capture times, and hash a save's
packed turns by what they hold.

**Script language** (one step a line; headers `@quick`, `@instances host,join`, `@timeout <s>`; `host: <step>` or
`join: <step>` runs a step on one instance only):

| Step | What it does |
|---|---|
| `settle [quiet-frames] [cap-frames] [noimage=1]` | wait until nothing changes (ui, wire, nodes, files, picture) |
| `checkpoint <name>` | write wire, state, ui, shot, files and steps |
| `click`, `rclick`, `dclick`, `mclick`, `hover <sel> [mod=Ctrl+Shift] [timeout=frames]` | pointer at a control or point |
| `drag`, `rdrag`, `mdrag <sel> <sel> [steps=10] [mod=…]` | a drag between two places |
| `mousedown`, `mouseup`, `move <sel> [button=right]` | one edge of a press, or a move |
| `wheel <sel> <ticks>` | positive is towards you, down |
| `key Ctrl+Z`, `keydown W`, `keyup W`, `type "text"` | keys |
| `drop-file "{fixtures}/map.png"` | a file dropped on the window (`{fixtures}`, `{live}`, `{packages}` expand) |
| `table-click x,z` | a world point; any selector may be `world:x,z` or `world:x,y,z` |
| `menu "row"` | a row of the open popup menu |
| `expect <sel>`, `absent <sel>` | written to steps.txt |
| `sync <name>` | two instances wait for each other |
| `restart`, `quit`, `close`, `wait <frames>`, `note "text"` | |

Selectors: `"exact text"` (text, tooltip, placeholder, tab, list or tree row; falls back to contains), `~"part"`,
`"text"#2` (the second, in reading order), `"text"+30,0` (an offset), `@x,y` or `@50%,50%` (a window point).

## `tests/lock/converter_diff.py`: the converter's lock

```
python -I tests/lock/converter_diff.py --old <converter dir> --new <converter dir> [--quick] [--only <substring>]
    [--invariant] [--cache <dir>] [--skip N] [--take N] [--keep]
```

A converter dir is a `dotnet publish tools/Converter -c Release -r win-x64 --self-contained false -o <dir>` (or a
build output folder). Inputs are every Tabletop Simulator module in `Mods\Workshop` and every save under `Saves`, read
only; `--quick` takes the six smallest modules and the two smallest saves. Each build in turn gets the same empty work
folder, a fresh copy of the download cache, and runs plan, import, export-tts, export-pack-tts and check-scripts; the
two trees are then compared.

- `KTABLE_REPEATABLE` fixes the seed and the clock. The culture is the machine's (or invariant for both with
  `--invariant`): piece ids depend on it, so builds are never compared across cultures.
- No network: every download goes to a local stub that answers 404, so an asset in neither cache is missing for both
  builds the same way, without the minutes of retries a dead network costs.
- JSON is compared as values, text by lines, tile images by decoded pixels, pictures drawn from text by pixels with a
  small allowance (one run now and then draws a glyph's edge pixel a little lighter), everything else by SHA-256.
  Lines whose order depends on threads (download progress) are compared as sets; the report's stamp and the download
  index's "missing" entries, which depend on thread timing, are left out.
- Exit code 1 when any input differs, 2 when the run could not be done. A difference's two files are kept in
  `%TEMP%\ktable-lock\findings`.

## `tests/lock/app/make_seed.py`

Builds `tests/lock/app/seed`, the packages root every lock run starts from: a few kinds copied from the TTS pack in
`%LOCALAPPDATA%\KriegsspielTable\packages` (read only), one pack written here (counters, text lines, states, a back,
an attachment, a script with a button, an input, a menu row, a hotkey, a table-menu row and a dialog), two scenarios, the joining PC's own pack, and the
fixtures the scripts drop or import (a map picture, a unit cube as obj, stl, ply and glb, a studio picture). The
result is the same bytes every time.

## `tools/make-dice.py`

```
python tools/make-dice.py [--preview DIR]
```

Writes the standard dice pack (`src/App/standard/std.dice/`): d4, d6, d8, d10, d12 and d20 as real polyhedra, each a
model piece whose states are its faces (state k-1 shows face k up), and the tool strip's dice icon
(`src/App/art/dice.png`), both pictured by rendering the real models. `--preview` writes the thumbnails and the icon
into DIR. Pillow.

- The atlas is exactly the 1,024 px the table loads at most, so it is never resampled on the way in. The chamfers
  share a whole spare row of their colour: a lone texel turns muddy in the small mipmaps.
- Numbers sit square to their face along one of its mirror axes, whichever is nearest up once the table has stood the
  face up (the way it is read); opposite faces add up to sides + 1. A d8 has no bar under its 6.
- Faces carry no painted light (the table lights the real bevels), only the ambient shadow at their rim and a body
  colour a hair off each other's; a painted blotch was tried and could not be seen at table size.

## `tools/make-icon.py`

```
python tools/make-icon.py
```

The app icon: a square of the 1889 USGS Fredericksburg sheet (`tools/icon-map.jpg`, cut (620,2310)-(1460,3150) from
`VA_Fredericksburg_188940_1889_125000_geo.tif`, public domain) with a red arrow across the Rappahannock drawn as a
whiteboard marker draws it. Writes `src/App/icon.png` (256) and `src/App/icon.ico` (16 to 256). Pillow and numpy.

## `tools/effects_extract.py`

```
python -I tools/effects_extract.py [--packages <root>] [--cache <tts cache>]
```

Run after `Converter pack iks.james-effects`. Takes the sprite textures out of the effect pack's Unity asset bundles
(parsed with UnityPy, never executed) into `sprite` assets, makes each effect's largest texture its image and every
sprite a state, and writes `effects.json` for rebuilding them as particle systems.

## `tools/make-stress-tables.py`

```
python tools/make-stress-tables.py <a scenario.json to take a piece from> [sizes...]
```

Installs stress-test tables into `%LOCALAPPDATA%\KriegsspielTable\packages` (or `KSTABLE_PACKAGES`): for each size
(10,000, 25,000 and 50,000 by default) one table of the same scripted block, which the renderer can batch, and one spread over every def of the
pack with tints and sizes, as a real board looks. They are ordinary scenario folders; delete one to be rid of it.
