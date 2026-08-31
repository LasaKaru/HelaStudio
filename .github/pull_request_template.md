## What this changes

<!-- One paragraph. What is different after this merges, and why. -->

## Sprint reference

- Sprint / task: <!-- e.g. S01 / T-01.4 -->
- Test case IDs covered: <!-- e.g. TC-S01-CFG-013 … TC-S01-CFG-034 -->

---

Review checklist from `01_ENGINEERING_STANDARDS.md` §9. Review your own PR against
it — solo discipline is the only discipline available.

## Correctness

- [ ] Acceptance criteria from the sprint file are all met
- [ ] All listed test case IDs pass
- [ ] Edge cases: empty, null, max size, unicode, concurrent, cancelled

## Performance

- [ ] No N+1 queries (checked the query log)
- [ ] No unbounded allocation on a user-controlled size
- [ ] Long operations are async, bounded, and cancellable
- [ ] Benchmark cited if this touches a hot path

## Security

- [ ] No secret in code, logs, or artifacts
- [ ] All inputs validated against the schema
- [ ] Authorisation checked at the resource level, not just the route
- [ ] No new dependency without a licence and maintenance check

## Maintainability

- [ ] No new warnings
- [ ] Public API documented
- [ ] Errors are typed, coded, and actionable
- [ ] No TODO without an issue link

## Ops

- [ ] Migration is backwards-compatible
- [ ] New config and environment variables documented and defaulted safely
- [ ] Logs carry a correlationId
- [ ] Feature is observable (a metric or a trace was added)
