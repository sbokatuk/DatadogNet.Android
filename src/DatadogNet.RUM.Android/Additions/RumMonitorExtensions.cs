#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Com.Datadog.Android;

namespace Com.Datadog.Android.Rum;

/// <summary>
/// Ergonomic overloads over <see cref="IRumMonitor"/>.
/// </summary>
/// <remarks>
/// The generated members are all still there; these sit alongside them. Two things they fix:
/// attributes no longer have to be hand-wrapped into Java objects (see
/// <see cref="DatadogAttributes"/>), and a view can be scoped to a <c>using</c> block instead of a
/// <c>StartView</c>/<c>StopView</c> pair matched by key.
/// </remarks>
public static class RumMonitorExtensions
{
    /// <summary>
    /// Starts a RUM view and returns a scope that stops it when disposed.
    /// </summary>
    /// <param name="monitor">The monitor, from <see cref="GlobalRumMonitor.Get()"/>.</param>
    /// <param name="key">Identifies the view. Also the default <paramref name="name"/>.</param>
    /// <param name="name">The name reported to Datadog. Defaults to <paramref name="key"/>.</param>
    /// <param name="attributes">Attributes attached to the view.</param>
    /// <remarks>
    /// The raw API is a pair of calls matched by key, and a view left open by an early return or an
    /// exception goes on collecting every later action and error in the session - attributed to a
    /// screen the user has left. Scoping it to a <c>using</c> block is the difference between that
    /// being possible and not.
    /// <para>
    /// MAUI renders every page into one Activity, so <c>ActivityViewTrackingStrategy</c> sees a
    /// single view for the whole app. This is how a MAUI app reports real screens.
    /// </para>
    /// </remarks>
    public static RumViewScope StartView(
        this IRumMonitor monitor,
        string key,
        string? name = null,
        IReadOnlyDictionary<string, object?>? attributes = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(key);

        monitor.StartView(new Java.Lang.String(key), name ?? key, DatadogAttributes.From(attributes));
        return new RumViewScope(monitor, key);
    }

    /// <summary>Stops a view started with <see cref="StartView"/>.</summary>
    public static void StopView(
        this IRumMonitor monitor,
        string key,
        IReadOnlyDictionary<string, object?>? attributes = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(key);

        monitor.StopView(new Java.Lang.String(key), DatadogAttributes.From(attributes));
    }

    /// <summary>Reports a user action.</summary>
    public static void AddAction(
        this IRumMonitor monitor,
        RumActionType type,
        string name,
        IReadOnlyDictionary<string, object?>? attributes = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        monitor.AddAction(type, name, DatadogAttributes.From(attributes));
    }

    /// <summary>Reports a .NET exception as a RUM error.</summary>
    /// <remarks>
    /// The generated overload takes a <c>Java.Lang.Throwable</c>, which a C# app does not have.
    /// This uses the stacktrace-as-string overload instead, so the managed type, message and stack
    /// all reach Datadog without anything having to cross the Java exception boundary.
    /// </remarks>
    public static void AddError(
        this IRumMonitor monitor,
        Exception exception,
        RumErrorSource? source = null,
        IReadOnlyDictionary<string, object?>? attributes = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(exception);

        monitor.AddErrorWithStacktrace(
            exception.Message,
            source ?? RumErrorSource.Source,
            exception.ToString(),
            DatadogAttributes.From(attributes));
    }

    /// <summary>Reports an error that is not an exception.</summary>
    public static void AddError(
        this IRumMonitor monitor,
        string message,
        RumErrorSource? source = null,
        IReadOnlyDictionary<string, object?>? attributes = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        monitor.AddErrorWithStacktrace(
            message,
            source ?? RumErrorSource.Source,
            stacktrace: null!,
            DatadogAttributes.From(attributes));
    }

    /// <summary>Adds an attribute to every subsequent RUM event.</summary>
    /// <remarks>
    /// The generated overload takes a <c>Java.Lang.Object</c>, so setting a global attribute means
    /// hand-wrapping the value - which is exactly what <see cref="DatadogAttributes"/> exists to
    /// avoid for the map-taking members.
    /// </remarks>
    public static void AddAttribute(this IRumMonitor monitor, string key, object? value)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(key);

