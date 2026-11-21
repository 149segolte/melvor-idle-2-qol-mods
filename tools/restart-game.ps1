# Closes the game (gracefully, so it saves), optionally rebuilds mods into its folder, starts it again through
# Steam and prints the loader's log. A mod's DLL is locked while the game runs, so the build happens in between.
#   .\tools\restart-game.ps1
#   .\tools\restart-game.ps1 -Mod RecipeRates,KillsInCombat
#   .\tools\restart-game.ps1 -Mod CombatSim -DevTools        with the development helpers compiled in
# Needs PowerShell 7 and the .NET 10 SDK.
param(
    [string[]]$Mod = @(),
    [string]$Configuration = 'Release',
    [switch]$DevTools,
    [string]$GameDir = '',
    [int]$AppId = 3218350
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'game-dir.ps1')
$GameDir = Get-GameDir $GameDir
$mods = @(Resolve-ModNames $Mod)

$running = Get-Process Melvor2 -ErrorAction SilentlyContinue
if ($running) {
    $running.CloseMainWindow() | Out-Null
    if (-not $running.WaitForExit(30000)) { throw 'The game did not close within 30 seconds.' }
    Start-Sleep -Seconds 1
}

foreach ($name in $mods) {
    $arguments = @('build', (Join-Path $RepoRoot "mods\$name\$name.csproj"), '-c', $Configuration, '--nologo', '-v', 'q', "-p:GameDir=$GameDir")
    if ($DevTools) { $arguments += '-p:DevTools=true' }
    $output = & dotnet @arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        $output | Select-Object -Last 15
        Write-Warning "Build of $name failed; the game starts with what was there before."
    }
}

# After the game closes, Steam syncs its cloud saves and drops start requests until it is done (sometimes
# over a minute): keep asking until the game comes up.
$steam = Join-Path ($GameDir -replace '\\steamapps\\common\\.*$', '') 'steam.exe'
foreach ($attempt in 1..6) {
    Start-Sleep -Seconds 6
    # steam.exe itself, not the steam:// link: the link's requests did not always reach Steam.
    if (Test-Path $steam) { Start-Process $steam -ArgumentList '-applaunch', $AppId } else { Start-Process "steam://rungameid/$AppId" }
    $deadline = (Get-Date).AddSeconds(25)
    while ((Get-Date) -lt $deadline -and -not (Get-Process Melvor2 -ErrorAction SilentlyContinue)) { Start-Sleep -Milliseconds 500 }
    if (Get-Process Melvor2 -ErrorAction SilentlyContinue) { break }
}
if (-not (Get-Process Melvor2 -ErrorAction SilentlyContinue)) { throw 'The game did not start.' }
Start-Sleep -Seconds 12
Get-Content (Join-Path $GameDir 'MelvorModLoader.log')
