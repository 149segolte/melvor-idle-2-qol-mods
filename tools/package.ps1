# Builds the mods in Release (no development helpers) and writes into dist\:
#   <Mod>-<version>.zip                  one per mod: mods\<Mod>\<Mod>.dll and README.txt, nothing else
#   MelvorIdle2-QoL-Mods-<version>.zip   all of them in one zip
#   nexus\<Mod>.txt                      the page texts of publish\nexus with the version filled in
# Every zip is made to be extracted into the game folder. The game does not have to be closed: nothing is
# written into its folder.
#   .\tools\package.ps1                  everything
#   .\tools\package.ps1 -Mod RecipeRates one mod's zip only
# Needs PowerShell 7 and the .NET 10 SDK.
param(
    [string[]]$Mod = @(),
    [string]$GameDir = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'game-dir.ps1')
$GameDir = Get-GameDir $GameDir
$version = Get-ModVersion
$asked = @(Resolve-ModNames $Mod)
$names = if ($asked.Count) { $asked } else { Get-ModNames }

$dist = Join-Path $RepoRoot 'dist'
$stage = Join-Path $dist 'stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force -Confirm:$false }
New-Item -ItemType Directory -Force $stage | Out-Null

function Write-Zip([string]$From, [string]$Name) {
    $zip = Join-Path $dist $Name
    if (Test-Path $zip) { Remove-Item $zip -Force -Confirm:$false }
    Compress-Archive -Path $From -DestinationPath $zip
    '{0,-40} {1,9:N0} bytes' -f $Name, (Get-Item $zip).Length
}

foreach ($name in $names) {
    $project = Join-Path $RepoRoot "mods\$name"
    $out = Join-Path $stage "$name\mods\$name"
    # A forward slash at the end: a backslash before the closing quote would swallow the quote.
    $output = & dotnet build (Join-Path $project "$name.csproj") -c Release --nologo -v q "-p:GameDir=$GameDir" "-p:OutputPath=$out/" 2>&1
    if ($LASTEXITCODE -ne 0) { $output | Select-Object -Last 20; throw "Build of $name failed." }

    # Only the mod's own DLL ships: the game and the loader already have everything else.
    Get-ChildItem $out -File | Where-Object { $_.Name -ne "$name.dll" } | Remove-Item -Force -Confirm:$false
    Get-ChildItem $out -Directory | Remove-Item -Recurse -Force -Confirm:$false
    if (-not (Test-Path (Join-Path $out "$name.dll"))) { throw "$name.dll was not built." }
    (Get-Content (Join-Path $project 'README.txt') -Raw).Replace('{version}', $version) | Set-Content (Join-Path $out 'README.txt') -NoNewline

    Write-Zip (Join-Path $stage "$name\mods") "$name-$version.zip"
}

if (-not $asked.Count) {
    # Each mod's folder next to the others, under one "mods".
    $all = Join-Path $stage '_all\mods'
    New-Item -ItemType Directory -Force $all | Out-Null
    foreach ($name in $names) { Copy-Item (Join-Path $stage "$name\mods\$name") $all -Recurse }
    Write-Zip $all "MelvorIdle2-QoL-Mods-$version.zip"

    $texts = Join-Path $dist 'nexus'
    New-Item -ItemType Directory -Force $texts | Out-Null
    Get-ChildItem (Join-Path $RepoRoot 'publish\nexus\*.txt') | ForEach-Object {
        (Get-Content $_.FullName -Raw).Replace('{version}', $version) | Set-Content (Join-Path $texts $_.Name) -NoNewline
    }
    "Page texts for version $version are in dist\nexus."
}
Remove-Item $stage -Recurse -Force -Confirm:$false
