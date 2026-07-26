# DatadogNet.Android

[![NuGet](https://img.shields.io/nuget/v/DatadogNet.RUM.Android?label=nuget)](https://www.nuget.org/packages/DatadogNet.RUM.Android)
[![release](https://github.com/sbokatuk/DatadogNet.Android/actions/workflows/release.yml/badge.svg)](https://github.com/sbokatuk/DatadogNet.Android/actions/workflows/release.yml)
[![Targets: net8.0 | net9.0 | net10.0](https://img.shields.io/badge/targets-net8.0%20%7C%20net9.0%20%7C%20net10.0-512BD4)](#packages)
[![dd-sdk-android 3.12.1](https://img.shields.io/badge/dd--sdk--android-3.12.1-632CA6)](https://github.com/DataDog/dd-sdk-android/releases/tag/3.12.1)
[![Licence: MIT AND Apache-2.0](https://img.shields.io/badge/licence-MIT%20AND%20Apache--2.0-green)](#licence)

.NET for Android and .NET MAUI bindings for the native
[Datadog Android SDK (`dd-sdk-android`)](https://github.com/DataDog/dd-sdk-android).

Enable Datadog Real User Monitoring, Logs, Trace, Session Replay, WebView tracking and native crash
reporting from C#, in a `net8.0-android`, `net9.0-android` or `net10.0-android` app.

```bash
dotnet add package DatadogNet.RUM.Android
```

```csharp
using Com.Datadog.Android;
using Com.Datadog.Android.Core.Configuration;
using Com.Datadog.Android.Privacy;
using Com.Datadog.Android.Rum;

var configuration = new Configuration.Builder(
        clientToken: "<CLIENT_TOKEN>", env: "production", variant: "", service: "my-app")
    .UseSite(DatadogSite.Us1)
    .Build();

Datadog.Initialize(context, configuration, TrackingConsent.Granted);
Rum.Enable(new RumConfiguration.Builder("<RUM_APPLICATION_ID>").Build());
```

---

## Contents

- [Packages](#packages)
- [Installing](#installing)
- [Usage](#usage)
- [Convenience API](#convenience-api)
- [How this repository works](#how-this-repository-works)
- [Building locally](#building-locally)
- [Upgrading the Datadog SDK](#upgrading-the-datadog-sdk)
- [Releasing](#releasing)
- [Troubleshooting](#troubleshooting)
- [Licence](#licence)

---

## Packages

Thirteen packages, one per `dd-sdk-android` module. Versions are
`<dd-sdk-android version>.<binding revision>` — `3.12.1.1` is dd-sdk-android **3.12.1**, binding
revision **1**. The fourth component belongs to this repository and advances when the bindings or
packaging change while the native artifacts stay put.

| Package | Wraps | Depends on | What it is for |
| --- | --- | --- | --- |
| **`DatadogNet.RUM.Android`** | `dd-sdk-android-rum` | Core, Internal | **The usual starting point.** Real User Monitoring — views, actions, resources, errors, long tasks and mobile vitals. |
| `DatadogNet.Core.Android` | `dd-sdk-android-core` | Internal | SDK initialization, configuration, consent, user and account info. Every feature needs it. |
| `DatadogNet.Internal.Android` | `dd-sdk-android-internal` | — | Shared foundation every other module is built on. |
| `DatadogNet.Logs.Android` | `dd-sdk-android-logs` | Core, Internal | Log collection. |
| `DatadogNet.Trace.Android` | `dd-sdk-android-trace` | Core, Internal, TraceApi, TraceInternal | Distributed tracing (APM). |
| `DatadogNet.TraceApi.Android` | `dd-sdk-android-trace-api` | Core, Internal | The tracing contract — `IDatadogTracer`, `IDatadogSpan`, `IDatadogScope`. |
| `DatadogNet.TraceInternal.Android` | `dd-sdk-android-trace-internal` | Core, Internal, TraceApi | The tracing engine. Ships the module; binds nothing. |
| `DatadogNet.SessionReplay.Android` | `dd-sdk-android-session-replay` | Core, Internal | Session Replay. Requires RUM. |
| `DatadogNet.SessionReplayMaterial.Android` | `dd-sdk-android-session-replay-material` | SessionReplay | Records Material Components faithfully. |
| `DatadogNet.SessionReplayCompose.Android` | `dd-sdk-android-session-replay-compose` | SessionReplay, Internal | Records Jetpack Compose content. **net9/net10 only.** |
| `DatadogNet.Ndk.Android` | `dd-sdk-android-ndk` | Core, Internal | Native (NDK) crash capture. Opt-in. |
| `DatadogNet.WebView.Android` | `dd-sdk-android-webview` | Core, Internal | Bridges RUM/Logs out of a `WebView`. Opt-in. |
| `DatadogNet.OkHttp.Android` | `dd-sdk-android-okhttp` | Internal, RUM, Trace | Reports outgoing HTTP calls as RUM resources and propagates tracing headers. |

Most apps need one or two lines:

```xml
<PackageReference Include="DatadogNet.RUM.Android" Version="3.12.1.4" />
<PackageReference Include="DatadogNet.Logs.Android" Version="3.12.1.4" />
```

`DatadogNet.Core.Android` arrives transitively — you rarely reference it directly, though you will
`using Com.Datadog.Android;` to reach `Datadog.Initialize`.

> **There is no façade package.** The iOS bindings have `DatadogNet.Objc.iOS`, which re-exports the
> whole SDK, because `dd-sdk-ios` ships a `DatadogObjc` framework that does exactly that.
> `dd-sdk-android` has no equivalent, and inventing one would mean every app paying for Session
> Replay and NDK crash reporting to use RUM. Reference the features you actually enable.

**Target frameworks**: `net8.0-android34.0`, `net9.0-android35.0`, `net10.0-android36.0` — except
`DatadogNet.SessionReplayCompose.Android`, which is net9/net10 only. Compose's last net8 build pins
an AndroidX `SavedState` old enough to collide with what a MAUI app resolves, and holding the whole
repository back to match would have broken MAUI consumers of all thirteen packages to keep net8 for
the one that needs Compose.

**Minimum API level**: **23** (Android 6.0) — dd-sdk-android 3.x raised its own floor from 21.

> **net8 sunset.** The net8 head is already past its platform support window — the net8 mobile
> workloads left support with MAUI 8 on 14 May 2025 — and ships for the apps that still target it,
> with the emulator checks run against it. It is also what holds the whole AndroidX/Kotlin
> dependency set below current (see `Directory.Build.props`). So the trade-off does not persist by
> inertia: **the net8 head is dropped, and the held-back versions unpinned, in the first release
> after .NET 8 itself leaves support on 10 November 2026.**

**MAUI version**: build with **MAUI 10**. See [Building locally](#building-locally).

---

## Installing

```xml
<ItemGroup>
  <PackageReference Include="DatadogNet.RUM.Android" Version="3.12.1.4" />
</ItemGroup>
```

```xml
<SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'">23</SupportedOSPlatformVersion>
```

The packages are Android-only. In a multi-targeted MAUI project, guard the reference so an iOS or
Windows head does not try to restore them:

```xml
<ItemGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'">
  <PackageReference Include="DatadogNet.RUM.Android" Version="3.12.1.4" />
</ItemGroup>
```

---

## Usage

The C# names are the Kotlin/Java ones projected into C#, so `com.datadog.android.rum.Rum` becomes
`Com.Datadog.Android.Rum.Rum` and `enable(...)` becomes `Enable(...)`.

[`samples/DatadogNet.Android.Example`](samples/DatadogNet.Android.Example) is a working MAUI app
that does all of the below; [`Datadog.cs`](samples/DatadogNet.Android.Example/Datadog.cs) is the
setup in one file.

### Initialize

Once, as early as possible. In MAUI that means `MauiProgram.CreateMauiApp`, or better still your
`Application` subclass — crash reporting only covers what happens after it is enabled, and startup
crashes are the ones worth catching.

```csharp
using Com.Datadog.Android;
using Com.Datadog.Android.Core.Configuration;
using Com.Datadog.Android.Privacy;

var configuration = new Configuration.Builder(
        clientToken: "<CLIENT_TOKEN>", env: "production", variant: "", service: "my-app")
    .UseSite(DatadogSite.Us1)          // Us1, Us3, Us5, Eu1, Ap1, Ap2, Uk1, Us1Fed, Us2Fed
    .SetBatchSize(BatchSize.Medium)
    .SetUploadFrequency(UploadFrequency.Average)
    .Build();

Datadog.Initialize(context, configuration, TrackingConsent.Granted);
Datadog.SetVerbosity((int)Android.Util.LogPriority.Warn);   // SDK problems to logcat
```

`Configuration.Builder` takes all four arguments — Kotlin's defaults for `variant` and `service`
do not survive into C#, so pass `""` and your service name explicitly.

### RUM

```csharp
using Com.Datadog.Android.Rum;
using Com.Datadog.Android.Rum.Tracking;

var rum = new RumConfiguration.Builder("<RUM_APPLICATION_ID>")
    .SetSessionSampleRate(100f)
    .TrackUserInteractions()
    .UseViewTrackingStrategy(new ActivityViewTrackingStrategy(true))
    .Build();

Rum.Enable(rum);
```

> **`TrackUserInteractions()`, not `TrackInteractions()`.** Datadog's own documentation still shows
> the latter; it does not exist in 3.x.

MAUI renders every page into a single `Activity`, so `ActivityViewTrackingStrategy` reports one
view for the whole app. Report pages yourself instead:

```csharp
var monitor = GlobalRumMonitor.Get();

using (monitor.StartView("checkout", "Checkout"))
{
    monitor.AddAction(RumActionType.Tap, "pay", new Dictionary<string, object?>
    {
        ["cart.total"] = 42.50m,
    });

    try { /* ... */ }
    catch (Exception exception) { monitor.AddError(exception); }
}
```

`StartView`, `AddAction` and `AddError` above are from the [convenience API](#convenience-api).

### Logs

```csharp
using Com.Datadog.Android.Log;

Logs.Enable(new LogsConfiguration.Builder().Build());

var logger = new Logger.Builder()
    .SetName("app")
    .SetBundleWithRumEnabled(true)
    .Build();

logger.Log(LogLevel.Info, "user signed in");
logger.Log(LogLevel.Error, "payment failed", exception, new Dictionary<string, object?>
{
    ["order.id"] = orderId,
});
```

### Trace

```csharp
using Com.Datadog.Android.Trace;

Trace.Enable(new TraceConfiguration.Builder().Build());

GlobalDatadogTracer.RegisterIfAbsent(DatadogTracing.NewTracerBuilder().Build());
```

> **`AndroidTracer` is gone.** dd-sdk-android 3.0 removed it along with the OpenTracing dependency.
> `DatadogTracing.NewTracerBuilder()` is the replacement. Note also that `GlobalDatadogTracer.Get`
> and `RegisterIfAbsent` are static while `Clear` and `GetOrNull` are not — the latter are
> `GlobalDatadogTracer.Instance.Clear()`.

### Session Replay

```csharp
using Com.Datadog.Android.Sessionreplay;

SessionReplay.Enable(new SessionReplayConfiguration.Builder(100f)
    .SetTextAndInputPrivacy(TextAndInputPrivacy.MaskAll)
    .SetImagePrivacy(ImagePrivacy.MaskAll)
    .SetTouchPrivacy(TouchPrivacy.Hide)
    .Build());
```

Requires RUM. The three privacy levels decide what is redacted *on the device*, before anything is
uploaded. Loosen them deliberately. The older single `SessionReplayPrivacy` is deprecated upstream
and still bound, but prefer the three.

> `Enable` is static; **`StartRecording` and `StopRecording` are not** — they are
> `SessionReplay.Instance.StartRecording(sdkCore)`. That asymmetry is upstream's, not the
> binding's.

Add `DatadogNet.SessionReplayMaterial.Android` or `DatadogNet.SessionReplayCompose.Android` and
register the extension when your app draws Material or Compose content:

```csharp
.AddExtensionSupport(new MaterialExtensionSupport())
```

### Native crash reporting

Add `DatadogNet.Ndk.Android`, then enable it **after** initializing the SDK:

```csharp
using Com.Datadog.Android.Ndk;

NdkCrashReports.Enable();
```

Crashes are reported on the next launch, as RUM errors and logs. Upload your native symbols to
Datadog for symbolication. Like `DatadogNet.WebView.Android`, the package ships consumer R8
keep-rules for its JNI-only entry point, so Release shrinking cannot strip `NdkCrashReports`.

### Network instrumentation

Add `DatadogNet.OkHttp.Android`. This is what makes HTTP calls visible in RUM:

```csharp
using Com.Datadog.Android.Okhttp;

var client = new OkHttpClient.Builder()
    .AddInterceptor(new DatadogInterceptor.Builder(hosts).Build())
    .EventListenerFactory(new DatadogEventListener.Factory())
    .Build();
```

`HttpClient` in .NET does **not** route through OkHttp unless you configure
`AndroidMessageHandler`, so a MAUI app using `HttpClient` will not report resources automatically.

### WebView tracking

```csharp
using Com.Datadog.Android.Webview;

WebViewTracking.Enable(webView, new List<string> { "example.com" });
```

The package ships consumer R8/ProGuard keep-rules (under `buildTransitive/`), because
`WebViewTracking` is reached from .NET through JNI alone and a shrunk Release build would
otherwise remove it and throw `ClassNotFoundException` at runtime. Nothing to configure — NuGet
imports them into the consuming app automatically.

### User info and consent

```csharp
Datadog.SetUserInfo(userId: "user-123", name: "Ada Lovelace", email: "ada@example.com");
Datadog.SetTrackingConsent(TrackingConsent.Pending);
```

`Pending` collects events but holds them on the device until consent is granted or refused — which
is what a prompt-on-first-launch flow wants.

---

## Convenience API

The generated binding is a faithful projection of Kotlin, which makes some things clumsy in C#.
Each package adds a small hand-written layer over it. The generated members are all still there;
nothing is hidden or renamed.

| Instead of | Write |
| --- | --- |
| `monitor.StartView("k", "Name", new Dictionary<string, Java.Lang.Object>())` … `StopView` | `using (monitor.StartView("k", "Name"))` |
| hand-wrapping every value in `Java.Lang.Object` | `new Dictionary<string, object?> { ["k"] = 42 }` on any overload below |
| `monitor.AddAction(type, name, javaMap)` | `monitor.AddAction(type, name, attributes?)` |
| `monitor.AddError(message, source, throwable, javaMap)` with a `Throwable` you do not have | `monitor.AddError(exception)` |
| `logger.Log(priority, message, throwable, javaMap)` | `logger.Log(level, message, exception?, attributes?)` |

The view scope is the one worth adopting everywhere: the raw API is a `StartView` / `StopView` pair
matched by key, and a view left open by an early return or an exception captures every later action
and error in the session.

Attribute values may be strings, any numeric type, `bool`, `DateTime`, `DateTimeOffset`, `Guid`,
enums, `Java.Lang.Object`s, arrays and nested dictionaries. Anything else throws `ArgumentException`
rather than being silently dropped.

> **IntelliSense on the generated tier is thin — wired, but limited by Kotlin.** Every binding
> project now feeds upstream's `-sources.jar` to the binding generator (`@(JavaSourceJar)`,
> downloaded and SHA-256-pinned like every other artifact), which imports API documentation into
> the generated XML docs. The importer parses *Java* sources, though, and dd-sdk-android's
> sources jars are almost entirely `.kt` — so it extracts what it can, and most of the raw
> surface stays bare. The convenience layer above is fully documented; for the raw surface the
> C# names map 1:1 onto the Kotlin ones, so upstream's own reference is the documentation.

---

## What is not bound

Three groups of members are deliberately absent, each for a reason recorded next to the rule that
removes it in the relevant `Transforms/Metadata.xml`:

- **Session Replay wireframe mappers** (`...recorder.mapper`, 28 types). The hierarchy is generic
  and its single member erases to `map(Object, …)`, which the generator cannot bind. The types
  still ship in the `.aar` and still run, so recording is unaffected — including through the
  Material and Compose extensions. What you cannot do is author a custom mapper in C#.
- **`dd-sdk-android-trace-internal`'s entire surface.** It is the dd-trace-java engine vendored
  under `com.datadog.trace.**`; the tracing API you call is `com.datadog.android.trace.*`, in
  `Trace` and `TraceApi`. The package ships the module without binding it.
- **A handful of internal types** — `EvictingQueue`, `SessionRebasedSampler`,
  `OkHttpRequestInfoBuilder` — plus the `ExecutorService` members of `FlushableExecutorService` and
  the abstract `build()` on `TracingInterceptor.BaseBuilder`. All are generator failures on erased
  generics or covariant returns, and none is reachable from a documented API.

**Kotlin constructs that do not project into C# at all** are a property of the language boundary,
not of these bindings: extension functions (Session Replay's `View.setSessionReplayHidden`, the RUM
resource helpers, everything in the coroutine and Rx integrations) land on synthetic `…Kt` classes,
and `inline` functions taking lambdas are not callable. Default parameter values do not exist in
C#; where upstream marked a member `@JvmOverloads` you get the arity ladder, and where it did not,
you must pass every argument.

---

## How this repository works

```
Maven Central: com.datadoghq:dd-sdk-android-*:3.12.1
        │  @(AndroidMavenLibrary) — resolves the .aar and its .pom, cached under
        │  ~/.cache/dotnet-android/MavenCacheDirectory
        ▼
   src/DatadogNet.<Package>.Android/ — binding project + committed Transforms/Metadata.xml
        │  build/BuildNugets.sh — pack twice (net9 band, net10 band), then merge
        ▼
   artifacts/*.nupkg  ──►  nuget.org
```

Nothing is committed and nothing is compiled from source. There is no fetch script: the .NET
Android SDK resolves each module from Maven Central, and — because it reads the `.pom` too — fails
the build when an `.aar` links against Java types that no referenced package provides. That check
is why this repository has no `libs/` directory and why a missing Java dependency is a build error
here rather than a `NoClassDefFoundError` in somebody's app.

That check is about *completeness*; **authenticity is pinned separately.** Every artifact this
repository resolves has a SHA-256 recorded in [`build/maven-checksums.txt`](build/maven-checksums.txt),
and the `VerifyDatadogMavenArtifactHashes` target checks the bytes actually resolved — on both the
`@(AndroidMavenLibrary)` path and the direct-download fallback — against it on every build. A hash
that changes for an unchanged version fails the build instead of shipping;
[`build/UpdateMavenChecksums.sh`](build/UpdateMavenChecksums.sh) regenerates the pins after a
version bump.

**Why the two-pass build.** Each .NET SDK's Android workload ships reference packs for the current
target framework and the previous one — the .NET 9 band builds net8 + net9, the .NET 10 band builds
net9 + net10. No single SDK builds all three, so `BuildNugets.sh` packs twice and
[`merge-packages.py`](build/merge-packages.py) grafts the net10 assets into the net9 packages,
carrying the dependency group across with them.

**Four Java dependencies have no .NET binding** and are embedded as plain Java rather than left to
fail at runtime: Kronos (`Core`), JCTools and RE2/J (`TraceApi`), and `androidx.metrics`
(`RUM`). Each is embedded in exactly one package — the same classes contributed twice would be
dexed twice and fail the consuming app's build.

### Layout

| Path | What |
| --- | --- |
| `src/DatadogNet.*.Android/` | One binding project per module. `Transforms/Metadata.xml` is committed and explains every rule. |
| `src/Datadog.Binding.props` | Everything the thirteen projects share, so each `.csproj` is its identity and its dependencies. |
| `src/DatadogNet.*/Additions/` | The hand-written [convenience API](#convenience-api). |
| `build/packages.tsv` | The package set. The build script, both workflows and the tests all read it. |
| `build/` | Pack and merge scripts. |
| `tests/DatadogNet.Android.PackageTests/` | Asserts the shape of the packed `.nupkg`s. |
| `tests/DatadogNet.Android.DeviceTests/` | An Android app that consumes the **packed packages** and drives the real SDK on an emulator. |
| `samples/DatadogNet.Android.Example/` | A MAUI app doing the same, the way an app would. |

The tests consume the packed `.nupkg`s from `artifacts/` rather than referencing the projects, so
they exercise what actually gets published — including the inter-package dependencies, which a
`ProjectReference` would bypass entirely.

`DatadogNet.sln` deliberately contains the binding projects and the tests but **not** the sample,
so `dotnet build DatadogNet.sln` does not require the MAUI workload.

---

## Building locally

Requires the .NET 9 and .NET 10 SDKs with the `android` workload, a JDK, and the Android SDK with
platforms 34, 35 and 36.

```bash
./build/BuildNugets.sh          # packs all thirteen into artifacts/
dotnet test tests/DatadogNet.Android.PackageTests
```

Run the on-emulator smoke tests against the packed packages, with an emulator already booted:

```bash
./.github/scripts/run-emulator-tests.sh 3.12.1.4 net9.0-android35.0
```

Build and run the sample:

```bash
cd /tmp && dotnet new globaljson --sdk-version 10.0.301 --force
dotnet build <repo>/samples/DatadogNet.Android.Example/DatadogNet.Android.Example.csproj -t:Run
```

> The sample targets `net10.0-android36.0` and needs the **.NET 10 SDK**, which this repository's
> `global.json` does not select — hence the scratch directory. That is a constraint on the MAUI
> version you build the sample with, not on what the packages support: MAUI 9 cannot build against
> the AndroidX generation these packages depend on, and it is an SDK defect rather than anything
> the bindings do. A plain MAUI 9 app with nothing but `Xamarin.AndroidX.AppCompat 1.7.1.1` added
> fails the same way, with no Datadog package present.


---

## Upgrading the Datadog SDK

One script runs every mechanical step, in order, stopping at the first failure:

```bash
./build/BumpNativeVersion.sh 3.13.0
```

It bumps `DatadogNativeVersion` and resets `DatadogBindingRevision` to `1` in
[`Directory.Build.props`](Directory.Build.props); re-pins every artifact's SHA-256
([`UpdateMavenChecksums.sh`](build/UpdateMavenChecksums.sh), which PGP-verifies the Datadog
downloads when `gpg` is present); regenerates the consumer R8 keep-rules from the new `.aar`s
([`generate-r8-rules.sh`](build/generate-r8-rules.sh)); re-checks every module's declared
dependencies against its Gradle metadata
([`verify-transitive-deps.py`](build/verify-transitive-deps.py)); rewrites this README's pinned
versions; and scaffolds `docs/release-notes/<version>.md`. What remains is judgement:

1. Review the diffs. In `build/maven-checksums.txt`, only artifacts whose *version changed*
   should have a changed hash — anything else is the event the pins exist to catch.
2. Re-check each `Transforms/Metadata.xml`. A removal rule that no longer matches anything
   becomes a `BG8A00` warning rather than an error, so a rule that upstream has fixed will
   linger silently. Same for the `@(AndroidIgnoredJavaDependency)` block in
   [`src/Datadog.Binding.props`](src/Datadog.Binding.props): verify-transitive-deps.py warns
   about entries no current `.pom` mentions, because a stale ignore is where a real missing
   dependency can hide.
3. Review the regenerated `src/*/buildTransitive/*.pro` for upstream rule changes, and extend
   the curated keeps in `generate-r8-rules.sh` if the new line added entry points.
4. `./build/BuildNugets.sh` — every module is resolved fresh from Maven Central and verified
   against the new pins — then both test suites.

To spelunk one module's dependencies by hand, read the **Gradle module metadata**, not the
`.pom`:

```bash
curl -s https://repo1.maven.org/maven2/com/datadoghq/dd-sdk-android-core/3.13.0/dd-sdk-android-core-3.13.0.module \
  | python3 -c "import json,sys; [print(d['group']+':'+d['module'], d['version']['requires']) for v in json.load(sys.stdin)['variants'] if v['name']=='releaseVariantReleaseRuntimePublication' for d in v.get('dependencies',[])]"
```

The `.pom` is **wrong**: Gradle flattens the test-fixtures variant into it, so it lists JUnit,
Mockito, AssertJ, Elmyr, kotlin-reflect and a `dd-sdk-android.tools:unit:unspecified` that
resolves nowhere. Those are neutralised by the `@(AndroidIgnoredJavaDependency)` block, and
verify-transitive-deps.py keeps both directions honest on every CI run: a dependency upstream
adds is a named failing coordinate, an ignore entry upstream retires is a warning.

If Datadog adds or removes a module, add or remove a row in `build/packages.tsv` and the matching
project — the build script, the workflows and the package tests all follow from it.

---

## Releasing

Tag it. `v3.12.1.4` builds, tests, publishes all thirteen packages to nuget.org via trusted
publishing, and creates a GitHub release. The tag drives which native SDK is bound, so an older
line can be released by tagging it.

Pull requests publish a `-beta.<pr>.<run>` prerelease of the whole set.

Both run the same [`build.yml`](.github/workflows/build.yml): pack, package tests, and the emulator
smoke tests. Pull requests additionally compile the MAUI sample against the packed packages;
releases do not, since the commit being tagged has already been through that check and it would
otherwise put a MAUI workload install between the packages being built and being published.

Curated notes in `docs/release-notes/<version>.md` replace the generated commit list when present.

---

## Troubleshooting

**Nothing appears in Datadog.** Check `UseSite` matches your organisation's region — this is the
most common cause. Call `Datadog.SetVerbosity((int)LogPriority.Verbose)` to see the SDK's own
diagnostics in logcat.

**`NoClassDefFoundError` at runtime.** A Java dependency did not make it into the app. Usually a
partially restored package; clear the cached copies and restore again:
`rm -rf ~/.nuget/packages/datadognet.*`. If it happens **only in a shrunk build** (R8 on, i.e.
`AndroidLinkTool=r8`) and names a Datadog class, see the next entry.

**R8 / Java shrinking.** Every package ships consumer keep-rules under `buildTransitive/` —
generated by [`build/generate-r8-rules.sh`](build/generate-r8-rules.sh) and applied to your app
automatically whenever R8 runs. Two sections each: upstream's own consumer rules, recovered from
the `.aar` because .NET for Android never feeds an embedded `proguard.txt` to R8, and curated
keeps for the entry surface C# reaches through JNI alone — reflection a Java shrinker cannot see,
which would otherwise remove `Datadog`, `Rum`, the configuration builders and every other entry
point from a shrunk build. CI runs one emulator leg with R8 on, so the rules are exercised
against a real shrink on every build. If a shrunk build still reports a Datadog class missing,
add a `-keep` for anything you reach only through *your own* reflection — and open an issue,
because a class reachable from the documented API belongs in the shipped rules.

**`Duplicate class` when building the app.** Two packages are contributing the same Java classes.
The four embedded third-party libraries are each owned by exactly one package for this reason — if
you added a `PackageReference` for one of them yourself, remove it.

**`NU1107` on `Xamarin.AndroidX.SavedState` in a net8 app.** Two packages in the AndroidX graph
the bindings pull in disagree about SavedState: Navigation.Common 2.9.6 wants `>= 1.4.0`, while
SavedState.Ktx 1.3.2 — reached through Activity.Ktx — pins `[1.3.2, 1.3.3)`. The .NET 9 and
.NET 10 SDKs resolve the higher version and emit `NU1608`; the .NET 8 SDK refuses outright:

```
error NU1107: Version conflict detected for Xamarin.AndroidX.SavedState. Install/reference
Xamarin.AndroidX.SavedState 1.4.0 directly to project ... to resolve this issue.
```

Do what the error says — this is a direct reference that exists only to settle the transitive
conflict, and one every net8 consumer of these packages needs:

```xml
<PackageReference Include="Xamarin.AndroidX.SavedState" Version="1.4.0" />
```

Scope it to net8 in a multi-targeted project: net9 and net10 settle their own graphs higher, and
pinning 1.4.0 there is a downgrade that fails the other way round (`NU1605`).

**`XA4241`/`XA4242` when building this repository.** Java dependency verification found an
unsatisfied dependency. Add the matching binding `PackageReference` to the project; the error names
the Microsoft-maintained package when one exists.

**`'Trace' is an ambiguous reference`.** `Com.Datadog.Android.Trace.Trace` collides with
`Android.OS.Trace`. Alias it: `using DdTrace = Com.Datadog.Android.Trace.Trace;`.

**`'Logs' does not exist in the namespace 'DatadogNet.Logs'`** — or the same for `Trace` or
`SessionReplay`. Your app's own namespace starts with `DatadogNet.`, and C# resolves a bare name
outward through enclosing namespaces before looking at `using` directives. The bindings avoid
creating `DatadogNet.<Feature>` namespaces for exactly this reason, so if you see it, something in
*your* solution declares one. Rename it or fully qualify the call.

**RUM records one view for the whole app.** Expected in MAUI: every page renders into a single
`Activity`. Report views yourself with `monitor.StartView(...)`.

**HTTP calls do not appear as RUM resources.** `HttpClient` does not route through OkHttp by
default. Use the OkHttp client directly, or configure `AndroidMessageHandler`.

---

## Licence

The binding code in this repository is [MIT](LICENSE). The native artifacts the packages ship are
built and published by Datadog and are Apache-2.0, as are Kronos, JCTools and `androidx.metrics`;
RE2/J is under the Go licence. Every package declares `MIT AND Apache-2.0` and carries the MIT
text, the Apache-2.0 text and Datadog's `NOTICE` under `licenses/`.
