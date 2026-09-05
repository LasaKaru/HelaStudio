/**
 * The semantic-rule contract.
 *
 * JSON Schema catches shape errors. These rules catch the errors that actually
 * get apps rejected by a store reviewer — an unjustified permission, a webview
 * with no native features, a regex that hangs the shell on every navigation.
 *
 * One rule, one file, one purpose, one test class.
 */
import type { JsonObject } from '../canonical.js';
import type { Diagnostic } from '../diagnostics.js';
import type { PluginRegistry } from '../plugin-registry.js';

/** Metadata about an uploaded asset, as far as validation cares. */
export interface AssetMetadata {
  /** Pixel width of the source image. */
  readonly width: number;
  /** Pixel height of the source image. */
  readonly height: number;
  /** Whether the image carries an alpha channel. iOS icons must not. */
  readonly hasAlpha: boolean;
}

/**
 * Looks up uploaded assets.
 *
 * Absent in the browser, where assets have not been uploaded yet — asset rules
 * skip rather than guess, and run again server-side where the store exists.
 */
export interface AssetResolver {
  /** Returns metadata for an `asset://sha256-…` reference, or undefined if unknown. */
  lookup(ref: string): AssetMetadata | undefined;
}

/** Everything a rule may consult beyond the document itself. */
export interface RuleContext {
  /** The configuration with schema defaults already resolved. */
  readonly config: JsonObject;
  /** Plugins available to this workspace. */
  readonly plugins: PluginRegistry;
  /** Asset metadata source, when assets have been uploaded. */
  readonly assets?: AssetResolver | undefined;
}

/** A single semantic check over a configuration document. */
export interface ValidationRule {
  /** Stable rule name, used in logs and in the traceability matrix. */
  readonly name: string;
  /** Returns every finding this rule has about the configuration. */
  run(context: RuleContext): readonly Diagnostic[];
}
