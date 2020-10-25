using PowerUp.Core.Decompilation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Linq;
using Iced.Intel;
using Microsoft.Diagnostics.Runtime;
using System.Diagnostics.CodeAnalysis;

namespace PowerUp.Core.Decompiler
{
    public class JITCodeDecompiler
    {
        public DecompiledMethod DecompileMethod(MethodInfo method, string functionCallName = null)
        {
            using (var dataTarget = DataTarget.AttachToProcess(Process.GetCurrentProcess().Id,
                UInt32.MaxValue,
                AttachFlag.Passive))
            {
                var clrVersion = dataTarget.ClrVersions.First();
                var runtime = clrVersion.CreateRuntime();

                // Attempt to make ClrMd aware of recent jitting:
                // https://github.com/microsoft/clrmd/issues/303
                dataTarget.DataReader.Flush();

                var topLevelDelegateAddress = GetMethodAddress(runtime, method);

                var decompiledMethod = DecompileMethod(runtime, topLevelDelegateAddress.codePointer,
                    topLevelDelegateAddress.codeSize, functionCallName);

                return decompiledMethod;
            }
        }

        public unsafe (ulong codePointer, uint codeSize) GetMethodAddress(ClrRuntime runtime, MethodInfo method)
        {
            var clrmdMethodHandle = runtime.GetMethodByHandle((ulong)method.MethodHandle.Value.ToInt64());
            if (clrmdMethodHandle.NativeCode == 0) throw new InvalidOperationException($"Unable to disassemble method `{method}`");

            var codePtr = clrmdMethodHandle.HotColdInfo.HotStart;
            var codeSize = clrmdMethodHandle.HotColdInfo.HotSize;

            return (codePtr, codeSize);
        }

        public unsafe DecompiledMethod DecompileMethod(ulong codePtr, uint codeSize, string name)
        {
            using (var dataTarget = DataTarget.AttachToProcess(Process.GetCurrentProcess().Id,
                UInt32.MaxValue,
                AttachFlag.Passive))
            {
                var clrVersion = dataTarget.ClrVersions.First();
                var runtime = clrVersion.CreateRuntime();

                // Attempt to make ClrMd aware of recent jitting:
                // https://github.com/microsoft/clrmd/issues/303
                dataTarget.DataReader.Flush();


                return DecompileMethod(runtime, codePtr, codeSize, name);
            }
        }

        public unsafe DecompiledMethod DecompileMethod(ClrRuntime runtime, ulong codePtr, uint codeSize, string functionCallName = null)
        {
            List<AssemblyInstruction> instructions = new List<AssemblyInstruction>();

            var codePointer = (long*)new IntPtr((long)codePtr);
            Span<byte> codeBuffer = new Span<byte>(codePointer, (int)codeSize);

            var reader = new ByteArrayCodeReader(codeBuffer.ToArray());
            var decoder = Iced.Intel.Decoder.Create(64, reader);
            decoder.IP = codePtr;

            while (reader.CanReadByte)
            {
                decoder.Decode(out var instruction);

                var inst = instruction.ToString();
                var instAndArgs = inst.Split(new char[] { ' ', ',' });
                var instructionName = instAndArgs[0];
                //
                // Parse Arguments
                //
                AssemblyInstruction assemblyInstruction = new AssemblyInstruction();
                assemblyInstruction.Instruction = instructionName;

                if (instAndArgs.Length > 1)
                {
                    ulong address = 0;
                    string name = null;
                    uint size = 0;

                    var hasAddress = GetReferencedAddressToMethodName(
                        out address, out size, out name,
                        instruction, runtime);

                    if (functionCallName != null && functionCallName == name)
                    {
                        return DecompileMethod(runtime, address, size);
                    }

                    assemblyInstruction.Arguments = new InstructionArg[instAndArgs.Length - 1];
                    for (int i = 1; i < instAndArgs.Length; i++)
                    {
                        assemblyInstruction.Arguments[i - 1] = new InstructionArg()
                        {
                            Value = hasAddress && instructionName == "call" ? name : instAndArgs[i],
                            CallAdress = address,
                            CallCodeSize = codeSize
                        };
                    }
                }
                else
                {
                    assemblyInstruction.Arguments = new InstructionArg[0];
                }

                instructions.Add(assemblyInstruction);
            }

            return new DecompiledMethod()
            {
                Instructions = instructions,
                CodeAdresss = codePtr,
                CodeSize = codeSize
            };
        }

        private bool GetReferencedAddressToMethodName(out ulong refAddress, out uint codeSize, out string name, Instruction instruction, ClrRuntime runtime)
        {
            name = null;
            refAddress = 0;
            codeSize = 0;

            bool isAddressOk = false;

            for (int i = 0; i < instruction.OpCount; i++)
            {
                switch (instruction.GetOpKind(i))
                {
                    case OpKind.NearBranch16:
                    case OpKind.NearBranch32:
                    case OpKind.NearBranch64:
                        refAddress = instruction.NearBranchTarget;
                        isAddressOk = refAddress > ushort.MaxValue;
                        break;
                    case OpKind.Immediate64:
                        refAddress = instruction.GetImmediate(i);
                        isAddressOk = refAddress > ushort.MaxValue;
                        break;
                    case OpKind.Memory64:
                        refAddress = instruction.MemoryAddress64;
                        isAddressOk = refAddress > ushort.MaxValue;
                        break;
                    case OpKind.Memory when instruction.IsIPRelativeMemoryOperand:
                        refAddress = instruction.IPRelativeMemoryAddress;
                        isAddressOk = refAddress > ushort.MaxValue;
                        break;
                    case OpKind.Memory:
                        refAddress = instruction.MemoryDisplacement;
                        isAddressOk = refAddress > ushort.MaxValue;
                        break;
                }
            }

            if (refAddress == 0)
                return false;

            var jitHelperFunctionName = runtime.GetJitHelperFunctionName(refAddress);
            if (string.IsNullOrWhiteSpace(jitHelperFunctionName) == false)
            {
                name = jitHelperFunctionName;
                return true;
            }

            var methodTableName = runtime.GetMethodTableName(refAddress);
            if (string.IsNullOrWhiteSpace(methodTableName) == false)
            {
                name = methodTableName;
                return true;
            }

            var methodDescriptor = runtime.GetMethodByHandle(refAddress);
            if (methodDescriptor != null)
            {
                name = methodDescriptor.Name;
                refAddress = methodDescriptor.HotColdInfo.HotStart;
                codeSize = methodDescriptor.HotColdInfo.HotSize;
                return true;
            }

            var methodCall = runtime.GetMethodByAddress(refAddress);
            if (methodCall != null && string.IsNullOrWhiteSpace(methodCall.Name) == false)
            {
                name = methodCall.Name;
                refAddress = methodCall.HotColdInfo.HotStart;
                codeSize = methodCall.HotColdInfo.HotSize;
                return true;
            }

            if (methodCall == null)
            {
                if (runtime.ReadPointer(refAddress, out ulong newAddress) && newAddress > ushort.MaxValue)
                    methodCall = runtime.GetMethodByAddress(newAddress);

                if (methodCall is null)
                    return false;

                name = methodCall.Name;
                refAddress = methodCall.HotColdInfo.HotStart;
                codeSize = methodCall.HotColdInfo.HotSize;

                return true;
            }

            return false;
        }

    }

}
