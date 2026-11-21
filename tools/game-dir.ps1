# Dot-sourced by the other scripts: where the game is installed, and which mods there are.
# The game folder comes from the -GameDir argument, else Local.props in the root of the repository (see
# Local.props.example), else the usual Steam folder.
$script:RepoRoot = Split-Path $PSScriptRoot -Parent

function Get-GameDir([string]$Given) {
    $dir = $Given
    if (-not $dir) {
        $local = Join-Path $script:RepoRoot 'Local.props'
        if (Test-Path $local) { $dir = (([xml](Get-Content $local -Raw)).Project.PropertyGroup.GameDir | Where-Object { $_ } | Select-Object -First 1) }
    }
    if (-not $dir) { $dir = 'C:\Program Files (x86)\Steam\steamapps\common\Melvor Idle 2' }
    if (-not (Test-Path (Join-Path $dir 'data_GamesByMalcs.Melvor2_windows_x86_64'))) {
        throw "Melvor Idle 2 was not found in '$dir'. Pass -GameDir, or copy Local.props.example to Local.props and set the folder there."
    }
    $dir
}

# The names of the mods: every folder under mods\ that holds a project of the same name.
function Get-ModNames {
    Get-ChildItem (Join-Path $script:RepoRoot 'mods') -Directory |
        Where-Object { Test-Path (Join-Path $_.FullName "$($_.Name).csproj") } |
        ForEach-Object Name
}

# "-Mod A,B" and "-Mod 'A,B'" both mean two mods; names that do not exist stop the script.
function Resolve-ModNames([string[]]$Given) {
    $all = @(Get-ModNames)
    $asked = @($Given | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    foreach ($name in $asked) { if ($all -notcontains $name) { throw "There is no mod '$name'. The mods are: $($all -join ', ')." } }
    $asked
}

# The version all the mods share (mods\Directory.Build.props).
function Get-ModVersion {
    $props = [xml](Get-Content (Join-Path $script:RepoRoot 'mods\Directory.Build.props') -Raw)
    $props.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
