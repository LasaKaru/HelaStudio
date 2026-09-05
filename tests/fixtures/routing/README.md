# Routing fixtures

The contract between the Android and iOS link routers.

Both shells read `link-routing.json` and must reach identical decisions for
every case. The two implementations share no code — the behaviour is ported
between Kotlin and Swift, not the source — so this corpus is the only thing
that catches them drifting apart.

It is the same technique that holds the TypeScript and C# config validators
together (`tests/fixtures/expected/`), applied to the second place in the system
where one behaviour has two implementations. Sprint 09 formalises the pattern
again for the JavaScript bridge, which will have three.

## Adding a routing behaviour

Add its cases here **first**, then make both shells pass. A behaviour that
exists on one platform and not the other is exactly what this file is for.

Each case carries a `why` explaining what it protects, because a routing table
without reasons decays into a list nobody dares change.

## Shape

- `ruleSets` — named `linkRules` arrays, so a rule set can be reused across cases
- `cases` — `{ why, rules, url, expect }`, where `expect` is a `LinkAction`
  fixture name: `internal`, `modal`, `readerModal`, `externalBrowser`, `block`,
  `external`, `download`
- `maxMillis` — optional, for cases that assert the router does not hang
