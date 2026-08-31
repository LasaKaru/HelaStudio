# Engineering Standards & Optimisation Techniques

Read once before Sprint 00. Re-read the review checklist (§9) before every merge.

---

## 1. Universal principles

1. **Make it correct, then make it fast, then make it small.** Never reorder these.
2. **Measure before optimising.** Every performance claim in a PR description must cite a benchmark number. "Feels faster" is not a number.
3. **Prefer deletion.** The cheapest code to maintain is code that does not exist. Before adding a plugin, a config field, or an abstraction, try to delete something instead.
4. **One-way doors get a design note.** Schema shapes, bridge protocol, plugin manifest format, and artifact hashing are one-way doors. Write a short ADR (`/docs/adr/NNNN-*.md`) before implementing.
5. **Fail fast, fail loud, fail cheap.** Validate on the cheapest machine available. A config error must never reach a macOS runner. This single rule is worth more than every other optimisation in this document combined.

---

## 2. Cross-cutting optimisation techniques

These are the techniques that matter for _this specific product_. Apply them deliberately.

### 2.1 Content-addressed build caching — the highest-leverage optimisation in the system

```
buildKey = BLAKE3(
    canonicalJson(config)      // key-sorted, whitespace-normalised
  ‖ pluginLockfile             // exact resolved plugin versions
  ‖ toolchainDescriptor        // xcode / agp / sdk / ndk versions
  ‖ shellTemplateVersion       // semver of the shell repo tag
)
```

**Split the key.** Do not use one hash for everything:

| Sub-key      | Covers                                                    | If only this changes                                           |
| ------------ | --------------------------------------------------------- | -------------------------------------------------------------- |
| `codeKey`    | plugins, toolchain, shell version, bundle id, permissions | Full recompile required                                        |
| `assetKey`   | icons, splash, colours, strings, nav labels               | **Resource-patch path** — repackage without recompiling        |
| `contentKey` | initial URL, link rules, injected CSS/JS                  | **Config-only path** — patch the embedded config file, re-sign |

Measured impact: a colour change goes from an 8-minute iOS build to roughly 40 seconds. In practice **70–80% of user-triggered builds are asset- or config-only.** Implement the split in S08; it is the difference between viable and non-viable unit economics.

### 2.2 Canonical JSON

Any hashing of config requires a canonical form or your cache will never hit. Rules: keys sorted lexicographically, no insignificant whitespace, numbers in shortest round-trip form, UTF-8 NFC normalisation, explicit nulls omitted. Write this once, in `packages/config-schema`, and use it from both C# and TypeScript. Test it with a property test that asserts `parse(canonical(x)) == parse(canonical(shuffle(x)))`.

### 2.3 Warm pools and snapshot restore

Cold-starting a build environment dominates short builds.

- **Linux:** pre-built Docker images with the Android SDK, Gradle distribution, and a warmed Gradle daemon baked in. Never `sdkmanager install` at build time.
- **macOS:** golden VM snapshots per Xcode version. Restore-from-snapshot (~2–10 s) instead of clean-up-after-job (minutes) and instead of fresh provisioning (~30+ min).
- **Target:** environment ready in < 15 s for Linux, < 30 s for macOS.

### 2.4 Dependency caching layers

| Layer         | Cache                                                               | Where                              |
| ------------- | ------------------------------------------------------------------- | ---------------------------------- |
| Gradle        | `~/.gradle/caches`, `~/.gradle/wrapper`                             | Baked into image + per-app volume  |
| Android build | Gradle build cache (`org.gradle.caching=true`), configuration cache | Remote build cache on R2           |
| CocoaPods     | `Pods/`, `~/.cocoapods/repos`                                       | Per-app volume                     |
| SwiftPM       | `.build`, `~/Library/Caches/org.swift.swiftpm`                      | Per-app volume                     |
| Xcode         | `DerivedData`                                                       | Per-app volume, keyed by `codeKey` |
| npm           | `~/.npm`                                                            | Baked into image                   |

⚠️ Cache **per app**, not globally, and key on `codeKey`. A shared mutable cache across tenants is both a correctness hazard and a security hole.

