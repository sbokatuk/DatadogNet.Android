using Android.Util;
using Com.Datadog.Android;
using Com.Datadog.Android.Core.Configuration;
using Com.Datadog.Android.Log;
using Com.Datadog.Android.Ndk;
using Com.Datadog.Android.Okhttp;
using Com.Datadog.Android.Privacy;
using Com.Datadog.Android.Rum;
using Com.Datadog.Android.Rum.Tracking;
using Com.Datadog.Android.Sessionreplay;
using Com.Datadog.Android.Sessionreplay.Material;
// Com.Datadog.Android.Trace.Trace collides with Android.OS.Trace, which the Android SDK's own
// global usings bring into scope. Aliasing is the least surprising fix; the alternative is
// fully qualifying every call.
using DdTrace = Com.Datadog.Android.Trace.Trace;
using Com.Datadog.Android.Trace;
using IO.Opentracing.Util;
using Com.Datadog.Android.Webview;

namespace DatadogNet.Android.DeviceTests;

/// <summary>A single on-device check. Throws to fail.</summary>
public sealed record SmokeTest(string Name, Action Execute);

/// <summary>
/// End-to-end checks that only mean anything on a real device or emulator: they load the native
/// Datadog modules out of the packaged .aar files and drive the real SDK.
/// </summary>
/// <remarks>
/// Nothing here reaches Datadog. The client token is fake and every feature is pointed at a custom
/// endpoint on localhost, so the SDK batches events to disk and its uploads fail locally rather
/// than sending junk to a real intake from CI.
/// <para>
/// The checks are ordered: the SDK has to be initialised before a feature can be enabled, and a
/// feature has to be enabled before it can be driven. A failure early on therefore cascades, which
/// is the intent - the first failure is the informative one.
/// </para>
/// </remarks>
public static class SmokeTests
{
    private const string ClientToken = "fake-client-token-for-e2e-only";
    private const string RumApplicationId = "00000000-0000-0000-0000-000000000000";

    /// <summary>
    /// Where the SDK is told to upload to. Nothing listens on this port; the SDK retries in the
    /// background and never throws, which is exactly the isolation wanted here.
    /// </summary>
    private const string LocalEndpoint = "http://localhost:9";

    public static Action<string> Reporter { get; set; } = _ => { };

    private static void Report(string message) => Reporter(message);

    private static Context Context => global::Android.App.Application.Context;

    public static SmokeTest[] All =>
    [
        new("every native module is loadable", EveryModuleIsLoadable),
        new("initializes the SDK", InitializesTheSdk),
        new("sets verbosity, consent and user info", SetsSdkLevelState),
        new("enables RUM", EnablesRum),
        new("drives a RUM view, action, resource and error", DrivesRum),
        new("enables Logs and writes every level", EnablesLogsAndWritesEveryLevel),
        new("enables Trace", EnablesTrace),
        new("enables Session Replay with the Material extension", EnablesSessionReplay),
        new("enables NDK crash reporting", EnablesNdkCrashReporting),
        new("constructs the OkHttp interceptors", ConstructsOkHttpInterceptors),
        new("exposes WebView tracking", ExposesWebViewTracking),
        new("drives RUM and Logs through the ergonomic overloads", ErgonomicOverloadsWork),
        new("stops the RUM session and the SDK instance", StopsCleanly),
    ];

