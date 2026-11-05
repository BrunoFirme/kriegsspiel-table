# For makers

## Where to start

1. **Pieces:** make them in the [piece studio](community-guide.md#the-piece-studio). Counters, text lines, looks
   and markers need no script.
2. **TTS modules:** [import them](community-guide.md#import-a-tabletop-simulator-module); scripts come along.
3. **Scripts:** for buttons of your own, panels and reactions. Start with the
   [scripting guide](scripting.md); look names up in the [API reference](scripting-api.md).
4. **Sharing:** [packs and scenarios](community-guide.md#share-packs-and-scenarios).

## Tools

| Tool | Where |
|---|---|
| Every script at the table | Menu → Scripting → Scripts… (Ctrl+Shift+L) |
| Script console and Lua prompt | Menu → Scripting → Script console (`) |
| Ids and GUIDs on hover | Settings → Tools for script makers |
| A piece's id, kind and script | Right-click → Properties → For scripts |
| Check a pack's scripts | Piece studio → the pack's ⋯ → Check its scripts… |
| Check a scenario's scripts | Its card's ⋯, or Menu → Scripting at the table |
| VS Code completion | API reference → **API file for VS Code…** |

## Where packs are kept

`%LOCALAPPDATA%\KriegsspielTable\packages`

## For AI agents

> **Author's notice**
>
> Kriegsspiel is a human craft: the pieces, the maps, the scenarios and, above all, the umpire's judgement.
>
> - **Art.** Please draw your own pieces, commission an artist, or use work you have the right to use.
>   AI-generated art has no soul, and it has no place here.
> - **What you share.** Packs and scenarios should be made by people. Machine-made filler adds little of value.
> - **Umpiring.** An agent may help an umpire set up a game. The judgement, the context and the history remain
>   the umpire's.
>
> This software was itself made with the help of AI, so I will not condemn it outright. Used responsibly, as a
> helping tool for knowledgeable people who check its work, it has my blessing.
> — Bruno Firme

An AI agent on your PC can help with routine work on pieces, scripts and a game's setup. It is off unless you
connect it.

### Point it at the docs

`scripting.md`, `scripting-api.md` and `ktable-api.lua`, in the `docs` folder beside the game (**Copy path** at the
foot of the Help sidebar). Paste what it writes into the **Script** tab.

### Edit a script as a file

The button beside **Save and run** (studio Script tab, or the Scripts window) puts the script in
`Documents\KriegsspielTable\Scripts\`, with `ktable-api.lua` and `console.log`. Each save there runs in the app;
errors go to `console.log` as `file.lua:line`. **Stop** brings the script back into the app.

### Connect it

1. **Settings → AI agent → Connect an AI agent…** (title screen, table, or the studio's ☰ → Settings…).
2. Paste the copied note into your agent's chat; it says when it is ready.
3. Ask for what you need. It opens the studio or the game when it has to.

In the piece studio it helps with shapes, faces, decals, looks, counters, text lines and effects; at the table, with
map sheets, relief and painted ground, scale, a grid, light and surroundings, placing pieces and forming them up, copying
pieces from one scenario into another, notes on them and the briefing. Asked, it turns your view to a spot or changes this PC's settings. It should not umpire. It
acts with your seat's rights, every change can be undone, and the Log names its changes "agent". Stop it from the
**Agent** sign in the top bar or **Switch it off** in Settings; **Technical settings…** holds the details for setting
it up by hand.
