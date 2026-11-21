## What this changes

<!-- One change per pull request. What does a player see that they did not see before?
     If it answers an issue, write "Closes #12". -->

## How I tried it

<!-- Game version, loader version, what you did in the game, and what you could NOT try.
     The checks here cannot build or run the mods, so this part is what a reviewer goes by. -->

## Pictures

<!-- When the change is visible: before and after, next to one of the game's own screens if you can. -->

## Who wrote it

<!-- If an AI agent wrote some or all of this, say so, and say what a person looked at in the game. -->

## Checklist

- [ ] It looks like the game (styles come from `mods/Shared/GameStyle.cs`).
- [ ] It does not write the save, give items or XP, or change a number the game keeps.
- [ ] Development helpers are inside `#if DEVTOOLS`.
- [ ] No game files are added, and `tools/check-repo.ps1` passes.
- [ ] The version number is untouched; what players would notice is a line under `## Unreleased` in
      `CHANGELOG.md`, and the mod's readmes say the same as the code.
