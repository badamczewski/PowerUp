using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using PowerUp.Core.Console;
using System.Linq;
using PowerUp.Core.Decompilation.Attributes;

namespace PowerUp.Core.Decompilation
{
    public static class JitExtensions
    {
        public static TypeLayout[] ToLayout(this Type typeInfo, bool @private = false)
        {
            List<TypeLayout> types = new List<TypeLayout>();
            var decompiler = new JitCodeDecompiler();

            var flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
            if (@private)
            {
                flags |= BindingFlags.NonPublic;
            }

            foreach(var type in typeInfo.GetNestedTypes(flags))
            {
                //
                // Create the type instance to be able to see it's layout.
                //
                var _ = type.Assembly.CreateInstance(type.FullName);
                //
                // Now Get the layout.
                //
                var typeMemLayout = decompiler.GetTypeLayout(type);
                if (typeMemLayout != null)
                    types.Add(typeMemLayout);
            }

            return types.ToArray(); 
        }

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
            var info = methodInfo;
            if (info.IsGenericMethod)
            {
                //
                // There's a reason why we are not using a type class called JITAttribute
                // (which we have in our lib), if we load the exact same code in two different
                // application domains the runtime will refuse to cast them and raise
                // InvalidCastException
                // 
                // We have to use reflection to be safe.
                //
                var attributes = info.GetCustomAttributes();
                foreach(var attribute in attributes)
                {
                    var type = attribute.GetType();
                    //
                    // If we find the correct attribute, then there's no turning back
                    // Use relection and should you fail, then fail fast.
                    //
                    // We need to make a generic method for each provided JIT attribute 
                    // So if we provide JIT[typeof(int)] JIT[typeof(string)] we are compiling not one,
                    // but two methods. 
                    //
                    // @TODO Multiple JIT attribute handling is not yet done.
                    //
                    if(type.Name == "JITAttribute")
                    {
                        var props = type.GetProperties();
                        var types = props.First(x => x.Name == "Types");
                        var value = (Type[])types.GetValue(attribute);
                        info = info.MakeGenericMethod(value);
                    }
                }               
            }
            RuntimeHelpers.PrepareMethod(info.MethodHandle);
            var decompiler = new JitCodeDecompiler();
            var decompiled = decompiler.DecompileMethod(info);

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