    /// <summary>
    /// Proves each module's .aar actually made it into the app.
    /// </summary>
    /// <remarks>
    /// This is the check that catches a packaging regression the compiler cannot see. A binding
    /// assembly reaches its Java classes through JNI lookups by name, so a package whose .aar was
    /// missing still compiles and links - and then throws ClassNotFoundException at runtime the
    /// first time a type is touched. That is exactly what shipped when @(AndroidMavenLibrary) was
    /// silently ignored on net8.
    /// <para>
    /// Resolving the Java class by name covers modules that bind no C# types at all, which is the
    /// case for dd-sdk-android-trace-internal.
    /// </para>
    /// </remarks>
    private static void EveryModuleIsLoadable()
    {
        string[] classes =
        [
            "com.datadog.android.Datadog",
            // dd-sdk-android-internal binds no consumer-facing entry point, so this is just a
            // class that only that module could have contributed - EvictingQueue is also the type
            // removed from the C# binding, which makes it a useful proof that remove-node affects
            // the binding and not the shipped .aar.
            "com.datadog.android.internal.collections.EvictingQueue",
            "com.datadog.android.log.Logs",
            "com.datadog.android.trace.Trace",
            "com.datadog.android.rum.Rum",
            "com.datadog.android.sessionreplay.SessionReplay",
            "com.datadog.android.sessionreplay.material.MaterialExtensionSupport",
            "com.datadog.android.sessionreplay.compose.ComposeExtensionSupport",
            "com.datadog.android.ndk.NdkCrashReports",
            "com.datadog.android.webview.WebViewTracking",
            "com.datadog.android.okhttp.DatadogInterceptor",
            // 2.x only: tracing is OpenTracing-based, and AndroidTracer extends the vendored
            // DDTracer that implements io.opentracing.Tracer. Both come from packages 3.x does
            // not have.
            "io.opentracing.Tracer",
            "io.opentracing.util.GlobalTracer",
            "com.datadog.android.trace.AndroidTracer",
            "com.datadog.opentracing.DDTracer",
            // The two libraries with no .NET binding, embedded as plain Java. Their absence is
            // otherwise invisible until the SDK reaches for them at runtime.
            "com.lyft.kronos.KronosClock",
            "org.jctools.queues.MpscArrayQueue",
        ];

        // The app's own class loader, not Class.forName(String). The single-argument overload
        // resolves against the *caller's* loader, and the caller here is a runtime frame whose
        // loader is the boot classpath - so every application class comes back not-found, however
        // correctly it was packaged. The symptom is spectacular and misleading: this check reports
        // com.datadog.android.Datadog missing in the same run where Datadog.Initialize succeeds.
        var loader = Context.ClassLoader!;

        var missing = new List<string>();
        foreach (var name in classes)
        {
            try
            {
                _ = Java.Lang.Class.ForName(name, false, loader);
            }
            catch (Java.Lang.ClassNotFoundException)
            {
                missing.Add(name);
            }
        }

        Assert(missing.Count == 0, $"these Java classes are not in the app: {string.Join(", ", missing)}");
        Report($"all {classes.Length} Java entry points resolved");
    }

    private static void InitializesTheSdk()
    {
        var configuration = new Configuration.Builder(
                clientToken: ClientToken,
                env: "e2e",
                variant: "",
                service: "datadognet-android-devicetests")
            .UseSite(DatadogSite.Us1)
            .SetBatchSize(BatchSize.Small)
            .SetUploadFrequency(UploadFrequency.Frequent)
            .Build();

        Datadog.Initialize(Context, configuration, TrackingConsent.Granted);

        Assert(Datadog.IsInitialized, "Datadog.IsInitialized was false after initialization.");
        Report("initialized service=datadognet-android-devicetests env=e2e");
    }

    private static void SetsSdkLevelState()
    {
        Datadog.Verbosity = (int)LogPriority.Debug;

        Datadog.SetTrackingConsent(TrackingConsent.Pending);
        Datadog.SetTrackingConsent(TrackingConsent.Granted);

        Datadog.SetUserInfo("e2e-user", "E2E User", "e2e@example.invalid");

        Report("verbosity, consent and user info all accepted");
    }

    private static void EnablesRum()
    {
        var configuration = new RumConfiguration.Builder(RumApplicationId)
            .SetSessionSampleRate(100f)
            .TrackUserInteractions()
            .TrackBackgroundEvents(true)
            .UseViewTrackingStrategy(new ActivityViewTrackingStrategy(true))
            .UseCustomEndpoint(LocalEndpoint)
            .Build();

        Rum.Enable(configuration);

        Assert(GlobalRumMonitor.IsRegistered, "No RUM monitor was registered after Rum.Enable.");
        Report($"RUM enabled for application {RumApplicationId}");
    }

