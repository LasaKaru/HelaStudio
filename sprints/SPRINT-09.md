# Sprint 09 — Bridge Protocol & SDK

|                   |                      |
| ----------------- | -------------------- |
| **Weeks**         | 19–20                |
| **Phase**         | 1 — Product          |
| **Capacity**      | 55 h (38 h new work) |
| **Depends on**    | S02, S03             |
| **Blocks**        | S10                  |
| **Planned spend** | $0                   |

---

## 1. Sprint goal

Design and implement the JavaScript bridge: a versioned, capability-negotiated, promise-based protocol implemented three times (TypeScript, Kotlin, Swift) and held in agreement by a shared contract-test fixture corpus.

⚠️ **One-way door.** Once customer websites call `sw.biometric.authenticate()`, the signature is permanent. Write the ADR first.

---

## 2. Exit criteria

- [ ] Protocol specified in `docs/reference/bridge-protocol.md` with a versioning policy
- [ ] Three implementations agreeing on a shared fixture corpus, enforced in CI
- [ ] `@shellwright/bridge` published to npm with full TypeScript types
- [ ] ⚠️ Capability negotiation working — web code branches on capability, never on user-agent
- [ ] Browser shim: the SDK is a safe no-op outside the app, so one codebase serves web and app
- [ ] ⚠️ Origin allowlist enforced natively — no bridge object exists on non-allowlisted origins
- [ ] Bridge inspector panel showing live envelopes in debug builds
- [ ] Coverage ≥ 90% line / 85% branch on all three implementations

---

## 3. Task breakdown

| ID     | Task                                      | Est.     | Priority |
| ------ | ----------------------------------------- | -------- | -------- |
| T-09.1 | Protocol design and ADR                   | 6 h      | P0       |
| T-09.2 | Native dispatcher — Android               | 6 h      | P0       |
| T-09.3 | Native dispatcher — iOS                   | 6 h      | P0       |
| T-09.4 | TypeScript SDK and npm package            | 8 h      | P0       |
| T-09.5 | Contract test corpus and three-way runner | 7 h      | P0       |
| T-09.6 | Bridge inspector                          | 5 h      | P1       |
|        | **Total**                                 | **38 h** |          |

---

## 4. Task detail

### T-09.1 — Protocol design and ADR (6 h)

**Envelope** (from master spec §13.6):

```jsonc
{
  "v": 1,
  "id": "01J...",
  "type": "request",
  "plugin": "biometric",
  "method": "authenticate",
  "params": { "reason": "Unlock" },
  "meta": { "ts": 1756600000000 },
}
```

**Decisions to record in `docs/adr/0008-bridge-protocol.md`:**

| Decision                   | Choice                                                                                                     | Rationale                                                                                            |
| -------------------------- | ---------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------- |
| Transport                  | iOS `WKScriptMessageHandlerWithReply`; Android `@JavascriptInterface` + `evaluateJavascript` for responses | Native, no polling, no custom URL-scheme hacks                                                       |
| ⚠️ Android injection point | `addJavascriptInterface` **only after** the origin allowlist check, and removed on navigation away         | `addJavascriptInterface` is the classic Android WebView RCE vector. Never expose it unconditionally. |
| Correlation                | ULID request ids                                                                                           | Sortable, collision-free, no coordination                                                            |
| Versioning                 | Integer protocol version + per-method `since`/`deprecated`                                                 | Old apps must keep working with new SDKs and vice versa                                              |
| Unknown method             | ⚠️ Reject with `BRIDGE_METHOD_UNSUPPORTED`, never silently resolve                                         | Silent no-ops produce bugs the customer cannot diagnose                                              |
| Errors                     | `{ code, message, retryable, details }`, never a bare string                                               | Actionable, searchable, testable                                                                     |
| Large payloads             | ⚠️ > 64 KB rejected; pass a URL or handle instead                                                          | Bridge serialisation is synchronous-ish and blocks                                                   |
| Events                     | Native → web via a subscription registry, batched per animation frame                                      | Avoids event storms on keyboard/scroll                                                               |

