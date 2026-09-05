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
    fun `an exponential pattern is refused before it can ever run`() {
        // The first layer: the same structural check the studio, the API and
        // the iOS shell run. Refusing here rather than surviving it at match
        // time is what keeps the two shells agreeing about what a config means.
        assertThat(SafeRegex.compile("^(a+)+${'$'}")).isNull()
    }

    @Test
    fun `the budget bounds a slow pattern the structural check does not catch`() {
        // Nine sequential stars and not a single group, so nothing in
        // BacktrackingCheck has anything to look at — it is polynomial rather
        // than exponential, and it compiles. It still takes the better part of a
        // second on this input, on the UI thread, on every navigation.
        //
        // This is the whole value of the second layer, and the gap the iOS
        // shell has to live with: see docs/qa/shell-parity.md.
        val regex = SafeRegex.compile("^a*a*a*a*a*a*a*a*a*b${'$'}")
        assertThat(regex).isNotNull()

        val attack = "a".repeat(28)

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
