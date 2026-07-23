using Android.Content;
using Android.Util;
using Com.Datadog.Android;
using Com.Datadog.Android.Core.Configuration;
using Com.Datadog.Android.Log;
using Com.Datadog.Android.Ndk;
using Com.Datadog.Android.Privacy;
using Com.Datadog.Android.Rum;
using Com.Datadog.Android.Rum.Tracking;
using Com.Datadog.Android.Sessionreplay;
using IO.Opentracing.Util;

// Com.Datadog.Android.Trace.Trace collides with Android.OS.Trace, which is in scope through the
// Android SDK's own usings. Aliasing is the least surprising fix.
using DdTrace = Com.Datadog.Android.Trace.Trace;
using Com.Datadog.Android.Trace;

namespace DatadogNet.Android.Example;

/// <summary>
/// Everything this sample does with the Datadog SDK, in one file.
/// </summary>
/// <remarks>
/// The client token and RUM application id are placeholders, and every feature is pointed at a
/// custom endpoint on localhost - so running the sample uploads nothing. Replace
/// <see cref="ClientToken"/>, <see cref="RumApplicationId"/> and <see cref="Site"/> with your own
/// and delete the <c>UseCustomEndpoint</c> calls to send data to Datadog for real.
/// </remarks>
public static class Datadog
{
    private const string ClientToken = "<CLIENT_TOKEN>";
    private const string RumApplicationId = "<RUM_APPLICATION_ID>";

    /// <summary>Your organisation's region. Getting this wrong is the usual reason nothing arrives.</summary>
    private static DatadogSite Site => DatadogSite.Us1!;

    /// <summary>Delete this, and the UseCustomEndpoint calls, to upload to Datadog for real.</summary>
    private const string LocalEndpoint = "http://localhost:9";

    private static Logger? logger;

    /// <summary>The app's logger, once <see cref="Initialize"/> has run.</summary>
    public static Logger Logger =>
        logger ?? throw new InvalidOperationException("Datadog.Initialize has not been called.");

    /// <summary>
    /// Initialises the SDK and every feature this sample uses.
    /// </summary>
    /// <remarks>
    /// Called from MauiProgram.CreateMauiApp, before the builder runs. Crash reporting only covers
    /// what happens after it is enabled, and startup crashes are the ones worth catching.
    /// </remarks>
    public static void Initialize(Context context)
    {
        var configuration = new Configuration.Builder(
                clientToken: ClientToken,
                env: "sample",
                variant: "",
                service: "datadognet-android-example")
            .UseSite(Site)
            .SetBatchSize(BatchSize.Medium!)
            .SetUploadFrequency(UploadFrequency.Average!)
            .Build();

        global::Com.Datadog.Android.Datadog.Initialize(context, configuration, TrackingConsent.Granted!);

        // The SDK's own diagnostics go to logcat. Worth turning up while wiring this in for the
        // first time; it is where "your client token is invalid" shows up.
        global::Com.Datadog.Android.Datadog.Verbosity = (int)LogPriority.Info;

        EnableRum();
        EnableLogs();
        EnableTrace();
        EnableSessionReplay();

        // Installs a signal handler, so it is deliberately last and deliberately opt-in.
        NdkCrashReports.Enable();
    }

    private static void EnableRum()
    {
        var configuration = new RumConfiguration.Builder(RumApplicationId)
            .SetSessionSampleRate(100f)
            .TrackUserInteractions()
            .TrackBackgroundEvents(true)
            // MAUI draws every page into one Activity, so this reports a single view for the whole
            // app. It is still worth enabling - it is what reports app start and ANRs - but real
            // per-screen views come from Datadog.StartView below.
            .UseViewTrackingStrategy(new ActivityViewTrackingStrategy(true))
            .UseCustomEndpoint(LocalEndpoint)
            .Build();

        Rum.Enable(configuration);
    }

    private static void EnableLogs()
    {
        Logs.Enable(new LogsConfiguration.Builder().UseCustomEndpoint(LocalEndpoint).Build());

        logger = new Logger.Builder()
            .SetName("sample")
            .SetService("datadognet-android-example")
            .SetNetworkInfoEnabled(true)
            // Bundling with RUM is what ties a log line to the view and session it happened in.
            .SetBundleWithRumEnabled(true)
            .SetLogcatLogsEnabled(true)
            .Build();
    }

    private static void EnableTrace()
    {
        DdTrace.Enable(new TraceConfiguration.Builder().UseCustomEndpoint(LocalEndpoint).Build());

        // 2.x tracing is OpenTracing: AndroidTracer implements io.opentracing.Tracer, and
        // GlobalTracer is where the rest of the app reaches it from. dd-sdk-android 3.0 removed
        // both in favour of DatadogTracing/GlobalDatadogTracer, so this is line-specific code.
        var tracer = new AndroidTracer.Builder()
            .SetService("datadognet-android-example")
            .Build();

        GlobalTracer.RegisterIfAbsent(tracer!);
    }

    private static void EnableSessionReplay()
    {
        // Everything masked. These levels decide what is redacted on the device, before anything
        // is uploaded - loosen them deliberately, not by default.
        var configuration = new SessionReplayConfiguration.Builder(100f)
            .SetTextAndInputPrivacy(TextAndInputPrivacy.MaskAll!)
            .SetImagePrivacy(ImagePrivacy.MaskAll!)
            .SetTouchPrivacy(TouchPrivacy.Hide!)
            .UseCustomEndpoint(LocalEndpoint)
            .Build();

        SessionReplay.Enable(configuration);
    }

    /// <summary>
    /// Starts a RUM view that is stopped when the returned scope is disposed.
    /// </summary>
    /// <remarks>
    /// This is the convenience overload from DatadogNet.RUM.Android. The raw API is a StartView /
    /// StopView pair matched by key, and a view left open by an early return goes on collecting
    /// every later action and error in the session.
    /// </remarks>
    public static RumViewScope StartView(string key, string? name = null) =>
        GlobalRumMonitor.Get().StartView(key, name);

    /// <summary>Reports a tap, with whatever attributes are worth querying on later.</summary>
    public static void TrackTap(string name, IReadOnlyDictionary<string, object?>? attributes = null) =>
        GlobalRumMonitor.Get().AddAction(RumActionType.Tap!, name, attributes);

    /// <summary>Reports an exception as a RUM error and a log line.</summary>
    public static void TrackError(Exception exception)
    {
        GlobalRumMonitor.Get().AddError(exception);
        Logger.Log(DatadogLogLevel.Error, exception.Message, exception);
    }
}
