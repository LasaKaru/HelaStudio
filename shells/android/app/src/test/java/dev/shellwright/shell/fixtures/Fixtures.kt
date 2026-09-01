package dev.shellwright.shell.fixtures

import java.io.File

/**
 * Locates the shared fixture corpus in `tests/fixtures`.
 *
 * The corpus is not copied into the module: both shells and both validators
 * must read the same bytes, so the tests walk up to it instead. Copying would
 * turn a contract test into a comparison of two copies.
 *
 * Walking rather than hardcoding a `../../../` prefix, because Gradle's working
 * directory for unit tests is the module directory, which is not where anyone
 * reading the test would guess, and the depth changes the moment a file moves.
 */
object Fixtures {

    val root: File by lazy {
        var directory: File? = File("").absoluteFile
        while (directory != null) {
            val candidate = File(directory, "tests/fixtures")
            if (candidate.isDirectory) return@lazy candidate
            directory = directory.parentFile
        }
        error("Could not locate tests/fixtures from ${File("").absolutePath}")
    }

    fun read(relativePath: String): String {
        val file = File(root, relativePath)
        check(file.exists()) { "Missing shared fixture: ${file.absolutePath}" }
        return file.readText()
    }
}
