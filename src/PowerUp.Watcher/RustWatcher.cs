using Microsoft.Extensions.Configuration;
using PowerUp.Core.Compilation;
using PowerUp.Core.Console;
using PowerUp.Core.Decompilation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PowerUp.Watcher
{
    public class RustWatcher
    {
        private string _pathToCompiler;
        private IConfigurationRoot _configuration;
        public RustWatcher(IConfigurationRoot configuration)
        {
            _configuration = configuration;
        }

        private void Initialize(string inputFile, string outAsmFile)
        {
            XConsole.WriteLine("Rust Lang Watcher Initialize:");

            XConsole.WriteLine($"`Input File`: {inputFile}");
            XConsole.WriteLine($"`ASM   File`: {outAsmFile}");

            if (File.Exists(inputFile) == false)
                XConsole.WriteLine("'[WARNING]': Input File doesn't exist");

            if (File.Exists(outAsmFile) == false)
                XConsole.WriteLine("'[WARNING]': ASM File doesn't exist");

            _pathToCompiler = _configuration["RustCompilerPath"];

            if (_pathToCompiler.EndsWith(Path.DirectorySeparatorChar) == false)
            {
                _pathToCompiler += Path.DirectorySeparatorChar;
            }

            if (Directory.Exists(_pathToCompiler) == false)
                XConsole.WriteLine("'[WARNING]': Compiler Directory Not Found");

            XConsole.WriteLine($"`Compiler  Path`: {_pathToCompiler}");
        }

        public Task WatchFile(string inputFile, string outAsmFile)
        {
            Initialize(inputFile, outAsmFile);

            var tmpAsmFile = outAsmFile + "_tmp.asm";
            var command = $"\"{_pathToCompiler}rustc.exe\" {inputFile} -o {tmpAsmFile} " +
                "-C debuginfo=1 " +
                $"--emit asm --crate-type rlib -Cllvm-args=--x86-asm-syntax=intel";

            string lastCode = null;
            DateTime lastWrite = DateTime.MinValue;
            var iDontCareAboutThisTask = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        FileInfo fileInfo = new FileInfo(inputFile);
                        if (fileInfo.LastWriteTime.Ticks > lastWrite.Ticks)
                        {
                            var code = File.ReadAllText(inputFile);
                            if (string.IsNullOrEmpty(code) == false && lastCode != code)
                            {
                                lastCode = code;

                                var options = WatcherUtils.ProcessCommandOptions(code);
                                var commandToCall = command;

                                if (options.OptimizationLevel > 0)
                                    commandToCall += $" -C opt-level={options.OptimizationLevel}";

                                XConsole.WriteLine($"Calling: {commandToCall}");
                                var info = WatcherUtils.StartCompilerProcess(commandToCall, errorPattern: "error");


                                if (info.errors.Any() == true)
                                {
                                    StringBuilder errorBuilder = new StringBuilder();
                                    errorBuilder.AppendLine(String.Join(Environment.NewLine, info.errors));
                                    errorBuilder.AppendLine(String.Join(Environment.NewLine, info.messages));
                                    File.WriteAllText(outAsmFile, errorBuilder.ToString());
                                }
                                else
                                {
                                    var methods = ParseASM(
                                        Path.GetFileNameWithoutExtension(inputFile),
                                        File.ReadAllText(tmpAsmFile),
                                        code);

                                    var unit = new DecompilationUnit()
                                    {
                                        Messages = info.messages,
                                        DecompiledMethods = methods,
                                        Options = options
                                    };

                                    var asmCode = ToAsmString(unit);
                                    File.WriteAllText(outAsmFile, asmCode);
                                }
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
            var builder     = new StringBuilder();
            var lineBuilder = new StringBuilder();
            var writer      = new AssemblyWriter(new AsmCodeFlowAnalyser());

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
                    // If this is a source map code line then obviously let's not include the address.
                    //
                    writer.AppendInstructionAddress(lineBuilder, inst, zeroPad: true);
                    writer.AppendInstructionName(lineBuilder, inst);
                    
                    int idx = 0;
                    foreach (var arg in inst.Arguments)
                    {
                        var isLast = idx == inst.Arguments.Length - 1;
                        writer.AppendArgument(lineBuilder, method, inst, arg, isLast);
                        idx++;
                    }

                    if (unit.Options.ShowASMDocumentation)
                    {
                        writer.DocumentationOffset = unit.Options.ASMDocumentationOffset;
                        writer.AppendX86Documentation(lineBuilder, method, inst);
                    }

                    builder.Append(lineBuilder.ToString());
                    builder.AppendLine();
                }
            }

            return builder.ToString();
        }

        private DecompiledMethod[] ParseASM(string fileName, string asm, string code)
        {
            //
            // Functions in rust asm have a simple way of detecting them,
            // This is a function:
            //   	.def	 _ZN5_code5test217h8b07b3430a90b6d8E;
            //  	.scl    2;
            //  	.type   32;
            //  	.endef
            //      .section.text,"xr",one_only,_ZN5_code5test217h8b07b3430a90b6d8E
            //  .globl _ZN5_code5test217h8b07b3430a90b6d8E
            //  	.p2align    4, 0x90
            //  _ZN5_code5test217h8b07b3430a90b6d8E:
            //      mov eax, ecx
            //      imul eax, ecx
            //      imul eax, ecx
            //      ret
            //
            // each function will contain some field metadata and a rather cryptic label like:
            //
            //     _ZN8testtest4test17hcb234a4e4ec86ae3E
            //
            // This label contains all of the data that we need to extract:
            //
            //     ZN[X]{filename}[FUNCTION_LEN]{functionname}[ID_THAT_WE_DONT_CARE_ABOUT]
            // 
            // This is a rather simple pattern to extract and we are only interested in the function name.
            // 
            // We shall skip everything else and parse instructions only to a new empty line.
            //

            var codeLines = code.Split("\n");

            var lines = asm
                .Trim()
                .Split("\n");

            Dictionary<string,ulong> labels  = new Dictionary<string, ulong>();
            List<DecompiledMethod>   methods = new List<DecompiledMethod>();
            DecompiledMethod decompiledMethod = null;
            int index = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                line = line.Trim();

                if (line.StartsWith("_ZN"))
                {
                    var methodName = ParseMethodHeader(fileName, line);
                    if (methodName != null)
                    {
                        decompiledMethod = new DecompiledMethod();
                        decompiledMethod.Name = methodName;
                        decompiledMethod.Arguments = new string[0];
                        methods.Add(decompiledMethod);
                        index = 0;
                    }
                }
                else if (line == "")
                {
                    SetJumpsBasedOnLabels(decompiledMethod, labels);
                    decompiledMethod = null;
                }
                else if(line.StartsWith(".cv_loc"))
                {
                    //
                    // The label called .cv_loc is a source map instruction.
                    //
                    // These lables are not as usefull as you might think
                    // since they don't map well to rust code when we compile with optimizations
                    // and they provide minimal support, but regardless if we have them in the asm,
                    // then we should use them.
                    //
                    // the cv label has four arguments:
                    //    .cv_loc 0 1 16 0
                    // 
                    // 1. Func ID
                    // 2. File ID
                    // 3. Line in the Source File
                    // 4. Position in the Source File
                    //
                    // We are mostly interested in argument (3), lets collect this into a source map
                    // which pulls the source code from that line and inserts it instead of this instruction.
                    // 
                    // @NOTE: This is a temprary solution, but in order to not destroy the jump guides
                    // we shall put the source code as an instruction.
                    //

                    //
                    // Get only the third argument
                    //
                    var sourceCodeLine = GetSourceLineArg(line);

                    if (sourceCodeLine < codeLines.Length)
                    {
                        var codeLine = codeLines[sourceCodeLine - 1];
                        var instruction = new AssemblyInstruction();
                        instruction.Instruction = codeLine.Trim();
                        instruction.Arguments = new InstructionArg[0];
                        instruction.Type = InstructionType.Code;
                        decompiledMethod.Instructions.Add(instruction);
                        decompiledMethod.CodeSize = (uint)index;
                        index++;
                    }

                }
                else if (decompiledMethod != null)
                {
                    //
                    // The instruction is a technical label, skip it.
                    // JUMP Labels start with "LB"
                    //
                    if(line.StartsWith(".") && line.StartsWith(".LB") == false)
                    {
                        continue;
                    }

                    var nameInst = ParseInstruction((ulong)index, line);
                    nameInst.OrdinalIndex = index;
                    decompiledMethod.Instructions.Add(nameInst);
                    decompiledMethod.CodeSize = (uint)index;
                    index++;

                    if(nameInst.Instruction.StartsWith(".") && nameInst.Instruction.EndsWith(":"))
                    {
                        labels.Add(nameInst.Instruction, nameInst.Address);
                    }
                }
            }

            SetJumpsBasedOnLabels(decompiledMethod, labels);
            return methods.ToArray();
        }

        private int GetSourceLineArg(string line)
        {
            int argIndex   = 2;
            int startIndex = -1;
            int index      = 0;
            for (; index < line.Length; index++)
            {
                if (line[index] == ' ')
                {
                    argIndex--;
                    if (argIndex == 0 && startIndex == -1)
                        startIndex = index;

                    if (argIndex < 0)
                        break;
                }
            }

            if(startIndex == -1)
                return -1;

            return int.Parse(line.Substring(startIndex + 1, index - startIndex - 1));
        }

        private void SetJumpsBasedOnLabels(DecompiledMethod decompiledMethod, Dictionary<string, ulong> labels)
        {
            if (decompiledMethod == null) return;
            //
            // Before we move to another method let's first reference all jumps to labels
            //
            foreach (var inst in decompiledMethod.Instructions)
            {
                if (inst.jumpDirection == JumpDirection.Label)
                {
                    if (labels.TryGetValue(inst.Arguments[0].Value + ":", out var addr))
                    {
                        inst.RefAddress = addr;
                    }
                }
            }
        }

        private AssemblyInstruction ParseInstruction(ulong addr, string line)
        {
            XConsoleTokenizer tokenizer = new XConsoleTokenizer();
            var tokens = tokenizer.Tokenize(line);

            //
            // The first token is the instruction, the rest is the arguments
            // separated by a comma ','
            //
            AssemblyInstruction instruction = new AssemblyInstruction();
            //
            // Use fake adresses for now but in the future we should have an instruction table
            // with all of the sizes.
            //
            instruction.Address = addr;
            instruction.Instruction = tokens[0].GetValue();

            List<InstructionArg> args = new List<InstructionArg>();
            InstructionArg instructionArg = new InstructionArg();

            if (tokens.Count > 1)
            {
                for (int i = 2; i < tokens.Count; i++)
                {
                    var token = tokens[i];
                    if (token is SeparatorToken)
                    {
                        args.Add(instructionArg);
                        instructionArg = new InstructionArg();
                    }
                    else
                    {
                        instructionArg.Value    += token.GetValue();
                        instructionArg.AltValue += token.GetValue();

                        if (AssemblyWriter.JumpInstructions.Contains(instruction.Instruction))
                        {
                            //
                            // Label jump means that there is a jump but we don't
                            // yet know in which direction we will make the jump.
                            // 
                            // This happens if we are parsing instructions down and
                            // instread of addrreses they use labels.
                            //
                            instruction.jumpDirection = JumpDirection.Label;
                            instructionArg.HasReferenceAddress = true;
                        }
                    }
                }
                args.Add(instructionArg);
            }
            instruction.Arguments = args.ToArray();

            return instruction;
        }

        private string ParseMethodHeader(string fileName, string header)
        {
            string methodName = null;
            //
            // Header:
            //
            // ZN[X]{filename}[FUNCTION_LEN]{functionname}[ID_THAT_WE_DONT_CARE_ABOUT]
            //

            // 
            // 1. Find the file name
            //
            var fnIndex = header.IndexOf(fileName);
            if (fnIndex != -1)
            {
                //
                // 2. Skip over the length
                //
                var lenOfLength = fileName.Length.ToString().Length;
                //
                // 3. Get the function name
                //
                int len = 0;
                var startIndex = fnIndex + fileName.Length + lenOfLength;
                for (int i = startIndex; i < header.Length; i++)
                {
                    var c = header[i];
                    if (!char.IsLetter(c) && !char.IsPunctuation(c) && !char.IsSymbol(c))
                    {
                        break;
                    }
                    len++;
                }
                methodName = header.Substring(startIndex, len);
            }
            return methodName;
        }

    }
}
