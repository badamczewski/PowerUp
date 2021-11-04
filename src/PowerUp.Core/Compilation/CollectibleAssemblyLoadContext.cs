using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;

namespace PowerUp.Core.Compilation
{
    //
    // @robertfriberg made me change the name of this class
    // to Collectible, I mean what was wrong with Custom? ^^
    //
    public class CollectibleAssemblyLoadContext : 
        AssemblyLoadContext, IDisposable
    {
        //
        // This assembly load context is used to load compiler generated assemblies
        // that will be decompiled by the JIT compiler. This is used to decompile code
        // from user based files that are processed by the PowerUp.Watcher program.
        //
        // We had to create a custom one beacuse once we load the assembly we need a way to unload it
        // and this is the only way to set the isCollectible flag on the context.
        //
        // Here's the catch though: if you set this flag to True JIT will **refuse** to do any kind of 
        // Tiered Compilation, meaning that all methods will be compiled in "optimized" mode.
        // 
        // This prevents us to look at differences between T0 and T1 and also prevents us from certain class
        // of optimizations that happen when moving from T0 -> T1 like static readonly branch elimination and
        // possibly Profile Guided Optimization, however LoadContext has a functionality to collect and apply 
        // profiles, so maybe there's a way to do that.
        //
        // Here's the link to .NET source code that prevents Tiered Compilation for collectible assemblies:
        // https://github.com/dotnet/runtime/blob/main/src/coreclr/vm/codeversion.cpp#L2112-L2115
        //
        private bool _isCollectibleButAlwaysOptimized;
        public CollectibleAssemblyLoadContext(bool isCollectibleButAlwaysOptimized)
            : base(isCollectible: isCollectibleButAlwaysOptimized)
        {
            _isCollectibleButAlwaysOptimized = isCollectibleButAlwaysOptimized;
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            var name = assemblyName.Name;
            if (name == "netstandard" || name == "mscorlib" || name.StartsWith("System."))
                return Assembly.Load(assemblyName);

            return LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, assemblyName.Name + ".dll"));
        }

        public void Dispose()
        {
            if (_isCollectibleButAlwaysOptimized)
                Unload();
        }
    }
}



