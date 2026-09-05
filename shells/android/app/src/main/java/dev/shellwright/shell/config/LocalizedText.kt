package dev.shellwright.shell.config

import kotlinx.serialization.KSerializer
import kotlinx.serialization.Serializable
import kotlinx.serialization.descriptors.SerialDescriptor
import kotlinx.serialization.descriptors.buildClassSerialDescriptor
import kotlinx.serialization.encoding.Decoder
import kotlinx.serialization.encoding.Encoder
import kotlinx.serialization.json.JsonDecoder
import kotlinx.serialization.json.JsonEncoder
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonPrimitive

/**
 * Text that is either one string or a map of translations.
 *
 * The schema models this as a `oneOf`, which is right for the document but
 * awkward for a typed model, so it is collapsed here into one type with a
 * custom serializer. Callers only ever ask for [resolve].
 */
@Serializable(with = LocalizedTextSerializer::class)
public data class LocalizedText(
    /** The text used for any language not listed in [translations]. */
    val default: String,
    /** Per-language overrides, keyed by language tag. */
    val translations: Map<String, String> = emptyMap(),
) {
    /**
     * Returns the best text for a language tag.
     *
     * Falls back from an exact match (`en-GB`) to the base language (`en`) to
     * [default], which is the order a user would expect.
     */
    public fun resolve(languageTag: String): String {
        translations[languageTag]?.let { return it }
        val base = languageTag.substringBefore('-')
        return translations[base] ?: default
    }

    public companion object {
        /** Builds plain, untranslated text. */
        public fun of(value: String): LocalizedText = LocalizedText(value)
    }
}

/** Reads a [LocalizedText] from either a JSON string or a JSON object. */
public object LocalizedTextSerializer : KSerializer<LocalizedText> {
    override val descriptor: SerialDescriptor = buildClassSerialDescriptor("LocalizedText")

    override fun deserialize(decoder: Decoder): LocalizedText {
        val input = decoder as? JsonDecoder
            ?: error("LocalizedText can only be read from JSON.")

        return when (val element = input.decodeJsonElement()) {
            is JsonPrimitive -> LocalizedText(element.content)
            is JsonObject -> {
                val entries = element.mapValues { it.value.jsonPrimitive.content }
                LocalizedText(
                    default = entries["default"].orEmpty(),
                    translations = entries - "default",
                )
            }
            else -> error("LocalizedText must be text or an object of translations.")
        }
    }

    override fun serialize(encoder: Encoder, value: LocalizedText) {
        val output = encoder as? JsonEncoder
            ?: error("LocalizedText can only be written as JSON.")

        if (value.translations.isEmpty()) {
            output.encodeString(value.default)
            return
        }

        val fields = buildMap {
            put("default", JsonPrimitive(value.default))
            value.translations.forEach { (tag, text) -> put(tag, JsonPrimitive(text)) }
        }
        output.encodeJsonElement(JsonObject(fields))
    }
}
