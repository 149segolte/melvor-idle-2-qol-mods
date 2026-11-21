# Security

## What these mods do on your computer

They are DLLs that the Melvor Mod Loader loads into the game. To make it easy to check what they do:

- They are **open source**, and a release zip holds one DLL and one readme, nothing else: no installer, no
  executable, no script.
- They make **no network requests**.
- They write **only in their own folder** under the game's `mods` folder: settings such as a window's
  place, and the Combat Simulator's earlier scans.
- They read the game's own data while it runs, and one file outside the game folder: the Combat Simulator
  asks the game to write its save the way autosave does, then reads that save file (in the game's own
  user-data folder) to make its in-memory copy. It never writes there.
- They never modify the save or the game's files.

Download the mods only from this repository's [releases](https://github.com/Springs-Tea/melvor-idle-2-qol-mods/releases)
or from the project's pages on Nexus Mods. A DLL from anywhere else is not ours, whatever it is called.

## Supported versions

Only the latest release of each mod is supported. The game is in early access; after a game update, use
the newest release.

## Reporting a vulnerability

If you find something that could harm a player (for example a way a mod could be made to write outside its
folder, or to damage a save), please report it privately rather than in a public issue:

- use **Report a vulnerability** on the repository's
  [Security tab](https://github.com/Springs-Tea/melvor-idle-2-qol-mods/security/advisories/new).

You will get an answer as soon as it has been read, and credit in the fix if you want it. Ordinary bugs
(a wrong number, a tag in the wrong place) belong in the normal [issues](https://github.com/Springs-Tea/melvor-idle-2-qol-mods/issues).
