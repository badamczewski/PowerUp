using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PowerUp.Core.Compilation
{
    public class ILCompiler
    {
        public Type Compile(string il)
        {
            ILTokenizer tokenizer = new ILTokenizer();
            ILParser iLParser = new ILParser();

            var tokens = tokenizer.Tokenize(il);
            var root = iLParser.Parse(tokens);

            var name = Guid.NewGuid().ToString();
            var asm  = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName(name),
                AssemblyBuilderAccess.Run);

            var module = asm.DefineDynamicModule(name);
            var type = module.DefineType("Compilation", TypeAttributes.Public | TypeAttributes.BeforeFieldInit);

            foreach (var ilMethod in root.Methods)
            {
                var returnType = StringToType(ilMethod.Returns);
               
                Type[] argTypes = new Type[ilMethod.Args.Count];
                int idx = 0;
                foreach(var arg in ilMethod.Args)
                {
                    argTypes[idx++] = StringToType(arg.Type);
                }

                MethodAttributes accessorAttribute = MethodAttributes.Public;
                if (ilMethod.Accessor == "private") accessorAttribute = MethodAttributes.Private;

                var method = type.DefineMethod(ilMethod.Name, accessorAttribute | MethodAttributes.HideBySig, returnType, argTypes);
                var ilGen  = method.GetILGenerator();

                //
                // Book keep the labels 
                //
                Label[] labels = new Label[ilMethod.Code.Count];
                int index = 0;

                foreach (var opCode in ilMethod.Code)
                {
                    var label = default(Label);  

                    if (labels[index] == default(Label))
                    {
                        label = ilGen.DefineLabel();
                    }
                    else
                    {
                        label = labels[index];
                    }

                    ilGen.MarkLabel(label);


                    switch (opCode.OpCode)
                    {
                        //
                        // Consts and Loads
                        //
                        case "ldc.i4.0": ilGen.Emit(OpCodes.Ldc_I4_0); break;
                        case "ldc.i4.1": ilGen.Emit(OpCodes.Ldc_I4_1); break;
                        case "ldc.i4.2": ilGen.Emit(OpCodes.Ldc_I4_2); break;
                        case "ldc.i4.3": ilGen.Emit(OpCodes.Ldc_I4_3); break;
                        case "ldc.i4.4": ilGen.Emit(OpCodes.Ldc_I4_4); break;
                        case "ldc.i4.5": ilGen.Emit(OpCodes.Ldc_I4_5); break;
                        case "ldc.i4.6": ilGen.Emit(OpCodes.Ldc_I4_6); break;
                        case "ldc.i4.7": ilGen.Emit(OpCodes.Ldc_I4_7); break;
                        case "ldc.i4.8": ilGen.Emit(OpCodes.Ldc_I4_8); break;
                        case "ldc.i4.s": ilGen.Emit(OpCodes.Ldc_I4_S,  int.Parse(opCode.GetFirstArg())); break;
                        case "ldc.i4":   ilGen.Emit(OpCodes.Ldc_I4,    int.Parse(opCode.GetFirstArg())); break;
                        case "ldarg.0":  ilGen.Emit(OpCodes.Ldarg_0);  break;
                        case "ldarg.1":  ilGen.Emit(OpCodes.Ldarg_1);  break;
                        case "ldarg.2":  ilGen.Emit(OpCodes.Ldarg_2);  break;
                        case "ldarg.3":  ilGen.Emit(OpCodes.Ldarg_3);  break;
                        case "ldarg.s":  ilGen.Emit(OpCodes.Ldarg_S,   FindArgIndex(opCode.GetFirstArg(), ilMethod)); break;
                        case "ldarga.s": ilGen.Emit(OpCodes.Ldarga_S,  FindArgIndex(opCode.GetFirstArg(), ilMethod)); break;

                        //
                        // Stringsssss
                        //
                        case "ldstr":    ilGen.Emit(OpCodes.Ldstr, opCode.GetFirstArg()); break;


                        //
                        // Conditions
                        //

                        // !=
                        case "bne.un.s":
                            {
                                var existing = GetOrCreateLabel(ilGen, labels, opCode, ilMethod);
                                ilGen.Emit(OpCodes.Bne_Un_S, existing); break;
                            }
                        case "bne.un":
                            {
                                var existing = GetOrCreateLabel(ilGen, labels, opCode, ilMethod);
                                ilGen.Emit(OpCodes.Bne_Un, existing); break;
                            }
                        // ==
                        case "beq.s":
                            {
                                var existing = GetOrCreateLabel(ilGen, labels, opCode, ilMethod);
                                ilGen.Emit(OpCodes.Beq_S, existing); break;
                            }
                        case "beq":
                            {
                                var existing = GetOrCreateLabel(ilGen, labels, opCode, ilMethod);
                                ilGen.Emit(OpCodes.Beq, existing); break;
                            }
                        // >=
                        case "bge.s":
                            {
                                var existing = GetOrCreateLabel(ilGen, labels, opCode, ilMethod);
                                ilGen.Emit(OpCodes.Bge_S, existing); break;
                            }
                        case "bge":
                            {
                                var existing = GetOrCreateLabel(ilGen, labels, opCode, ilMethod);
                                ilGen.Emit(OpCodes.Bge, existing); break;
                            }
                        // <=
                        case "ble.s":
                            {
                                var existing = GetOrCreateLabel(ilGen, labels, opCode, ilMethod);
                                ilGen.Emit(OpCodes.Ble_S, existing); break;
                            }
                        case "ble":
                            {
                                var existing = GetOrCreateLabel(ilGen, labels, opCode, ilMethod);
                                ilGen.Emit(OpCodes.Ble, existing); break;
                            }
                        // <
                        case "blt.s":
                            {
                                var existing = GetOrCreateLabel(ilGen, labels, opCode, ilMethod);
                                ilGen.Emit(OpCodes.Blt_S, existing); break;
                            }
                        case "blt":
                            {
                                var existing = GetOrCreateLabel(ilGen, labels, opCode, ilMethod);
                                ilGen.Emit(OpCodes.Blt, existing); break;
                            }
                        // >
                        case "bgt.s":
                            {
                                var existing = GetOrCreateLabel(ilGen, labels, opCode, ilMethod);
                                ilGen.Emit(OpCodes.Bgt_S, existing); break;
                            }
                        case "bgt":
                            {
                                var existing = GetOrCreateLabel(ilGen, labels, opCode, ilMethod);
                                ilGen.Emit(OpCodes.Bgt, existing); break;
                            }
                        // Unconditional Jump
                        case "br.s":
                            {
                                var existing = GetOrCreateLabel(ilGen, labels, opCode, ilMethod);
                                ilGen.Emit(OpCodes.Br_S, existing); break;
                            }
                        case "br":
                            {
                                var existing = GetOrCreateLabel(ilGen, labels, opCode, ilMethod);
                                ilGen.Emit(OpCodes.Br, existing); break;
                            }
                        case "stelem.i1": ilGen.Emit(OpCodes.Stelem_I1); break;
                        case "stelem.i2": ilGen.Emit(OpCodes.Stelem_I2); break;
                        case "stelem.i4": ilGen.Emit(OpCodes.Stelem_I4); break;
                        case "stelem.i8": ilGen.Emit(OpCodes.Stelem_I8); break;
                        case "stelem.r4": ilGen.Emit(OpCodes.Stelem_R4); break;
                        case "stelem.r8": ilGen.Emit(OpCodes.Stelem_R8); break;

                        //
                        // Boxing / Unboxing
                        //
                        case "box":       ilGen.Emit(OpCodes.Box,       StringToType(opCode.GetFirstArg())); break;
                        case "unbox.any": ilGen.Emit(OpCodes.Unbox_Any, StringToType(opCode.GetFirstArg())); break;
                        case "unbox":     ilGen.Emit(OpCodes.Unbox,     StringToType(opCode.GetFirstArg())); break;
                        //
                        // Switch / Case
                        //
                        case "switch":    ilGen.Emit(OpCodes.Switch, GetOrCreateLabels(ilGen, labels, opCode, ilMethod)); break;
                        //
                        // Call
                        // This needs better handling; we want to call methods we have defined in IL
                        // so to do this we first would need to figure out which one to emit first.
                        //
                        case "call":
                            break;
                            //ilGen.Emit(OpCodes.Call, opCode.GetFirstArg()); break;
                        //
                        // Maths 
                        //
                        case "sub":      ilGen.Emit(OpCodes.Sub); break;
                        case "mul":      ilGen.Emit(OpCodes.Mul); break;
                        case "div":      ilGen.Emit(OpCodes.Div); break;
                        case "add":      ilGen.Emit(OpCodes.Add); break;
                        case "ret":      ilGen.Emit(OpCodes.Ret); break;
                    }

                    index++;
                }
            }
            
            return type.CreateType();
        }
        
        private Label[] GetOrCreateLabels(ILGenerator ilGen, Label[] labels, ILInst opCode, ILMethod ilMethod)
        {
            Label[] labelsToReturn = new Label[opCode.Arguments.Length];
            int index = 0;
            foreach (var arg in opCode.Arguments)
            {
                var lblIndex = FindLabelIndex(arg, ilMethod);
                ref var existing = ref labels[lblIndex];
                if (existing == default(Label))
                {
                    existing = ilGen.DefineLabel();
                    labels[lblIndex] = existing;
                }

                labelsToReturn[index++] = existing;
            }
            return labelsToReturn;
        }


        private ref Label GetOrCreateLabel(ILGenerator ilGen, Label[] labels, ILInst opCode, ILMethod ilMethod)
        {
            var lblIndex = FindLabelIndex(opCode.GetFirstArg(), ilMethod);
            ref var existing = ref labels[lblIndex];
            if (existing == default(Label))
            {
                existing = ilGen.DefineLabel();
                labels[lblIndex] = existing;
            }

            return ref existing;
        }

        private int FindLabelIndex(string name, ILMethod method)
        {
            int idx = 0;
            foreach (var opCode in method.Code)
            {
                if (opCode.Label == name)
                    return idx;

                idx++;
            }

            return -1;
        }

        private int FindArgIndex(string name, ILMethod method)
        {
            int idx = 0;
            foreach(var arg in method.Args)
            {
                if (arg.Name == name)
                    return idx;

                idx++;
            }

            return -1;
        }

        private Type StringToType(string typeInfo)
        {
            //
            // @TODO 31.08.21 BA; this is very basic, and needs to be improved,
            // but we are ok with this.
            //
            Type returnType = null;
            switch (typeInfo)
            {
                case "void":   returnType = typeof(void);   break;
                //
                // Do a shorthand for int32
                //
                case "int":
                case "int32":  returnType = typeof(Int32);  break;
                case "string": returnType = typeof(string); break;
                case "object": returnType = typeof(object); break;
                default:       returnType = Type.GetType(typeInfo); break;
            }

            return returnType;
        }

    }
}
