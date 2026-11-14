# GRIDWORX Build Tools — design brainstorm

Working notes, not a commitment. Each "SDK evidence" item comes from the
local VRAGE3 index (`scripts/query-vrage3.ps1` in dev-environment). That is
discovery evidence only. Confirm each one against the installed SE2 build
before relying on it.

## Decisions (2026-11-14)

- **CoG** = the center of the grid's block bounding box. **CoM** is offered as
  a separate pivot option.
- **Square** = align exactly to the world axes: grid forward = world forward,
  with an identity rotation. The "nearest 90°" mode is dropped.
- Grids attached by **rotors, pistons and connectors move with** the targeted
  grid as one rigid group.
- All backlog ideas are planned, not optional.

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

- *Orientation*: always identity. Grid forward = world forward.
- *Pivot*: CoG (default), CoM, or the aimed block.
- *Group*: rotor, piston and connector subgrids move with the same delta.
  `CubeGridComponent.GetAllGridsInShip()` is the candidate for gathering them.
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

### Home Grid implementation notes (v0.2.0, SE2 4.0.0.3169)

- `EntityAdmin.TeleportEntity` carries `[Server("ValidateAdminAccess")]`. A
  client call becomes a server-run, admin-checked request. It calls
  `EntityTransformFunctions.Teleport`, which sets the transform plus the
  `Teleported` flag and does not clear velocity, so v0.2 refuses moving grids.
- Group = `CubeGridComponent.GetAllGridsInShip()`, which is the **Terminal**
  connection group (`a5bd777b-…`). In vanilla content its users are connectors,
  hinges, pistons and rotors. **Physical** (`262d50d2-…`) is broader; blueprint
  pasting uses it.
- Conventions come from the IL. A world point is
  `Position + rotate(local, Orientation)`, and
  `GetRelativeTransform(child, parent)` gives
  `(TransformInv(child.Pos, parent), conj(parent.Ori)·child.Ori)`. Vanilla fast
  travel uses `RelativeTransform * WorldTransform`, which takes the parent on the
  **right**. `WorldTransform * WorldTransform` takes the parent on the **left**
  (`Ori = a·b`, `Pos = a.Pos + rotate(b.Pos, a.Ori)`). `GetRelativeTransform`
  returns a `WorldTransform`, so Home Grid composes `anchor * relative`, with
  `new_anchor = (−pivot_local, identity)`. Before sending, a self-check
  confirms `old_main * rel ≈ old` (position and orientation), and aborts if not.
  v0.2.0 had the operands swapped. The self-check caught it in-game and
  nothing moved.
- Vanilla fast travel teleports the whole group inside one `Scene.MoveToDCS`
  phase. Home Grid instead sends one request per entity in the same frame, in
  order: main grid, then the other grids, then the character. Whether the
  server applies them all before a physics step is the main in-game test.
- **No C# `async`.** Game2.Client defines its own `AsyncVoidMethodBuilder`
  (compiler warning CS1685), and `NetworkStory.Task` is
  `Keen.VRage.Library.Threading.Task`. Results are polled on the UI thread
  with `IsCompleted` and `GetAwaiter().GetExceptionResult()`.
- CoM = `RigidBodyMassProperties.CenterOfMass`, assumed to be grid-local. This
  is verified in-game.

## 2. Symmetry to CoG

One action sets all three planes, or one axis via a modifier, to the CoG.

SDK evidence: `BuildingInfoComponent.AddSymmetryPlane(SymmetryPlaneAxis, int)`
and `RemoveSymmetryPlaneByAxis(SymmetryPlaneAxis)` in
`Game2.Simulation.WorldObjects.CubeGrids`. The object builder notes that
`int32.MaxValue` = a disabled plane. The planes are grid data, so they are
probably saved and replicated with the grid.

**Resolved from metadata and IL (SE2 4.0.0.3169):**

- A grid cell is 0.25 m (`CubeGridCoords.GridToLocal` = `cell · 0.25`). Grid
  boxes are max-inclusive.
