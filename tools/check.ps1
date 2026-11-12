param([switch]$Full, [switch]$Quick, [string]$Only, [string]$Changed, [string]$Lock, [switch]$Again)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo
$failed = @()
$nodeTestsMs = 300000
$timingTests = "Category=Timing"
$onlyNames = @(if ($Only) { $Only.Split(",") | ForEach-Object { $_.Trim() } | Where-Object { $_ } })
$quickRun = $Quick -or $onlyNames.Count -gt 0
$stamping = -not ($quickRun -or $Lock)
$levelNames = @{ 1 = "changed"; 2 = "all"; 3 = "full" }
$level = if ($Full) { 3 } elseif ($Changed) { 1 } else { 2 }

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

$testProjects = [ordered]@{
    "Core" = "src/Core.Tests/Core.Tests.csproj"; "Client" = "src/Client.Tests/Client.Tests.csproj"; "Server" = "src/Server.Tests/Server.Tests.csproj"
    "Converter" = "tools/Converter.Tests/Converter.Tests.csproj"; "NetRelay" = "tools/NetRelay.Tests/NetRelay.Tests.csproj"
}
$appProject = "src/App/KriegsspielTable.csproj"
$surfaceInputs = '^(src/Server/Scripting/|tools/Converter/|tools/surface/|tools/module-surface\.py$)'
$checkedByNoProject = '^(tools/[^/]+\.py$|tools/surface/|tools/installer)'

function Project-Dirs($csproj) {
    $dirs = @{}
    $todo = New-Object System.Collections.Queue
    $todo.Enqueue([IO.Path]::GetFullPath((Join-Path $repo $csproj)))
    while ($todo.Count -gt 0) {
        $path = $todo.Dequeue()
        $dir = (Split-Path -Parent $path).Substring($repo.Length + 1).Replace("\", "/") + "/"
        if ($dirs.ContainsKey($dir)) { continue }
        $dirs[$dir] = $true
        foreach ($m in [regex]::Matches((Get-Content -Raw $path), 'ProjectReference Include="([^"]+)"')) {
            $todo.Enqueue([IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $path) $m.Groups[1].Value)))
        }
    }
    @($dirs.Keys)
}

function Touches($dirs) { [bool]($changedFiles | Where-Object { $f = $_; $dirs | Where-Object { $f.StartsWith($_) } }) }

$changedFiles = @()
$runAll = $true
if ($Changed) {
    $diff = git diff --name-only $Changed
    if ($LASTEXITCODE -ne 0) { Write-Host "check: $Changed is not a commit git knows" -ForegroundColor Red; exit 1 }
    $changedFiles = @(@($diff) + @(git ls-files --others --exclude-standard) | Where-Object { $_ } | Sort-Object -Unique)
    $projectDirs = @($testProjects.Values + $appProject | ForEach-Object { Project-Dirs $_ }) | Sort-Object -Unique
    $unplaced = @($changedFiles | Where-Object { $f = $_; -not ($projectDirs | Where-Object { $f.StartsWith($_) }) -and $f -notmatch $checkedByNoProject })
    $runAll = $unplaced.Count -gt 0
    Write-Host "== $($changedFiles.Count) file(s) changed since $Changed"
    if ($runAll) { Write-Host "   outside every project, so every test runs: $(($unplaced | Select-Object -First 5) -join ', ')" }
}
$testsToRun = @($testProjects.Keys | Where-Object { $onlyNames.Count -gt 0 -or $runAll -or (Touches (Project-Dirs $testProjects[$_])) })
$appTests = $runAll -or (Touches (Project-Dirs $appProject))
$runApp = if ($quickRun) { $onlyNames.Count -gt 0 } else { $appTests }
$surfaceGate = $runAll -or [bool]($changedFiles -match $surfaceInputs)
$installerGuard = $Full -or [bool]($changedFiles -match '^tools/installer')

