param([switch]$Quick, [string]$Lock)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo
$failed = @()
$nodeTestsMs = 300000

function Step($name, [scriptblock]$body) {
    Write-Host "== $name"
    & $body
    if ($LASTEXITCODE -ne 0) { $script:failed += $name; Write-Host "   FAILED: $name" -ForegroundColor Red }
}

. (Join-Path $PSScriptRoot "godot-runs.ps1")

function Wait-WithExitCode($process, $ms) {
    $holdingTheHandleKeepsTheExitCode = $process.Handle
    if (-not $process.WaitForExit($ms)) { $process.Kill(); Write-Host "   the node tests did not finish in $($ms / 60000) minutes"; return 1 }
    $process.ExitCode
}

$coverageDir = Join-Path ([IO.Path]::GetTempPath()) ("ktable-coverage-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Force $coverageDir | Out-Null

function Run-AppTests($kind, [string[]]$godotArgs) {
    $godot = Find-GodotConsole
    if (-not $godot) { Write-Host "   Godot .NET not found (winget install GodotEngine.GodotEngine.Mono)"; $global:LASTEXITCODE = 1; return }
    $app = Join-Path $repo "src\App"
    if ($kind -eq "node") { & $godot.FullName --headless --path $app --import *> $null }
    $run = Join-Path ([IO.Path]::GetTempPath()) ("ktable-$kind-tests-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
    $cobertura = Join-Path $coverageDir "$kind.xml"
    try {
        With-UserFoldersIn $run {
            $out = Join-Path $run "out.txt"
            $err = Join-Path $run "err.txt"
            $collect = @("dotnet-coverage", "collect", "--output", "`"$cobertura`"", "--output-format", "cobertura", "--include-files", "**\KriegsspielTable.dll", "--", "`"$($godot.FullName)`"")
            $p = Start-Process "dotnet" -ArgumentList ($collect + $godotArgs + @("--path", "`"$app`"", "res://dev/tests/TestRunner.tscn")) -NoNewWindow -PassThru `
                -RedirectStandardOutput $out -RedirectStandardError $err
            $global:LASTEXITCODE = Wait-WithExitCode $p $nodeTestsMs
            $lines = Get-Content $out, $err -ErrorAction SilentlyContinue
            $lines | Where-Object { $_ -match "^(FAIL|$kind tests)" } | ForEach-Object { "   $_" }
            if (-not ($lines -match "^$kind tests: \d+ passed, 0 failed")) { $global:LASTEXITCODE = 1 }
            if (-not (Test-Path $cobertura)) { Write-Host "   no coverage was collected (dotnet tool restore?)"; $global:LASTEXITCODE = 1 }
        }
    }
    finally { Remove-Item -Recurse -Force $run -ErrorAction SilentlyContinue }
}

function Run-NodeTests { Run-AppTests "node" @("--headless") }

function Run-ScreenTests { Run-AppTests "screen" @("--resolution", "1280x720", "--position", "40,40") }

function Check-CoverageFloor {
    $files = Get-ChildItem $coverageDir -Filter "*.xml" | ForEach-Object { $_.FullName }
    if ($files.Count -eq 0) { Write-Host "   no coverage files"; $global:LASTEXITCODE = 1; return }
    python -I tools/covcheck.py @files
}

function Report-VulnerablePackages {
    Write-Host "== packages with known vulnerabilities"
    dotnet list KriegsspielTable.sln package --vulnerable --include-transitive
    dotnet list src/App package --vulnerable --include-transitive
}

function Run-BehaviourLock($ref) {
    $work = Join-Path ([IO.Path]::GetTempPath()) ("ktable-check-lock-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
    $base = Join-Path $work "base"
    git worktree add --detach $base $ref
    if ($LASTEXITCODE -ne 0) { $script:failed += "behaviour lock (no worktree of $ref)"; return }
    try {
        Step "behaviour lock: app" { python -I tests/lock/app_lock.py --old $base --new $repo --quick }
        Step "publish the converters" {
            dotnet publish (Join-Path $base "tools/Converter") -c Release -r win-x64 --self-contained false -o (Join-Path $work "conv-old") --nologo -v q
            if ($LASTEXITCODE -eq 0) { dotnet publish tools/Converter -c Release -r win-x64 --self-contained false -o (Join-Path $work "conv-new") --nologo -v q }
        }
        if ($script:failed -notcontains "publish the converters") {
            Step "behaviour lock: converter" { python -I tests/lock/converter_diff.py --old (Join-Path $work "conv-old") --new (Join-Path $work "conv-new") --quick }
        }
    }
    finally {
        Set-Location $repo
        git worktree remove --force $base
        Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
    }
}

Step "tools" { dotnet tool restore --verbosity quiet }
Step "build solution" { dotnet build KriegsspielTable.sln --nologo -v q }
Step "build app" { dotnet build src/App --nologo -v q }
if (-not $Quick) { Step "build app with Steam" { dotnet build src/App --nologo -v q -p:KTableSteam=true } }
Step "tests" { dotnet test KriegsspielTable.sln --nologo -v q --no-build }
Step "app node tests (headless, under coverage)" { Run-NodeTests }
Step "app screen tests (a window opens for a moment)" { Run-ScreenTests }
Step "app coverage floor" { Check-CoverageFloor }
Step "size and hygiene rules" { python -I tools/codecheck.py }
if (-not $Quick) { Step "installer guard" { & (Join-Path $PSScriptRoot "installer-guard-test.ps1") } }
if (-not $Quick) { Report-VulnerablePackages }
if ($Lock) { Run-BehaviourLock $Lock }
Remove-Item -Recurse -Force $coverageDir -ErrorAction SilentlyContinue

if ($failed.Count -gt 0) { Write-Host "check: FAILED ($($failed -join ', '))" -ForegroundColor Red; exit 1 }
Write-Host "check: all passed" -ForegroundColor Green
