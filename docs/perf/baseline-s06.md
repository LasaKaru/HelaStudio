# Performance baseline — Sprint 06

Measured 2 September 2026 against the control plane at commit `9b04d52`.

| Scenario                             | Offered load | Budget (p95) | Measured (p95) | Failures |
| ------------------------------------ | ------------ | ------------ | -------------- | -------- |
| `GET /v1/apps/{id}/config`           | 200 req/s    | < 100 ms     | **12.1 ms**    | 0        |
| `POST /v1/apps/{id}/config/validate` | 100 req/s    | < 100 ms     | **23.6 ms**    | 0        |
| `POST /v1/apps/{id}/config`          | 50 req/s     | < 400 ms     | **20.4 ms**    | 0        |

Medians were 6.9 ms, 12.1 ms and 12.3 ms. No run dropped an iteration, so the
server kept up with the offered rate throughout and the percentiles describe
latency rather than queueing.

Reproduce with:

```
bash scripts/load-test.sh
```

## What the numbers are, and are not

⚠️ **This is not the production environment.** It is a container with the API,
PostgreSQL 16, and k6 all on the same host and no network between them. The
figures are therefore a _floor_: they say the code is not the bottleneck, and
they say nothing about the Oracle Always Free host, which will add real network
latency, contend for two cores between the API and Postgres, and run PgBouncer
in front of a connection limit far below the pool size used here.

The value of a floor measured this way is that a regression in the code shows up
against it immediately, without waiting for infrastructure to exist. Rerun this
on the real host as soon as there is one, and replace the table.

⚠️ **Rate limits are raised for the duration of the run**, to a million per
minute, by `scripts/load-test.sh`. Left at production values the limiter is the
first thing the load meets and the run measures the limiter: the first attempt
at this baseline failed **99.95% of 840,000 requests** and reported a p95 for the
374 that got through. That number would have looked like a result. Whether the
production limits are correct is a separate question, answered by
`CrossCuttingTests.A_flood_is_rate_limited_with_a_retry_after`.

## Why a fixed arrival rate

The sprint plan asks for 50 virtual users for 60 seconds. Implemented literally,
with k6's `constant-vus` and no think time, each user issues its next request
the instant the last returns — so the offered load is whatever the server allows
rather than a chosen number. The first run reached **13,418 requests a second**,
which is not a load any studio produces, and the resulting "p95" is the p95 of a
queue.

`constant-arrival-rate` pins the offered load instead. The percentile then
answers a question worth asking — _what is the latency at 200 requests a
second_ — and `dropped_iterations` states outright when the server could not
keep up, rather than letting a saturated server quietly inflate its own
percentile.

## Choices that show up in these numbers

- **No change tracking on reads.** `QueryTrackingBehavior.NoTracking` is set
  globally; a tracked read of a config version deep-clones the whole document.
- **The current version is a pointer, not a query.** `apps.current_config_version_id`
  makes "the current config" one indexed lookup rather than an ordering query
  over every version ever saved.
- **Saving an unchanged config is a read.** The content-addressed unique index
  turns the common autosave into a lookup, which is most of why the save path
  measures close to the validate path.
- **Row-level security is not free but is not visible here.** Every policy is a
  membership lookup through a `STABLE SECURITY DEFINER` function, which
  PostgreSQL caches within a statement.

## What is not measured yet

- **PgBouncer.** The free-tier Postgres connection limit is the constraint the
  sprint plan flags as high risk, and this run used a 40-connection pool
  straight to the server. Nothing here says what happens behind a pooler in
  transaction mode.
- **Asset upload.** Bounded by image decoding and by whatever blob store is in
  front of it, and the blob store is currently a local directory rather than the
  R2 it will be.
- **Concurrent tenants.** Every scenario uses one organisation. Row-level
  security's cost is a function of membership size, which this does not vary.
