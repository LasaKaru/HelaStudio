# Fixture websites

Three controlled sites the shells are pointed at during testing, so that a shell
bug is never confused with a website bug.

| Site     | What it exercises                                                    |
| -------- | -------------------------------------------------------------------- |
| `simple` | Static multi-page navigation, external links, obvious visual markers |
| `spa`    | Client-side routing, long scroll, file input, a deliberate JS error  |
| `auth`   | Cookie session, protected page, logout, a mock OAuth redirect chain  |

Each serves `/health` returning a build id, so a test can assert which version it
reached.

Deploy each to Cloudflare Pages under its own subdomain — see
`docs/ops/provisioning.md`. Run one locally with:

```
pnpm --filter @shellwright/fixture-simple dev
```
