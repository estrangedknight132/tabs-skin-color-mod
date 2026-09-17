using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace TabsSkinColorMod.MetaDump
{
    /// <summary>
    /// Offline discovery tool: dumps types, fields, methods and (optionally) IL bodies
    /// from Assembly-CSharp.dll so the mod's Harmony hook points can be pinned without
    /// launching the game. Output goes to docs/meta/.
    /// </summary>
    public static class Program
    {
        private static string _outDir = "docs/meta";

        public static int Main(string[] args)
        {
            string asmPath = null;
            string focus = null;
            bool il = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--asm": asmPath = args[++i]; break;
                    case "--focus": focus = args[++i]; break;
                    case "--il": il = true; break;
                    case "--out": _outDir = args[++i]; break;
                }
            }

            if (asmPath == null || !File.Exists(asmPath))
            {
                Console.Error.WriteLine("usage: MetaDump --asm <Assembly-CSharp.dll> [--focus substring] [--il] [--out dir]");
                return 1;
            }

            Directory.CreateDirectory(_outDir);

            var rp = new ReaderParameters { ReadSymbols = false };
            using (var asm = AssemblyDefinition.ReadAssembly(asmPath, rp))
            {
                var sb = new StringBuilder();
                foreach (var type in asm.MainModule.Types.Where(t => !t.IsNested))
                {
                    var all = new List<TypeDefinition> { type };
                    all.AddRange(type.NestedTypes);

                    foreach (var t in all)
                    {
                        if (focus != null &&
                            t.FullName.IndexOf(focus, StringComparison.OrdinalIgnoreCase) < 0 &&
                            (t.BaseType == null || t.BaseType.FullName.IndexOf(focus, StringComparison.OrdinalIgnoreCase) < 0))
                            continue;

                        DumpType(sb, t, il);
                    }
                }
                var suffix = focus != null ? "." + MakeSafeFileName(focus) : ".all";
                var outFile = Path.Combine(_outDir, "metadump" + suffix + ".txt");
                File.WriteAllText(outFile, sb.ToString());
                Console.WriteLine("wrote " + outFile + " (" + sb.Length + " chars)");
            }
            return 0;
        }

        private static void DumpType(StringBuilder sb, TypeDefinition t, bool il)
        {
            sb.AppendLine("================================================================");
            sb.AppendLine("TYPE: " + t.FullName);
            if (t.BaseType != null) sb.AppendLine("  base: " + t.BaseType.FullName);
            sb.AppendLine("  interfaces: " + string.Join(", ", t.Interfaces.Select(i => i.InterfaceType.FullName)));

            foreach (var f in t.Fields)
            {
                var attrs = new List<string>();
                if (f.IsPublic) attrs.Add("public");
                if (f.IsStatic) attrs.Add("static");
                if (f.IsFamily) attrs.Add("protected");
                if (f.IsPrivate) attrs.Add("private");
                if (f.HasConstant) attrs.Add("const=" + f.Constant);
                sb.AppendLine("  field: " + f.FieldType.Name + " " + f.Name + (attrs.Count > 0 ? "  [" + string.Join(",", attrs) + "]" : ""));
            }

            foreach (var p in t.Properties)
                sb.AppendLine("  prop: " + p.PropertyType.Name + " " + p.Name + (p.GetMethod != null ? " { get; }" : "") + (p.SetMethod != null ? " { set; }" : ""));

            foreach (var m in t.Methods)
                DumpMethod(sb, m, il);
        }

        private static void DumpMethod(StringBuilder sb, MethodDefinition m, bool il)
        {
            var pars = string.Join(", ", m.Parameters.Select(p => p.ParameterType.Name + " " + p.Name));
            sb.AppendLine("  method: " + m.ReturnType.Name + " " + m.Name + "(" + pars + ")" +
                          (m.IsStatic ? " [static]" : "") +
                          (m.IsVirtual || m.HasOverrides ? " [virtual]" : "") +
                          (m.Body == null || !m.HasBody ? " [no body]" : ""));

            if (!il || m.Body == null) return;

            foreach (var ins in m.Body.Instructions)
            {
                sb.AppendLine("      IL_" + ins.Offset.ToString("X4") + ": " + ins.OpCode.Name + " " +
                              (ins.Operand != null ? ins.Operand.ToString() : ""));
            }
        }

        private static string MakeSafeFileName(string s)
            => new string(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
    }
}
