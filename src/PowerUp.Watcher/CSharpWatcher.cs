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
using System.Diagnostics;
using TypeLayout = PowerUp.Core.Decompilation.TypeLayout;

namespace PowerUp.Watcher
{
    public class CSharpWatcher
    {
        private IConfigurationRoot _configuration;

        private CodeCompiler _compiler     = null;
        private ILDecompiler _iLDecompiler = new ILDecompiler();
        private ILCompiler   _iLCompiler   = new ILCompiler();

        private bool _unsafeUseTieredCompilation = false;

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

        public CSharpWatcher(IConfigurationRoot configuration, bool unsafeUseTieredCompilation = false)
        {
            _configuration = configuration;
            _unsafeUseTieredCompilation = unsafeUseTieredCompilation;
        }

        private void Initialize(string csharpFile, string outAsmFile, string outILFile)
        {
            XConsole.WriteLine("CSharp Watcher Initialize:");

            InitializeCsharpCompiler();

            XConsole.WriteLine($"`Input File`: {csharpFile}");
            XConsole.WriteLine($"`ASM   File`: {outAsmFile}");
            XConsole.WriteLine($"`IL    File`: {outILFile}");

            if (File.Exists(csharpFile) == false)
                XConsole.WriteLine("'[WARNING]': Input File doesn't exist");

            if (File.Exists(outAsmFile) == false)
                XConsole.WriteLine("'[WARNING]': ASM File doesn't exist");

            if (File.Exists(outILFile) == false)
                XConsole.WriteLine("'[WARNING]': IL File doesn't exist");

            XConsole.WriteLine($"`Libs  Path`: {_compiler.DotNetCoreDirPath}");

            if(Directory.Exists(_compiler.DotNetCoreDirPath) == false)
            {
                XConsole.WriteLine($"'Cannot find the libs under Path: {_compiler.DotNetCoreDirPath}");
            } 

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
            var iDontCareAboutThisTask = Task.Run(async () => {
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

                                unit.Options = compilation.CompilationOptions;

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

                                    XConsole.WriteLine($"Writing Errors to: {outAsmFile}, {outILFile}");

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

                                    XConsole.WriteLine($"Writing Results to: {outAsmFile}, {outILFile}");

                                    File.WriteAllText(outAsmFile, asmCode);
                                    File.WriteAllText(outILFile, ilCode);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        XConsole.WriteLine($"Writing Errors to: {outAsmFile}, {outILFile}");
                        //
                        // Report this back to the out files.
                        //
                        File.WriteAllText(outAsmFile, ex.ToString());
                        File.WriteAllText(outILFile,  ex.ToString());
                    }

                    await Task.Delay(500);
                }  
            });
            return iDontCareAboutThisTask;
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
            if (method == null || method.Instructions.Any() == false) return (0, 0);

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

