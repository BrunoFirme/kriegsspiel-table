param(
    [string]$Out,
    [switch]$Zip,
    [switch]$Installer,
    [string]$VersionName,
    [switch]$Steam,
    [switch]$Linux
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if ($Linux -and ($Installer -or $Steam)) { throw "-Linux builds the plain archive only: no -Installer, no -Steam" }
if (-not $Out) { $Out = Join-Path $repo $(if ($Linux) { "build\linux64" } else { "build\win64" }) }
$target = if ($Linux) {
    @{ Preset = "Linux"; Exe = "KriegsspielTable.x86_64"; Template = "linux_release.x86_64"; Runtime = "linux-x64" }
} else {
    @{ Preset = "Windows Desktop"; Exe = "KriegsspielTable.exe"; Template = "windows_release_x86_64.exe"; Runtime = "win-x64" }
}
$guidesThatShip = "README.md", "start.md", "community-guide.md", "makers.md", "player-guide.md", "scripting-api.md", "scripting.md", "umpire-guide.md", "ktable-api.lua"

function Find-Godot {
    $dir = Get-ChildItem (Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Packages") -Directory -Filter "GodotEngine.GodotEngine.Mono*" |
        ForEach-Object { Get-ChildItem $_.FullName -Directory -Filter "Godot_v*_mono_win64" } | Sort-Object Name -Descending | Select-Object -First 1
    if (-not $dir) { throw "Godot .NET not found (winget install GodotEngine.GodotEngine.Mono)" }
    $exe = Get-ChildItem $dir.FullName -Filter "Godot_v*_mono_win64_console.exe" | Select-Object -First 1
    if (-not $exe) { $exe = Get-ChildItem $dir.FullName -Filter "Godot_v*_mono_win64.exe" | Select-Object -First 1 }
    $version = ($dir.Name -replace '^Godot_v([0-9.]+)-(\w+)_mono_win64$', '$1.$2')
    $templates = Join-Path $env:APPDATA "Godot\export_templates\$version.mono"
    if (-not (Test-Path (Join-Path $templates $target.Template))) { throw "export templates missing: $templates (unpack the $version .NET .tpz there)" }
    $exe
}

function Copy-Tree($from, $to, [string[]]$extra = @()) {
    robocopy $from $to @extra /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "copying $from failed ($LASTEXITCODE)" }
}

function Export-Game($godot, $dist) {
    $env:KTableSteam = if ($Steam) { "true" } else { "false" }
    $exe = Join-Path $dist $target.Exe
    & $godot.FullName --headless --path (Join-Path $repo "src\App") --export-release $target.Preset $exe
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exe)) { throw "export failed ($LASTEXITCODE)" }
    if (-not (Get-ChildItem $dist -Directory -Filter "data_*")) { throw "export produced no data_* folder: the .NET build did not run (is dotnet/project/solution_directory in project.godot pointing at the repo root?)" }
}

function Copy-FilesBesideTheExe($dist) {
    Copy-Tree (Join-Path $repo "src\App\standard") (Join-Path $dist "standard") @("/XF", ".gdignore")
    Copy-Tree (Join-Path $repo "src\App\backdrops") (Join-Path $dist "backdrops") @("/XF", ".gdignore")
    if ($Steam) { Copy-Tree (Join-Path $repo "src\App\native") (Join-Path $dist "native") }
    Copy-Tree (Join-Path $repo "docs") (Join-Path $dist "docs") ($guidesThatShip + "/LEV:1")
    Copy-Item (Join-Path $repo "src\App\icon.png") $dist -Force
    if ($Linux) { Copy-Item (Join-Path $PSScriptRoot "linux\install.sh") $dist -Force }
    else { Copy-Item (Join-Path $repo "src\App\icon.ico") $dist -Force }
    Copy-Item (Join-Path $repo "LICENSE") (Join-Path $dist "LICENSE.txt") -Force
    Copy-Item (Join-Path $repo "THIRD-PARTY-NOTICES.md") $dist -Force
}

