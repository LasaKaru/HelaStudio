/**
 * The configuration migration framework.
 *
 * Migrations operate on raw JSON, never on typed models: the typed model always
 * represents the *current* version, so a migration written against it silently
 * breaks the moment the schema moves again.
 *
 * The v0-to-v1 migration below is deliberately near-trivial. It exists now, with
 * its tests, because building this before it is needed costs five hours and
 * retrofitting it after customers have stored configs costs a fortnight.
 */
import type { JsonObject, JsonValue } from './canonical.js';
import { CURRENT_SCHEMA_VERSION } from './validate.js';

/** One step from a schema version to the next. */
export interface ConfigMigration {
  /** The version this migration reads. */
  readonly fromVersion: number;
  /** The version this migration writes. */
  readonly toVersion: number;
  /** Migrates a document forward. Must be pure. */
  up(config: JsonObject): JsonObject;
  /**
   * Migrates a document back, when the change is reversible.
   *
   * Undefined for a lossy migration — a downgrade that would silently drop data
   * must fail loudly instead.
   */
  down?(config: JsonObject): JsonObject;
}

/** Raised when a document cannot be migrated to the current version. */
export class MigrationError extends Error {
  /** Stable diagnostic code, matching the validation code table. */
  public readonly code: string;

  /** Creates a migration error with a stable code. */
  public constructor(code: string, message: string) {
    super(message);
    this.name = 'MigrationError';
    this.code = code;
  }
}

/**
 * v0 to v1.
 *
 * v0 was the pre-release shape used during Sprint 00 spikes. Two things changed:
 * `startUrl` became `app.initialUrl`, and link rules gained stable ids so the
 * studio can track them across a reorder.
 */
export const migrationV0ToV1: ConfigMigration = {
  fromVersion: 0,
  toVersion: 1,

  up(config: JsonObject): JsonObject {
    const next = structuredClone(config);
    next['schemaVersion'] = 1;

    const startUrl = next['startUrl'];
    if (typeof startUrl === 'string') {
      const app = isObject(next['app']) ? next['app'] : {};
      app['initialUrl'] = startUrl;
      next['app'] = app;
      delete next['startUrl'];
    }

    const rules = next['linkRules'];
    if (Array.isArray(rules)) {
      next['linkRules'] = rules.map((rule, index) => {
        if (!isObject(rule) || typeof rule['id'] === 'string') return rule;
        return { ...rule, id: `rule-${String(index + 1)}` };
      });
    }

    return next;
  },

  down(config: JsonObject): JsonObject {
    const next = structuredClone(config);
    next['schemaVersion'] = 0;

    const app = next['app'];
    if (isObject(app) && typeof app['initialUrl'] === 'string') {
      next['startUrl'] = app['initialUrl'];
      delete app['initialUrl'];
      if (Object.keys(app).length === 0) delete next['app'];
    }

    const rules = next['linkRules'];
    if (Array.isArray(rules)) {
      next['linkRules'] = rules.map((rule) => {
        if (!isObject(rule)) return rule;
        const { id: _id, ...rest } = rule;
        return rest as JsonValue;
      });
    }

    return next;
  },
};

/** Every migration this build knows, in ascending order. */
export const migrations: readonly ConfigMigration[] = [migrationV0ToV1];

/**
 * Migrates a document up to the current schema version.
 *
 * A document already at the current version is returned unchanged, so migrating
 * twice is safe and hash-stable.
 *
 * @throws {MigrationError} if the version is missing, from the future, or if no
 *   migration path exists.
 */
export function migrateToCurrent(
  config: JsonObject,
  available: readonly ConfigMigration[] = migrations,
): JsonObject {
  const version = config['schemaVersion'];
  if (typeof version !== 'number' || !Number.isInteger(version) || version < 0) {
    throw new MigrationError(
      'CFG_SCHEMA_VERSION_UNSUPPORTED',
      'This configuration has no readable schemaVersion, so there is no way to tell which format it ' +
        'is in. Add "schemaVersion": 1 if it was written against the current format.',
    );
  }

  if (version > CURRENT_SCHEMA_VERSION) {
    throw new MigrationError(
      'CFG_SCHEMA_VERSION_UNSUPPORTED',
      `This configuration is at schema version ${String(version)}, which is newer than the version ` +
        `${String(CURRENT_SCHEMA_VERSION)} this build understands. Update before opening it.`,
    );
  }

  let current = config;
  let at = version;
  while (at < CURRENT_SCHEMA_VERSION) {
    const step = available.find((m) => m.fromVersion === at);
    if (step === undefined) {
      throw new MigrationError(
        'CFG_SCHEMA_VERSION_UNSUPPORTED',
        `No migration exists from schema version ${String(at)} to ${String(at + 1)}.`,
      );
    }
    current = step.up(current);
    at = step.toVersion;
  }
  return current;
}

function isObject(value: JsonValue | undefined): value is JsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
