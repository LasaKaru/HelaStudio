# Contributing

## Branching

```
main                                  always deployable, protected, CI-gated
 └─ feat/S07-temporal-workflows        one branch per task or tight task group
 └─ fix/S07-build-timeout
 └─ chore/S07-bump-agp
```

Squash merge only: one task, one commit on `main`. Tag each sprint end
(`sprint-07`) and each milestone (`v0.1.0-alpha`).

## Commits

[Conventional Commits](https://www.conventionalcommits.org/), enforced by
`commitlint` in the pre-commit hook and again in CI:

```
feat(config-schema): add catastrophic backtracking detection
fix(studio): stop the report flashing on every keystroke
docs(adr): record the three-way cache key decision
```

Types: `feat`, `fix`, `chore`, `perf`, `test`, `docs`, `refactor`, `build`,
`ci`, `revert`. Scopes are kebab-case.

## Before you start a task

From `00_MASTER_SPRINT_PLAN.md` §6, a task is not ready unless:

- [ ] It has an ID and an hour estimate
- [ ] Its acceptance criteria are written and testable
- [ ] Its test case IDs are listed
- [ ] Its dependencies are complete
- [ ] It fits in one sprint

## Before you open a pull request

Run what CI runs:

```bash
pnpm format:check && pnpm lint && pnpm typecheck && pnpm test:coverage && pnpm build
dotnet build -c Release && dotnet test -c Release
```

`TreatWarningsAsErrors` is on everywhere and coverage gates are enforced, so a
green local run is a green CI run.

## Two things that will bite you

**Changing the schema.** `packages/config-schema/schema/appconfig.v1.json` feeds
generated TypeScript types _and_ two validators. After editing it:

```bash
pnpm --filter @shellwright/config-schema generate       # regenerate types
pnpm --filter @shellwright/config-schema exec tsx scripts/write-goldens.ts
```

Then read the golden diff. It is the diff every existing customer would see.
CI fails if either output is stale.

**Changing a diagnostic message.** The two validators must produce byte-identical
text. Change it in `src/validate.ts` _and_ `SchemaMessages.cs`, regenerate the
goldens, and run `dotnet test` — the cross-language contract test is what catches
a half-done change.

## Definition of done

A task is not done until: merged via a pull request, all listed test cases pass,
the coverage gate is met, there are no new warnings, no `TODO` lacks an issue
link, public API changes are documented, `CHANGELOG.md` is updated, and the
secrets scan is clean. If it touches the build pipeline, a real APK or IPA was
produced and installed on a physical device.