- The plane `distance` is in **doubled-cell** units. `ReflectPoint` maps
  `p → 2·distance − p`. So half-cell planes are representable, and both
  odd and even spans center exactly.
- The vanilla setup places a box-centered plane at
  `2·Min + GetPlaneOffsetFromBlock(size, 0)` = `2·Min + size − 1` = `Min + Max`.
  **CoG distance per axis = `Min + Max`** of the union of block AABBs.
- `BuildingInfoComponent.AddSymmetryPlane` is a signal with
  replication-forwarding generated code. Vanilla
  `BlockPlacementSymmetrySetup.PlaceCurrentAxis` calls it through
  `PlacementTarget.AddSymmetryPlane`, and v0.1 calls the same method on
  `GridPlacementTarget`.

Still to verify in-game: calling it from a UI button behaves like vanilla's
build-input flow, in both single-player and multiplayer.

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
| v0.1 | Symmetry to CoG (all axes or one), grid info readout, single-player. **Built; in-game test pending.** |
| v0.1.1 | Hotkeys with a click-through confirmation message; configurable in `settings.json`. **Built; in-game test pending.** |
| v0.2.1 | Home Grid (CoG/CoM) + Return, moving the Terminal group and the player. **Verified in-game (single-player):** CoG, CoM, rotor/piston/hinge subgrids, connector-docked ship, Return. |
| v0.2.2 | Stop-then-home for moving grids/player (`IGameServer.StopEntityMovement`, in-process server only) and an origin clearance check (`GridIdSessionComponent` registry, world AABB overlap + 0.5 m). **Built; in-game test pending.** |
| v0.3 | Tape, Midpoint, Clear width |
| v0.4 | Symmetry to CoR with Corridor mode |
| later | Subgrids for Home, asymmetry finder, camera views, MP/admin validation |

v0.1 comes first because it is the lowest-risk proof that the grid symmetry
API can be driven from a client plugin. Home Grid moves entities, which is the
riskiest part.

## Controls

Implemented in v0.1.1. A Harmony postfix on `BlockPlacerEntityComponent.BlockPlacerActive`,
a per-frame job, records only a timestamp. A UI-thread timer polls
`InputDeviceManager.Keyboard` every 30 ms, then fires an action on the press
edge, but only if the placer ran in the last 250 ms. Actions run on the UI
thread, like the screen's buttons. The defaults use `Ctrl+Alt` and still need
checking against SE2's keybinds.

**Startup rule (precaution):** never touch Avalonia, including
`Dispatcher.UIThread` and `DispatcherTimer`, from the plugin constructor.
`Dispatcher.UIThread` is created on first access. Before the game registers
its dispatcher, that first access falls back to a `NullDispatcherImpl` (seen
in the Avalonia IL). v0.1.2 starts the hotkey timer from the block placer's
`Init` hook. **Correction:** this was first blamed for the v0.1.1 boot freeze.
A later freeze (v0.2.0, with no constructor Avalonia access) has the
**identical** stack, and its dump shows no plugin, Harmony or Avalonia frames.
The render thread is waiting in `SwapChain.Resize` →
`FlushAllQueuesAndWaitCpu` for a GPU fence that never signals, and the main
thread waits on it in `FrameData.UpdateData.OverflowGate`. So the boot freeze
is an intermittent render/GPU hang at the main menu, and its link to the plugin
is unproven.

Planned chords:

Defaults use only keys that vanilla gameplay leaves unbound, because vanilla
matches its key binds whatever modifiers are held. As of SE2 4.0.0.3169 the
only free letters are I, N and O. Further tools will need non-letter keys
(for example Insert, F3, F10, OemPlus or OemMinus) or the screen.

- `Ctrl+Alt+N` Symmetry to CoG · `Ctrl+Alt+Shift+N` clear
- `Ctrl+Alt+O` Home Grid · `Ctrl+Alt+Shift+O` Return
- `Ctrl+Alt+I` open Build Tools
