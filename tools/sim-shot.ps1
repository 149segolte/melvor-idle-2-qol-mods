# Development: asks the running Combat Simulator (built with its development helpers: tools\dev-cycle.ps1 -Mod CombatSim) for a run and a picture of it, waits for
# the picture and prints what the mod logged. The game must be open with the character loaded.
#   .\tools\sim-shot.ps1 'best Magic@Golbin+Farmlands|0|1'
#   .\tools\sim-shot.ps1 'open|0|1|||history' -Save history
# The request is  <where>|<duration index>|<1 unlimited, 0 from bank>|<row to select>|<loadout>|<picker>|<slot=item>
# (see DevRequests in mods\CombatSim\CombatSimMod.cs).
param(
    [Parameter(Mandatory)][string]$Request,
    [string]$Save = '',
    [int]$Wait = 240,
    [string]$GameDir = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'game-dir.ps1')
$GameDir = Get-GameDir $GameDir
$mod = Join-Path $GameDir 'mods\CombatSim'
$shot = Join-Path $mod 'shot-sim.png'
$log = Join-Path $GameDir 'MelvorModLoader.log'
$before = (Get-Content $log).Count
Remove-Item $shot -ErrorAction SilentlyContinue
Set-Content (Join-Path $mod 'sim.request') $Request -NoNewline
$deadline = (Get-Date).AddSeconds($Wait)
while ((Get-Date) -lt $deadline -and -not (Test-Path $shot)) { Start-Sleep -Milliseconds 500 }
Start-Sleep -Seconds 1
if (-not (Test-Path $shot)) { 'No picture after {0}s.' -f $Wait }
elseif ($Save) { Copy-Item $shot (Join-Path $mod "shot-$Save.png") -Force }
Get-Content $log | Select-Object -Skip $before | Where-Object { $_ -match 'CombatSim|Error' } |
    ForEach-Object { $_.Substring(0, [Math]::Min(400, $_.Length)) }
