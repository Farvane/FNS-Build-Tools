# \[FNS\] GRIDWORX - Build Tools

**v0.9.0 preview.** Symmetry to CoG and Home Grid are verified in-game
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

v0.5 adds **measuring tools**: Tape between two blocks, Midpoint → symmetry,
clear width where you stand, and a live centerline offset readout.

v0.6 adds a live **asymmetry finder**.

v0.7 adds a **grid info panel**, **plane nudge and presets**, **axis views**
and **thrust balance**. That completes the original design backlog. See [docs/DESIGN.md](docs/DESIGN.md).

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

## Grid info, nudge, presets, views and thrust (screen)

Open the Build Tools screen (`Ctrl+Alt+I`) while looking at a grid:

- **Grid info:** size in meters, block count, attached grids, and mass. It
  also shows the center-of-mass offset from the CoG on each axis, which is
  handy for spotting a heavy side.
- **Nudge:** moves the symmetry plane of the grid axis you are looking along by
  ½ cell (0.125 m) or a whole cell. + is away from you. Hotkeys work without
  the screen.
- **Plane presets:** type a name and **Save current planes**. **Apply** puts
  that set back on the target grid, and an axis that was off is cleared.
  Presets live in `settings.json` and work across grids and worlds.
- **Axis views:** Front, Back, Left, Right, Top or Bottom moves you to look at
  the grid's center from that side, square to its axes, far enough away to
  frame it. This is for reference shots in the Design Range. **Back from
  view** returns you to where you were before the first view. It uses the
  admin teleport, like Home Grid, and works best in zero gravity.
- **Thrust balance:** for each thrust direction it lists rated thrust and how
  far the center of thrust sits off the center of mass, plus the torque that
  causes. Under 5 cm reads "balanced". It covers this grid only (not
  subgrids), and direction names are the game's own thrust directions.

## Naming (merged from Smart Naming)

v0.8.0 folds **FNS Smart Naming** into this plugin. Smart Naming is deprecated
and does nothing on its own; remove it from your Pulsar profile.

1. Select blocks: equip any block, aim at a built functional block, and press
   **F** to add or remove its cyan outline. Or select blocks in Terminal →
   Control Panel.
2. Press `Ctrl+Alt+Shift+I` (or **Open naming window** on the Build Tools screen).
3. Pick a template or type location, system and function, then **Refresh
   preview** and **Apply names**. Numbering is alphabetical by current name.

First-person selection closes the Build Tools screen first, so the aim and F
are free. Applying names or unloading the plugin stops selection and clears the
highlights. Naming is independent of the symmetry hooks, so it still works when
symmetry is unsupported in a build.

## Radial menu (v0.9.0)

Press `Ctrl+Alt+F3` with a block equipped to open the radial menu. Seven sectors
sit on the outer ring: Symmetry, Measure, Grid, Nudge, Name, Check and View.
Point at a wedge to highlight it and click to pick it; its actions appear on an
inner ring. Click the hub to go back a ring, or to close the menu from the top ring. Each action closes the menu first, then shows its
result as a toast.

**Studio lighting** appears in the View sector only when FNS Studio Lighting is
installed and running. It toggles that plugin's studio lighting on or off.

Verified in-game (single-player). Hold-to-open and Escape to close are not
built yet.

## Asymmetry finder

With symmetry planes set and a block equipped, look at the grid and press
`Ctrl+Alt+\`. For every active plane, each block must have a block of the
same size at its mirrored position:

- **Red:** no mirror. A dim red ghost outline shows where the mirror belongs.
- **Orange:** a different block type sits in the mirrored spot.
- **Yellow:** same block, different paint.

Blocks that straddle a plane count as their own mirror. The finder is live. It
rescans when blocks change, when planes change, and every 3 s (which picks up
paint fixes). Press `Ctrl+Alt+\` again to turn it off. The tools screen shows
the counts. Up to 300 outlines are drawn. Mirrored *orientation* is not
checked yet, because vanilla's mirror rotations differ per block.

## Measure

With a block equipped:

- **Tape:** look at a block and press `Ctrl+Alt+;` to mark **A** (green). Look
  at another block on the same grid and press it again to mark **B** (blue). A
  line joins their centers. The label shows the center-to-center distance and
  the outer size, face to face, on each axis. A third press starts a new
  measurement. `Ctrl+Alt+Shift+;` clears the marks.
- **Midpoint → symmetry:** `Ctrl+Alt+'` puts symmetry planes halfway between A
  and B, on every axis where they differ.
- **Clear width:** `Ctrl+Alt+/` measures the free space through where you stand
  along X, Y and Z, up to 100 m each way, and draws it for 10 seconds. "Open"
  means no wall was found on one side.
- **Centerline readout:** `Ctrl+Alt+Shift+/` toggles a label above the aimed
  block showing its signed offset from each active symmetry plane.

Overlays draw only while a block is equipped, because they use the block
placer's own 3D overlay, the same one that draws vanilla symmetry planes.

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
| `Ctrl+Alt+;` | Measure: mark the aimed block A, then B |
| `Ctrl+Alt+Shift+;` | Measure: clear marks |
| `Ctrl+Alt+'` | Symmetry planes at the A↔B midpoint |
| `Ctrl+Alt+/` | Clear width where you stand |
| `Ctrl+Alt+Shift+/` | Toggle the centerline offset readout |
| `Ctrl+Alt+\` | Toggle the live asymmetry finder |
| `Ctrl+Alt+=` / `Ctrl+Alt+-` | Nudge the plane you are facing ½ cell away / toward you |
| `Ctrl+Alt+Shift+=` / `Ctrl+Alt+Shift+-` | Nudge a whole cell |

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
- [x] Tape: A/B outlines, line and label appear; distances match counted blocks (0.5 m and 2.5 m blocks).
- [x] Midpoint → symmetry: plane lands halfway; mirrored placement from A lands on B.
- [x] Clear width: lines and labels in a corridor match its width/height; "open" when facing space.
- [x] Centerline readout: offsets read 0.00 on the plane and ± either side.
- [x] `;` `'` `/` with Ctrl+Alt do not trigger anything vanilla.
- [ ] Asymmetry: a symmetric ship reports "every block has its mirror".
- [ ] Remove one block: its mirror turns red with a ghost where it was; re-adding it clears both.
- [ ] Swap a block type / repaint one side: orange / yellow outlines appear and clear when fixed.
- [ ] Grid info: size/blocks match; mass and CoM offset appear on a ship, "static grid" on a station.
- [ ] Nudge: facing each axis moves that plane ½ / 1 cell the expected way; facing an axis with no plane refuses.
- [ ] Presets: save, apply to another grid, delete; survives a game restart.
- [ ] Views: each of the six views frames the ship from the right side; Back from view returns you.
- [ ] Thrust balance: a symmetric thruster layout reads balanced; adding one off-center thruster shows an offset.
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
