using Microsoft.CodeAnalysis.Emit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace PowerUp.Core.Compilation
{
    public class CompilationUnit
    {
        public MemoryStream AssemblyStream { get; set; }
        public MemoryStream PDBStream { get; set; }
        public EmitResult CompilationResult { get; set; }
    }
}
