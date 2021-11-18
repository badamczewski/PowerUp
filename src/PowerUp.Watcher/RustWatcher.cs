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
            var command = $"{_pathToCompiler}rustc.exe {inputFile} -o {tmpAsmFile} --emit asm --crate-type rlib -Cllvm-args=--x86-asm-syntax=intel -C opt-level=3";
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

                                var options = ProcessCommandOptions(code);
                                var methods = ParseASM(
                                    Path.GetFileNameWithoutExtension(inputFile), 
                                    File.ReadAllText(tmpAsmFile));

                                var unit = new DecompilationUnit()
                                {
                                    DecompiledMethods = methods,
                                    Options = options
                                };

                                var asmCode = ToAsmString(unit);

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
            var builder     = new StringBuilder();
            var lineBuilder = new StringBuilder();
            var writer      = new AssemblyWriter();

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
                    writer.AppendInstructionName(lineBuilder, inst);

                    int idx = 0;
                    foreach (var arg in inst.Arguments)
                    {
                        var argumentValue = CreateArgument(method.CodeAddress, method.CodeSize, inst, arg, idx == inst.Arguments.Length - 1, unit.Options);
                        lineBuilder.Append(argumentValue);
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

                var valueTrim = value.Trim();
                if (ulong.TryParse(valueTrim, out var valueFormatted))
                {
                    builder.Append($"{valueFormatted.ToString("X")}");
                }
                else
                {
                    //
                    // If we failed to parse the arg value as ulong
                    // then this simply means that we are dealing with some kind of
                    // label.
                    //
                    builder.Append($"{valueTrim}");
                }

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

        private CompilationOptions ProcessCommandOptions(string asm)
        {
            CompilationOptions options = new CompilationOptions();
            if (asm.IndexOf("//up:showGuides") != -1)
            {
                options.ShowGuides = true;
            }

            if (asm.IndexOf("//up:showASMDocs") != -1)
            {
                options.ShowASMDocumentation = true;
            }

            return options;
        }

        private DecompiledMethod[] ParseASM(string fileName, string asm)
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
            var lines = asm
                .Trim()
                .Split("\n");

            Dictionary<string,ulong> labels = new Dictionary<string, ulong>();
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
                else if (decompiledMethod != null)
                {
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
                        instructionArg.Value += token.GetValue();
                        if(AssemblyWriter.JumpInstructions.Contains(instruction.Instruction))
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
