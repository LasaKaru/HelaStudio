/**
 * Canonical JSON — deterministic bytes for a configuration document.
 *
 * Every cache key in the platform is a hash of this output, so the rules below
 * are load-bearing: two documents that mean the same thing must produce
 * identical bytes, or the build cache never hits and the unit economics in the
 * master spec §16 do not hold.
 *
 * Rules (01_ENGINEERING_STANDARDS.md §2.2, SPRINT-01 T-01.5):
 *   1. Object keys sorted by UTF-16 code unit
 *   2. No insignificant whitespace
 *   3. Numbers in shortest round-trip form (1.0 becomes 1)
 *   4. Strings NFC-normalised, minimal escaping
 *   5. Explicit null omitted — equivalent to absent
 *   6. Array order preserved — order is semantic for tabs and link rules
 *   7. Defaults resolved before hashing (see defaults.ts)
 */

/** Any value expressible in JSON. */
export type JsonValue =
  | null
  | boolean
  | number
  | string
  | JsonValue[]
  | { [key: string]: JsonValue };

/** A JSON object. */
export type JsonObject = Record<string, JsonValue>;

/**
 * Serialises a value to canonical JSON.
 *
 * @throws {RangeError} if a number is not finite — NaN and Infinity have no JSON form.
 */
export function canonicalize(value: JsonValue): string {
  const out: string[] = [];
  write(value, out);
  return out.join('');
}

function write(value: JsonValue, out: string[]): void {
  if (value === null) {
    out.push('null');
    return;
  }
  switch (typeof value) {
    case 'boolean':
      out.push(value ? 'true' : 'false');
      return;
    case 'number':
      out.push(canonicalNumber(value));
      return;
    case 'string':
      out.push(canonicalString(value));
      return;
    default:
      break;
  }
  if (Array.isArray(value)) {
    writeArray(value, out);
    return;
  }
  writeObject(value, out);
}

function writeArray(value: JsonValue[], out: string[]): void {
  out.push('[');
  for (let i = 0; i < value.length; i++) {
    if (i > 0) out.push(',');
    // Rule 6: order is semantic, so an explicit null inside an array is preserved
    // rather than dropped — removing it would shift every later index.
    write(value[i] ?? null, out);
  }
  out.push(']');
}

function writeObject(value: JsonObject, out: string[]): void {
  // Rule 5: drop null-valued keys before sorting, so `{"a":null}` and `{}` agree.
  const keys = Object.keys(value)
    .filter((k) => value[k] !== undefined && value[k] !== null)
    .sort(compareByCodeUnit);
  out.push('{');
  for (let i = 0; i < keys.length; i++) {
    if (i > 0) out.push(',');
    const key = keys[i] as string;
    out.push(canonicalString(key), ':');
    write(value[key] as JsonValue, out);
  }
  out.push('}');
}

/**
 * Compares two strings by UTF-16 code unit.
 *
 * Deliberately not `localeCompare`: that is locale-dependent and would make the
 * cache key differ between machines.
 */
function compareByCodeUnit(a: string, b: string): number {
  return a < b ? -1 : a > b ? 1 : 0;
}

/**
 * Formats a number in shortest round-trip form.
 *
 * JavaScript's own `String(n)` is already the shortest representation that
 * round-trips, with two exceptions handled here: negative zero, and exponent
 * notation, which C# and JavaScript format differently.
 */
export function canonicalNumber(n: number): string {
  if (!Number.isFinite(n)) {
    throw new RangeError(`Cannot canonicalise non-finite number: ${String(n)}`);
  }
  if (Object.is(n, -0)) return '0';
  const s = String(n);
  // Normalise exponent form: JS writes 1e+21, we write 1E21 to match the C# side.
  if (s.includes('e')) {
    const [mantissa = '', exponent = ''] = s.split('e');
    const sign = exponent.startsWith('-') ? '-' : '';
    return `${mantissa}E${sign}${exponent.replace(/^[+-]/, '')}`;
  }
  return s;
}

const ESCAPES: Readonly<Record<string, string>> = {
  '"': '\\"',
  '\\': '\\\\',
  '\b': '\\b',
  '\f': '\\f',
  '\n': '\\n',
  '\r': '\\r',
  '\t': '\\t',
};

/** Serialises a string: NFC-normalised, minimally escaped, double-quoted. */
export function canonicalString(s: string): string {
  const normalized = s.normalize('NFC');
  let out = '"';
  for (const ch of normalized) {
    const escape = ESCAPES[ch];
    if (escape !== undefined) {
      out += escape;
    } else if (ch < ' ') {
      out += `\\u${ch.charCodeAt(0).toString(16).padStart(4, '0')}`;
    } else {
      out += ch;
    }
  }
  return `${out}"`;
}

/** Canonical JSON as UTF-8 bytes, ready for hashing. */
export function canonicalBytes(value: JsonValue): Uint8Array {
  return new TextEncoder().encode(canonicalize(value));
}
