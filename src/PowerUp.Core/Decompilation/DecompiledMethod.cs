using PowerUp.Core.Compilation;
using PowerUp.Core.Errors;
using System;
using System.Collections.Generic;
using System.Text;


namespace PowerUp.Core.Decompilation
{
    public class DecompilationUnit
    {
        public Error[] Errors { get; set; }  = Array.Empty<Error>();
        public DecompiledMethod[] DecompiledMethods { get; set; }
        public TypeLayout[] TypeLayouts { get; set; }
        public ILToken[] ILTokens { get; set; }
        public string[] Messages { get; set; } = Array.Empty<string>();
        public CompilationOptions Options { get; set; }
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

    public class MethodSignature
    {
        public string TypeName { get; set; }
        public string Name { get; set; }
        public string Return { get; set; }
        public string[] Arguments { get; set; }

        public string ToMethodCallSignature()
        {
            StringBuilder sigBuilder = new StringBuilder();
            sigBuilder.Append(TypeName);
            sigBuilder.Append(".");
            sigBuilder.Append(Name);
            sigBuilder.Append("(");

            int index = 1;
            foreach (var arg in Arguments)
            {
                sigBuilder.Append(arg);
                if (index < Arguments.Length)
                    sigBuilder.Append(", ");
                index++;
            }
            sigBuilder.Append(")");
            return sigBuilder.ToString();
        }
    }


    public class DecompiledMethod : MethodSignature
    {
        public List<AssemblyInstruction> Instructions { get; set; }
            = new List<AssemblyInstruction>();
        public int ILOffsetStart { get; set; }
        public int ILOffsetEnd { get; set;}
        public bool IsVisible { get; set; } = true;
        public uint CodeSize { get; set; }
        public ulong CodeAddress { get; set; }
        public List<string> Messages { get; set; } = new();
        public List<MethodSignature> Calls { get; set; } = new();
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
        public int OrdinalIndex { get; set; }

        public int[] GuideBlocks = new int[64];
        public ulong RefAddress { get; set; }
        public ulong Address { get; set; }
        public string Instruction { get; set; }
        public string OpCode { get; set; }
        public InstructionArg[] Arguments { get; set; }
        public JumpDirection jumpDirection { get; set; }
        public int JumpSize { get; set; } = -1;
        public int JumpIndex { get; set; } = -1;
        public bool IsCode { get; set; }

        public override string ToString()
        {
            return Instruction;
        }
    }

    public enum JumpDirection
    {
        None,
        Up,
        Down,
        Out,
        Label
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
