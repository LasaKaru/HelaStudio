# Plugins

Empty until **Sprint 10**.

A plugin is a directory and a manifest, never a change to a shell's core files.
That rule is the whole design: without it, plugin fifteen requires touching code
that plugins one through fourteen also touch, and the combinatorics end the
project.

The manifest declares dependencies, permissions, a config schema, bridge
methods, entitlements, and conflicts. The build system consumes manifests to
generate the native project; the studio renders `configSchema` as a form; the
validator already reads the registry — see
`packages/config-schema/src/plugin-registry.ts`, which holds the eight
descriptors Sprint 01 validates against.

Two rules that are easy to state now and expensive to retrofit:

- ⚠️ **No plugin may initialise at launch.** Plugins register lazily on first
  bridge call. Otherwise fifteen plugins mean fifteen SDK inits and a two-second
  cold start.
- **Every plugin publishes its binary size delta**, shown in the studio next to
  the toggle. Both an optimisation and a piece of honesty nobody else in the
  category offers.
