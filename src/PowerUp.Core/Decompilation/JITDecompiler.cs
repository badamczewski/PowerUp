using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Linq;
using Iced.Intel;
using Microsoft.Diagnostics.Runtime;

namespace PowerUp.Core.Decompilation
{
    /// <summary>
    /// Decompiles runtime methods and extracts native assembly code, both
    /// for non tiered and tiered comppilations.
    /// </summary>
    public class JitCodeDecompiler
    {
        public DecompiledMethod DecompileMethod(MethodInfo method, string functionCallName = null)
        {
            using (var dataTarget = DataTarget.AttachToProcess(Process.GetCurrentProcess().Id,
                UInt32.MaxValue,
                AttachFlag.Passive))
            {
                var clrVersion = dataTarget.ClrVersions.First();
                var runtime = clrVersion.CreateRuntime();

                //
                // Attempt to make ClrMd aware of recent jitting:
                //
                // @NOTE: Not sure if this is needed any longer but it doesn't matter for us here.
                // Link: https://github.com/microsoft/clrmd/issues/303
                //
                dataTarget.DataReader.Flush();

                var topLevelDelegateAddress = GetMethodAddress(runtime, method);

                var decompiledMethod = DecompileMethod(runtime, topLevelDelegateAddress.codePointer,
                    topLevelDelegateAddress.codeSize, functionCallName);

                var @params = method.GetParameters();
                decompiledMethod.Name = method.Name;
                decompiledMethod.Return = method.ReturnType.Name;
                decompiledMethod.Arguments = new string[@params.Length];

                int idx = 0;
                foreach(var parameter in @params)
                {
                    decompiledMethod.Arguments[idx++] = parameter.ParameterType.Name;
                }
                return decompiledMethod;
            }
        }

        public unsafe (ulong codePointer, uint codeSize) GetMethodAddress(ClrRuntime runtime, MethodInfo method)
        {
            var handleValue = (ulong)method.MethodHandle.Value.ToInt64();
            ulong codePtr = 0;
            uint  codeSize = 0;
            //
            // For generic methods we need a different way of finding out the generic type implementation
            // that JIT has created.
            //
            // @NOTE: I don't know how to do it any other way than using SOS lib and getting
            // the data out this way. We should not rely on SOS long term since it is worth while
            // to learn how the data is laid out in the VM.
            //
            if (method.IsGenericMethod)
            {
                //
                // Get the SOS interface and try to extract all of the needed information by using 
                // Method Desc (MD) and extracting the Method Table (MT), this will allow us the extract
                // the code data header.
                //
                // @TODO / @NOTE: In theory this could be better since now we have access to tireded 
                // metadata, which should be somewhere in the code header (JITType field)
                //
                var sos = runtime.DacLibrary.SOSDacInterface;
                if (sos.GetMethodDescData(handleValue, 0, out var data))
                {
                    var slot = sos.GetMethodTableSlot(data.MethodTable, data.SlotNumber);
                    if(sos.GetCodeHeaderData(data.NativeCodeAddr, out var code))
                    {
                        codePtr  = code.MethodStart;
                        codeSize = code.HotRegionSize;     
                    }
                }
            }
            else
            {
                var clrmdMethodHandle = runtime.GetMethodByHandle(handleValue);
                if (clrmdMethodHandle.NativeCode == 0) throw new InvalidOperationException($"Unable to disassemble method `{method}`");

                codePtr  = clrmdMethodHandle.HotColdInfo.HotStart;
                codeSize = clrmdMethodHandle.HotColdInfo.HotSize;
            }

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

                //
                // Attempt to make ClrMd aware of recent jitting:
                //
                // @NOTE: Not sure if this is needed any longer but it doesn't matter for us here.
                // Link: https://github.com/microsoft/clrmd/issues/303
                //
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
            int instructionIndex = 0;

            while (reader.CanReadByte)
            {
                decoder.Decode(out var instruction);

                var inst = instruction.ToString();
                var instNameIndex = inst.IndexOf(' ');
                var instructionName = inst;

                if (instNameIndex != -1)
                    instructionName = inst.Substring(0, instNameIndex);

                //
                // Parse Arguments
                //
                AssemblyInstruction assemblyInstruction = new AssemblyInstruction();
                assemblyInstruction.Instruction = instructionName;
                assemblyInstruction.Address = instruction.IP;
                assemblyInstruction.RefAddress = instruction.MemoryAddress64;
                assemblyInstruction.OpCode = instruction.OpCode.ToString();
                assemblyInstruction.OrdinalIndex = instructionIndex++;

                if (instNameIndex > 0)
                {
                    var argumentSegment = inst.Substring(instNameIndex);
                    var args = argumentSegment.Split(',');

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

                    assemblyInstruction.Arguments = new InstructionArg[args.Length];
                    for (int i = 0; i < args.Length; i++)
                    {
                        var value = args[i];
                        string altValue = null;
                        if(hasAddress && address != codePtr)
                        {
                            value = name;
                            altValue = args[i];
                        }

                        bool isAddressing = false;
                        //
                        // Adressing, set the flag.
                        //
                        if(value.StartsWith("[") && value.EndsWith("]"))
                        {
                            isAddressing = true;
                        }

                        assemblyInstruction.Arguments[i] = new InstructionArg()
                        {
                            Value = value,
                            CallAdress = address,
                            CallCodeSize = size,
                            HasReferenceAddress = hasAddress,
                            AltValue = altValue,
                            IsAddressing = isAddressing
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
                CodeAddress = codePtr,
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
                    case OpKind.Immediate16:
                    case OpKind.Immediate32:
                    case OpKind.Immediate64:
                        refAddress = instruction.GetImmediate(i);
                        isAddressOk = refAddress > ushort.MaxValue;
                        break;
                    case OpKind.Memory64:
                        refAddress = instruction.MemoryAddress64;
                        isAddressOk = refAddress > ushort.MaxValue;
                        break;
                    case OpKind.Memory:
                        if (instruction.IsIPRelativeMemoryOperand)
                        {
                            refAddress = instruction.IPRelativeMemoryAddress;
                            isAddressOk = refAddress > ushort.MaxValue;
                        }
                        else
                        {
                            refAddress = instruction.MemoryDisplacement;
                            isAddressOk = refAddress > ushort.MaxValue;
                        }
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
                name = methodDescriptor.ToString();
                refAddress = methodDescriptor.HotColdInfo.HotStart;
                codeSize = methodDescriptor.HotColdInfo.HotSize;
                return true;
            }

            var methodCall = runtime.GetMethodByAddress(refAddress);
            if (methodCall != null && string.IsNullOrWhiteSpace(methodCall.Name) == false)
            {
                name = methodCall.ToString();
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

                name = methodCall.ToString();
                refAddress = methodCall.HotColdInfo.HotStart;
                codeSize = methodCall.HotColdInfo.HotSize;

                return true;
            }

            return false;
        }

    }

}
