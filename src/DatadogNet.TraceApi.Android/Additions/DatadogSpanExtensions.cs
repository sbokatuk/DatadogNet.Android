#nullable enable

using System;

namespace Com.Datadog.Android.Trace.Api.Span;

/// <summary>Describing a span's outcome, in a form C# can call directly.</summary>
public static class DatadogSpanExtensions
{
    /// <summary>Marks the span as failed, from a .NET exception.</summary>
    /// <param name="span">The span.</param>
    /// <param name="exception">The exception that failed it.</param>
    /// <remarks>
    /// <c>addThrowable</c> takes a <c>java.lang.Throwable</c>, which a .NET exception is not — so
    /// the managed type, message and stack have to be set as separate fields for any of them to
    /// reach Datadog. Getting that split wrong is quiet: the span is marked as an error, and the
    /// APM error panel shows nothing to act on.
    /// <para>
    /// <c>setError</c> takes a <c>java.lang.Boolean</c> rather than a <c>bool</c>, so even the flag
    /// needs boxing.
    /// </para>
    /// </remarks>
    public static void SetError(this IDatadogSpan span, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(span);
        ArgumentNullException.ThrowIfNull(exception);

        span.SetError(
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            exception.ToString());
    }

    /// <summary>Marks the span as failed, from an error kind, message and stack.</summary>
    /// <param name="span">The span.</param>
    /// <param name="kind">The error type.</param>
    /// <param name="message">The error message.</param>
    /// <param name="stack">The stack, attached as an error log on the span.</param>
    public static void SetError(
        this IDatadogSpan span,
        string kind,
        string message,
        string? stack = null)
    {
        ArgumentNullException.ThrowIfNull(span);
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(message);

        // These are real members in 3.x. In 2.x io.opentracing.Span had no setError at all, and a
        // caller had to set an "error" tag plus four log fields by convention - where getting one
        // field name wrong produced a span that looked fine and was never counted as an error.
        span.SetError(Java.Lang.Boolean.True!);
        span.SetErrorMessage($"{kind}: {message}");

        if (stack is not null)
            span.LogErrorMessage(stack);
    }

    /// <summary>
    /// The trace id, rendered the way Datadog's own instrumentation renders it.
    /// </summary>
    /// <param name="span">The span.</param>
    /// <returns>32 lowercase hexadecimal characters, or an empty string if there is no context.</returns>
    /// <remarks>
    /// <c>DatadogTraceId.ToHexString()</c>, which is what <c>DatadogInterceptor</c> writes as
    /// <c>_dd.trace_id</c> when it links a RUM resource to its APM trace. Anything else — including
    /// <c>ToLong()</c>, which silently returns the low half of a 128-bit id — produces a string the
    /// backend will not correlate on.
    /// </remarks>
    public static string GetTraceId(this IDatadogSpan span)
    {
        ArgumentNullException.ThrowIfNull(span);

        return span.Context()?.TraceId?.ToHexString() ?? string.Empty;
    }

    /// <summary>
    /// The span id, rendered the way Datadog's own instrumentation renders it.
    /// </summary>
    /// <param name="span">The span.</param>
    /// <returns>The decimal form, or an empty string if there is no context.</returns>
    /// <remarks>
    /// Decimal, and deliberately not hexadecimal like <see cref="GetTraceId"/>. The asymmetry is
    /// Datadog's wire format: <c>DatadogInterceptor</c> writes <c>_dd.span_id</c> as
    /// <c>String.valueOf(long)</c>.
    /// </remarks>
    public static string GetSpanId(this IDatadogSpan span)
    {
        ArgumentNullException.ThrowIfNull(span);

        return span.Context() is { } context
            ? context.SpanId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
    }
}
