# Free Resource Playbook

**Goal: reach a working private alpha (Sprint 12) for under $150 total spend.**

All figures verified around August 2026. Free tiers change without notice — Oracle halved theirs mid-2026 with no announcement — so **re-verify each one at the sprint that first depends on it**, and never build a critical path on a free tier without a documented paid fallback.

---

## 1. The $0 stack

| Need                              | Free option                                                                             | Limit                                                                                                        | First paid trigger                                         |
| --------------------------------- | --------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------ | ---------------------------------------------------------- |
| **Source control + CI**           | GitHub Free                                                                             | Unlimited private repos; 2,000 Actions min/mo private, **unlimited on public repos**                         | Never (see §2)                                             |
| **macOS build minutes** ⚠️        | **Codemagic Free**                                                                      | **500 macOS M2 min/mo**, 1 concurrency, personal account only, no teams                                      | S13, or when > ~33 builds/mo                               |
| **Extra macOS minutes**           | GitHub Actions macOS on a **public** repo                                               | Unlimited on public repos                                                                                    | When shells go private                                     |
| **Linux compute**                 | Oracle Cloud Always Free (Ampere A1 ARM)                                                | ⚠️ **2 OCPU / 12 GB** (halved 15 Jun 2026, enforced from 18 Aug 2026), 200 GB block storage, 10 TB/mo egress | S17                                                        |
| **Extra Linux compute**           | Google Cloud e2-micro always-free (1 instance, US regions) + Fly.io free allowance      | Small                                                                                                        | S17                                                        |
| **Postgres**                      | **Neon** free (0.5 GB, autosuspend) or **Supabase** free (500 MB)                       | Small, sleeps when idle                                                                                      | S17                                                        |
| **Redis**                         | **Upstash** free (10k commands/day)                                                     | Low command budget                                                                                           | S13                                                        |
| **Object storage**                | **Cloudflare R2** free tier (10 GB storage, 1M Class A ops/mo) + **zero egress always** | 10 GB                                                                                                        | S13                                                        |
| **CDN / DNS / WAF**               | Cloudflare Free                                                                         | Generous                                                                                                     | Probably never                                             |
| **Static hosting (studio, docs)** | Cloudflare Pages / GitHub Pages                                                         | Unlimited requests                                                                                           | Never                                                      |
| **Workflow engine**               | Temporal OSS self-hosted on the Oracle box                                              | RAM-bound                                                                                                    | S17                                                        |
| **Error tracking**                | Sentry Developer free (5k errors/mo)                                                    | 5k events                                                                                                    | S20                                                        |
| **Metrics/logs**                  | Grafana Cloud free (10k series, 50 GB logs)                                             | Fine early                                                                                                   | S20                                                        |
| **Device preview**                | **Appetize.io free** (~100 min/mo)                                                      | 100 min                                                                                                      | S13                                                        |
| **Container registry**            | GitHub Container Registry                                                               | Free for public, generous for private                                                                        | Never                                                      |
| **Secret storage (dev)**          | SOPS + age, encrypted in repo                                                           | Dev only ⚠️                                                                                                  | S14 — must move to a real KMS before holding customer keys |
| **Email (transactional)**         | Resend free (3k/mo) or Brevo free (300/day)                                             | Fine early                                                                                                   | S17                                                        |
| **Uptime monitoring**             | UptimeRobot free (50 monitors)                                                          | Fine                                                                                                         | Never                                                      |
| **Docs site**                     | Astro Starlight on Cloudflare Pages                                                     | Free                                                                                                         | Never                                                      |
| **Status page**                   | Cloudflare Pages + a static generator                                                   | Free                                                                                                         | Never                                                      |

### Unavoidable costs

| Item                           | Cost             | Needed by                                                                                  |
| ------------------------------ | ---------------- | ------------------------------------------------------------------------------------------ |
| ⚠️ **Apple Developer Program** | **$99/yr**       | Sprint 03 — no way around it, you cannot produce a signed IPA or use TestFlight without it |
| ⚠️ **Google Play Console**     | **$25 one-time** | Sprint 03                                                                                  |
| Domain name                    | ~$10–12/yr       | Sprint 11 (nice to have earlier)                                                           |
| **Phase 0 total**              | **$124**         |                                                                                            |