    private static void DrivesRum()
    {
        var monitor = GlobalRumMonitor.Get();
        var attributes = DatadogAttributes.Empty;

        monitor.StartView(new Java.Lang.String("e2e-view"), "E2E View", attributes);
        monitor.AddAction(RumActionType.Tap, "e2e-action", attributes);
        monitor.AddErrorWithStacktrace("e2e-error", RumErrorSource.Source, "at E2E.Fake()", attributes);
        monitor.AddTiming("e2e-timing");

        // A resource is started and stopped so the resource path is exercised too - that is the
        // part of RUM most apps get through the OkHttp interceptor rather than by hand.
        monitor.StartResource("e2e-resource", RumResourceMethod.Get!, "https://example.invalid/thing", attributes);
        monitor.StopResource(
            "e2e-resource",
            new Java.Lang.Integer(200),
            new Java.Lang.Long(0),
            RumResourceKind.Native!,
            attributes);

        monitor.StopView(new Java.Lang.String("e2e-view"), attributes);

        Report("started and stopped a view with an action, error, timing and resource");
    }

    private static void EnablesLogsAndWritesEveryLevel()
    {
        Logs.Enable(new LogsConfiguration.Builder().UseCustomEndpoint(LocalEndpoint).Build());

        var logger = new Logger.Builder()
            .SetName("e2e")
            .SetService("datadognet-android-devicetests")
            .SetNetworkInfoEnabled(true)
            .SetBundleWithRumEnabled(true)
            .SetLogcatLogsEnabled(false)
            .Build();

        Assert(logger is not null, "Logger.Builder.Build returned null.");

        logger!.V("e2e verbose");
        logger.D("e2e debug");
        logger.I("e2e info");
        logger.W("e2e warn");
        logger.E("e2e error");

        logger.AddTag("suite", "device-tests");
        logger.AddAttribute("attempt", new Java.Lang.Integer(1));
        logger.RemoveTag("suite");
        logger.RemoveAttribute("attempt");

        Report("wrote five levels and round-tripped a tag and an attribute");
    }

    private static void EnablesTrace()
    {
        DdTrace.Enable(new TraceConfiguration.Builder().UseCustomEndpoint(LocalEndpoint).Build());

        // 2.x tracing is OpenTracing. AndroidTracer extends the vendored DDTracer, which implements
        // io.opentracing.Tracer - so the tracer registers with GlobalTracer and hands out spans
        // through the OpenTracing interfaces bound by DatadogNet.OpenTracing.Android.
        //
        // 3.x has none of this: it removed OpenTracing and AndroidTracer along with it, and uses
        // DatadogTracing.NewTracerBuilder + GlobalDatadogTracer instead. This check is the clearest
        // single difference between the two lines.
        var tracer = new AndroidTracer.Builder().SetService("datadognet-android-devicetests").Build();
        Assert(tracer is not null, "AndroidTracer.Builder.Build returned null.");

        GlobalTracer.RegisterIfAbsent(tracer!);
        Assert(GlobalTracer.IsRegistered, "GlobalTracer.IsRegistered was false after registering.");

        // Drive a real span through the OpenTracing surface - this is the part that proves the
        // cross-package chain works: AndroidTracer (Trace package) returning an ISpanBuilder and
        // ISpan (OpenTracing package).
        var span = tracer!.BuildSpan("e2e-operation").Start();
        Assert(span is not null, "BuildSpan(...).Start() returned null.");

        // The numeric overload is setTag(String, java.lang.Number), so the value has to be a Java
        // box rather than a C# int - a bare 1 binds to the generic setTag(Tag<T>, T) instead and
        // fails to convert.
        span!.SetTag("e2e.kind", "smoke");
        span.SetTag("e2e.count", new Java.Lang.Integer(1));
        span.SetTag("e2e.flag", true);
        span.Log("something happened");

        using (var scope = tracer.ActivateSpan(span))
        {
            Assert(scope is not null, "ActivateSpan returned no scope.");
            Assert(tracer.ActiveSpan() is not null, "No active span inside the scope.");
        }

        span.Finish();

        Report("Trace enabled; AndroidTracer registered with GlobalTracer and a span round-tripped");
    }

    private static void EnablesSessionReplay()
    {
        var configuration = new SessionReplayConfiguration.Builder(100f)
            .SetTextAndInputPrivacy(TextAndInputPrivacy.MaskAll!)
            .SetImagePrivacy(ImagePrivacy.MaskAll!)
            .SetTouchPrivacy(TouchPrivacy.Hide!)
            .AddExtensionSupport(new MaterialExtensionSupport())
            .UseCustomEndpoint(LocalEndpoint)
            .Build();

        SessionReplay.Enable(configuration);

        Report("Session Replay enabled with the Material extension and everything masked");
    }

