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
using CompilationOptions = PowerUp.Core.Compilation.CompilationOptions;

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
    
        private string _pathToCompiler;
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

            _pathToCompiler = _configuration["GOCompilerPath"]; 

            if (_pathToCompiler.EndsWith(Path.DirectorySeparatorChar) == false)
            {
                _pathToCompiler += Path.DirectorySeparatorChar;
            }

            if(Directory.Exists(_pathToCompiler) == false)
                XConsole.WriteLine("'[WARNING]': Compiler Directory Not Found");


            XConsole.WriteLine($"`Compiler  Path`: {_pathToCompiler}");
        }

        public Task WatchFile(string goFile, string outAsmFile)
        {
            Initialize(goFile, outAsmFile);

            var tmpAsmFile = outAsmFile + "_tmp.asm";
            var command = $"\"{_pathToCompiler}go.exe\" tool compile -S {goFile} > {tmpAsmFile}";
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
                                lastCode = code;
                                XConsole.WriteLine($"Calling: {command}");

                                var messages = WatcherUtils.StartCompilerProcess(command);
                                var options = WatcherUtils.ProcessCommandOptions(code);
                                var methods = ParseASM(File.ReadAllText(tmpAsmFile), code);

                                if(messages.messages != null)
                                {
                                    foreach (var message in messages.messages)
                                    {
                                        XConsole.WriteLine($"[GO] {message}");
                                    }
                                }

                                var unit = new DecompilationUnit() 
                                { 
                                    DecompiledMethods = methods, 
                                    Options = options 
                                };

                                var asmCode = ToAsmString(unit, outAsmFile);

                                //File.WriteAllText(outAsmFile, asmCode);

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

        public string ToAsmString(DecompilationUnit unit, string outAsmFile)
        {
            var builder     = new OutputBuilder(outAsmFile);
            var lineBuilder = new StringBuilder();
            var writer      = new AssemblyWriter();

            if (unit.Options.ShowHelp)
            {
                writer.AppendHelp(builder);
                return builder.ToString();
            }

            //
            // Diff selected methods.
            //
            if (unit.Options.Diff)
            {
                (var source, var target) = WatcherUtils.FindDiffMethods(unit);

                if (source != null && target != null)
                {
                    writer.DocumentationOffset = unit.Options.ASMDocumentationOffset;
                    writer.AppendDiff(builder, source, target, unit.Options.ShowASMDocumentation);
                }
            }

            foreach (var method in unit.DecompiledMethods)
            {
                if (method == null) continue;

                (int jumpSize, int nestingLevel) sizeAndNesting = (-1, -1);
                sizeAndNesting = JumpGuideDetector.PopulateGuides(method);
                //
                // Print messages.
                //
                writer.AppendMessages(builder, method);
                //
                // Write out method signature.
                //
                writer.AppendMethodSignature(builder, method);

                foreach (var inst in method.Instructions)
                {
                    lineBuilder.Clear();
                    //
                    // @TODO This is a temp solution to a tailing binary dump of the 
                    // method that we should not even parse.
                    //
                    if (inst.Instruction == null) continue;
                    if (inst.Type == InstructionType.Code && unit.Options.ShowSourceMaps == false) continue;

                    lineBuilder.Append("  ");
                    //
                    // Append Jump Guides if needed.
                    //
                    if (unit.Options.ShowGuides)
                    {
                        writer.AppendGuides(lineBuilder, inst, sizeAndNesting);
                    }
                    //
                    // If the option to cut addresses was selected we should set the cut length
                    // before we write out the address using this writer.
                    //
                    if (unit.Options.ShortAddresses)
                        writer.AddressCutBy = unit.Options.AddressesCutByLength;
                    //
                    // Write out the address as a hex padded string.
                    //
                    writer.AppendInstructionAddress(lineBuilder, inst, zeroPad: true);
                    writer.AppendInstructionName   (lineBuilder, inst);

                    int idx = 0;
                    foreach (var arg in inst.Arguments)
                    {
                        var isLast = idx == inst.Arguments.Length - 1;
                        writer.AppendArgument(lineBuilder, method, inst, arg, isLast);
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
                if (instruction.Instruction == "MOVQ")
                {
                    var lhs = instruction.Arguments[0].Value.Trim();
                    var rhs = instruction.Arguments[1].Value.Trim();

                    if (lhs.StartsWith("(")) { lhs = "Memory" + lhs; }
                    if (rhs.StartsWith("(")) { rhs = "Memory" + rhs; }
                    lineBuilder.Append($" # {rhs} = {lhs}");
                }
                else if (instruction.Instruction == "LEAQ")
                {
                    var lhs = instruction.Arguments[0].Value.Trim();
                    var rhs = instruction.Arguments[1].Value.Trim();

                    if (rhs.StartsWith("(")) { rhs = "Memory" + rhs; }
                    if (lhs.StartsWith("(")) { lhs = lhs.Replace("(", "").Replace(")", ""); }
                    lineBuilder.Append($" # {rhs} = {lhs}");
                }
                else if (instruction.Instruction == "INCQ")
                {
                    var lhs = instruction.Arguments[0].Value.Trim();

                    if (lhs.StartsWith("(")) { lhs = "Memory" + lhs; }
                    lineBuilder.Append($" # {lhs}++");
                }
                else if (instruction.Instruction == "ADDQ")
                {
                    string stackInfo = "";
                    var lhs = instruction.Arguments[0].Value.Trim();
                    var rhs = instruction.Arguments[1].Value.Trim();

                    if (lhs.StartsWith("(")) { lhs = "Memory" + lhs; }
                    if (rhs.StartsWith("(")) { rhs = "Memory" + rhs; }

                    if (rhs == "SP")
                        stackInfo = $"stack.pop({lhs})";

                    lineBuilder.Append($" # {rhs} += {lhs} {stackInfo}");
                }
                else if (instruction.Instruction == "SUBQ")
                {
                    string stackInfo = "";
                    var lhs = instruction.Arguments[0].Value.Trim();
                    var rhs = instruction.Arguments[1].Value.Trim();

                    if (lhs.StartsWith("(")) { lhs = "Memory" + lhs; }
                    if (rhs.StartsWith("(")) { rhs = "Memory" + rhs; }

                    if (rhs == "SP")
                        stackInfo = $"stack.push({lhs})";

                    lineBuilder.Append($" # {rhs} -= {lhs} {stackInfo}");
                }
                else if (instruction.Instruction == "XORL")
                {
                    var lhs = instruction.Arguments[0].Value.Trim();
                    var rhs = instruction.Arguments[1].Value.Trim();

                    if (lhs.StartsWith("(")) { lhs = "Memory" + lhs; }
                    if (rhs.StartsWith("(")) { rhs = "Memory" + rhs; }

                    if (lhs == rhs)
                        lineBuilder.Append($" # {lhs} = 0");
                    else
                        lineBuilder.Append($" # {lhs} ^= {rhs}");
                }
                else if (instruction.Instruction == "RET")
                {
                    lineBuilder.Append($" # return");
                }
                else if (instruction.Instruction == "CMPQ")
                {
                    if (instruction.OrdinalIndex + 1 < method.Instructions.Count)
                    {
                        string @operator = "NA";
                        var inst = instruction;
                        var next = method.Instructions[instruction.OrdinalIndex + 1];

                        var lhs = instruction.Arguments[0].Value.Trim();
                        var rhs = instruction.Arguments[1].Value.Trim();

                        if (lhs.StartsWith("(")) { lhs = "Memory" + lhs; }
                        if (rhs.StartsWith("(")) { rhs = "Memory" + rhs; }

                        @operator = SetOperatorForASMDocs(next);
                        lineBuilder.Append($" # if({rhs} {@operator} {lhs})");
                    }
                }
                else if (jumpInstructions.Contains(instruction.Instruction))
                {
                    var prev = method.Instructions[instruction.OrdinalIndex - 1];
                    var guide = prev.Instruction == "CMPQ" ? XConsole.ConsoleBorderStyle.BottomLeft.ToString() + "> " : "";
                    var lhs = instruction.RefAddress.ToString("X");
                    lineBuilder.Append($" # {guide}goto {lhs}");
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

        private string SetOperatorForASMDocs(AssemblyInstruction instruction)
        {
            return instruction.Instruction switch
            {
                "JEQ" => "==",
                "JNE" => "!=",
                "JLT" => "<",
                "JGT" => ">",
                "JLE" => "<=",
                "JGE" => ">=",
                //
                // Unsigned
                //
                "JCC" => ">=",
                "JLS" => "<=",
                "JHI" => ">",
                "JCS" => "<",

                _ => "NA"
            };
        }

        private string HexStrToDecimalStr(string value)
        {
            int decValue = Convert.ToInt32(value, 16);
            return decValue.ToString();
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

        private DecompiledMethod[] ParseASM(string asm, string sourceCode)
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
            //
            var lines = asm
                .Trim()
                .Split("\n");

            var sourceCodeLines = sourceCode.Split("\n");

            List<DecompiledMethod> methods = new List<DecompiledMethod>();
            DecompiledMethod decompiledMethod = null;
            int index = 0;
            int lastSourceLine = -1;

            for (int i = 0; i < lines.Length; i++)
            { 
                var line = lines[i];
                line = line.Trim();

                if (line.StartsWith("0x"))
                {
                    if (line.Contains("TEXT"))
                    {
                        var nameInst = ParseInstruction(line, out var sourceCodeLine);

                        index = 0;
                        decompiledMethod = new DecompiledMethod();

                        //
                        // Clean method name:
                        // - Remove: "".
                        // - Remove: (SB)
                        //
                        var name = nameInst.Arguments[0].Value;
                        if(name != null)
                        {
                            if (name.StartsWith("\"\"."))
                                name = name.Substring(3);

                            var parrenIndex = name.IndexOf('(');
                            if (parrenIndex >= 0)
                                name = name.Substring(0, parrenIndex);
                        }

                        decompiledMethod.Name = name;

                        decompiledMethod.Arguments = new string[0];
                        methods.Add(decompiledMethod);
                    }
                    else
                    {
                        var inst = ParseInstruction(line, out var sourceCodeLine);
                        //
                        // Check if the instruction has an source code line map.
                        // If it has then create a source map intruction with the
                        // instrution value pointing back to source code.
                        //
                        // Multiple instructions can map to the same code line
                        // like for example this if statement:
                        //     0000: CMPQ   AX, $1        
                        //     0004: JNE C ⇣    
                        //
                        // We only want to display a single source line, so we shall keep
                        // the lastSourceLineNumber and only emit the source code instruction
                        // if we haven't already emited it.
                        //
                        if (sourceCodeLine != -1 && lastSourceLine != sourceCodeLine)
                        {
                            AssemblyInstruction sourceCodeInst = new AssemblyInstruction();
                            sourceCodeInst.Type = InstructionType.Code;
                            sourceCodeInst.Instruction = $"{sourceCodeLines[sourceCodeLine - 1].Trim()}";
                            sourceCodeInst.OrdinalIndex = index;
                            sourceCodeInst.Arguments = new InstructionArg[0];
                            decompiledMethod.Instructions.Add(sourceCodeInst);
                            index++;

                            lastSourceLine = sourceCodeLine;
                        }

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


        private AssemblyInstruction ParseInstruction(string line, out int sourceCodeLine)
        {
            sourceCodeLine = -1;
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
                //
                // Look for bracket close [6], the instruction and it's arguments fall 
                // right after this token.
                //
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
                else if(tokens[i] is BracketOpenToken && i + 1 < tokens.Count)
                {
                    //
                    // Should be a Source Code Map:
                    //
                    // The next token should contain a source map.
                    // Since not all lines contain such a map and some do contain it but it's
                    // not something that we can use ((<autogenerated>:1), we have to take 
                    // extra care when parsing this next token.
                    //
                    if (PeekNext(tokens, i, out var next))
                    {
                        var value = next.GetValue();
                        var last  = value.Split(":")[^1];
                        //
                        // Take the last element and this will be our source line number.
                        //
                        if(int.TryParse(last, out var lineNumber))
                        {
                            sourceCodeLine = lineNumber;
                        }
                    }
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
                        instructionArg.Value    += tokens[argsIndex].GetValue();
                        instructionArg.AltValue += tokens[argsIndex].GetValue();

                        if (jumpInstructions.Contains(instruction.Instruction))
                        {
                            instruction.RefAddress = ulong.Parse(instructionArg.Value); 
                            instructionArg.HasReferenceAddress = true;
                            instructionArg.Value = "";
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
