/**
 * Rules about link routing.
 *
 * Link rules are evaluated first-match-wins on every navigation, so both their
 * order and their cost matter.
 */
import { DiagnosticCode, diagnostic, pointer, type Diagnostic } from '../diagnostics.js';
import type { JsonObject, JsonValue } from '../canonical.js';
import { checkRegex } from './regex-safety.js';
import type { RuleContext, ValidationRule } from './rule.js';

function linkRules(config: JsonObject): JsonObject[] {
  const raw = config['linkRules'];
  if (!Array.isArray(raw)) return [];
  return raw.map((r) => (typeof r === 'object' && r !== null && !Array.isArray(r) ? r : {}));
}

function patternOf(rule: JsonObject): string | undefined {
  const pattern = rule['pattern'];
  return typeof pattern === 'string' ? pattern : undefined;
}

/** Every user pattern must compile, and must not be able to hang the shell. */
export const regexSafetyRule: ValidationRule = {
  name: 'regex-safety',
  run({ config }: RuleContext): readonly Diagnostic[] {
    const found: Diagnostic[] = [];
    for (const { path, pattern } of allPatterns(config)) {
      const verdict = checkRegex(pattern);
      if (verdict.kind === 'invalid') {
        found.push(
          diagnostic(
            DiagnosticCode.RegexInvalid,
            'error',
            path,
            'This pattern is not a valid regular expression, so no link could ever match it. ' +
              'Check for an unclosed bracket or parenthesis, and remember to escape dots in domain ' +
              'names - for example ^https://app\\.acme\\.com.',
          ),
        );
      } else if (verdict.kind === 'catastrophic') {
        found.push(
          diagnostic(
            DiagnosticCode.RegexCatastrophic,
            'error',
            path,
            `The construct ${verdict.construct} nests one repetition inside another, which can take ` +
              'exponential time to match and would freeze the app on every navigation. ' +
              'Rewrite it with a single repetition, for example (a+) instead of (a+)+.',
          ),
        );
      }
    }
    return found;
  },
};

/** Every pattern in the document, wherever it appears. */
function* allPatterns(
  config: JsonObject,
): Generator<{ readonly path: string; readonly pattern: string }> {
  for (const [index, rule] of linkRules(config).entries()) {
    const pattern = patternOf(rule);
    if (pattern !== undefined) yield { path: pointer('linkRules', index, 'pattern'), pattern };
  }

  const navigation = config['navigation'];
  const tabBar = asObject(asObject(navigation)['tabBar']);
  const items = Array.isArray(tabBar['items']) ? tabBar['items'] : [];
  for (const [index, item] of items.entries()) {
    const activePattern = asObject(item)['activePattern'];
    if (typeof activePattern === 'string') {
      yield {
        path: pointer('navigation', 'tabBar', 'items', index, 'activePattern'),
        pattern: activePattern,
      };
    }
  }
}

/** A rule shadowed by an earlier, broader rule can never fire. */
export const unreachableRuleRule: ValidationRule = {
  name: 'link-rule-unreachable',
  run({ config }: RuleContext): readonly Diagnostic[] {
    const rules = linkRules(config);
    const found: Diagnostic[] = [];

    for (let i = 1; i < rules.length; i++) {
      const pattern = patternOf(rules[i] as JsonObject);
      if (pattern === undefined) continue;

      const shadower = findShadower(rules, i);
      if (shadower === undefined) continue;

      found.push(
        diagnostic(
          DiagnosticCode.LinkRuleUnreachable,
          'warning',
          pointer('linkRules', i, 'pattern'),
          `Rule ${String(i + 1)} can never match, because rule ${String(shadower + 1)} above it already ` +
            'matches everything it would. Move this rule above that one, or remove it.',
        ),
      );
    }
    return found;
  },
};

/** Returns the index of an earlier rule that already matches everything rule `i` would. */
function findShadower(rules: readonly JsonObject[], i: number): number | undefined {
  const pattern = patternOf(rules[i] as JsonObject);
  if (pattern === undefined) return undefined;

  for (let j = 0; j < i; j++) {
    const earlier = patternOf(rules[j] as JsonObject);
    if (earlier === undefined) continue;
    if (isCatchAll(earlier)) return j;
    // A literal prefix that is a prefix of this one means this one is subsumed.
    if (earlier !== pattern && pattern.startsWith(earlier) && isLiteralPrefix(earlier)) return j;
  }
  return undefined;
}

const CATCH_ALL = new Set(['.*', '^.*$', '.*$', '^.*', '(.*)', '.+', '^.+$']);

function isCatchAll(pattern: string): boolean {
  return CATCH_ALL.has(pattern.trim());
}

/** True when a pattern has no metacharacters beyond a leading anchor and escaped dots. */
function isLiteralPrefix(pattern: string): boolean {
  const body = pattern.startsWith('^') ? pattern.slice(1) : pattern;
  return !/(?<!\\)[.*+?[\]{}()|$]/.test(body);
}

/** Without a terminal catch-all, unmatched links have undefined behaviour. */
export const catchAllRule: ValidationRule = {
  name: 'link-rule-catchall',
  run({ config }: RuleContext): readonly Diagnostic[] {
    const rules = linkRules(config);
    if (rules.length === 0) return [];

    const last = patternOf(rules[rules.length - 1] as JsonObject);
    if (last !== undefined && isCatchAll(last)) return [];

    return [
      diagnostic(
        DiagnosticCode.LinkRuleNoCatchall,
        'warning',
        pointer('linkRules'),
        'No rule matches every remaining link, so it is not defined where an unrecognised link opens. ' +
          'Add a final rule with the pattern ".*" and the action you want as the fallback, ' +
          'usually externalBrowser.',
      ),
    ];
  },
};

function asObject(value: JsonValue | undefined): JsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value : {};
}