    private static void EnablesNdkCrashReporting()
    {
        // Enabling installs a signal handler. The app is not crashed afterwards - a crash would
        // take the test host with it - so this proves the module links and the handler installs,
        // not that a report round-trips to Datadog.
        NdkCrashReports.Enable();

        Report("NDK crash reporting enabled");
    }

    private static void ConstructsOkHttpInterceptors()
    {
        var hosts = new List<string> { "example.invalid" };

        var datadogInterceptor = new DatadogInterceptor.Builder(hosts).Build();
        Assert(datadogInterceptor is not null, "DatadogInterceptor.Builder.Build returned null.");

        var eventListenerFactory = new DatadogEventListener.Factory();
        Assert(eventListenerFactory is not null, "DatadogEventListener.Factory could not be constructed.");

        Report("DatadogInterceptor and DatadogEventListener.Factory both constructed");
    }

    private static void ExposesWebViewTracking()
    {
        // A WebView is not created here: instantiating one spins up the whole web content process,
        // which is slow and flaky in CI for no added coverage. That the class resolves proves the
        // module is linked, which is what this package contributes.
        Assert(
            Java.Lang.Class.ForName("com.datadog.android.webview.WebViewTracking", false, Context.ClassLoader!) is not null,
            "WebViewTracking did not resolve to a Java class.");

        Report("WebViewTracking is available");
    }

    /// <summary>
    /// Exercises the hand-written convenience layer, which the generated binding knows nothing
    /// about and which no other check would touch.
    /// </summary>
    private static void ErgonomicOverloadsWork()
    {
        var monitor = GlobalRumMonitor.Get();

        // The scope form: the view is stopped when the using block is left, whatever happens in it.
        using (var view = monitor.StartView("ergonomic-view", "Ergonomic View"))
        {
            Assert(view.Key == "ergonomic-view", $"Scope reported the wrong key: {view.Key}");

            monitor.AddAction(RumActionType.Tap, "ergonomic-action", new Dictionary<string, object?>
            {
                ["string"] = "text",
                ["int"] = 42,
                ["double"] = 1.5,
                ["decimal"] = 42.50m,
                ["bool"] = true,
                ["null"] = null,
                ["date"] = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ["guid"] = Guid.Empty,
                ["enum"] = DatadogLogLevel.Info,
                ["list"] = new[] { 1, 2, 3 },
                ["nested"] = new Dictionary<string, object?> { ["inner"] = "value" },
            });

            monitor.AddError(new InvalidOperationException("ergonomic failure"));
        }

        // Disposing a scope whose view was already stopped by hand must not stop it twice.
        var second = monitor.StartView("ergonomic-view-2");
        monitor.StopView("ergonomic-view-2");
        second.Dispose();

        // An attribute type with no Java representation must be rejected loudly rather than
        // silently dropped, since a missing attribute is invisible until someone queries for it.
        var rejected = false;
        try
        {
            DatadogAttributes.From(new Dictionary<string, object?> { ["bad"] = new object() });
        }
        catch (ArgumentException)
        {
            rejected = true;
        }

        Assert(rejected, "An unconvertible attribute value was accepted instead of throwing.");

        var logger = new Logger.Builder().SetName("ergonomics").SetLogcatLogsEnabled(false).Build()!;
        logger.Log(DatadogLogLevel.Info, "ergonomic info");
        logger.Log(DatadogLogLevel.Error, "ergonomic error", new InvalidOperationException("boom"));
        logger.Log(DatadogLogLevel.Warn, "ergonomic warn", null, new Dictionary<string, object?>
        {
            ["k"] = "v",
        });

        Report("view scopes, attribute conversion and logger helpers all behaved");
    }

    private static void StopsCleanly()
    {
        GlobalRumMonitor.Get().StopSession();
        Datadog.ClearAllData();
        Datadog.StopInstance();

        Assert(!Datadog.IsInitialized, "Datadog.IsInitialized was still true after StopInstance.");
        Report("session stopped, data cleared and instance torn down");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
