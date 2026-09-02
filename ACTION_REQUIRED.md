# Action required

Everything the programme needs that **cannot be done from a development machine** —
accounts, payment cards, identity verification, physical devices. Development
continues around these; nothing here blocks writing code, but several items block
_shipping_, and two of them take days of calendar time that cannot be compressed.

This file is the single place these accumulate. Each entry says what it unblocks,
what it costs, and how long the waiting is likely to be — so the ones with lead
times can be started early even if everything else waits until the end.

**Legend:** ⏳ has a calendar wait you cannot shorten · 💳 needs a payment card ·
🔒 blocks a release, not development

---

## Start these first — they have lead times

Nothing else on the list has a queue. These two do, and both sit on the critical
path, so starting them costs an email today and saves a week later.

### 1. Codemagic — repository connected ✅, three steps left

**Unblocks:** all macOS build minutes for Phase 0 and Phase 1.

The repository is connected. `codemagic.yaml` is now in the **repository root**,
which is the only place Codemagic looks — it was under `shells/ios/` before, so
_Check for configuration file_ would have found nothing.

⚠️ Codemagic's onboarding offers a **React Native** quick start. Ignore it. The
iOS shell is native Swift (SwiftPM + XcodeGen) and that guide configures a
toolchain this repository does not use. `codemagic.yaml` is already written for
the real one.

What is left:

1. **Click _Check for configuration file_.** Two workflows should appear:
   `iOS — unsigned verification build` and `iOS — build and ship to TestFlight`.
2. **Run `ios-verify` by hand.** It needs no Apple account and no credentials.
   ▶ This is the single most informative thing you can do right now: it is the
   proof behind the largest cost assumption in the business plan — that signed
   iOS builds are possible without owning a Mac. Green means the Phase 0
   toolchain assumption holds. Record the minutes it consumed in `COSTS.md`.
3. **Apply for the education account** ⏳ — free for students, teachers, and
   non-profits, applied for with a university address. It removes the
   500 macOS-minute ceiling that otherwise constrains every iOS build from
   Sprint 03 to Sprint 12. Takes days to process; free, and costs nothing if
   unused. Apply now and it processes while other work continues.

`ios-testflight` will fail at _Fetch signing files_ until items 3 and the Apple
enrolment below are done. That is the correct failure, not a misconfiguration.

⚠️ Neither workflow is triggered by a pull request, on purpose. Pull requests run
the shell's logic on free Linux minutes in GitHub Actions instead. Turning on
pull-request triggering here would spend the monthly macOS allowance in about a
week — see [ADR 0005](docs/adr/0005-shellcore-shellapp-split.md).

### 2. Google Play Console 💳 ⏳ 🔒

**Unblocks:** Sprint 03's kill gate (an app on Play internal testing).

**$25, one time.** Developer identity verification has taken days to weeks since
Google tightened it, and the Sprint 03 exit criteria depend on it. Registering
early means the verification queue runs in parallel with development rather than
after it.

### 3. Apple Developer Program 💳 ⏳ 🔒

**Unblocks:** Sprint 03's kill gate (an app on TestFlight). Sprint 03's planned
spend, and the first genuinely unavoidable cost in the programme.

**$99/year.** Enrolment is usually 24–48 hours, occasionally longer if identity
verification is escalated.

⚠️ Individual enrolment is fine to start. Publishing under an organisation later
needs a D-U-N-S number and a _separate_ enrolment — worth knowing now, because
customers will hit the same wall and it belongs in the publishing knowledge base.

⚠️ The App Store Connect API key (`.p8`) is downloadable **exactly once**. Save it
to a password manager immediately. It never goes in the repository — `gitleaks`
is configured to catch it, but that is the second line of defence, not the first.

Once enrolled, create a Codemagic environment variable group named
**`appstore_credentials`** holding `APP_STORE_CONNECT_ISSUER_ID`,
`APP_STORE_CONNECT_KEY_IDENTIFIER` and `APP_STORE_CONNECT_PRIVATE_KEY` (the
`.p8`), all marked secure. `codemagic.yaml` already expects that exact name.

---

## Infrastructure — Sprint 00, deferred

