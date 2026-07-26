#!/bin/sh
# Regenerates the consumer R8/ProGuard keep-rules every package ships:
# src/DatadogNet.<Name>.Android/buildTransitive/DatadogNet.<Name>.Android.pro, one per row in
# build/packages.tsv. Run it after bumping DatadogNativeVersion; CI re-runs it and fails on a
# diff, so a stale committed file cannot ship.
#
# Why the rules exist at all - two facts, both checked against the .NET Android SDK's targets:
#
#   1. .NET for Android never feeds an .aar's embedded proguard.txt to R8. That file is the
#      Maven ecosystem's "consumer rules" channel - Gradle extracts and applies it - but
#      _CalculateProguardConfigurationFiles assembles only the SDK's own defaults, the generated
#      Xamarin config and @(ProguardConfiguration). Upstream's rules are silently lost, so this
#      script recovers them from the .aar and ships them verbatim.
#
#   2. A binding's Java side is reached from C# through JNI lookups by name, which R8 cannot
#      see. A class only C# constructs - and every Datadog entry point is exactly that - looks
#      dead to a shrinker, is removed from a shrunk Release build, and then fails at runtime
#      with ClassNotFoundException from a package that is, in fact, correctly installed. The
#      curated sections below keep each module's JNI-only entry surface; everything deeper
#      survives through ordinary reference reachability from there.
#
# The .aars are downloaded from Maven Central and verified against build/maven-checksums.txt
# BEFORE anything is read out of them, so this path sits behind the same supply-chain pins as
# the build itself. As a typo guard, every curated '-keep class X' without a wildcard is also
# checked to exist in the .aar it is about.
set -eu

root="$(cd "$(dirname "$0")/.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

version=$(sed -n 's:.*<DatadogNativeVersion>\(.*\)</DatadogNativeVersion>.*:\1:p' "$root/Directory.Build.props" | head -1)
if [ -z "$version" ]; then
    echo "error: could not read DatadogNativeVersion from Directory.Build.props" >&2
    exit 1
fi

# The curated JNI-only entry surface, per package. Derived from what C# actually reaches: the
# projects' Additions/, the README's documented API, and the device tests' smoke surface. Three
# kinds of entry belong here, all invisible to R8 because the only call sites are C#:
#
#   - classes C# constructs or calls statically (Datadog, Rum, the *Configuration.Builder types);
#   - interfaces whose members C# invokes on returned instances (RumMonitor, DatadogSpan) - a
#     kept interface method also keeps its implementations on live classes, which is what makes
#     the returned object usable;
#   - enums whose constants C# reads as static fields (TrackingConsent, RumActionType) - a type
#     that survives only as a method signature loses its fields.
#
# Keep it tight: one '-keep class X { *; }' per entry point, with 'X$*' only where a nested type
# is itself part of the called surface (builders, factories) or carries Kotlin DefaultImpls.
curated() {
    case "$1" in
    Internal) cat <<'EOF'
# dd-sdk-android-internal has no C#-facing entry point of its own - it is the foundation the
# other modules call from Kotlin, and their keeps carry it. EvictingQueue is pinned anyway: the
# device tests resolve it by name as proof this module's .aar reached the app, and nothing else
# guarantees a shrinker keeps any recognisable class of it alive.
-keep class com.datadog.android.internal.collections.EvictingQueue { *; }
EOF
    ;;
    Core) cat <<'EOF'
# The SDK's front door: everything an app touches before and around Datadog.initialize.
-keep class com.datadog.android.Datadog { *; }
-keep class com.datadog.android.DatadogSite { *; }
-keep class com.datadog.android.privacy.TrackingConsent { *; }
-keep class com.datadog.android.core.configuration.Configuration { *; }
-keep class com.datadog.android.core.configuration.Configuration$* { *; }
-keep class com.datadog.android.core.configuration.BatchSize { *; }
-keep class com.datadog.android.core.configuration.UploadFrequency { *; }
EOF
    ;;
    Logs) cat <<'EOF'
-keep class com.datadog.android.log.Logs { *; }
-keep class com.datadog.android.log.LogsConfiguration { *; }
-keep class com.datadog.android.log.LogsConfiguration$* { *; }
-keep class com.datadog.android.log.Logger { *; }
-keep class com.datadog.android.log.Logger$* { *; }
EOF
    ;;
    TraceApi) cat <<'EOF'
# All interfaces: C# holds the tracer, span, scope and propagation objects the trace module
# hands out and invokes their members through JNI, so the interface methods must survive - a
# kept interface method is what keeps its implementation alive on the concrete classes.
-keep class com.datadog.android.trace.api.tracer.DatadogTracer { *; }
-keep class com.datadog.android.trace.api.tracer.DatadogTracer$* { *; }
-keep class com.datadog.android.trace.api.tracer.DatadogTracerBuilder { *; }
-keep class com.datadog.android.trace.api.span.DatadogSpan { *; }
-keep class com.datadog.android.trace.api.span.DatadogSpanBuilder { *; }
-keep class com.datadog.android.trace.api.span.DatadogSpanContext { *; }
-keep class com.datadog.android.trace.api.scope.DatadogScope { *; }
-keep class com.datadog.android.trace.api.propagation.DatadogPropagation { *; }
-keep class com.datadog.android.trace.api.trace.DatadogTraceId { *; }
-keep class com.datadog.android.trace.api.trace.DatadogTraceId$* { *; }
EOF
    ;;
    TraceInternal) cat <<'EOF'
