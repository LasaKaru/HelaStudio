# JavaScript bridge SDK

Empty until **Sprint 09**.

The `@shellwright/bridge` npm package: a promise-based API, TypeScript types, and
capability negotiation, so a website can call native code without knowing which
shell version it is running inside.

The bridge exists three times — TypeScript, Kotlin, Swift — and they must agree
exactly. `packages/bridge-protocol/fixtures/*.json` will hold one fixture set
that all three implementations run against, the same technique that keeps the two
config validators in step today.

⚠️ Adding a bridge method requires adding its fixtures first. No fixtures, no
merge.
