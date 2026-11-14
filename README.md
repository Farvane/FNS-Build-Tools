# \[FNS\] GRIDWORX - Build Tools

**Status: planning.** No plugin code yet. See [docs/DESIGN.md](docs/DESIGN.md).

A planned Pulsar Modern client plugin for Space Engineers 2 with precision
construction helpers:

- **Home Grid**: move the aimed grid to world origin, square to the world
  axes, and carry the player along with it.
- **Symmetry to CoG**: set the X, Y and Z symmetry planes to the Center of
  Grid in one action.
- **Symmetry to CoR**: set symmetry planes to the center of the room the player
  is standing in.
- **Measure**: block-to-block span, midpoint and clear-width readouts.

Like the other GRIDWORX companions, it will follow the
[FNS Studio Lighting](https://github.com/Farvane/FNS-Studio-Lighting) layout:
`ClientPlugin/` source, a Pulsar `FNSBuildTools.xml`, and a local `build.ps1`
against the installed SE2 `Game2` assemblies. Source is MIT licensed.

This is a community plugin, not an official Keen Software House product.