        monitor.AddAttribute(key, DatadogAttributes.ToJava(value, key));
    }

    /// <summary>Records that a feature flag was evaluated, so RUM events can be split by variant.</summary>
    public static void AddFeatureFlagEvaluation(this IRumMonitor monitor, string name, object? value)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(name);

        monitor.AddFeatureFlagEvaluation(name, DatadogAttributes.ToJava(value, name));
    }

    /// <summary>Begins tracking a network request as a RUM resource.</summary>
    public static void StartResource(
        this IRumMonitor monitor,
        string key,
        RumResourceMethod method,
        string url,
        IReadOnlyDictionary<string, object?>? attributes = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        monitor.StartResource(key, method, url, DatadogAttributes.From(attributes));
    }

    /// <summary>Completes a resource begun by <see cref="StartResource"/>.</summary>
    /// <remarks>
    /// The generated overload takes <c>java.lang.Integer</c> and <c>java.lang.Long</c>, because both
    /// are nullable in Kotlin - so a C# caller has to box through <c>Java.Lang.Integer.ValueOf</c>
    /// and remember that a null means "not known" rather than zero. Nullable value types say the
    /// same thing in the language the caller is already writing.
    /// </remarks>
    public static void StopResource(
        this IRumMonitor monitor,
        string key,
        int? statusCode,
        long? size,
        RumResourceKind kind,
        IReadOnlyDictionary<string, object?>? attributes = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        monitor.StopResource(
            key,
            statusCode is { } code ? Java.Lang.Integer.ValueOf(code) : null,
            size is { } bytes ? Java.Lang.Long.ValueOf(bytes) : null,
            kind,
            DatadogAttributes.From(attributes));
    }

    /// <summary>Completes a resource that failed, from a message.</summary>
    /// <remarks>
    /// Guards a trap the C# signature does not show: <c>stackTrace</c> is <c>String</c> in Kotlin,
    /// not <c>String?</c>, so passing null reaches Java's own null check and throws
    ///
    ///     NullPointerException: Parameter specified as non-null is null: method
    ///     DatadogRumMonitor.stopResourceWithError, parameter stackTrace
    ///
    /// - unlike <c>errorType</c> beside it, and unlike <c>addErrorWithStacktrace</c>, both of which
    /// are genuinely nullable. Nothing in the generated binding distinguishes the three.
    /// </remarks>
    public static void StopResourceWithError(
        this IRumMonitor monitor,
        string key,
        string message,
        int? statusCode = null,
        string? stackTrace = null,
        string? errorType = null,
        RumErrorSource? source = null,
        IReadOnlyDictionary<string, object?>? attributes = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(message);

        monitor.StopResourceWithError(
            key,
            statusCode is { } code ? Java.Lang.Integer.ValueOf(code) : null,
            message,
            source ?? RumErrorSource.Network,
            stackTrace: stackTrace ?? string.Empty,
            errorType: errorType!,
            DatadogAttributes.From(attributes));
    }

    /// <summary>Completes a resource that failed, from a .NET exception.</summary>
    /// <remarks>
    /// The generated overloads take either a <c>Java.Lang.Throwable</c>, which a C# app does not
    /// have, or six positional arguments including two nullable strings. This uses the string form
    /// so the managed type, message and stack all reach Datadog.
    /// </remarks>
    public static void StopResourceWithError(
        this IRumMonitor monitor,
        string key,
        Exception exception,
        int? statusCode = null,
        RumErrorSource? source = null,
        IReadOnlyDictionary<string, object?>? attributes = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(exception);

        monitor.StopResourceWithError(
            key,
            statusCode is { } code ? Java.Lang.Integer.ValueOf(code) : null,
            exception.Message,
            source ?? RumErrorSource.Network,
            stackTrace: exception.ToString(),
            errorType: exception.GetType().FullName!,
            DatadogAttributes.From(attributes));
    }

    /// <summary>
    /// The id of the current RUM session, or <see langword="null"/> if there is none.
    /// </summary>
    /// <remarks>
    /// The generated <c>GetCurrentSessionId</c> takes a
    /// <c>kotlin.jvm.functions.Function1</c>, which C# cannot express as a lambda at all: it binds
    /// as an interface, so calling it requires declaring a <see cref="Java.Lang.Object"/> subclass
    /// implementing <c>IFunction1</c> and returning null for Kotlin's <c>Unit</c>. That is a lot of
    /// ceremony for a value most apps want in order to put it on a support ticket.
    /// <para>
    /// dd-sdk-ios takes an ordinary block for the same call and needs none of this.
    /// </para>
    /// </remarks>
    public static Task<string?> GetCurrentSessionIdAsync(this IRumMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        // RunContinuationsAsynchronously: the callback arrives on whichever thread the SDK answers
        // on, and a synchronous continuation would run the caller's await-resumption there too.
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        monitor.GetCurrentSessionId(new SessionIdCallback(completion));

        return completion.Task;
    }

    /// <summary>Adapts <c>getCurrentSessionId</c>'s Kotlin lambda to a completion source.</summary>
    private sealed class SessionIdCallback(TaskCompletionSource<string?> completion)
        : Java.Lang.Object, Kotlin.Jvm.Functions.IFunction1
    {
        public Java.Lang.Object? Invoke(Java.Lang.Object? sessionId)
        {
            completion.TrySetResult(sessionId?.ToString());

            // Kotlin's Unit, which the binding maps to null for a Unit-returning lambda.
            return null;
        }
    }
}

/// <summary>
/// A started RUM view. Disposing it stops the view.
/// </summary>
/// <remarks>
/// Disposal is idempotent, so stopping the view by hand and then leaving the <c>using</c> block
/// does not stop it twice - which the SDK would otherwise report as a second view lifecycle.
/// </remarks>
public sealed class RumViewScope : IDisposable
{
    private readonly IRumMonitor monitor;
    private bool stopped;

    internal RumViewScope(IRumMonitor monitor, string key)
    {
        this.monitor = monitor;
        Key = key;
    }

    /// <summary>The key the view was started with.</summary>
    public string Key { get; }

    /// <summary>Stops the view, if it is still open.</summary>
    public void Stop(IReadOnlyDictionary<string, object?>? attributes = null)
    {
        if (stopped)
        {
            return;
        }

        stopped = true;
        monitor.StopView(new Java.Lang.String(Key), DatadogAttributes.From(attributes));
    }

    /// <inheritdoc />
    public void Dispose() => Stop();
}
