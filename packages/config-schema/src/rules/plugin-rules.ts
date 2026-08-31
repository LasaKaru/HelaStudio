/**
 * Rules about plugins.
 *
 * Plugin combinatorics are the failure mode that kills platforms like this one
 * around plugin fifteen. Every conflict caught here is a build that never runs
 * and a support ticket that never opens.
 */
import ajvModule from 'ajv';

// See validate.ts: Ajv is CommonJS, so the namespace must be unwrapped.
const Ajv = ajvModule as unknown as typeof ajvModule.default;
import { DiagnosticCode, diagnostic, pointer, type Diagnostic } from '../diagnostics.js';
import type { JsonObject, JsonValue } from '../canonical.js';
import type { PluginDescriptor } from '../plugin-registry.js';
import type { RuleContext, ValidationRule } from './rule.js';
import { constraint } from '../validate.js';

const ajv = new Ajv({ allErrors: true, strict: false });

interface EnabledPlugin {
  readonly id: string;
  readonly config: JsonObject;
  readonly descriptor: PluginDescriptor | undefined;
}

function enabledPlugins(context: RuleContext): readonly EnabledPlugin[] {
  const plugins = asObject(context.config['plugins']);
  return Object.entries(plugins).map(([id, config]) => ({
    id,
    config: asObject(config),
    descriptor: context.plugins.get(id),
  }));
}

/** A plugin id that is not in the registry cannot be built. */
export const pluginKnownRule: ValidationRule = {
  name: 'plugin-known',
  run(context: RuleContext): readonly Diagnostic[] {
    return enabledPlugins(context)
      .filter((p) => p.descriptor === undefined)
      .map((p) =>
        diagnostic(
          DiagnosticCode.PluginUnknown,
          'error',
          pointer('plugins', p.id),
          `There is no plugin called "${p.id}". Check the spelling against the plugin library, ` +
            'or remove this entry.',
        ),
      );
  },
};

/** Each plugin's settings must satisfy that plugin's own schema. */
export const pluginConfigRule: ValidationRule = {
  name: 'plugin-config',
  run(context: RuleContext): readonly Diagnostic[] {
    const found: Diagnostic[] = [];
    for (const plugin of enabledPlugins(context)) {
      if (plugin.descriptor === undefined) continue;

      const validate = ajv.compile(plugin.descriptor.configSchema);
      if (validate(plugin.config)) continue;

      for (const error of validate.errors ?? []) {
        found.push(
          diagnostic(
            DiagnosticCode.PluginConfigInvalid,
            'error',
            pointer('plugins', plugin.id) + error.instancePath,
            `${plugin.descriptor.name}: this value ${constraint(error)}.`,
          ),
        );
      }
    }
    return found;
  },
};

/** Two plugins that declare a mutual conflict cannot ship in one app. */
export const pluginConflictRule: ValidationRule = {
  name: 'plugin-conflict',
  run(context: RuleContext): readonly Diagnostic[] {
    const plugins = enabledPlugins(context);
    const ids = new Set(plugins.map((p) => p.id));
    const found: Diagnostic[] = [];

    for (const plugin of plugins) {
      const descriptor = plugin.descriptor;
      if (descriptor === undefined) continue;

      for (const otherId of descriptor.conflictsWith) {
        // Report the pair once, on the alphabetically first id.
        if (!ids.has(otherId) || plugin.id > otherId) continue;

        const reason = descriptor.conflictReasons[otherId] ?? 'They cannot be used together.';
        found.push(
          diagnostic(
            DiagnosticCode.PluginConflict,
            'error',
            pointer('plugins', plugin.id),
            `"${plugin.id}" and "${otherId}" conflict. ${reason} Remove one of them.`,
          ),
        );
      }
    }
    return found;
  },
};

/** A plugin cannot require a newer platform than the app targets. */
export const pluginPlatformFloorRule: ValidationRule = {
  name: 'plugin-platform-floor',
  run(context: RuleContext): readonly Diagnostic[] {
    const build = asObject(context.config['build']);
    const minSdk = asNumber(asObject(build['android'])['minSdk']) ?? 24;
    const minIos = asString(asObject(build['ios'])['minVersion']) ?? '15.0';
    const found: Diagnostic[] = [];

    for (const plugin of enabledPlugins(context)) {
      const descriptor = plugin.descriptor;
      if (descriptor === undefined) continue;

      if (descriptor.minSdkAndroid > minSdk) {
        found.push(
          diagnostic(
            DiagnosticCode.PluginMinSdk,
            'error',
            pointer('plugins', plugin.id),
            `${descriptor.name} needs Android API ${String(descriptor.minSdkAndroid)} or newer, but this app ` +
              `targets API ${String(minSdk)}. Raise build.android.minSdk to ${String(descriptor.minSdkAndroid)}, ` +
              'or remove the plugin.',
          ),
        );
      }

      if (compareVersions(descriptor.minVersionIos, minIos) > 0) {
        found.push(
          diagnostic(
            DiagnosticCode.PluginMinSdk,
            'error',
            pointer('plugins', plugin.id),
            `${descriptor.name} needs iOS ${descriptor.minVersionIos} or newer, but this app targets ` +
              `iOS ${minIos}. Raise build.ios.minVersion to ${descriptor.minVersionIos}, or remove the plugin.`,
          ),
        );
      }
    }
    return found;
  },
};

/** A plugin whose permission is switched off will fail at runtime. */
export const pluginPermissionRule: ValidationRule = {
  name: 'plugin-permission',
  run(context: RuleContext): readonly Diagnostic[] {
    const permissions = asObject(context.config['permissions']);
    const found: Diagnostic[] = [];

    for (const plugin of enabledPlugins(context)) {
      const descriptor = plugin.descriptor;
      if (descriptor === undefined) continue;

      for (const permission of descriptor.requiredPermissions) {
        const value = permissions[permission];
        if (value === true || (typeof value === 'string' && value !== 'none')) continue;

        found.push(
          diagnostic(
            DiagnosticCode.PluginPermissionMissing,
            'error',
            pointer('permissions', permission),
            `${descriptor.name} cannot work without the ${permission} permission, which is currently off. ` +
              `Turn on permissions.${permission}, or remove the plugin.`,
          ),
        );
      }
    }
    return found;
  },
};

/** Compares dotted version strings numerically. Returns -1, 0, or 1. */
function compareVersions(a: string, b: string): number {
  const left = a.split('.').map(Number);
  const right = b.split('.').map(Number);
  for (let i = 0; i < Math.max(left.length, right.length); i++) {
    const diff = (left[i] ?? 0) - (right[i] ?? 0);
    if (diff !== 0) return Math.sign(diff);
  }
  return 0;
}

function asObject(value: JsonValue | undefined): JsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value : {};
}

function asNumber(value: JsonValue | undefined): number | undefined {
  return typeof value === 'number' ? value : undefined;
}

function asString(value: JsonValue | undefined): string | undefined {
  return typeof value === 'string' ? value : undefined;
}