Full step-by-step instructions, with the pitfalls, are in
[`docs/ops/provisioning.md`](docs/ops/provisioning.md). Summary only here.

### 4. Billing alerts on every account 💳

⚠️ **Do this before provisioning anything.** A $10 alert on each account that can
bill. Screenshot into `docs/ops/billing-alerts/` and tick the table in
[`COSTS.md`](COSTS.md).

The whole cost model assumes free tiers hold. This is how you find out early when
one does not.

### 5. Oracle Cloud Always Free host 💳

**Unblocks:** Sprints 06–08 (the API, the database, the build orchestrator).

`VM.Standard.A1.Flex`, **2 OCPU / 12 GB** — not the older 4/24 figure, which
Oracle halved in mid-2026. A card is needed for identity but an Always Free
account is not charged.

⚠️ "Out of host capacity" is common. Try other availability domains and regions,
but **timebox it to three hours** — the fallback is a Hetzner CX22 at about
€4/month, recorded in `COSTS.md`.

### 6. Cloudflare account

**Unblocks:** artifact storage, the studio's public URL, the fixture sites, and
the public schema URL.

R2 bucket, a token scoped to that bucket only, and a Pages project. R2 has zero
egress fees, which is why artifacts, source exports, and later OTA bundles all
live there.

⚠️ Verify the negative case: the scoped token must be **denied** on a different
bucket. An untested scope is an assumed scope.

### 7. Free credits and student packs

GitHub Student Developer Pack (a free domain, extra Actions minutes, Sentry
credit), Azure for Students, and free accounts on Neon, Upstash, Sentry, Grafana
Cloud, Resend, UptimeRobot, and Appetize.

Record the limits you actually observe in `02_FREE_RESOURCE_PLAYBOOK.md`. They
differ from the published ones more often than not.

---

## Physical devices

### 8. An Android phone, and later an iPhone 🔒

**Unblocks:** the Sprint 02 and Sprint 03 criteria that no emulator can prove.

The checklist is [`docs/qa/physical-device-smoke.md`](docs/qa/physical-device-smoke.md).
An emulator does not catch permission dialogs, background eviction, or the way a
real network fails.

⚠️ Test on a **Samsung** as well as a Pixel. Samsung is the largest Android
install base by far and its System WebView update cadence differs from Google's —
`docs/qa/android-device-matrix.md` explains what else earns a slot.

---

## Decisions only you can make

### 9. Extract the shells into public repositories

[ADR 0002](docs/adr/0002-monorepo-with-public-shells.md) calls for
`shells/android` and `shells/ios` to be **separate public repositories**, brought
in as submodules. Two reasons: public repositories get unmetered GitHub Actions
minutes including macOS, and customers can audit the code that runs on their
users' phones.

They are in-tree for now. Creating public repositories under your account is not
something to do unprompted — say the word and it is a mechanical change.

⚠️ Whenever it happens: nothing secret may ever be committed there.

### 10. Set `main` as the repository's default branch

The repository was empty when this work started, so the first pushed branch
became the default. `main` exists now and is the trunk. GitHub → Settings →
General → Default branch.

### 11. Enable branch protection on `main`

Require a pull request, disallow force pushes, and require these three checks:

| Check                      | Workflow      | Covers                     |
| -------------------------- | ------------- | -------------------------- |
| `gate`                     | `ci.yml`      | lint, Node, .NET, security |
| `Build and unit test`      | `android.yml` | the Android shell          |
| `ShellCore build and test` | `ios.yml`     | the iOS shell's logic      |

⚠️ `gate` aggregates the jobs **in `ci.yml` only** — that is what makes adding a
job there free. The shells are separate workflows, so they need naming
separately; requiring `gate` alone would let a broken shell merge.

Do not require the macOS job. It only runs on `workflow_dispatch`, so requiring
it would block every pull request forever.

### 12. Confirm the image-library licensing decision

**Nothing is blocked; this is a decision made on your behalf that you may want
to overrule.**

The icon pipeline needs an image library. The sprint plan recommended
**ImageSharp**, and it has a real advantage — pure managed code, no native
binaries to go missing on the arm64 Oracle host.

