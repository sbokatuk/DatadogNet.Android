#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace Com.Datadog.Android;

/// <summary>
/// Converts ordinary .NET values into the <c>IDictionary&lt;string, Java.Lang.Object&gt;</c> that
/// every attribute-taking member of the Datadog SDK expects.
/// </summary>
/// <remarks>
/// Kotlin declares these parameters as <c>Map&lt;String, Any?&gt;</c>, which reaches C# as a
/// dictionary of <see cref="Java.Lang.Object"/>. Building one by hand means wrapping every value -
/// <c>new Java.Lang.Integer(42)</c>, <c>new Java.Lang.String("x")</c> - which is enough friction
/// that attributes tend not to get added at all.
/// <para>
/// A value with no Java representation throws <see cref="ArgumentException"/> rather than being
/// dropped. A silently missing attribute is invisible until someone queries for it in Datadog and
/// finds nothing, which is the worst time to discover it.
/// </para>
/// </remarks>
public static class DatadogAttributes
{
    /// <summary>An empty attribute map, for the overloads that require one.</summary>
    public static IDictionary<string, Java.Lang.Object> Empty { get; } =
        new Dictionary<string, Java.Lang.Object>();

    /// <summary>Converts a .NET dictionary into Datadog's attribute map.</summary>
    /// <exception cref="ArgumentException">A value has no Java representation.</exception>
    public static IDictionary<string, Java.Lang.Object> From(
        IReadOnlyDictionary<string, object?>? attributes)
    {
        if (attributes is null || attributes.Count == 0)
        {
            return new Dictionary<string, Java.Lang.Object>();
        }

        var converted = new Dictionary<string, Java.Lang.Object>(attributes.Count);
        foreach (var pair in attributes)
        {
            converted[pair.Key] = ToJava(pair.Value, pair.Key);
        }

        return converted;
    }

    /// <summary>Converts one value, naming the attribute in any error so it can be found.</summary>
    private static Java.Lang.Object ToJava(object? value, string key) => value switch
    {
        null => null!,

        // Already a Java object - a bound SDK type, or something the caller wrapped itself.
        Java.Lang.Object java => java,

        string text => new Java.Lang.String(text),
        bool flag => new Java.Lang.Boolean(flag),

        // Widened to the largest Java type of the same kind rather than mapped one-for-one: Datadog
        // serialises these to JSON numbers, so the distinction between Int and Long is not
        // observable in the product, and collapsing it keeps the match arms from doubling.
        sbyte or short or int or long => new Java.Lang.Long(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
        byte or ushort or uint => new Java.Lang.Long(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
        ulong big => new Java.Lang.String(big.ToString(CultureInfo.InvariantCulture)),
        float or double => new Java.Lang.Double(Convert.ToDouble(value, CultureInfo.InvariantCulture)),

        // decimal has no Java counterpart. Converting to double would quietly lose precision on
        // exactly the values decimal is chosen for - money - so it goes over as its invariant
        // string form, which round-trips.
        decimal money => new Java.Lang.String(money.ToString(CultureInfo.InvariantCulture)),

        // ISO-8601 round-trip format, which is what Datadog's own attribute conventions use and
        // what its query language can parse as a date.
        DateTime timestamp => new Java.Lang.String(
            timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
        DateTimeOffset timestamp => new Java.Lang.String(
            timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
        TimeSpan duration => new Java.Lang.Double(duration.TotalMilliseconds),

        Guid id => new Java.Lang.String(id.ToString("D", CultureInfo.InvariantCulture)),
        Enum member => new Java.Lang.String(member.ToString()),
        Uri uri => new Java.Lang.String(uri.ToString()),

        // Nested structures are converted recursively, so a dictionary of lists of dictionaries
        // works. Checked before IEnumerable, since a dictionary is one.
        IReadOnlyDictionary<string, object?> nested => ToJavaMap(nested, key),
        IDictionary dictionary => ToJavaMap(dictionary, key),

        // string is IEnumerable and is handled above; this is arrays and lists.
        IEnumerable sequence => ToJavaList(sequence, key),

        _ => throw new ArgumentException(
            $"Attribute '{key}' is a {value.GetType().FullName}, which has no Java representation. " +
            "Convert it to a string, a number, a bool, a date, a collection, or a Java.Lang.Object."),
    };

    private static Java.Lang.Object ToJavaMap(IReadOnlyDictionary<string, object?> source, string key)
    {
        var map = new Java.Util.HashMap();
        foreach (var pair in source)
        {
            map.Put(pair.Key, ToJava(pair.Value, $"{key}.{pair.Key}"));
        }

        return map;
    }

    private static Java.Lang.Object ToJavaMap(IDictionary source, string key)
    {
        var map = new Java.Util.HashMap();
        foreach (DictionaryEntry entry in source)
        {
            var name = entry.Key?.ToString() ?? string.Empty;
            map.Put(name, ToJava(entry.Value, $"{key}.{name}"));
        }

        return map;
    }

    private static Java.Lang.Object ToJavaList(IEnumerable source, string key)
    {
        var list = new Java.Util.ArrayList();
        var index = 0;
        foreach (var item in source)
        {
            list.Add(ToJava(item, $"{key}[{index}]"));
            index++;
        }

        return list;
    }
}
