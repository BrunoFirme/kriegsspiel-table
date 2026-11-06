# Kriegsspiel Table

A table for Kriegsspiel practitioners, umpires and players alike: the map, the blocks, dice, a ruler and a
board shared live by up to eight umpires and thirty-two observers, or kept to yourself. It replaces a
general-purpose digital tabletop with one built for Kriegsspiel.

- **The table:** tiled map sheets from PNG, JPG, TIFF or GIMP files, pieces that rest, stack and carry what is
  on them, drawing boards, range rings, a line-of-sight picture tool, turns and a game clock.
- **The game is never lost:** it saves itself continuously, keeps every ended turn to look back at or return
  to, and leaves a copy with each co-umpire that they can host from.
- **Digital tabletop modules come across:** a converter brings in their maps, pieces and Lua scripts.
- **Pieces of your own:** a piece studio for faces, counters, text and 3D models; packs share as `.ktpack` files.

The app is an early beta and is played by one group so far. Windows is the platform it is built and
tested on.

## Getting it

- **Free, from [Releases](https://github.com/BrunoFirme/kriegsspiel-table/releases), for good.** The installer or
  the zip: the whole game, updating itself from here. The source stays here too.
- **On Steam, coming soon: US$10 / €10, at most R$20 in Brazil.** The same game, plus what Steam adds:
  - **Workshop:** publish packs and scenarios; subscribed ones install themselves.
  - **Steam Cloud:** hosted games and settings follow you to another PC.
  - **Joining through Steam:** a `steam:` join code, Join from the friends list or an overlay invite, with no port
    to open and the host's address hidden.
  - **Updates through Steam.**

Both play together: players on either one join the same table. Buying it on Steam is how to support the work.

## Found a problem?

[Open an issue](https://github.com/BrunoFirme/kriegsspiel-table/issues/new/choose). At the table, the menu's
**Copy diagnostics** puts the version, your PC, the latest errors and the last commands on the clipboard: paste
them into the issue.

## Building

Needs the .NET 10 SDK and Godot 4.7 with .NET (`winget install GodotEngine.GodotEngine.Mono`).

| | |
|---|---|
| `dotnet build KriegsspielTable.sln` | the libraries, the converter and their tests |
| `dotnet build src/App` | the app (Godot builds and runs it; open `src/App` in the Godot editor to run it there) |
| `powershell -File tools\check.ps1` | everything a change must pass: both builds, every test, the app's node tests, the size and hygiene rules |
| `powershell -File tools\export-windows.ps1 -Zip` | a release folder that runs without Godot or .NET installed |
| `powershell -File tools\export-windows.ps1 -Installer -VersionName "0.3 beta"` | the same folder plus a Windows installer (needs Inno Setup 6: `winget install JRSoftware.InnoSetup`) |

Steam (the Workshop) is left out of every build unless asked for: `-p:KTableSteam=true`, or `-Steam` on the
export. Without it the app has no Steam code and needs no Steam client.

## Finding your way

- [`docs/architecture.md`](docs/architecture.md): how the code is put together, and how to add a command.
- [`docs/umpire-guide.md`](docs/umpire-guide.md), [`docs/player-guide.md`](docs/player-guide.md),
  [`docs/community-guide.md`](docs/community-guide.md): using it.
- [`docs/scripting.md`](docs/scripting.md) and [`docs/scripting-api.md`](docs/scripting-api.md): piece scripts.

## License and credits

Made by **Bruno Firme**. The source is published under the [Kriegsspiel Table License](LICENSE): the PolyForm
Noncommercial License 1.0.0 with a few plain terms on top. In short:

- **Free for everyone who plays, teaches or studies.** Playing, umpiring, hosting, teaching, researching, running
  events (paid or not), streaming or recording games with it costs nothing, for people, clubs, shops, venues,
  schools, universities and researchers. Change it and share it free of charge, passing on the `Required Notice`
  line in `LICENSE`, which is the credit.
- **Share what you make, free.** Packs, pieces and scenarios made for it can be shared freely; selling them needs a
  commercial license. Your own rulebooks, maps and pictures stay yours to sell anywhere.
- **A commercial license is needed** to sell the software or charge for access to it; to sell packs, pieces or
  scenarios for it; for a company run for profit to use it in its work (a wargaming consultancy, a company training
  its staff, a publisher); and for defence and national-security use of any country, including contractors and
  research done for them. Soldiers and employees playing in their own time are hobbyists like anyone else.

`LICENSE` is the text that counts; this is only a summary of it.

For a commercial license, write to Bruno Firme at firma.eht@outlook.com (or on Discord: b.firme).

Contributions are welcome on the terms in [CONTRIBUTING.md](CONTRIBUTING.md).

The engine and libraries it is built on are listed, with their licenses, in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
