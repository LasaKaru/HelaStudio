# Diagnostic codes

Every finding the configuration validator produces carries one of these codes.
Codes are permanent: if a rule is removed its code is retired, never reused, so a
support article written today stays accurate.

`error` blocks a save and a build. `warning` does not block, but most warnings
here predict a store rejection, which is more expensive than a failed build.

This file is generated from `packages/config-schema/src/diagnostics.ts`.

## Schema shape

| Code                                                                        | Severity | What it means                                                                         | What to do                                                                           |
| --------------------------------------------------------------------------- | -------- | ------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------ |
| <a id="cfg_schema_violation"></a>`CFG_SCHEMA_VIOLATION`                     | error    | The document does not match the schema.                                               | Read the message: it names the field and what it expected.                           |
| <a id="cfg_schema_version_unsupported"></a>`CFG_SCHEMA_VERSION_UNSUPPORTED` | error    | The configuration was written for a newer schema version than this build understands. | Update to a newer release before opening it.                                         |
| <a id="cfg_unknown_field"></a>`CFG_UNKNOWN_FIELD`                           | error    | A field name is not recognised at this position.                                      | Check the spelling, or move it under an `x-` prefixed object if it is your own data. |

## Identity and naming

| Code                                                      | Severity | What it means                                   | What to do                                                                                                    |
| --------------------------------------------------------- | -------- | ----------------------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| <a id="cfg_bundle_id_invalid"></a>`CFG_BUNDLE_ID_INVALID` | error    | The bundle identifier is not valid reverse-DNS. | Use lowercase with at least one dot, such as `com.acme.app`. Both stores reject uppercase and leading digits. |
| <a id="cfg_name_too_long"></a>`CFG_NAME_TOO_LONG`         | error    | The app name exceeds 30 characters.             | Shorten it. Use the full name in your store listing instead.                                                  |

## Origins and URLs

| Code                                                                  | Severity | What it means                                                                                    | What to do                                                                                      |
| --------------------------------------------------------------------- | -------- | ------------------------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------- |
| <a id="cfg_initial_url_not_allowed"></a>`CFG_INITIAL_URL_NOT_ALLOWED` | error    | The start URL is not under an allowed origin.                                                    | Add its origin to `app.allowedOrigins`, or change the start URL.                                |
| <a id="cfg_origin_not_covered"></a>`CFG_ORIGIN_NOT_COVERED`           | error    | A navigation destination is not under an allowed origin, so it would open in the device browser. | Add the origin to `app.allowedOrigins`.                                                         |
| <a id="cfg_cleartext_url"></a>`CFG_CLEARTEXT_URL`                     | error    | A plain `http://` URL appears in the configuration.                                              | Use `https://`. Both platforms block cleartext by default, so this would fail on a real device. |

## Link rules

| Code                                                              | Severity | What it means                                                                         | What to do                                                                                   |
| ----------------------------------------------------------------- | -------- | ------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------- |
| <a id="cfg_regex_invalid"></a>`CFG_REGEX_INVALID`                 | error    | A link rule or tab pattern does not compile.                                          | Check for an unclosed bracket, and escape dots in domain names.                              |
| <a id="cfg_regex_catastrophic"></a>`CFG_REGEX_CATASTROPHIC`       | error    | A pattern nests one repetition inside another and can take exponential time to match. | Rewrite with a single repetition. This runs on every navigation, so it would freeze the app. |
| <a id="cfg_link_rule_unreachable"></a>`CFG_LINK_RULE_UNREACHABLE` | warning  | A link rule is shadowed by an earlier, broader rule and can never fire.               | Move it above the broader rule, or remove it.                                                |
| <a id="cfg_link_rule_no_catchall"></a>`CFG_LINK_RULE_NO_CATCHALL` | warning  | No rule matches every remaining link.                                                 | Add a final rule with the pattern `.*`, usually `externalBrowser`.                           |

## Navigation

