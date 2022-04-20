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
        public CompilationOptions Options { get; set; } = new CompilationOptions();    
    }

    /// <summary>
    /// Represents Watcher Compilation options for various languages.
    /// </summary>
    public class CompilationOptions
    {
        //
        // The compilation map, maps specific compilation instructions to language objects
        // like methods, structs, classes fields and other.
        //
        public Dictionary<string, string> CompilationMap { get; set; } = new();
        /// <summary>
        /// Should help be displayed by the watcher.
        /// </summary>
        public bool ShowHelp { get; set; }
        /// <summary>
        /// Should jump guides be rendered with the output.
        /// </summary>
        public bool ShowGuides { get; set; }
        /// <summary>
        /// Should instruction addresses be shorter in the ouptut.
        /// </summary>
        public bool ShortAddresses { get; set; }
        /// <summary>
        /// Should relative addresses be used (counted from zero) in the output.
        /// </summary>
        public bool RelativeAddresses { get; set; }
        /// <summary>
        /// The length by which a given address should be cut by.
        /// </summary>
        public int AddressesCutByLength { get; set; } = 4;
        /// <summary>
        /// Should documentation for output instructions or code be printed.
        /// </summary>
        public bool ShowASMDocumentation { get; set; }
        /// <summary>
        /// The offset at which documentation should be shown absolute to the instruction.
        /// </summary>
        public int ASMDocumentationOffset { get; set; } = 45;
        /// <summary>
        /// Should source code that maps to the instructions be printed in the output. 
        /// </summary>
        public bool ShowSourceMaps { get; set; }
        /// <summary>
        /// Should long namespaces and criptic names for lambdas and other constructs be simplified.
        /// </summary>
        public bool SimpleNames { get; set; }
        /// <summary>
        /// What was the optimization level when compilation happened.
        /// </summary>
        public int OptimizationLevel { get; set; }
        /// <summary>
        /// Should we compare two functions using a diff alghoritm.
        /// </summary>
        public bool Diff { get; set; }
        /// <summary>
        /// Signature of the Source Function to compare. 
        /// </summary>
        public string DiffSource { get; set; }
        /// <summary>
        /// Signature of the Target Function to compare.
        /// </summary>
        public string DiffTarget { get; set; }
        /// <summary>
        /// What other BCL functions should be decompiled/dissasembled and be printed in the
        /// output.
        /// </summary>
        public List<string> ImportList { get; set; } = new List<string>();

        public CompilationOptions() { }
    }
}