### 2.5 Do expensive work once, at the right layer

| Work                      | Wrong place        | Right place                                                        |
| ------------------------- | ------------------ | ------------------------------------------------------------------ |
| Config validation         | macOS runner       | Browser (client-side) → API → runner. Three times, cheapest first. |
| Icon resizing             | Build              | Upload time, cached in R2 by source hash                           |
| Plugin conflict detection | Build failure      | Config-save time in the studio                                     |
| Privacy manifest merge    | Runtime            | Codegen                                                            |
| Store readiness scoring   | Submission failure | Config-save time                                                   |

### 2.6 Streaming over buffering

Build logs can be hundreds of MB. Never accumulate in memory or in a DB row.

- Runner → Redis Stream (capped, `MAXLEN ~ 50000`) → WebSocket fan-out to browser.
- Simultaneously append to an R2 object via multipart upload for the durable record.
- Client renders a virtualised list; never `innerHTML +=`.

### 2.7 Async, bounded, cancellable

Every long operation must be: `async` end-to-end, bounded by an explicit timeout, and cancellable by the user. A build the user cancelled must free the runner within 5 seconds. On a metered Mac fleet, an uncancellable build is money on fire.

### 2.8 Backend-specific (.NET)

- **Minimal APIs**, not MVC controllers — less allocation, faster startup.
- **`System.Text.Json` with source generators** (`[JsonSerializable]`). Reflection-based serialisation of large config documents is a measurable hotspot.
- **`ValueTask`** for hot paths that frequently complete synchronously (cache hits).
- **`ArrayPool<byte>` / `RecyclableMemoryStream`** for artifact streaming. Never `new byte[fileSize]` on a 200 MB IPA.
- **`Span<T>` / `ReadOnlySequence<T>`** for hashing and canonicalisation.
- **EF Core:** `AsNoTracking()` for all reads, compiled queries for hot paths, explicit projections (`Select`) — never fetch whole entities to render a list. Split queries for collections.
- **⚠️ Zero N+1 tolerance.** Add an EF Core interceptor in dev that logs a warning when > 20 queries execute in one request. Fail CI if an integration test trips it.
- **ReadyToRun + trimming** for the API container; cuts cold start meaningfully on small free-tier instances.
- **Rate limiting** via built-in `AddRateLimiter` on every public endpoint, from day one.

### 2.9 Frontend (React studio)

- **Route-level code splitting**; the config editor and Monaco must be lazy-loaded (Monaco alone is ~2 MB).
- **TanStack Query** for all server state. Never duplicate server state into Zustand.
- **Uncontrolled forms** (`react-hook-form`) for the large config forms — controlled inputs on a 200-field form cause visible lag.
- **Virtualise** any list that can exceed 50 rows (build history, log viewer).
- **Debounce config validation** to 300 ms; run the JSON-schema validation in a **Web Worker** so typing never blocks.
- **Budget: initial JS < 200 KB gzipped, LCP < 2.0 s on a 4× CPU-throttled profile.** Enforce with `size-limit` in CI.
- Prefer CSS transforms over layout-triggering properties for anything animated.

### 2.10 Mobile shells — the code that ships to users

This is the product. Its performance is judged by App Store reviewers and end users.

**Startup (target: first pixel < 300 ms, interactive shell < 500 ms):**

- Parse the embedded config **lazily and partially** — read only what's needed to draw the first frame; defer the rest to a background thread.
- Draw the native skeleton **before** the WebView begins loading, never after.
- Create the WebView **eagerly on a background thread during splash** so it is warm when needed.
- Android: enable **Baseline Profiles** (`androidx.profileinstaller`) — 20–30% startup improvement for free. Enable **R8 full mode** and resource shrinking.
- iOS: avoid work in `application(_:didFinishLaunchingWithOptions:)`; measure with `os_signpost`.
- ⚠️ **No plugin may initialise at launch.** Plugins register lazily on first bridge call. Otherwise 15 plugins = 15 SDK inits = a 2-second cold start.

**Memory:**