function Tree-Hash {
    $index = Join-Path ([IO.Path]::GetTempPath()) ("ktable-tree-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
    $kept = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $env:GIT_INDEX_FILE = $index
    try {
        git read-tree HEAD
        git -c core.safecrlf=false add -A 2> $null
        $hash = git write-tree
    }
    finally {
        Remove-Item Env:GIT_INDEX_FILE
        Remove-Item -Force $index -ErrorAction SilentlyContinue
        $ErrorActionPreference = $kept
    }
    $hash
}

$stamps = Join-Path (git rev-parse --path-format=absolute --git-common-dir) "ktable-checks"
$tree = if ($stamping) { Tree-Hash } else { $null }

function Passed-Before {
    foreach ($l in $level..3) {
        $stamp = Join-Path $stamps "$tree.$($levelNames[$l])"
        if (Test-Path $stamp) { return (Get-Content -Raw $stamp).Trim() }
    }
    $null
}

function Write-Stamp {
    if ((Tree-Hash) -ne $tree) { Write-Host "   files changed while the check ran, so this tree is not stamped"; return }
    New-Item -ItemType Directory -Force $stamps | Out-Null
    $what = "$($levelNames[$level]) on $(git rev-parse --short HEAD)$(if ($Changed) { " since $Changed" }), $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
    Set-Content -Path (Join-Path $stamps "$tree.$($levelNames[$level])") -Value $what
    Write-Host "   tree $($tree.Substring(0, 12)) stamped: $what"
}

if ($tree -and -not $Again) {
    $before = Passed-Before
    if ($before) { Write-Host "check: this tree already passed ($before); -Again runs it anyway" -ForegroundColor Green; exit 0 }
}

$script:testsRun = 0

function Count-Tests {
    process {
        if ($_ -match 'Total:\s+(\d+)') { $script:testsRun += [int]$Matches[1] }
        $_
    }
}

function Run-DotnetTests([string]$filter) {
    $filterArgs = @(if ($filter) { "--filter"; $filter })
    if ($testsToRun.Count -eq $testProjects.Count) { dotnet test KriegsspielTable.sln --nologo -v q --no-build @filterArgs | Count-Tests; return }
    $worst = 0
    foreach ($name in $testsToRun) {
        dotnet test $testProjects[$name] --nologo -v q --no-build @filterArgs | Count-Tests
        if ($LASTEXITCODE -ne 0) { $worst = $LASTEXITCODE }
    }
    $global:LASTEXITCODE = $worst
}

function Only-Filter { ($onlyNames | ForEach-Object { "FullyQualifiedName~$_" }) -join "|" }

function Import-AppAssets($godot, $app) {
    $seen = -1
    for ($pass = 0; $pass -lt 4; $pass++) {
        & $godot.FullName --headless --path $app --import *> $null
        $imported = @(Get-ChildItem $app -Recurse -File -Filter "*.import" | Where-Object { $_.FullName -notmatch '\\\.godot\\' }).Count
        if ($imported -eq $seen) { return }
        $seen = $imported
    }
}

$coverageDir = Join-Path ([IO.Path]::GetTempPath()) ("ktable-coverage-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Force $coverageDir | Out-Null

$app = Join-Path $repo "src\App"

function Start-AppTests($godot, $kind, [string[]]$godotArgs) {
    $run = Join-Path ([IO.Path]::GetTempPath()) ("ktable-$kind-tests-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
    $tests = [pscustomobject]@{
        Kind = $kind; Run = $run; Out = (Join-Path $run "out.txt"); Err = (Join-Path $run "err.txt")
        Cobertura = (Join-Path $coverageDir "$kind.xml"); Process = $null
    }
    $env:KTABLE_TESTS = $Only
    try {
        With-UserFoldersIn $run {
            $collect = @("dotnet-coverage", "collect", "--output", "`"$($tests.Cobertura)`"", "--output-format", "cobertura", "--include-files", "**\KriegsspielTable.dll", "--", "`"$($godot.FullName)`"")
            $tests.Process = Start-Process "dotnet" -ArgumentList ($collect + $godotArgs + @("--path", "`"$app`"", "res://dev/tests/TestRunner.tscn")) -NoNewWindow -PassThru `
                -RedirectStandardOutput $tests.Out -RedirectStandardError $tests.Err
            $holdingTheHandleKeepsTheExitCode = $tests.Process.Handle
        }
    }
    finally { Remove-Item Env:KTABLE_TESTS -ErrorAction SilentlyContinue }
    $tests
}

function Start-AllAppTests {
    $godot = Find-GodotConsole
    if (-not $godot) { return @() }
    Import-AppAssets $godot $app
    @((Start-AppTests $godot "node" @("--headless")), (Start-AppTests $godot "screen" @("--resolution", "1280x720", "--position", "40,40")))
}

function Finish-AppTests($kind) {
    $tests = $appRuns | Where-Object { $_.Kind -eq $kind }
    if (-not $tests) { Write-Host "   Godot .NET not found (winget install GodotEngine.GodotEngine.Mono)"; $global:LASTEXITCODE = 1; return }
    try {
        $global:LASTEXITCODE = Wait-WithExitCode $tests.Process $nodeTestsMs
        $lines = Get-Content $tests.Out, $tests.Err -ErrorAction SilentlyContinue
        $lines | Where-Object { $_ -match "^(FAIL|$kind tests)" } | ForEach-Object { "   $_" }
        $lines | Where-Object { $_ -match "^$kind tests: (\d+) passed, (\d+) failed" } | ForEach-Object { $script:testsRun += [int]$Matches[1] + [int]$Matches[2] }
        if (-not ($lines -match "^$kind tests: \d+ passed, 0 failed")) { $global:LASTEXITCODE = 1 }
        if (-not (Test-Path $tests.Cobertura)) { Write-Host "   no coverage was collected (dotnet tool restore?)"; $global:LASTEXITCODE = 1 }
    }
    finally { Remove-Item -Recurse -Force $tests.Run -ErrorAction SilentlyContinue }
}

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
    }
    finally {
        Set-Location $repo
        git worktree remove --force $base
        Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
    }
}

Step "tools" { dotnet tool restore --verbosity quiet }
Step "build solution" { dotnet build KriegsspielTable.sln --nologo -v q }
if ($appTests -or $runApp) { Step "build app" { dotnet build src/App --nologo -v q } }
if ($Full) { Step "build app with Steam" { dotnet build src/App --nologo -v q -p:KTableSteam=true } }
$appRuns = @()
$runApp = $runApp -and $failed.Count -eq 0
if ($runApp) {
    Write-Host "== app node and screen tests started (a window opens for a moment)"
    $appRuns = @(Start-AllAppTests)
}
if ($onlyNames.Count -gt 0) { Step "tests named $Only" { Run-DotnetTests (Only-Filter) } }
elseif ($quickRun -and $testsToRun.Count -gt 0) { Step "tests ($($testsToRun -join ', '))" { Run-DotnetTests "" } }
elseif ($testsToRun.Count -gt 0) { Step "tests ($($testsToRun -join ', ')) but the timing ones" { Run-DotnetTests "Category!=Timing" } }
if ($runApp) {
    Step "app node tests (headless, under coverage)" { Finish-AppTests "node" }
    Step "app screen tests" { Finish-AppTests "screen" }
    if (-not $quickRun) { Step "app coverage floor" { Check-CoverageFloor } }
}
if ($onlyNames.Count -gt 0 -and $script:testsRun -eq 0 -and $failed.Count -eq 0) { Write-Host "   no test is named like $Only"; $failed += "no test named $Only" }
if (-not $quickRun -and $testsToRun.Count -gt 0) { Step "timing tests, alone" { Run-DotnetTests $timingTests } }
Step "size and hygiene rules" { python -I tools/codecheck.py }
if ($surfaceGate) { Step "module surface gate" { python -I tools/module-surface.py --gate --quiet } }
if ($installerGuard) { Step "installer guard" { & (Join-Path $PSScriptRoot "installer-guard-test.ps1") } }
if ($Full) { Report-VulnerablePackages }
if ($Lock) { Run-BehaviourLock $Lock }
Remove-Item -Recurse -Force $coverageDir -ErrorAction SilentlyContinue

if ($failed.Count -gt 0) { Write-Host "check: FAILED ($($failed -join ', '))" -ForegroundColor Red; exit 1 }
if ($stamping) { Write-Stamp }
Write-Host "check: all passed$(if ($quickRun) { ' (quick: no coverage floor, no stamp)' })" -ForegroundColor Green
