/** Shared access to the fixture corpus in `tests/fixtures`. */
import { readFileSync, readdirSync } from 'node:fs';
import { basename, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { JsonObject } from '../src/index.js';

const here = fileURLToPath(new URL('.', import.meta.url));

/** Root of the shared fixture corpus. */
export const fixtureRoot = resolve(here, '../../../tests/fixtures');
/** Directory holding `appconfig` fixtures. */
export const configDir = join(fixtureRoot, 'configs');
/** Directory holding migration input and golden files. */
export const migrationDir = join(fixtureRoot, 'migrations');

/** Reads one fixture config by file name. */
export function readConfig(name: string): JsonObject {
  return JSON.parse(readFileSync(join(configDir, name), 'utf8')) as JsonObject;
}

/** Reads one migration fixture by file name. */
export function readMigrationFixture(name: string): JsonObject {
  return JSON.parse(readFileSync(join(migrationDir, name), 'utf8')) as JsonObject;
}

/** Every fixture file name matching a prefix. */
export function listConfigs(prefix = ''): string[] {
  return readdirSync(configDir)
    .filter((f) => f.endsWith('.json') && f.startsWith(prefix))
    .sort();
}

/** Fixtures expected to validate with no errors. */
export const validFixtures: readonly string[] = [
  'minimal.json',
  'maximal.json',
  'all-plugins.json',
  'unicode.json',
  'edge-hostile-text.json',
  'edge-portrait-locked.json',
  'edge-no-tabs.json',
  'edge-long-bundleid.json',
  'edge-many-linkrules.json',
  'edge-deep-nesting.json',
];

/** The diagnostic code an `invalid-*.json` fixture declares in its file name. */
export function declaredCode(fileName: string): string {
  return basename(fileName, '.json').replace(/^invalid-/, '');
}
