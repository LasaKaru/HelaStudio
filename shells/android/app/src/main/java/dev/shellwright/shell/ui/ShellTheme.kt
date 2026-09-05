package dev.shellwright.shell.ui

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.luminance

/** The colours the shell paints its native chrome with. */
public data class ShellColors(
    val primary: Color,
    val navBar: Color,
    val tabBar: Color,
    val splashBackground: Color,
) {
    /**
     * Whether the status bar should use dark icons.
     *
     * Derived from the nav bar's luminance rather than trusted from config: a
     * customer who sets a dark nav bar and forgets to change `statusBar` would
     * otherwise get an unreadable clock.
     */
    public val navBarIsLight: Boolean get() = navBar.luminance() > LIGHT_THRESHOLD

    public companion object {
        private const val LIGHT_THRESHOLD = 0.5f

        /**
         * Parses `#RRGGBB` or `#RRGGBBAA`, falling back to [fallback].
         *
         * Never throws: a bad colour must not stop the app from drawing.
         */
        public fun parseColor(value: String?, fallback: Color): Color {
            val hex = value?.removePrefix("#") ?: return fallback
            return when (hex.length) {
                RGB_LENGTH -> hex.toLongOrNull(radix = HEX)?.let { Color(it or ALPHA_OPAQUE) }
                RGBA_LENGTH -> hex.toLongOrNull(radix = HEX)?.let { rgba ->
                    // The schema writes #RRGGBBAA; Compose wants 0xAARRGGBB.
                    val alpha = rgba and 0xFF
                    Color((alpha shl RGB_BITS) or (rgba ushr BYTE_BITS))
                }
                else -> null
            } ?: fallback
        }

        private const val HEX = 16
        private const val RGB_LENGTH = 6
        private const val RGBA_LENGTH = 8
        private const val BYTE_BITS = 8
        private const val RGB_BITS = 24
        private const val ALPHA_OPAQUE = 0xFF00_0000L
    }
}

/**
 * Applies the configured colours.
 *
 * `darkMode` decides whether the app follows the device or pins itself, which
 * is a config choice rather than something to infer.
 */
@Composable
public fun ShellTheme(
    colors: ShellColors,
    useDarkTheme: Boolean,
    content: @Composable () -> Unit,
) {
    val scheme = if (useDarkTheme) {
        darkColorScheme(primary = colors.primary, surface = colors.navBar)
    } else {
        lightColorScheme(primary = colors.primary, surface = colors.navBar)
    }

    MaterialTheme(colorScheme = scheme, content = content)
}
