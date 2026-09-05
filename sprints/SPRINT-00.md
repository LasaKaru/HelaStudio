# Sprint 00 — Foundations & Development Environment

|                   |                      |
| ----------------- | -------------------- |
| **Weeks**         | 1–2                  |
| **Phase**         | 0 — Proof            |
| **Capacity**      | 55 h (38 h new work) |
| **Depends on**    | Nothing              |
| **Blocks**        | Everything           |
| **Planned spend** | **$0**               |

---

## 1. Sprint goal

Stand up the entire development substrate — monorepo, CI, standards enforcement, free infrastructure, and the test harness — so that from Sprint 01 onward every commit is linted, tested, and gated automatically. Also secure every free credit and student benefit available, because that decision compounds for twelve months.

**This sprint produces no user-facing functionality and that is correct.** Skipping it means retrofitting CI, coverage gates, and secret scanning into a codebase that already has 40 files, which costs three times as much.

---

## 2. Exit criteria

- [ ] Monorepo exists, builds green on CI, with all six language toolchains configured
- [ ] A trivial commit runs lint → unit → secrets scan → build in under 5 minutes on GitHub Actions
- [ ] `TreatWarningsAsErrors` / `strict` enabled in every project; zero warnings
- [ ] Oracle Always Free instance provisioned, reachable over SSH, running Docker with an `arm64` sanity container
- [ ] Cloudflare account with R2 bucket and a Pages project deployed (a placeholder page is fine)
- [ ] Billing alerts set at $10 on every cloud account
- [ ] Codemagic account created; student application submitted
- [ ] GitHub Student Developer Pack applied for
- [ ] `COSTS.md`, `JOURNAL.md`, `CHANGELOG.md`, ADR folder, and PR template committed
- [ ] Three fixture test websites live on Cloudflare Pages

---

## 3. Task breakdown

| ID     | Task                                                          | Est.     | Priority |
| ------ | ------------------------------------------------------------- | -------- | -------- |
| T-00.1 | Apply for all free credits, student packs, and accounts       | 2 h      | P0       |
| T-00.2 | Monorepo scaffold and tooling                                 | 6 h      | P0       |
| T-00.3 | CI pipeline (GitHub Actions)                                  | 6 h      | P0       |
| T-00.4 | Standards enforcement (linters, analysers, formatters, hooks) | 5 h      | P0       |
| T-00.5 | Provision Oracle Always Free host                             | 5 h      | P0       |
| T-00.6 | Provision Cloudflare (R2, Pages, DNS)                         | 3 h      | P0       |
| T-00.7 | Test harness skeleton and fixture corpus structure            | 5 h      | P0       |
| T-00.8 | Fixture test websites                                         | 3 h      | P1       |
| T-00.9 | Project documentation scaffolding                             | 3 h      | P1       |
|        | **Total**                                                     | **38 h** |          |

---

## 4. Task detail

### T-00.1 — Apply for all free credits, student packs, and accounts (2 h)

**Objective:** Convert two hours into twelve months of free infrastructure.

**Steps:**

1. **GitHub Student Developer Pack** — apply with university email and student ID. Unlocks a free domain, extra Actions minutes, JetBrains, Sentry, and cloud credits.
2. **Codemagic student/education account** — Codemagic offers free accounts for students, teachers, and non-profits. Apply with the university address. ⚠️ This is the single highest-value application; it can remove the 500 macOS-minute ceiling that otherwise constrains all of Phase 0 and 1.
3. **Oracle Cloud Free Tier** — sign up. ⚠️ Requires a card for identity; it is not charged on an Always Free account. Choose the region with A1 capacity — if you hit "Out of host capacity", try an adjacent region before giving up.
4. **Cloudflare** — free account.
5. **Azure for Students** — credit with no card required.
6. **Google Cloud / AWS free tier** — as backup compute.
7. **Neon, Upstash, Sentry, Grafana Cloud, Resend, UptimeRobot, Appetize** — free accounts, note the exact limits in `02_FREE_RESOURCE_PLAYBOOK.md`.
8. **Set a $10 billing alert on every account that can bill you.** Do this before provisioning anything.

**Acceptance criteria:**

