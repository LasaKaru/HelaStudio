/**
 * `@shellwright/config-schema` — the shape of every generated app.
 *
 * This package is the one-way door named in SPRINT-01: every native project,
 * every cache key, every studio form, and every stored customer configuration
 * depends on what is exported here.
 */

export {
  canonicalize,
  canonicalBytes,
  canonicalNumber,
  canonicalString,
  type JsonObject,
  type JsonValue,
} from './canonical.js';

export { resolveDefaults } from './defaults.js';

export {
  DiagnosticCode,
  diagnostic,
  pointer,
  pointerSegment,
  toResult,
  type Diagnostic,
  type DiagnosticCodeValue,
  type Severity,
  type ValidationResult,
} from './diagnostics.js';

export { computeHashes, hashValue, type ConfigHashes, type HashContext } from './hash.js';

export {
  builtInPluginRegistry,
  builtInPlugins,
  type PluginDescriptor,
  type PluginRegistry,
} from './plugin-registry.js';

export {
  checkRegex,
  defaultRules,
  type AssetMetadata,
  type AssetResolver,
  type RegexVerdict,
  type RuleContext,
  type ValidationRule,
} from './rules/index.js';

export {
  appConfigSchema,
  validate,
  CURRENT_SCHEMA_VERSION,
  type ValidateOptions,
  type ValidatedConfig,
} from './validate.js';

export {
  migrateToCurrent,
  migrations,
  migrationV0ToV1,
  MigrationError,
  type ConfigMigration,
} from './migrate.js';

export type { ShellwrightAppConfiguration as AppConfig } from './generated/appconfig.v1.js';
