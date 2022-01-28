using PowerUp.Core.Compilation;
using PowerUp.Core.Console;
using PowerUp.Core.Decompilation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static PowerUp.Core.Console.XConsole;

namespace PowerUp.Watcher
{
    //
    // This class writes out common formatting
    // that all compilers tend to use for display,
    // to stay consistent, everything else is compiler specific
    // and needs special handling each time.
    //
    public class AssemblyWriter
    {
        //
        // This array contains all of the jump instructions for X86 ISA
        //
        public static string[] JumpInstructions = new string[]
        {
            "jae",
            "jb",
            "jecxz",
            "je",
            "jz",
            "jge",
            "jg",
            "ja",
            "jle",
            "jbe",
            "jl",
            "js",
            "jne",
            "jnz",
            "jno",
            "jo",
            "jnp",
            "jpo",
            "jns",
            "jp",
            "jpe",
            "jmp"
        };
        public int InstructionPad { get; set; } = 6;
        public int AddressPad { get; set; } = 4;
        public int AddressCutBy { get; set; } = 0;
        public int DocumentationOffset { get; set; } = 40;

        public void AppendHelp(StringBuilder helpBuilder)
        {
            var sep = new string(XConsole.ConsoleBorderStyle.TopBottom, 20);
            helpBuilder.AppendLine("# Help:");
            foreach (var cmd in WatcherUtils.upCommands)
            {
                helpBuilder.AppendLine($"#   //{cmd.Name}");
                helpBuilder.AppendLine($"#   {XConsole.ConsoleBorderStyle.BottomLeft}> {cmd.Description}");
                if (cmd.Args.Length > 0)
                {
                    helpBuilder.AppendLine("#   Optional Arguments:");
                    foreach (var arg in cmd.Args)
                        helpBuilder.AppendLine($"#     {arg} = X");
                }
                helpBuilder.AppendLine($"#   " + sep);
                helpBuilder.AppendLine();
            }
        }

        public void AppendInstructionAddress(StringBuilder lineBuilder, AssemblyInstruction inst, bool zeroPad = true)
        {
            //
            // Do nothing with code.
            //
            if (inst.IsCode) return;

            var address   = inst.Address.ToString("X");
            int cutBy     = AddressCutBy;
            string hexPad = null;
            if (zeroPad)
            {
                var hexSize = AddressPad;
                hexPad = new string('0', hexSize - address.Length);
            }
            //
            // The option to shorten addresses was selected but we cannot trim them to len < 0
            //
            if (address.Length < cutBy)
                lineBuilder.Append($"{hexPad} ");
            else if (cutBy > 0)
                lineBuilder.Append($"{hexPad}{address.Substring(cutBy)}: ");
            else
                lineBuilder.Append($"{hexPad}{address}: ");
        }
        public void AppendInstructionName(StringBuilder lineBuilder, AssemblyInstruction inst)
        {
            var offset = InstructionPad - inst.Instruction.Length;
            if (offset < 0) offset = 0;
            lineBuilder.Append($"{(inst.IsCode ? "# " : "")}{inst.Instruction} " + new string(' ', offset));
        }
        public void AppendMethodSignature(StringBuilder methodBuilder, DecompiledMethod method)
        {
            methodBuilder.AppendLine($"# Instruction Count: {method.Instructions.Count}; Code Size: {method.CodeSize}");
            methodBuilder.Append($"{(method.TypeName == null ? "" : method.TypeName + "+")}{method.Return} {method.Name}(");

            for (int i = 0; i < method.Arguments.Length; i++)
            {
                methodBuilder.Append($"{method.Arguments[i]}");

                if (i != method.Arguments.Length - 1)
                {
                    methodBuilder.Append(", ");
                }
            }

            methodBuilder.AppendLine("):");
        }

        public void AppendArgument(StringBuilder lineBuilder, DecompiledMethod method, AssemblyInstruction instruction, InstructionArg arg, bool isLast, Core.Compilation.CompilationOptions options)
        {
            //
            // Check if the instruction was a jump, since jumps need a very special handling,
            // that is slightly different per compiler (but we managed to come up with a sensible default)
            //
            if (instruction.jumpDirection != JumpDirection.None)
            {
                //
                // Try to separate the value from the address or anything else that might be there.
                //
                var addressOrAnyInArg = arg.Value.LastIndexOf(' ');
                var value = arg.Value;
                if (addressOrAnyInArg != -1)
                {
                    value = arg.Value.Substring(0, addressOrAnyInArg);
                }

                //
                // The argument is a jump and since we support many flavors of jumps (from many languages, and compilers),
                // the argument value will most likley be eiter a lablel (LB0001) or reference to a call (THROW_HELPER).
                // The correct address will be stored in the RefAddress.
                //
                // For compilers like Rust (and LLVM) there will be no RefAdress but the label will fill the same function.
                //
                lineBuilder.Append($"{value.Trim()}");
                if (instruction.RefAddress > 0)
                {
                    var addressValue = instruction.RefAddress.ToString("X");
                    //
                    // The option to shorten addresses was selected but we cannot trim them to len < 0
                    //
                    if (options.ShortAddresses)
                    {
                        var cutBy = options.AddressesCutByLength;
                        if (addressValue.Length < options.AddressesCutByLength)
                            cutBy = addressValue.Length;

                        lineBuilder.Append($" {addressValue.Substring(options.AddressesCutByLength)}");
                    }
                    else
                        lineBuilder.Append($" {addressValue}");
                }

                //
                // Render jump direction guides.
                //
                if (instruction.jumpDirection == JumpDirection.Out)
                    lineBuilder.Append($" ↷");
                else if (instruction.jumpDirection == JumpDirection.Up)
                    lineBuilder.Append($" ⇡");
                else if (instruction.jumpDirection == JumpDirection.Down)
                    lineBuilder.Append($" ⇣");
            }
            else
            {
                //
                // The instruction wasn't a jump so this will be a standard value
                // meaning that it will be an const, register, operator, or array access
                //
                var value = arg.Value.Trim();
                var code = string.Empty;
                for (int i = 0; i < value.Length; i++)
                {
                    var c = value[i];
                    if (c == ']' || c == '[' || c == '+' || c == '-' || c == '*')
                    {
                        if (string.IsNullOrEmpty(code) == false)
                        {
                            lineBuilder.Append($"{code}");
                            code = string.Empty;
                        }

                        lineBuilder.Append($"{c}");
                    }
                    else
                    {
                        code += c;
                    }
                }
                if (string.IsNullOrEmpty(code) == false)
                {
                    lineBuilder.Append($"{code}");
                }
            }

            if (isLast == false)
            {
                lineBuilder.Append($", ");
            }
        }

        public void AppendMessages(StringBuilder methodBuilder, DecompiledMethod method)
        {
            //
            // Print messages.
            //
            if (method.Messages != null && method.Messages.Count > 0)
            {
                methodBuilder.AppendLine(
                    Environment.NewLine +
                    string.Join(Environment.NewLine, method.Messages));
            }
        }
        public void AppendGuides(StringBuilder methodBuilder, AssemblyInstruction inst, (int jumpSize, int nestingLevel) sizeAndNesting)
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
                    if (wsCount > 0) methodBuilder.Append(new String(' ', wsCount));
                    wsCount = 0;
                    methodBuilder.Append((char)block);
                    usedGuides = true;
                }
            }

