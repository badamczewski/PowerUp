using Iced.Intel;
using PowerUp.Core.Decompilation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PowerUp.Watcher
{
    public class AsmCodeFlowAnalyser
    {
        private TypeLayout[] _typeLayouts;
        private List<AssemblyInstruction> previrousInstructions = new List<AssemblyInstruction>();

        //
        // The purpose of this is to keep track of registers and their state once we move through instructions; when generating
        // output and documentation.
        //
        private Dictionary<string, string> movValues = new Dictionary<string, string>();
        private HashSet<string> registers = new HashSet<string>()
        {
            "rax", "rcx", "rdx", "rbx", "rsp", "rbp", "rsi", "rdi",
            "eax", "ecx", "edx", "ebx", "esp", "ebp", "esi", "edi" 
        };

        public void SetTypeInfo(TypeLayout[] layouts)
        {
            _typeLayouts = layouts;
        }

        public string GetValueFor(string reg)
        {
            if(movValues.TryGetValue(reg, out var value))
                return value;

            return null;
        }

        public bool IsRegister(string item)
        {
            return registers.Contains(item);
        }

        public void Process(AssemblyInstruction instruction)
        {

            //
            // Track Reference Movs
            //
            if (instruction.Instruction.Equals("mov", StringComparison.OrdinalIgnoreCase))
            {
                var lhs = Decode(instruction.Arguments[0].Value.Trim());
                var rhs = Decode(instruction.Arguments[1].Value.Trim());

                //
                // Value: X is in RDX
                // Values[RDX] = X
                //
                // MOV RCX, RDX
                // Values[RCX] = X
                // MOV RDX, RAX <- Assume we don't know what's in RAX
                // Values[RDX] = null
                //

                //
                // In order to be able to do this correclty, we need to track the instruction
                // because it might be used later by calls to new *but* we might mov it through registers.
                // Since we don't have a *proper* code flow analyzer let's do a poor version for now ... and do a simple mov
                // detector once we realize that we just loaded the type from MT.
                //
                if (instruction.Arguments[0].IsRefType)
                {
                    lhs = Decode(instruction.Arguments[0].AltValue.Trim());
                    AddOrUpdate(lhs, rhs);
                }
                //
                // Plain MOV of value that's in the dictionary.
                //
                else if (movValues.TryGetValue(rhs, out var rhsValue))
                {
                    AddOrUpdate(lhs, rhsValue);
                }
                else if (instruction.Arguments[0].IsAddressing)
                {
                    var arg = instruction.Arguments[0];
                    var value = Decode(arg.Value);
                    var components = value.Split("+");
                    bool array = false;
                    string regValue = null;

                    int offset = 0;
                    TypeLayout layout = null;

                    foreach (var c in components)
                    {
                        if (IsRegister(c))
                        {
                            if (movValues.TryGetValue(c, out var reg))
                            {
                                regValue = reg;
                                if (reg.EndsWith("[]")) //array
                                {
                                    array = true;
                                }

                                foreach (var typeLayout in _typeLayouts)
                                {
                                    if (typeLayout.Name == reg)
                                    {
                                        layout = typeLayout;

                                        if (typeLayout.IsBoxed == false)
                                            offset += 8;
                                    }
                                }
                            }
                        }
                        else
                        {
                            var dec = c;
                            if (IsHex(dec))
                            {
                                dec = HexToDecimal(c);
                            }
                            //
                            // Is this a number.
                            //
                            if (Int32.TryParse(dec, out var num))
                            {
                                offset += num;
                            }
                        }
                    }

                    if (layout == null && array)
                    {
                        offset -= 16;
                        offset = offset / 4; 
                        rhs = regValue.Replace("[]", $"[{offset}]");
                        AddOrUpdate(lhs, rhs);

                        //regValue
                    }
                }
                else
                {
                    AddOrUpdate(lhs, rhs);
                }
            }
            //
            // After the call the result addr ends up in EAX 32b/RAX 64b
            //
            else if (instruction.Instruction.Equals("call")) 
            {
                var previrousInstruction = previrousInstructions[previrousInstructions.Count - 1];

                if (previrousInstruction != null && 
                    previrousInstruction.Instruction.Equals("mov"))
                {
                    if (instruction.Arguments[0].Value.Contains("NEWARR"))
                    {
                        previrousInstruction = previrousInstructions[previrousInstructions.Count - 2];
                    }

                    var value = previrousInstruction.Arguments[0].Value.Trim();

                    var finalRegVal = value;
                    if (IsRegister(value) == false)
                    {
                        var alt = previrousInstruction.Arguments[0].AltValue.Trim();
                        finalRegVal = alt;
                    }

                    if (movValues.TryGetValue(finalRegVal, out var typeOrValue))
                    {
                        AddOrUpdate("rax", typeOrValue);
                    }
                }
            }
            else if(instruction.Instruction.Equals("inc"))
            {
                var arg = instruction.Arguments[0];
                if (arg.IsAddressing)
                {
                    var value = Decode(arg.Value);
                    //
                    // Remove Adressing and leave just the main register
                    //
                    var components = value.Split("+");
                    
                    int offset = 0;
                    TypeLayout layout = null;

                    foreach(var c in components)
                    {
                        if(IsRegister(c))
                        {
                            if (movValues.TryGetValue(c, out var reg))
                            {
                                foreach(var typeLayout in _typeLayouts)
                                {
                                    if(typeLayout.Name == reg)
                                    {
                                        layout = typeLayout;

                                        if (typeLayout.IsBoxed == false)
                                            offset += 8;

                                    }
                                }
                            }
                        }
                        else
                        {
                            var dec = c;
                            if(IsHex(dec))
                            {
                                dec = HexToDecimal(c);
                            }
                            //
                            // Is this a number.
                            //
                            if (Int32.TryParse(dec, out var num))
                            {
                                offset += num;
                            }
                        }
                    }

                    if(layout != null)
                    {
                        foreach(var field in layout.Fields)
                        {
                            if(field.Offset == offset)
                            {
                                AddOrUpdate(value, layout.Name + "." + field.Name);

                                break;
                            }
                        }
                    }
                }
            }

            previrousInstructions.Add(instruction);
        }

        private string Decode(string value)
        {
            var begin = value.IndexOf("[");
            var end   = value.IndexOf("]");

            if (begin == -1 || end == -1 || begin + 1 == end)
                return value;

            return value.Substring(begin + 1, end - begin - 1);
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
            var decValue = Convert.ToInt64(hexEssence, 16);
            return decValue.ToString();
        }

        private void AddOrUpdate(string key, string value)
        {
            if (movValues.TryGetValue(key, out _))
            {
                movValues[key] = value;
            }
            else
            {
                movValues.Add(key, value);
            }
        }
    }
}
