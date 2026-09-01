package dev.shellwright.shell.routing

import com.google.common.truth.Truth.assertThat
import dev.shellwright.shell.fixtures.Fixtures
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import org.junit.jupiter.api.DynamicTest
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.TestFactory

/**
 * The backtracking-heuristic contract, shared with the studio, the API, and the
 * iOS shell.
 *
 * Four implementations of one judgement, sharing no code. If they disagree, a
 * customer's rule either stops working silently or freezes their app — see
 * `tests/fixtures/regex-safety/README.md`.
 */
class BacktrackingCheckContractTest {

    @Serializable
    data class Corpus(val cases: List<Case>)

    @Serializable
    data class Case(val pattern: String, val verdict: String, val why: String)

    private val json = Json { ignoreUnknownKeys = true }

    private val corpus: Corpus by lazy {
        json.decodeFromString(Corpus.serializer(), Fixtures.read("regex-safety/patterns.json"))
    }

    @TestFactory
    fun `every pattern in the shared corpus is classified as declared`(): List<DynamicTest> =
        corpus.cases.map { case ->
            DynamicTest.dynamicTest("/${case.pattern}/ is ${case.verdict}") {
                val name = when (BacktrackingCheck.verdict(case.pattern)) {
                    is RegexVerdict.Ok -> "ok"
                    is RegexVerdict.Invalid -> "invalid"
                    is RegexVerdict.Catastrophic -> "catastrophic"
                }

                assertThat(name).isEqualTo(case.verdict)
            }
        }

    @Test
    fun `a rejection names the construct that caused it`() {
        val verdict = BacktrackingCheck.verdict("^(a+)+${'$'}")

        // The studio surfaces this text to the user, so it has to point at the
        // part of their pattern that is wrong rather than repeat the whole thing.
        assertThat(verdict).isInstanceOf(RegexVerdict.Catastrophic::class.java)
        assertThat((verdict as RegexVerdict.Catastrophic).construct).isEqualTo("(a+)+")
    }

    @Test
    fun `the scan itself cannot be made slow`() {
        // A checker that hangs on a hostile pattern has only moved the problem.
        val hostile = "(".repeat(2000) + ")*".repeat(2000)

        val elapsedMs = kotlin.system.measureNanoTime {
            BacktrackingCheck.verdict(hostile)
        } / 1_000_000.0

        assertThat(elapsedMs).isLessThan(500.0)
    }
}
