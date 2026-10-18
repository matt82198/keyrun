using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Static determinism checker for the sim assemblies. It looks at four layers so
/// an impurity cannot hide in any one of them:
/// <list type="number">
/// <item>assembly references (no Godot assembly),</item>
/// <item>metadata type references (no forbidden type named anywhere in the binary),</item>
/// <item>member signatures: fields, properties, events, parameters, returns, locals, base types,</item>
/// <item>method bodies: floating-point opcodes and every member a body calls or loads.</item>
/// </list>
/// Each violation is one human-readable line naming the offending member and the reason.
/// </summary>
public static class PurityScanner
{
    private const BindingFlags All =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
        BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>Floating-point types: the sim is integer-only (mm, ticks, bp).</summary>
    private static readonly HashSet<string> FloatTypes = new(StringComparer.Ordinal)
    {
        "System.Single", "System.Double", "System.Decimal", "System.Half",
        "System.MathF", "System.Numerics.Vector2",
        "System.Numerics.Vector3", "System.Numerics.Vector4", "System.Numerics.Quaternion",
        "System.Numerics.Matrix3x2", "System.Numerics.Matrix4x4",
    };

    /// <summary>Non-deterministic sources: randomness, clocks, process state, seeded hashing.</summary>
    private static readonly HashSet<string> NondeterministicTypes = new(StringComparer.Ordinal)
    {
        "System.Random", "System.DateTime", "System.DateTimeOffset", "System.TimeProvider",
        "System.TimeZoneInfo", "System.Environment", "System.Guid", "System.HashCode",
        "System.Diagnostics.Stopwatch", "System.Diagnostics.Process",
        "System.Security.Cryptography.RandomNumberGenerator",
        "System.Threading.Timer", "System.Timers.Timer",
    };

    /// <summary>IL opcodes that load or produce floating-point values.</summary>
    private static readonly HashSet<short> FloatOpCodes = new()
    {
        OpCodes.Ldc_R4.Value, OpCodes.Ldc_R8.Value, OpCodes.Conv_R4.Value,
        OpCodes.Conv_R8.Value, OpCodes.Conv_R_Un.Value, OpCodes.Ckfinite.Value,
    };

    private static readonly Dictionary<short, OpCode> OpCodeByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    /// <summary>All types defined in <paramref name="assembly"/>, including nested and compiler-generated ones.</summary>
    public static IReadOnlyList<Type> TypesOf(Assembly assembly) => assembly.GetTypes();

    /// <summary>True when <paramref name="name"/> is a Godot engine assembly (GodotSharp, Godot.*).</summary>
    public static bool IsGodotAssemblyName(string name) =>
        name.StartsWith("Godot", StringComparison.OrdinalIgnoreCase);

