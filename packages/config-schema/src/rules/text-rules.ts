/**
 * Rejects characters that survive validation and then break something downstream.
 *
 * WARNING: this rule exists because of a specific, reproducible failure, not out
 * of caution. PostgreSQL's `jsonb` type cannot represent U+0000 in a string —
 * casting a document containing one raises "unsupported Unicode escape
 * sequence" — so a configuration carrying it passes every check the studio
 * makes, passes the schema, and then fails the save with a 500 that names
 * nothing the author can act on. The other C0 controls store fine and go on to
 * appear verbatim in an Android string resource, an Info.plist, and a store
 * listing.
 *
 * Tab, newline, and carriage return are allowed: multi-line text is legitimate
 * in a description, and the generators escape them correctly.
 */
import { DiagnosticCode, diagnostic, pointer, type Diagnostic } from '../diagnostics.js';
import type { JsonObject, JsonValue } from '../canonical.js';
import type { RuleContext, ValidationRule } from './rule.js';

/** Control characters that are safe to keep. */
const ALLOWED = new Set(['\t', '\n', '\r']);

/** No unprintable control character may appear in any string. */
export const noControlCharactersRule: ValidationRule = {
  name: 'no-control-characters',
  run({ config }: RuleContext): readonly Diagnostic[] {
    const found: Diagnostic[] = [];

    walk(config, [], (path, value) => {
      for (const character of value) {
        const code = character.codePointAt(0) ?? 0;
        const isControl = code < 0x20 || (code >= 0x7f && code <= 0x9f);

        if (isControl && !ALLOWED.has(character)) {
          found.push(
            diagnostic(
              DiagnosticCode.ControlCharacter,
              'error',
              path,
              `This value contains U+${code.toString(16).toUpperCase().padStart(4, '0')}, an ` +
                'unprintable control character. It is almost always an accident of copying and ' +
                'pasting, it cannot be stored, and it would appear verbatim in your store ' +
                'listing. Retype the value rather than pasting it.',
            ),
          );
          return;
        }
      }
    });

    return found;
  },
};

function walk(
  node: JsonValue,
  path: (string | number)[],
  visit: (path: string, value: string) => void,
): void {
  if (typeof node === 'string') {
    visit(pointer(...path), node);
    return;
  }
  if (Array.isArray(node)) {
    node.forEach((item, index) => {
      walk(item, [...path, index], visit);
    });
    return;
  }
  if (typeof node !== 'object' || node === null) return;
  for (const [key, value] of Object.entries(node as JsonObject)) {
    walk(value, [...path, key], visit);
  }
}