⚠️ Its licence has a revenue trigger. The Six Labors Split License is
Apache-2.0 only while the consumer is open source, a non-profit, or a for-profit
under **1M USD annual gross revenue**. Above that it needs a paid commercial
licence. Version 4 also enforces this at build time — a Release build fails
outright without a key.

I chose **SkiaSharp** instead: MIT over Google's BSD-licensed Skia, no revenue
trigger, no key. The cost is native binaries per platform, which is a deployment
detail on hosts you control. Reasoning in
[ADR 0007](docs/adr/0007-image-pipeline.md).

If you would rather pay Six Labors later in exchange for a simpler deployment
today, say so — it is one class behind `IImagePipeline` and a golden-file
approval.

### 13. Close Dependabot's open security PRs

Dependabot keeps re-reporting advisories that are already fixed directly in the
branch, which shows as failing checks on the pull request. They are not from our
workflows and do not affect the `gate` check. Closing its open PRs from the
Security tab settles it.

### 14. Register OAuth applications with GitHub and Google

The sign-in flow is written and wired; it has never completed a real
authorisation code exchange, because that needs live credentials at both
providers. Account linking is tested directly, so what is unproven is the
redirect chain rather than the logic behind it.

**GitHub:** Settings → Developer settings → OAuth Apps → New OAuth App.
Callback `https://<api-host>/v1/auth/oauth/github/callback`.

**Google:** Cloud Console → APIs & Services → Credentials → OAuth client ID
(Web application). Same callback path with `google`.

Then set, from a secret store and never in `appsettings.json`:

```
Auth__Providers__github__ClientId
Auth__Providers__github__ClientSecret
Auth__Providers__google__ClientId
Auth__Providers__google__ClientSecret
```

A provider with no credentials is skipped rather than registered with empty
ones, so `/v1/auth/oauth/github` returns 404 until this is done — deliberately,
because the alternative is an endpoint that accepts a request and fails at the
provider with an error nobody can act on.

### 15. Create a Cloudflare R2 bucket for assets

Uploaded icons currently go to a directory on the API host. That is fine for
development and wrong for anything else: it does not survive a container
restart, and it does not exist on the second instance.

R2 has no egress charge and a 10 GB free tier, which is the reason it was chosen
over S3. Create a bucket and an API token scoped to it, then set
`AssetStorage__*`. The swap is one class — `FileSystemAssetBlobStore` behind
`IAssetBlobStore` — and the interface is already the seam.

### 16. Provide a signing key for access tokens

`Auth__SigningKey` must be at least 32 bytes, from a secret store.

```
openssl rand -base64 32
```

⚠️ The application refuses to start without one rather than generating one. A
generated key would silently invalidate every session on each restart and would
differ between instances, which presents as users being randomly signed out and
is very hard to diagnose.

### 17. Decide whether per-instance rate limits are acceptable

Rate limiting is in-process, so three instances mean three times the stated
limit. That is fine for protecting a host from a runaway client and not fine for
anything a customer is billed against.

Two ways forward, and it is a product decision rather than a technical one:
move the limiter to a shared store before the second instance exists, or write
down that the published limits are per instance. Either is defensible; the
current state — a limit that quietly means something else in production — is
not.

### 18. Get a Resend API key for transactional email

Verification and password-reset links are generated and, without a key, written
to the log with a warning rather than sent. That is deliberate and loud, because
a production deployment silently posting reset links into a log file is the
failure worth making impossible to miss.

Resend's free tier is 3,000 messages a month. Set `Email__ApiKey` and
`Email__From` on a domain verified with them.

---

## Running cost

Nothing has been spent. From [`COSTS.md`](COSTS.md):

| When      | Item                            | Amount      |
| --------- | ------------------------------- | ----------- |
| Sprint 02 | Google Play Console             | $25 once    |
| Sprint 03 | Apple Developer Program         | $99/year    |
| Sprint 13 | VPS, Appetize, managed Postgres | ~$60/month  |
| Sprint 20 | Mac host, ClickHouse, Sentry    | ~$250/month |

Phase 0 total is **$124**. Everything before Sprint 13 is designed to run on free
tiers, and `02_FREE_RESOURCE_PLAYBOOK.md` records the exact trigger for each
upgrade.
