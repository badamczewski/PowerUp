using System.Collections.Generic;
using System.Text;

namespace PowerUp.Core.Decompilation
{
    public class DecompiledMethod
    {
        public List<AssemblyInstruction> Instructions = new List<AssemblyInstruction>();
        public uint CodeSize;
        public ulong CodeAddress;

        public override string ToString()
        {
            foreach (var assemblyCode in Instructions)
            {
                StringBuilder argBuilder = new StringBuilder();
                foreach (var arg in assemblyCode.Arguments)
                {
                    argBuilder.Append(arg.Value + ",");
                }

                if (argBuilder.Length > 1)
                    argBuilder.Remove(argBuilder.Length - 1, 1);

                return $"{assemblyCode.Address.ToString("X")} `{assemblyCode.Instruction}` {argBuilder.ToString()}";
            }

            return base.ToString();
        }
    }

    public class AssemblyInstruction
    {
        public int[] RefIds = new int[16];
        public ulong RefAddress;
        public ulong Address;
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
