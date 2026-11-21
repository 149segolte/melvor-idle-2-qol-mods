# Publishing

The mods are distributed on [Nexus Mods](https://www.nexusmods.com/games/melvoridle2/mods), where the Melvor
Mod Loader and the other Melvor Idle 2 mods live, and as zips on this repository's releases. Each mod is a
page of its own, so players take the ones they want; one more page holds all of them in a single zip.

## Before a release

1. All the mods share one version: set `<Version>` in `mods/Directory.Build.props` and add a `## <version>`
   entry to `CHANGELOG.md`. Nothing else holds a version number: `README.txt` and the texts in `nexus/` say
   `{version}`, and the packaging fills it in.
2. Run `.\tools\check-repo.ps1`.
3. Run `.\tools\package.ps1`. It builds every mod in Release and writes into `dist\`:
   - `<Mod>-<version>.zip` for each mod: `mods\<Mod>\<Mod>.dll` and `README.txt`, nothing else. Never the
     game's, Godot's or the loader's files.
   - `MelvorIdle2-QoL-Mods-<version>.zip`: all the mods in one zip.
   - `nexus\<Mod>.txt`: the page texts with the version filled in.
4. Try the zips the way a player gets them: close the game, extract them into the game folder, start the
   game, and read `MelvorModLoader.log`. (`.\tools\dev-cycle.ps1` builds with the development helpers; a
   zip never has them.)
5. When the game or the loader version the mods are made for has changed, set `GAME_VERSION` and
   `LOADER_VERSION` at the top of `tools/make-art.py`, run `python tools/make-art.py`, and write the same
   versions into the `alt` texts of the two version tags at the top of the main `README.md`.

## On GitHub

Tag the commit `v<version>` and attach every zip from `dist\` to a release, so the repository alone is
enough to install from. The release notes are the changelog entry, with the install steps under it.

## A mod's page on Nexus

The texts are in `dist\nexus\<Mod>.txt` after packaging: name, version, summary and description, ready to
paste. `Pack.txt` is the page of the all-in-one zip.

1. Add a mod to the Melvor Idle 2 section and paste the name, the summary and the description.
2. Upload `dist\<Mod>-<version>.zip` as the main file, with the same version number.
3. Under **Requirements**, add **Melvor Mod Loader** (minimum 0.1.3) as a required file.
4. Add the mod's pictures from `docs/img`.
5. Under permissions, link this repository as the source.

For an update: upload the new zip as the main file with the new version, and paste the changelog entry into
the page's changelog.
