# 3. `appconfig.json` v1

Date: 2026-08-31

## Status

Accepted

## Context

`appconfig.json` is the single input to the entire platform. A generated Android
project, a generated Xcode project, every build cache key, every form in the
studio, and every stored customer configuration are all derived from it.

This is the hardest one-way door in the programme. Sprint 01 exists to get it
right, and the migration framework exists because it will not be entirely right.

The schema also has an unusual dual audience. It is a machine contract consumed
by two validators and two code generators. It is _also_ the source of the help
text a non-engineer reads in the studio, because the studio renders each field's
`title` and `description` directly.

## Decision

### Versioning: both markers

A `$schema` URL for editor tooling, and an integer `schemaVersion` for migration
logic. The URL gives anyone hand-writing a config autocomplete in their editor at
no cost to us. The integer is what `ConfigMigrator` walks.

### Unknown fields: reject on save, preserve on read

Strict input catches `brandng` before it silently does nothing for a month.
Lenient reading means a config written by a newer studio does not brick an older
shell — the shells parse with unknown keys ignored, and `resolveDefaults`
preserves fields the schema does not model.

### Secrets: forbidden, and actively rejected

Config is stored, hashed, logged, exported, and **embedded in the shipped
binary**, where anyone who downloads the app can read it. A secret here is a
secret published. `CFG_SECRET_IN_CONFIG` is therefore an error, not a warning,
and it pattern-matches known credential shapes as well as suspicious key names.
Credentials live in a separate store and are referenced by id.

### Asset references: content-addressed

`asset://sha256-<hex>` rather than an inline blob or a repository path. This
makes `assetKey` trivial to compute, deduplicates an icon shared across an
agency's twenty apps, and keeps the config small enough to validate on every
keystroke.

### Defaults: in the schema, not in code

One source of truth. The studio renders them, code generation reads the resolved
document, and hashing sees the resolved document. Critically, defaults are
resolved **before** canonicalisation: an omitted field and an explicitly-default
field must hash identically, or a user toggling a value back to its default
would miss the build cache.

### Extensibility: `x-` prefixed objects

An escape hatch that costs nothing. Ignored by code generation, excluded from
every cache key, preserved through migrations.

### Structural rules

1. Nested where it maps to a real subsystem: `branding`, `navigation`,
   `linkRules`, `webOverrides`, `plugins`, `build`.
2. Every field has a default. `minimal.json` is ten lines and produces a working
   app.
3. No field means two things. Anything that would need a string-or-object union
   is split instead.
4. Every array of objects carries a stable `id`, so the studio can track an item
   across a drag-and-drop reorder and a diff can be meaningful.
5. Nothing platform-specific at the top level. `build.ios` and `build.android`
   exist only where behaviour genuinely diverges.

### Constraints are store rules, not taste

The tight patterns are the ones Apple and Google enforce: lowercase reverse-DNS
bundle ids, a version code ceiling of 2,100,000,000, a 30-character app name,
https-only URLs. Rejecting these at config time costs nothing; discovering them
at submission costs a week.

## Consequences

- Two validators must agree exactly. `tests/fixtures/expected/` holds the
  goldens, and the cross-language contract test is what enforces it. Any drift
  fails CI on both sides.
- Validation is pure and allocation-light, because it runs on every keystroke.
  Measured at 0.5 ms for the maximal fixture against a 50 ms budget.
- Because canonicalisation NFC-normalises strings, the C# side cannot run under
  `InvariantGlobalization`. That setting silently makes `String.Normalize` a
  no-op for non-ASCII, which would make a decomposed accent hash differently in
  C# than in TypeScript. Anything consuming `Shellwright.ConfigSchema` must set
  `InvariantGlobalization` to false; the corpus carries a decomposed character
  so the contract test catches a regression.
- The schema will be wrong in places. `ConfigMigrator` exists, is tested, and has
  a proven v0-to-v1 path, so being wrong is survivable rather than terminal.
