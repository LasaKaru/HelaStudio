package dev.shellwright.shell.config

import com.google.common.truth.Truth.assertThat
import java.io.File
import org.junit.jupiter.api.Test
import org.junit.jupiter.params.ParameterizedTest
import org.junit.jupiter.params.provider.ValueSource

class ShellConfigTest {

    private fun fixture(name: String): String =
        File("../../../tests/fixtures/configs/$name").readText()

    // TC-S02-AND-002 — the shell must parse every configuration the validator
    // accepts. This is the shell's half of the schema contract.
    @ParameterizedTest
    @ValueSource(
        strings = [
            "minimal.json",
            "maximal.json",
            "all-plugins.json",
            "unicode.json",
            "edge-no-tabs.json",
            "edge-many-tabs.json",
            "edge-long-bundleid.json",
            "edge-many-linkrules.json",
            "edge-single-page.json",
            "edge-deep-nesting.json",
        ],
    )
    fun `parses every valid fixture`(name: String) {
        val config = ShellJson.decodeFromString<ShellConfig>(fixture(name))
        assertThat(config.app.initialUrl).startsWith("https://")
    }

    @Test
    fun `reads the maximal fixture into the expected shape`() {
        val config = ShellJson.decodeFromString<ShellConfig>(fixture("maximal.json"))

        assertThat(config.app.name).isEqualTo("Acme Orders")
        assertThat(config.app.versionCode).isEqualTo(42)
        assertThat(config.navigation.tabBar.items).hasSize(4)
        assertThat(config.linkRules).hasSize(4)
        assertThat(config.webOverrides.userAgentSuffix).isEqualTo("AcmeApp/1.4.0")
        assertThat(config.permissions.wantsLocation).isTrue()
    }

    @Test
    fun `applies schema defaults for omitted fields`() {
        val config = ShellJson.decodeFromString<ShellConfig>(fixture("minimal.json"))

        assertThat(config.app.versionName).isEqualTo("1.0.0")
        assertThat(config.branding.darkMode).isEqualTo("system")
        assertThat(config.webOverrides.persistCookies).isTrue()
        assertThat(config.webOverrides.allowZoom).isFalse()
        assertThat(config.build.androidSettings.minSdk).isEqualTo(24)
    }

    // TC-S02-AND-003 — ⚠️ a shell at version N must not crash on a config
    // written at version N+1. An app in a store cannot be patched as fast as a
    // config can be edited.
    @Test
    fun `ignores fields added by a newer schema`() {
        val fromTheFuture = """
            {
              "schemaVersion": 1,
              "app": {
                "name": "Future",
                "bundleId": "com.acme.future",
                "initialUrl": "https://app.acme.com/",
                "allowedOrigins": ["https://app.acme.com"],
                "hyperdriveEnabled": true
              },
              "quantumSurfaces": [{ "id": "one" }]
            }
        """.trimIndent()

        val config = ShellJson.decodeFromString<ShellConfig>(fromTheFuture)
        assertThat(config.app.name).isEqualTo("Future")
    }

    @Test
    fun `resolves localized labels with sensible fallback`() {
        val config = ShellJson.decodeFromString<ShellConfig>(fixture("unicode.json"))
        val orders = config.navigation.tabBar.items[1].label

        assertThat(orders.resolve("ar")).isEqualTo("الطلبات")
        assertThat(orders.resolve("en-GB")).isEqualTo("Orders")
        // No French translation, so the default is used.
        assertThat(orders.resolve("fr-CA")).isEqualTo(orders.default)
    }

    @Test
    fun `plain text labels need no translation map`() {
        val config = ShellJson.decodeFromString<ShellConfig>(fixture("maximal.json"))
        val home = config.navigation.tabBar.items[0].label

        assertThat(home.resolve("ar")).isEqualTo("Home")
        assertThat(home.translations).isEmpty()
    }
}