# (none) This module is the vendored dd-trace-java engine under com.datadog.trace.**; nothing in
# it is called from C# - the package ships the .aar without binding it - and its classes survive
# through reachability from dd-sdk-android-trace's kept entry points, which construct the engine.
EOF
    ;;
    Trace) cat <<'EOF'
-keep class com.datadog.android.trace.Trace { *; }
-keep class com.datadog.android.trace.TraceConfiguration { *; }
-keep class com.datadog.android.trace.TraceConfiguration$* { *; }
-keep class com.datadog.android.trace.DatadogTracing { *; }
-keep class com.datadog.android.trace.GlobalDatadogTracer { *; }
EOF
    ;;
    RUM) cat <<'EOF'
# RumMonitor is the interface behind GlobalRumMonitor.get(); C# drives every view, action,
# resource and error through it. The view-tracking strategies are constructed by the app in
# RumConfiguration.Builder.useViewTrackingStrategy and by nothing on the Java side.
-keep class com.datadog.android.rum.Rum { *; }
-keep class com.datadog.android.rum.GlobalRumMonitor { *; }
-keep class com.datadog.android.rum.RumMonitor { *; }
-keep class com.datadog.android.rum.RumMonitor$* { *; }
-keep class com.datadog.android.rum.RumConfiguration { *; }
-keep class com.datadog.android.rum.RumConfiguration$* { *; }
-keep class com.datadog.android.rum.RumActionType { *; }
-keep class com.datadog.android.rum.RumErrorSource { *; }
-keep class com.datadog.android.rum.RumResourceKind { *; }
-keep class com.datadog.android.rum.RumResourceMethod { *; }
-keep class com.datadog.android.rum.tracking.ActivityViewTrackingStrategy { *; }
-keep class com.datadog.android.rum.tracking.FragmentViewTrackingStrategy { *; }
-keep class com.datadog.android.rum.tracking.MixedViewTrackingStrategy { *; }
-keep class com.datadog.android.rum.tracking.NavigationViewTrackingStrategy { *; }
EOF
    ;;
    SessionReplay) cat <<'EOF'
# The privacy types are enums whose constants C# reads as static fields while building the
# configuration; a class kept only through a method signature loses them.
-keep class com.datadog.android.sessionreplay.SessionReplay { *; }
-keep class com.datadog.android.sessionreplay.SessionReplayConfiguration { *; }
-keep class com.datadog.android.sessionreplay.SessionReplayConfiguration$* { *; }
-keep class com.datadog.android.sessionreplay.SystemRequirementsConfiguration { *; }
-keep class com.datadog.android.sessionreplay.SystemRequirementsConfiguration$* { *; }
-keep class com.datadog.android.sessionreplay.SessionReplayPrivacy { *; }
-keep class com.datadog.android.sessionreplay.TextAndInputPrivacy { *; }
-keep class com.datadog.android.sessionreplay.ImagePrivacy { *; }
-keep class com.datadog.android.sessionreplay.TouchPrivacy { *; }
EOF
    ;;
    SessionReplayMaterial) cat <<'EOF'
# Constructed by the app for SessionReplayConfiguration.Builder.addExtensionSupport and by
# nothing on the Java side; the mappers it registers survive through it.
-keep class com.datadog.android.sessionreplay.material.MaterialExtensionSupport { *; }
EOF
    ;;
    SessionReplayCompose) cat <<'EOF'
# Constructed by the app for SessionReplayConfiguration.Builder.addExtensionSupport and by
# nothing on the Java side; the mappers it registers survive through it.
-keep class com.datadog.android.sessionreplay.compose.ComposeExtensionSupport { *; }
EOF
    ;;
    Ndk) cat <<'EOF'
# NdkCrashReports is called from .NET through JNI only, which a Java shrinker cannot see, so
# without these it is removed from shrunk Release builds and Enable throws ClassNotFoundException
# at runtime. Keeping the entry type and its nested types is enough: everything they use survives
# through ordinary reference reachability from there - including the feature class whose native
# methods the crash library binds, which the SDK's default proguard-android.txt keeps by the
# global 'native <methods>' rule once the class itself is live.
-keep class com.datadog.android.ndk.NdkCrashReports { *; }
-keep class com.datadog.android.ndk.NdkCrashReports$* { *; }
EOF
    ;;
    WebView) cat <<'EOF'