- All accounts created; credentials in a password manager (never in the repo)
- Student applications submitted with confirmation emails saved
- Billing alerts confirmed by screenshot in `/docs/ops/billing-alerts/`
- `02_FREE_RESOURCE_PLAYBOOK.md` updated with the _actual_ limits you observed, which may differ from published ones

**Tests:** `TC-S00-OPS-001`

---

### T-00.2 — Monorepo scaffold and tooling (6 h)

**Objective:** One repository, clear boundaries, fast builds, no cross-contamination.

**Layout:**

```
shellwright/
├─ .github/workflows/           ci.yml, nightly.yml
├─ .config/                     dotnet-tools.json
├─ docs/
│  ├─ adr/                      0001-record-architecture-decisions.md
│  ├─ ops/
│  └─ qa/
├─ apps/
│  ├─ api/                      Shellwright.Api            (.NET 10)
│  └─ studio/                   React + Vite + TS
├─ services/
│  └─ orchestrator/             Shellwright.Orchestrator   (.NET 10)
├─ packages/
│  ├─ config-schema/            JSON Schema + TS types + C# models (shared)
│  ├─ bridge-sdk/               (empty until S09)
│  └─ cli/                      (empty until S19)
├─ shells/
│  ├─ android/                  (empty until S02)
│  └─ ios/                      (empty until S03)
├─ plugins/                     (empty until S10)
├─ tests/
│  ├─ fixtures/configs/
│  └─ fixtures/sites/
├─ infra/
│  ├─ compose/                  docker-compose.yml for the Oracle host
│  └─ scripts/
├─ Directory.Build.props        shared MSBuild settings
├─ Shellwright.slnx
├─ pnpm-workspace.yaml
├─ turbo.json
├─ COSTS.md  JOURNAL.md  CHANGELOG.md  README.md
```

**Steps:**

1. `git init`; create the tree above. Public/private split: create `shells/android` and `shells/ios` as **separate public repos** added as submodules or a second workspace — this is what unlocks unmetered GitHub Actions macOS minutes (see the free-resource playbook §2). Decide now; changing later is painful.
2. **.NET:** `dotnet new sln`, add `apps/api` and `services/orchestrator`. Create `Directory.Build.props`:
   ```xml
   <Project>
     <PropertyGroup>
       <TargetFramework>net10.0</TargetFramework>
       <Nullable>enable</Nullable>
       <ImplicitUsings>enable</ImplicitUsings>
       <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
       <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
       <AnalysisLevel>latest-all</AnalysisLevel>
       <LangVersion>latest</LangVersion>
       <InvariantGlobalization>true</InvariantGlobalization>
     </PropertyGroup>
   </Project>
   ```
3. **Node:** pnpm workspaces + **Turborepo** for task caching. `pnpm-workspace.yaml` covering `apps/*`, `packages/*`. Turbo's local cache alone will save hours of CI time over the programme.
4. **`.editorconfig`** at root covering C#, TS, Kotlin, Swift, JSON, YAML. This is what makes formatters agree.
5. **`.gitattributes`** — force LF, mark binary fixtures, mark snapshot files as `-diff` where noisy.
6. **`.gitignore`** covering all six toolchains plus `DerivedData`, `.gradle`, `Pods`, `local.properties`.

**Acceptance criteria:**

- `dotnet build` succeeds with zero warnings
- `pnpm install && pnpm build` succeeds
- `turbo run build` produces a cache hit on second run
- Directory structure matches the layout above

**Tests:** `TC-S00-CI-001`, `TC-S00-CI-002`

---

### T-00.3 — CI pipeline (6 h)

**Objective:** A PR gate that finishes in under five minutes and blocks anything unsafe.

**`.github/workflows/ci.yml` shape:**

```yaml
name: CI
on: [pull_request, push]
concurrency:                       # cancel superseded runs — saves free minutes
  group: ci-${{ github.ref }}
  cancel-in-progress: true
jobs:
  changes:                         # path filter → skip untouched stacks
    outputs: { dotnet: ..., node: ..., android: ..., ios: ... }
  lint:      needs: changes
  dotnet:    needs: changes        # restore(cached) → build → test → coverage gate
  node:      needs: changes        # install(cached) → lint → test → build → size-limit
  security:  # gitleaks, semgrep, trivy, dotnet list package --vulnerable
  gate:      needs: [lint, dotnet, node, security]   # single required status check
```