| Code                                                      | Severity | What it means                      | What to do                                                                    |
| --------------------------------------------------------- | -------- | ---------------------------------- | ----------------------------------------------------------------------------- |
| <a id="cfg_tab_count_high"></a>`CFG_TAB_COUNT_HIGH`       | warning  | More than five tabs.               | iOS hides everything past the fourth behind a "More" tab. Keep five or fewer. |
| <a id="cfg_duplicate_item_id"></a>`CFG_DUPLICATE_ITEM_ID` | error    | Two items in one list share an id. | Give each item its own id so edits and reordering are tracked correctly.      |

## Store readiness

| Code                                                                | Severity | What it means                                                     | What to do                                                                                               |
| ------------------------------------------------------------------- | -------- | ----------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------- |
| <a id="cfg_no_native_features"></a>`CFG_NO_NATIVE_FEATURES`         | warning  | The app has no native navigation, plugins, or native screens.     | Apple rejects these under guideline 4.2. Add a tab bar, a drawer, a capability, or an onboarding screen. |
| <a id="cfg_permission_unjustified"></a>`CFG_PERMISSION_UNJUSTIFIED` | warning  | A permission is requested that nothing in the configuration uses. | Enable the plugin that needs it, or turn the permission off.                                             |

## Plugins

| Code                                                                      | Severity | What it means                                            | What to do                                                                    |
| ------------------------------------------------------------------------- | -------- | -------------------------------------------------------- | ----------------------------------------------------------------------------- |
| <a id="cfg_plugin_unknown"></a>`CFG_PLUGIN_UNKNOWN`                       | error    | A plugin id is not in the registry.                      | Check the spelling against the plugin library.                                |
| <a id="cfg_plugin_config_invalid"></a>`CFG_PLUGIN_CONFIG_INVALID`         | error    | A plugin's settings fail that plugin's own schema.       | Read the message: it names the setting and what it expected.                  |
| <a id="cfg_plugin_conflict"></a>`CFG_PLUGIN_CONFLICT`                     | error    | Two enabled plugins declare a mutual conflict.           | Remove one of them.                                                           |
| <a id="cfg_plugin_min_sdk"></a>`CFG_PLUGIN_MIN_SDK`                       | error    | A plugin requires a newer platform than the app targets. | Raise `build.android.minSdk` or `build.ios.minVersion`, or remove the plugin. |
| <a id="cfg_plugin_permission_missing"></a>`CFG_PLUGIN_PERMISSION_MISSING` | error    | A plugin's required permission is switched off.          | Turn the permission on, or remove the plugin.                                 |

## Assets

| Code                                                  | Severity | What it means                                               | What to do                                                                     |
| ----------------------------------------------------- | -------- | ----------------------------------------------------------- | ------------------------------------------------------------------------------ |
| <a id="cfg_asset_missing"></a>`CFG_ASSET_MISSING`     | error    | A referenced file is not in the workspace.                  | Upload it again; the previous upload may not have finished.                    |
| <a id="cfg_icon_dimensions"></a>`CFG_ICON_DIMENSIONS` | error    | The source icon is smaller than 1024x1024 or is not square. | Every store size is generated from it, and the App Store requires 1024x1024.   |
| <a id="cfg_icon_alpha"></a>`CFG_ICON_ALPHA`           | error    | The source icon has a transparent background.               | Apple rejects app icons with an alpha channel. Flatten it onto a solid colour. |

## Secrets

| Code                                                    | Severity | What it means                    | What to do                                                                                                                     |
| ------------------------------------------------------- | -------- | -------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| <a id="cfg_secret_in_config"></a>`CFG_SECRET_IN_CONFIG` | error    | A value looks like a credential. | Configuration is embedded in the shipped app where anyone can read it. Store the credential separately and reference it by id. |

## Migration failures

`ConfigMigrator` raises `CFG_SCHEMA_VERSION_UNSUPPORTED` when a stored
configuration has no readable `schemaVersion`, is from a future version, or
has no migration path to the current version. It never migrates silently or
partially.
