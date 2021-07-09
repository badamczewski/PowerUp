using System;
using System.Collections.Generic;
using System.Text;


namespace PowerUp.Core.Decompilation
{
    public class DecompilationUnit
    {
        public Error[] Errors { get; set; }
        public DecompiledMethod[] DecompiledMethods { get; set; }
        public ILToken[] ILCode { get; set; }
    }

    public class Error
    {
        public string Id { get; set; }
        public string Message { get; set; }
    }


    public enum ILTokenType
    {
        Unknown,
        Indent,
        FoldEnd,
        FoldStart,
        Unindent,
        Char,
        Text,
        LocalRef,
        Ref,
        OpCode,
        NewLine
    }

    public class ILToken
    {
        public ILTokenType Type { get; set; }
        public string Value { get; set; }

        public override string ToString()
        {
            return $"{Type.ToString()} {Value}";
        }
    }

    public class DecompiledMethod
    {
        public string Name { get; set; }

        public string Return { get; set; }
        public string[] Arguments { get; set; }

        public List<AssemblyInstruction> Instructions { get; set; }
            = new List<AssemblyInstruction>();

        public uint CodeSize { get; set; }
        public ulong CodeAddress { get; set; }

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"{Name}:");

            foreach (var assemblyCode in Instructions)
            {
                StringBuilder argBuilder = new StringBuilder();
                foreach (var arg in assemblyCode.Arguments)
                {
                    argBuilder.Append(arg.Value + ", ");
                }

                if (argBuilder.Length > 2)
                    argBuilder.Remove(argBuilder.Length - 2, 2);

                builder.AppendLine($"  {assemblyCode.Address.ToString("X")} `{assemblyCode.Instruction}` {argBuilder.ToString()}");
            }

            return builder.ToString();
        }
    }

    public class AssemblyInstruction
    {
        public int[] RefIds = new int[16];
        public ulong RefAddress { get; set; }
        public ulong Address { get; set; }
        public string Instruction { get; set; }
        public string OpCode { get; set; }
        public InstructionArg[] Arguments { get; set; }
    }

    public struct InstructionArg
    {
        public string Value { get; set; }
        public ulong CallAdress { get; set; }
        public ulong CallCodeSize { get; set; }
        public bool HasReferenceAddress { get; set; }
        public string AltValue { get; set; }
        public bool IsAddressing { get; set; }

        public override string ToString()
        {
            return Value;
        }
    }
}
