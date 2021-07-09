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
    public class CustomAssemblyLoadContext : AssemblyLoadContext, IDisposable
    {
        public CustomAssemblyLoadContext()
            : base(isCollectible: true)
        {
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            var name = assemblyName.Name ?? "";
            if (name == "netstandard" || name == "mscorlib" || name.StartsWith("System."))
                return Assembly.Load(assemblyName);

            return LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, assemblyName.Name + ".dll"));
        }

        public void Dispose()
        {
            Unload();
        }
    }
}