**Key optimisations to apply now, not later:**

- `concurrency.cancel-in-progress` — stops burning minutes on superseded pushes
- `dorny/paths-filter` — do not run the .NET job for a CSS change
- `actions/cache` on `~/.nuget/packages`, pnpm store, Gradle caches
- One aggregated `gate` job as the single required branch-protection check, so adding jobs later doesn't require reconfiguring protection rules

**Steps:**

1. Write `ci.yml` with the jobs above.
2. Enable **branch protection** on `main`: require the `gate` check, require PRs, disallow force push.
3. Add `gitleaks` with a config that also catches Apple `.p8`/`.p12` patterns and Google service-account JSON shapes.
4. Add `nightly.yml` as a stub — it becomes real in S04.
5. Add a PR template containing the review checklist from `01_ENGINEERING_STANDARDS.md` §9.

**Acceptance criteria:**

- A PR with a lint error is blocked
- A PR with a committed fake AWS key is blocked by gitleaks
- Full pipeline on a no-op change completes in < 5 min
- Second run shows cache hits for NuGet and pnpm

**Tests:** `TC-S00-CI-003`, `TC-S00-CI-004`, `TC-S00-SEC-001`

---

### T-00.4 — Standards enforcement (5 h)

**Objective:** Make the standards document mechanically enforced rather than aspirational.

**Steps:**

