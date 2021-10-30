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
using PowerUp.Core.Errors;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using BenchmarkDotNet.Reports;
using Microsoft.Extensions.Configuration;
using PowerUp.Core.Console;
using static PowerUp.Core.Console.XConsole;

namespace PowerUp.Watcher
{
    public class Watcher
    {
        private IConfigurationRoot _configuration;

        private CodeCompiler _compiler     = null;
        private ILDecompiler _iLDecompiler = new ILDecompiler();
        private ILCompiler   _iLCompiler   = new ILCompiler();

        public static bool IsDebug
        {
            get
            {
                #if DEBUG
                return true;
                #else
                return false;
                #endif
            }
        }
        public Watcher(IConfigurationRoot configuration)
        {
            _configuration = configuration;
        }

        private void Initialize(string csharpFile, string outAsmFile, string outILFile)
        {
            InitializeCsharpCompiler();

            XConsole.WriteLine($"`Input File`: {csharpFile}");
            XConsole.WriteLine($"`ASM   File`: {outAsmFile}");
            XConsole.WriteLine($"`IL    File`: {outILFile}");


            XConsole.WriteLine($"`Libs  Path`: {_compiler.DotNetCoreDirPath}");
            XConsole.WriteLine($"`Language  Version`: {_compiler.LanguageVersion.ToDisplayString()}");
            XConsole.WriteLine($"`.NET Version`: {Environment.Version.ToString()}");
            XConsole.WriteLine(IsDebug ? "'[DEBUG]'" : "`[RELEASE]`");
        }
        private void InitializeCsharpCompiler()
        {
            if (Environment.Version.Major == 5)
            {
                _compiler = new CodeCompiler(_configuration["DotNetCoreDirPathNet5"]);
            }
            else if (Environment.Version.Major == 6)
            {
                _compiler = new CodeCompiler(_configuration["DotNetCoreDirPathNet6"], LanguageVersion.Default);
            }

            else
            {
                _compiler = new CodeCompiler(_configuration["DotNetCoreDirPathDefault"]);
            }
        }


