# API client

Types for the control plane, generated from its own OpenAPI document.

Nothing here is written by hand. `openapi/v1.json` is emitted by the API from
its route table, and `src/generated/v1.ts` is emitted from that:

```
pnpm --filter @shellwright/api-client generate:openapi   # API  -> openapi/v1.json
pnpm --filter @shellwright/api-client generate           # JSON -> src/generated/v1.ts
```

⚠️ **Both outputs are committed, and CI fails if either is stale.** The point is
not to save a build step — it is that a change to an endpoint and the client's
view of that endpoint land in the same commit, where a reviewer sees them
together. A generated client that is regenerated at build time drifts silently
between releases and only disagrees with the server in production.
