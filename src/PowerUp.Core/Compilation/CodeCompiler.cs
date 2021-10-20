using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using PowerUp.Core.Decompilation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace PowerUp.Core.Compilation
{
    public class CodeCompiler
    {
        public string DotNetCoreDirPath { get; private set; }

        public CodeCompiler(string dotNetCoreDirPath)
        {
            DotNetCoreDirPath = dotNetCoreDirPath;
        }

        public CompilationUnit Compile(string code)
        {

            var sourceCode = RewriteCode(code);

            var compilation = CSharpCompilation.Create("assembly")
            .WithOptions(
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    .WithOptimizationLevel(OptimizationLevel.Release)
                    .WithAllowUnsafe(true)
            )
            .AddReferences(
                MetadataReference.CreateFromFile(typeof(object).GetTypeInfo().Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Console).GetTypeInfo().Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(DotNetCoreDirPath, "mscorlib.dll")),
                MetadataReference.CreateFromFile(Path.Combine(DotNetCoreDirPath, "netstandard.dll")),
                MetadataReference.CreateFromFile(Path.Combine(DotNetCoreDirPath, "System.Runtime.dll")),
                MetadataReference.CreateFromFile(Path.Combine(DotNetCoreDirPath, "System.Linq.dll")),
                MetadataReference.CreateFromFile(Path.Combine(DotNetCoreDirPath, "System.Linq.Expressions.dll"))
                )
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(sourceCode));

            EmitOptions emitOptions = new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb);
            DecompilationUnit unit = new DecompilationUnit();

            var assemblyStream = new MemoryStream();
            var pdbStream = new MemoryStream();
            var compilationResult = compilation.Emit(assemblyStream, pdbStream: pdbStream, options: emitOptions);

            return new CompilationUnit()
            {
                SourceCode = sourceCode,
                AssemblyStream = assemblyStream,
                PDBStream = pdbStream,
                CompilationResult = compilationResult
            };
        }

        //
        // Parse and rewrite code to provide some extra utility when like
        // Running / Benchmark and Aliases on C# Methods and Classes.
        //
        private string RewriteCode(string code)
        {
            var benchCode = "";
            code = code.Replace("[NoInline]", "[MethodImpl(MethodImplOptions.NoInlining)]");

            var ast = CSharpSyntaxTree.ParseText(code);
            var root = ast.GetRoot();

            foreach (var node in root.DescendantNodes())
            {
                if (node.IsKind(SyntaxKind.InvocationExpression))
                {
                    var invocation = (InvocationExpressionSyntax)node;
                    if (invocation?.Expression?.ToString() == "Print")
                    {
                        var printCall = invocation.Expression.ToString();
                        var arg = invocation.ArgumentList.Arguments[0].ToString();
                        if (invocation.ArgumentList.Arguments[0].Expression.Kind() == SyntaxKind.IdentifierName)
                        {
                            var newPrintCall = $"{printCall}({arg},nameof({arg}));";
                            var oldPrintCall = $"{printCall}({arg});";
                            code = code.Replace(oldPrintCall, newPrintCall);
                        }
                    }
                }
                else if (node.IsKind(SyntaxKind.LocalFunctionStatement))
                {
                    var func = (LocalFunctionStatementSyntax)node;
                    if (func.AttributeLists.Count > 0)
                    {
                        foreach (var list in func.AttributeLists)
                        {
                            if (list.Attributes.FirstOrDefault(x => x.Name.ToString() == "Bench") != null)
                            {
                                //
                                // Generate benchmark function
                                //
                                var functionName = func.Identifier.ValueText;
                                //
                                // Add Bench Code
                                //
                                benchCode += $@"
                                    public long Bench_{functionName}() 
                                    {{ 
                                        Stopwatch w = new Stopwatch();
                                        for(int i = 0; i < 1000; i++) {functionName}();
                                        w.Start();
                                        for(int i = 0; i < 1000; i++) {functionName}();
                                        w.Stop();
                                        return w.ElapsedMilliseconds;
                                    }}

                                    ";
                            }
                        }
                    }
                }
            }

            var sourceCode = $@"
                    using System.Linq;
                    using System.Runtime.CompilerServices;
                    using System.Diagnostics;
                    using System;
                    using System.Runtime.CompilerServices;
                    using System.Collections.Generic;
                    using System.Collections;
                    using System.Linq.Expressions;

                    [System.AttributeUsage(System.AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
                    sealed class BenchAttribute : Attribute
                    {{
                        public BenchAttribute()
                        {{
                        }}
                    }}

                    [System.AttributeUsage(System.AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
                    sealed class RunAttribute : Attribute
                    {{
                        public RunAttribute()
                        {{
                        }}
                    }}


                    public class _Log 
                    {{
                        public static List<string> print = new();
                    }}

                    public class CompilerGen
                    {{
                        {code}
                        {benchCode}

                        static void Print(object o, string name = ""<expr>"") 
                        {{
                            if(o is string str)
                            {{
                              _Log.print.Add(name + "" => "" + o.ToString());
                            }}
                            else if(o is IEnumerable arr)
                            {{
                              string message = name + "" => "" + ""["";
                              foreach (var value in arr)
                              {{
                                  message += value + "", "";
                              }}
                              message = message.Remove(message.Length - 2);
                              message += ""]"";
                              _Log.print.Add(message);
                           }}
                           else
                             _Log.print.Add(name + "" => "" + o.ToString());
                        }}

                    }}";

            return sourceCode;
        }
    }
}