- One WebView per window, destroyed on close. WebViews leak if retained.
- Android: `WebView.destroy()` after removing from the hierarchy; never keep an Activity reference in a plugin.
- Cap the modal window stack (config: `maximumWindows`).

**Binary size (target: < 25 MB base):**

- Android: AAB with per-ABI and per-density splits; R8 full mode; `resConfigs` limited to declared locales.
- iOS: strip symbols, bitcode off, asset catalogue with on-demand where sensible.
- **Every plugin must publish its size delta.** Show it in the studio next to the toggle. This is both an optimisation and a great UX detail nobody else has.

**Bridge:**

- Batch events emitted within one animation frame into a single message.
- Never serialise large payloads across the bridge — pass a handle/URL for anything over 64 KB.
- Rate-limit per method natively.

### 2.11 Database

- Indexes on every foreign key and every column used in a `WHERE` on a hot path. Verify with `EXPLAIN ANALYZE`, not by intuition.
- Partial indexes for status queries: `CREATE INDEX ... WHERE status IN ('queued','running')`.
- `jsonb` for config bodies with a **GIN index** only if you actually query into them; otherwise plain `jsonb` and query by hash.
- Config versions are **append-only and immutable** — no `UPDATE`, ever. Makes caching and audit trivially correct.
- Connection pooling via **PgBouncer** (transaction mode) — free-tier Postgres has low connection limits.
- Every migration must be **backwards-compatible for one release** (expand → migrate → contract). You will deploy while builds are running.

---

## 3. Language & style

| Language   | Formatter       | Linter/Analyser                                                  | Style                                                                     |
| ---------- | --------------- | ---------------------------------------------------------------- | ------------------------------------------------------------------------- |
| C#         | `dotnet format` | Roslyn analysers, `TreatWarningsAsErrors=true`, nullable enabled | Microsoft conventions, file-scoped namespaces, records for DTOs           |
| TypeScript | Prettier        | ESLint (`@typescript-eslint/strict-type-checked`)                | `strict: true`, no `any` (use `unknown`), no default exports except pages |
| Kotlin     | ktlint          | detekt, `-Xexplicit-api=strict` for the shell library            | Official Kotlin style, explicit visibility                                |
| Swift      | swift-format    | SwiftLint                                                        | Swift API Design Guidelines, `final` by default                           |
| SQL        | pgFormatter     | sqlfluff                                                         | Explicit column lists, never `SELECT *`                                   |

**Hard rules:**

- `TreatWarningsAsErrors` on everywhere. A warning you tolerate is a warning you stop seeing.
- No `any`, no `!!`, no force-unwrap (`!`) in Swift outside tests. Lint-enforced.
- Public API surfaces are documented (`///` XML docs, TSDoc, KDoc). Generated docs are a deliverable.
- Max function length 60 lines, max cyclomatic complexity 10, enforced by analysers. Exceeding requires an inline justification comment.

---

## 4. Error handling

- **Never swallow.** No empty `catch`. Lint-enforced.
- **Typed, coded errors everywhere.** Every error crossing a boundary (API, bridge, runner) carries `{ code, message, retryable, details }`. Codes are stable, documented, and searchable in the knowledge base.
- **Errors users see must be actionable.** "Build failed" is a bug. "Plugin `qr-scanner` requires Android minSdk 24 but your config sets 21 — raise minSdk or remove the plugin" is correct.
- **Result types over exceptions** for expected failures (validation, conflicts). Exceptions only for genuinely exceptional states.

---

## 5. Logging & observability

- **Structured logging only** (Serilog → JSON). No string interpolation into messages; use message templates with properties.
- Every request and every build carries a `correlationId` propagated through API → workflow → runner → logs.
- **OpenTelemetry traces** on API, workflows, and runner steps from Sprint 06. Retrofitting tracing is miserable.
- ⚠️ **Scrub secrets before writing, not before displaying.** Signing tools print key paths, keychain contents, and occasionally key material. Maintain a redaction filter with a test suite of known-leaky outputs.
- Four golden signals per service: latency, traffic, errors, saturation. One dashboard. Look at it weekly.

---

## 6. Security rules (non-negotiable)

