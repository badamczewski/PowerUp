using System;
using System.Collections.Generic;
using System.Text;

namespace PowerUp.Core.Decompilation
{
    public class DecompiledMethod
    {
        public List<AssemblyInstruction> Instructions = new List<AssemblyInstruction>();
        public uint CodeSize;
        public ulong CodeAdresss;
    }

    public class AssemblyInstruction
    {
        public string Instruction;
        public InstructionArg[] Arguments;
    }

    public struct InstructionArg
    {
        public string Value;
        public ulong CallAdress;
        public ulong CallCodeSize;

        public override string ToString()
        {
            return Value;
        }
    }

}
