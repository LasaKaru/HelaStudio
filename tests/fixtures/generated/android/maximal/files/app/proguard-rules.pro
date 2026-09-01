# R8 full mode is on, so anything reached only by reflection must be kept
# explicitly. Getting this wrong produces an app that works in debug and
# crashes in release, which is the worst failure mode available.

# kotlinx.serialization generates serializers reflectively at class level.
-keepattributes *Annotation*, InnerClasses
-dontnote kotlinx.serialization.**
-keepclassmembers class dev.shellwright.shell.config.** {
    *** Companion;
}
-keepclasseswithmembers class dev.shellwright.shell.config.** {
    kotlinx.serialization.KSerializer serializer(...);
}

# The WebView calls annotated bridge methods reflectively. Nothing is exposed
# yet — the bridge lands in Sprint 09 — but the rule belongs with the WebView
# configuration rather than being remembered later.
-keepclassmembers class * {
    @android.webkit.JavascriptInterface <methods>;
}

# Keep the stack traces in crash reports readable.
-keepattributes SourceFile,LineNumberTable
-renamesourcefileattribute SourceFile
