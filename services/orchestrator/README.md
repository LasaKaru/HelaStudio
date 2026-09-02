# Build orchestration

Temporal workflows driving Linux (and, later, macOS) runners. Retries,
cancellation, and log streaming are the reasons for Temporal rather than a
queue.

Two rules from `01_ENGINEERING_STANDARDS.md` shape this service:

- Every build runs in a fresh, isolated environment, destroyed after. No reuse
  across tenants, ever.
- A cancelled build must free its runner within five seconds. On a metered Mac
  fleet, an uncancellable build is money on fire.

## Running it locally

Three dependencies, each with a script that starts one and prints the
connection string to export:

```bash
eval "$(scripts/dev-temporal.sh)"   # Temporal dev server
eval "$(scripts/dev-redis.sh)"      # Redis, for live log streaming
eval "$(scripts/dev-postgres.sh)"   # Postgres, for the API
dotnet run --project services/orchestrator/Shellwright.Orchestrator
```

The tests start these themselves when the corresponding environment variable is
absent, so `dotnet test` works on a clean checkout with nothing exported.

## Build logs

A build's output goes to two places at once, and they fail independently.

|             | Archive                            | Live stream                       |
| ----------- | ---------------------------------- | --------------------------------- |
| Where       | ndjson on disk, one file per build | a Redis Stream, `build:{id}:logs` |
| Kept        | forever                            | the last `LiveStreamLines` lines  |
| If it fails | the build fails                    | the build carries on              |

The asymmetry is the design. The archive is the record a customer can come back
to in six months; the live stream is a convenience for whoever is watching right
now. Redis being unavailable — not configured, unreachable, or failing
mid-build — degrades to archive-only and reports how many lines the viewer
missed, rather than failing anyone's build.

Set `BuildLogs:RedisConnectionString` to enable the live stream. Leave it empty
and the worker still runs builds and still keeps their logs.

### Redaction

Every line is redacted **on the way in**, in `LogRedaction`, before it reaches
either destination. Redacting at render time would leave the secret in the
stream and in the archive, and the archive is the copy that lives for years.

The patterns cover what build tools actually print: Gradle echoing a `-P`
property on failure, keystore paths from `apksigner` and `keytool`, bearer
tokens, cloud provider keys, and private key blocks. It is a filter over known
shapes, not a proof — see `tests/fixtures/log-redaction/README.md` for the
corpus it is tested against, including the three cases that must _survive_
redaction untouched.

### Reading a log back

`BuildLogReader.ReadAsync(buildId, afterStreamId, count)` returns a page of
lines and the id to resume from. A viewer that reconnects passes back the last
id it saw; an empty page hands the same position back rather than rewinding to
the beginning. Reads are paged rather than blocking, because a blocking read per
viewer is a Redis connection per viewer and the managed tiers count connections.
