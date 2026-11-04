# How Kriegsspiel Table is built

For people who want to change the code. The player and umpire guides are elsewhere in `docs/`.

## The projects

| Project | What it is | Depends on |
|---|---|---|
| `src/Core` | The game's data and rules: the table (`GameState`), every change to it (`Command`), how a change applies (`StateMachine.Apply`), what each role may see (`Visibility`), packs and scenarios, saves (`SaveStore`, `SaveLog`, `Json`). No engine, no network, no files beyond saves and packs. | — |
| `src/Server` | The authoritative game (`GameServer`): who is at the table, who may do what, the turn record, undo, and the Lua scripts (`Scripting/`). Transport-agnostic: it takes joins and commands and returns the messages to send. | Core |
| `src/Client` | The client side that needs no engine: the session and its undo stack, the host's save policy and co-umpire mirrors, asset waits, file transfers over the asset channel, message framing. | Core, Server |
| `src/App` | The Godot 4 game: the 3D table, the interface, the piece studio, the network transport (ENet), the title screen. | Core, Server, Client |
| `tools/Converter` | Tabletop Simulator modules and saves into packs and scenarios, and back; the scripting API reference. A separate program the app runs. | Core, Server |

`dotnet build KriegsspielTable.sln` builds everything except the app, which Godot builds itself:
`dotnet build src/App`. `tools/check.ps1` builds all of it, runs every test and checks the size and hygiene rules.

## One path for every change

Nothing changes the table except a `Command`. A click on the table, a script, an undo and a restored turn all
take the same path:

