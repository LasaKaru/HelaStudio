# Costs

Actual spend, recorded per sprint. The plan is in `00_MASTER_SPRINT_PLAN.md` §11
and the free-tier limits are in `02_FREE_RESOURCE_PLAYBOOK.md`.

The rule for this table: record what was actually charged, not what was
budgeted. A free tier that quietly started billing is exactly what this file
exists to catch.

## Running total

| Sprint | Planned | Actual | Running | Notes                                                                                                  |
| ------ | ------- | ------ | ------- | ------------------------------------------------------------------------------------------------------ |
| S00    | $0      | **$0** | $0      | All development toolchains are free. Cloud provisioning not yet done — see `docs/ops/provisioning.md`. |
| S01    | $0      | **$0** | $0      | No infrastructure needed; the validation engine runs locally and in CI.                                |

## Committed and recurring

Nothing yet.

## Expected next

| When | Item                            | Amount       | Why it becomes unavoidable                                                                               |
| ---- | ------------------------------- | ------------ | -------------------------------------------------------------------------------------------------------- |
| S02  | Google Play Console             | $25 one-time | Needed to reach internal testing, which is the Sprint 03 kill-gate criterion.                            |
| S03  | Apple Developer Program         | $99/year     | ⚠️ No way to reach TestFlight without it. This is the first genuinely unavoidable cost in the programme. |
| S13  | VPS, Appetize, managed Postgres | ~$60/month   | Free tiers are outgrown once device preview is real.                                                     |
| S20  | Mac host, ClickHouse, Sentry    | ~$250/month  | Should be revenue-covered by this point.                                                                 |

## Billing alerts

⚠️ Every account that can bill must carry a $10 alert **before** anything is
provisioned on it. Record confirmations in `docs/ops/billing-alerts/`.

| Account      | Alert set | Confirmed |
| ------------ | --------- | --------- |
| Oracle Cloud | ☐         | —         |
| Cloudflare   | ☐         | —         |
| Codemagic    | ☐         | —         |
| Neon         | ☐         | —         |
| Upstash      | ☐         | —         |