**Compatibility rules — write them down and honour them:**

1. Adding a method or an optional parameter is non-breaking.
2. ⚠️ Removing a method, renaming, changing a type, or making an optional parameter required is **breaking** and requires a protocol version bump.
3. A shell at protocol v1 receiving a v2 envelope responds `BRIDGE_VERSION_UNSUPPORTED` with the version it speaks — the SDK then degrades rather than hanging.
4. Deprecated methods keep working for **at least 12 months** after deprecation.

**Acceptance criteria:** ADR merged; protocol document published; the compatibility rules have a test each.

**Tests:** `TC-S09-BRG-001` … `TC-S09-BRG-004`

---

### T-09.2 — Native dispatcher, Android (6 h)

```kotlin
class BridgeDispatcher(
    private val registry: PluginRegistry,
    private val allowlist: OriginAllowlist,
    private val scope: CoroutineScope,
) {
    @JavascriptInterface
    fun postMessage(json: String) { /* parse → validate → route → respond */ }
}
```

**Requirements:**

- ⚠️ **Origin check on every message**, not only at injection time. A page can navigate or embed an iframe; re-verify the current origin before dispatch.
- ⚠️ Payload size cap enforced **before** parsing (check the string length first) — a 50 MB string will OOM before any handler sees it.
- **Per-method rate limiting** with a token bucket. A runaway `while(true)` in customer JS must not lock the UI thread.
- Dispatch on a background dispatcher; marshal to the main thread only for handlers that need UI.
- Responses via `evaluateJavascript` with ⚠️ **properly escaped** JSON (a response containing `</script>` or a lone surrogate must not break the page).
- Plugin handlers resolved from a **generated** registry (no reflection — reflection breaks under R8 and costs startup time).
- ⚠️ **Lazy plugin initialisation:** a plugin's SDK initialises on its first bridge call, never at app launch. This is what keeps cold start under 300 ms with 15 plugins installed.

**Acceptance criteria:** methods dispatch and respond; a non-allowlisted iframe gets no bridge; an oversized payload is rejected cheaply; rate limiting engages.

**Tests:** `TC-S09-BRG-005` … `TC-S09-BRG-014`, `TC-S09-SEC-001`, `TC-S09-SEC-002`

---

### T-09.3 — Native dispatcher, iOS (6 h)

```swift
final class BridgeDispatcher: NSObject, WKScriptMessageHandlerWithReply {
    func userContentController(_ c: WKUserContentController,
                               didReceive message: WKScriptMessage,
                               replyHandler: @escaping (Any?, String?) -> Void) { … }
}
```

**Requirements mirror Android, with iOS specifics:**

- ⚠️ Use `WKScriptMessageHandlerWithReply` (iOS 14+) rather than the older handler plus an injected callback table — it gives native promise semantics and avoids a whole class of leaked-callback bugs.
- ⚠️ **Register the handler on a per-`WKUserContentController` basis and remove it on navigation to a non-allowlisted origin.** Handlers are retained by the content controller; failing to remove them leaks the WebView and can expose the bridge after navigation.
- ⚠️ **Retain-cycle discipline:** the message handler holds a strong reference to its target by design. Use a weak proxy or the WebView will never deallocate — a known, subtle iOS leak that shows up as memory growth across many modal windows.
- Same size cap, rate limiting, lazy plugin init, and generated registry.

**Acceptance criteria:** parity with Android on the shared fixture corpus; no WebView leak across 50 open/close cycles (instrumented test).

**Tests:** `TC-S09-BRG-015` … `TC-S09-BRG-024`, `TC-S09-SEC-003`

---

### T-09.4 — TypeScript SDK and npm package (8 h)

**Package: `@shellwright/bridge`**

```ts
import sw from '@shellwright/bridge';

if (sw.isNativeApp) {
  const caps = await sw.capabilities();
  if (caps.biometric?.includes('authenticate')) {
    await sw.biometric.authenticate({ reason: 'Unlock' });
  }
}
```

