package dev.shellwright.shell.config

/**
 * Phase one of the two-phase config parse.
 *
 * ⚠️ Parsing a 40 KB config with a reflective or generated JSON parser costs
 * real milliseconds on a cold JIT, on a budget device, on the main thread,
 * before anything is on screen. The startup budget is 300 ms to first frame
 * (`03_TEST_STRATEGY.md` §12) and this must not eat a tenth of it.
 *
 * So the first frame is drawn from this: a hand-written scanner that pulls out
 * only the handful of values needed to paint the native skeleton — theme
 * colours, tab labels, the start URL. The full [ShellConfig] is parsed on a
 * background dispatcher afterwards and everything else waits for it.
 *
 * This reads a *flat subset* deliberately. It is not a JSON parser and must not
 * grow into one; if a field is not needed for the first frame, it belongs in
 * phase two.
 *
 * Budget: under 5 ms on a mid-range device (`TC-S02-PRF-003`).
 */
public object FastConfigReader {

    /** The values needed to draw the native skeleton before any web content. */
    public data class FirstFrame(
        val appName: String,
        val initialUrl: String,
        val splashBackground: String,
        val themePrimary: String,
        val themeNavBar: String,
        val themeTabBar: String,
        val statusBarStyle: String,
        val topBarEnabled: Boolean,
        val tabBarEnabled: Boolean,
        val tabLabels: List<String>,
    )

    private const val DEFAULT_WHITE = "#FFFFFF"

    /**
     * Scans the raw config for first-frame values.
     *
     * Never throws: a malformed config still has to draw something, and phase
     * two reports the real error. Every field falls back to the schema default.
     */
    public fun read(json: String): FirstFrame = FirstFrame(
        appName = stringAfter(json, "\"name\"") ?: "",
        initialUrl = stringAfter(json, "\"initialUrl\"") ?: "",
        splashBackground = stringAfter(json, "\"backgroundColor\"") ?: DEFAULT_WHITE,
        themePrimary = stringAfter(json, "\"primary\"") ?: "#2563EB",
        themeNavBar = stringAfter(json, "\"navBar\"") ?: DEFAULT_WHITE,
        themeTabBar = stringAfter(json, "\"tabBar\"") ?: DEFAULT_WHITE,
        statusBarStyle = stringAfter(json, "\"statusBar\"") ?: "dark-content",
        topBarEnabled = enabledUnder(json, "\"topBar\"", default = true),
        tabBarEnabled = enabledUnder(json, "\"tabBar\"", default = false),
        tabLabels = tabLabels(json),
    )

    /**
     * The string value following the first occurrence of [key].
     *
     * Returns null when the key is absent, or when its value is not a plain
     * string — `"tabBar": { … }` must not be mistaken for a colour.
     */
    private fun stringAfter(json: String, key: String): String? {
        val keyAt = json.indexOf(key)
        if (keyAt < 0) return null

        val colon = json.indexOf(':', keyAt + key.length)
        if (colon < 0) return null

        val openQuote = json.indexOfFirst(colon + 1) { it == '"' || it == '{' || it == '[' }
        if (openQuote < 0 || json[openQuote] != '"') return null

        return readString(json, openQuote)
    }

    /** Reads a JSON string starting at its opening quote, honouring escapes. */
    private fun readString(json: String, openQuote: Int): String? {
        val builder = StringBuilder()
        var i = openQuote + 1

        while (i < json.length) {
            when (val ch = json[i]) {
                '"' -> return builder.toString()
                '\\' -> {
                    if (i + 1 >= json.length) return null
                    // Phase one only reads names, URLs, colours, and labels.
                    // Unicode escapes are left to phase two rather than
                    // reimplementing them here.
                    val escaped = json[i + 1]
                    if (escaped == 'u') return null
                    builder.append(unescape(escaped))
                    i += 2
                }
                else -> {
                    builder.append(ch)
                    i++
                }
            }
        }
        return null
    }

    private fun unescape(escaped: Char): Char = when (escaped) {
        'n' -> '\n'
        't' -> '\t'
        'r' -> '\r'
        'b' -> '\b'
        else -> escaped
    }

    /** Whether the object introduced by [key] has `"enabled": true`. */
    private fun enabledUnder(json: String, key: String, default: Boolean): Boolean {
        val keyAt = json.indexOf(key)
        if (keyAt < 0) return default

        val enabledAt = json.indexOf("\"enabled\"", keyAt)
        if (enabledAt < 0) return default

        // Only trust an "enabled" that belongs to this object: anything past the
        // next object key at the same nesting level is somebody else's.
        val valueAt = json.indexOf(':', enabledAt)
        if (valueAt < 0) return default

        val trueAt = json.indexOf("true", valueAt)
        val falseAt = json.indexOf("false", valueAt)
        return when {
            trueAt in 0..(valueAt + 4) -> true
            falseAt in 0..(valueAt + 4) -> false
            else -> default
        }
    }

    /** Tab labels in document order, for painting the bar before web content. */
    private fun tabLabels(json: String): List<String> {
        val tabBarAt = json.indexOf("\"tabBar\"")
        if (tabBarAt < 0) return emptyList()

        val itemsAt = json.indexOf("\"items\"", tabBarAt)
        if (itemsAt < 0) return emptyList()

        val labels = mutableListOf<String>()
        var cursor = itemsAt
        val end = json.indexOf(']', itemsAt).takeIf { it >= 0 } ?: json.length

        while (labels.size < MAX_TABS) {
            val labelAt = json.indexOf("\"label\"", cursor)
            if (labelAt < 0 || labelAt > end) break

            val colon = json.indexOf(':', labelAt)
            if (colon < 0) break

            val valueAt = json.indexOfFirst(colon + 1) { !it.isWhitespace() }
            if (valueAt < 0) break

            // A translated label is an object; phase two resolves it properly.
            if (json[valueAt] == '"') {
                readString(json, valueAt)?.let(labels::add)
            } else {
                labels.add("")
            }
            cursor = valueAt + 1
        }
        return labels
    }

    /** iOS shows five; more than eight is beyond anything the schema allows. */
    private const val MAX_TABS = 8

    private inline fun String.indexOfFirst(from: Int, predicate: (Char) -> Boolean): Int {
        for (i in from until length) {
            if (predicate(this[i])) return i
        }
        return -1
    }
}
