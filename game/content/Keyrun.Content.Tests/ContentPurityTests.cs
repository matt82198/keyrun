using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// ENGINE-PLAN section 4: no float, double, decimal or Godot type inside Keyrun.Content. Content
/// numbers are integers so the sim (lane 1C) consumes them without conversion. The control type
/// proves the scan catches a floating-point member.
/// </summary>
public sealed class ContentPurityTests
{
    private static readonly HashSet<Type> Forbidden = new() { typeof(float), typeof(double), typeof(decimal) };

    private static Type Strip(Type t) => Nullable.GetUnderlyingType(t) ?? (t.IsArray ? t.GetElementType()! : t);

    private static bool IsForbidden(Type t)
    {
        Type s = Strip(t);
        if (Forbidden.Contains(s)) return true;
        if (s.Namespace is not null && s.Namespace.StartsWith("Godot", StringComparison.Ordinal)) return true;
        return s.IsGenericType && s.GetGenericArguments().Any(IsForbidden);
    }

    private static List<string> Scan(IEnumerable<Type> types)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var violations = new List<string>();
        foreach (Type t in types)
        {
            foreach (FieldInfo f in t.GetFields(all))
                if (IsForbidden(f.FieldType)) violations.Add(t.FullName + "." + f.Name + " : " + f.FieldType);
            foreach (PropertyInfo p in t.GetProperties(all))
                if (IsForbidden(p.PropertyType)) violations.Add(t.FullName + "." + p.Name + " : " + p.PropertyType);
        }
        return violations;
    }

    private sealed class FloatControl
    {
        public double Speed { get; set; }
    }

    [Fact]
    public void Content_assembly_has_no_floating_point_or_godot_members()
    {
        Type[] types = typeof(ContentLoader).Assembly.GetTypes();
        Assert.True(types.Length > 20, "vacuity guard: only " + types.Length + " types scanned");
        List<string> violations = Scan(types);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Content_assembly_references_no_godot_assembly()
    {
        var refs = typeof(ContentLoader).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? "").ToArray();
        Assert.DoesNotContain(refs, n => n.StartsWith("Godot", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Control_scan_catches_a_double_member()
    {
        Assert.NotEmpty(Scan(new[] { typeof(FloatControl) }));
    }
}
