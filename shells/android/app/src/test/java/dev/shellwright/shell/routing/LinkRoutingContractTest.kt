package dev.shellwright.shell.routing

import com.google.common.truth.Truth.assertThat
import dev.shellwright.shell.config.LinkRule
import dev.shellwright.shell.fixtures.Fixtures
import kotlin.system.measureNanoTime
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import org.junit.jupiter.api.DynamicTest
import org.junit.jupiter.api.TestFactory
import org.junit.jupiter.api.Test

/**
 * The routing contract between the two shells.
 *
 * The Android and iOS routers share no code — the behaviour is ported between
 * Kotlin and Swift, the source is not — so this corpus is the only thing that
 * catches them drifting apart. The Swift suite reads the same file and asserts
 * the same decisions.
 *
 * It is the same technique that holds the TypeScript and C# config validators
 * together, applied to the second place in the system where one behaviour has
 * two implementations.
 */
class LinkRoutingContractTest {

    @Serializable
    private data class Corpus(
        val ruleSets: Map<String, List<CorpusRule>>,
        val cases: List<Case>,
    )

    @Serializable
    private data class CorpusRule(val id: String, val pattern: String, val action: String)

    @Serializable
    private data class Case(
        val why: String,
        val rules: String,
        val url: String,
        val expect: String,
        val maxMillis: Double? = null,
    )

    private val json = Json { ignoreUnknownKeys = true }

    private val corpus: Corpus =
        json.decodeFromString(Fixtures.read("routing/link-routing.json"))

    /** The stable name each action carries in the shared corpus. */
    private fun LinkAction.fixtureName(): String = when (this) {
        is LinkAction.Internal -> "internal"
        is LinkAction.Modal -> "modal"
        is LinkAction.ReaderModal -> "readerModal"
        is LinkAction.ExternalBrowser -> "externalBrowser"
        is LinkAction.Block -> "block"
        is LinkAction.External -> "external"
        is LinkAction.Download -> "download"
    }

    @TestFactory
    fun `every case in the shared corpus routes as declared`(): List<DynamicTest> =
        corpus.cases.map { case ->
            DynamicTest.dynamicTest("${case.url} -> ${case.expect}") {
                val rules = requireNotNull(corpus.ruleSets[case.rules]) {
                    "the corpus names a rule set '${case.rules}' that it does not define"
                }

                val router = LinkRouter(
                    rules.map { LinkRule(id = it.id, pattern = it.pattern, action = it.action) },
                )

                var resolved: LinkAction
                val elapsedMillis = measureNanoTime { resolved = router.resolve(case.url) } / 1_000_000.0

                assertThat(resolved.fixtureName()).isEqualTo(case.expect)

                case.maxMillis?.let { budget ->
                    assertThat(elapsedMillis).isLessThan(budget)
                }
            }
        }

    @Test
    fun `the corpus covers every action the router can return`() {
        val covered = corpus.cases.map { it.expect }.toSet()

        // A router action with no fixture is an untested decision, and the
        // other shell has nothing to be held to.
        val expected = setOf(
            "internal", "readerModal", "externalBrowser", "block", "external", "download",
        )

        assertThat(covered).containsAtLeastElementsIn(expected)
    }
}
