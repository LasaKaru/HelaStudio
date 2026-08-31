# 4. A three-way build cache key

Date: 2026-08-31

## Status

Accepted

## Context

iOS binaries can only be compiled on Apple hardware, and that hardware is the
largest cost in the business. A full iOS build is roughly eight minutes of
metered macOS time. Android is cheaper but not free.

Most builds are not interesting. A user changes an accent colour, renames a tab,
or points the app at a different start URL, and then rebuilds. Under a single
whole-document cache key, every one of those triggers a full recompile, and the
unit economics do not work.

## Decision

The cache key is split three ways, by what a change actually costs to apply:

| Key          | Covers                                                                                                      | Cost when only this changes        |
| ------------ | ----------------------------------------------------------------------------------------------------------- | ---------------------------------- |
| `codeKey`    | plugins, permissions, bundle id, deep links, native surface types, build settings, shell version, toolchain | Full recompile                     |
| `assetKey`   | app name, branding, navigation labels and icons                                                             | Repackage resources                |
| `contentKey` | start URL, allowed origins, navigation structure, link rules, web overrides, offline, OTA                   | Patch the embedded config, re-sign |

Each key is BLAKE3 over the canonical JSON of a projection of the resolved
configuration. BLAKE3 because it is fast and this is a cache key, not a security
boundary.

Navigation is deliberately split across two keys: an item's label and icon are
resources, its URL and active pattern are content. Moving a tab changes the
asset key, because order is part of the generated resource.

## Consequences

- Roughly 70–80% of user-triggered builds become an asset-only or content-only
  path. A colour change goes from about eight minutes to about forty seconds.
- Canonical JSON becomes load-bearing. If two documents that mean the same thing
  do not produce identical bytes, the cache never hits and the whole argument
  collapses. It is property-tested for order-independence over a thousand
  generated cases, and asserted byte-for-byte against the C# implementation.
- The projections are duplicated in two languages and must agree. The
  cross-language contract test asserts all three keys for every fixture.
- Caches are keyed per app, never shared across tenants. A shared mutable build
  cache is both a correctness hazard and a security hole
  (`01_ENGINEERING_STANDARDS.md` §10).
- The split is a claim about what the build system can patch without
  recompiling. Sprint 08 has to make the resource-patch and config-patch paths
  real; until then the keys are computed and stored but only `codeKey` is acted
  upon.
