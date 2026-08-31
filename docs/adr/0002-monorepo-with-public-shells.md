# 2. One monorepo, with the native shells split into public repositories

Date: 2026-08-31

## Status

Accepted

## Context

The programme spans six toolchains: C#, TypeScript, Kotlin, Swift, Gradle, and
Xcode. They share one artefact — `appconfig.json` — and that shared artefact is
the reason the system holds together. A change to the schema must be visible, in
one commit, to the validator, the code generators, the studio, and both shells.

Cutting against that is the cost of macOS CI. GitHub Actions bills macOS runners
at ten times the Linux rate against a private repository's included minutes, and
Phase 0 and Phase 1 have no budget for that (`02_FREE_RESOURCE_PLAYBOOK.md`).
Public repositories get unmetered Actions minutes, macOS included.

The shells are also the part of the system a customer is most entitled to
inspect. They run on the customer's users' devices, holding sessions and
requesting camera and location permissions. "Trust our closed binary" is a
weaker position than "read the code".

## Decision

One monorepo holds everything that is private: the control plane, the code
generators, the studio, the configuration schema, and the plugin registry.

`shells/android` and `shells/ios` are **separate public repositories**, brought
in as git submodules. They contain no business logic — only the native runtime
that a generated project is assembled from.

The split is made now, in Sprint 00, while both directories are empty. Splitting
a repository after it carries history is painful, and the CI-minutes rationale
only pays out if the split exists before the macOS builds begin in Sprint 03.

## Consequences

- macOS CI minutes are unmetered for shell work, which is where they are spent.
- The shells are auditable by customers and by store reviewers, which is a
  genuine differentiator against a category built on opaque binaries.
- A schema change touching both a generator and a shell now spans two
  repositories and two pull requests. The contract tests are what stop them
  drifting: the shells consume the published schema package rather than a copy.
- Shell versions are semver'd independently (`shell-android@1.4.0`). A generated
  app pins the shell version it was built from, and must stay reproducible
  forever, so a shell release is a tagged, immutable artefact.
- Nothing secret may ever enter a shell repository. The `gitleaks` configuration
  and the ban on committed signing material matter more there than anywhere else.
