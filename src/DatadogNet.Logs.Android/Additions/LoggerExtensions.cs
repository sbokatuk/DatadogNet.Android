#nullable enable

using System;
using System.Collections.Generic;
using Com.Datadog.Android;

namespace Com.Datadog.Android.Log;

/// <summary>The severity of a Datadog log entry.</summary>
/// <remarks>
/// The generated <c>Logger.Log</c> takes an <see cref="int"/> priority, because Kotlin passes
/// android.util.Log's constants straight through. The values here are those constants, so this is
/// a name for what the SDK already expects rather than a translation layer.
/// </remarks>
public enum DatadogLogLevel
{
    /// <summary>android.util.Log.VERBOSE</summary>
    Verbose = 2,

    /// <summary>android.util.Log.DEBUG</summary>
    Debug = 3,

    /// <summary>android.util.Log.INFO</summary>
    Info = 4,

    /// <summary>android.util.Log.WARN</summary>
    Warn = 5,

    /// <summary>android.util.Log.ERROR</summary>
    Error = 6,

    /// <summary>android.util.Log.ASSERT - Datadog reports this as CRITICAL.</summary>
    Critical = 7,
}

/// <summary>
/// Ergonomic overloads over <see cref="Logger"/>.
/// </summary>
public static class LoggerExtensions
{
    /// <summary>Writes a log entry, optionally with an exception and attributes.</summary>
    /// <remarks>
    /// The generated API is six methods - <c>V</c>, <c>D</c>, <c>I</c>, <c>W</c>, <c>E</c>,
    /// <c>Wtf</c> - each overloaded on a <c>Java.Lang.Throwable</c> a C# app does not have, plus a
    /// <c>Log(int, ...)</c> whose priority is a bare integer.
    /// <para>
    /// This routes to the string-based error overload, so a .NET exception's type, message and
    /// stack reach Datadog as <c>error.kind</c>, <c>error.message</c> and <c>error.stack</c>
    /// without anything crossing the Java exception boundary.
    /// </para>
    /// </remarks>
    public static void Log(
        this Logger logger,
        DatadogLogLevel level,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? attributes = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        logger.Log(
            (int)level,
            message,
            exception?.GetType().FullName!,
            exception?.Message!,
            exception?.ToString()!,
            DatadogAttributes.From(attributes));
    }
}
