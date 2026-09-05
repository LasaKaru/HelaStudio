/**
 * The validation entry point.
 *
 * Validation runs three times, cheapest machine first: in the browser on every
 * keystroke, at the API on save, and on the runner before a build. A config
 * error must never reach a macOS runner — that single rule is worth more than
 * every other optimisation in the system (01_ENGINEERING_STANDARDS.md §1.5).
 */
import ajv2020Module, { type ErrorObject, type ValidateFunction } from 'ajv/dist/2020.js';
import ajvFormatsModule from 'ajv-formats';

// Ajv ships CommonJS with `export =`, which NodeNext resolution surfaces as a
// namespace rather than the constructor. Unwrap once, here, rather than at
// every call site.
const Ajv2020 = ajv2020Module as unknown as typeof ajv2020Module.default;
const addFormats = ajvFormatsModule as unknown as typeof ajvFormatsModule.default;
import schemaJson from '../schema/appconfig.v1.json' with { type: 'json' };
import type { JsonObject, JsonValue } from './canonical.js';
import { resolveDefaults } from './defaults.js';
import {
  DiagnosticCode,
  diagnostic,
  pointerSegment,
  toResult,
  type Diagnostic,
  type ValidationResult,
} from './diagnostics.js';
import { builtInPluginRegistry, type PluginRegistry } from './plugin-registry.js';
import { defaultRules } from './rules/index.js';
import type { AssetResolver, RuleContext, ValidationRule } from './rules/rule.js';

/** The `appconfig.json` v1 JSON Schema. */
export const appConfigSchema = schemaJson as unknown as JsonObject;

/** The schema version this build of the package writes. */
export const CURRENT_SCHEMA_VERSION = 1;

/** Options for a validation run. */
export interface ValidateOptions {
  /** Plugin registry to resolve plugin ids against. Defaults to the built-in set. */
  readonly plugins?: PluginRegistry;
  /** Asset store, when available. Asset rules skip without one. */
  readonly assets?: AssetResolver;
  /** Rule set to run. Defaults to every rule. */
  readonly rules?: readonly ValidationRule[];
}

/** A validated configuration, with defaults resolved. */
export interface ValidatedConfig {
  /** Diagnostics grouped by severity. */
  readonly result: ValidationResult;
  /** The document with every schema default filled in, ready to hash and generate from. */
  readonly resolved: JsonObject;
}

// Compiling the schema is the expensive part, so it is done once per process.
let compiled: ValidateFunction | undefined;

function schemaValidator(): ValidateFunction {
  let validator = compiled;
  if (validator === undefined) {
    const ajv = new Ajv2020({ allErrors: true, strict: false, allowUnionTypes: true });
    addFormats(ajv);
    validator = ajv.compile(appConfigSchema);
    compiled = validator;
  }
  return validator;
}

/**
 * Validates a configuration document and resolves its defaults.
 *
 * Schema violations short-circuit the semantic rules: running rules against a
 * document of the wrong shape produces a cascade of confusing secondary errors.
 */
export function validate(config: JsonValue, options: ValidateOptions = {}): ValidatedConfig {
  const versionError = checkSchemaVersion(config);
  if (versionError !== undefined) {
    return { result: toResult([versionError]), resolved: asObject(config) };
  }

  const validateSchema = schemaValidator();
  if (!validateSchema(config)) {
    const diagnostics = (validateSchema.errors ?? []).map(toDiagnostic);
    return { result: toResult(diagnostics), resolved: asObject(config) };
  }

  const resolved = asObject(resolveDefaults(config, appConfigSchema));
  const context: RuleContext = {
    config: resolved,
    plugins: options.plugins ?? builtInPluginRegistry,
    assets: options.assets,
  };

  const rules = options.rules ?? defaultRules;
  const diagnostics = rules.flatMap((rule) => rule.run(context));
  return { result: toResult(diagnostics), resolved };
}

/** Rejects a document written against a schema version this build cannot read. */
function checkSchemaVersion(config: JsonValue): Diagnostic | undefined {
  const version = asObject(config)['schemaVersion'];
  if (typeof version !== 'number' || version <= CURRENT_SCHEMA_VERSION) return undefined;

  return diagnostic(
    DiagnosticCode.SchemaVersionUnsupported,
    'error',
    '/schemaVersion',
    `This configuration was written for schema version ${String(version)}, but this build understands ` +
      `up to version ${String(CURRENT_SCHEMA_VERSION)}. Update to a newer release before opening it.`,
  );
}

/** Turns one Ajv error into a diagnostic, preferring a specific code where we have one. */
function toDiagnostic(error: ErrorObject): Diagnostic {
  const path = pathOf(error);
  const code = specificCode(error, path);
  return diagnostic(code, 'error', path, describe(error, path));
}

