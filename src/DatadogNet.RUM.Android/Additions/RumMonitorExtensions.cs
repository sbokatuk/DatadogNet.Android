#nullable enable

using System;
using System.Collections.Generic;
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
