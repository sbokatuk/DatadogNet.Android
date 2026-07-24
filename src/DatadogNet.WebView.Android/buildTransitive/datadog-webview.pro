# Consumer rules for R8/ProGuard, attached by DatadogNet.WebView.Android.targets.
#
# WebViewTracking is called from .NET through JNI only, which a Java shrinker cannot see, so
# without these it is removed from shrunk Release builds and Enable throws ClassNotFoundException
# at runtime. Keeping the entry type and its nested types is enough: everything they use survives
# through ordinary reference reachability from there.
-keep class com.datadog.android.webview.WebViewTracking { *; }
-keep class com.datadog.android.webview.WebViewTracking$* { *; }
