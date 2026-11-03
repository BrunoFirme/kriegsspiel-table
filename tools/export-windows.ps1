param(
    [string]$Out = (Join-Path (Split-Path -Parent $PSScriptRoot) "build\win64"),
    [switch]$Zip,
    [switch]$Steam
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$guidesThatShip = "README.md", "start.md", "community-guide.md", "makers.md", "player-guide.md", "scripting-api.md", "scripting.md", "umpire-guide.md", "ktable-api.lua"

function Find-Godot {
    $dir = Get-ChildItem (Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Packages") -Directory -Filter "GodotEngine.GodotEngine.Mono*" |
        ForEach-Object { Get-ChildItem $_.FullName -Directory -Filter "Godot_v*_mono_win64" } | Sort-Object Name -Descending | Select-Object -First 1
    if (-not $dir) { throw "Godot .NET not found (winget install GodotEngine.GodotEngine.Mono)" }
    $exe = Get-ChildItem $dir.FullName -Filter "Godot_v*_mono_win64_console.exe" | Select-Object -First 1
    if (-not $exe) { $exe = Get-ChildItem $dir.FullName -Filter "Godot_v*_mono_win64.exe" | Select-Object -First 1 }
    $version = ($dir.Name -replace '^Godot_v([0-9.]+)-(\w+)_mono_win64$', '$1.$2')
    $templates = Join-Path $env:APPDATA "Godot\export_templates\$version.mono"
    if (-not (Test-Path (Join-Path $templates "windows_release_x86_64.exe"))) { throw "export templates missing: $templates (unpack the $version .NET .tpz there)" }
    $exe
}

function Copy-Tree($from, $to, [string[]]$extra = @()) {
    robocopy $from $to @extra /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "copying $from failed ($LASTEXITCODE)" }
}

function Export-Game($godot, $dist) {
    $env:KTableSteam = if ($Steam) { "true" } else { "false" }
    $exe = Join-Path $dist "KriegsspielTable.exe"
    & $godot.FullName --headless --path (Join-Path $repo "src\App") --export-release "Windows Desktop" $exe
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exe)) { throw "export failed ($LASTEXITCODE)" }
    if (-not (Get-ChildItem $dist -Directory -Filter "data_*")) { throw "export produced no data_* folder: the .NET build did not run (is dotnet/project/solution_directory in project.godot pointing at the repo root?)" }
}

function Copy-FilesBesideTheExe($dist) {
    Copy-Tree (Join-Path $repo "src\App\standard") (Join-Path $dist "standard") @("/XF", ".gdignore")
    if ($Steam) { Copy-Tree (Join-Path $repo "src\App\native") (Join-Path $dist "native") }
    Copy-Tree (Join-Path $repo "docs") (Join-Path $dist "docs") ($guidesThatShip + "/LEV:1")
    Copy-Item (Join-Path $repo "src\App\icon.png") $dist -Force
    Copy-Item (Join-Path $repo "src\App\icon.ico") $dist -Force
    Copy-Item (Join-Path $repo "LICENSE") (Join-Path $dist "LICENSE.txt") -Force
    Copy-Item (Join-Path $repo "THIRD-PARTY-NOTICES.md") $dist -Force
}

function Publish-SelfContainedConverter($dist) {
    dotnet publish (Join-Path $repo "tools\Converter\Converter.csproj") -c Release -r win-x64 --self-contained true -o (Join-Path $dist "converter") --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "converter publish failed" }
    $convCfg = Join-Path $dist "converter\converter.json"
    $lookForThePlayersOwnTtsFolder = [IO.File]::ReadAllText($convCfg) -replace '"ttsModsPath":\s*"[^"]*"', '"ttsModsPath": ""'
    [IO.File]::WriteAllText($convCfg, $lookForThePlayersOwnTtsFolder)
}

function Write-Stamp($dist, $commit) {
    $dirty = if ((git -C $repo status --porcelain) 2>$null) { "+local changes" } else { "" }
    $stamp = "release build $commit$dirty - $(Get-Date -Format 'd MMM yyyy HH:mm')"
    [IO.File]::WriteAllText((Join-Path $dist "BUILD.txt"), $stamp)
    $stamp
}

function Write-Readme($dist, $stamp) {
    [IO.File]::WriteAllText((Join-Path $dist "README.txt"), @"
Kriegsspiel Table - $stamp

Run KriegsspielTable.exe. Nothing to install: the game, the .NET runtime and the Tabletop Simulator
converter are all in this folder. Keep the folder together (data_*, standard, native, converter).

Your packs, scenarios and settings live in %LOCALAPPDATA%\KriegsspielTable, not here, so a new
version can simply replace this folder.

Playing over the internet: the umpire hosts (Host a game, with a password or none), then Menu >
Invite... shows the password and the addresses to send. The app asks the router to open the UDP port;
if Invite says it could not, forward that port by hand. Players: Join a game > address, port,
password and name.

The guides are in docs\ beside this file: the umpire's guide, the player's, the community guide and
the scripting reference for pieces that carry their own Lua. Menu > Guides and scripting reference
opens that folder from inside the game.

Made by Bruno Firme. The license is in LICENSE.txt: free for playing, umpiring, teaching, research
and events, and for sharing what you make free of charge. Selling the software, selling packs,
pieces or scenarios for it, use in a company's work, and defence use need a commercial license
(ask Bruno Firme: firma.eht@outlook.com, or Discord b.firme). The engine and libraries it is built
on, and their licenses, are in THIRD-PARTY-NOTICES.md.
"@)
}

function Write-Zip($dist, $commit) {
    $zipPath = Join-Path $Out "KriegsspielTable-win64-$commit.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path $dist -DestinationPath $zipPath
    Write-Host "Zip    : $zipPath ($([math]::Round((Get-Item $zipPath).Length / 1MB)) MB)"
}

$godot = Find-Godot
$dist = Join-Path $Out "Kriegsspiel Table"
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force $dist | Out-Null
Write-Host "Source : $repo"
Write-Host "Output : $dist"

Export-Game $godot $dist
Copy-FilesBesideTheExe $dist
Publish-SelfContainedConverter $dist
$commit = (git -C $repo rev-parse --short HEAD) 2>$null
$stamp = Write-Stamp $dist $commit
Write-Readme $dist $stamp
if ($Zip) { Write-Zip $dist $commit }
$size = [math]::Round(((Get-ChildItem $dist -Recurse -File | Measure-Object Length -Sum).Sum / 1MB))
Write-Host "Export ready ($size MB): $stamp"
