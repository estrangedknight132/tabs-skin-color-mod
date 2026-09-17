using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TabsSkinColorMod.TestsRunner
{
    /// <summary>
    /// Dead-simple reflective executor for the xunit test classes. Bypasses the xunit
    /// execution engine (which deadlocks in this sandboxed shell) by invoking test
    /// methods directly: [Fact] methods with no args, [Theory] methods once per data
    /// row supplied by their data attribute. Assert.* failures throw, which we count
    /// as failures. Prints a summary and exits non-zero if anything failed.
    /// </summary>
    public static class Program
    {
        public static int Main()
        {
            var testAsm = typeof(TabsSkinColorMod.Core.Tests.ColorStoreTests).Assembly;
            int passed = 0, failed = 0;
            var failures = new List<string>();

            foreach (var type in testAsm.GetTypes().Where(t => t.IsClass && !t.IsAbstract))
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                {
                    var fact = method.GetCustomAttribute("Xunit.FactAttribute");
                    var theory = method.GetCustomAttribute("Xunit.TheoryAttribute");
                    if (fact == null && theory == null) continue;

                    object instance = null;
                    if (!method.IsStatic)
                    {
                        var ctor = type.GetConstructor(Type.EmptyTypes);
                        if (ctor == null) continue;
                        instance = ctor.Invoke(null);
                    }

                    if (theory == null)
                    {
                        RunCase(method, instance, null, type.Name, ref passed, ref failed, failures);
                    }
                    else
                    {
                        Say("DATA " + type.Name + "." + method.Name);
                        var rows = GetTheoryRows(method);
                        Say("ROWS " + type.Name + "." + method.Name + " = " + rows.Count);
                        if (rows.Count == 0)
                        {
                            RunCase(method, instance, null, type.Name, ref passed, ref failed, failures);
                        }
                        else
                        {
                            foreach (var row in rows)
                                RunCase(method, instance, row, type.Name, ref passed, ref failed, failures);
                        }
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("SUMMARY: " + passed + " passed, " + failed + " failed, total " + (passed + failed));
            foreach (var f in failures)
                Console.WriteLine("  FAILED: " + f);
            return failed == 0 ? 0 : 1;
        }

        private static void RunCase(MethodInfo method, object instance, object[] args, string typeName,
            ref int passed, ref int failed, List<string> failures)
        {
            var label = typeName + "." + method.Name + (args != null ? "(" + string.Join(", ", args.Select(a => a?.ToString() ?? "null")) + ")" : "()");
            Say("RUN  " + label);
            try
            {
                if (args == null || args.Length == 0)
                    method.Invoke(instance, null);
                else
                    method.Invoke(instance, CoerceArgs(method, args));
                passed++;
                Console.WriteLine("PASS " + label);
            }
            catch (Exception ex)
            {
                failed++;
                var msg = (ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex).Message;
                failures.Add(label + " -- " + msg.Replace("\n", " | ").Replace("\r", ""));
                Console.WriteLine("FAIL " + label);
            }
        }

        private static void Say(string msg)
        {
            Console.WriteLine(msg);
            Console.Out.Flush();
        }

        private static object[] CoerceArgs(MethodInfo method, object[] args)
        {
            var pars = method.GetParameters();
            var result = new object[pars.Length];
            for (int i = 0; i < pars.Length; i++)
            {
                var raw = i < args.Length ? args[i] : pars[i].DefaultValue;
                var pt = pars[i].ParameterType;
                if (raw == null) { result[i] = null; continue; }
                var rawType = raw.GetType();
                if (pt.IsInstanceOfType(raw)) { result[i] = raw; continue; }
                // xunit InlineData boxes constants; enums may arrive as ints
                if (pt.IsEnum) result[i] = Enum.ToObject(pt, Convert.ChangeType(raw, Enum.GetUnderlyingType(pt)));
                else result[i] = Convert.ChangeType(raw, pt);
            }
            return result;
        }

        private static List<object[]> GetTheoryRows(MethodInfo method)
        {
            var rows = new List<object[]>();
            foreach (var attr in method.GetCustomAttributes(false))
            {
                var attrType = attr.GetType();
                if (attrType.FullName != "Xunit.InlineDataAttribute" && !DerivesFrom(attrType, "Xunit.Sdk.DataAttribute")) continue;

                // Prefer InlineData's constructor data; fall back to DataAttribute.GetData.
                var ctorData = ReadCtorArray(attr);
                if (ctorData != null) { rows.Add(ctorData); continue; }

                try
                {
                    var getData = attrType.GetMethod("GetData", new[] { typeof(MethodInfo) })
                                  ?? attrType.GetMethod("GetData");
                    if (getData == null) continue;
                    var result = getData.Invoke(attr, new object[] { method });
                    if (result is IEnumerable seq && !(result is string))
                    {
                        foreach (var row in seq)
                            rows.Add(NormalizeRow(row));
                    }
                }
                catch { /* skip unsupported data source */ }
            }
            return rows;
        }

        private static object[] ReadCtorArray(object attr)
        {
            // InlineDataAttribute stores its values in the generic ICollection ctor param.
            var ctors = attr.GetType().GetConstructors();
            foreach (var ctor in ctors)
            {
                var pars = ctor.GetParameters();
                if (pars.Length == 1 && typeof(IEnumerable).IsAssignableFrom(pars[0].ParameterType))
                {
                    var field = attr.GetType()
                        .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                        .FirstOrDefault(f => typeof(IEnumerable).IsAssignableFrom(f.FieldType));
                    if (field != null && field.GetValue(attr) is IEnumerable seq)
                    {
                        var items = seq.Cast<object>().ToArray();
                        return items.Length > 0 ? items : null;
                    }
                }
            }
            return null;
        }

        private static object[] NormalizeRow(object row)
        {
            if (row is object[] arr) return arr;
            var props = row.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var data = props.FirstOrDefault(p => p.Name == "TestData" && p.PropertyType == typeof(object[]));
            if (data != null && data.GetValue(row) is object[] arr2) return arr2;
            return new[] { row };
        }

        private static bool DerivesFrom(Type type, string fullName)
        {
            for (var t = type.BaseType; t != null; t = t.BaseType)
                if (t.FullName == fullName) return true;
            return false;
        }
    }

    internal static class ReflectionExtensions
    {
        public static object GetCustomAttribute(this MethodInfo method, string fullName)
            => method.GetCustomAttributes(false).FirstOrDefault(a => a.GetType().FullName == fullName);
    }
}
