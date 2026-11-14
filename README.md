# \[FNS\] GRIDWORX - Build Tools

**v0.4.0 preview.** Symmetry to CoG and Home Grid are verified in-game
(single-player). An optional Pulsar Modern client
plugin for Space Engineers 2 with precision construction helpers.

v0.1 sets a grid's X, Y and Z symmetry planes to its **CoG (Center of
Grid)**, the center of the grid's block bounding box, in one click. It uses
the same replicated symmetry call as the vanilla symmetry setup and writes
nothing to disk.

Requires **Pulsar Modern** and **SE2 4.0.0.3169**. Other game builds are
disabled until validated. Windows is the release test platform; multiplayer is
not yet validated.

v0.2 adds **Home Grid**. It moves the grid so its CoG (or CoM) sits at world
0,0,0, squared to the world axes. Anything on its rotors, pistons, hinges and
connectors moves with it, and so do you. **Return** undoes the move.

v0.4 adds **Symmetry to CoR (Center of Room)**. It sets the planes to the center
of the enclosed room you are standing in.

Planned next: measuring tools and an asymmetry finder. See [docs/DESIGN.md](docs/DESIGN.md).

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

## Symmetry to CoR

1. With a block equipped, stand inside an enclosed room of the grid and look at
   the grid.
2. Press `Ctrl+Shift+N`, or use **All axes → CoR** on the screen. Use
   **Corridor → CoR** to set only the two axes across the room's longest
   direction.

The room is all the empty space connected to where you stand, in 0.25 m cells.
Doors and other blocks count as walls, and an open doorway joins two rooms.
If that space leaks to the outside of the grid, CoR refuses with "not inside
an enclosed room". The planes go at the center of the room's bounding box.

## Home Grid

1. With a block equipped, look at the grid and press `Ctrl+Alt+O`, or use
   **Home grid (CoG)** or **Home grid (CoM)** on the screen.
3. The grid moves so the pivot is at 0,0,0, with the grid's axes on the world
   axes. Grids joined by rotors, pistons, hinges and connectors (the game's
   "Terminal" connection group) keep their pose relative to it. You keep your
   position relative to the grid.
4. `Ctrl+Alt+Shift+O` (or **Return grid**) puts everything back. Return works
   until the next Home.

**Moving grids.** If the grid or you are moving, Home first stops the whole
group, waits up to 3 s for everything to read still, and then moves it. Home
also stops the group again afterwards, so everything arrives at rest. Stopping
uses the game's in-process server, so it works in single-player and when you
host. As a client on a remote server, Home still refuses a moving grid.

**Clearance.** Home refuses if another grid's bounds (plus 0.5 m) would overlap
where the group lands, and names the grid in the way. To turn the check off,
set `"HomeClearanceCheck": false` in `settings.json`.

Home Grid uses the game's admin teleport, which is checked on the server.
Single-player works. On a server you need admin rights. 

## Hotkeys

Hotkeys work only while a block is equipped. A short message near the top of
the screen confirms each action.

| Default | Action |
| --- | --- |
| `Ctrl+Alt+N` | Set all symmetry planes to CoG (ceNter) |
| `Ctrl+Alt+Shift+N` | Clear all symmetry planes |
| `Ctrl+Alt+I` | Open the Build Tools screen |
| `Ctrl+Alt+O` | Home Grid by CoG (Origin) |
| `Ctrl+Alt+Shift+O` | Return the homed grid |
| `Ctrl+Shift+N` | Set all symmetry planes to CoR (Center of Room) |

Vanilla reacts to its own keys even while Ctrl+Alt is held. For example, `G` opens the block picker. The defaults therefore use keys that vanilla gameplay leaves unbound (`I`, `N` and `O`). Pick unbound keys when customizing. Modifiers must match exactly. To change a hotkey, close SE2 and edit
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
- [ ] Planes persist after save/reload. (A homed grid saved without errors.)
- [x] `Ctrl+Alt+N` with a block equipped sets the planes and shows a message that doesn't steal mouse or keyboard input. (v0.1.2 with `G` set the planes but also opened the block picker.)
- [x] Hotkeys do nothing without a block equipped, in chat, or in menus.
- [x] Vanilla does not react to `N` or `I` while Ctrl+Alt is held.
- [x] Home (CoG): grid lands centered on 0,0,0, square to the world axes; you arrive beside it as before.
- [x] Home with a rotor/piston/hinge subgrid: the subgrid moves too and stays attached (no snapping or explosion).
- [x] Home with a ship docked by connector: both move together.
- [x] Home (CoM): the pivot is the center of mass. Compare with the game's CoM indicator if one is visible.
- [x] Moving grid (and moving player): Home stops everything, then homes; group arrives at rest.
- [x] Clearance: with another grid at the origin, Home refuses and names it; nothing moves.
- [x] Return restores the grid and you.
- [x] CoR in a closed room: planes centered on the room, not the ship.
- [x] CoR in an L-shaped room / with a doorway open to a corridor: planes at the combined bounding box center.
- [x] Corridor → CoR in a long corridor: only the two cross axes change.
- [x] CoR with a hole to space, or standing outside: refuses, nothing changes.
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