            if (sizeAndNesting.nestingLevel > 0 && usedGuides == false)
                methodBuilder.Append(' ', sizeAndNesting.nestingLevel);
        }
        public void AppendX86Documentation(StringBuilder lineBuilder, DecompiledMethod method, AssemblyInstruction instruction)
        {
            try
            {
                int lineOffset = DocumentationOffset;
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

                    if (lhs.StartsWith("dword ptr")) { lhs = lhs.Replace("dword ptr", "(32bit)Memory"); }
                    else if (lhs.StartsWith("word ptr")) { lhs = lhs.Replace("word ptr", "(16bit)Memory"); }
                    else if (lhs.StartsWith("byte ptr")) { lhs = lhs.Replace("byte ptr", "(8bit)Memory"); }
                    else if (lhs.StartsWith("[")) { lhs = "Memory" + lhs; }

                    if (rhs.StartsWith("dword ptr")) { rhs = rhs.Replace("dword ptr", "(32bit)Memory"); }
                    else if (rhs.StartsWith("word ptr")) { rhs = rhs.Replace("word ptr", "(16bit)Memory"); }
                    else if (rhs.StartsWith("byte ptr")) { rhs = rhs.Replace("byte ptr", "(8bit)Memory"); }
                    else if (rhs.StartsWith("[")) { rhs = "Memory" + rhs; }

                    if (lhs.StartsWith("r")) { lhs = "(64bit)" + lhs; }
                    if (rhs.StartsWith("e")) { rhs = "(32bit)" + rhs; }
                    else if (rhs.StartsWith("r")) 
                    {
                        if (rhs.EndsWith("d"))      { rhs = "(32bit)" + rhs; }
                        else if (rhs.EndsWith("w")) { rhs = "(16bit)" + rhs; }
                        else if (rhs.EndsWith("b")) { rhs = "(8bit)" + rhs; }
                    }

                    lineBuilder.Append($" # {lhs} = {rhs} (sign extend)");
                }
                else if(instruction.Instruction == "movzx")
                {
                    var lhs = instruction.Arguments[0].Value.Trim();
                    var rhs = instruction.Arguments[1].Value.Trim();

                    if      (lhs.StartsWith("dword ptr")) { lhs = lhs.Replace("dword ptr", "(32bit)Memory"); }
                    else if (lhs.StartsWith("word ptr"))  { lhs = lhs.Replace("word ptr", "(16bit)Memory"); }
                    else if (lhs.StartsWith("byte ptr"))  { lhs = lhs.Replace("byte ptr", "(8bit)Memory"); }
                    else if (lhs.StartsWith("[")) { lhs = "Memory" + lhs; }

                    if      (rhs.StartsWith("dword ptr")) { rhs = rhs.Replace("dword ptr", "(32bit)Memory"); }
                    else if (rhs.StartsWith("word ptr"))  { rhs = rhs.Replace("word ptr", "(16bit)Memory"); }
                    else if (rhs.StartsWith("byte ptr"))  { rhs = rhs.Replace("byte ptr", "(8bit)Memory"); }
                    else if (rhs.StartsWith("[")) { rhs = "Memory" + rhs; }

                    if (lhs.StartsWith("e")) { lhs = "(32bit)" + lhs; }
                    if (rhs.StartsWith("r"))
                    {
                        if (rhs.EndsWith("d")) { rhs = "(32bit)" + rhs; }
                        else if (rhs.EndsWith("w")) { rhs = "(16bit)" + rhs; }
                        else if (rhs.EndsWith("b")) { rhs = "(8bit)" + rhs; }
                    }
                    else if (rhs.Length == 2) { rhs = "(16bit)" + rhs; }


                    lineBuilder.Append($" # {lhs} = {rhs} (zero extend)");
                }
                else if (instruction.Instruction == "shl")
                {
                    var lhs = instruction.Arguments[0].Value.Trim();
                    var rhs = instruction.Arguments[1].Value.Trim();

                    if (lhs.StartsWith("[")) { lhs = "Memory" + lhs; }
                    if (rhs.StartsWith("[")) { rhs = "Memory" + rhs; }

                    if (lhs.StartsWith("r")) { lhs = "(64bit)" + lhs; }
                    if (rhs.StartsWith("e")) { rhs = "(32bit)" + rhs; }

                    lineBuilder.Append($" # {lhs} << {rhs}");
                }
                else if (instruction.Instruction == "shr")
                {
                    var lhs = instruction.Arguments[0].Value.Trim();
                    var rhs = instruction.Arguments[1].Value.Trim();

                    if (lhs.StartsWith("[")) { lhs = "Memory" + lhs; }
                    if (rhs.StartsWith("[")) { rhs = "Memory" + rhs; }

                    if (lhs.StartsWith("r")) { lhs = "(64bit)" + lhs; }
                    if (rhs.StartsWith("e")) { rhs = "(32bit)" + rhs; }

                    lineBuilder.Append($" # {lhs} >> {rhs}");
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
                else if (instruction.Instruction == "push")
                {
                    var lhs = instruction.Arguments[0].Value.Trim();

                    if (lhs.StartsWith("[")) { lhs = "Memory" + lhs; }

                    if (IsHex(lhs))
                        lhs = HexToDecimal(lhs);

                    lineBuilder.Append($" # stack.push({lhs})");
                }
                else if (instruction.Instruction == "pop")
                {
                    var lhs = instruction.Arguments[0].Value.Trim();

                    if (lhs.StartsWith("[")) { lhs = "Memory" + lhs; }

                    if (IsHex(lhs))
                        lhs = HexToDecimal(lhs);

                    lineBuilder.Append($" # {lhs} = stack.pop()");
                }
                else if (instruction.Instruction == "add")
                {
                    var lhs = instruction.Arguments[0].Value.Trim();
                    var rhs = instruction.Arguments[1].Value.Trim();

                    if (lhs.StartsWith("[")) { lhs = "Memory" + lhs; }
                    if (rhs.StartsWith("[")) { rhs = "Memory" + rhs; }

                    if (IsHex(rhs))
                        rhs = HexToDecimal(rhs);

                    if (lhs == "rsp")
                    {
                        lineBuilder.Append($" # stack.pop_times({int.Parse(rhs) / 8})");
                    }
                    else
                    {
                        lineBuilder.Append($" # {lhs} += {rhs}");
                    }
                }
                else if (instruction.Instruction == "sub")
                {
                    var lhs = instruction.Arguments[0].Value.Trim();
                    var rhs = instruction.Arguments[1].Value.Trim();

                    if (lhs.StartsWith("[")) { lhs = "Memory" + lhs; }
                    if (rhs.StartsWith("[")) { rhs = "Memory" + rhs; }

                    if (IsHex(rhs))
                        rhs = HexToDecimal(rhs);

                    if (lhs == "rsp")
                    {
                        lineBuilder.Append($" # stack.push_times({int.Parse(rhs) / 8})");
                    }
                    else
                    {
                        lineBuilder.Append($" # {lhs} -= {rhs}");
                    }
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
                else if (instruction.Instruction.StartsWith(".") && instruction.Instruction.EndsWith(":"))
                {
                    lineBuilder.Append($" # jump label");
                }
                else if (JumpInstructions.Contains(instruction.Instruction))
                {
                    var prev = method.Instructions[instruction.OrdinalIndex - 1];
                    var guide = prev.Instruction == "cmp" ? XConsole.ConsoleBorderStyle.BottomLeft.ToString() + "> " : "";
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

        private bool IsHex(string value)
        {
            if (value != null)
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
    }
}
