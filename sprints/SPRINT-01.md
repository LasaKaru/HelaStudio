# Sprint 01 — Config Schema & Validation Engine

|                   |                         |
| ----------------- | ----------------------- |
| **Weeks**         | 3–4                     |
| **Phase**         | 0 — Proof               |
| **Capacity**      | 55 h (38 h new work)    |
| **Depends on**    | S00                     |
| **Blocks**        | S02, S03, S04, S05, S06 |
| **Planned spend** | $0                      |

---

## 1. Sprint goal

Define `appconfig.json` v1 — the single source of truth for the entire platform — and build the validation, canonicalisation, migration, and hashing machinery around it.

⚠️ **This is a one-way door.** Every generated project, every cache key, every studio form, and every customer's stored configuration depends on this schema's shape. Write an ADR before implementing. Get it wrong and you will be writing migrations for two years.

---

## 2. Exit criteria

- [ ] JSON Schema (Draft 2020-12) for `appconfig` v1 published in `packages/config-schema`
- [ ] Validation runs identically in C# and TypeScript against a shared fixture corpus, with byte-identical error output
- [ ] Canonical JSON serialiser produces stable bytes; property test proves order-independence
- [ ] `codeKey` / `assetKey` / `contentKey` hash split implemented and tested
- [ ] Migration framework exists with a proven v0→v1 migration and a round-trip test
- [ ] Fixture corpus: `minimal`, `maximal`, `all-plugins`, `unicode`, and ≥ 6 `edge-*` configs
- [ ] Validation of the `maximal` fixture completes in < 50 ms
- [ ] Coverage ≥ 95% line / 90% branch on this package

---

## 3. Task breakdown

