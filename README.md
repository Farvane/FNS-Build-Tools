# \[FNS\] GRIDWORX - Build Tools

**v0.1.3 preview.** Symmetry to CoG verified in-game (single-player). An optional Pulsar Modern client
plugin for Space Engineers 2 with precision construction helpers.

v0.1 sets a grid's X, Y and Z symmetry planes to its **CoG (Center of
Grid)**, the center of the grid's block bounding box, in one click. It uses
the same replicated symmetry call as the vanilla symmetry setup and writes
nothing to disk.

Requires **Pulsar Modern** and **SE2 4.0.0.3169**. Other game builds are
disabled until validated. Windows is the release test platform; multiplayer is
not yet validated.

Planned next: move a grid to world origin, Center of Room symmetry, measuring
tools and an asymmetry finder. See [docs/DESIGN.md](docs/DESIGN.md).

## Install

1. Install Pulsar from <https://github.com/SpaceGT/Pulsar/releases> and run Modern.
2. Close SE2/Pulsar. Copy `FNSBuildTools.dll` and `FNSBuildTools.xml` into the
   existing `Modern/Local` directory under your Pulsar installation.
3. Enable **FNSBuildTools** in your Pulsar profile and launch SE2.

## Use

1. Load a world, equip any block, and look at the grid you want to edit.
2. Open the plugin's settings from the Pulsar plugin menu. The status lists the
   target grid's size per axis and its current planes.
3. Click **All axes → CoG**, or a single axis. Turn on symmetry mode in the block
   placer to see the planes.

## Hotkeys

Hotkeys work only while a block is equipped. A short message near the top of
the screen confirms each action.

| Default | Action |
| --- | --- |
| `Ctrl+Alt+N` | Set all symmetry planes to CoG (ceNter) |
| `Ctrl+Alt+Shift+N` | Clear all symmetry planes |
| `Ctrl+Alt+I` | Open the Build Tools screen |

Vanilla reacts to its own keys even while Ctrl+Alt is held. For example, `G` opens the block picker. The defaults therefore use keys that vanilla gameplay leaves unbound (`I`, `N`, and `O`, which is reserved for Home Grid). Pick unbound keys when customizing. Modifiers must match exactly. To change a hotkey, close SE2 and edit
`settings.json` in the plugin's Pulsar configuration folder (the screen shows
the path). Set an action to `""` to disable it:

```json
{
  "HotkeysEnabled": true,
  "Hotkeys": { "SymmetryToCoG": "Ctrl+Alt+F6", "OpenBuildTools": "" }
}
```

Every chord needs Ctrl or Alt. Key names follow the game's keyboard inputs
(`A`–`Z`, `D0`–`D9`, `F1`–`F24`, ...). An invalid entry disables only that
hotkey, and the reason appears on the screen and in the SE2 log.

An even cell count puts the plane on a cell boundary, and an odd count puts it
through a cell center. Both are exact. SE2 grid cells are 0.25 m.

## In-game test checklist (v0.1)

- [x] Status shows the looked-at grid; sizes match the grid (count blocks).
- [x] All axes → CoG: planes appear centered; mirrored placement lands symmetric.
- [ ] Even- and odd-width test grids both center exactly.
- [ ] Single-axis buttons change only that axis; Clear removes all planes.
- [ ] Planes persist after save/reload.
- [ ] `Ctrl+Alt+N` with a block equipped sets the planes and shows a message that doesn't steal mouse or keyboard input. (v0.1.2 with `G` set the planes but also opened the block picker.)
- [ ] Hotkeys do nothing without a block equipped, in chat, or in menus.
- [ ] Vanilla does not react to `N` or `I` while Ctrl+Alt is held.
- [ ] Not tested yet: a dedicated server or a multiplayer client.

## Development

Requires the installed game assemblies and .NET 10 SDK:

```powershell
.\build.ps1 -Game2Path 'path-to-your-SE2-Game2-directory'
```

Runs the game-independent unit tests, then builds a Release DLL and package
under `artifacts/`. Nothing is deployed or published automatically. Source is
MIT licensed. See `THIRD-PARTY-NOTICES.md`.

This is a community plugin, not an official Keen Software House product.
