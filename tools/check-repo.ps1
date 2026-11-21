# Checks what can be checked without the game: the mods cannot be built on a machine that does not have
# Melvor Idle 2, so this guards the rules of the repository instead. Runs on every push and pull request,
# and locally:
#   .\tools\check-repo.ps1          (PowerShell 7, Windows or Linux)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
$problems = [System.Collections.Generic.List[string]]::new()
function Fail([string]$message) { $problems.Add($message) }

try {
    # Everything git knows about or would add: tracked files plus new ones that are not ignored.
    $files = @(git ls-files) + @(git ls-files --others --exclude-standard) | Sort-Object -Unique

    # 1. Source only. Nothing built, nothing from the game: its DLLs, its package, its scenes, styles, fonts
    #    and sounds, its log, its saves. The only pictures are the ones of the readmes, under docs/img.
    $never = '\.(dll|exe|pdb|so|dylib|zip|7z|rar|pck|sav|import|ctex|godot|tres|tscn|res|scn|ttf|otf|ogg|wav|mp3|glb|log|cfg)$'
    foreach ($file in $files | Where-Object { $_ -match $never }) {
        Fail "$file : built or game file. The repository holds source only."
    }
    foreach ($file in $files | Where-Object { $_ -match '\.(png|jpe?g|gif|webp|bmp)$' -and $_ -notmatch '^docs/img/' }) {
        Fail "$file : pictures belong under docs/img."
    }
    foreach ($file in $files | Where-Object { $_ -match '(^|/)(bin|obj|dist|_save-backup)/' -or $_ -match '(^|/)Local\.props$' }) {
        Fail "$file : must stay out of git."
    }

    # 2. Files stay small enough to browse.
    foreach ($file in $files) {
        $size = (Get-Item -LiteralPath $file -Force).Length   # -Force: dot files are hidden on Linux
        if ($size -gt 1MB) { Fail ('{0} : {1:N0} bytes. Keep files under 1 MB.' -f $file, $size) }
    }

    # 3. No path into somebody's user folder (the Windows form with single, doubled or forward slashes, and
    #    the macOS and Linux forms) and no Steam id, in any file that is text.
    $text = $files | Where-Object { $_ -notmatch '\.(png|jpe?g|gif|webp|bmp)$' -and $_ -notmatch '^third_party/' }
    foreach ($file in $text) {
        $content = Get-Content -LiteralPath $file -Raw -Force
        if (-not $content) { continue }
        if ($content -match '(?i)[A-Z]:[\\/]+Users[\\/]+\w' -or $content -match '(?<![\w.])/(Users|home)/[A-Za-z][\w.-]*/') {
            Fail "$file : contains a path into a user folder."
        }
        if ($content -match '\b7656119\d{10}\b') { Fail "$file : contains a Steam id." }
    }

    # 4. One version, one place for what the mods share, and references that never copy the game's DLLs.
    $props = [xml](Get-Content mods/Directory.Build.props -Raw)
    $version = $props.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if ($version -notmatch '^\d+\.\d+\.\d+$') { Fail 'mods/Directory.Build.props : <Version> is missing or not like 1.2.3.' }
    foreach ($reference in $props.Project.ItemGroup.Reference | Where-Object { $_ }) {
        if ($reference.Private -ne 'false') {
            Fail "mods/Directory.Build.props : reference $($reference.Include) needs <Private>false</Private>, or the game's DLL is copied into every mod."
        }
    }
    if ((Get-Content CHANGELOG.md -Raw) -notmatch "(?m)^## $([regex]::Escape($version))\s*$") {
        Fail "CHANGELOG.md : there is no '## $version' entry for the version in mods/Directory.Build.props."
    }

    # 5. Every mod is complete.
    $mods = Get-ChildItem mods -Directory | Where-Object { Test-Path (Join-Path $_.FullName "$($_.Name).csproj") }
    if (-not $mods) { Fail 'No mod projects found under mods/.' }
    $readme = Get-Content README.md -Raw
    foreach ($mod in $mods) {
        $name = $mod.Name
        $project = [xml](Get-Content (Join-Path $mod.FullName "$name.csproj") -Raw)
        # A mod's own project file adds nothing of its own to what a mod is made of.
        foreach ($own in 'Version', 'AssemblyName', 'OutputPath', 'TargetFramework') {
            if ($project.Project.PropertyGroup.$own | Where-Object { $_ }) { Fail "mods/$name/$name.csproj : <$own> belongs in mods/Directory.Build.props." }
        }
        if ($project.Project.ItemGroup.Reference | Where-Object { $_ }) { Fail "mods/$name/$name.csproj : references belong in mods/Directory.Build.props." }
        if ($project.Project.ItemGroup.PackageReference | Where-Object { $_ }) { Fail "mods/$name : package references are not supported, a mod ships as one DLL." }

        if (-not (Test-Path (Join-Path $mod.FullName 'README.md'))) { Fail "mods/$name : README.md is missing." }
        foreach ($stamped in "mods/$name/README.txt", "publish/nexus/$name.txt") {
            if (-not (Test-Path $stamped)) { Fail "$stamped is missing."; continue }
            $content = Get-Content $stamped -Raw
            # tools/package.ps1 fills the version in; a number typed here would go stale.
            if ($content -notmatch '\{version\}') { Fail "$stamped : write {version} where the version goes." }
            if ($content -match '\b\d+\.\d+\.\d+\.zip\b') { Fail "$stamped : names a zip with a version number typed in." }
        }
        if ($readme -notmatch [regex]::Escape("mods/$name")) { Fail "README.md does not link mods/$name." }
    }

    # 6. Development helpers are behind DEVTOOLS, which only tools/dev-cycle.ps1 switches on: a plain build,
    #    Debug or Release, must be the mod as players get it.
    foreach ($file in $files | Where-Object { $_ -match '^mods/.*\.cs$' }) {
        if ((Get-Content -LiteralPath $file -Raw) -match '(?m)^\s*#if\s+!?DEBUG\b') { Fail "$file : use #if DEVTOOLS for development helpers, not DEBUG." }
    }

    # 7. Links between the pages of the repository lead somewhere.
    foreach ($page in $files | Where-Object { $_ -match '\.md$' -and $_ -notmatch '^third_party/' }) {
        $folder = Split-Path $page -Parent
        $content = Get-Content -LiteralPath $page -Raw -Force
        $targets = @([regex]::Matches($content, '\]\(([^)\s]+)\)') | ForEach-Object { $_.Groups[1].Value }) +
                   @([regex]::Matches($content, '(?:src|href)="([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
        foreach ($target in $targets | Sort-Object -Unique) {
            if ($target -match '^(https?:|mailto:|#)') { continue }
            $path = ($target -split '[#?]')[0]
            if (-not $path) { continue }
            $full = if ($folder) { Join-Path $folder $path } else { $path }
            if (-not (Test-Path -LiteralPath $full)) { Fail "$page : link to $target leads nowhere." }
        }
    }

    # 8. The version tags under the banner are pictures: their alt texts must say what is drawn.
    $art = Get-Content tools/make-art.py -Raw
    foreach ($name in 'GAME_VERSION', 'LOADER_VERSION') {
        if ($art -notmatch "(?m)^$name = '([^']+)'") { Fail "tools/make-art.py : $name is missing."; continue }
        $drawn = $Matches[1].TrimEnd('+')
        if ($readme -notmatch [regex]::Escape($drawn)) { Fail "README.md : the alt text of a version tag does not say $drawn ($name in tools/make-art.py)." }
    }
}
finally { Pop-Location }

if ($problems.Count) {
    $problems | ForEach-Object { Write-Host "  x $_" }
    Write-Host "`n$($problems.Count) problem(s)."
    exit 1
}
Write-Host "Repository checks passed: $($mods.Count) mods at $version, $($files.Count) files."
