#nullable enable

using System;
using System.Collections.Generic;
using Com.Datadog.Android;
using IO.Opentracing;
using IO.Opentracing.Propagation;

namespace Com.Datadog.Android.Trace;

/// <summary>
/// Ergonomic overloads over the OpenTracing API 2.x tracing is built on.
/// </summary>
/// <remarks>
/// The generated members are all still there; these sit alongside them. Each one closes a gap that
/// only appears from C#, and each has a counterpart on the iOS side of a cross-platform app - so
/// these are also what makes a shared tracing abstraction over both SDKs possible.
/// </remarks>
public static class TracingExtensions
{
    /// <summary>
    /// Marks a span as failed and attaches an exception's type, message and stack.
    /// </summary>
    /// <param name="span">The span.</param>
    /// <param name="exception">What went wrong.</param>
    /// <remarks>
    /// <c>io.opentracing.Span</c> has no <c>setError</c> - dd-sdk-ios's <c>OTSpan</c> does - so the
    /// Datadog convention is an <c>error</c> tag plus four log fields, which dd-trace turns into
    /// the span's error facets. Getting the field names wrong produces a span that looks fine and
    /// is never counted as an error, which is exactly the kind of thing worth writing down once.
    /// </remarks>
    public static void SetError(this ISpan span, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(span);
        ArgumentNullException.ThrowIfNull(exception);

        span.SetError(
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            exception.ToString());
    }

    /// <summary>Marks a span as failed, without an exception.</summary>
    /// <param name="span">The span.</param>
    /// <param name="kind">The error's type, as it should be grouped by.</param>
    /// <param name="message">What went wrong.</param>
    /// <param name="stack">An optional stack trace.</param>
    public static void SetError(this ISpan span, string kind, string message, string? stack = null)
    {
        ArgumentNullException.ThrowIfNull(span);

        span.SetTag("error", true);

        var fields = new Dictionary<string, object?>
        {
            ["event"] = "error",
            ["error.kind"] = kind,
            ["message"] = message,
        };

        if (stack is not null)
        {
            fields["stack"] = stack;
        }

        span.Log(fields);
    }

    /// <summary>Attaches a structured log to a span, without hand-wrapping the values.</summary>
    /// <remarks>
    /// <c>io.opentracing.Span.log</c> takes <c>Map&lt;String, ?&gt;</c>, which the binding projects
    /// as <c>IDictionary&lt;string, object&gt;</c> - a different dictionary type from the
    /// <c>IDictionary&lt;string, Java.Lang.Object&gt;</c> every other Datadog member takes, so
    /// <see cref="DatadogAttributes.From"/>'s output cannot be passed straight to it.
    /// </remarks>
    public static void Log(this ISpan span, IReadOnlyDictionary<string, object?> fields)
    {
        ArgumentNullException.ThrowIfNull(span);
        ArgumentNullException.ThrowIfNull(fields);

        var converted = DatadogAttributes.From(fields);
        var carrier = new Dictionary<string, object>(converted.Count);

        foreach (var pair in converted)
        {
            carrier[pair.Key] = pair.Value;
        }

        span.Log(carrier);
    }

    /// <summary>
    /// Writes a span's trace context into HTTP headers, so the trace continues into the service
    /// being called.
    /// </summary>
    /// <param name="tracer">The tracer, usually from <c>GlobalTracer.Get()</c>.</param>
    /// <param name="span">The span to propagate.</param>
    /// <returns>The headers to add to the request, in whichever formats the tracer was configured with.</returns>
    /// <remarks>
    /// The obvious way to do this - <c>new TextMapInjectAdapter(myDictionary)</c> - <b>silently
    /// produces nothing</b>, and that is worth knowing about. The adapter's constructor takes an
    /// <c>IDictionary&lt;string, string&gt;</c>, which the binding marshals by <i>copying</i> into
    /// a fresh <c>java.util.HashMap</c>; the SDK then writes the headers into the copy, the managed
    /// dictionary never sees them, and the request goes out untraced with no error anywhere.
    /// <para>
    /// This implements <c>ITextMapInject</c> instead, so the SDK calls back into managed code.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, string> InjectHeaders(this ITracer tracer, ISpan span)
    {
        ArgumentNullException.ThrowIfNull(tracer);
        ArgumentNullException.ThrowIfNull(span);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var context = span.Context();
        if (context is null)
        {
            return headers;
        }

        tracer.Inject(context, IFormat.Builtin.TextMapInject!, new HeaderCollector(headers));

        return headers;
    }

    /// <summary>Collects injected headers straight into a managed dictionary.</summary>
    private sealed class HeaderCollector(IDictionary<string, string> headers)
        : Java.Lang.Object, ITextMapInject
    {
        public void Put(string key, string value) => headers[key] = value;
    }
}