1. **No secret ever touches the database, a log, or a build artifact.** Secrets live in the secret store; artifacts get references.
2. **Bridge injection is origin-allowlisted.** Enforced natively, not in JS. A page outside the allowlist must have no bridge object at all.
3. **Every build runs in a fresh, isolated environment, destroyed after.** No reuse across tenants, ever.
4. **All user input is untrusted**, including config JSON, uploaded icons, and URLs. Validate against schema, verify image magic bytes and dimensions, and check URLs against a reputation list before first build.
5. **SSRF defence:** the platform fetches user-supplied URLs (site analysis, favicon). Block private IP ranges, link-local, and metadata endpoints. Use an allowlist-based fetcher with a hard timeout and size cap.
6. **Dependency pinning + lockfiles committed.** `gitleaks` and dependency audit in CI, blocking.
7. **Least privilege** on every cloud credential. The runner's storage credential can write artifacts and read nothing else.
8. **Threat-model each new subsystem** in one page before building it. Especially signing (S14), OTA (S22), and push (S20).

---

## 7. API design

- REST, resource-oriented, plural nouns: `/v1/orgs/{orgId}/apps/{appId}/builds`.
- **Versioned from day one** (`/v1`). Breaking changes get `/v2`, never a silent change.
- Cursor pagination, never offset.
- **Idempotency keys** on all `POST` that create billable work (builds, submissions). A retried request must not double-charge or double-build.
- RFC 7807 `application/problem+json` for errors.
- ETag + `If-None-Match` on config reads.
- OpenAPI generated from code, published, and used to generate the TypeScript client. Hand-written clients drift.

---

## 8. Documentation duties

Every sprint produces:

- Updates to affected `/docs` pages
- ADRs for one-way-door decisions
- `CHANGELOG.md` entries
- `SPRINT-NN_REVIEW.md`

Docs are written **as the feature is built**, not after. A feature without docs is not done.

---

## 9. Pull-request review checklist

Copy this into your PR template. Review your own PRs against it — solo discipline is the only discipline you have.

```markdown
## Correctness

- [ ] Acceptance criteria from the sprint file are all met
- [ ] All listed test case IDs pass
- [ ] Edge cases: empty, null, max size, unicode, concurrent, cancelled

## Performance

- [ ] No N+1 queries (checked query log)
- [ ] No unbounded allocation on user-controlled size
- [ ] Long operations are async, bounded, cancellable
- [ ] Benchmark cited if this touches a hot path

## Security

- [ ] No secret in code, logs, or artifacts
- [ ] All inputs validated against schema
- [ ] AuthZ checked at the resource level, not just the route
- [ ] No new dependency without a licence + maintenance check

## Maintainability

- [ ] No new warnings
- [ ] Public API documented
- [ ] Errors are typed, coded, and actionable
- [ ] No TODO without an issue link

## Ops

- [ ] Migration is backwards-compatible
- [ ] New config/env vars documented and defaulted safely
- [ ] Logs carry correlationId
- [ ] Feature is observable (metric or trace added)
```

---

## 10. Anti-patterns explicitly banned in this codebase

| Banned                                                                    | Why                                   | Do instead                              |
| ------------------------------------------------------------------------- | ------------------------------------- | --------------------------------------- |
| Mutating a `ConfigVersion`                                                | Destroys cache correctness and audit  | Create a new version                    |
| A plugin modifying shell core files                                       | Combinatorial explosion by plugin #15 | Add a shell capability with a flag      |
| Shelling out with string-concatenated arguments                           | Injection via app name / bundle id    | Argument arrays, always                 |
| Storing build logs in Postgres                                            | Table bloat, slow queries             | Redis Stream + R2 object                |
| A shared mutable build cache across tenants                               | Correctness + security                | Per-app cache keyed by `codeKey`        |
| Catching `Exception` broadly at a boundary                                | Hides real failures                   | Catch specific, rethrow with context    |
| Version ranges (`^1.2.0`) in generated projects                           | Non-reproducible builds               | Exact pinned versions from the lockfile |
| Doing anything expensive in `Application.onCreate` / `didFinishLaunching` | Cold-start regression                 | Lazy init on first use                  |
