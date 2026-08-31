package dev.shellwright.shell.web

import com.google.common.truth.Truth.assertThat
import org.junit.jupiter.api.Test

class UserAgentTest {

    private val chrome =
        "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36"

    // ⚠️ Replacing the base string breaks feature detection on the customer's
    // own site. This test exists to stop anyone "tidying" that away.
    @Test
    fun `the browser user agent is preserved in full`() {
        val built = UserAgent.build(chrome, shellVersion = "1.0.0", suffix = null)
        assertThat(built).startsWith(chrome)
    }

    @Test
    fun `the shell token is appended so a site can detect the app`() {
        val built = UserAgent.build(chrome, shellVersion = "1.4.0", suffix = null)

        assertThat(built).endsWith("Shellwright/1.4.0")
        assertThat(UserAgent.isShell(built)).isTrue()
    }

    @Test
    fun `a configured suffix is appended after the shell token`() {
        val built = UserAgent.build(chrome, shellVersion = "1.0.0", suffix = "AcmeApp/1.4.0")
        assertThat(built).isEqualTo("$chrome Shellwright/1.0.0 AcmeApp/1.4.0")
    }

    @Test
    fun `a blank suffix adds nothing`() {
        val built = UserAgent.build(chrome, shellVersion = "1.0.0", suffix = "   ")
        assertThat(built).isEqualTo("$chrome Shellwright/1.0.0")
    }

    @Test
    fun `an ordinary browser is not mistaken for the shell`() {
        assertThat(UserAgent.isShell(chrome)).isFalse()
    }
}
