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
// The hand-written convenience layers in DatadogNet.TraceApi.Android live beside the generated
// types they extend, so each namespace has to be in scope: Tracer for the generator's own
// BuildSpan(string), Span for GetTraceId/GetSpanId/SetError, Propagation for Inject.
using Com.Datadog.Android.Trace.Api.Propagation;
using Com.Datadog.Android.Trace.Api.Span;
using Com.Datadog.Android.Trace.Api.Tracer;
using Com.Datadog.Android.Webview;

namespace DatadogNet.Android.DeviceTests;

/// <summary>A single on-device check. Throws to fail.</summary>
public sealed record SmokeTest(string Name, Func<Task> Execute)
{
    /// <summary>A synchronous check, which most of them are.</summary>
    public SmokeTest(string name, Action execute)
        : this(name, () =>
        {
            execute();
            return Task.CompletedTask;
        })
    {
    }
}

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
        new("drives a span and reads its ids", DrivesASpanAndReadsItsIds),
        new("injects trace headers into a carrier", InjectsTraceHeaders),
        new("enables Session Replay with the Material extension", EnablesSessionReplay),
        new("enables NDK crash reporting", EnablesNdkCrashReporting),
        new("constructs the OkHttp interceptors", ConstructsOkHttpInterceptors),
        new("exposes WebView tracking", ExposesWebViewTracking),
        new("drives RUM and Logs through the ergonomic overloads", ErgonomicOverloadsWork),
        new("sets single attributes without hand-wrapping", SingleValueAttributesWork),
        new("reads the current RUM session id", ReadsCurrentSessionId),
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

        // dd-sdk-android 3.0 removed AndroidTracer along with the OpenTracing dependency; this is
        // the replacement, and the part of tracing an app touches directly.
        // NewTracerBuilder has no no-argument overload: upstream did not mark it @JvmOverloads,
        // so the SDK core has to be passed explicitly.
        var tracer = DatadogTracing.NewTracerBuilder(Datadog.Instance).Build();
        Assert(tracer is not null, "DatadogTracing.NewTracerBuilder().Build() returned null.");

        GlobalDatadogTracer.RegisterIfAbsent(tracer!);
        Assert(GlobalDatadogTracer.Get() is not null, "GlobalDatadogTracer.Get() returned null.");

        Report("Trace enabled and a tracer registered globally");
    }

    /// <summary>Starts a real span and reads back what the SDK made of it.</summary>
    /// <remarks>
    /// Until 3.12.1.2 nothing in this suite started a span — <see cref="EnablesTrace"/> registered a
    /// tracer and stopped there — so the tracing path was enabled and never driven.
    /// </remarks>
    private static void DrivesASpanAndReadsItsIds()
    {
        // BuildSpan(string) comes from the generator's own IDatadogTracerExtensions; the interface
        // declares CharSequence, so calling it directly would need a Java.Lang.String.
        var span = GlobalDatadogTracer.Get()!.BuildSpan("device-test-span")!.Start()!;

        span.SetTag("kind", "smoke");
        span.SetTag("count", 1L);
        span.SetTag("enabled", true);

        var traceId = span.GetTraceId();
        var spanId = span.GetSpanId();

        Report($"trace {traceId} span {spanId}");

        // The shape is the assertion. These ids are what Datadog correlates a RUM resource to an
        // APM trace on, and they must match what DatadogInterceptor writes: 32 lowercase hex for
        // the trace, decimal for the span.
        Assert(
            traceId.Length == 32 && traceId.All(IsLowerHex),
            $"The trace id '{traceId}' is not 32 lowercase hex characters.");

        Assert(traceId.Any(c => c != '0'), "The trace id is all zeros, so no trace was started.");

        Assert(
            spanId.Length > 0 && spanId.All(char.IsAsciiDigit),
            $"The span id '{spanId}' is not decimal.");

        span.SetError(new InvalidOperationException("span failure"));
        span.LogAttributes(DatadogAttributes.From(new Dictionary<string, object?>
        {
            ["event"] = "retry",
            ["attempt"] = 2,
        }));

        span.Finish();

        Report("span tagged, errored, logged and finished");
    }

    /// <summary>Injects trace headers, in both the dictionary and the delegate form.</summary>
    /// <remarks>
    /// The native setter is a Kotlin <c>(C, String, String) -> Unit</c>, which binds as
    /// <c>IFunction3</c> and cannot be a C# lambda — so before <c>Inject</c> existed here, every
    /// consumer wrote a Java.Lang.Object subclass, and getting it wrong produced a request with no
    /// trace headers and nothing reported.
    /// </remarks>
    private static void InjectsTraceHeaders()
    {
        var tracer = GlobalDatadogTracer.Get()!;
        var span = tracer.BuildSpan("injected-span")!.Start()!;
        var context = span.Context()!;

        var headers = tracer.Propagate()!.Inject(context);

        Report($"headers: {string.Join(", ", headers.Keys)}");

        Assert(
            headers.ContainsKey("x-datadog-trace-id"),
            "Injection produced no x-datadog-trace-id, so a trace would not continue into a backend.");

        var traceId = span.GetTraceId();

        // One call writes every configured format, so traceparent is here too - and it carries the
        // full 128 bits as hex, derived independently of GetTraceId. A second opinion, not a
        // restatement.
        if (headers.TryGetValue("traceparent", out var traceparent))
        {
            var parts = traceparent.Split('-');

            Assert(
                parts.Length >= 2 && parts[1] == traceId,
                $"The trace id '{traceId}' disagrees with traceparent '{traceparent}'.");
        }

        // The Datadog header carries the low 64 bits in decimal; they must be the tail of the id.
        if (ulong.TryParse(headers["x-datadog-trace-id"], out var low))
        {
            var expected = low.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);

            Assert(
                traceId.EndsWith(expected, StringComparison.Ordinal),
                $"The trace id '{traceId}' does not end with '{expected}', the low 64 bits the " +
                $"x-datadog-trace-id header carries as '{headers["x-datadog-trace-id"]}'.");
        }

        // The delegate form, for carriers that are not dictionaries.
        var collected = new List<string>();
        tracer.Propagate()!.Inject(context, (name, _) => collected.Add(name));

        Assert(
            collected.Count == headers.Count,
            $"The delegate form wrote {collected.Count} headers where the dictionary form wrote " +
            $"{headers.Count}.");

        span.Finish();
    }

    /// <summary>The single-value attribute overloads, which need no hand-wrapped Java object.</summary>
    private static void SingleValueAttributesWork()
    {
        var monitor = GlobalRumMonitor.Get();

        using (monitor.StartView("single-value-view"))
        {
            monitor.AddAttribute("global.string", "text");
            monitor.AddAttribute("global.int", 42);
            monitor.AddAttribute("global.null", null);

            monitor.AddFeatureFlagEvaluation("new-checkout", true);
            monitor.AddFeatureFlagEvaluation("checkout-variant", "b");

            monitor.RemoveAttribute("global.int");
        }

        var logger = new Logger.Builder().SetName("single-value").Build();
        logger.AddAttribute("tenant", "acme");
        logger.AddAttribute("retries", 3);
        logger.Log(DatadogLogLevel.Info, "with logger-wide attributes");

        // The converter itself, now public - the reason none of the above needs a Java.Lang.Object.
        Assert(
            DatadogAttributes.ToJava("text", "k") is Java.Lang.String,
            "ToJava did not convert a string to a Java.Lang.String.");

        Report("single-value attributes accepted on RUM, feature flags and a logger");
    }

    /// <summary>The session id, which the SDK answers through a Kotlin lambda.</summary>
    private static async Task ReadsCurrentSessionId()
    {
        var sessionId = await GlobalRumMonitor.Get().GetCurrentSessionIdAsync();

        Report($"session {sessionId ?? "(none)"}");

        // Non-null because RUM is enabled and sampled at 100 above. A null means the callback never
        // fired, which is the failure mode the Task wrapper exists to make visible.
        Assert(sessionId is not null, "The SDK reported no RUM session id.");
    }

    /// <summary>Whether a character is a lowercase hex digit.</summary>
    private static bool IsLowerHex(char c) => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f';

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
