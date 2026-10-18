using System.Collections.Generic;
using System.Reflection;

namespace Keyrun.Content;

/// <summary>
/// System.Text.Json on net8.0 enforces <c>required</c> (key present) but not nullable reference
/// annotations, so <c>"name": null</c> would bind to a non-nullable <see cref="string"/>. This
/// walks a freshly loaded graph and fails on any null stored in a member declared non-nullable,
/// and on any null item inside a <see cref="ValueList{T}"/> or <see cref="ValueMap{TValue}"/>.
/// </summary>
internal static class NullGuard
{
    private static readonly Assembly ContentAssembly = typeof(NullGuard).Assembly;

    /// <summary>Throws <see cref="ContentLoadException"/> naming the first null found under <paramref name="where"/>.</summary>
    public static void Check(object value, string where)
    {
        Walk(value, where, new NullabilityInfoContext());
    }

    private static void Walk(object value, string where, NullabilityInfoContext nullability)
    {
        if (value is IContentCollection collection)
        {
            foreach (KeyValuePair<string, object?> entry in collection.Entries)
            {
                if (entry.Value is null) throw new ContentLoadException(where + entry.Key + " is null");
                Walk(entry.Value, where + entry.Key, nullability);
            }
            return;
        }

        System.Type type = value.GetType();
        if (type.IsEnum || type.Assembly != ContentAssembly) return;

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0) continue;
            string at = where + "." + char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1);
            object? member = property.GetValue(value);
            if (member is null)
            {
                if (nullability.Create(property).ReadState == NullabilityState.NotNull)
                    throw new ContentLoadException(at + " is null but is not optional");
                continue;
            }
            Walk(member, at, nullability);
        }
    }
}
