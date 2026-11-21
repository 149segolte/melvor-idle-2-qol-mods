# Third-party code

## MelvorModLoader

The source of the [Melvor Mod Loader](https://github.com/falorfrozen-cmd/MelvorModLoader) by falorfrozen,
version 0.1.3, under its own MIT license (`MelvorModLoader/LICENSE`). It was copied from that version's
package and has not been edited here.

It is here to be read, not built: it is the other half of what the mods compile against (`IMod`,
`ModContext`, `GameAccess`), and reading it answers questions the loader's wiki does not, such as when
`Frame` fires and what happens to an exception thrown from it. The mods use the loader that is installed in
the game; nothing in this folder ships with them.

To get or rebuild the loader itself, go to its own repository.
