using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using PowerUp.Core.Console;

namespace PowerUp.Core.Decompilation
{
    public static class JitExtensions
    {
        public static DecompiledMethod[] ToAsm(this Type typeInfo, bool @private = false)
        {
            List<DecompiledMethod> methods = new List<DecompiledMethod>();

            foreach (var constructorInfo in typeInfo.GetConstructors())
            {
                RuntimeHelpers.PrepareMethod(constructorInfo.MethodHandle);
            }

            var flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
            if (@private)
            {
                flags |= BindingFlags.NonPublic;
            }

            foreach (var methodInfo in typeInfo.GetMethods(flags))
            {
                if (methodInfo.DeclaringType != typeof(System.Object))
                {
                    var decompiledMethod = ToAsm(methodInfo);
                    methods.Add(decompiledMethod);
                }
            }

            return methods.ToArray();
        }

        /// <summary>
        /// Extracts native assembly code from method.
        /// This method will only provide a tier0 or optimizedTier (for loops) native assembly
        /// when JIT Tiered compilation is on.
        ///
        /// For optimized build you should build your project with:  <TieredCompilation>false</TieredCompilation>
        /// </summary>
        /// <param name="methodInfo"></param>
        /// <returns></returns>
        public static DecompiledMethod ToAsm(this MethodInfo methodInfo)
        {
            RuntimeHelpers.PrepareMethod(methodInfo.MethodHandle);
            var decompiler = new JitCodeDecompiler();
            var decompiled = decompiler.DecompileMethod(methodInfo);

            return decompiled;
        }

        /// <summary>
        /// Extracts native assembly code from method.
        /// This method will provide a tier0 and tier1 native code but in order to do that
        /// the user needs to run the method a sufficient number of times; That's why
        /// the action delegate is provided.
        /// </summary>
        /// <param name="methodInfo"></param>
        /// <returns></returns>
        public static (DecompiledMethod tier0, DecompiledMethod tier1)
            ToAsm(this MethodInfo methodInfo, Action action)
        {
            RuntimeHelpers.PrepareMethod(methodInfo.MethodHandle);
            var decompiler = new JitCodeDecompiler();
            var decompiled = decompiler.DecompileMethod(methodInfo);

            int runCount = 10_000_000;
            int checkCount = 100;

            for (int i = 0; i < checkCount; i++)
            {
                for (int n = 0; n < runCount; n++)
                {
                    action();
                }

                var decompiledAfter = decompiler.
                    DecompileMethod(
                        decompiled.CodeAddress,
                        decompiled.CodeSize,
                        methodInfo.Name);

                if (decompiledAfter.CodeAddress != decompiled.CodeAddress)
                {
                    return (decompiled, decompiledAfter);
                }
            }

            return (decompiled, decompiled);
        }
        
        /// <summary>
        /// Prints assembly code of the decompiled method.
        /// </summary>
        /// <param name="method"></param>
        public static void Print(this DecompiledMethod method)
        {
            XConsole.WriteLine(method.ToString());
        }

        /// <summary>
        /// Prints assembly code of the decompiled method.
        /// </summary>
        /// <param name="method"></param>
        public static string Print(this DecompiledMethod[] methods)
        {
            StringBuilder @string = new StringBuilder();
            foreach (var method in methods)
            {
                @string.Append(method.ToString());
                XConsole.WriteLine(method.ToString());
            }

            return @string.ToString();
        }

    }

}
