# Provisioning

The Sprint 00 tasks that need real accounts, and so could not be done from a
development container. Everything else in Sprint 00 is committed and green.

Work them in this order. Task T-00.1 first, on day one, because the applications
take days to process.

---

## T-00.1 — Free credits, student packs, and accounts (2 h)

Two hours here buys twelve months of free infrastructure.

1. **Codemagic education account.** ⚠️ Do this first. It is the highest-value
   application in the programme: it removes the 500 macOS-minute ceiling that
   otherwise constrains all of Phase 0 and Phase 1. Apply with a university
   address.
2. **GitHub Student Developer Pack.** Unlocks a free domain, extra Actions
   minutes, JetBrains, Sentry, and cloud credits.
3. **Oracle Cloud Free Tier.** ⚠️ Requires a card for identity, but an Always
   Free account is not charged. Choose a region with A1 capacity.
4. **Cloudflare**, **Azure for Students**, and free accounts on Neon, Upstash,
   Sentry, Grafana Cloud, Resend, UptimeRobot, and Appetize.
5. ⚠️ **Set a $10 billing alert on every account that can bill you, before
   provisioning anything on it.** Screenshot each into
   `docs/ops/billing-alerts/` and tick the table in `COSTS.md`.
6. Record the limits you actually observe in `02_FREE_RESOURCE_PLAYBOOK.md`.
   They differ from the published ones more often than not.

Credentials go in a password manager. Never in this repository — `gitleaks` runs
on every commit and every pull request, but it is the second line of defence,
not the first.

**Verifies:** `TC-S00-OPS-001`

---

## T-00.5 — Oracle Always Free host (5 h)

⚠️ Plan for **2 OCPU / 12 GB**, not the older 4/24 figure. Oracle halved the
Always Free ARM allocation in mid-2026 and began enforcing it in August 2026.

1. Launch `VM.Standard.A1.Flex`, 2 OCPU / 12 GB, Ubuntu 24.04 LTS (arm64),
   100 GB boot volume. On "Out of host capacity", try another availability
   domain or region — this is common and does not mean the tier is gone.
   **Timebox this to three hours**; the fallback is a Hetzner CX22 at about
   €4/month, recorded in `COSTS.md`.
2. Harden: key-only SSH, a non-root user, `ufw` allowing 22/80/443,
   `fail2ban`, unattended upgrades. ⚠️ Oracle's default iptables rules need
   editing too — the security list _and_ the host firewall both matter, and
   this catches almost everyone.
3. Install Docker and the Compose plugin (arm64).
4. **Verify an arm64 image exists for every planned dependency.** This is the
   real deliverable: a gap found now changes the architecture cheaply, a gap
   found in Sprint 07 does not. Record results in `docs/ops/arm64-compat.md`.
5. Write `infra/compose/docker-compose.yml` with **explicit memory limits per
   service**. 12 GB will not survive an unbounded Gradle daemon sitting next to
   Postgres.
6. Install Caddy for automatic TLS.
7. Nightly `pg_dump` to R2 by cron, and **restore it once** to prove it works.
   An untested backup is not a backup.

**Verifies:** `TC-S00-OPS-002`, `TC-S00-OPS-003`, `TC-S00-SEC-002`

---

## T-00.6 — Cloudflare (3 h)

1. R2 bucket `shellwright-artifacts-dev`. R2 has zero egress fees, which is why
   artifacts, source exports, and later OTA bundles all live there.
2. An API token scoped to **that bucket only**. Then verify the negative case:
   the token must be denied on a different bucket. An untested scope is an
   assumed scope.
3. A Pages project for the studio; deploy a placeholder.
4. Publish the schema at `https://schema.shellwright.dev/appconfig/v1.json` so
   editors give autocomplete on hand-written configs. Small effort,
   disproportionate goodwill.
5. Deploy the three fixture sites from `tests/fixtures/sites/` under distinct
   subdomains. The auth site's endpoints need Pages Functions mirroring
   `serve.mjs`.
6. Enable the free WAF ruleset and a rate limit on `/v1/*`.

**Verifies:** `TC-S00-OPS-004`, `TC-S00-OPS-005`, `TC-S00-SEC-003`

---

## Branch protection

Once CI has run once on `main`:

- Require the `gate` check. It aggregates every job, so adding a job later never
  requires touching the protection rule.
- Require a pull request. Disallow force pushes.
