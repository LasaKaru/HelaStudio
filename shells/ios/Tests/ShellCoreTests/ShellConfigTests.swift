import Foundation
import Testing
@testable import ShellCore

/// The shell's half of the schema contract.
///
/// Every configuration the validator accepts must decode here. A config that
/// passes every check in the pipeline and then fails on a customer's phone is
/// the failure this suite exists to prevent.
struct ShellConfigTests {

    private func decode(_ name: String) throws -> ShellConfig {
        let json = try Fixtures.config(name)
        return try ShellJSON.decoder().decode(ShellConfig.self, from: Data(json.utf8))
    }

    @Test("parses every valid fixture", arguments: Fixtures.validConfigs)
    func parsesValidFixtures(name: String) throws {
        let config = try decode(name)
        #expect(config.app.initialUrl.hasPrefix("https://"))
    }

    @Test("reads the maximal fixture into the expected shape")
    func maximalShape() throws {
        let config = try decode("maximal.json")

        #expect(config.app.name == "Acme Orders")
        #expect(config.app.versionCode == 42)
        #expect(config.navigation.tabBar.items.count == 4)
        #expect(config.linkRules.count == 4)
        #expect(config.webOverrides.userAgentSuffix == "AcmeApp/1.4.0")
        #expect(config.permissions.wantsLocation)
    }

    @Test("applies schema defaults for omitted fields")
    func defaultsAreApplied() throws {
        let config = try decode("minimal.json")

        #expect(config.app.versionName == "1.0.0")
        #expect(config.branding.darkMode == "system")
        #expect(config.webOverrides.persistCookies)
        #expect(!config.webOverrides.allowZoom)
        #expect(config.build.ios.minVersion == "15.0")
    }

    /// A shell built at version N must not fail on a config written at N+1. An
    /// app in a store cannot be patched as quickly as a config can be edited.
    @Test("ignores fields added by a newer schema")
    func toleratesFutureFields() throws {
        let fromTheFuture = """
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
        """

        let config = try ShellJSON.decoder().decode(ShellConfig.self, from: Data(fromTheFuture.utf8))
        #expect(config.app.name == "Future")
    }

    @Test("resolves localized labels with sensible fallback")
    func localizedLabels() throws {
        let config = try decode("unicode.json")
        let orders = config.navigation.tabBar.items[1].label

        #expect(orders.resolve(languageTag: "ar") == "الطلبات")
        #expect(orders.resolve(languageTag: "en-GB") == "Orders")
        // No French translation, so the default is used.
        #expect(orders.resolve(languageTag: "fr-CA") == orders.default)
    }

    @Test("plain text labels need no translation map")
    func plainLabels() throws {
        let config = try decode("maximal.json")
        let home = config.navigation.tabBar.items[0].label

        #expect(home.resolve(languageTag: "ar") == "Home")
        #expect(home.translations.isEmpty)
    }
}