**Requirements:**

- ⚠️ **Browser shim.** Outside the app, `isNativeApp` is `false`, `capabilities()` returns `{}`, and every method rejects with `BRIDGE_NOT_AVAILABLE`. ⚠️ It must **never throw on import** — importing the SDK during SSR (Next.js, Nuxt, Astro) must be safe, which means no `window` access at module scope. This single detail decides whether the SDK is usable by the modern web stack.
- **Tree-shakeable**: `import { authenticate } from '@shellwright/bridge/biometric'` pulls only that module. ESM + CJS + types, `sideEffects: false`.
- **Bundle budget: < 4 KB gzipped** for the core. Enforce with `size-limit` in CI. A heavy SDK is a reason not to adopt.
- Typed errors as a discriminated union so `switch` on `err.code` is exhaustive.
- Timeouts per call, with a sensible default (10 s) and per-call override; a hung native handler must not hang the page forever.
- Event API returning an unsubscribe function (never a `removeListener` by identity — that pattern leaks).
- **Framework packages** (thin, P1): `@shellwright/react` exposing `useCapabilities()`, `useAppEvent()`, `useNativeApp()`.
- Generated API docs published to the docs site from TSDoc.

**Acceptance criteria:** published to npm; SSR-safe import verified in a Next.js test app; core bundle < 4 KB gzipped; types resolve under `moduleResolution: bundler` and `node16`.

**Tests:** `TC-S09-BRG-025` … `TC-S09-BRG-036`

---

### T-09.5 — Contract test corpus and three-way runner (7 h)

⚠️ **This is the sprint's most valuable artifact.** It is what stops the SDK and the shells drifting apart over 27 sprints.

**Structure:**

```
packages/bridge-protocol/fixtures/
  biometric.authenticate.success.json
  biometric.authenticate.not-enrolled.json
  clipboard.write.oversized.json
  core.capabilities.v1.json
  core.unknown-method.json
  ...
```

Each fixture:

```jsonc
{
  "description": "authenticate resolves when biometry succeeds",
  "protocolVersion": 1,
  "input": { "plugin": "biometric", "method": "authenticate", "params": { "reason": "Unlock" } },
  "expectedEnvelope": {
    /* exact serialised request, canonical form */
  },
  "mockNativeResponse": { "type": "response", "result": { "authenticated": true } },
  "expectedResult": { "authenticated": true },
}
```

**Three runners** — Vitest, JUnit, Swift Testing — each loading the same files from the same directory. CI fails if any implementation disagrees.

⚠️ **Process rule: a new bridge method may not be merged without fixtures.** Enforce it with a CI check that every method in the generated registry has at least one success and one error fixture.

**Acceptance criteria:** all three runners green on the full corpus; deliberately changing one implementation's serialisation fails its runner and only its runner.

**Tests:** `TC-S09-BRG-037` … `TC-S09-BRG-042`

---

### T-09.6 — Bridge inspector (5 h)

A debug-only overlay showing every envelope live: timestamp, direction, plugin, method, params, result or error, and duration.

**Why it earns a sprint slot:** bridge debugging is otherwise invisible. A customer says "biometrics doesn't work"; without an inspector, diagnosing it means a screen-share. With it, they send a screenshot. This is a support-cost reduction and a genuine developer-experience differentiator — no competitor ships one.

**Requirements:**

- ⚠️ **Debug builds only**, compiled out of release via build flags. Verify with a test asserting the symbol is absent from the release binary.
- Toggle via a config flag plus a hidden gesture (five-finger tap / triple two-finger tap).
- Filter by plugin, highlight errors, copy an envelope to the clipboard.
- Show measured call duration — surfaces slow native handlers immediately.

**Acceptance criteria:** inspector shows calls in real time; absent from release builds (asserted).

**Tests:** `TC-S09-BRG-043`, `TC-S09-BRG-044`, `TC-S09-SEC-004`

---

## 5. Test cases (selected detail)

