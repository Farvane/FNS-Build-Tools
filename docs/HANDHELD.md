# Handheld Build Tool and radial menu — design write-up

Status: **design, not built.** Written 2026-11-18, after the Smart Naming merge
(v0.8.0). Nothing here has run in-game.

Written for: the Farvane developer who will build the radial prototype and the
handheld spike. Drafted on Haiku 5.5; the model routing file puts design work on
Opus 5.5, so review this with an Opus pass before committing to the approach.

## 1. Goal

One tool for all build helpers, operated through a radial menu instead of a
separate chord for each action. Today Build Tools has about 20 chords, and the
free keys are nearly used up (only I, N and O were free letters at SE2
4.0.0.3169, see [DESIGN.md](DESIGN.md) "Controls"). Naming now lives in the same
plugin (v0.8.0), so one tool can hold symmetry, measuring, home/view, nudge,
naming and checks.

Success means: a player can reach every current action without memorising a
chord, and a power user can still use chords.

## 2. What the handheld is (option B)

A Farvane hand tool that sits in the character's hand like the vanilla tools.
Using it (primary action) opens the radial menu. Equipping it does not change
any block placement.

Fallback order, so the radial work does not wait on the handheld:

- **A. Mode on the block placer.** The radial opens while any block is equipped,
  using the same hooks the plugin uses today (`BlockPlacerActive` for "the
  player is building"). No new item. This is the first build target.
- **B. Real hand tool.** A dedicated item with its own icon in the toolbar. Only
  after the spike in section 5 succeeds.
- **C. Physical tool with inventory and power.** Out of scope.

The radial code does not depend on the trigger. Only the "open" call does, so A
can ship first and B can replace the trigger later.

## 3. SDK evidence (discovery only, unverified)

From the local VRAGE3 index (`scripts/query-vrage3.ps1`). Per
[AGENTS.md](../../dev-environment/AGENTS.md), these are discovery evidence, not
a supported mod API:

- `Keen.Game2.Simulation.WorldObjects.Tools.IntegrityHandToolComponent`: "Implementation
  of usable as hand tool." (Game2.Simulation)
- `Keen.Game2.Simulation.WorldObjects.Tools.IntegrityHandToolDefinition` (same assembly).
- `BlackboardConstants.StringKeys.EquippedHandTool` and `EquippedHandToolState`,
  `BoolKeys.EquippedHandToolActive`, `EquippingHandTool`, `DequippingHandTool`
  (animation state for an equipped hand tool).
- `Keen.Game2.Client.UI.HUD.Toolbar.ToolbarTiles.VoxelHandToolbarTileViewModel`:
  a toolbar tile for hand tools, so the toolbar has a hand-tool concept.
- Vanilla content: `CharacterTools/HandDrills/DrillMk1/Items/DrillMk1Item/HandHeld/`
  holds the drill's `.def` files (`HandDrill.def`, `HandDrill_LOD.def`,
  `HandDrill_LOD3.def`), which give a hand tool a model with LODs and textures.

What the index does **not** show: whether a mod can add a new hand-tool
definition, how its behaviour is wired (a component that the plugin provides, or
only game-defined ones), and whether the toolbar accepts a plugin-defined tool.
The index has no prefab evidence for these, and the GUID links are not a schema.

## 4. Radial menu design

### Layout

Ring 1 (six sectors, clockwise from top):

| Sector | Contents |
| --- | --- |
| Symmetry | CoG (all / one axis), CoR, Corridor → CoR, Clear planes |
| Measure | Mark A/B, Clear marks, Midpoint → symmetry, Clear width, Centerline readout |
| Grid | Home (CoG / CoM), Return, Thrust balance, Grid info |
| Nudge & Presets | Nudge facing ±½ / ±1 cell, Save / Apply / Delete preset |
| Name | Start / stop selection, Open naming window, Apply last template |
| Check & View | Asymmetry finder (toggle), axis views, Back from view |

Each sector opens a ring of 3–6 items when it has sub-options. Sub-rings can be
context-sensitive: Return only appears after a Home, the nudge sector only when
a plane is active, and Mark/Measure only when a block is aimed.

