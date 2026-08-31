/**
 * Detects credentials pasted into the configuration.
 *
 * Config is stored, hashed, logged, exported, and embedded in the shipped app
 * binary, where anyone can read it. A secret here is a secret published, so this
 * is an error rather than a warning (ADR-0003).
 */
import { DiagnosticCode, diagnostic, pointer, type Diagnostic } from '../diagnostics.js';
import type { JsonObject, JsonValue } from '../canonical.js';
import type { RuleContext, ValidationRule } from './rule.js';

interface SecretPattern {
  readonly name: string;
  readonly test: RegExp;
}

const SECRET_PATTERNS: readonly SecretPattern[] = [
  { name: 'an AWS access key id', test: /\b(?:AKIA|ASIA)[0-9A-Z]{16}\b/ },
  { name: 'a GitHub token', test: /\bgh[pousr]_[A-Za-z0-9]{36,}\b/ },
  { name: 'a Slack token', test: /\bxox[baprs]-[A-Za-z0-9-]{10,}\b/ },
  { name: 'a Stripe secret key', test: /\bsk_(?:live|test)_[A-Za-z0-9]{16,}\b/ },
  { name: 'a Google API key', test: /\bAIza[0-9A-Za-z_-]{35}\b/ },
  { name: 'a private key block', test: /-----BEGIN (?:[A-Z ]+ )?PRIVATE KEY-----/ },
  {
    name: 'a JSON web token',
    test: /\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b/,
  },
];

/** Keys whose name alone suggests the value is a credential. */
const SUSPICIOUS_KEYS = /^(?:authorization|x-api-key|api[-_]?key|secret|password|token|bearer)$/i;

/** No credential may appear anywhere in the document. */
export const noSecretsRule: ValidationRule = {
  name: 'no-secrets',
  run({ config }: RuleContext): readonly Diagnostic[] {
    const found: Diagnostic[] = [];

    walk(config, [], (path, key, value) => {
      const matched = SECRET_PATTERNS.find((p) => p.test.test(value));
      if (matched !== undefined) {
        found.push(secretDiagnostic(path, `This looks like ${matched.name}.`));
        return;
      }
      // A header literally named Authorization is a credential regardless of shape.
      if (SUSPICIOUS_KEYS.test(key) && value.length >= 8) {
        found.push(secretDiagnostic(path, `A value under "${key}" is almost always a credential.`));
      }
    });

    return found;
  },
};

function secretDiagnostic(path: string, why: string): Diagnostic {
  return diagnostic(
    DiagnosticCode.SecretInConfig,
    'error',
    path,
    `${why} App configuration is stored, exported, and embedded in the app itself, where anyone who ` +
      'downloads it can read this value. Store the credential in your workspace credentials instead ' +
      'and reference it by id, or have your website supply it after the user signs in.',
  );
}

function walk(
  node: JsonValue,
  path: (string | number)[],
  visit: (path: string, key: string, value: string) => void,
  key = '',
): void {
  if (typeof node === 'string') {
    visit(pointer(...path), key, node);
    return;
  }
  if (Array.isArray(node)) {
    node.forEach((item, index) => {
      walk(item, [...path, index], visit, key);
    });
    return;
  }
  if (typeof node !== 'object' || node === null) return;
  for (const [childKey, value] of Object.entries(node as JsonObject)) {
    walk(value, [...path, childKey], visit, childKey);
  }
}