| ID     | Task                                  | Est.     | Priority |
| ------ | ------------------------------------- | -------- | -------- |
| T-01.1 | ADR + schema design                   | 6 h      | P0       |
| T-01.2 | JSON Schema authoring (v1)            | 7 h      | P0       |
| T-01.3 | Type generation (C# + TypeScript)     | 4 h      | P0       |
| T-01.4 | Validation engine with semantic rules | 8 h      | P0       |
| T-01.5 | Canonical JSON + hash split           | 5 h      | P0       |
| T-01.6 | Migration framework                   | 5 h      | P0       |
| T-01.7 | Fixture corpus                        | 3 h      | P0       |
|        | **Total**                             | **38 h** |          |

---

## 4. Task detail

### T-01.1 — ADR + schema design (6 h)

**Objective:** Decide the shape before writing it.

**Decisions to record in `docs/adr/0003-appconfig-schema-v1.md`:**

| Decision               | Options                                            | Recommendation                                                                                                                  |
| ---------------------- | -------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| Versioning strategy    | Field `schemaVersion` / URL-based `$schema` / both | **Both.** `$schema` for tooling, `schemaVersion` integer for migration logic.                                                   |
| Unknown fields         | Reject / ignore / preserve                         | **Reject on save, preserve on read.** Strict on input catches typos; lenient on read survives rollbacks.                        |
| Plugin config location | Nested under `plugins.<id>` / flat                 | **Nested**, validated against each plugin's own `configSchema`                                                                  |
| Secrets in config      | Allowed / forbidden                                | ⚠️ **Forbidden.** Config is stored, hashed, logged, and exported. Secrets go in a separate credentials store, referenced by id. |
| Asset references       | Inline base64 / R2 URI / repo path                 | **Content-addressed URI** (`asset://sha256-…`) — makes `assetKey` trivial and deduplicates icons across apps                    |
| Defaults               | In schema / in code                                | **In schema.** One source of truth; the studio renders them; codegen reads the resolved document.                               |
| Extensibility          | `x-` prefixed fields                               | Allow `x-` prefixed objects, ignored by codegen — gives an escape hatch without schema churn                                    |

**Design principles to write down and follow:**

1. **Flat where possible, nested where it maps to a real subsystem.** `branding`, `navigation`, `linkRules`, `webOverrides`, `plugins`, `build`.
2. **Every field has a default.** `minimal.json` must be ~10 lines and produce a working app.
3. **No field means two things.** If `orientation` can be a string or an object, split it.
4. **Arrays of objects carry a stable `id`.** Tab items, nav buttons, and link rules all need identity for the studio's drag-and-drop and for diffs.
5. **Nothing platform-specific at the top level.** Use `ios: {}` / `android: {}` sub-objects only where behaviour genuinely diverges.

**Acceptance criteria:** ADR merged; a reviewer (you, a week later) can rebuild the schema from it.

---

### T-01.2 — JSON Schema authoring (7 h)

**Steps:**

1. Author `packages/config-schema/schema/appconfig.v1.json` (Draft 2020-12). Base it on Appendix B of the master spec.
2. Split into `$defs` for reusable shapes: `Color`, `AssetRef`, `UrlPattern`, `NavItem`, `LinkRule`, `LocalizedString`.
3. Add tight constraints — this is where validation quality comes from:
   - `bundleId`: `^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$`, 2–155 chars ⚠️ (Apple and Google both reject uppercase and leading digits in segments)
   - `versionName`: semver-ish `^\d+(\.\d+){0,2}$`
   - `versionCode`: integer 1–2100000000 ⚠️ (Play's hard ceiling)
   - `color`: `^#([0-9a-fA-F]{6}|[0-9a-fA-F]{8})$`
   - `initialUrl`: `format: uri`, `pattern: ^https://` ⚠️ (HTTP-only origins fail ATS and Android cleartext policy by default — reject at config time, not build time)
   - `allowedOrigins`: `minItems: 1`, each an https origin
   - tab items: `maxItems: 5` ⚠️ (iOS shows a "More" tab beyond 5; warn rather than reject, but the constraint must be expressed)
   - app name: 1–30 chars ⚠️ (App Store limit)
4. Add `title` and `description` to **every** property. These render as help text in the studio for free — write them as user-facing copy, not developer notes.
5. Add `examples` to each property; they feed both docs and fixtures.
6. Publish the schema to a stable URL on Cloudflare Pages (`https://schema.shellwright.dev/appconfig/v1.json`) so editors like VS Code give users autocomplete on hand-written configs. Small effort, disproportionate developer goodwill.

**Acceptance criteria:**

- Schema self-validates against the Draft 2020-12 meta-schema
- Every property has a description
- `minimal.json` validates; a config with a bundle id of `Com.Foo` fails with a clear message

**Tests:** `TC-S01-CFG-001` … `TC-S01-CFG-010`

---

### T-01.3 — Type generation (4 h)

**Objective:** One schema, two languages, zero drift.

**Steps:**

1. **TypeScript:** generate types with `json-schema-to-typescript` as a build step in `packages/config-schema`. Commit the output so consumers do not need the generator, and add a CI check that regeneration produces no diff.
2. **C#:** generate records with NJsonSchema (or hand-write and add a contract test asserting equivalence — see below).
3. **Add a contract test that is worth more than either generator:** for every fixture, deserialise in C#, re-serialise, deserialise in TS, re-serialise, and assert the canonical forms are byte-identical. This catches the entire class of "C# treats missing as null, TS treats it as undefined" bugs that would otherwise surface as mysterious cache misses in S08.

**Acceptance criteria:**

- `pnpm generate` produces no git diff when run on a clean tree
- Round-trip contract test passes across all fixtures

**Tests:** `TC-S01-CFG-011`, `TC-S01-CFG-012`

---

### T-01.4 — Validation engine with semantic rules (8 h)

**Objective:** JSON Schema catches shape errors. Semantic rules catch the errors that actually get apps rejected.

**Architecture:**

```
validate(config) → ValidationResult {
    errors:   Diagnostic[]   // block save and build
    warnings: Diagnostic[]   // allow, but surface prominently
    info:     Diagnostic[]   // hints
}

Diagnostic {
    code:     string   // "CFG_BUNDLE_ID_INVALID" — stable, documented, searchable
    severity: error | warning | info
    path:     string   // JSON Pointer: "/navigation/tabBar/items/2/url"
    message:  string   // user-facing, actionable, names the fix
    docsUrl:  string
}
```

**Semantic rules to implement (each is a separate, unit-tested rule class):**

| Code                          | Rule                                                                                                                    | Severity                                                 |
| ----------------------------- | ----------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------- |
| `CFG_ORIGIN_NOT_COVERED`      | Every `navigation` and `linkRules` internal URL falls under `allowedOrigins`                                            | error                                                    |
| `CFG_INITIAL_URL_NOT_ALLOWED` | `initialUrl` host is in `allowedOrigins`                                                                                | error                                                    |
| `CFG_LINK_RULE_UNREACHABLE`   | A rule is shadowed by an earlier broader pattern                                                                        | warning                                                  |
| `CFG_LINK_RULE_NO_CATCHALL`   | No terminal `.*` rule — undefined behaviour for unmatched links                                                         | warning                                                  |
| `CFG_REGEX_INVALID`           | Pattern fails to compile                                                                                                | error                                                    |
| `CFG_REGEX_CATASTROPHIC`      | ⚠️ Pattern is vulnerable to catastrophic backtracking (nested quantifiers) — this runs on every navigation in the shell | error                                                    |
| `CFG_TAB_COUNT_HIGH`          | > 5 tabs                                                                                                                | warning                                                  |
| `CFG_NO_NATIVE_FEATURES`      | ⚠️ No tabs, no drawer, no plugins, no push — near-certain Guideline 4.2 rejection                                       | warning (becomes the seed of the Readiness Score in S16) |
| `CFG_PLUGIN_UNKNOWN`          | Plugin id not in registry                                                                                               | error                                                    |
| `CFG_PLUGIN_CONFIG_INVALID`   | Plugin config fails that plugin's own schema                                                                            | error                                                    |
| `CFG_PLUGIN_CONFLICT`         | Two plugins declare mutual conflict                                                                                     | error                                                    |
| `CFG_PLUGIN_MIN_SDK`          | Plugin requires a higher minSdk/iOS version than configured                                                             | error                                                    |
| `CFG_PERMISSION_UNJUSTIFIED`  | ⚠️ Permission requested with no plugin or feature using it — a common rejection cause                                   | warning                                                  |
| `CFG_ASSET_MISSING`           | Referenced asset not found in storage                                                                                   | error                                                    |
| `CFG_ICON_DIMENSIONS`         | Source icon < 1024×1024 or not square                                                                                   | error                                                    |
| `CFG_ICON_ALPHA`              | ⚠️ iOS icons must not contain an alpha channel                                                                          | error                                                    |
| `CFG_NAME_TOO_LONG`           | App name > 30 chars                                                                                                     | error                                                    |
| `CFG_CLEARTEXT_URL`           | Any `http://` URL                                                                                                       | error                                                    |

**Implementation notes:**

- Rules are individual classes implementing `IValidationRule`, registered in DI. One rule, one test class, one purpose.
- ⚠️ Run rules **in parallel** but collect deterministically ordered results (sort by path then code) — non-deterministic error ordering breaks snapshot tests and confuses users.
- Compile and cache user regexes with a **matching timeout** (`RegexOptions.NonBacktracking` where possible). The same protection must exist in the shells.
- Validation must be pure and allocation-light: it runs on every keystroke (debounced) in the studio.

**Acceptance criteria:**

- Every rule has a passing and a failing unit test
- Error output is deterministic across runs
- `maximal` fixture validates in < 50 ms (benchmark asserted in CI)
- The TypeScript validator produces the same diagnostic codes and paths as C# for all fixtures

**Tests:** `TC-S01-CFG-013` … `TC-S01-CFG-034`, `TC-S01-PRF-001`

---

### T-01.5 — Canonical JSON + hash split (5 h)

**Objective:** Deterministic bytes, so caching works. This is the foundation of the unit economics in the master spec §16.

**Canonicalisation rules:**

1. Object keys sorted by UTF-16 code unit
2. No insignificant whitespace
3. Numbers in shortest round-trip form (`1.0` → `1`)
4. Strings NFC-normalised, minimal escaping
5. Explicit `null` omitted (equivalent to absent)
6. Arrays preserve order (order is semantic for tabs and link rules)
7. Defaults **resolved** before hashing — an omitted field and an explicitly-default field must hash identically

**Hash split** (from `01_ENGINEERING_STANDARDS.md` §2.1):

```csharp
public sealed record ConfigHashes(string CodeKey, string AssetKey, string ContentKey);

// codeKey    : plugins[], build.toolchain, app.bundleId, permissions,
//              nativeSurfaces[], deepLinks, shellVersion
// assetKey   : branding.*, navigation labels/icons, localised strings
// contentKey : app.initialUrl, allowedOrigins, linkRules, webOverrides,
//              offline, ota
```

Implement by projecting the resolved config into three sub-documents, canonicalising each, and hashing with **BLAKE3** (fast, and fine for a cache key — this is not a security boundary).

**Property tests (FsCheck) — these earn their keep:**

- `canonical(shuffleKeys(x)) == canonical(x)`
- `canonical(x) == canonical(parse(canonical(x)))`
- `hash(x) == hash(y)` ⟺ `resolvedEquals(x, y)`
- Changing any `branding` field changes `assetKey` and leaves `codeKey` unchanged
- Changing any plugin changes `codeKey`

**Acceptance criteria:** All property tests pass over 1,000 generated cases; hashing the `maximal` fixture takes < 5 ms.

**Tests:** `TC-S01-CFG-035` … `TC-S01-CFG-042`, `TC-S01-PRF-002`

---

### T-01.6 — Migration framework (5 h)

**Objective:** Be able to change the schema without breaking stored configs — you will do this a dozen times.

**Design:**

```csharp
public interface IConfigMigration {
    int FromVersion { get; }
    int ToVersion   { get; }
    JsonNode Up(JsonNode config);      // required
    JsonNode Down(JsonNode config);    // optional; null if lossy
}
```

- A `ConfigMigrator` walks from the stored `schemaVersion` to current, applying migrations in order.
- Migrations are **pure functions on `JsonNode`**, never on typed models — typed models represent only the _current_ version, so using them in a migration guarantees breakage later.
- Every migration ships with fixtures: an input at version N and the expected output at N+1, committed as golden files.
- Write a **no-op v0→v1 migration now** purely to prove the framework, including its tests. Building this before you need it takes 5 hours; retrofitting it after 200 customers have stored configs takes a fortnight.

**Acceptance criteria:**

- v0 fixture migrates to v1 and matches the golden output byte-for-byte
- Round-trip test: `Down(Up(x)) == x` for non-lossy migrations
- Migrating an already-current config is a no-op (identity), verified by hash equality
- Unknown future version fails with `CFG_SCHEMA_VERSION_UNSUPPORTED`, never silently

**Tests:** `TC-S01-CFG-043` … `TC-S01-CFG-048`

---

### T-01.7 — Fixture corpus (3 h)

Create in `tests/fixtures/configs/`:

| Fixture                    | Purpose                                                                                                  |
| -------------------------- | -------------------------------------------------------------------------------------------------------- |
| `minimal.json`             | Smallest valid config (~10 lines). Must produce a working app.                                           |
| `maximal.json`             | Every field populated. The performance and codegen benchmark target.                                     |
| `all-plugins.json`         | Every registered plugin enabled                                                                          |
| `unicode.json`             | ⚠️ Arabic (RTL) app name, emoji in tab labels, CJK, combining characters, a 30-char name at the boundary |
| `edge-no-tabs.json`        | Drawer-only navigation                                                                                   |
| `edge-many-tabs.json`      | 8 tabs (triggers the warning)                                                                            |
| `edge-long-bundleid.json`  | 155-char bundle id at the limit                                                                          |
| `edge-many-linkrules.json` | 200 link rules (performance)                                                                             |
| `edge-single-page.json`    | ⚠️ No native features — the config the Readiness Score must reject in S16                                |
| `edge-deep-nesting.json`   | Maximum nesting depth                                                                                    |
| `invalid-*.json`           | ~10 configs, each violating exactly one rule, named for the expected diagnostic code                     |

**Acceptance criteria:** each `invalid-*.json` produces exactly the diagnostic code its filename declares, and no others.

**Tests:** `TC-S01-CFG-049`

---

## 5. Test cases (summary)

| ID range               | Type          | Coverage                                                                                            |
| ---------------------- | ------------- | --------------------------------------------------------------------------------------------------- |
| `TC-S01-CFG-001`–`010` | Unit          | Schema shape validation: valid/invalid bundle ids, colours, URLs, versions, name length, tab counts |
| `TC-S01-CFG-011`–`012` | Contract      | C#/TS type round-trip equivalence across all fixtures                                               |
| `TC-S01-CFG-013`–`034` | Unit          | One pass + one fail case per semantic rule (22 tests)                                               |
| `TC-S01-CFG-035`–`042` | Property      | Canonicalisation and hash-split invariants                                                          |
| `TC-S01-CFG-043`–`048` | Unit + golden | Migration up/down/identity/unsupported-version                                                      |
| `TC-S01-CFG-049`       | Data-driven   | Every `invalid-*.json` yields exactly its declared code                                             |
| `TC-S01-PRF-001`       | Benchmark     | Validate `maximal.json` < 50 ms                                                                     |
| `TC-S01-PRF-002`       | Benchmark     | Hash `maximal.json` < 5 ms                                                                          |

**Detailed example:**

| Field            | Value                                                                                                                                                                                                      |
| ---------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **ID**           | `TC-S01-CFG-024`                                                                                                                                                                                           |
| **Title**        | Catastrophic-backtracking regex in a link rule is rejected                                                                                                                                                 |
| **Type**         | Unit                                                                                                                                                                                                       |
| **Precondition** | Validator registered with `LinkRuleRegexRule`                                                                                                                                                              |
| **Steps**        | Validate a config whose link rule pattern is `^(a+)+$`                                                                                                                                                     |
| **Expected**     | One `error` diagnostic, code `CFG_REGEX_CATASTROPHIC`, path `/linkRules/0/pattern`, message naming the offending construct; validation itself completes in < 50 ms (i.e. the checker does not itself hang) |

---

## 6. Risks

| Risk                                                                  | Likelihood | Mitigation                                                                                                                  |
| --------------------------------------------------------------------- | ---------- | --------------------------------------------------------------------------------------------------------------------------- |
| Schema over-designed for features 12 months away                      | **High**   | Only model what S02–S05 will consume. `x-` escape hatch covers the rest. Adding fields later is easy; removing them is not. |
| C#/TS validators drift                                                | Medium     | The shared fixture corpus + contract test is the whole defence. Never let a rule exist in only one language.                |
| Canonicalisation subtly non-deterministic (float formatting, unicode) | Medium     | Property tests over 1,000 generated cases; explicit NFC normalisation; shortest round-trip number formatting                |
| Sprint becomes a design rabbit hole                                   | **High**   | Timebox T-01.1 to 6 hours. Ship v1 imperfect; the migration framework exists precisely so imperfection is survivable.       |

---

## 7. Deliverables

- `packages/config-schema` published internally, schema hosted at a stable public URL
- Validation engine (C# + TS) with 22 semantic rules and a documented diagnostic code table
- Canonicaliser + three-way hash split with property tests
- Migration framework with a proven v0→v1 path
- Fixture corpus of ~20 configs
- `docs/adr/0003-appconfig-schema-v1.md`
- `docs/reference/diagnostics.md` — the public error-code table (becomes user-facing docs)
- `SPRINT-01_REVIEW.md`
