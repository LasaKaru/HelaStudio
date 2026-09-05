/**
 * Generates TypeScript types from the JSON Schema.
 *
 * Output is committed so consumers never need the generator, and CI asserts that
 * regenerating produces no diff — that check is what stops the schema and the
 * types drifting apart (SPRINT-01 T-01.3).
 */
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { compile } from 'json-schema-to-typescript';

const here = dirname(fileURLToPath(import.meta.url));
const schemaPath = resolve(here, '../schema/appconfig.v1.json');
const outPath = resolve(here, '../src/generated/appconfig.v1.ts');

const schema = JSON.parse(await readFile(schemaPath, 'utf8')) as Record<string, unknown>;

const banner = `/* eslint-disable */
/**
 * GENERATED FILE — do not edit.
 * Source: schema/appconfig.v1.json
 * Regenerate with: pnpm --filter @shellwright/config-schema generate
 */
`;

const types = await compile(schema, 'AppConfig', {
  bannerComment: '',
  additionalProperties: false,
  style: { singleQuote: true, printWidth: 100 },
  enableConstEnums: false,
});

await mkdir(dirname(outPath), { recursive: true });
await writeFile(outPath, banner + types, 'utf8');
console.log(`wrote ${outPath}`);
