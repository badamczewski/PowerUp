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
        public string SourceCode { get; set; }
        public MemoryStream AssemblyStream { get; set; }
        public MemoryStream PDBStream { get; set; }
        public EmitResult CompilationResult { get; set; }
        public string LanguageVersion { get; set; }
        public CompilationOptions CompilationOptions { get; set; } = new CompilationOptions();    
    }

    public class CompilationOptions
    {
        public bool ShowGuides { get; set; }
        public bool ShortAddresses { get; set; }
        public int AddressesCutByLength { get; set; } = 4;
        public bool ShowASMDocumentation { get; set; }
        public int ASMDocumentationOffset { get; set; } = 45;
        public CompilationOptions() { }
    }
}