### Mapping from today's chords

Every current chord maps to a radial item, so nothing is lost:

| Current chord | Radial item |
| --- | --- |
| `Ctrl+Alt+N` / `Ctrl+Alt+Shift+N` | Symmetry → All axes CoG / Clear planes |
| `Ctrl+Shift+N` | Symmetry → CoR |
| `Ctrl+Alt+O` / `Ctrl+Alt+Shift+O` | Grid → Home / Return |
| `Ctrl+Alt+;` / `Ctrl+Alt+Shift+;` | Measure → Mark A/B / Clear marks |
| `Ctrl+Alt+'` | Measure → Midpoint → symmetry |
| `Ctrl+Alt+/` / `Ctrl+Alt+Shift+/` | Measure → Clear width / Centerline readout |
| `Ctrl+Alt+\` | Check → Asymmetry finder |
| `Ctrl+Alt+=` / `-` (and Shift) | Nudge & Presets → nudge (½ or 1 cell) |
| `Ctrl+Alt+I` | Opens the Tools screen (kept as the expert entry) |
| `Ctrl+Alt+Shift+I` | Name → Open naming window (kept) |
| `F` (selection) | Name → Start / stop selection (F stays a direct toggle while selecting) |

Chords stay as the expert layer. Nothing in the radial replaces a chord that
users already rely on.

### Input (to decide and test)

- **Open:** hold a key (a candidate is a non-letter key such as Insert or F3,
  not a vanilla bind). Alternative: middle mouse. Check SE2's keybinds first;
  vanilla may already use the key, and the existing `Hotkey` parser only allows
  Ctrl or Alt chords, so this needs a separate path.
- **Choose:** mouse direction from the ring centre while the cursor is shown, or
  the scroll wheel to step through sectors. The scroll-wheel path is the safer
  first build because it needs no cursor geometry.
- **Confirm:** release the open key (hold-to-open), or click.
- **Cancel:** Escape, or a click outside the ring.
- **Last-used:** tapping the open key with no movement repeats the last action.

### Drawing

Start with an Avalonia overlay, using the same `ScreenView` pattern as
`ToolsScreen`. It must not be created from the plugin constructor (see the
startup rule in [DESIGN.md](DESIGN.md)). The Build Tools screen runs on the UI
thread, and the radial should too, so no game-thread work is needed for drawing.

## 5. Handheld spike (option B) — what to check, in order

Each step needs a game session, so none of it runs from a delegated agent (see
model routing).

1. **S1: Can a mod define a hand tool at all?** Read
   `IntegrityHandToolDefinition` and the drill `.def` files in the SDK. Check
   whether a Farvane `.def` with no new model can reference an existing tool
   type. Decide with the Mod Editor, not by hand-writing `.def` files (the
   project rule: the Mod Editor owns `.meta` files and AssetIDs).
2. **S2: Does the toolbar accept it?** Install a test definition in a Mod Hub
   test project (not a shipped mod), equip it, and check the toolbar slot.
3. **S3: Primary action.** Confirm the tool can run plugin code on its primary
   action. If it cannot, the handheld only opens the radial through the block
   placer, which is option A.
4. **S4: Model and icon.** Only if S1–S3 pass. Blender export with
   `blender_export(dry_run=True)` first, then `asset_validate(deep=True)`, then a
   Mod Editor import.

Stop at the first failed step and record the result here. Option A still
ships without it.

### S1 result (2026-11-18): blocked

- The SDK has the types (`IntegrityHandToolComponent`, `IntegrityHandToolDefinition`,
  signal types such as `RequestActivatePrimaryActionSignal`), but they are
  game-defined. No `IntegrityHandTool` string appears in any `.def` file.
- `sdk_examples` lists five example mods (Armor, CharacterModel, Ice Voxel,
  Modded Ion Thruster, Skybox). None is a hand tool.
- Nothing in the SDK shows a mod-defined hand tool, or plugin code running on
  a hand tool's primary action. S2 and S3 cannot start from this evidence.

Decision: option A (radial on the block placer) ships first. Option B stays open
until someone finds a supported route, for example from a Mod Editor template.

## 6. Naming inside the tool (later, 0.9 candidates)

These use geometry Build Tools already computes:

- **Side tokens from the centerline.** The centerline offset (Measure) gives each
  block's signed distance from the active plane, so PORT/STBD can be filled in
  automatically. Blocks on the plane get no side token.
- **Name-aware asymmetry.** The asymmetry finder can also report a `PORT-…-01`
  with no `STBD-…-01` mirror.
- **Room token from CoR.** `RoomFill` already knows the enclosed room the player
  is standing in, so a room can keep its own default location token.
- **Numbering by position.** Sort along an axis (front to back, then top to
  bottom) instead of by old name. Today's order is alphabetical by name, which
  stays the default until tested.

## 7. Risks

- **Two keyboard pollers.** Hotkeys and naming each run a 30 ms `DispatcherTimer`.
  This is correct today, but the radial should use one shared input poller
  rather than adding a third.
- **Input conflicts** with vanilla (see section 4).
- **Multiplayer.** Naming and the radial are client-side. The client may not set
  `FunctionalBlockComponent.DisplayName` (a sync property: "Sync property was
  disabled by ISignalTableBuilder"), and the vanilla Control Panel route only
  works while the panel is open (closing it detaches its models and drops their
  secure `EntityId`s). `ServerRename` pairs each F-selected client replica with
  its server entity through the in-process `ReplayContext.TranslateLocalToRemote`
  and posts the write with `InProcessReplicationSessionComponent.PostInvoke`,
  which runs on the in-process server. Both types are internal to
  `VRage.Multiplayer` and reached by name, so a game update can disable it (it
  then fails with a clear message). Single-player and host only; a multiplayer
  client gets an error. MP is untested.
- **Merge regressions.** Smart Naming's selection was verified in-game as a
  separate plugin. The merged code compiles, but its in-game test is still pending.

## 8. Validation checklist

In-game pass on 2026-11-18, SE2 4.0.0.3169, Pulsar Modern 2.4.3, single-player,
no exceptions in the game log.

- [x] Radial menu (v0.9.0): opens with Ctrl+Alt+F3 while building; each sector
      and item fires its action; wedges highlight under the pointer; the hub goes
      back a ring and closes from the top ring; nothing leaks into vanilla.
- [x] Radial menu: the Studio lighting item toggles FNS Studio Lighting's setting
      off and on (logged as "Disabled in settings", then re-armed).
- [ ] Radial menu: Studio lighting toggle seen lighting change in the Design Range
      (the test world had no Design Range skybox, so lighting stayed inactive).
- [ ] Radial menu: the Studio lighting item is hidden when FNS Studio Lighting is
      not installed.
- [x] v0.8.0 naming, single-player: selection mode, F selects and deselects the
      aimed block.
- [x] v0.8.0 naming: rename applied (2026-11-19, single-player): 13 F-selected
      CIC lights renamed CIC-LT-INT-001 to -013 with no Control Panel open; log
      shows "Server renamed 13 of 13" on the in-process server thread and
      "Rename check: 13 of 13" on the client.
- [x] v0.8.0 naming, Control Panel selection (2026-11-19, single-player): two
      lights selected in the panel, panel closed, renamed from the naming window;
      log shows "Server renamed 2 of 2" and "Rename check: 2 of 2". The selection
      is resolved to server entities on each panel selection change (the panel's
      ids die when it closes) and renamed through the same server path.
- [ ] v0.8.0: Ctrl+Alt+Shift+I opens naming without a block equipped (as Smart
      Naming did).
- [ ] Spike S1–S3 recorded here with the SE2 build number (S1 blocked, see
      section 5).

## Verification of this write-up

- The Build Tools unit tests pass (70, including the ported naming and radial geometry tests).
- `FNSBuildTools.csproj` and `FNSSmartNaming.csproj` compile against the SE2
  4.0.0.3169 assemblies in `reference/se2-game/Game2` with 0 warnings.
- Deployed locally to Pulsar Modern and run in-game on 2026-11-18 (see section 8).
  Nothing has been published.