---

## 2. The GitHub Actions public-repo trick — use it deliberately

GitHub Actions is **free and unmetered on public repositories**, including macOS runners. For Phase 0 and Phase 1 this is a legitimate way to get far more than 500 macOS minutes per month.

**How to use it safely:**

- Keep the **shell template repos public** (`shells/ios`, `shells/android`) and build them there. There is a strong argument for open-sourcing the shells anyway — it is a trust and 4.2-credibility asset.
- Keep the **control plane, codegen, and studio private** — that is the actual business.
- ⚠️ **Never put a secret in a public-repo workflow.** No signing certificates, no API keys. Use it for _unsigned_ compile verification and test runs only. Signed builds run on Codemagic with encrypted environment variables.
- Cache aggressively (`actions/cache`) even when minutes are free — build wall-clock time is your feedback loop.

**Practical split:**

| Job                                       | Where                                            | Why                              |
| ----------------------------------------- | ------------------------------------------------ | -------------------------------- |
| Android compile + unit tests + lint       | GitHub Actions Linux (public repo)               | Free, fast                       |
| iOS compile + unit tests (unsigned)       | GitHub Actions macOS (public repo)               | Free, unmetered                  |
| **Signed** iOS archive → IPA → TestFlight | **Codemagic free 500 min**                       | Secrets are encrypted and scoped |
| Signed Android AAB → Play                 | GitHub Actions private repo w/ encrypted secrets | Cheap, 2,000 min/mo is plenty    |

### Student benefits — check these in Sprint 00

- **GitHub Student Developer Pack** — free Actions minutes uplift, free domain for a year, DigitalOcean/Azure credits, JetBrains IDEs, and more. You are a final-year BSc student; this is directly available.
- **Codemagic offers free accounts for students, teachers and non-profits.** Apply in Sprint 00 with your university email — this may remove the 500-minute ceiling entirely, which is the single biggest cost constraint in Phase 0–1.
- **Oracle, Azure, AWS, and Google Cloud all run student/startup credit programmes.** Azure for Students gives credit with no card. Apply to all of them in Sprint 00; treat any credit as runway, not as architecture.
- **JetBrains, Figma, Notion, Sentry** all have free education tiers.

**Sprint 00 action: spend 2 hours applying to every one of these before writing code.** Highest hourly return in the entire programme.

---

## 3. Architecture that stays free longest

```
┌─ Cloudflare (free) ──────────────────────────────────────┐
│  DNS · CDN · WAF · Pages (studio + docs) · R2 (artifacts)│
└──────────────────────┬───────────────────────────────────┘
                       │
┌──────────────────────▼───────────────────────────────────┐
│  Oracle Cloud Always Free — Ampere A1, 2 OCPU / 12 GB     │
│  ┌────────────┐ ┌──────────┐ ┌──────────┐ ┌───────────┐  │
│  │ API (.NET) │ │ Temporal │ │ Postgres │ │ Linux     │  │
│  │ + PgBouncer│ │ + worker │ │ (local)  │ │ build     │  │
│  │            │ │          │ │          │ │ runner    │  │
│  └────────────┘ └──────────┘ └──────────┘ └───────────┘  │
│  All in Docker Compose. Caddy for TLS.                    │
└──────────────────────┬───────────────────────────────────┘
                       │
┌──────────────────────▼───────────────────────────────────┐
│  Codemagic Free — macOS M2, 500 min/mo                    │
│  Triggered by API webhook. Signed iOS builds.             │
└───────────────────────────────────────────────────────────┘
```

⚠️ **ARM caveats on the Oracle box — plan for these:**