        public Task WatchFile(string csharpFile, string outAsmFile, string outILFile)
        {
            Initialize(csharpFile, outAsmFile, outILFile);

            string lastCode = null;
            DateTime lastWrite = DateTime.MinValue;
            var t = Task.Run(async () => {

                while (true)
                {
                    try
                    {
                        FileInfo fileInfo = new FileInfo(csharpFile);
                        if (fileInfo.LastWriteTime.Ticks > lastWrite.Ticks)
                        {
                            var code = File.ReadAllText(csharpFile);
                            if (string.IsNullOrEmpty(code) == false && lastCode != code)
                            {
                                DecompilationUnit unit = null;
                                var compilation = _compiler.Compile(code);

                                XConsole.WriteLine($"Decompiling: {csharpFile}");

                                if (fileInfo.Extension == ".il")
                                {
                                    unit = DecompileIL(code);
                                }
                                else
                                {
                                    unit = Decompile(code);
                                }

                                lastWrite = fileInfo.LastWriteTime;
                                lastCode = code;

                                string asmCode = string.Empty;
                                string ilCode = string.Empty;

                                if (unit.Errors.Length > 0)
                                {
                                    StringBuilder errorBuilder = new StringBuilder();
                                    foreach (var error in unit.Errors)
                                    {
                                        errorBuilder.AppendLine($"{error.Message} {error.Trace} {error.Position}");
                                        errorBuilder.AppendLine($"{Environment.NewLine}-----------------------------");
                                    }
                                    var errors = errorBuilder.ToString();
                                    File.WriteAllText(outAsmFile, errors);
                                    File.WriteAllText(outILFile, errors);
                                }
                                else
                                {
                                    if (unit.DecompiledMethods != null)
                                    {
                                        asmCode = ToAsm(unit);
                                    }
                                    if (unit.ILCode != null)
                                    {
                                        ilCode = ToIL(unit);
                                    }

                                    //
                                    // Pring Global Messages.
                                    //
                                    if(unit.Messages != null)
                                    {
                                        asmCode += 
                                            Environment.NewLine + 
                                            string.Join(Environment.NewLine, unit.Messages);
                                    }

                                    File.WriteAllText(outAsmFile, asmCode);
                                    File.WriteAllText(outILFile, ilCode);
                                }

                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        //
                        // Report this back to the out files.
                        //
                        File.WriteAllText(outAsmFile, ex.ToString());
                        File.WriteAllText(outILFile,  ex.ToString());
                    }

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

        //
        // This section describes guides drawing.
        //
        // When presented with the following code:
        //
        // 001: A
        // 002: JMP 005
        // 003: B
        // 004: JMP 005
        // 005: C
        // 006: D
        // 007: JMP 001
        //
        // We need to produce the following guides:
        //
        // ┌001: A
        // │┌002: JMP 005
        // ││003: B
        // ││┌004: JMP 005
        // │└└005: C
        // │006: D
        // └007: JMP 001
        //
        // To make this plesant to look at the longest jumps should be the most outer ones.
        // 1. We need to figure out the direction of each jump, and see if the jump is inside our method.
        // 2. We need to compute the lenght of each jump.
        // 3. We then sort the jump table by the longest length
        // 4. We draw the guides.
        //
        // Since we're writing out one instruction at a time from top to bottom we will need an additional piece of information
        // on the instruction or as a lookup table that will tell us if we should draw a line segment(s), for example:
        // When we are writing out 005: C we need to lookup the guides table that will contain:
        //   005 =>
        //      [0] = |
        //      [1] = └
        //      [2] = └
        // 
        private (int jumpSize, int nestingLevel) PopulateGuides(DecompiledMethod method)
        {
            if (method == null) return (0, 0);

            var methodAddress = method.CodeAddress;
            var codeSize = method.CodeSize;
            int jumps    = 0;

            foreach (var instruction in method.Instructions)
            {
                foreach (var arg in instruction.Arguments)
                {
                    if (arg.HasReferenceAddress && instruction.RefAddress > 0)
                    {
                        instruction.jumpDirection = JumpDirection.Out;
                        if (instruction.RefAddress >= methodAddress && instruction.RefAddress <= methodAddress + codeSize)
                        {
                            jumps++;
                            //
                            //  Jump Up
                            //
                            instruction.jumpDirection = JumpDirection.Up;
                            if (instruction.Address < instruction.RefAddress)
                            {
                                //
                                // Jump Down
                                //
                                instruction.jumpDirection = JumpDirection.Down;
                            }
                            //
                            // Find the instruction and relative distance
                            //
                            foreach(var jmpTo in method.Instructions)
                            {
                                if(instruction.RefAddress == jmpTo.Address)
                                {
                                    //Found.
                                    instruction.JumpIndex = jmpTo.OrdinalIndex;
                                    instruction.JumpSize = Math.Abs(instruction.OrdinalIndex - jmpTo.OrdinalIndex);
                                }
                            }
                        }
                    }
                }
            }

            //
            // Most outer instruction -> most inner instruction
            //
            int index = 0;
            int maxJmpSize = -1;

            var orderedInstructions = method.Instructions.OrderByDescending(x => x.JumpSize).ToArray();
            maxJmpSize = orderedInstructions[0].JumpSize;

            foreach (var orderedInstruction in orderedInstructions)
            {
                if (orderedInstruction.jumpDirection == JumpDirection.Out || orderedInstruction.jumpDirection == JumpDirection.None)
                    continue;

                var inst = method.Instructions[orderedInstruction.OrdinalIndex];

                if (inst.jumpDirection == JumpDirection.Down)
                {
                    PopulateGuidesForDownJump(inst, method, jumps, index);
                }
                else if (inst.jumpDirection == JumpDirection.Up)
                {
                    PopulateGuidesForUpJump(inst, method, jumps, index);
                }

                index += 2;
            }

            return (maxJmpSize, index);
        }

        private void PopulateGuidesForDownJump(AssemblyInstruction inst, DecompiledMethod method, int methodJumpCount, int nestingIndex)
        {
            //
            // What is our maximum nesting level for this jump.
            //
            var level = 2 * methodJumpCount - nestingIndex;

            //
            // Generate starting guides 
            //
            inst.GuideBlocks[nestingIndex] = ConsoleBorderStyle.TopLeft;
            for (int i = 1; i < level - 1; i++)
                inst.GuideBlocks[nestingIndex + i] = ConsoleBorderStyle.TopBottom;

            inst.GuideBlocks[nestingIndex + level - 1] = ConsoleBorderStyle.Bullet;


            for (int i = 1; i < inst.JumpSize; i++)
            {
                var nestedInst = method.Instructions[inst.OrdinalIndex + i];

                //
                // Check prev guide and if the guide is TopBotom char then 
                // we change our guide to a plus.
                //
                if (nestingIndex > 0 && nestedInst.GuideBlocks[nestingIndex - 1] == ConsoleBorderStyle.TopBottom)
                    nestedInst.GuideBlocks[nestingIndex] = ConsoleBorderStyle.SeparatorBoth;
                else
                    nestedInst.GuideBlocks[nestingIndex] = ConsoleBorderStyle.Left;

                //
                // Populate everything down with whitespace.
                //
                for (int l = 1; l < level; l++)
                {
                    if (nestedInst.GuideBlocks[nestingIndex + l] == '\0')
                        nestedInst.GuideBlocks[nestingIndex + l] = ' ';
                }
            }

            //
            // Get last instruction to set the arrow.
            //
            var lastInst = method.Instructions[inst.OrdinalIndex + inst.JumpSize];
            //
            // If guide above me is TopBotom then change it to arrow since, we are ending a jump here;
            // So someone jumps here as well.
            //
            if (nestingIndex > 0 && lastInst.GuideBlocks[nestingIndex - 1] == ConsoleBorderStyle.TopBottom)
                lastInst.GuideBlocks[nestingIndex - 1] = '>';

            //
            // Generate ending guides 
            //
            lastInst.GuideBlocks[nestingIndex] = ConsoleBorderStyle.BottomLeft;
            for (int i = 1; i < level - 1; i++)
                lastInst.GuideBlocks[nestingIndex + i] = ConsoleBorderStyle.TopBottom;

            lastInst.GuideBlocks[nestingIndex + level - 1] = '>';
        }
        private void PopulateGuidesForUpJump(AssemblyInstruction inst, DecompiledMethod method, int methodJumpCount, int nestingIndex)
        {
            //
            // What is our maximum nesting level for this jump.
            //
            var level = 2 * methodJumpCount - nestingIndex;

            //
            // Generate starting guides 
            //
            inst.GuideBlocks[nestingIndex] = ConsoleBorderStyle.BottomLeft;
            for (int i = 1; i < level - 1; i++)
                inst.GuideBlocks[nestingIndex + i] = ConsoleBorderStyle.TopBottom;

            inst.GuideBlocks[nestingIndex + level - 1] = ConsoleBorderStyle.Bullet;


            for (int i = 1; i < inst.JumpSize; i++)
            {
                var nestedInst = method.Instructions[inst.OrdinalIndex - i];

                //
                // Check prev guide and if the guide is TopBotom char then 
                // we change our guide to a plus.
                //
                if (nestingIndex > 0 && nestedInst.GuideBlocks[nestingIndex - 1] == ConsoleBorderStyle.TopBottom)
                    nestedInst.GuideBlocks[nestingIndex] = ConsoleBorderStyle.SeparatorBoth;
                else
                    nestedInst.GuideBlocks[nestingIndex] = ConsoleBorderStyle.Left;

                //
                // Populate everything down with whitespace.
                //
                for (int l = 1; l < level; l++)
                {
                    if (nestedInst.GuideBlocks[nestingIndex + l] == '\0')
                        nestedInst.GuideBlocks[nestingIndex + l] = ' ';
                }
            }

            //
            // Generate ending guides 
            //
            var lastInst = method.Instructions[inst.OrdinalIndex - inst.JumpSize];
            lastInst.GuideBlocks[nestingIndex] = ConsoleBorderStyle.TopLeft;
            for (int i = 1; i < level - 1; i++)
                lastInst.GuideBlocks[nestingIndex + i] = ConsoleBorderStyle.TopBottom;

            lastInst.GuideBlocks[nestingIndex + level - 1] = '>';
        }

        public string ToAsm(DecompilationUnit unit)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine();

            foreach(var method in unit.DecompiledMethods)
            {
                if (method == null) continue;

                var sizeAndNesting = PopulateGuides(method);

                //
                // Print messages.
                //
                if (method.Messages != null && method.Messages.Length > 0)
                {
                    builder.AppendLine(
                        Environment.NewLine +
                        string.Join(Environment.NewLine, method.Messages));
                }

                builder.AppendLine($"# Instruction Count: {method.Instructions.Count}; Code Size: {method.CodeSize}");
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

                    builder.Append("  ");

                    AppendGuides(builder, inst, sizeAndNesting);
                   
                    builder.Append($"{inst.Address.ToString("X")}: ");
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

        private void AppendGuides(StringBuilder builder, AssemblyInstruction inst, (int jumpSize, int nestingLevel) sizeAndNesting)
        {
            int wsCount = 0;
            bool usedGuides = false;
            for (int i = 0; i <= sizeAndNesting.nestingLevel; i++)
            {
                var block = inst.GuideBlocks[i];

                //
                // Guide not found, append whitespace.
                // @TODO: Most of this stuff is done when we're populating guides
                // This loop should be as simple as possible.
                //
                if (block == '\0') wsCount++;
                else
                {
                    if (wsCount > 0) builder.Append(new String(' ', wsCount));
                    wsCount = 0;
                    builder.Append((char)block);
                    usedGuides = true;
                }
            }

            if (sizeAndNesting.nestingLevel > 0 && usedGuides == false)
                builder.Append(' ', sizeAndNesting.nestingLevel);
        }

        private string CreateArgument(ulong methodAddress, ulong codeSize, AssemblyInstruction instruction, InstructionArg arg, bool isLast)
        {
            StringBuilder builder = new StringBuilder();

            if (instruction.jumpDirection != JumpDirection.None)
            {
                var addressInArg = arg.Value.LastIndexOf(' ');
                var value = arg.Value;
                if (addressInArg != -1)
                {
                    value = arg.Value.Substring(0, addressInArg);
                }

                builder.Append($"{value.Trim()}");
                builder.Append($" {instruction.RefAddress.ToString("X")}");

                if (instruction.jumpDirection == JumpDirection.Out)
                    builder.Append($" ↷");
                else if (instruction.jumpDirection == JumpDirection.Up)
                    builder.Append($" ⇡");
                else if (instruction.jumpDirection == JumpDirection.Down)
                    builder.Append($" ⇣");
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

        public DecompilationUnit DecompileIL(string code)
        {
            var decompilationUnit = new DecompilationUnit();
            var compilationUnit   = _iLCompiler.Compile(code);

            if (compilationUnit.Errors.Count > 0)
            {
                //
                // Handle errors
                //
                decompilationUnit.Errors = compilationUnit.Errors.ToArray();
            }
            else
            {
                var result = compilationUnit
                    .CompiledType
                    .ToAsm(@private: true);
                decompilationUnit.DecompiledMethods = result;

            }

            return decompilationUnit;
        }

        public DecompilationUnit Decompile(string code)
        {
            var unit = new DecompilationUnit();

            var compilation = _compiler.Compile(code);
            var compilationResult = compilation.CompilationResult;
            var assemblyStream = compilation.AssemblyStream;
            var pdbStream = compilation.PDBStream;

            XConsole.WriteLine($"Language Version: `{compilation.LanguageVersion}`");

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
                    var compiledType = loaded.GetType("CompilerGen");
                    var decompiledMethods = compiledType.ToAsm(@private: true);
                    RunPostCompilationOperations(loaded, compiledType, decompiledMethods);
                    HideDecompiledMethods(decompiledMethods);

                    assemblyStream.Position = 0;
                    pdbStream.Position = 0;

                    ILDecompiler iLDecompiler = new ILDecompiler();
                    unit.ILCode = iLDecompiler.ToIL(assemblyStream, pdbStream);
                    unit.DecompiledMethods = decompiledMethods;
                }

                return unit;
            }
        }

        private void HideDecompiledMethods(DecompiledMethod[] methods)
        {
            //
            // Null the benchmark method so it's not displayed.
            //
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name.StartsWith("Bench_")) methods[i] = null;
                else if (methods[i].Name == "Print")      methods[i] = null;
            }
        }

        private void RunPostCompilationOperations(Assembly loadedAssembly, Type compiledType, DecompiledMethod[] decompiledMethods)
        {
            List<string> messages = new List<string>();

            var compiledLog = loadedAssembly.GetType("_Log");
            var instance = loadedAssembly.CreateInstance("CompilerGen");
            var methods = compiledType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            foreach (var method in methods)
            {
                if (method.Name.StartsWith("Bench_"))
                {
                    var summary = (long)method.Invoke(instance, null);

                    var methodUnderBenchmarkName = method.Name.Split("_")[1];

                    messages.Add("# ");
                    messages.Add($"# Method: {methodUnderBenchmarkName}");
                    messages.Add("# Warm-up Count: 1000 calls");
                    messages.Add($"# Took {summary} ms / 1000 calls");
                    messages.Add("# ");

                    //
                    // @TODO Refactor to something faster.
                    //
                    var found = decompiledMethods.FirstOrDefault(x => x.Name == methodUnderBenchmarkName);
                    if (found != null) found.Messages = messages.ToArray();

                    messages.Clear();
                }
                else
                {
                    var attributes = method.GetCustomAttributes();

                    if (attributes.FirstOrDefault(x => x.GetType().Name == "RunAttribute") != null)
                    {
                        method.Invoke(instance, null);
                        var log = (List<string>)compiledLog.GetField("print", BindingFlags.Static | BindingFlags.Public).GetValue(null);

                        messages.AddRange(log);

                        //
                        // @TODO Refactor to something faster.
                        //
                        var found = decompiledMethods.FirstOrDefault(x => x.Name == method.Name);

                        if (found != null) found.Messages = messages.ToArray();

                        messages.Clear();

                    }
                }


            }
        }
    }
}
