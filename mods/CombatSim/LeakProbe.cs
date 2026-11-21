#if DEVTOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace CombatSim;

/// <summary>
/// Development: finds what still refers to an object that should be gone. Walks the objects reachable from
/// the static fields of every loaded type, breadth first, until it meets the target, and reports the chain of
/// fields that leads there.
/// </summary>
static class LeakProbe
{
    sealed class Step
    {
        public object From = null!;
        public string Via = "";
        public Step? Parent;
    }

    static readonly Dictionary<Type, FieldInfo[]> s_fields = new();

    static FieldInfo[] FieldsOf(Type type)
    {
        if (s_fields.TryGetValue(type, out FieldInfo[]? known)) return known;
        var all = new List<FieldInfo>();
        for (Type? t = type; t != null && t != typeof(object); t = t.BaseType)
            all.AddRange(t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(f => !f.FieldType.IsPrimitive && !f.FieldType.IsPointer && !f.FieldType.IsEnum && f.FieldType != typeof(string) && !f.FieldType.IsByRefLike));
        return s_fields[type] = all.ToArray();
    }

    public static string Find(object target, int maxObjects = 20_000_000)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<(object Value, Step Step)>();
        int roots = 0;

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly == typeof(LeakProbe).Assembly) continue;   // the simulator's own state is not the question
            Type[] types;
            try { types = assembly.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray()!; }
            foreach (Type type in types)
            {
                if (type.ContainsGenericParameters) continue;
                FieldInfo[] statics;
                try { statics = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); } catch { continue; }
                foreach (FieldInfo field in statics)
                {
                    if (field.IsLiteral || field.FieldType.IsPrimitive || field.FieldType.IsPointer || field.FieldType == typeof(string) || field.FieldType.IsByRefLike) continue;
                    if (field.GetCustomAttribute<ThreadStaticAttribute>() != null) continue;
                    object? value;
                    try { value = field.GetValue(null); } catch { continue; }
                    if (value == null || value is string || !seen.Add(value)) continue;
                    roots++;
                    queue.Enqueue((value, new Step { From = type, Via = $"static {type.FullName}.{field.Name}" }));
                }
            }
        }

        int visited = 0;
        while (queue.Count > 0 && visited < maxObjects)
        {
            var (value, step) = queue.Dequeue();
            visited++;
            if (ReferenceEquals(value, target)) return $"found after {visited:N0} objects from {roots:N0} static roots:\n" + Describe(step, value);

            Type type = value.GetType();
            if (value is Array array)
            {
                Type? element = type.GetElementType();
                if (element == null || element.IsPrimitive || element.IsPointer || element == typeof(string) || element.IsEnum) continue;
                int index = 0;
                foreach (object? item in array)
                {
                    if (item != null && item is not string && !item.GetType().IsPrimitive && (item.GetType().IsValueType || seen.Add(item)))
                        queue.Enqueue((item, new Step { From = value, Via = $"[{index}]", Parent = step }));
                    index++;
                }
                continue;
            }
            if (value is Pointer || value is WeakReference || type.FullName?.StartsWith("System.Reflection", StringComparison.Ordinal) == true || value is Type) continue;
            foreach (FieldInfo field in FieldsOf(type))
            {
                object? next;
                try { next = field.GetValue(value); } catch { continue; }
                if (next == null || next is string) continue;
                // Boxed structs are new objects each time: follow them without remembering them.
                if (next.GetType().IsValueType) { if (next.GetType().IsPrimitive || next.GetType().IsEnum) continue; }
                else if (!seen.Add(next)) continue;
                queue.Enqueue((next, new Step { From = value, Via = $"{type.Name}.{field.Name}", Parent = step }));
            }
        }
        return $"not reachable from static fields ({visited:N0} objects walked from {roots:N0} roots{(queue.Count > 0 ? ", stopped at the limit" : "")}).";
    }

    static string Describe(Step step, object target)
    {
        var chain = new List<string>();
        for (Step? s = step; s != null; s = s.Parent) chain.Add(s.Via);
        chain.Reverse();
        return "  " + string.Join("\n  -> ", chain) + $"\n  = {target.GetType().FullName}";
    }
}
#endif