    /// <summary>Layer 1: the assembly must not reference any Godot assembly.</summary>
    public static IReadOnlyList<string> ScanAssemblyReferences(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Where(r => r.Name is not null && IsGodotAssemblyName(r.Name))
            .Select(r => $"assembly reference: {r.Name} (Godot is forbidden in the sim)")
            .ToList();

    /// <summary>Layer 2: no forbidden type may be named in the binary's TypeRef table.</summary>
    public static IReadOnlyList<string> ScanTypeReferences(Assembly assembly)
    {
        var violations = new List<string>();
        using var stream = File.OpenRead(assembly.Location);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        foreach (var handle in md.TypeReferences)
        {
            var tr = md.GetTypeReference(handle);
            var ns = md.GetString(tr.Namespace);
            var name = md.GetString(tr.Name);
            var full = ns.Length == 0 ? name : ns + "." + name;
            var reason = ReasonForName(full, ns, assemblyName: null);
            if (reason is not null)
            {
                violations.Add($"type reference: {full} ({reason})");
            }
        }
        return violations;
    }

    /// <summary>Layers 3 and 4 over the given types.</summary>
    public static IReadOnlyList<string> ScanTypes(IEnumerable<Type> types)
    {
        var violations = new List<string>();
        foreach (var type in types)
        {
            ScanType(type, violations);
        }
        return violations;
    }

    private static void ScanType(Type type, List<string> violations)
    {
        var owner = type.FullName ?? type.Name;
        Check(type.BaseType, $"{owner} base type", violations);
        foreach (var iface in type.GetInterfaces())
        {
            Check(iface, $"{owner} interface", violations);
        }
        foreach (var field in type.GetFields(All))
        {
            Check(field.FieldType, $"{owner}.{field.Name} field", violations);
        }
        foreach (var prop in type.GetProperties(All))
        {
            Check(prop.PropertyType, $"{owner}.{prop.Name} property", violations);
        }
        foreach (var ev in type.GetEvents(All))
        {
            Check(ev.EventHandlerType, $"{owner}.{ev.Name} event", violations);
        }
        foreach (var method in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
        {
            ScanMethod(type, method, violations);
        }
    }

    private static void ScanMethod(Type type, MethodBase method, List<string> violations)
    {
        var where = $"{type.FullName ?? type.Name}.{method.Name}";
        if (method is MethodInfo mi)
        {
            Check(mi.ReturnType, $"{where} return", violations);
        }
        foreach (var p in method.GetParameters())
        {
            Check(p.ParameterType, $"{where} parameter {p.Name}", violations);
        }
        var body = method.GetMethodBody();
        if (body is null)
        {
            return;
        }
        foreach (var local in body.LocalVariables)
        {
            Check(local.LocalType, $"{where} local #{local.LocalIndex}", violations);
        }
        var il = body.GetILAsByteArray();
        if (il is not null)
        {
            ScanIl(type, method, il, where, violations);
        }
    }

    private static void ScanIl(Type type, MethodBase method, byte[] il, string where, List<string> violations)
    {
        var typeArgs = type.IsGenericType ? type.GetGenericArguments() : null;
        var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
        var pos = 0;
        while (pos < il.Length)
        {
            var offset = pos;
            short value = il[pos++];
            if (value == 0xFE)
            {
                if (pos >= il.Length)
                {
                    violations.Add($"{where} IL_{offset:x4}: truncated two-byte opcode");
                    return;
                }
                value = unchecked((short)(0xFE00 | il[pos++]));
            }
            if (!OpCodeByValue.TryGetValue(value, out var op))
            {
                violations.Add($"{where} IL_{offset:x4}: unknown opcode 0x{value:x4} (fail closed)");
                return;
            }
            if (FloatOpCodes.Contains(op.Value))
            {
                violations.Add($"{where} IL_{offset:x4}: {op.Name} (floating point is forbidden)");
            }
            var operandSize = OperandSize(op.OperandType, il, pos);
            if (IsMemberToken(op.OperandType))
            {
                var token = BitConverter.ToInt32(il, pos);
                CheckToken(method.Module, token, typeArgs, methodArgs, $"{where} IL_{offset:x4} {op.Name}", violations);
            }
            pos += operandSize;
        }
    }

    private static bool IsMemberToken(OperandType t) =>
        t is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok;

    private static int OperandSize(OperandType t, byte[] il, int pos) => t switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, pos)),
        _ => 4,
    };

    private static void CheckToken(Module module, int token, Type[]? typeArgs, Type[]? methodArgs, string where, List<string> violations)
    {
        MemberInfo? member;
        try
        {
            member = module.ResolveMember(token, typeArgs, methodArgs);
        }
        catch (Exception ex) when (ex is ArgumentException or BadImageFormatException)
        {
            violations.Add($"{where}: unresolvable token 0x{token:x8} (fail closed)");
            return;
        }
        switch (member)
        {
            case Type t:
                Check(t, $"{where} -> {t.Name}", violations);
                break;
            case FieldInfo f:
                Check(f.DeclaringType, $"{where} -> {f.DeclaringType?.Name}.{f.Name}", violations);
                Check(f.FieldType, $"{where} -> {f.DeclaringType?.Name}.{f.Name}", violations);
                break;
            case MethodBase m:
                var label = $"{where} -> {m.DeclaringType?.Name}.{m.Name}";
                Check(m.DeclaringType, label, violations);
                if (m is MethodInfo called)
                {
                    Check(called.ReturnType, label, violations);
                }
                foreach (var p in m.GetParameters())
                {
                    Check(p.ParameterType, label, violations);
                }
                break;
        }
    }

    private static void Check(Type? type, string where, List<string> violations)
    {
        var reason = ReasonForType(type, depth: 0);
        if (reason is not null)
        {
            violations.Add($"{where}: {reason}");
        }
    }

    private static string? ReasonForType(Type? type, int depth)
    {
        if (type is null || depth > 16 || type.IsGenericParameter)
        {
            return null;
        }
        if (type.HasElementType)
        {
            return ReasonForType(type.GetElementType(), depth + 1);
        }
        if (type.IsGenericType)
        {
            foreach (var arg in type.GetGenericArguments())
            {
                var inner = ReasonForType(arg, depth + 1);
                if (inner is not null)
                {
                    return inner;
                }
            }
            type = type.GetGenericTypeDefinition();
        }
        var full = type.FullName ?? type.Name;
        var tick = full.IndexOf('`');
        if (tick >= 0)
        {
            full = full[..tick];
        }
        return ReasonForName(full, type.Namespace ?? string.Empty, type.Assembly.GetName().Name);
    }

    private static string? ReasonForName(string fullName, string ns, string? assemblyName)
    {
        if (FloatTypes.Contains(fullName))
        {
            return $"{fullName} is floating point";
        }
        if (NondeterministicTypes.Contains(fullName))
        {
            return $"{fullName} is non-deterministic";
        }
        if (ns.StartsWith("Godot", StringComparison.Ordinal) ||
            (assemblyName is not null && IsGodotAssemblyName(assemblyName)))
        {
            return $"{fullName} is a Godot type";
        }
        return null;
    }
}