1. The client makes a command and sends it to the host (`Net`, the host's own table included).
2. `GameServer.Handle` checks it: the sender's role (`Command.SentBy`), umpire zones (`ZonePieces`, `ZonePoints`),
   numbers that could not be saved. It fills what only the server decides (ids, dice).
3. `StateMachine.Apply` changes the state and returns the command that undoes it. A batch applies whole or not
   at all.
4. The server logs it, keeps the inverse for undo, and sends each peer the copy `Visibility.ForPeer` allows.
5. Scripts react (`ScriptHost.AfterApplied`); what they do is one more command, applied the same way.
6. Every client applies what it receives with the same `StateMachine.Apply`, so all tables stay in step.

The host saves the whole table in a background thread (written to a temporary file, flushed, then renamed),
keeps a log of commands beside it between saves, and copies a checkpoint at every ended turn. A co-umpire keeps a
mirror of the game they can host from.

## Who may see what

`Visibility` is the one place that decides what a non-umpire receives, for snapshots (`Filter`) and for each
command (`ForPeer`). A new role is a new case there and nowhere else.

## Adding a command

1. A class in `src/Core/Protocol/Commands.cs`, registered with `[JsonDerivedType]`.
2. Its answers: `SentBy`, `OnUndoStack`, `ShownToObservers`, `ZonePieces`, `ZonePoints`, `ScriptsToReload`. The
   compiler makes you answer each one.
3. Its case in `StateMachine.Apply`, returning its inverse. A test fails for any registered command without one.
4. Run the tests. The command-answer snapshots (`src/Server.Tests/Snapshots/`) show the new lines; review them
   and commit the updated files. If the command or any saved type changed shape, the saved-shape test fails
   until `SaveFile.CurrentFormat` is bumped and a fixture of the new format is added under
   `src/Core.Tests/Fixtures/formats/`.

## Scripts

Lua 5.2 through MoonSharp, on the host only. Each scripted piece has its own environment, plus one global
script. A call gets a time budget, a nesting limit and a coroutine limit (`ScriptGuards`); a script that breaks a
limit stops with an error in the console, and the game goes on. Scripts never get the network, files, processes
or code from outside their own pack (`ScriptSafety` also judges every script that comes from outside). The API is
written once in `Scripting/ApiDoc.cs`; the reference in `docs/scripting-api.md` is generated from it and a test
keeps the two in agreement.

## The app

- `Main` builds the title screen (`scripts/title/`) and, once a game opens, the table.
- `Table` coordinates the parts of the 3D table, each owning its own state: camera, pieces, sheets, drawings,
  selection, carrying and dropping, dice, the gizmo, cropping, zones and pointers, sighting, the ruler.
- `Hud` coordinates the interface parts: top bar, tool strip, panels, the drawer, menus and dialogs.
- `PieceStudio` and `ModelStudio` are the piece studio (3D, 2D and Try it views), with the same pattern.
- `Net` is the transport only. What it does with the game goes through `src/Client`.

**Threads.** Godot nodes are touched on the main thread only. Work that takes time (decoding pictures, slicing
maps, writing saves, reading title-screen cards) runs on a worker and hands its result back through a queue
the main thread drains each frame, or through `CallDeferred`.

**Files at the table.** A joiner gets each pack's piece list from the host, then every picture, model and map
tile as something needs to draw it, plus, in the background, every look of the pieces it can see. Only those:
pieces hidden from a player are never fetched ahead, so the downloads give nothing away. Before asking the host,
the joiner looks in its own installed packs for a file of the same name (names are content hashes, checked
before use) and for a map with the same tile list. The host sends file pieces as raw bytes on their own channel,
urgent ones first, each player in turn, and each player's pieces are acknowledged: `Pacer` keeps about a quarter
of a second of them on the way, so a move never waits long behind a download. A piece that arrives damaged is
thrown away and fetched once more.

**Steam** is behind a platform interface (`scripts/platform/`) and is in a build only with
`-p:KTableSteam=true` (or `tools/export-windows.ps1 -Steam`). The ordinary build has no Steam code or library
and runs fully without it.

**Development aids** live in `src/App/dev/`: command-line flags that drive the app for screenshots and checks
(`--demo-*`, `--shot`), the driver for the behaviour checks (`--lock`), and the node tests (`dev/tests/`: the
table's parts on a real table and an in-process game, run headless by `tools/check.ps1`). None of it does anything
unless its flag is given or its scene is run, and none of it is in a release export (`KTableDev`).

The app reads the pointer through `Pointer` and the clipboard through `AppClipboard`. In use they are the real
cursor and the system clipboard; the aids drive the first (`Pointer.Drive`) and keep the second in the app, so a
scripted run never moves the person's cursor or touches their clipboard. The tools and the lock are described in
[tools.md](tools.md).

## Rules the check enforces

- No class over 1,500 lines (partial classes counted together), no method over 100 lines, no line over 200 characters.
- One member declaration per line, one statement per line, and a block written on one line holds at most two
  statements; a third puts the block on its own lines. `python -I tools/tidy.py` splits and wraps what these
  rules flag and leaves every other line as it is (`--check` only lists the files it would change).
- Every catch is narrow (`catch (IOException)`, or a `when` filter), or it names the exception and does something
  with it: reports it, keeps it, or rethrows. Nothing is swallowed in silence; a step that may fail without
  consequence goes through `BestEffort`, which records what it swallowed.
- The app's own code keeps its line coverage under the node and screen tests at or above the floor in
  `tools/covcheck.py`: `check.ps1` runs them under `dotnet-coverage` (`dotnet tool restore` installs it) and fails
  below it. A change to the app brings tests for what it touches; when coverage rises, the floor rises with it.
- No comments in any code: C#, Python, PowerShell, MSBuild files, shaders, Lua and the lock scripts. Names and
  structure say what the code does; a reason the code cannot show goes in these docs.
- Warnings are errors in every project (`Directory.Build.props`); nullable reference types are on everywhere.
- Beyond the default analysers, `.editorconfig` makes these warnings: a ValueTask awaited once only (CA2012), a
  CancellationToken passed on (CA2016) and taken last (CA1068), a result not thrown away (CA1806), a rethrow that keeps
  its origin (CA2200). Dispose-before-scope (CA2000) stays off: Godot objects are IDisposable and the scene tree or
  reference counting owns them. Unused usings are pointed out in the editor only.

## Repeatable runs

`KTABLE_REPEATABLE=seed=<n>,clock=<iso>` makes dice, ids, GUIDs and timestamps repeat from run to run
(`Core/Repeatable.cs`), and `KTABLE_USER_DIR` moves every user folder into a scratch folder. Unset, both change
nothing. Together they let two builds be run on the same input and compared.
