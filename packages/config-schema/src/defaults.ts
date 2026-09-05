/**
 * Default resolution.
 *
 * Defaults live in the schema, not in code (ADR-0003), so there is one source of
 * truth that the studio renders, code generation reads, and hashing sees.
 *
 * Resolution happens *before* canonicalisation: an omitted field and an
 * explicitly-default field must hash identically, or a user toggling a value
 * back to its default would miss the build cache.
 */
import type { JsonObject, JsonValue } from './canonical.js';

/** The subset of JSON Schema this resolver walks. */
interface SchemaNode {
  readonly $ref?: string;
  readonly type?: string | string[];
  readonly default?: JsonValue;
  readonly properties?: Readonly<Record<string, SchemaNode>>;
  readonly items?: SchemaNode;
  readonly $defs?: Readonly<Record<string, SchemaNode>>;
  readonly oneOf?: readonly SchemaNode[];
  readonly anyOf?: readonly SchemaNode[];
}

/** Resolves a local `#/$defs/Name` reference against the root schema. */
function deref(node: SchemaNode, root: SchemaNode): SchemaNode {
  if (node.$ref === undefined) return node;
  const name = node.$ref.replace('#/$defs/', '');
  const target = root.$defs?.[name];
  if (target === undefined) {
    throw new Error(`Unresolvable schema reference: ${node.$ref}`);
  }
  // A $ref sibling may carry its own default, which wins over the target's.
  return node.default === undefined ? target : { ...target, default: node.default };
}

/**
 * Returns a copy of `value` with every schema default filled in.
 *
 * Objects are only materialised when they contain at least one default or an
 * already-present value — an absent optional object with no defaults stays
 * absent rather than becoming `{}`.
 */
export function resolveDefaults(value: JsonValue, schema: JsonObject): JsonValue {
  const root = schema as unknown as SchemaNode;
  return resolveNode(value, root, root) ?? value;
}

function resolveNode(
  value: JsonValue | undefined,
  rawNode: SchemaNode,
  root: SchemaNode,
): JsonValue | undefined {
  const node = deref(rawNode, root);

  if (node.properties !== undefined) {
    // A value of the wrong JSON type is left exactly as authored: fabricating
    // defaults over it would hide the error that validation is about to report.
    if (value !== undefined && !isPlainObject(value)) return value;
    return resolveObject(value, node, root);
  }
  if (node.items !== undefined && Array.isArray(value)) {
    return value.map((item) => resolveNode(item, node.items as SchemaNode, root) ?? item);
  }
  // A union (oneOf/anyOf) is left as authored: picking a branch to fill defaults
  // into would guess at the user's intent. LocalizedString and IconRef rely on this.
  return value ?? node.default;
}

function resolveObject(
  value: JsonValue | undefined,
  node: SchemaNode,
  root: SchemaNode,
): JsonValue | undefined {
  const properties = node.properties ?? {};
  const source: JsonObject = isPlainObject(value) ? value : {};
  const present = isPlainObject(value);

  const out: JsonObject = {};
  for (const [key, childSchema] of Object.entries(properties)) {
    const resolved = resolveNode(source[key], childSchema, root);
    if (resolved !== undefined) out[key] = resolved;
  }
  // Preserve anything the schema does not model (x- extensions, plugin config
  // bodies, future fields written by a newer studio).
  for (const [key, raw] of Object.entries(source)) {
    if (!(key in properties)) out[key] = raw;
  }

  if (!present && Object.keys(out).length === 0) return undefined;
  return out;
}

function isPlainObject(value: JsonValue | undefined): value is JsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
