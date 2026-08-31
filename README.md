# Shellwright

A cloud platform that takes a URL and a configuration, and produces signed,
store-ready iOS and Android apps: native chrome, a live web view, and a
JavaScript bridge that lets the website drive real device capability.

The user never installs Xcode, never touches a certificate, and never learns
Swift or Kotlin.

## Status

Repository initialised. Development lands here through pull requests, sprint by
sprint, against the plan in `00_MASTER_SPRINT_PLAN.md`.

| Phase | Sprints | Milestone |
|---|---|---|
| 0 — Proof | S00–S03 | A config JSON puts an app on a real phone via TestFlight and Play internal testing |
| 1 — Pipeline | S04–S08 | A config submitted by API produces signed artifacts on cloud runners |
| 1 — Product | S09–S12 | Private alpha: ten external users build and install their own app |
| 2 — Beta | S13–S19 | Public beta: self-serve signup through to store submission |
| 3 — Commercial | S20–S26 | GA: first-party push, analytics, OTA, offline, agency tier |

## The idea in one paragraph

Median.co has led this category since 2014, and roughly 70% of the price gap
between its free and top tiers is charged for things that cost it nothing per
user: removing a watermark, allowing more plugins, adding a team seat. The
strategy here inverts that. Every software capability is free — every plugin,
watermark-free builds, unlimited seats, full source export. Revenue comes from
what genuinely costs something: iOS build and simulator minutes beyond a
generous allowance, first-party push and analytics at volume, managed
publishing, and enterprise controls.

The full analysis is in `SHELLWRIGHT_MASTER_SPEC.md`, which arrives with the
first pull request.
