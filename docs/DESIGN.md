# GRIDWORX Build Tools — design brainstorm

Working notes, not a commitment. Each "SDK evidence" item comes from the
local VRAGE3 index (`scripts/query-vrage3.ps1` in dev-environment). That is
discovery evidence only. Confirm each one against the installed SE2 build
before relying on it.

## Terms

| Term | Meaning |
| --- | --- |
| **CoG** (Center of Grid) | Center of the grid's occupied block bounding box, in grid cells. Geometric, not mass-based. |
| **CoM** (Center of Mass) | Physics center of mass. Offered separately because it matters for thrust balance, not symmetry. |
| **CoR** (Center of Room) | Center of the enclosed empty volume containing the player's cell. |
| **Span** | Inclusive block count between two cells on one axis: `abs(b - a) + 1`. |

An **odd** span has a center *cell*. An **even** span has a center *boundary*
between two cells. Every centering feature has to handle both cases, and the
UI should say which one applies ("center: block 7" vs "center: between 6 | 7").

## 1. Home Grid (origin and square)

Aim at a grid, press the hotkey:

1. Compute the target transform: CoG at world `0,0,0`, grid axes aligned to
   world axes.
2. Compute the delta `D = target * inverse(current)`.
3. Apply `D` to the grid **and** to the player character (and to any connected
   mechanical subgrids, later), so the player keeps the same relative pose.
   Zero the linear and angular velocity.
4. Store the previous transform so **Return** can undo it.

Options:

- *Square mode*: `Identity` (grid forward = world forward) or `Nearest 90°`
  (snap to the closest axis-aligned rotation, which keeps the grid's rough
  facing).
- *Pivot*: CoG (default), CoM, or the aimed block.
- *Clearance check*: refuse, or offset along +Y, if another entity's bounds
  overlap the destination.

SDK evidence: `EntityAdmin.TeleportEntity(SkipOnReplicationFail<Entity>, WorldTransform)`
in `Game2.Simulation` ("Teleports the entity to a provided location").
`Teleported` / `MotionStateFlags.Teleported` mark discontinuous moves for
replication. These are admin tools, so expect v1 to need single-player,
creative or host-admin.

Risks: subgrids (rotors, pistons, connectors) must move with the same delta or
they tear off. Static grids may refuse transforms. Planet gravity makes the
origin a bad place on planets, so this is aimed at the Design Range or empty
space.

## 2. Symmetry to CoG

One action sets all three planes, or one axis via a modifier, to the CoG.

SDK evidence: `BuildingInfoComponent.AddSymmetryPlane(SymmetryPlaneAxis, int)`
and `RemoveSymmetryPlaneByAxis(SymmetryPlaneAxis)` in
`Game2.Simulation.WorldObjects.CubeGrids`. The object builder notes that
`int32.MaxValue` = a disabled plane. The planes are grid data, so they are
probably saved and replicated with the grid.

**Open question:** the plane position is an `int`. Find out whether it is a
cell index, which can only express odd-span centers, or a half-cell index,
which can express both. SE1 used a cell index plus an "odd" flag. This decides
whether even-span centering is possible. Test it by placing planes by hand and
reading the value back.

Also verify whether calling it locally replicates, or whether the client must
go through the same request path as the block placer
(`BlockPlacementSymmetrySetup`).

## 3. Symmetry to CoR (expansion)

1. Convert the player's position to a grid cell.
2. Flood-fill (6-connected) the empty cells from there. Occupied cells are
   walls.
3. Stop if the fill reaches the grid's bounding box (the room is not enclosed)
   or exceeds a cell budget, e.g. 50,000 cells. Report "room not enclosed".
4. Take the CoR as the center of the filled region's bounding box. Optionally
   offer the volume centroid for L-shaped rooms.
5. Set the planes, or only the axes the user picks. *Corridor mode* sets only
   the two axes across the corridor.

Decisions: in v1, any occupied cell (doors, windows, catwalks) counts as a
wall. Later options: treat open doors as walls, and ignore decorative
non-full blocks. Airtightness and pressurization did not appear in the SDK
index, so this does not depend on them.

## 4. Measure tools

- **Tape (A→B):** mark block A, then block B. Show Δx/Δy/Δz as both spans and
  meters, plus the straight-line distance. Draw a line overlay.
- **Midpoint:** the center of A→B, with odd/even handling. Optionally "set
  symmetry plane here".
- **Clear width:** aim at a face and ray-march along its normal to the next
  occupied cell. Shows interior width, for hallways and hangar doors.
- **Offset from centerline:** the HUD readout for the aimed block, e.g. "3
  blocks port of X plane, 2 up from CoG".

Overlay rendering: `UI3DDefaultRendering.DrawSymmetryPlane` and
`UI3DConfiguration` suggest that a UI3D drawing path exists for lines and
planes. Investigate reusing it.

## 5. Other ideas (backlog)

- **Asymmetry finder:** highlight blocks that have no mirrored counterpart
  across the active planes. This is a strong use case for ship builders.
- **Grid info panel:** size in blocks and meters, block count, mass, and the
  CoM-vs-CoG offset.
- **Plane nudge:** move the active plane ±1 cell or ±½ cell with a hotkey.
  Save and restore plane presets per grid.
- **Axis camera views:** snap the spectator to front, side or top views around
  the CoG. Pairs with Design Range and Studio Lighting for reference shots.
- **Thrust balance:** compare the summed thrust vectors per direction against
  the CoM.

## Phasing

| Version | Scope |
| --- | --- |
| v0.1 | Symmetry to CoG (all axes or one), grid info readout, single-player |
| v0.2 | Home Grid + Return for a single grid; clearance check |
| v0.3 | Tape, Midpoint, Clear width |
| v0.4 | Symmetry to CoR with Corridor mode |
| later | Subgrids for Home, asymmetry finder, camera views, MP/admin validation |

v0.1 comes first because it is the lowest-risk proof that the grid symmetry
API can be driven from a client plugin. Home Grid moves entities, which is the
riskiest part.

## Controls (draft)

Hotkeys configurable in the Pulsar settings screen. Proposed defaults sit on a
single modifier to avoid clashing with vanilla. Each must be checked against
the SE2 keybinds:

- `Ctrl+Alt+H` Home Grid · `Ctrl+Alt+R` Return
- `Ctrl+Alt+G` Symmetry to CoG · `Ctrl+Alt+C` Symmetry to CoR
- `Ctrl+Alt+M` Tape mark A/B · `Ctrl+Alt+W` Clear width