/**
 * The JSON Pointer a diagnostic should carry.
 *
 * Ajv reports an unrecognised field against its *parent* object, which leaves the
 * studio unable to highlight the field the user actually mistyped. Extending the
 * path to name it is both better for the user and what the C# side reports.
 */
function pathOf(error: ErrorObject): string {
  if (error.keyword !== 'unevaluatedProperties' && error.keyword !== 'additionalProperties') {
    return error.instancePath;
  }
  const params = error.params as { additionalProperty?: string; unevaluatedProperty?: string };
  const property = params.additionalProperty ?? params.unevaluatedProperty;
  return property === undefined
    ? error.instancePath
    : `${error.instancePath}/${pointerSegment(property)}`;
}

function specificCode(error: ErrorObject, path: string): Diagnostic['code'] {
  if (path === '/app/bundleId') return DiagnosticCode.BundleIdInvalid;
  if (path === '/app/name' && error.keyword === 'maxLength') return DiagnosticCode.NameTooLong;
  if (error.keyword === 'unevaluatedProperties' || error.keyword === 'additionalProperties') {
    return DiagnosticCode.UnknownField;
  }
  return DiagnosticCode.SchemaViolation;
}

/**
 * Turns an Ajv error into text a non-engineer can act on.
 *
 * Ajv's own messages describe the schema ("must match pattern ^[a-z]..."), which
 * tells the user nothing about what to type instead. These strings are
 * user-facing copy and must match the C# `SchemaMessages` word for word — the
 * cross-language contract test asserts it.
 */
// eslint-disable-next-line complexity -- a dispatch table from schema keyword to user-facing copy; splitting it would scatter one message catalogue.
function describe(error: ErrorObject, path: string): string {
  if (path === '/app/bundleId') {
    return (
      'The bundle identifier must be lowercase reverse-DNS with at least one dot, such as ' +
      'com.acme.app. Uppercase letters, spaces, and leading digits are rejected by both stores.'
    );
  }
  if (path === '/app/name' && error.keyword === 'maxLength') {
    return (
      'The app name is longer than 30 characters, which is the App Store limit. Shorten it, or ' +
      'use a shorter name on the icon and the full name in your store listing.'
    );
  }
  if (error.keyword === 'unevaluatedProperties' || error.keyword === 'additionalProperties') {
    // Ajv names the offending key differently per keyword: `additionalProperty`
    // for `additionalProperties`, `unevaluatedProperty` for `unevaluatedProperties`.
    // The offending key is the last segment of the path (see `pathOf`), which is
    // the one source both implementations can agree on.
    const extra = path.slice(path.lastIndexOf('/') + 1);
    return (
      `"${extra === '' ? 'A field' : extra}" is not a recognised setting here. ` +
      'Check the spelling, or move it under an "x-" prefixed object if it is your own data.'
    );
  }
  if (error.keyword === 'required') {
    const missing = (error.params as { missingProperty?: string }).missingProperty;
    return `"${missing ?? 'A required field'}" is required and is missing.`;
  }
  if (error.keyword === 'pattern' || error.keyword === 'format') {
    return 'This value is not in the expected format.';
  }
  return `This value ${constraint(error)}.`;
}

/**
 * Describes a failed constraint in our own words.
 *
 * Deliberately does not pass Ajv's message through: Ajv and JsonSchema.Net word
 * the same failure differently, so either engine's phrasing would break the
 * cross-language contract — and neither is written in a voice a non-engineer
 * should have to read.
 */
// eslint-disable-next-line complexity -- see `describe`: a message catalogue.
export function constraint(error: ErrorObject): string {
  if (error.keyword === 'type') {
    const expected = (error.params as { type?: string | string[] }).type;
    const single = Array.isArray(expected) ? expected[0] : expected;
    return `must be ${typeName(single ?? '')}`;
  }
  switch (error.keyword) {
    case 'enum':
    case 'const':
      return 'must be one of the allowed values';
    case 'minLength':
      return 'is too short';
    case 'maxLength':
      return 'is too long';
    case 'minimum':
    case 'exclusiveMinimum':
      return 'is below the smallest allowed number';
    case 'maximum':
    case 'exclusiveMaximum':
      return 'is above the largest allowed number';
    case 'minItems':
      return 'does not have enough entries';
    case 'maxItems':
      return 'has too many entries';
    case 'uniqueItems':
      return 'contains the same entry twice';
    case 'multipleOf':
      return 'is not one of the allowed steps';
    default:
      return 'is not valid here';
  }
}

/** Names a JSON type the way a non-engineer would. */
export function typeName(jsonType: string): string {
  switch (jsonType) {
    case 'boolean':
      return 'either on or off';
    case 'string':
      return 'text';
    case 'number':
      return 'a number';
    case 'integer':
      return 'a whole number';
    case 'array':
      return 'a list';
    case 'object':
      return 'a group of settings';
    case 'null':
      return 'empty';
    default:
      return 'a valid value';
  }
}

function asObject(value: JsonValue | undefined): JsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value : {};
}