# WebViewTracking is called from .NET through JNI only, which a Java shrinker cannot see, so
# without these it is removed from shrunk Release builds and Enable throws ClassNotFoundException
# at runtime. Keeping the entry type and its nested types is enough: everything they use survives
# through ordinary reference reachability from there.
-keep class com.datadog.android.webview.WebViewTracking { *; }
-keep class com.datadog.android.webview.WebViewTracking$* { *; }
EOF
    ;;
    OkHttp) cat <<'EOF'
# The interceptors and the event-listener factory are constructed by the app and handed to an
# OkHttpClient.Builder; nothing on the Java side ever news them up.
-keep class com.datadog.android.okhttp.DatadogInterceptor { *; }
-keep class com.datadog.android.okhttp.DatadogInterceptor$* { *; }
-keep class com.datadog.android.okhttp.DatadogEventListener { *; }
-keep class com.datadog.android.okhttp.DatadogEventListener$* { *; }
-keep class com.datadog.android.okhttp.trace.TracingInterceptor { *; }
-keep class com.datadog.android.okhttp.trace.TracingInterceptor$* { *; }
EOF
    ;;
    *)
        echo "error: no curated keep-rules for package '$1' - add a case to build/generate-r8-rules.sh" >&2
        exit 1
    ;;
    esac
}

generated=0
while IFS="$(printf '\t')" read -r name artifact _rest; do
    case "$name" in ''|\#*) continue ;; esac

    file="$artifact-$version.aar"
    pin=$(awk -v f="$file" '$1 == f { print $2; exit }' "$root/build/maven-checksums.txt")
    if [ -z "$pin" ]; then
        echo "error: no SHA-256 pin for $file in build/maven-checksums.txt - run build/UpdateMavenChecksums.sh first" >&2
        exit 1
    fi

    curl -fsSL -o "$work/$file" "https://repo1.maven.org/maven2/com/datadoghq/$artifact/$version/$file"
    hash=$(shasum -a 256 "$work/$file" | cut -d' ' -f1)
    if [ "$hash" != "$pin" ]; then
        echo "error: $file hashes to $hash but build/maven-checksums.txt pins $pin - refusing to read rules out of it" >&2
        exit 1
    fi

    # A missing proguard.txt is normal - six of the thirteen modules ship none - so distinguish
    # "absent" from "unzip failed" instead of swallowing both.
    upstream=""
    if unzip -l "$work/$file" proguard.txt >/dev/null 2>&1; then
        upstream=$(unzip -p "$work/$file" proguard.txt)
    fi

    out="$root/src/DatadogNet.$name.Android/buildTransitive/DatadogNet.$name.Android.pro"
    mkdir -p "$(dirname "$out")"

    {
        echo "# Generated by build/generate-r8-rules.sh from $artifact $version. Do not edit by hand."
        echo "#"
        echo "# Consumer rules for R8/ProGuard, attached to consuming applications by"
        echo "# DatadogNet.$name.Android.targets. Two sections: upstream's own consumer rules, recovered"
        echo "# from the .aar because .NET for Android never feeds an embedded proguard.txt to R8, and"
        echo "# curated keeps for the entry surface C# reaches through JNI alone - reflection a Java"
        echo "# shrinker cannot see, so without them a shrunk Release build removes classes and the"
        echo "# binding fails at runtime with ClassNotFoundException."
        echo
        echo "# ---- Upstream consumer rules, verbatim from $file!proguard.txt ----"
        if [ -n "$upstream" ]; then
            printf '%s\n' "$upstream"
        else
            echo "# (none: $artifact $version embeds no proguard.txt)"
        fi
        echo
        echo "# ---- Curated keeps for the JNI-only entry surface ----"
        curated "$name"
    } > "$out"

    # Typo guard: every curated '-keep class X' whose name has no wildcard must exist in the
    # .aar. Checked against the curated section only - upstream's rules name classes from other
    # modules and from optional integrations, deliberately.
    unzip -l "$work/$file" classes.jar >/dev/null 2>&1 || { echo "error: $file has no classes.jar" >&2; exit 1; }
    unzip -p "$work/$file" classes.jar > "$work/classes.jar"
    unzip -l "$work/classes.jar" > "$work/classes.txt"
    curated "$name" | sed -n 's/^-keep class \([^ ]*\) .*/\1/p' | while read -r class; do
        case "$class" in *\**) continue ;; esac
        path="$(printf '%s' "$class" | tr '.' '/').class"
        if ! grep -qF " $path" "$work/classes.txt"; then
            echo "error: curated rule keeps $class, but $file has no $path - fix the case block in build/generate-r8-rules.sh" >&2
            exit 1
        fi
    done

    rules=$(grep -c '^-' "$out" || true)
    echo "==> wrote $(printf '%s' "$out" | sed "s|^$root/||") ($rules rules)"
    generated=$((generated + 1))
done < "$root/build/packages.tsv"

echo "==> generated keep-rules for $generated packages against dd-sdk-android $version"
