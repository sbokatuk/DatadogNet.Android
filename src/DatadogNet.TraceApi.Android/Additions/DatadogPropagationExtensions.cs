#nullable enable

using System;
using System.Collections.Generic;
using Com.Datadog.Android.Trace.Api.Span;

namespace Com.Datadog.Android.Trace.Api.Propagation;

/// <summary>
/// Distributed tracing propagation, in a form C# can call.
/// </summary>
/// <remarks>
/// <c>DatadogPropagation.inject</c> is declared in Kotlin as
///
///     fun &lt;C&gt; inject(context: DatadogSpanContext, carrier: C, setter: (C, String, String) -&gt; Unit)
///
/// and that trailing lambda is the problem. Kotlin function types bind as
/// <c>Kotlin.Jvm.Functions.IFunction3</c> — an <i>interface</i>, not a delegate — so C# cannot pass a
/// lambda, a method group, or an <see cref="Action{T1, T2, T3}"/>. Calling it means declaring a
/// <see cref="Java.Lang.Object"/> subclass that implements <c>IFunction3</c>, marshalling three
/// <see cref="Java.Lang.Object"/> parameters back to strings, and returning <see langword="null"/>
/// for Kotlin's <c>Unit</c>.
/// <para>
/// Every consumer who wants distributed tracing has to write that, and getting it wrong does not
/// fail loudly: the request simply goes out with no trace headers, and the trace stops at the app.
/// dd-sdk-ios needs none of it — <c>OTTracer.inject</c> takes an ordinary writer object.
/// </para>
/// <para>
/// <b>Not wrapped:</b> <c>extract</c>. Its signature nests a second Kotlin lambda — the SDK hands
/// you a visitor you call per header, whose <c>Boolean</c> return governs whether iteration
/// continues — and that contract is not documented anywhere the binding can see. Guessing it would
/// produce something that silently reads one header and stops. Extraction is also the rare
/// direction for a mobile app, which receives responses rather than serving requests.
/// </para>
/// </remarks>
public static class DatadogPropagationExtensions
{
    /// <summary>
    /// Writes the trace headers for <paramref name="context"/> into a new dictionary.
    /// </summary>
    /// <param name="propagation">The tracer's propagation, from <c>DatadogTracer.Propagate()</c>.</param>
    /// <param name="context">The span context to propagate, from <c>DatadogSpan.Context()</c>.</param>
    /// <returns>
    /// The headers to add to the outgoing request. Which headers appear depends on the tracing
    /// header types the tracer was built with; a single call writes all of them.
    /// </returns>
    /// <example>
    /// <code>
    /// var span = GlobalDatadogTracer.Get ().BuildSpan (new Java.Lang.String ("http.request")).Start ();
    ///
    /// foreach (var header in GlobalDatadogTracer.Get ().Propagate ().Inject (span.Context ()))
    ///     request.Headers.TryAddWithoutValidation (header.Key, header.Value);
    /// </code>
    /// </example>
    public static IDictionary<string, string> Inject(
        this IDatadogPropagation propagation,
        IDatadogSpanContext context)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        propagation.Inject(context, headers);

        return headers;
    }

    /// <summary>Writes the trace headers for <paramref name="context"/> into an existing dictionary.</summary>
    /// <param name="propagation">The tracer's propagation.</param>
    /// <param name="context">The span context to propagate.</param>
    /// <param name="headers">The dictionary to write into. Existing keys are replaced.</param>
    public static void Inject(
        this IDatadogPropagation propagation,
        IDatadogSpanContext context,
        IDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        propagation.Inject(context, (name, value) => headers[name] = value);
    }

    /// <summary>Writes the trace headers for <paramref name="context"/> through a callback.</summary>
    /// <param name="propagation">The tracer's propagation.</param>
    /// <param name="context">The span context to propagate.</param>
    /// <param name="setter">Called once per header, with its name and value.</param>
    /// <remarks>
    /// The form to use when the destination is not a dictionary — an <c>HttpRequestMessage</c>, a
    /// gRPC metadata collection, a message envelope.
    /// </remarks>
    public static void Inject(
        this IDatadogPropagation propagation,
        IDatadogSpanContext context,
        Action<string, string> setter)
    {
        ArgumentNullException.ThrowIfNull(propagation);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(setter);

        // The carrier is the value the SDK hands back to the setter untouched, and this closes over
        // the real destination instead - so it only has to be a non-null Java object. It cannot be
        // `new Java.Lang.Object()`, whose constructor is protected.
        propagation.Inject(context, new Java.Lang.String(string.Empty), new HeaderSetter(setter));
    }

    /// <summary>Adapts <c>inject</c>'s Kotlin lambda to a C# delegate.</summary>
    private sealed class HeaderSetter(Action<string, string> setter)
        : Java.Lang.Object, Kotlin.Jvm.Functions.IFunction3
    {
        public Java.Lang.Object? Invoke(
            Java.Lang.Object? carrier,
            Java.Lang.Object? name,
            Java.Lang.Object? value)
        {
            // The SDK never passes a null name, but a null value is cheaper to tolerate than to
            // crash an app's request pipeline over.
            if (name is not null)
            {
                setter(name.ToString()!, value?.ToString() ?? string.Empty);
            }

            // Kotlin's Unit, which the binding maps to null for a Unit-returning lambda.
            return null;
        }
    }
}
