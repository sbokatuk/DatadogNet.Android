using Com.Datadog.Android.Log;
using Com.Datadog.Android.Rum;

// The hand-written convenience layers live beside the generated types they extend, so each
// namespace has to be in scope: Tracer for BuildSpan(string), Span for the span's own helpers,
// Propagation for Inject.
using Com.Datadog.Android.Trace;
using Com.Datadog.Android.Trace.Api.Propagation;
using Com.Datadog.Android.Trace.Api.Span;
using Com.Datadog.Android.Trace.Api.Tracer;

namespace DatadogNet.Android.Example;

/// <summary>
/// Each button drives one part of the Datadog API. The <see cref="ActivityLabel"/> echoes what was
/// reported, so the sample is useful even without a Datadog account to send it to.
/// </summary>
public partial class MainPage : ContentPage
{
    private const string ViewKey = "main";

    private readonly List<string> activity = [];

    private IDisposable? viewScope;
    private int count;

    public MainPage()
    {
        InitializeComponent();

        StatusLabel.Text = Datadog.IsConfigured
            ? "Reporting to Datadog."
            : "No client token set, so events are collected but never delivered. "
              + "Put your own values in Datadog.cs to send them for real.";
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // One RUM view per page. MAUI renders every page into a single Activity, so without this
        // the whole app reports as a single view for its entire lifetime.
        viewScope = Datadog.StartView(ViewKey, "Main Page");
    }

    protected override void OnDisappearing()
    {
        // Dispose and stop. An earlier version of this sample started a *new* view here, which is
        // the mistake worth not shipping in a sample: it left a view open for the rest of the
        // session, collecting every action and error that followed.
        viewScope?.Dispose();
        viewScope = null;

        base.OnDisappearing();
    }

    private void OnCounterClicked(object? sender, EventArgs e)
    {
        count++;
        CounterBtn.Text = count == 1 ? "Recorded 1 action" : $"Recorded {count} actions";
        SemanticScreenReader.Announce(CounterBtn.Text);

        // Attributes are plain C# values - the convenience overload converts them. The raw binding
        // takes an IDictionary<string, Java.Lang.Object> and needs every value hand-wrapped.
        Datadog.TrackTap("counter", new Dictionary<string, object?> { ["count"] = count });

        Record($"RUM action recorded (count {count})");
    }

    private void OnReportErrorClicked(object? sender, EventArgs e)
    {
        try
        {
            throw new InvalidOperationException("Something the user did went wrong.");
        }
        catch (Exception exception)
        {
            // Reports the managed type, message and stack, so errors group by where they were
            // thrown rather than by message text alone.
            Datadog.TrackError(exception);
            Record($"RUM error recorded: {exception.Message}");
        }
    }

    private async void OnTrackWork(object? sender, EventArgs e)
    {
        // A view scope stops the view however the block is left, including on an exception.
        using (Datadog.StartView("work", "Background Work"))
        {
            Record("started tracking a unit of work");
            await Task.Delay(750);
        }

        Record("work finished, view stopped");
    }

    private async void OnShowSessionId(object? sender, EventArgs e)
    {
        // Worth attaching to a support ticket: it is what turns "the app was slow" into a session
        // you can watch. The raw API takes a Kotlin Function1, which C# cannot express as a lambda.
        var sessionId = await GlobalRumMonitor.Get().GetCurrentSessionIdAsync();

        Record($"session {sessionId ?? "(none - RUM is off, or this session was sampled out)"}");
    }

    private async void OnTraceRequest(object? sender, EventArgs e)
    {
        var tracer = GlobalDatadogTracer.Get()!;

        // The operation name is what APM groups by, so it has to be low cardinality - never a URL
        // and never anything carrying an id.
        var span = tracer.BuildSpan("http.request")!.Start()!;

        try
        {
            span.SetTag("http.method", "GET");

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/api/items");

            // This is the whole point of distributed tracing: the receiving service reads these
            // headers and continues the same trace, so one flame graph spans both sides.
            //
            // The raw setter is a Kotlin (C, String, String) -> Unit, which binds as an interface -
            // so without this overload every call site needs a Java.Lang.Object subclass, and
            // getting it wrong sends the request untraced with nothing reported.
            foreach (var header in tracer.Propagate()!.Inject(span.Context()!))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            Record($"trace {span.GetTraceId()}");
            Record($"propagating {request.Headers.Count()} headers");

            using var client = new HttpClient();
            using var response = await client.SendAsync(request);

            span.SetTag("http.status_code", (long)(int)response.StatusCode);
            Record($"request finished: {(int)response.StatusCode}");
        }
        catch (Exception exception)
        {
            span.SetError(exception);
            Record($"request failed: {exception.GetType().Name}");
        }
        finally
        {
            span.Finish();
        }
    }

    private void OnFailedSpan(object? sender, EventArgs e)
    {
        var span = GlobalDatadogTracer.Get()!.BuildSpan("checkout.submit")!.Start()!;

        try
        {
            throw new InvalidOperationException("The cart expired before checkout completed.");
        }
        catch (Exception exception)
        {
            // Sets the error flag and message together. io.opentracing.Span had no setError at all
            // in 2.x, so this was four log fields written by convention and easy to get wrong.
            span.SetError(exception);
            Record($"span {span.GetSpanId()} marked as failed");
        }
        finally
        {
            span.Finish();
        }
    }

    private void OnWriteLogs(object? sender, EventArgs e)
    {
        Datadog.Logger.Log(DatadogLogLevel.Debug, "a debug message");
        Datadog.Logger.Log(DatadogLogLevel.Info, "an info message");
        Datadog.Logger.Log(DatadogLogLevel.Warn, "a warning");
        Datadog.Logger.Log(DatadogLogLevel.Error, "an error");

        // A logger-wide attribute goes on every subsequent entry from this logger.
        Datadog.Logger.AddAttribute("screen", "main");

        Record("wrote four log levels and a logger-wide attribute");
    }

    private void OnLogException(object? sender, EventArgs e)
    {
        try
        {
            _ = int.Parse("not a number");
        }
        catch (Exception exception)
        {
            // error.kind, error.message and error.stack are set from the exception, which is what
            // makes it render as an error in the Logs UI rather than as a plain message.
            Datadog.Logger.Log(
                DatadogLogLevel.Error,
                "parsing failed",
                exception,
                new Dictionary<string, object?> { ["input"] = "not a number" });

            Record($"logged exception: {exception.GetType().Name}");
        }
    }

    private void Record(string message)
    {
        activity.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");

        // Keep the list short enough to stay on screen.
        if (activity.Count > 12)
        {
            activity.RemoveAt(activity.Count - 1);
        }

        ActivityLabel.Text = string.Join(Environment.NewLine, activity);
    }
}
