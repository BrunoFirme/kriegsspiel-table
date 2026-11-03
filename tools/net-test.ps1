param(
    [string[]]$Clients = @("good", "mobile"),
    [string]$Scenario,
    [string]$Packages = (Join-Path $env:LOCALAPPDATA "KriegsspielTable\packages"),
    [double]$HostUpKbps = 0,
    [int]$Port = 7790,
    [string]$Engine,
    [switch]$Keep
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "godot-runs.ps1")
$hostListensWithinSeconds = 180
$titleBarPixels = 32
$closeGentlyMs = 5000
$logInFolder = "APPDATA\Godot\app_userdata\Kriegsspiel Table\logs\godot.log"

function Quoted($text) { "`"$text`"" }

function Assert-Ran($what) { if ($LASTEXITCODE -ne 0) { throw $what } }

$console = Find-GodotConsole
if (-not $console) { throw "Godot .NET not found (winget install GodotEngine.GodotEngine.Mono)" }
$godot = if ($Engine) { $Engine } else { $console.FullName -replace "_console\.exe$", ".exe" }
$scenarios = @(Get-ChildItem (Join-Path $Packages "scenarios") -Directory -ErrorAction SilentlyContinue | Where-Object { Test-Path (Join-Path $_.FullName "scenario.json") } | ForEach-Object Name)
if (-not $Scenario -and $scenarios.Count -eq 1) { $Scenario = $scenarios[0] }
if (-not $Scenario -or $scenarios -notcontains $Scenario) { throw "choose a scenario with -Scenario, one of: $($scenarios -join ', ') (in $Packages\scenarios; -Packages picks another folder)" }
$app = Join-Path $repo "src\App"
dotnet build $app --nologo -v q; Assert-Ran "the app does not build"
dotnet build (Join-Path $repo "tools\NetRelay") --nologo -v q; Assert-Ran "the relay does not build"
$relayDll = (Get-ChildItem (Join-Path $repo "tools\NetRelay\bin\Debug") -Recurse -Filter "NetRelay.dll" | Sort-Object LastWriteTime | Select-Object -Last 1).FullName
$relayArgs = @("--to", "127.0.0.1:$Port", "--host-up", $HostUpKbps)
for ($i = 0; $i -lt $Clients.Count; $i++) { $relayArgs += "--lane", "$($Port + 1 + $i)=$($Clients[$i])" }
dotnet $relayDll @relayArgs --check; Assert-Ran "the relay cannot use those connections"
& $console.FullName --headless --path $app --import *> $null

Add-Type -AssemblyName System.Windows.Forms
$area = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$windows = $Clients.Count + 1
$columns = [Math]::Ceiling([Math]::Sqrt($windows))
$rows = [Math]::Ceiling($windows / $columns)
$width = [Math]::Floor($area.Width / $columns)
$height = [Math]::Floor($area.Height / $rows)

$run = Join-Path ([IO.Path]::GetTempPath()) ("ktable-nettest-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory $run | Out-Null
$hostPackages = Join-Path $run "host-packages"
New-Item -ItemType Directory (Join-Path $hostPackages "scenarios") | Out-Null
Copy-Item -Recurse (Join-Path $Packages "scenarios\$Scenario") (Join-Path $hostPackages "scenarios")
foreach ($pack in (Get-Content -Raw (Join-Path $Packages "scenarios\$Scenario\scenario.json") | ConvertFrom-Json).packs) {
    if (Test-Path (Join-Path $Packages $pack)) { Copy-Item -Recurse (Join-Path $Packages $pack) $hostPackages }
}
$started = @()
$failed = $false

function Start-Instance($name, $index, $userArgs) {
    $x = $area.X + ($index % $columns) * $width
    $y = $area.Y + [Math]::Floor($index / $columns) * $height + $titleBarPixels
    $godotArgs = @("--path", (Quoted $app), "--resolution", "$($width)x$($height - $titleBarPixels)", "--position", "$x,$y", "--") + $userArgs
    With-UserFoldersIn (Join-Path $run $name) { Start-Process $godot -ArgumentList $godotArgs -PassThru }
}

function Wait-ForHost($process) {
    $deadline = (Get-Date).AddSeconds($hostListensWithinSeconds)
    while ((Get-Date) -lt $deadline) {
        if ($process.HasExited) { throw "the host closed before it was ready: see $run\host\$logInFolder" }
        if (Get-NetUDPEndpoint -LocalPort $Port -ErrorAction SilentlyContinue | Where-Object OwningProcess -eq $process.Id) { return }
        Start-Sleep -Milliseconds 500
    }
    throw "the host did not open port $Port within $hostListensWithinSeconds s: see $run\host\$logInFolder"
}

function Stop-All {
    foreach ($p in $started | Where-Object { -not $_.HasExited }) { [void]$p.CloseMainWindow() }
    $deadline = (Get-Date).AddMilliseconds($closeGentlyMs)
    while ((Get-Date) -lt $deadline -and ($started | Where-Object { -not $_.HasExited })) { Start-Sleep -Milliseconds 200 }
    foreach ($p in $started | Where-Object { -not $_.HasExited }) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
}

try {
    $hostProcess = Start-Instance "host" 0 @("--packages", (Quoted $hostPackages), "--port", $Port, "--host", (Quoted $Scenario), "--name", "Umpire")
    $started += $hostProcess
    Write-Host "Waiting for the host to open $Scenario on port $Port..."
    Wait-ForHost $hostProcess
    $started += Start-Process dotnet -ArgumentList (@((Quoted $relayDll)) + $relayArgs) -PassThru
    for ($i = 0; $i -lt $Clients.Count; $i++) {
        $kind = ($Clients[$i] -split ",")[0]
        $started += Start-Instance "client-$($i + 1)" ($i + 1) @("--join", "127.0.0.1", "--port", ($Port + 1 + $i), "--name", (Quoted "P$($i + 1) $kind"))
    }
    Write-Host "Running. Let the players in on the host. The relay window takes commands (help lists them). Close the host to end."
    while (-not $hostProcess.WaitForExit(500)) { }
}
catch { $failed = $true; throw }
finally {
    Stop-All
    if ($Keep -or $failed) { Write-Host "Kept: $run (each window's log is in <name>\$logInFolder)" }
    else {
        Remove-Item -Recurse -Force $run -ErrorAction SilentlyContinue
        if (Test-Path $run) { Write-Host "Could not remove $run yet: a window may still be closing" }
    }
}
