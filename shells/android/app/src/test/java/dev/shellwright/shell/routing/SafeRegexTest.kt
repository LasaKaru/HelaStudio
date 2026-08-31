package dev.shellwright.shell.routing

import com.google.common.truth.Truth.assertThat
import kotlin.system.measureNanoTime
import org.junit.jupiter.api.Test

class SafeRegexTest {

    @Test
    fun `an ordinary pattern matches normally`() {
        val regex = SafeRegex.compile("""^https://app\.acme\.com""")

        assertThat(regex).isNotNull()
        assertThat(regex!!.matches("https://app.acme.com/orders")).isTrue()
        assertThat(regex.matches("https://other.example.com/")).isFalse()
    }

    @Test
    fun `a pattern that does not compile returns null rather than throwing`() {
        assertThat(SafeRegex.compile("(")).isNull()
        assertThat(SafeRegex.compile("[z-a]")).isNull()
    }

    @Test
    fun `the separated-list idiom is not penalised`() {
        // The same pattern Sprint 01's checker was corrected not to reject.
        val regex = SafeRegex.compile("""^[a-z]+(-[a-z]+)*${'$'}""")

        assertThat(regex).isNotNull()
        assertThat(regex!!.matches("some-long-hyphenated-slug")).isTrue()
    }

    @Test
    fun `an exponential pattern gives up instead of hanging`() {
        val regex = SafeRegex.compile("^(a+)+${'$'}")
        assertThat(regex).isNotNull()

        // Without the budget this input backtracks for longer than a phone's
        // ANR timeout, on the UI thread, on every navigation.
        val attack = "a".repeat(64) + "!"

        val elapsedMs = measureNanoTime {
            assertThat(regex!!.matches(attack)).isFalse()
        } / 1_000_000.0

        assertThat(elapsedMs).isLessThan(500.0)
    }

    @Test
    fun `the budget does not interfere with a long legitimate url`() {
        val regex = SafeRegex.compile("""^https://app\.acme\.com/.*""")
        val longUrl = "https://app.acme.com/" + "segment/".repeat(200)

        assertThat(regex!!.matches(longUrl)).isTrue()
    }

    @Test
    fun `the source pattern is available for logging`() {
        val pattern = """^https://app\.acme\.com"""
        assertThat(SafeRegex.compile(pattern)!!.source).isEqualTo(pattern)
    }
}
