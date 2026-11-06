$ErrorActionPreference = "Stop"
$iss = Join-Path $PSScriptRoot "installer.iss"
$dataFolder = "data_KriegsspielTable_windows_x86_64"
$problems = @()

function Find-Iscc {
    @((Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"), (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"), (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")) |
        Where-Object { Test-Path $_ } | Select-Object -First 1
}

function Section($text, $from, $to) {
    $start = $text.IndexOf($from)
    if ($start -lt 0) { throw "installer.iss has no $from section" }
    if (-not $to) { return $text.Substring($start) }
    $end = $text.IndexOf($to)
    if ($end -lt $start) { throw "installer.iss has no $to section after $from" }
    $text.Substring($start, $end - $start)
}

function Note($wrong) {
    $script:problems += $wrong
    Write-Host "   $wrong" -ForegroundColor Red
}

function Check-TheGuardCoversWhatIsDeleted($removed, $code) {
    $listed = @([regex]::Matches($code, "Folders\[\d+\]\s*:=\s*'([^']+)'") | ForEach-Object { $_.Groups[1].Value })
    $length = [regex]::Match($code, "SetArrayLength\(Folders,\s*(\d+)\)")
    if (-not $length.Success) { Note "the guard no longer sets the length of its folder list"; return }
    if ([int]$length.Groups[1].Value -ne $listed.Count) { Note "the guard names $($listed.Count) folder(s) and sets its list to $($length.Groups[1].Value)" }
    foreach ($name in $removed) {
        if ($name.Contains("*")) {
            if (-not $code.Contains("'$name'")) { Note "[InstallDelete] removes $name and the guard does not look for it" }
        }
        elseif ($listed -notcontains $name) { Note "[InstallDelete] removes $name and the guard's folder list leaves it out" }
    }
    foreach ($name in $listed) {
        if ($removed -notcontains $name) { Note "the guard checks $name and [InstallDelete] does not remove it" }
    }
}

$source = Get-Content $iss -Raw
$deletes = Section $source "[InstallDelete]" "[Files]"
$code = Section $source "[Code]"
$shipped = $deletes + $code
$removed = @([regex]::Matches($deletes, 'Name:\s*"\{app\}\\([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
if ($removed.Count -eq 0) { Note "[InstallDelete] removes nothing from {app}" }
Check-TheGuardCoversWhatIsDeleted $removed $code

$iscc = Find-Iscc
if (-not $iscc) { Write-Host "   Inno Setup 6 not found (winget install JRSoftware.InnoSetup)"; exit 1 }

$stand = @'
[Setup]
AppId={{9A7F1C20-5D4E-4B88-9E31-7C0A2F6B4D15}
AppName=Kriegsspiel Table installer guard test
AppVersion=1.0
DefaultDirName={localappdata}\ktable-installer-guard-test
PrivilegesRequired=lowest
CreateAppDir=yes
Uninstallable=no
DisableProgramGroupPage=yes
OutputDir=@SCRATCH@
OutputBaseFilename=guard-test-setup
Compression=none
CloseApplications=yes

[Files]
Source: "@DIST@\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

'@

$scratch = Join-Path ([IO.Path]::GetTempPath()) ("ktable-installer-guard-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
$dist = Join-Path $scratch "dist"
$installed = Join-Path $scratch "installed"
New-Item -ItemType Directory -Force $dist, $installed | Out-Null

try {
    foreach ($d in "converter", "standard", "backdrops", "docs", $dataFolder) {
        New-Item -ItemType Directory -Force (Join-Path $dist $d) | Out-Null
        Set-Content (Join-Path $dist "$d\new.txt") "new $d" -Encoding utf8
    }
    Set-Content (Join-Path $dist "BUILD.txt") "new build" -Encoding utf8

    foreach ($d in "converter", "standard", "backdrops", "docs", $dataFolder) {
        New-Item -ItemType Directory -Force (Join-Path $installed $d) | Out-Null
        Set-Content (Join-Path $installed "$d\old.txt") "old $d" -Encoding utf8
    }
    Set-Content (Join-Path $installed "BUILD.txt") "old build" -Encoding utf8
    $held = Join-Path $installed "$dataFolder\held.bin"
    Set-Content $held "an assembly the running app has open" -Encoding utf8

    $standIn = Join-Path $scratch "guard-test.iss"
    Set-Content $standIn ($stand.Replace("@SCRATCH@", $scratch).Replace("@DIST@", $dist) + $shipped) -Encoding utf8
    & $iscc /Q "/DDist=$dist" $standIn
    if ($LASTEXITCODE -ne 0) { throw "the stand-in installer did not compile ($LASTEXITCODE)" }
    $setup = Join-Path $scratch "guard-test-setup.exe"

    $handle = [IO.File]::Open($held, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $line = '/SILENT /SUPPRESSMSGBOXES /NORESTART /LOG="' + $scratch + '\held.log" /DIR="' + $installed + '"'
        $run = Start-Process -FilePath $setup -ArgumentList $line -Wait -PassThru
        if ($run.ExitCode -eq 0) { Note "the installer ran to the end while a file in $dataFolder was held" }
        foreach ($d in "converter", "standard", "backdrops", "docs", $dataFolder) {
            if (-not (Test-Path (Join-Path $installed $d))) { Note "$d was deleted although the installer stopped" }
            if (-not (Test-Path (Join-Path $installed "$d\old.txt"))) { Note "$d\old.txt was deleted although the installer stopped" }
        }
        $moved = @(Get-ChildItem $installed -Recurse -Force | Where-Object { $_.Name -like "*.replacing" -or $_.Name -like "*.update-check" })
        if ($moved.Count -gt 0) { Note "the check left $($moved.Count) file(s) or folder(s) under a name of its own: $($moved[0].FullName)" }
        if ((Get-Content (Join-Path $installed "BUILD.txt")) -ne "old build") { Note "BUILD.txt was overwritten although the installer stopped" }
    }
    finally { $handle.Close() }

    $line = '/SILENT /SUPPRESSMSGBOXES /NORESTART /LOG="' + $scratch + '\free.log" /DIR="' + $installed + '"'
    $run = Start-Process -FilePath $setup -ArgumentList $line -Wait -PassThru
    if ($run.ExitCode -ne 0) { Note "the installer failed with nothing holding the folder ($($run.ExitCode))" }
    if ((Get-Content (Join-Path $installed "BUILD.txt")) -ne "new build") { Note "BUILD.txt was not replaced" }
    foreach ($d in "converter", "standard", "backdrops", "docs", $dataFolder) {
        if (-not (Test-Path (Join-Path $installed "$d\new.txt"))) { Note "$d was not installed" }
        if (Test-Path (Join-Path $installed "$d\old.txt")) { Note "$d\old.txt outlived the install" }
    }
}
finally {
    if ($problems.Count -eq 0) { Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue }
    else { Write-Host "   the run is in $scratch" }
}

if ($problems.Count -gt 0) {
    Write-Host "installer guard: $($problems.Count) problem(s)"
    exit 1
}
Write-Host "installer guard: the guard covers every folder [InstallDelete] removes, a held file stops the installer before it deletes anything, and a free folder installs clean"
exit 0
