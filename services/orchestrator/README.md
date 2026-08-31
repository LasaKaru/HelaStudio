# Build orchestration

Empty until **Sprint 07**.

Temporal workflows driving Linux and macOS runners. Retries, cancellation, and
log streaming are the reasons for Temporal rather than a queue.

Two rules from `01_ENGINEERING_STANDARDS.md` that shape this service:

- Every build runs in a fresh, isolated environment, destroyed after. No reuse
  across tenants, ever.
- A cancelled build must free its runner within five seconds. On a metered Mac
  fleet, an uncancellable build is money on fire.