1. **C#:** enable Roslyn analysers via `AnalysisLevel=latest-all`; add `.globalconfig` to tune severities. Add `dotnet format --verify-no-changes` to CI.
2. **TypeScript:** ESLint with `@typescript-eslint/strict-type-checked` + `eslint-plugin-import` (enforce the layering rules) + Prettier. `tsconfig` with `strict`, `noUncheckedIndexedAccess`, `exactOptionalPropertyTypes`.
3. **Pre-commit hooks** via `lefthook` (fast, single binary, cross-platform — better than husky here): format staged files, run gitleaks on staged files. Keep hooks under 3 seconds or you will disable them.
4. **Commit linting:** `commitlint` with Conventional Commits.
5. **Coverage tooling:** Coverlet (C#) and Vitest v8 coverage, both emitting Cobertura; a CI step asserting the thresholds from `03_TEST_STRATEGY.md` §6. Thresholds start permissive and rise per that table.
6. **`size-limit`** configured for the studio bundle with the 200 KB budget (it will pass trivially now; the point is that it exists before the bundle grows).

**Acceptance criteria:**

- A badly formatted commit is auto-formatted or rejected pre-commit
- A commit message not matching Conventional Commits is rejected
- Coverage below threshold fails CI (verify by temporarily lowering a threshold)

**Tests:** `TC-S00-CI-005`, `TC-S00-CI-006`

---

### T-00.5 — Provision Oracle Always Free host (5 h)

**Objective:** A durable, free Linux host that will run the API, Postgres, Redis, Temporal, and the Linux build runner.

⚠️ **Plan for 2 OCPU / 12 GB**, not the older 4/24 figure — Oracle halved the Always Free ARM allocation in mid-2026 and began enforcing it in August 2026.

**Steps:**

1. Launch `VM.Standard.A1.Flex`, **2 OCPU / 12 GB**, Ubuntu 24.04 LTS (arm64), 100 GB boot volume.
   - If you hit "Out of host capacity", retry in another availability domain or region. This is common; do not conclude the tier is gone.
2. Harden: key-only SSH, non-root user, `ufw` allowing 22/80/443 only, `fail2ban`, unattended-upgrades. ⚠️ Oracle's default iptables rules also need editing — the security list _and_ the host firewall both matter, and this trips up almost everyone.
3. Install Docker + Compose plugin (arm64).
4. **Verify arm64 images exist for every planned dependency** — this is the real deliverable of the task:
   | Image | arm64? | Verify by |
   |---|---|---|
   | `mcr.microsoft.com/dotnet/aspnet:10.0` | yes | `docker run --rm ... --version` |
   | `postgres:17` | yes | `psql --version` |
   | `redis:7` / `valkey` | yes | `redis-cli ping` |
   | `temporalio/auto-setup` | yes | web UI loads |
   | Android SDK cmdline-tools + JDK 21 | yes | `sdkmanager --list` |
   Record results in `/docs/ops/arm64-compat.md`. Any gap changes the architecture, so find it now.
5. Write `infra/compose/docker-compose.yml` with **explicit memory limits per service** — 12 GB will not survive an unbounded Gradle daemon next to Postgres.
6. Install **Caddy** for automatic TLS against your domain (or a temporary `*.nip.io` host until you own one).
7. Set up automated backups: nightly `pg_dump` to R2 via a cron job.

**Acceptance criteria:**

- SSH by key only; password auth rejected
- `docker compose up` brings up Postgres + Redis + Temporal; Temporal UI reachable over HTTPS
- Memory limits enforced (verify with `docker stats`)
- `pg_dump` lands in R2 on schedule and can be restored to a local container

**Tests:** `TC-S00-OPS-002`, `TC-S00-OPS-003`, `TC-S00-SEC-002`

---

### T-00.6 — Provision Cloudflare (3 h)

**Steps:**

1. Create an **R2 bucket** `shellwright-artifacts-dev`. Note: R2 has **zero egress fees**, which is why artifacts, source exports, and later OTA bundles all live here.
2. Create an R2 API token scoped to that bucket only (least privilege — the runner will use it).
3. Create a **Cloudflare Pages** project for the studio; deploy a placeholder from the repo.
4. If the Student Pack domain has arrived, add it and point DNS at the Oracle host; otherwise defer.
5. Enable the free WAF ruleset and set a rate-limiting rule on `/v1/*`.

**Acceptance criteria:**

- Object upload and download to R2 via the scoped token works from the Oracle host
- The scoped token is _denied_ on a different bucket (verify the negative case)
- Pages placeholder is live over HTTPS

**Tests:** `TC-S00-OPS-004`, `TC-S00-SEC-003`

---

### T-00.7 — Test harness skeleton and fixture corpus (5 h)

**Objective:** The testing machinery from `03_TEST_STRATEGY.md` exists and is proven, before there is anything to test.

**Steps:**

1. Create test projects: `tests/Shellwright.UnitTests`, `tests/Shellwright.IntegrationTests`, `tests/Shellwright.ContractTests` (xUnit + FluentAssertions + NSubstitute).
2. Add **Testcontainers** to the integration project with a shared Postgres + Redis fixture, using a **collection fixture** so containers start once per run, not per test class.
3. Add **Verify** for snapshot testing; configure a scrubber for GUIDs, timestamps, and absolute paths so snapshots are deterministic from day one.
4. Add **FsCheck** to the unit project.
5. Create `tests/fixtures/configs/` with placeholder `minimal.json` and `maximal.json` — they become real in S01.
6. Write one deliberately trivial test per project and prove each runs in CI (this validates the harness, which is the point).
7. Set up Vitest in the studio package with coverage reporting.

**Acceptance criteria:**

- `dotnet test` runs all three projects green
- Integration tests spin up real Postgres via Testcontainers and tear down cleanly
- A Verify snapshot test passes, then fails when the output is changed, then passes when approved
- Coverage reports are produced and read by the CI gate

**Tests:** `TC-S00-CI-007`, `TC-S00-CI-008`

---

### T-00.8 — Fixture test websites (3 h)

**Objective:** Three controlled websites to point shells at, so shell bugs are never confused with website bugs.

**Steps:**

1. `tests/fixtures/sites/simple/` — static HTML, 4 pages, obvious visual markers, no JS routing.
2. `tests/fixtures/sites/spa/` — small React app with client-side routing, a long scroll page, a form with a file input, and a page that deliberately throws a JS error.
3. `tests/fixtures/sites/auth/` — a login form setting an `HttpOnly` cookie, a protected page, a logout, and a mock OAuth redirect chain (this becomes essential in S09 and again in S13).
4. Deploy all three to Cloudflare Pages under distinct subdomains.
5. Add a `/health` endpoint to each returning a build id, so tests can assert which version they hit.

**Acceptance criteria:**

- All three reachable over HTTPS
- Auth site correctly sets and requires a cookie
- SPA site's error page reliably throws a catchable JS error

**Tests:** `TC-S00-OPS-005`

---

### T-00.9 — Project documentation scaffolding (3 h)

**Steps:**

1. `README.md` — what this is, how to run it locally, in under 20 lines.
2. `CONTRIBUTING.md` — branching, commit format, PR checklist.
3. `docs/adr/0001-record-architecture-decisions.md` — adopt ADRs (use the Nygard template).
4. `docs/adr/0002-monorepo-with-public-shells.md` — record the public/private split decision and its CI-minutes rationale.
5. `COSTS.md` with a table seeded for sprint 00.
6. `JOURNAL.md` for the daily three-line standup.
7. `CHANGELOG.md` (Keep a Changelog format).

**Acceptance criteria:** All files exist, are accurate, and a stranger could clone and run the repo from the README alone.

---

## 5. Test cases

| ID               | Type        | Precondition                        | Steps                                                                       | Expected                                 |
| ---------------- | ----------- | ----------------------------------- | --------------------------------------------------------------------------- | ---------------------------------------- |
| `TC-S00-OPS-001` | Manual      | —                                   | Review every cloud account's billing settings                               | A $10 alert exists on each               |
| `TC-S00-OPS-002` | Manual      | Oracle host provisioned             | SSH with password                                                           | Connection rejected                      |
| `TC-S00-OPS-003` | Integration | Compose stack up                    | `pg_dump`, upload to R2, restore into a fresh container, compare row counts | Restore succeeds; counts match           |
| `TC-S00-OPS-004` | Integration | R2 token issued                     | Upload 5 MB object, download, compare checksum                              | Checksums match                          |
| `TC-S00-OPS-005` | Integration | Fixture sites deployed              | GET `/health` on each                                                       | 200 with build id                        |
| `TC-S00-CI-001`  | Automated   | Clean checkout                      | `dotnet build`                                                              | Exit 0, zero warnings                    |
| `TC-S00-CI-002`  | Automated   | Clean checkout                      | `turbo run build` twice                                                     | Second run reports cache hit             |
| `TC-S00-CI-003`  | Automated   | PR with a lint error                | Open PR                                                                     | `gate` check fails                       |
| `TC-S00-CI-004`  | Automated   | No-op PR                            | Open PR                                                                     | Full pipeline < 5 min                    |
| `TC-S00-CI-005`  | Automated   | Commit message `wip stuff`          | Commit                                                                      | Rejected by commitlint                   |
| `TC-S00-CI-006`  | Automated   | Coverage threshold set above actual | Run CI                                                                      | Gate fails                               |
| `TC-S00-CI-007`  | Automated   | —                                   | `dotnet test`                                                               | All projects green; containers torn down |
| `TC-S00-CI-008`  | Automated   | Verify snapshot exists              | Change generated output without approving                                   | Test fails with a diff                   |
| `TC-S00-SEC-001` | Automated   | PR containing a fake `.p8` key      | Open PR                                                                     | gitleaks blocks                          |
| `TC-S00-SEC-002` | Manual      | Oracle host                         | `nmap` from outside                                                         | Only 22/80/443 open                      |
| `TC-S00-SEC-003` | Integration | Scoped R2 token                     | Attempt write to a different bucket                                         | 403                                      |

---

## 6. Risks

| Risk                                              | Likelihood | Mitigation                                                                                                                                    |
| ------------------------------------------------- | ---------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| Oracle "Out of host capacity" blocks provisioning | **High**   | Try multiple ADs/regions; fall back to Hetzner CX22 (€4/mo) and record the cost in `COSTS.md`. Do not lose days to this — timebox to 3 hours. |
| An arm64 image is missing for a core dependency   | Medium     | T-00.5 step 4 finds it in sprint 00 rather than sprint 07. If found, either use an x86 fallback host or substitute the component.             |
| Student applications rejected or slow             | Medium     | They take days to process. Submit on day 1, not day 10. Proceed on the standard free tier meanwhile.                                          |
| Over-engineering the scaffold                     | **High**   | Timebox each task. No abstraction may be written in this sprint that has fewer than two concrete callers planned.                             |

---

## 7. Deliverables

- Monorepo on GitHub with protected `main` and a green CI gate
- Oracle host running Docker Compose (Postgres, Redis, Temporal, Caddy) with backups to R2
- Cloudflare R2 bucket + Pages project + scoped tokens
- Three fixture websites live
- Test harness proven across unit / integration / contract / snapshot
- `docs/ops/arm64-compat.md`, two ADRs, `COSTS.md`, PR template
- `SPRINT-00_REVIEW.md`