| ID               | Type                   | Precondition                                                                | Steps                                         | Expected                                                                     |
| ---------------- | ---------------------- | --------------------------------------------------------------------------- | --------------------------------------------- | ---------------------------------------------------------------------------- |
| `TC-S09-SEC-001` | Instrumented (Android) | App on an allowlisted page containing an iframe to a non-allowlisted origin | Evaluate `typeof window.__sw` in the iframe   | `"undefined"`                                                                |
| `TC-S09-SEC-002` | Instrumented           | App loaded                                                                  | Post a 50 MB string to the bridge             | Rejected without parsing; no OOM; app responsive                             |
| `TC-S09-SEC-003` | Instrumented (iOS)     | App loaded                                                                  | Open and close 50 modal WebViews              | No WebView instances retained; memory returns to baseline                    |
| `TC-S09-SEC-004` | CI                     | Release binary                                                              | Search for inspector symbols                  | Absent                                                                       |
| `TC-S09-BRG-009` | Instrumented           | App loaded                                                                  | Call an unknown method                        | Rejects with `BRIDGE_METHOD_UNSUPPORTED`; never hangs                        |
| `TC-S09-BRG-012` | Instrumented           | App loaded                                                                  | Call `clipboard.write` 1,000× in a tight loop | Rate limiter engages; UI stays responsive; caller gets `BRIDGE_RATE_LIMITED` |
| `TC-S09-BRG-018` | Instrumented           | App with 15 plugins configured                                              | Cold start                                    | ⚠️ Zero plugin SDKs initialised; startup budget still met                    |
| `TC-S09-BRG-027` | Unit                   | Node SSR environment, no `window`                                           | `import sw from '@shellwright/bridge'`        | No throw; `isNativeApp === false`                                            |
| `TC-S09-BRG-030` | Unit                   | Browser, not in app                                                         | `await sw.biometric.authenticate({})`         | Rejects with `BRIDGE_NOT_AVAILABLE`                                          |
| `TC-S09-BRG-033` | CI                     | Built package                                                               | Measure core bundle                           | < 4 KB gzipped                                                               |
| `TC-S09-BRG-038` | Contract ×3            | Fixture corpus                                                              | Run all fixtures in TS, Kotlin, Swift         | All three produce identical envelopes and results                            |
| `TC-S09-BRG-041` | Contract               | Shell speaking v1                                                           | Send a v2 envelope                            | `BRIDGE_VERSION_UNSUPPORTED` naming v1                                       |

---

## 6. Risks

| Risk                                               | Likelihood         | Impact       | Mitigation                                                                                                                                        |
| -------------------------------------------------- | ------------------ | ------------ | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| ⚠️ Protocol design mistake locks in bad API        | Medium             | **High**     | ADR first; timebox to 6 h; explicit versioning policy; capability negotiation means a bad method can be deprecated rather than removed            |
| Three implementations drift                        | **High**           | High         | The contract corpus is the entire defence — and the "no method without fixtures" CI check is what keeps it honest                                 |
| ⚠️ Android `addJavascriptInterface` misused → RCE  | Low but **severe** | **Critical** | Origin check on every message, not just at injection; security test `TC-S09-SEC-001` on every PR; a security review of this specific file at S25  |
| SDK too large or SSR-unsafe → developers reject it | Medium             | High         | 4 KB budget and SSR test in CI from the first release                                                                                             |
| Scope creep into building plugins                  | **High**           | Medium       | ⚠️ No plugins this sprint. Two trivial built-in methods (`device.info`, `clipboard`) exist purely to exercise the protocol. Real plugins are S10. |

---

## 7. Deliverables

- `docs/reference/bridge-protocol.md` and `docs/adr/0008-bridge-protocol.md`
- Native dispatchers in both shells with origin gating, rate limiting, and lazy plugin init
- `@shellwright/bridge` on npm, SSR-safe, < 4 KB, fully typed
- `packages/bridge-protocol/fixtures` with three-way contract runners in CI
- Bridge inspector (debug builds only)
- `SPRINT-09_REVIEW.md`
