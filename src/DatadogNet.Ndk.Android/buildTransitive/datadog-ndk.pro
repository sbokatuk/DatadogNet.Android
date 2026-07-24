# Consumer rules for R8/ProGuard, attached by DatadogNet.Ndk.Android.targets.
#
# NdkCrashReports is called from .NET through JNI only, which a Java shrinker cannot see, so
# without these it is removed from shrunk Release builds and Enable throws ClassNotFoundException
# at runtime. Keeping the entry type and its nested types is enough: everything they use survives
# through ordinary reference reachability from there - including the JNI handlers the native
# crash library calls back into, which the module's own embedded rules cover.
-keep class com.datadog.android.ndk.NdkCrashReports { *; }
-keep class com.datadog.android.ndk.NdkCrashReports$* { *; }
