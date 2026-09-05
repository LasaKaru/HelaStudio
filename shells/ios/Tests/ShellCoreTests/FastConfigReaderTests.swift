import Foundation
import Testing
@testable import ShellCore

/// Phase one of the startup parse, which runs on the main thread before the
/// first frame. Must agree with the Kotlin `FastConfigReader` field for field.
struct FastConfigReaderTests {

    @Test("reads the first-frame values from the maximal fixture")
    func readsMaximal() throws {
        let frame = FastConfigReader.read(try Fixtures.config("maximal.json"))

        #expect(frame.appName == "Acme Orders")
        #expect(frame.initialUrl == "https://app.acme.com/")
        #expect(frame.splashBackground == "#0B1220")
        #expect(frame.themePrimary == "#2563EB")
        #expect(frame.statusBarStyle == "dark-content")
        #expect(frame.tabBarEnabled)
        #expect(frame.tabLabels == ["Home", "Orders", "Scan", "Account"])
    }

    @Test("falls back to schema defaults for the minimal fixture")
    func readsMinimal() throws {
        let frame = FastConfigReader.read(try Fixtures.config("minimal.json"))

        #expect(frame.appName == "Minimal")
        #expect(frame.splashBackground == "#FFFFFF")
        #expect(frame.themePrimary == "#2563EB")
        #expect(!frame.tabBarEnabled)
        #expect(frame.tabLabels.isEmpty)
    }

    /// `unicode.json` has one tab whose label is an object of translations.
    /// Phase one cannot resolve it, and must not mis-read the next tab.
    @Test("a translated label does not derail the scan")
    func translatedLabel() throws {
        let frame = FastConfigReader.read(try Fixtures.config("unicode.json"))

        #expect(frame.tabLabels.count == 4)
        #expect(frame.tabLabels[1].isEmpty)
    }

    /// Never throw. A malformed config still has to draw something; phase two
    /// reports the real error.
    @Test("malformed input yields defaults rather than a crash")
    func malformedInput() {
        let frame = FastConfigReader.read("{\"app\": {\"name\": \"Broken")

        #expect(frame.initialUrl.isEmpty)
        #expect(frame.themePrimary == "#2563EB")
    }

    @Test("an empty document yields defaults")
    func emptyInput() {
        let frame = FastConfigReader.read("")

        #expect(frame.appName.isEmpty)
        #expect(frame.splashBackground == "#FFFFFF")
    }

    /// Runs on the main thread before the first frame, against a 300 ms
    /// startup budget. `TC-S03-PRF-003`.
    @Test("reads the maximal fixture well inside the first-frame budget")
    func withinFirstFrameBudget() throws {
        let json = try Fixtures.config("maximal.json")

        for _ in 0..<50 { _ = FastConfigReader.read(json) }

        let runs = 200
        let started = Date()
        for _ in 0..<runs { _ = FastConfigReader.read(json) }
        let meanMillis = Date().timeIntervalSince(started) / Double(runs) * 1000

        #expect(meanMillis < 5.0, "mean \(meanMillis) ms against a 5 ms budget")
    }
}
