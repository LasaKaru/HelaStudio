# `@shellwright/api-client`

A TypeScript client generated from the API's own route table.

`openapi/v1.json` is exported from the running application by
`scripts/generate-api-client.sh`, and `src/generated/v1.ts` is generated from
that. Both are committed, and CI fails when either is stale — the point being
that a change to an endpoint and the client's view of it land in the same diff,
where a reviewer sees them together.

## ⚠️ `generate` is cached by turbo, and its inputs must list what it reads

`turbo.json` declares the `generate` task's inputs. This package generates from
`openapi/**`; `@shellwright/config-schema` generates from `schema/**`. Both
directories have to be listed, and the failure when one is missing is
particularly unpleasant:

turbo does not re-run a task whose declared inputs have not changed — it
restores the previous output from cache. So with `openapi/**` unlisted, editing
`openapi/v1.json` did not invalidate anything, and the next command that
depended on `generate` (`pnpm test`, among others) **overwrote the freshly
generated `src/generated/v1.ts` with a stale cached copy**, silently deleting
324 lines' worth of endpoints from a file nobody edits by hand.

It was caught by CI's stale-client check rather than by anybody noticing, which
is exactly what that check is for. If a generated file ever loses content for no
reason you can see, look here first.

## Regenerating

```bash
bash scripts/generate-api-client.sh
```

The exporter starts the real host to materialise the route table, so it needs
configuration to be present — it never opens a connection or signs a token. The
script supplies throwaway values for everything the host validates at startup.
