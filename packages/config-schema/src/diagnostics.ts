/**
 * Typed, coded diagnostics. Every error crossing a boundary carries a stable code
 * so it can be searched, documented, and localised.
 * See 01_ENGINEERING_STANDARDS.md §4 and docs/reference/diagnostics.md.
 */

/** How much a diagnostic matters. Errors block a save and a build; warnings do not. */
export type Severity = 'error' | 'warning' | 'info';

/**
 * A stable, documented diagnostic code.
 *
 * Codes are permanent. If a rule is removed its code is retired, never reused —
 * customers and support articles reference these strings.
 */
export const DiagnosticCode = {
  // ── Schema shape ───────────────────────────────────────────────────────
  SchemaViolation: 'CFG_SCHEMA_VIOLATION',
  SchemaVersionUnsupported: 'CFG_SCHEMA_VERSION_UNSUPPORTED',
  UnknownField: 'CFG_UNKNOWN_FIELD',

  // ── Identity and naming ────────────────────────────────────────────────
  BundleIdInvalid: 'CFG_BUNDLE_ID_INVALID',
  NameTooLong: 'CFG_NAME_TOO_LONG',

  // ── Origins and URLs ───────────────────────────────────────────────────
  InitialUrlNotAllowed: 'CFG_INITIAL_URL_NOT_ALLOWED',
  OriginNotCovered: 'CFG_ORIGIN_NOT_COVERED',
  CleartextUrl: 'CFG_CLEARTEXT_URL',

  // ── Link rules ─────────────────────────────────────────────────────────
  RegexInvalid: 'CFG_REGEX_INVALID',
  RegexCatastrophic: 'CFG_REGEX_CATASTROPHIC',
  LinkRuleUnreachable: 'CFG_LINK_RULE_UNREACHABLE',
  LinkRuleNoCatchall: 'CFG_LINK_RULE_NO_CATCHALL',

  // ── Navigation ─────────────────────────────────────────────────────────
  TabCountHigh: 'CFG_TAB_COUNT_HIGH',
  DuplicateItemId: 'CFG_DUPLICATE_ITEM_ID',

  // ── Store readiness ────────────────────────────────────────────────────
  NoNativeFeatures: 'CFG_NO_NATIVE_FEATURES',
  PermissionUnjustified: 'CFG_PERMISSION_UNJUSTIFIED',

  // ── Plugins ────────────────────────────────────────────────────────────
  PluginUnknown: 'CFG_PLUGIN_UNKNOWN',
  PluginConfigInvalid: 'CFG_PLUGIN_CONFIG_INVALID',
  PluginConflict: 'CFG_PLUGIN_CONFLICT',
  PluginMinSdk: 'CFG_PLUGIN_MIN_SDK',
  PluginPermissionMissing: 'CFG_PLUGIN_PERMISSION_MISSING',

  // ── Assets ─────────────────────────────────────────────────────────────
  AssetMissing: 'CFG_ASSET_MISSING',
  IconDimensions: 'CFG_ICON_DIMENSIONS',
  IconAlpha: 'CFG_ICON_ALPHA',

  // ── Secrets ────────────────────────────────────────────────────────────
  SecretInConfig: 'CFG_SECRET_IN_CONFIG',

  // ── Text ───────────────────────────────────────────────────────────────
  ControlCharacter: 'CFG_CONTROL_CHARACTER',
} as const;

/** One of the stable diagnostic codes. */
export type DiagnosticCodeValue = (typeof DiagnosticCode)[keyof typeof DiagnosticCode];

/** A single finding about a configuration document. */
export interface Diagnostic {
  /** Stable, documented, searchable code. */
  readonly code: DiagnosticCodeValue;
  /** Whether this blocks a save and build. */
  readonly severity: Severity;
  /** RFC 6901 JSON Pointer to the offending value, for example `/linkRules/2/pattern`. */
  readonly path: string;
  /** User-facing text. Must name the fix, not merely the problem. */
  readonly message: string;
  /** Where to read more. */
  readonly docsUrl: string;
}

/** The outcome of validating a configuration document. */
export interface ValidationResult {
  /** True when there are no errors. Warnings do not block. */
  readonly valid: boolean;
  /** Findings that block a save and a build. */
  readonly errors: readonly Diagnostic[];
  /** Findings that are allowed through but surfaced prominently. */
  readonly warnings: readonly Diagnostic[];
  /** Hints and suggestions. */
  readonly info: readonly Diagnostic[];
}

const DOCS_BASE = 'https://docs.shellwright.dev/reference/diagnostics';

/** Builds a diagnostic, deriving its documentation URL from the code. */
export function diagnostic(
  code: DiagnosticCodeValue,
  severity: Severity,
  path: string,
  message: string,
): Diagnostic {
  return { code, severity, path, message, docsUrl: `${DOCS_BASE}#${code.toLowerCase()}` };
}

/**
 * Groups diagnostics into a result, sorting each bucket by path then code.
 *
 * Rules run in parallel, so ordering must be imposed here: non-deterministic
 * error order breaks snapshot tests and makes the studio's error list jump
 * around while the user types.
 */
export function toResult(diagnostics: readonly Diagnostic[]): ValidationResult {
  const sorted = [...diagnostics].sort(
    (a, b) => a.path.localeCompare(b.path) || a.code.localeCompare(b.code),
  );
  const errors = sorted.filter((d) => d.severity === 'error');
  return {
    valid: errors.length === 0,
    errors,
    warnings: sorted.filter((d) => d.severity === 'warning'),
    info: sorted.filter((d) => d.severity === 'info'),
  };
}

/** Escapes a JSON Pointer path segment per RFC 6901. */
export function pointerSegment(segment: string | number): string {
  return typeof segment === 'number'
    ? String(segment)
    : segment.replace(/~/g, '~0').replace(/\//g, '~1');
}

/** Joins path segments into an RFC 6901 JSON Pointer. */
export function pointer(...segments: (string | number)[]): string {
  return segments.length === 0 ? '' : `/${segments.map(pointerSegment).join('/')}`;
}
