param(
    [string]$Target = (Join-Path $env:LOCALAPPDATA "Programs\Kriegsspiel Table Sandbox"),
    [switch]$Clean
)
$DataName = "KriegsspielTable Sandbox"
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$app = Join-Path $Target "app"

function Find-GodotFolder {
    $dir = Get-ChildItem (Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Packages") -Directory -Filter "GodotEngine.GodotEngine.Mono*" |
        ForEach-Object { Get-ChildItem $_.FullName -Directory -Filter "Godot_v*_mono_win64" } | Sort-Object Name -Descending | Select-Object -First 1
    if (-not $dir) { throw "Godot .NET not found (winget install GodotEngine.GodotEngine.Mono)" }
    $dir
}

function Copy-Tree($from, $to, [string[]]$extra = @()) {
    robocopy $from $to @extra /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "copying $from failed ($LASTEXITCODE)" }
}

function Build-OptimizedDebugApp {
    dotnet build (Join-Path $repo "src\App\KriegsspielTable.csproj") -c Debug -p:Optimize=true --no-incremental --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "app build failed" }
}

function Publish-ConverterIntoEmptyFolder {
    Remove-Item -Recurse -Force (Join-Path $Target "converter") -ErrorAction SilentlyContinue
    dotnet publish (Join-Path $repo "tools\Converter\Converter.csproj") -c Release -o (Join-Path $Target "converter") --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "converter publish failed" }
}

function Import-NewAssets($godotDir) {
    $console = Get-ChildItem $godotDir.FullName -Filter "Godot_v*_mono_win64_console.exe" | Select-Object -First 1
    & $console.FullName --headless --path (Join-Path $repo "src\App") --import *> $null
}

function Copy-Game {
    Copy-Tree (Join-Path $repo "src\App") $app @("/XD", (Join-Path $repo "src\App\.godot\mono\temp\obj"), "/XF", "*.user")
    Copy-Tree (Join-Path $repo "docs") (Join-Path $Target "docs") @("*.md", "/LEV:1", "/XF", "steam-release.md")
    Copy-Item (Join-Path $repo "LICENSE") (Join-Path $Target "LICENSE.txt") -Force
    Copy-Item (Join-Path $repo "THIRD-PARTY-NOTICES.md") $Target -Force
}

function Set-SandboxIdentity {
    $proj = Join-Path $app "project.godot"
    $text = Get-Content $proj -Raw
    if ($text -notmatch "custom_user_dir_name") {
        $text = $text -replace '(\[application\]\r?\n)', "`$1`r`nconfig/use_custom_user_dir=true`r`nconfig/custom_user_dir_name=`"KriegsspielTableSandbox`"`r`n"
    }
    if ($text -notmatch "data_dir_name") {
        $text = $text.TrimEnd() + "`r`n`r`n[ktable]`r`n`r`ndata_dir_name=`"$DataName`"`r`n"
    }
    $withoutBom = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText($proj, $text, $withoutBom)
    $convCfg = Join-Path $Target "converter\converter.json"
    $sandboxCache = [IO.File]::ReadAllText($convCfg) -replace '%LOCALAPPDATA%\\\\KriegsspielTable\\\\cache', "%LOCALAPPDATA%\\$DataName\\cache"
    [IO.File]::WriteAllText($convCfg, $sandboxCache)
}

function Clear-SandboxData {
    $shellDocuments = [Environment]::GetFolderPath("MyDocuments")
    foreach ($d in @((Join-Path $env:LOCALAPPDATA $DataName), (Join-Path $shellDocuments $DataName), (Join-Path $env:USERPROFILE "Documents\$DataName"), (Join-Path $env:APPDATA "KriegsspielTableSandbox"))) {
        if (Test-Path $d) { Remove-Item $d -Recurse -Force; Write-Host "Cleaned  $d" }
    }
}

function Write-Stamp {
    $commit = (git -C $repo rev-parse --short HEAD) 2>$null
    $dirty = if ((git -C $repo status --porcelain) 2>$null) { "+local changes" } else { "" }
    [IO.File]::WriteAllText((Join-Path $app "BUILD.txt"), "sandbox build $commit$dirty - $(Get-Date -Format 'd MMM yyyy HH:mm')")
}

function Install-IconUnderItsHash {
    $iconSrc = Join-Path $app "icon.ico"
    $iconHash = (Get-FileHash $iconSrc -Algorithm SHA256).Hash.Substring(0, 8).ToLower()
    $iconDir = Join-Path $Target "icons"
    New-Item -ItemType Directory -Force $iconDir | Out-Null
    Get-ChildItem $iconDir -Filter "*.ico" | Where-Object { $_.Name -ne "kriegsspiel-table-$iconHash.ico" } | Remove-Item -Force
    $iconFile = Join-Path $iconDir "kriegsspiel-table-$iconHash.ico"
    Copy-Item $iconSrc $iconFile -Force
    $iconFile
}

function Write-Shortcuts($godotDir) {
    $editor = Get-ChildItem $godotDir.FullName -Filter "Godot_v*_mono_win64.exe" | Where-Object { $_.Name -notlike "*console*" } | Select-Object -First 1
    $iconFile = Install-IconUnderItsHash
    $engineExe = Join-Path $Target ("engine\" + $editor.Name)
    $shell = New-Object -ComObject WScript.Shell
    foreach ($dir in @([Environment]::GetFolderPath("Desktop"), (Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"))) {
        $lnkPath = Join-Path $dir "Kriegsspiel Table (Sandbox).lnk"
        if (Test-Path $lnkPath) { Remove-Item $lnkPath -Force }
        $lnk = $shell.CreateShortcut($lnkPath)
        $lnk.TargetPath = $engineExe
        $lnk.Arguments = "--path `"$app`""
        $lnk.WorkingDirectory = $Target
        $lnk.Description = "Kriegsspiel Table - sandbox build"
        $lnk.IconLocation = "$iconFile,0"
        $lnk.Save()
    }
    $refreshShellIcons = "$env:WINDIR\System32\ie4uinit.exe"
    try { Start-Process -FilePath $refreshShellIcons -ArgumentList "-show" -WindowStyle Hidden -Wait } catch { Write-Host "Shortcut icons may show the old picture until the next sign-in ($($_.Exception.Message))" }
    Set-Content (Join-Path $Target "Kriegsspiel Table.cmd") "@start `"`" `"%~dp0engine\$($editor.Name)`" --path `"%~dp0app`"" -Encoding ascii
}

$godotDir = Find-GodotFolder
Write-Host "Source : $repo"
Write-Host "Sandbox: $Target"
Build-OptimizedDebugApp
Publish-ConverterIntoEmptyFolder
Import-NewAssets $godotDir
Copy-Game
Copy-Tree $godotDir.FullName (Join-Path $Target "engine")
Set-SandboxIdentity
if ($Clean) { Clear-SandboxData }
Write-Stamp
Write-Shortcuts $godotDir
$size = [math]::Round(((Get-ChildItem $Target -Recurse -File | Measure-Object Length -Sum).Sum / 1MB))
Write-Host "Sandbox ready ($size MB): $(Get-Content (Join-Path $app 'BUILD.txt'))"
