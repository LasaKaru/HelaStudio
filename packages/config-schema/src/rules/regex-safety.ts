/**
 * Detects regular expressions that are invalid or vulnerable to catastrophic
 * backtracking.
 *
 * This matters more here than in most codebases: user patterns are evaluated by
 * the shell on *every navigation*, on a phone. A pattern like `^(a+)+$` does not
 * merely slow a server down, it freezes the customer's app.
 */

/** Why a pattern was rejected. */
export type RegexVerdict =
  | { readonly kind: 'ok' }
  | { readonly kind: 'invalid'; readonly reason: string }
  | { readonly kind: 'catastrophic'; readonly construct: string };

/** Checks a user-supplied pattern for compilability and backtracking safety. */
export function checkRegex(pattern: string): RegexVerdict {
  try {
    new RegExp(pattern, 'u');
  } catch {
    // Not every valid pattern is Unicode-mode valid, so fall back before failing.
    try {
      new RegExp(pattern);
    } catch (error) {
      return { kind: 'invalid', reason: (error as Error).message };
    }
  }
  const construct = findNestedQuantifier(pattern);
  return construct === undefined ? { kind: 'ok' } : { kind: 'catastrophic', construct };
}

const QUANTIFIERS = new Set(['*', '+', '?', '{']);

/**
 * Finds a quantified group whose body is itself quantified or alternated —
 * the `(a+)+` and `(a|a)*` shapes that cause exponential backtracking.
 *
 * Returns the offending substring, or undefined when the pattern looks safe.
 * This is a deliberately conservative structural check rather than a full parse:
 * it catches the shapes seen in practice without needing a regex engine model.
 */
// eslint-disable-next-line complexity -- one scan over the pattern; splitting the guards into helpers would hide the order they must be applied in.
function findNestedQuantifier(pattern: string): string | undefined {
  for (let i = 0; i < pattern.length; i++) {
    if (pattern[i] !== '(' || isEscaped(pattern, i)) continue;

    const close = matchingParen(pattern, i);
    if (close === undefined) continue;

    const after = pattern[close + 1];
    if (after === undefined || !QUANTIFIERS.has(after)) continue;
    // `(...)?` cannot blow up: the group is tried at most once.
    if (after === '?') continue;
    // A possessive or lazy quantifier bounds the search.
    if (pattern[close + 2] === '?' || pattern[close + 2] === '+') continue;

    const body = pattern.slice(i + 1, close);
    if (bodyIsAmbiguous(body)) {
      return pattern.slice(i, close + 2);
    }
  }
  return undefined;
}

/**
 * True when a group body can match the same text more than one way.
 *
 * Two shapes cause exponential backtracking when wrapped in an outer repetition:
 *
 *   1. The body *begins* with a quantified atom, as in `(a+)+`. The inner and
 *      outer repetitions then compete for the same characters.
 *   2. The body is a top-level alternation whose branches overlap, as in
 *      `(a|a)*` or `(a|ab)*`.
 *
 * Note what is deliberately *not* flagged: `(-[a-z]+)*`, the ordinary
 * separated-list idiom. Its body must consume a literal `-` before anything
 * else, so repetitions cannot overlap and matching stays linear. Rejecting it
 * would be a false positive on one of the most common patterns users write.
 */
function bodyIsAmbiguous(body: string): boolean {
  const inner = stripGroupPrefix(body);
  return startsWithQuantifiedAtom(inner) || hasOverlappingAlternation(inner);
}

/** Removes a non-capturing, lookaround, or named-group prefix such as `?:`. */
function stripGroupPrefix(body: string): string {
  return body.replace(/^\?(?::|=|!|<[=!]|<[A-Za-z_$][\w$]*>)/, '');
}

/** True when the first atom of `body` carries an unbounded quantifier. */
function startsWithQuantifiedAtom(body: string): boolean {
  const end = firstAtomEnd(body);
  if (end === undefined) return false;
  const quantifier = body[end];
  if (quantifier === '*' || quantifier === '+') return true;
  // `{n,}` is unbounded; `{n}` and `{n,m}` are not, so they cannot explode.
  if (quantifier !== '{') return false;
  const close = body.indexOf('}', end);
  return close !== -1 && body.slice(end + 1, close).endsWith(',');
}

/** Index just past the first atom in `body`, or undefined if there is none. */
// eslint-disable-next-line complexity -- a flat dispatch over the atom kinds a regex can open with; a table, not branching logic.
function firstAtomEnd(body: string): number | undefined {
  const first = body[0];
  if (first === undefined) return undefined;
  if (first === '\\') return body.length > 1 ? 2 : undefined;
  if (first === '[') {
    const close = classEnd(body);
    return close === undefined ? undefined : close + 1;
  }
  if (first === '(') {
    const close = matchingParen(body, 0);
    return close === undefined ? undefined : close + 1;
  }
  // A quantifier cannot open an atom, and an anchor consumes nothing.
  if ('*+?{|)'.includes(first)) return undefined;
  if (first === '^' || first === '$') return undefined;
  return 1;
}

/** End index of a character class starting at position 0, or undefined. */
function classEnd(body: string): number | undefined {
  for (let i = 1; i < body.length; i++) {
    if (isEscaped(body, i)) continue;
    if (body[i] === ']') return i;
  }
  return undefined;
}

/**
 * True when two top-level branches can match the same text.
 *
 * Only identical branches, or one branch that is a prefix of another, are
 * genuinely ambiguous. `(-a|-b)` merely shares a first character and stays
 * linear, so it is left alone.
 */
function hasOverlappingAlternation(body: string): boolean {
  const branches = splitTopLevel(body);
  if (branches.length < 2) return false;

  for (let i = 0; i < branches.length; i++) {
    for (let j = i + 1; j < branches.length; j++) {
      const a = branches[i] as string;
      const b = branches[j] as string;
      if (a === b || a.startsWith(b) || b.startsWith(a)) return true;
    }
  }
  return false;
}

/** Splits a body on top-level `|`, ignoring pipes inside groups and classes. */
function splitTopLevel(body: string): string[] {
  const branches: string[] = [];
  let depth = 0;
  let inClass = false;
  let start = 0;

  for (let i = 0; i < body.length; i++) {
    if (isEscaped(body, i)) continue;
    const ch = body[i];
    if (inClass) {
      if (ch === ']') inClass = false;
      continue;
    }
    if (ch === '[') inClass = true;
    else if (ch === '(') depth++;
    else if (ch === ')') depth--;
    else if (ch === '|' && depth === 0) {
      branches.push(body.slice(start, i));
      start = i + 1;
    }
  }
  branches.push(body.slice(start));
  return branches;
}

function matchingParen(pattern: string, open: number): number | undefined {
  let depth = 0;
  let inClass = false;
  for (let i = open; i < pattern.length; i++) {
    if (isEscaped(pattern, i)) continue;
    const ch = pattern[i];
    if (inClass) {
      if (ch === ']') inClass = false;
      continue;
    }
    if (ch === '[') inClass = true;
    else if (ch === '(') depth++;
    else if (ch === ')' && --depth === 0) return i;
  }
  return undefined;
}

function isEscaped(s: string, index: number): boolean {
  let backslashes = 0;
  for (let i = index - 1; i >= 0 && s[i] === '\\'; i--) backslashes++;
  return backslashes % 2 === 1;
}
