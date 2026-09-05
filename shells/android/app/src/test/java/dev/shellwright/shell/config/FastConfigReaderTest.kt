package dev.shellwright.shell.config

import com.google.common.truth.Truth.assertThat
import java.io.File
import kotlin.system.measureNanoTime
import org.junit.jupiter.api.Test

class FastConfigReaderTest {

    private fun fixture(name: String): String =
        File("../../../tests/fixtures/configs/$name").readText()

    // TC-S02-AND-002
    @Test
    fun `reads the first-frame values from the maximal fixture`() {
        val frame = FastConfigReader.read(fixture("maximal.json"))

        assertThat(frame.appName).isEqualTo("Acme Orders")
        assertThat(frame.initialUrl).isEqualTo("https://app.acme.com/")
        assertThat(frame.splashBackground).isEqualTo("#0B1220")
        assertThat(frame.themePrimary).isEqualTo("#2563EB")
        assertThat(frame.statusBarStyle).isEqualTo("dark-content")
        assertThat(frame.tabBarEnabled).isTrue()
        assertThat(frame.tabLabels).containsExactly("Home", "Orders", "Scan", "Account").inOrder()
    }

    @Test
    fun `falls back to schema defaults for the minimal fixture`() {
        val frame = FastConfigReader.read(fixture("minimal.json"))

        assertThat(frame.appName).isEqualTo("Minimal")
        assertThat(frame.splashBackground).isEqualTo("#FFFFFF")
        assertThat(frame.themePrimary).isEqualTo("#2563EB")
        assertThat(frame.tabBarEnabled).isFalse()
        assertThat(frame.tabLabels).isEmpty()
    }

    @Test
    fun `a translated label does not derail the scan`() {
        // unicode.json has one tab whose label is an object of translations.
        // Phase one cannot resolve it, and must not mis-read the next tab.
        val frame = FastConfigReader.read(fixture("unicode.json"))

        assertThat(frame.tabLabels).hasSize(4)
        assertThat(frame.tabLabels[1]).isEmpty()
    }

    // TC-S02-AND-003 — never throw. A malformed config still has to draw
    // something; phase two reports the real error.
    @Test
    fun `malformed input yields defaults rather than an exception`() {
        val frame = FastConfigReader.read("""{"app": {"name": "Broken""")

        assertThat(frame.initialUrl).isEmpty()
        assertThat(frame.themePrimary).isEqualTo("#2563EB")
    }

    @Test
    fun `an empty document yields defaults`() {
        val frame = FastConfigReader.read("")
        assertThat(frame.appName).isEmpty()
        assertThat(frame.splashBackground).isEqualTo("#FFFFFF")
    }

    // TC-S02-PRF-003 — this runs on the main thread before the first frame.
    @Test
    fun `reads the maximal fixture well inside the first-frame budget`() {
        val json = fixture("maximal.json")

        repeat(WARMUP) { FastConfigReader.read(json) }

        val runs = 200
        val elapsedNs = measureNanoTime { repeat(runs) { FastConfigReader.read(json) } }
        val meanMs = elapsedNs / runs.toDouble() / 1_000_000.0

        assertThat(meanMs).isLessThan(5.0)
    }

    private companion object {
        const val WARMUP = 50
    }
}
