using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Web;
using PowerUp.Core.Decompilation;
using System.Threading.Tasks;
using PowerUp.Core.Compilation;

namespace PowerUp.Watcher
{
    public class Watcher
    {
        private CodeCompiler _compiler = new CodeCompiler();
        private ILDecompiler _iLDecompiler = new ILDecompiler();

        public Task WatchFile(string file, string outAsmFile, string outILFile)
        {
            string lastCode = null;
            DateTime lastWrite = DateTime.MinValue;
            var t = Task.Run(async () => {

                while (true)
                {
                    try
                    {
                        FileInfo fileInfo = new FileInfo(file);
                        if (fileInfo.LastWriteTime > lastWrite)
                        {
                            var code = File.ReadAllText(file);
                            if (string.IsNullOrEmpty(code) == false && lastCode != code)
                            {
                                var unit = Decompile(code);
                                lastWrite = fileInfo.LastWriteTime;
                                lastCode = code;

                                string asmCode = string.Empty;
                                string ilCode = string.Empty;

                                if (unit.DecompiledMethods != null)
                                {
                                    asmCode = ToAsm(unit);
                                }
                                if(unit.ILCode != null)
                                {
                                    ilCode = ToIL(unit);
                                }
                                File.WriteAllText(outAsmFile, asmCode);
                                File.WriteAllText(outILFile,  ilCode);

                            }
                        }
                    }
                    catch { }

                    await Task.Delay(500);
                }  
            });
            return t;
        }

        public string ToIL(DecompilationUnit unit)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine();

            int indentLevel = 0;
            int opCodeIndentLen = 12;
            string indent = new string(' ', indentLevel);
            var na = new ILToken();
            ILToken next = na;

            for (int i = 0; i < unit.ILCode.Length; i++)
            {
                var il = unit.ILCode[i];
                if (i + 1 < unit.ILCode.Length)
                {
                    next = unit.ILCode[i + 1];
                }
                else
                {
                    next = na;
                }

                switch (il.Type)
                {
                    case ILTokenType.Char:
                        builder.Append($"{il.Value}");
                        break;
                    case ILTokenType.LocalRef:
                        builder.Append($"{il.Value}");
                        break;
                    case ILTokenType.Ref:
                        builder.Append($"{il.Value}");
                        break;
                    case ILTokenType.Text:
                        //
                        // Remove comments.
                        //
                        string value = il.Value;

                        if (value.StartsWith("{"))
                        {
                            indentLevel += 4;
                            indent = new string(' ', indentLevel);
                        }
                        else if (value.StartsWith("}"))
                        {
                            indentLevel -= 4;
                            if (indentLevel < 0) indentLevel = 0;
                            indent = new string(' ', indentLevel);
                            builder.Append($"\r\n{indent}");
                        }

                        var commentsIdx = value.IndexOf("//");
                        if (commentsIdx != -1) value = value.Substring(0, commentsIdx);

                        builder.Append($"{value}");

                        break;
                    case ILTokenType.NewLine:
                        builder.Append($"{il.Value}{indent}");
                        break;
                    case ILTokenType.OpCode:
                        var offsetLen = opCodeIndentLen - il.Value.Length;
                        if (offsetLen <= 0) offsetLen = 1;

                        builder.Append($"{il.Value}{new string(' ', offsetLen)}");

                        if(next.Type == ILTokenType.LocalRef)
                        {
                            i++;
                        }

                        break;
                    case ILTokenType.Indent:
                        break;
                    case ILTokenType.Unindent:
                        break;
                    default:
                        builder.Append($"{il.Value}{indent}");
                        break;
                }
            }
            return builder.ToString();
        }

        public string ToAsm(DecompilationUnit unit)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine();

            foreach(var method in unit.DecompiledMethods)
            {
                builder.Append($"{method.Return} {method.Name}(");

                for (int i = 0; i < method.Arguments.Length; i++)
                {
                    builder.Append($"{method.Arguments[i]}");

                    if (i != method.Arguments.Length - 1)
                    {
                        builder.Append(", ");
                    }
                }

                builder.AppendLine("):");

                int pad = 6;
                foreach(var inst in method.Instructions)
                {
                    var offset = pad - inst.Instruction.Length;
                    if (offset < 0) offset = 0;

                    builder.Append($"  {inst.Address.ToString("X")}: ");
                    builder.Append($"{inst.Instruction} " + new string(' ', offset));

                    int idx = 0;
                    foreach (var arg in inst.Arguments)
                    {
                        var argumentValue = CreateArgument(method.CodeAddress, method.CodeSize, inst, arg, idx == inst.Arguments.Length - 1);
                        builder.Append(argumentValue);

                        idx++;
                    }
                    builder.AppendLine();
                }
            }

            return builder.ToString();
        }

        private string CreateArgument(ulong methodAddress, ulong codeSize, AssemblyInstruction instruction, InstructionArg arg, bool isLast)
        {
            StringBuilder builder = new StringBuilder();

            if (arg.HasReferenceAddress && instruction.RefAddress > 0)
            {
                var addressInArg = arg.Value.LastIndexOf(' ');
                var value = arg.Value;
                if (addressInArg != -1)
                {
                    value = arg.Value.Substring(0, addressInArg);
                }

                var arrow = "↷";
                if (instruction.RefAddress >= methodAddress && instruction.RefAddress <= methodAddress + codeSize)
                {
                    arrow = "⇡";
                    if (instruction.Address < instruction.RefAddress)
                        arrow = "⇣";
                }
                else
                {
                    arrow = "↷";
                }

                builder.Append($"{value.Trim()}");
                builder.Append($" {instruction.RefAddress.ToString("X")}");
                builder.Append($" {arrow}");
            }
            else
            {
                //Parse Value
                var value = arg.Value.Trim();
                var code = string.Empty;
                for (int i = 0; i < value.Length; i++)
                {
                    var c = value[i];
                    if (c == ']' || c == '[' || c == '+' || c == '-' || c == '*')
                    {
                        if (string.IsNullOrEmpty(code) == false)
                        {
                            builder.Append($"{code}");
                            code = string.Empty;
                        }

                        builder.Append($"{c}");
                    }
                    else
                    {
                        code += c;
                    }
                }
                if (string.IsNullOrEmpty(code) == false)
                {
                    builder.Append($"{code}");
                }
            }

            if (isLast == false)
            {
                builder.Append($", ");
            }

            return builder.ToString();
        }

        public DecompilationUnit Decompile(string code)
        {
            var unit = new DecompilationUnit();

            var compilation = _compiler.Compile(code);
            var compilationResult = compilation.CompilationResult;
            var assemblyStream = compilation.AssemblyStream;
            var pdbStream = compilation.PDBStream;

            using (var ctx = new CustomAssemblyLoadContext())
            {
                if (compilation.CompilationResult.Success == false)
                {
                    List<Error> errors = new List<Error>();

                    foreach (var diag in compilationResult.Diagnostics)
                    {
                        errors.Add(new Error() { Id = diag.Id, Message = diag.GetMessage() });
                    }

                    unit.Errors = errors.ToArray();
                }
                else
                {
                    assemblyStream.Position = 0;
                    var loaded = ctx.LoadFromStream(assemblyStream);
                    var result = loaded.GetType("CompilerGen").ToAsm(@private: true);

                    assemblyStream.Position = 0;
                    pdbStream.Position = 0;

                    ILDecompiler iLDecompiler = new ILDecompiler();
                    unit.ILCode = iLDecompiler.ToIL(assemblyStream, pdbStream);
                    unit.DecompiledMethods = result;
                }

                return unit;
            }
        }
    }
}
