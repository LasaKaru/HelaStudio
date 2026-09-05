package dev.shellwright.shell.config

import android.content.Context
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json

/** The JSON reader used for phase two of the config parse. */
public val ShellJson: Json = Json {
    // ⚠️ A shell built at version N must not crash on a config written at
    // version N+1. An app in a store cannot be patched as fast as a config.
    ignoreUnknownKeys = true
    isLenient = false
    explicitNulls = false
    coerceInputValues = true
}

/**
 * Loads the embedded configuration.
 *
 * Two phases, because the first frame cannot wait for the second:
 *
 * 1. [readFirstFrame] runs on the main thread and returns in well under 5 ms.
 *    It is enough to paint the native skeleton — colours, tab labels, the URL
 *    to start loading.
 * 2. [load] runs on a background dispatcher and produces the full typed model.
 *
 * See `FastConfigReader` for why phase one exists at all.
 */
public class ConfigRepository(
    private val context: Context,
    private val io: CoroutineDispatcher = Dispatchers.IO,
    private val assetName: String = ASSET_NAME,
) {
    /** Phase one. Safe to call on the main thread. */
    public fun readFirstFrame(): FastConfigReader.FirstFrame =
        FastConfigReader.read(readAsset())

    /** Phase two. Never call this on the main thread. */
    public suspend fun load(): Result<ShellConfig> = withContext(io) {
        runCatching { ShellJson.decodeFromString<ShellConfig>(readAsset()) }
    }

    private fun readAsset(): String =
        context.assets.open(assetName).bufferedReader().use { it.readText() }

    public companion object {
        /** Where the build pipeline writes the configuration. */
        public const val ASSET_NAME: String = "appconfig.json"
    }
}
