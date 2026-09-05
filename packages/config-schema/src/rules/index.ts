/** Every semantic rule, in registration order. */
import type { ValidationRule } from './rule.js';
import { assetExistsRule, iconAlphaRule, iconDimensionsRule } from './asset-rules.js';
import { catchAllRule, regexSafetyRule, unreachableRuleRule } from './link-rules.js';
import {
  pluginConfigRule,
  pluginConflictRule,
  pluginKnownRule,
  pluginPermissionRule,
  pluginPlatformFloorRule,
} from './plugin-rules.js';
import { noSecretsRule } from './secret-rules.js';
import { noControlCharactersRule } from './text-rules.js';
import {
  duplicateIdRule,
  nativeFeaturesRule,
  permissionJustifiedRule,
  tabCountRule,
} from './store-readiness.js';
import { cleartextUrlRule, initialUrlAllowedRule, originCoverageRule } from './url-rules.js';

export type { AssetMetadata, AssetResolver, RuleContext, ValidationRule } from './rule.js';
export { checkRegex, type RegexVerdict } from './regex-safety.js';

/**
 * The default rule set.
 *
 * Order here does not affect output — results are sorted by path and code in
 * `toResult` so that diagnostics are deterministic across runs.
 */
export const defaultRules: readonly ValidationRule[] = [
  initialUrlAllowedRule,
  originCoverageRule,
  cleartextUrlRule,
  regexSafetyRule,
  unreachableRuleRule,
  catchAllRule,
  tabCountRule,
  nativeFeaturesRule,
  permissionJustifiedRule,
  duplicateIdRule,
  pluginKnownRule,
  pluginConfigRule,
  pluginConflictRule,
  pluginPlatformFloorRule,
  pluginPermissionRule,
  assetExistsRule,
  iconDimensionsRule,
  iconAlphaRule,
  noSecretsRule,
  noControlCharactersRule,
];
