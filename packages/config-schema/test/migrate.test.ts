import { describe, expect, it } from 'vitest';
import { canonicalize } from '../src/canonical.js';
import { hashValue } from '../src/hash.js';
import {
  MigrationError,
  migrateToCurrent,
  migrationV0ToV1,
  type ConfigMigration,
} from '../src/migrate.js';
import { validate } from '../src/validate.js';
import type { JsonObject } from '../src/canonical.js';
import { readConfig, readMigrationFixture } from './fixtures.js';

describe('migrateToCurrent', () => {
  // TC-S01-CFG-050: the v0 fixture matches its golden output byte for byte.
  it('migrates the v0 fixture to the committed v1 golden file', () => {
    const migrated = migrateToCurrent(readMigrationFixture('v0-input.json'));
    const golden = readMigrationFixture('v1-golden.json');
    expect(canonicalize(migrated)).toBe(canonicalize(golden));
  });

  // TC-S01-CFG-051
  it('produces a document that then validates cleanly', () => {
    const migrated = migrateToCurrent(readMigrationFixture('v0-input.json'));
    const { result } = validate(migrated);
    expect(result.errors).toEqual([]);
  });

  // TC-S01-CFG-052: migrating an already-current config is the identity.
  it('leaves a current document unchanged, verified by hash equality', () => {
    const config = readConfig('maximal.json');
    const once = migrateToCurrent(config);
    expect(hashValue(once)).toBe(hashValue(config));
    expect(hashValue(migrateToCurrent(once))).toBe(hashValue(config));
  });

  // TC-S01-CFG-053
  it('refuses a version from the future rather than guessing', () => {
    expect(() => migrateToCurrent({ schemaVersion: 99 })).toThrow(MigrationError);
    try {
      migrateToCurrent({ schemaVersion: 99 });
    } catch (error) {
      expect((error as MigrationError).code).toBe('CFG_SCHEMA_VERSION_UNSUPPORTED');
    }
  });

  // TC-S01-CFG-054
  it('refuses a document with no readable schemaVersion', () => {
    expect(() => migrateToCurrent({ app: {} })).toThrow(MigrationError);
    expect(() => migrateToCurrent({ schemaVersion: 'one' as unknown as number })).toThrow(
      MigrationError,
    );
    expect(() => migrateToCurrent({ schemaVersion: -1 })).toThrow(MigrationError);
  });

  // TC-S01-CFG-055
  it('fails loudly when a step in the chain is missing', () => {
    const orphan: ConfigMigration = { fromVersion: 5, toVersion: 6, up: (c) => c };
    expect(() => migrateToCurrent({ schemaVersion: 0 }, [orphan])).toThrow(
      /No migration exists from schema version 0/,
    );
  });

  it('does not mutate its input', () => {
    const input = readMigrationFixture('v0-input.json');
    const before = canonicalize(input);
    migrateToCurrent(input);
    expect(canonicalize(input)).toBe(before);
  });
});

describe('migrationV0ToV1 round trip', () => {
  // TC-S01-CFG-056: Down(Up(x)) == x for a non-lossy migration.
  it('returns the original document when reversed', () => {
    const original = readMigrationFixture('v0-input.json');
    const roundTripped = migrationV0ToV1.down?.(migrationV0ToV1.up(original));
    expect(canonicalize(roundTripped as JsonObject)).toBe(canonicalize(original));
  });

  it('leaves link rules that already carry an id alone', () => {
    const input: JsonObject = {
      schemaVersion: 0,
      linkRules: [{ id: 'kept', pattern: '.*', action: 'internal' }],
    };
    const up = migrationV0ToV1.up(input);
    expect((up['linkRules'] as JsonObject[])[0]?.['id']).toBe('kept');
  });

  it('handles a v0 document with no startUrl and no link rules', () => {
    const up = migrationV0ToV1.up({ schemaVersion: 0 });
    expect(up).toEqual({ schemaVersion: 1 });
    expect(migrationV0ToV1.down?.(up)).toEqual({ schemaVersion: 0 });
  });
});