function Publish-ConverterBesideTheGame($dist) {
    $data = (Get-ChildItem $dist -Directory -Filter "data_*" | Select-Object -First 1).FullName
    $published = Join-Path ([IO.Path]::GetTempPath()) ("ktable-converter-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
    try {
        dotnet publish (Join-Path $repo "tools\Converter\Converter.csproj") -c ExportRelease -r $target.Runtime --self-contained true -o $published --nologo -v q
        if ($LASTEXITCODE -ne 0) { throw "converter publish failed" }
        $differ = @()
        foreach ($file in Get-ChildItem $published -Recurse -File) {
            $relative = $file.FullName.Substring($published.Length + 1)
            $target = Join-Path $data $relative
            if (Test-Path $target) {
                if ((Get-FileHash $file.FullName).Hash -ne (Get-FileHash $target).Hash) { $differ += $relative }
                continue
            }
            New-Item -ItemType Directory -Force (Split-Path -Parent $target) | Out-Null
            Copy-Item $file.FullName $target
        }
        if ($differ.Count -gt 0) { throw "the converter and the game need different copies of: $($differ -join ', ')" }
    }
    finally { Remove-Item $published -Recurse -Force -ErrorAction SilentlyContinue }
    $convCfg = Join-Path $data "converter.json"
    $shipped = [IO.File]::ReadAllText($convCfg)
    if ($shipped -notmatch '"modsPath":\s*"[^"]*"') { throw "converter.json has no modsPath to empty" }
    $lookForThePlayersOwnModsFolder = $shipped -replace '"modsPath":\s*"[^"]*"', '"modsPath": ""'
    [IO.File]::WriteAllText($convCfg, $lookForThePlayersOwnModsFolder)
}

function Write-Stamp($dist, $commit, $name) {
    $dirty = if ((git -C $repo status --porcelain) 2>$null) { "+local changes" } else { "" }
    $stamp = "$name, release build $commit$dirty - $(Get-Date -Format 'd MMM yyyy HH:mm')"
    [IO.File]::WriteAllText((Join-Path $dist "BUILD.txt"), $stamp)
    $stamp
}

function Write-Readme($dist, $stamp) {
    $start = if ($Linux) { @"
Run ./install.sh to add Kriegsspiel Table to your applications menu (it copies this folder to
~/.local/opt/kriegsspiel-table; run it again from a newer version to update, or with --uninstall
to remove it). Or run ./KriegsspielTable.x86_64 straight from here. The game, the .NET runtime
and the module converter are all in this folder: keep it together (data_*, standard, backdrops).
Copying a picture or a file to the clipboard needs wl-copy (Wayland) or xclip (X11).

Your packs, scenarios, saves and settings live in ~/.local/share/KriegsspielTable, not here, so
a new version can simply replace this folder.
"@ } else { @"
Run KriegsspielTable.exe. Nothing to install: the game, the .NET runtime and the module
converter are all in this folder. Keep the folder together (data_*, standard, backdrops, native).

Your packs, scenarios and settings live in %LOCALAPPDATA%\KriegsspielTable, not here, so a new
version can simply replace this folder.
"@ }
    [IO.File]::WriteAllText((Join-Path $dist "README.txt"), @"
Kriegsspiel Table - $stamp

$start

Playing over the internet: the umpire hosts (Scenarios, Open, with a password or none), then Menu >
Invite... shows the password and the addresses to send. The app asks the router to open the UDP port;
if Invite says it could not, forward that port by hand. Players: Join a game > address, port,
password and name.

The guides are in docs$(if ($Linux) { "/" } else { "\" }) beside this file: the umpire's guide, the player's, the community guide and
the scripting reference for pieces that carry their own Lua. Menu > Help... reads them inside the game.

Found a problem? Open an issue at https://github.com/BrunoFirme/kriegsspiel-table/issues and paste
what Menu > Copy diagnostics puts on the clipboard.

Made by Bruno Firme. The license is in LICENSE.txt: free for playing, umpiring, teaching, research
and events, and for sharing what you make free of charge. Selling the software, selling packs,
pieces or scenarios for it, use in a company's work, and defence use need a commercial license
(ask Bruno Firme: firma.eht@outlook.com, or Discord b.firme). The engine and libraries it is built
on, and their licenses, are in THIRD-PARTY-NOTICES.md.
"@)
}

function Get-Version {
    $line = Select-String -Path (Join-Path $repo "src\App\project.godot") -Pattern '^config/version="([^"]+)"' | Select-Object -First 1
    if (-not $line) { throw "config/version missing from project.godot" }
    $line.Matches[0].Groups[1].Value
}

function Find-Iscc {
    $found = @((Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"), (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"), (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $found) { throw "Inno Setup 6 not found (winget install JRSoftware.InnoSetup)" }
    $found
}

function Write-Installer($dist, $version, $name) {
    $outName = "KriegsspielTable-$($name -replace '\s+', '-')-setup"
    & (Find-Iscc) /Q "/DDist=$dist" "/DVersion=$version" "/DVersionName=$name" "/DOutName=$outName" (Join-Path $PSScriptRoot "installer.iss")
    if ($LASTEXITCODE -ne 0) { throw "installer build failed ($LASTEXITCODE)" }
    $setup = Join-Path $Out "$outName.exe"
    Write-Host "Setup  : $setup ($([math]::Round((Get-Item $setup).Length / 1MB)) MB)"
}

function Write-Zip($dist, $commit) {
    $zipPath = Join-Path $Out "KriegsspielTable-win64-$commit.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path $dist -DestinationPath $zipPath
    Write-Host "Zip    : $zipPath ($([math]::Round((Get-Item $zipPath).Length / 1MB)) MB)"
}

function Write-Tarball($dist, $name) {
    $tarPath = Join-Path $Out "KriegsspielTable-$($name -replace '\s+', '-')-linux-x86_64.tar.gz"
    python -I (Join-Path $PSScriptRoot "linux\pack.py") $dist $tarPath "kriegsspiel-table"
    if ($LASTEXITCODE -ne 0) { throw "packing the Linux archive failed" }
    Write-Host "Archive: $tarPath ($([math]::Round((Get-Item $tarPath).Length / 1MB)) MB)"
}

$godot = Find-Godot
$dist = Join-Path $Out "Kriegsspiel Table"
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force $dist | Out-Null
Write-Host "Source : $repo"
Write-Host "Output : $dist"

Export-Game $godot $dist
Copy-FilesBesideTheExe $dist
Publish-ConverterBesideTheGame $dist
$commit = (git -C $repo rev-parse --short HEAD) 2>$null
$version = Get-Version
if (-not $VersionName) { $VersionName = $version }
$stamp = Write-Stamp $dist $commit $VersionName
Write-Readme $dist $stamp
if ($Zip -and $Linux) { Write-Tarball $dist $VersionName }
elseif ($Zip) { Write-Zip $dist $commit }
if ($Installer) { Write-Installer $dist $version $VersionName }
$size = [math]::Round(((Get-ChildItem $dist -Recurse -File | Measure-Object Length -Sum).Sum / 1MB))
Write-Host "Export ready ($size MB): $stamp"
