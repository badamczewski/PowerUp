using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using PowerUp.Core.Decompilation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace PowerUp.Core.Compilation
{
    public class CodeCompiler
    {
        private string _dotNetCoreDirPath = null;

        public CodeCompiler(string dotNetCoreDirPath = @"C:\Program Files\dotnet\shared\Microsoft.NETCore.App\5.0.0\")
        {
            _dotNetCoreDirPath = dotNetCoreDirPath;
        }

        public CompilationUnit Compile(string code)
        {
            var compilation = CSharpCompilation.Create("assembly")
            .WithOptions(
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    .WithOptimizationLevel(OptimizationLevel.Release)
                    .WithAllowUnsafe(true)
            )
            .AddReferences(
                MetadataReference.CreateFromFile(typeof(object).GetTypeInfo().Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Console).GetTypeInfo().Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(_dotNetCoreDirPath, "mscorlib.dll")),
                MetadataReference.CreateFromFile(Path.Combine(_dotNetCoreDirPath, "netstandard.dll")),
                MetadataReference.CreateFromFile(Path.Combine(_dotNetCoreDirPath, "System.Runtime.dll")),
                MetadataReference.CreateFromFile(Path.Combine(_dotNetCoreDirPath, "System.Linq.dll")))

            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            $@"
                    using System.Linq;
                    using System.Runtime.CompilerServices;
                    using System;

                    public class CompilerGen
                    {{
                        {code}
                    }}")
            );


            EmitOptions emitOptions = new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb);
            DecompilationUnit unit = new DecompilationUnit();

            var assemblyStream = new MemoryStream();
            var pdbStream = new MemoryStream();
            var compilationResult = compilation.Emit(assemblyStream, pdbStream: pdbStream, options: emitOptions);

            return new CompilationUnit()
            {
                AssemblyStream = assemblyStream,
                PDBStream = pdbStream,
                CompilationResult = compilationResult
            };
        }
    }
}