- Everything must have an `arm64` container image. .NET 10, Postgres, Redis, Temporal, and the Android SDK command-line tools all do. Verify each in Sprint 00.
- **The Android emulator will not run well here.** Oracle A1 does not expose nested virtualisation, and ARM hosts want ARM system images. **Do not plan self-hosted emulator streaming before Sprint 13** — use Appetize's free tier, then reassess.
- Gradle on ARM is fine. Java 21 ARM builds are fine.
- 12 GB is tight for Postgres + Temporal + a Gradle build simultaneously. Set explicit memory limits per container in Compose or the OOM killer will pick your database.

**Fallback if Oracle capacity is unavailable in your region** (the "Out of host capacity" error is common): Hetzner CX22 at roughly €4/month, or Google Cloud's always-free e2-micro. Budget €4/mo as a contingency from Sprint 07.

---

## 4. Free tools for the build itself

| Purpose                  | Free tool                                                                              |
| ------------------------ | -------------------------------------------------------------------------------------- |
| Store automation         | **fastlane** (MIT) — `deliver`, `pilot`, `match`, `supply`                             |
| iOS code signing         | fastlane `match` (free) or App Store Connect API                                       |
| Android signing          | `apksigner` (Android SDK)                                                              |
| Workflow orchestration   | **Temporal OSS**                                                                       |
| Secrets (pre-production) | **SOPS + age**                                                                         |
| Secrets (production)     | **OpenBao** (Vault fork, Apache 2.0) self-hosted                                       |
| WebRTC SFU               | **LiveKit OSS**                                                                        |
| Android screen streaming | **scrcpy** (Apache 2.0)                                                                |
| macOS VMs                | **Tart** (free for personal/open-source use — ⚠️ check current licence for commercial) |
| JSON Schema validation   | Ajv (TS), JsonSchema.Net (C#)                                                          |
| Hashing                  | BLAKE3 reference implementations                                                       |
| Load testing             | k6 OSS                                                                                 |
| API testing              | Bruno (open source, git-friendly)                                                      |
| Test containers          | Testcontainers                                                                         |
| Security scanning        | gitleaks, Trivy, OWASP Dependency-Check, Semgrep OSS                                   |
| Accessibility testing    | axe-core, Accessibility Scanner (Android), Xcode Accessibility Inspector               |
| Analytics store          | ClickHouse OSS                                                                         |
| Push                     | APNs and FCM are **free** — you only pay for your own compute                          |

---

## 5. Upgrade triggers — when to actually spend money

Do not pre-emptively upgrade. Wait for the trigger.

| Trigger                                           | Spend                                            | Approx. cost                          |
| ------------------------------------------------- | ------------------------------------------------ | ------------------------------------- |
| Codemagic free minutes exhausted 2 months running | Codemagic pay-as-you-go                          | ~$0.095/min (≈$0.95 per 10-min build) |
| > 20 concurrent alpha users                       | Hetzner CX32 or AX41 to replace/augment Oracle   | €7–€40/mo                             |
| Postgres > 500 MB or connection limits hit        | Neon paid or self-host on the VPS                | $19/mo or $0                          |
| Appetize 100 min/mo exhausted                     | Appetize paid tier                               | from ~$59/mo                          |
| First customer holding signing keys               | **OpenBao on a dedicated VPS** ⚠️ non-negotiable | ~€7/mo                                |
| > 150 iOS builds/day                              | **Own a Mac mini**                               | ~$600 capex + colo                    |
| Revenue > $500 MRR                                | Managed Postgres + Sentry Team + Grafana paid    | ~$100/mo                              |

**Rule: never let a free-tier limit block a paying customer.** The moment revenue exists, the free tier is a cost optimisation, not a constraint. Upgrade the instant a paying user is affected.

---

## 6. Cost tracking discipline

- Add a `COSTS.md` at the repo root. One row per sprint: planned vs actual spend.
- Set a **billing alert at $10** on every cloud account you create, in Sprint 00, before provisioning anything.
- ⚠️ Oracle, AWS, and GCP free tiers all have paths to accidental charges (egress, capacity upgrades, forgotten instances). Cap them explicitly.
- Review `COSTS.md` at every sprint retro. A surprise bill in month 8 is a programme risk, not an accounting detail.
