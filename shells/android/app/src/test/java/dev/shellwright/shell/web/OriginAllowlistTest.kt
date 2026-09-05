package dev.shellwright.shell.web

import com.google.common.truth.Truth.assertThat
import org.junit.jupiter.api.Test
import org.junit.jupiter.params.ParameterizedTest
import org.junit.jupiter.params.provider.ValueSource

class OriginAllowlistTest {

    private val allowlist = OriginAllowlist(
        listOf("https://app.acme.com", "https://acme.com", "https://staging.acme.com:8443"),
    )

    @Test
    fun `a url on an allowed origin is allowed, whatever its path`() {
        assertThat(allowlist.allows("https://app.acme.com")).isTrue()
        assertThat(allowlist.allows("https://app.acme.com/orders/42?ref=x#top")).isTrue()
    }

    @Test
    fun `a different host is not allowed`() {
        assertThat(allowlist.allows("https://evil.example.com/")).isFalse()
    }

    // A prefix match would allow this. An origin comparison does not.
    @Test
    fun `a host that merely starts with an allowed host is not allowed`() {
        assertThat(allowlist.allows("https://app.acme.com.evil.example/")).isFalse()
    }

    @Test
    fun `a subdomain of an allowed host is not implicitly allowed`() {
        assertThat(allowlist.allows("https://internal.app.acme.com/")).isFalse()
    }

    // ⚠️ The whole point of the allowlist. An http origin would let anyone on
    // the network inject a page that counts as the app's own.
    @Test
    fun `the same host over plain http is not allowed`() {
        assertThat(allowlist.allows("http://app.acme.com/")).isFalse()
    }

    @Test
    fun `port is part of the origin`() {
        assertThat(allowlist.allows("https://staging.acme.com:8443/x")).isTrue()
        assertThat(allowlist.allows("https://staging.acme.com/x")).isFalse()
        assertThat(allowlist.allows("https://staging.acme.com:9000/x")).isFalse()
    }

    @Test
    fun `host comparison ignores case`() {
        assertThat(allowlist.allows("https://APP.ACME.COM/orders")).isTrue()
    }

    @ParameterizedTest
    @ValueSource(
        strings = [
            "file:///data/data/dev.shellwright.shell/databases/x.db",
            "content://com.android.providers/document/1",
            "javascript:alert(1)",
            "data:text/html,<script>alert(1)</script>",
            "about:blank",
        ],
    )
    fun `non-https schemes are never allowed`(url: String) {
        assertThat(allowlist.allows(url)).isFalse()
    }

    @Test
    fun `null, empty, and unparseable input is refused rather than throwing`() {
        assertThat(allowlist.allows(null)).isFalse()
        assertThat(allowlist.allows("")).isFalse()
        assertThat(allowlist.allows("not a url at all")).isFalse()
        assertThat(allowlist.allows("https://")).isFalse()
    }

    // An empty allowlist must deny everything, not allow everything. A config
    // that failed to load must not become an open door.
    @Test
    fun `an empty allowlist denies everything`() {
        val empty = OriginAllowlist(emptyList())

        assertThat(empty.isEmpty).isTrue()
        assertThat(empty.allows("https://app.acme.com/")).isFalse()
    }

    @Test
    fun `malformed entries are dropped without discarding the good ones`() {
        val mixed = OriginAllowlist(listOf("http://insecure.example", "https://app.acme.com", "]["))

        assertThat(mixed.origins).containsExactly("https://app.acme.com")
        assertThat(mixed.allows("https://app.acme.com/")).isTrue()
    }
}
