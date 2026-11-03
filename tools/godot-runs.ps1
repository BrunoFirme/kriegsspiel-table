function Find-GodotConsole {
    Get-ChildItem (Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Packages") -Directory -Filter "GodotEngine.GodotEngine.Mono*" -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ChildItem $_.FullName -Recurse -Filter "Godot_v*_mono_win64_console.exe" } | Select-Object -First 1
}

function With-UserFoldersIn($run, [scriptblock]$body) {
    $names = "APPDATA", "LOCALAPPDATA", "TEMP", "TMP", "KTABLE_USER_DIR"
    $kept = @{}; foreach ($n in $names) { $kept[$n] = [Environment]::GetEnvironmentVariable($n) }
    try {
        foreach ($n in $names) { New-Item -ItemType Directory -Force (Join-Path $run $n) | Out-Null; [Environment]::SetEnvironmentVariable($n, (Join-Path $run $n)) }
        & $body
    }
    finally {
        foreach ($n in $names) { [Environment]::SetEnvironmentVariable($n, $kept[$n]) }
    }
}