        private string ToLayout(TypeLayout[] typeLayouts)
        {
            int offsetPad = 8;
            int sizePad   = 30;
            int tableSize = 42;
            var headerTopBottom = new string(ConsoleBorderStyle.TopBottom, tableSize);
            var displayPadding = new string(' ', 4);

            StringBuilder layoutBuilder = new StringBuilder();
            foreach (var typeLayout in typeLayouts)
            {
                displayPadding = new string(' ', 4);

                layoutBuilder.AppendLine($"# {typeLayout.Name} Memory Layout. {(typeLayout.IsBoxed ? "\r\n# (struct sizes might be wrong since they are boxed to extract layouts)" : "")} ");
                layoutBuilder.AppendLine($"{(typeLayout.IsBoxed ? "struct" : "class")} {typeLayout.Name}");
                layoutBuilder.AppendLine("{");

                bool isHeaderEnd = false;
                int index = 0;
                foreach (var fieldLayout in typeLayout.Fields)
                {
                    layoutBuilder.Append(displayPadding);
                    //
                    // For reference types we need to include the Metadata
                    // which means that we will check if a collection of fields
                    // has a header flag set:
                    // 
                    // [0] - IsHeader = true  -> Render 'Metadata' string and top guides and the field  
                    // [1] - IsHeader = true  -> Render left and right guids and the field
                    // [2] - IsHeader = true  -> Render left and right guids and the field
                    // [3] - IsHeader = false -> Render 'Fields' string and bottom guides and then the field
                    // (Which will not be a part of the header table)
                    //
                    if (typeLayout.IsBoxed == false)
                    {
                        if (index == 0 && fieldLayout.IsHeader == true)
                        {
                            layoutBuilder.AppendLine(@"Metadata:");
                            layoutBuilder.Append(displayPadding);
                            layoutBuilder.AppendLine(ConsoleBorderStyle.TopLeft + headerTopBottom + ConsoleBorderStyle.TopRight);
                            layoutBuilder.Append(displayPadding);
                            layoutBuilder.Append(ConsoleBorderStyle.Left + " ");
                        }
                        else if (index > 0 && fieldLayout.IsHeader == true)
                        {
                            layoutBuilder.Append(ConsoleBorderStyle.Left + " ");
                        }
                        else if (isHeaderEnd == false)
                        {
                            isHeaderEnd = true;
                            layoutBuilder.AppendLine(ConsoleBorderStyle.BottomLeft + headerTopBottom + ConsoleBorderStyle.BottomRight);
                            layoutBuilder.Append(displayPadding);
                            layoutBuilder.AppendLine(@"Fields:");
                            displayPadding += "  ";
                            layoutBuilder.Append(displayPadding);
                        }
                    }
                    else
                    {
                        if (index == 0)
                        {
                            layoutBuilder.AppendLine(@"Fields:");
                            displayPadding += "  ";
                            layoutBuilder.Append(displayPadding);
                        }
                    }

                    var offsetString  = $"[{fieldLayout.Offset}-{fieldLayout.Offset + fieldLayout.Size - 1}]";
                    var padBy         = offsetPad - offsetString.Length;
                    var line          = $"{offsetString} {new string(' ', padBy)} {fieldLayout.Type} {fieldLayout.Name}";
                    var sizeInBytesPad = "";

                    //
                    // Pad the (X bytes) string such that it's all nicley 
                    // aligned when we render multiple lines on screen.
                    //
                    if (sizePad - line.Length > 0)
                    {
                        sizeInBytesPad = new string(' ', sizePad - line.Length);
                    }
                    var sizeInBytes  = $"{sizeInBytesPad}({fieldLayout.Size} bytes)";
                    layoutBuilder.Append(line + sizeInBytes);

                    //
                    // Create the right hand size of the table ('|') with the correct pad and placement  
                    //
                    if (index >= 0 && fieldLayout.IsHeader == true)
                    {
                        var toPad = tableSize - (line.Length + sizeInBytes.Length) - 1;
                        layoutBuilder.Append(new String(' ', toPad) + ConsoleBorderStyle.Left);
                    }

                    layoutBuilder.AppendLine();

                    index++;
                }
                layoutBuilder.AppendLine($"    Size:    {typeLayout.Size} {(typeLayout.IsBoxed ? "# Estimated" : "")}");
                layoutBuilder.AppendLine($"    Padding: {typeLayout.PaddingSize} {(typeLayout.IsBoxed ? "# Estimated" : "")}");

                layoutBuilder.AppendLine("}");
                layoutBuilder.AppendLine();
            }

            return layoutBuilder.ToString();
        }

        public string ToAsm(DecompilationUnit unit)
        {
            StringBuilder builder = new StringBuilder();
            StringBuilder lineBuilder = new StringBuilder();

            builder.AppendLine();

            if (unit.TypeLayouts != null && unit.TypeLayouts.Any())
            {
                builder.AppendLine(ToLayout(unit.TypeLayouts));
                builder.AppendLine();
            }

            foreach (var method in unit.DecompiledMethods)
            {
                if (method == null) continue;

                (int jumpSize, int nestingLevel) sizeAndNesting = (-1,-1);
                sizeAndNesting = PopulateGuides(method);
                
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

                foreach (var inst in method.Instructions)
                {
                    lineBuilder.Clear();

                    var offset = pad - inst.Instruction.Length;
                    if (offset < 0) offset = 0;

                    lineBuilder.Append("  ");

                    if (unit.Options.ShowGuides)
                    {
                        AppendGuides(lineBuilder, inst, sizeAndNesting);
                    }

                    if (unit.Options.ShortAddresses)
                    {
                        lineBuilder.Append($"{inst.Address.ToString("X").Substring(unit.Options.AddressesCutByLength)}: ");
                    }
                    else
                    {
                        lineBuilder.Append($"{inst.Address.ToString("X")}: ");
                    }

                    lineBuilder.Append($"{inst.Instruction} " + new string(' ', offset));

                    int idx = 0;
                    foreach (var arg in inst.Arguments)
                    {
                        var argumentValue = CreateArgument(method.CodeAddress, method.CodeSize, inst, arg, idx == inst.Arguments.Length - 1, unit.Options);
                        lineBuilder.Append(argumentValue);
                        idx++;
                    }

                    if (unit.Options.ShowASMDocumentation)
                    {
                        AppendDocumentation(lineBuilder, method, inst, unit.Options);
                    }

                    builder.Append(lineBuilder.ToString());
                    builder.AppendLine();
                }
            }

            return builder.ToString();
        }

        private void AppendDocumentation(StringBuilder lineBuilder, DecompiledMethod method, AssemblyInstruction instruction, Core.Compilation.CompilationOptions options)
        {
            try
            {
                int lineOffset = options.ASMDocumentationOffset;
                if (lineBuilder.Length < lineOffset)
                {
                    lineBuilder.Append(' ', lineOffset - lineBuilder.Length);
                }
                if (instruction.Instruction == "mov")
                {
                    var lhs = instruction.Arguments[0].Value.Trim();
                    var rhs = instruction.Arguments[1].Value.Trim();

                    if (lhs.StartsWith("[")) { lhs = "Memory" + lhs; }
                    if (rhs.StartsWith("[")) { rhs = "Memory" + rhs; }
                    lineBuilder.Append($" # {lhs} = {rhs}");
                }
                else if (instruction.Instruction == "movsxd")
                {
                    var lhs = instruction.Arguments[0].Value.Trim();
                    var rhs = instruction.Arguments[1].Value.Trim();

                    if (lhs.StartsWith("[")) { lhs = "Memory" + lhs; }
                    if (rhs.StartsWith("[")) { rhs = "Memory" + rhs; }

                    if (lhs.StartsWith("r")) { lhs = "(64bit)" + lhs; }
                    if (rhs.StartsWith("e")) { rhs = "(32bit)" + rhs; }

                    lineBuilder.Append($" # {lhs} = {rhs}");
                }
                else if (instruction.Instruction == "lea")
                {
                    var lhs = instruction.Arguments[0].Value.Trim();
                    var rhs = instruction.Arguments[1].Value.Trim();

                    if (lhs.StartsWith("[")) { lhs = "Memory" + lhs; }
                    if (rhs.StartsWith("[")) { rhs = rhs.Replace("[", "").Replace("]", ""); }
                    lineBuilder.Append($" # {lhs} = {rhs}");
                }
                else if (instruction.Instruction == "inc")
                {
                    var lhs = instruction.Arguments[0].Value.Trim();

                    if (lhs.StartsWith("[")) { lhs = "Memory" + lhs; }
                    lineBuilder.Append($" # {lhs}++");
                }
                else if (instruction.Instruction == "call")
                {
                    var lhs = instruction.Arguments[0].Value.Trim();

                    if (lhs.StartsWith("CORINFO") && lhs.EndsWith("FAIL"))
                    {
                        lineBuilder.Append($" # throw");
                    }
                }
                else if (instruction.Instruction == "add")
                {
                    string stackInfo = "";
                    var lhs = instruction.Arguments[0].Value.Trim();
                    var rhs = instruction.Arguments[1].Value.Trim();

                    if (lhs.StartsWith("[")) { lhs = "Memory" + lhs; }
                    if (rhs.StartsWith("[")) { rhs = "Memory" + rhs; }

                    if (IsHex(rhs))
                        rhs = HexToDecimal(rhs);

                    if (lhs == "rsp")
                        stackInfo = $"stack.pop({rhs})";

                    lineBuilder.Append($" # {lhs} += {rhs} {stackInfo}");
                }
                else if (instruction.Instruction == "sub")
                {
                    string stackInfo = "";
                    var lhs = instruction.Arguments[0].Value.Trim();
                    var rhs = instruction.Arguments[1].Value.Trim();

                    if (lhs.StartsWith("[")) { lhs = "Memory" + lhs; }
                    if (rhs.StartsWith("[")) { rhs = "Memory" + rhs; }

                    if (IsHex(rhs))
                        rhs = HexToDecimal(rhs);

                    if (lhs == "rsp")
                        stackInfo = $"stack.push({rhs})";

                    lineBuilder.Append($" # {lhs} -= {rhs} {stackInfo}");
                }
                else if (instruction.Instruction == "xor")
                {
                    var lhs = instruction.Arguments[0].Value.Trim();
                    var rhs = instruction.Arguments[1].Value.Trim();

                    if (lhs.StartsWith("[")) { lhs = "Memory" + lhs; }
                    if (rhs.StartsWith("[")) { rhs = "Memory" + rhs; }

                    if (IsHex(rhs))
                        rhs = HexToDecimal(rhs);

                    if (lhs == rhs)
                        lineBuilder.Append($" # {lhs} = 0");
                    else
                        lineBuilder.Append($" # {lhs} ^= {rhs}");
                }
                else if (instruction.Instruction == "ret")
                {
                    lineBuilder.Append($" # return;");
                }
                else if (instruction.Instruction == "cmp")
                {
                    if (instruction.OrdinalIndex + 1 < method.Instructions.Count)
                    {
                        string @operator = "NA";
                        var inst = instruction;
                        var next = method.Instructions[instruction.OrdinalIndex + 1];

                        var lhs = instruction.Arguments[0].Value.Trim();
                        var rhs = instruction.Arguments[1].Value.Trim();

                        if (lhs.StartsWith("[")) { lhs = "Memory" + lhs; }
                        if (rhs.StartsWith("[")) { rhs = "Memory" + rhs; }

                        if (IsHex(rhs))
                            rhs = HexToDecimal(rhs);

                        @operator = SetOperatorForASMDocs(next);
                        lineBuilder.Append($" # if({lhs} {@operator} {rhs})");
                    }
                }
                else if (instruction.Instruction == "test")
                {
                    if (instruction.OrdinalIndex + 1 < method.Instructions.Count)
                    {
                        string @operator = "NA";
                        var inst = instruction;
                        var next = method.Instructions[instruction.OrdinalIndex + 1];
                        @operator = SetOperatorForASMDocs(next);
                        lineBuilder.Append($" # if({inst.Arguments[0].Value.Trim()} & {inst.Arguments[1].Value} {@operator} 0)");
                    }
                }
            }
            catch (Exception ex)
            {
                //
                // Ignore this error, we don't want to crash the output if documentaton
                // blows up.
                //
                // Log it to the console and move on.
                //
                XConsole.WriteLine($"'Documentation Generation Failed with message: {ex.Message}'");
            }
        }

        private bool IsHex(string value)
        { 
            if(value != null)
            {
                return char.IsDigit(value[0]) && value[value.Length - 1] == 'h';
            }
            return false;
        }

        private string HexToDecimal(string value)
        {
            var hexEssence = value.Substring(0, value.Length - 1);
            int decValue = Convert.ToInt32(hexEssence, 16);
            return decValue.ToString();
        }

        private string SetOperatorForASMDocs(AssemblyInstruction instruction)
        {
            return instruction.Instruction switch
            {
                "je" => "==",
                "jne" => "!=",
                "jl" => "<",
                "jg" => ">",
                "jle" => "<=",
                "jge" => ">=",
                //
                // Unsigned
                //
                "jae" => ">=",
                "jbe" => "<=",
                "ja" => ">",
                "jb" => "<",

                _ => "NA"
            };
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

        private string CreateArgument(ulong methodAddress, ulong codeSize, AssemblyInstruction instruction, InstructionArg arg, bool isLast, Core.Compilation.CompilationOptions options)
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
                if(options.ShortAddresses)
                    builder.Append($" {instruction.RefAddress.ToString("X").Substring(options.AddressesCutByLength)}");
                else
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

            using (var ctx = new CollectibleAssemblyLoadContext(_unsafeUseTieredCompilation == false))
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
                    var decompiledMethods  = compiledType.ToAsm(@private: true);
                    var typesMemoryLayouts = compiledType.ToLayout(@private: true);

                    RunPostCompilationOperations(loaded, compiledType, decompiledMethods);
                    HideDecompiledMethods(decompiledMethods);

                    assemblyStream.Position = 0;
                    pdbStream.Position = 0;

                    ILDecompiler iLDecompiler = new ILDecompiler();
                    unit.ILCode = iLDecompiler.ToIL(assemblyStream, pdbStream);
                    unit.DecompiledMethods = decompiledMethods;
                    unit.TypeLayouts = typesMemoryLayouts;
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
            int order = 1;

            var compiledLog = loadedAssembly.GetType("_Log");
            var instance = loadedAssembly.CreateInstance("CompilerGen");
            var methods = compiledType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            foreach (var method in methods)
            {
                if (method.Name.StartsWith("Bench_"))
                {
                    (long took, int warmUpCount, int runCount) summary = 
                        ((long, int, int))method.Invoke(instance, null);

                    var methodUnderBenchmarkName = method.Name.Split("_")[1];

                    messages.Add($"# [{order++}] ");
                    messages.Add($"# Method: {methodUnderBenchmarkName}");
                    messages.Add($"# Warm-up Count: {summary.warmUpCount} calls");
                    messages.Add($"# Took {summary.took} ms / {summary.runCount} calls");
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
                    foreach(var attribute in attributes)
                    {
                        var name = attribute.GetType().Name;

                        if (name == "RunAttribute")
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
                            order++;
                        }
                        else if(name == "HideAttribute")
                        {
                            for (int i = 0; i < decompiledMethods.Length; i++)
                            {
                                if (decompiledMethods[i].Name == method.Name)
                                {
                                    decompiledMethods[i].Instructions.Clear();
                                    break;
                                }
                            }
                        }
                    }

                }
            }
        }
    }
}
