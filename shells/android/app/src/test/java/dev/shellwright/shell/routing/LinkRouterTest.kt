package dev.shellwright.shell.routing

import com.google.common.truth.Truth.assertThat
import dev.shellwright.shell.config.LinkRule
import kotlin.system.measureNanoTime
import org.junit.jupiter.api.Test
import org.junit.jupiter.params.ParameterizedTest
import org.junit.jupiter.params.provider.CsvSource

class LinkRouterTest {

    private fun router(vararg rules: Pair<String, String>) = LinkRouter(
        rules.mapIndexed { index, (pattern, action) ->
            LinkRule(id = "rule-$index", pattern = pattern, action = action)
        },
    )

    private val maximalRouter = router(
        """^https://app\.acme\.com""" to "internal",
        """^https://help\.acme\.com""" to "readerModal",
        """^https://ads\.example\.com""" to "block",
        ".*" to "externalBrowser",
    )

    // TC-S02-AND-021
    @Test
    fun `first matching rule wins, in declared order`() {
        // app.acme.com matches rule 1 and the catch-all; rule 1 is declared
        // first, so drag-to-reorder in the studio means something.
        assertThat(maximalRouter.resolve("https://app.acme.com/orders"))
            .isEqualTo(LinkAction.Internal)
    }

    // TC-S02-AND-022
    @Test
    fun `a later, narrower rule does not override an earlier one`() {
        val shadowed = router(
            ".*" to "internal",
            """^https://help\.acme\.com""" to "readerModal",
        )
        assertThat(shadowed.resolve("https://help.acme.com/faq"))
            .isEqualTo(LinkAction.Internal)
    }

    // TC-S02-AND-023
    @Test
    fun `an unmatched url falls through to the browser`() {
        val noCatchAll = router("""^https://app\.acme\.com""" to "internal")
        assertThat(noCatchAll.resolve("https://external.example.com/x"))
            .isEqualTo(LinkAction.ExternalBrowser)
    }

    @Test
    fun `reader and block actions route correctly`() {
        assertThat(maximalRouter.resolve("https://help.acme.com/faq"))
            .isEqualTo(LinkAction.ReaderModal)
        assertThat(maximalRouter.resolve("https://ads.example.com/track"))
            .isEqualTo(LinkAction.Block)
    }

    // TC-S02-AND-024 — non-http schemes are decided before any pattern runs.
    @ParameterizedTest
    @CsvSource(
        "mailto:hello@acme.com",
        "tel:+94112345678",
        "sms:+94112345678",
        "intent://scan/#Intent;scheme=zxing;end",
        "geo:6.9271,79.8612",
    )
    fun `known external schemes go to another app`(url: String) {
        assertThat(maximalRouter.resolve(url)).isEqualTo(LinkAction.External(url))
    }

    // TC-S02-SEC-001 — a file URL must never be honoured, whatever the rules say.
    @ParameterizedTest
    @CsvSource(
        "file:///data/data/dev.shellwright.shell/databases/x.db",
        "content://com.android.providers/document/1",
        "javascript:alert(1)",
    )
    fun `dangerous schemes are blocked even with a catch-all internal rule`(url: String) {
        val permissive = router(".*" to "internal")
        assertThat(permissive.resolve(url)).isEqualTo(LinkAction.Block)
    }

    // TC-S02-AND-025
    @ParameterizedTest
    @CsvSource(
        "https://app.acme.com/invoice.pdf",
        "https://app.acme.com/export.csv?range=90d",
        "https://app.acme.com/report.xlsx#sheet2",
    )
    fun `a downloadable file goes to the download manager`(url: String) {
        assertThat(maximalRouter.resolve(url)).isEqualTo(LinkAction.Download(url))
    }

    @Test
    fun `a page path that merely contains a dot is not a download`() {
        assertThat(maximalRouter.resolve("https://app.acme.com/v1.2/release-notes"))
            .isEqualTo(LinkAction.Internal)
    }

    // TC-S02-AND-026 — the shell defends even though S01 rejects these at
    // config time: a config may predate the rule, and slow is bad enough.
    @Test
    fun `a catastrophic pattern returns quickly rather than hanging`() {
        val hostile = router("^(a+)+${'$'}" to "internal", ".*" to "externalBrowser")
        val attack = "a".repeat(60) + "!"

        val elapsedMs = measureNanoTime {
            assertThat(hostile.resolve(attack)).isEqualTo(LinkAction.ExternalBrowser)
        } / 1_000_000.0

        assertThat(elapsedMs).isLessThan(200.0)
    }

    @Test
    fun `a pattern that does not compile is reported and skipped`() {
        val broken = router("^https://app\\.acme\\.com(" to "internal", ".*" to "externalBrowser")

        assertThat(broken.rejectedPatterns).hasSize(1)
        assertThat(broken.resolve("https://app.acme.com/x")).isEqualTo(LinkAction.ExternalBrowser)
    }

    @Test
    fun `an action added by a newer schema degrades to the browser`() {
        // A shell at version N must not crash on a config written at N+1.
        val future = router("""^https://app\.acme\.com""" to "holographicProjection")
        assertThat(future.resolve("https://app.acme.com/x")).isEqualTo(LinkAction.ExternalBrowser)
    }

    // TC-S02-PRF-004 — under 1 ms mean over 10,000 resolutions, with 200 rules.
    @Test
    fun `resolves a 200-rule config well inside the per-navigation budget`() {
        val manyRules = (1..199).map {
            LinkRule("rule-$it", """^https://app\.acme\.com/section-$it/""", "internal")
        } + LinkRule("fallback", ".*", "externalBrowser")

        val busy = LinkRouter(manyRules)
        val urls = (1..100).map { "https://app.acme.com/section-$it/page-$it" }

        // Warm the JIT and populate nothing: each URL is distinct per pass.
        repeat(WARMUP_PASSES) { pass -> urls.forEach { busy.resolve("$it?warm=$pass") } }

        val resolutions = 10_000
        val elapsedNs = measureNanoTime {
            repeat(resolutions / urls.size) { pass ->
                urls.forEach { busy.resolve("$it?run=$pass") }
            }
        }

        val meanMs = elapsedNs / resolutions.toDouble() / 1_000_000.0
        assertThat(meanMs).isLessThan(1.0)
    }

    @Test
    fun `repeated resolutions of the same url are cached`() {
        val busy = router(""".*""" to "internal")
        val url = "https://app.acme.com/orders"

        val first = measureNanoTime { busy.resolve(url) }
        val cached = measureNanoTime { repeat(1_000) { busy.resolve(url) } } / 1_000

        // A cache hit must be cheaper than the original resolution. Comparing
        // against the first call rather than a fixed number keeps this stable
        // on a loaded CI machine.
        assertThat(cached).isLessThan(first)
    }

    private companion object {
        const val WARMUP_PASSES = 5
    }
}
