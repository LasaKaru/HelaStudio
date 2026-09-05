# Android build runner

The container one build runs in, and nothing else runs in.

```
docker build -t shellwright/runner-android:latest infra/runner/android
bash infra/runner/android/create-network.sh
```

## What makes it safe

The image is half the story. The other half is how the orchestrator starts it,
which lives in `SandboxHardening.RunArguments` — a read-only root filesystem,
`--cap-drop=ALL`, `no-new-privileges`, an unprivileged user, bounded memory,
CPU and process count, and an egress-restricted network.

⚠️ **A missing flag has no symptom.** A container started without
`--cap-drop=ALL` builds exactly as well as one started with it. That is why the
flags are data in one place with a test over them, rather than a command line
assembled where nobody reads it.

## Egress

⚠️ An allowlist, not a blocklist. A build executes the customer's dependency
graph — Gradle plugins, transitive libraries, annotation processors — any of
which can open a socket. The question is not which hosts are dangerous but
which five a build legitimately needs:

- `repo.maven.apache.org`
- `dl.google.com`
- `maven.google.com`
- `plugins.gradle.org`
- `services.gradle.org`

`create-network.sh` builds a Docker network whose egress is confined to those.
Anything else is refused, which is what `TC-S07-SEC-001` checks.

## Unverified here

⚠️ **Nothing in this repository has ever run this image.** There is no container
runtime in the development environment, so what the test suite asserts is that
the correct arguments are produced — not that Docker then honours them, and not
that the image builds.

This is the same shape of gap as the iOS generator's: Sprint 04 established that
a generator passing every unit test says nothing about whether the toolchain
accepts its output, and a sandbox is no different. Recorded in
`SPRINT-07_REVIEW.md` and in `ACTION_REQUIRED.md` rather than left to be
discovered.
