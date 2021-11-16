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
    public class GOWatcher
    {
        private string[] jumpInstructions = new string[] {

            //
            // Conditional Jumps
            //
            "JCC",
            "JCS",
            "JCXZL",
            "JEQ",
            "JGE",
            "JGT",
            "JHI",
            "JLE",
            "JLS",
            "JLT",
            "JMI",
            "JNE",
            "JOC",
            "JOS",
            "JPC",
            "JPL",
            "JPS",
            //
            // Unconditional Jumps
            //
            "JMP"
        };
    
        private string _pathToGOCompiler;
        private IConfigurationRoot _configuration;

        public GOWatcher(IConfigurationRoot configuration)
        {
            _configuration = configuration;
        }

        private void Initialize(string goFile, string outAsmFile)
        {
            XConsole.WriteLine("GO Lang Watcher Initialize:");

            XConsole.WriteLine($"`Input File`: {goFile}");
            XConsole.WriteLine($"`ASM   File`: {outAsmFile}");

            if (File.Exists(goFile) == false)
                XConsole.WriteLine("'[WARNING]': Input File doesn't exist");

            if (File.Exists(outAsmFile) == false)
                XConsole.WriteLine("'[WARNING]': ASM File doesn't exist");

            _pathToGOCompiler = _configuration["GOCompilerPath"]; 

            if (_pathToGOCompiler.EndsWith(Path.DirectorySeparatorChar) == false)
            {
                _pathToGOCompiler += Path.DirectorySeparatorChar;
            }

            if(Directory.Exists(_pathToGOCompiler) == false)
                XConsole.WriteLine("'[WARNING]': Compiler Directory Not Found");


            XConsole.WriteLine($"`Compiler  Path`: {_pathToGOCompiler}");
        }

        public Task WatchFile(string goFile, string outAsmFile)
        {
            Initialize(goFile, outAsmFile);

            var tmpAsmFile = outAsmFile + "_tmp.asm";
            var command = $"{_pathToGOCompiler}go.exe tool compile -S {goFile} > {tmpAsmFile}";
            string lastCode = null;
            DateTime lastWrite = DateTime.MinValue;
            var iDontCareAboutThisTask = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        FileInfo fileInfo = new FileInfo(goFile);
                        if (fileInfo.LastWriteTime.Ticks > lastWrite.Ticks)
                        {
                            var code = File.ReadAllText(goFile);
                            if (string.IsNullOrEmpty(code) == false && lastCode != code)
                            {
                                XConsole.WriteLine($"Calling: {command}");

                                System.Diagnostics.Process process = new System.Diagnostics.Process();
                                System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
                                startInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                                startInfo.FileName = "cmd.exe";
                                startInfo.Arguments = $"/C {command}";
                                process.StartInfo = startInfo;
                                process.Start();
                                process.WaitForExit();

                                lastCode = code;

                                var methods = ParseASM(File.ReadAllText(tmpAsmFile));
                                var asmCode = ToAsmString(new DecompilationUnit() { DecompiledMethods = methods });

                                File.WriteAllText(outAsmFile, asmCode);

                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        XConsole.WriteLine($"Writing Errors to: {outAsmFile}");
                        //
                        // Report this back to the out files.
                        //
                        File.WriteAllText(outAsmFile, ex.ToString());
                    }

                    await Task.Delay(500);
                }
            });
            return iDontCareAboutThisTask;
        }

        public string ToAsmString(DecompilationUnit unit)
        {
            StringBuilder builder = new StringBuilder();
            StringBuilder lineBuilder = new StringBuilder();

            foreach (var method in unit.DecompiledMethods)
            {
                if (method == null) continue;

                (int jumpSize, int nestingLevel) sizeAndNesting = (-1, -1);
                sizeAndNesting = JumpGuideDetector.PopulateGuides(method);
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

                    //
                    // @TODO This is a temp solution to a tailing binary dump of the 
                    // method that we should not even parse.
                    //
                    if (inst.Instruction == null) continue;

                    var offset = pad - inst.Instruction.Length;
                    if (offset < 0) offset = 0;

                    var hexAddrSize = inst.Address.ToString("X");
                    int hexSize = 4;

                    string hexPad = new string('0', hexSize - hexAddrSize.Length);

                    lineBuilder.Append("  ");

                    AppendGuides(lineBuilder, inst, sizeAndNesting);
                    lineBuilder.Append($"{hexPad}{hexAddrSize}: ");
                    lineBuilder.Append($"{inst.Instruction} " + new string(' ', offset));

                    int idx = 0;
                    foreach (var arg in inst.Arguments)
                    {
                        var argumentValue = CreateArgument(method.CodeAddress, method.CodeSize, inst, arg, idx == inst.Arguments.Length - 1, unit.Options);
                        lineBuilder.Append(argumentValue);
                        idx++;
                    }

                    builder.Append(lineBuilder.ToString());
                    builder.AppendLine();
                }

                builder.Append(lineBuilder.ToString());
                builder.AppendLine();
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

                var valueFormatted = ulong.Parse(value.Trim()).ToString("X");
                builder.Append($"{valueFormatted}");

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

        private DecompiledMethod[] ParseASM(string asm)
        {
            //
            // GO ASM Format can be expressed by a state machine going line by line
            // We have multiple types of lines but for methods we are interested
            // in two types of lines:
            // - Header Lines
            // - Instruction Lines
            // This is a header line:
            //     "".M STEXT nosplit size=17 args=0x8 locals=0x0 funcid=0x0
            //
            // This is a instruction line:
            //     0x0006 00006 (_code.go:23)	MOVL $1, AX
            //
            // There's a special instruction called TEXT that contains
            // mostly the same info as the header line.
            // 
            // Let's parse instruction lines for now (an instruction line will always contain an address)
            // since this should give us all of the information that we need.
            //
            var lines = asm
                .Trim()
                .Split("\n");

            List<DecompiledMethod> methods = new List<DecompiledMethod>();
            DecompiledMethod decompiledMethod = null;
            int index = 0;

            for (int i = 0; i < lines.Length; i++)
            { 
                var line = lines[i];
                line = line.Trim();

                if (line.StartsWith("0x"))
                {
                    if (line.Contains("TEXT"))
                    {
                        var nameInst = ParseInstruction(line);

                        index = 0;
                        decompiledMethod = new DecompiledMethod();
                        decompiledMethod.Name = nameInst.Arguments[0].Value;
                        decompiledMethod.Arguments = new string[0];
                        methods.Add(decompiledMethod);
                    }
                    else
                    {
                        var inst = ParseInstruction(line);
                        inst.OrdinalIndex = index;
                        index++;
                        decompiledMethod.Instructions.Add(inst);
                        //
                        // @TODO this has off by X errors.
                        // We need to parse header lines and read the correct
                        // value(s) from there. Remove as soon as more pressing issues
                        // are dealth with.
                        //
                        var size = (uint)inst.Address;
                        if(decompiledMethod.CodeSize < size)
                            decompiledMethod.CodeSize = size;

                    }
                }
            }

            return methods.ToArray();
        }


        private AssemblyInstruction ParseInstruction(string line)
        {
            XConsoleTokenizer tokenizer = new XConsoleTokenizer();
            var tokens = tokenizer.Tokenize(line);
            //
            // Instruction Layout:
            // (Whitespaces and tabs are skiped in this diagram but they are tokenized
            // and should be handeled).
            //
            //  
            //     [0]        [2]    [4]     [5]      [6]     [8]
            // "Address0" "Address1" "(" "PathToFile" ")" "Instruction" "Arg1", "ArgX"
            // 
            // All of the index offsets arent guaranteed except the first two, but what we
            // can expect that after address tokens we should expect "(" at some point and skip 
            // this whole section, and after this section we should expect an instruction followed
            // by a list of arguments.
            //
            // An argument can have operators, parens, numbers, strings.
            // Arguments in the list will be seperated by a comma ',' 
            //
            
            AssemblyInstruction instruction = new AssemblyInstruction();
            instruction.Address = HexToDecimal(tokens[0].GetValue());
            //
            // The OpCode should be after ")"
            //
            int argsIndex = -1;
            for(int i = 3; i < tokens.Count; i++)
            {
                if(tokens[i] is BracketCloseToken && i + 1 < tokens.Count)
                {
                    while(PeekNext(tokens, i, out var next))
                    {
                        if(next is WordToken word)
                        {
                            instruction.Instruction = tokens[i + 1].GetValue();
                            //
                            // Skip over whitespace after the instruction.
                            //
                            argsIndex = i + 3;
                            break;
                        }
                        i++;
                    }
                    break;
                }
            }

            List<InstructionArg> instructionArgs = new List<InstructionArg>();

            if (argsIndex != -1 && argsIndex < tokens.Count)
            {
                InstructionArg instructionArg = new InstructionArg();

                for (; argsIndex < tokens.Count; argsIndex++)
                {
                    if (tokens[argsIndex] is SeparatorToken)
                    {
                        instructionArgs.Add(instructionArg);
                        instructionArg = new InstructionArg();
                    }
                    else
                    {
                        instructionArg.Value += tokens[argsIndex].GetValue();
                        if (jumpInstructions.Contains(instruction.Instruction))
                        {
                            instruction.RefAddress = ulong.Parse(instructionArg.Value); 
                            instructionArg.HasReferenceAddress = true;
                        }
                    }
                }

                instructionArgs.Add(instructionArg);
            }

            instruction.Arguments = instructionArgs.ToArray();
            return instruction;
        }

        private ulong HexToDecimal(string value)
        {
            var hexEssence = value.Substring(0, value.Length);
            return Convert.ToUInt64(hexEssence, 16);
        }

        private bool PeekNext(List<Token> tokens, int i, out Token next)
        {
            next = null;
            if (i + 1 < tokens.Count)
            {
                next = tokens[i + 1];
                return true;
            }
            return false;
        }
    }


}
