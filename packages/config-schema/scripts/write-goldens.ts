/**
 * Writes the cross-language contract goldens.
 *
 * These files are the single agreed truth about what validation and hashing
 * produce for the fixture corpus. Both the TypeScript and the C# test suites
 * assert against them, so either implementation drifting fails CI — which is the
 * entire defence against the two validators diverging (SPRINT-01 T-01.3).
 *
 * Regenerate deliberately, and read the diff: a change here changes what every
 * customer sees.
 */
import { readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { canonicalize, computeHashes, validate, type JsonObject } from '../src/index.js';

const here = fileURLToPath(new URL('.', import.meta.url));
const fixtures = resolve(here, '../../../tests/fixtures');
const configDir = join(fixtures, 'configs');
const expectedDir = join(fixtures, 'expected');

const HASH_CONTEXT = {
  shellVersion: '1.0.0',
  toolchain: { agp: '8.9', xcode: '26.1' },
  pluginLock: { 'qr-scanner': '1.2.0' },
} as const;

const names = readdirSync(configDir)
  .filter((f) => f.endsWith('.json'))
  .sort();

const diagnostics: Record<string, unknown> = {};
const hashes: Record<string, unknown> = {};
const canonical: Record<string, string> = {};

for (const name of names) {
  const config = JSON.parse(readFileSync(join(configDir, name), 'utf8')) as JsonObject;
  const { result, resolved } = validate(config);

  diagnostics[name] = {
    valid: result.valid,
    errors: result.errors,
    warnings: result.warnings,
    info: result.info,
  };
  canonical[name] = canonicalize(resolved);
  // Hashing a document that failed validation is meaningless — it is never built.
  if (result.valid) hashes[name] = computeHashes(resolved, HASH_CONTEXT);
}

write('diagnostics.json', diagnostics);
write('hashes.json', hashes);
write('canonical.json', canonical);

function write(file: string, value: unknown): void {
  writeFileSync(join(expectedDir, file), `${JSON.stringify(value, null, 2)}\n`, 'utf8');
  console.log(`wrote tests/fixtures/expected/${file}`);
}
