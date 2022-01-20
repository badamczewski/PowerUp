using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using PowerUp.Core.Compilation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;

namespace PowerUp.Core.Decompilation
{
    public class CSharpDecompiler
    {
        private MemoryStream _assemblyStream;
        private MemoryStream _pdbStream;
        private bool _disposeStreams = false;

        public CSharpDecompiler(MemoryStream assemblyStream, MemoryStream pdbStream, bool disposeStreams = false)
        {
            _assemblyStream = assemblyStream;
            _pdbStream = pdbStream;
            _disposeStreams = disposeStreams;
        }

        public string Decompile(Type type, DebugInfoProvider sourceMapProvider = null, CompilationOptions options = null)
        {
            _assemblyStream.Position = 0;
            _pdbStream.Position = 0;

            var settings = new DecompilerSettings(ICSharpCode.Decompiler.CSharp.LanguageVersion.CSharp1)
            {
                UseLambdaSyntax = false,
                Deconstruction = false,
                ArrayInitializers = false,
                AutomaticEvents = false,
                DecimalConstants = false,
                FixedBuffers = false,
                UsingStatement = false,
                SwitchStatementOnString = false,
                LockStatement = false,
                NativeIntegers = false,
                ForStatement = false,
                ForEachStatement = false,
                SparseIntegerSwitch = false,
                DoWhileStatement = false,
                StringConcat = false,
                UseRefLocalsForAccurateOrderOfEvaluation = true,
                Discards = false,
                InitAccessors = true,
                FunctionPointers = true,
            };

            var peOptions = _disposeStreams == false ? PEStreamOptions.LeaveOpen : PEStreamOptions.Default;

            using (PEFile pEFile = new PEFile("", _assemblyStream, streamOptions: peOptions))
            {
                var disassembler = new ICSharpCode.Decompiler.CSharp.CSharpDecompiler(pEFile, new CSharpAssemblyResolver(), settings)
                {
                    DebugInfoProvider = sourceMapProvider
                };

                var ast = disassembler.DecompileType(new FullTypeName(type.FullName));

                var visitorOptions = FormattingOptionsFactory.CreateAllman();
                visitorOptions.IndentationString = "    ";

                using (StringWriter writer = new StringWriter())
                {
                    var visitor = new CSharpDecompilerVisitor(writer, visitorOptions, options);
                    visitor.VisitSyntaxTree(ast);
                    return writer.ToString();
                }
            }
        }

        //
        // Experimental visitor (rewriter) that has the ability to simplify
        // names of lambdas and closures.
        //
        public class CSharpDecompilerVisitor : CSharpOutputVisitor
        {
            private CompilationOptions _options;
            private string closureDefaultName = "<>c__DisplayClass";
            private string closureVariableDefaultName = "c__DisplayClass0";
            private string closureReplacementName = "Closure";
            private string closureVariableReplacementName = "lambda";
            public CSharpDecompilerVisitor(TextWriter textWriter, CSharpFormattingOptions formattingPolicy, CompilationOptions options) : base(textWriter, formattingPolicy)
            {
                _options = options;
            }

            public override void VisitIdentifierExpression(ICSharpCode.Decompiler.CSharp.Syntax.IdentifierExpression identifierExpression)
            {
                if (_options != null && _options.SimpleNames)
                {
                    if(identifierExpression.Identifier.StartsWith("<>"))
                    {
                        identifierExpression.Identifier = identifierExpression.Identifier.Replace("<>",String.Empty);
                    }

                    if (identifierExpression.Identifier.Contains(closureVariableDefaultName))
                    {
                        identifierExpression.Identifier = identifierExpression.Identifier.Replace(closureVariableDefaultName, closureVariableReplacementName);
                    }
                }
                base.VisitIdentifierExpression(identifierExpression);
            }

            public override void VisitVariableDeclarationStatement(ICSharpCode.Decompiler.CSharp.Syntax.VariableDeclarationStatement variableDeclarationStatement)
            {
                if (_options != null && _options.SimpleNames)
                {
                    if (variableDeclarationStatement.Type is ICSharpCode.Decompiler.CSharp.Syntax.SimpleType simpleType)
                    {
                        if (simpleType.Identifier.Contains(closureDefaultName))
                        {
                            simpleType.Identifier = simpleType.Identifier.Replace(closureDefaultName, closureReplacementName);
                        }
                    }

                    foreach (var variable in variableDeclarationStatement.Variables)
                    {
                        if (variable.Name.Contains(closureDefaultName))
                        {
                            variable.Name = variable.Name.Replace(closureVariableDefaultName, closureVariableReplacementName);
                        }
                    }
                }

                base.VisitVariableDeclarationStatement(variableDeclarationStatement);
            }

            public override void VisitMethodDeclaration(ICSharpCode.Decompiler.CSharp.Syntax.MethodDeclaration methodDeclaration)
            {
                base.VisitMethodDeclaration(methodDeclaration);
            }

            public override void VisitTypeDeclaration(ICSharpCode.Decompiler.CSharp.Syntax.TypeDeclaration typeDeclaration)
            {
                if (_options != null && _options.SimpleNames)
                {
                    if (typeDeclaration.Name.Contains(closureDefaultName))
                    {
                        typeDeclaration.Name = typeDeclaration.Name.Replace(closureDefaultName, closureReplacementName);
                    }
                }
                base.VisitTypeDeclaration(typeDeclaration);
            }
            public override void VisitSimpleType(ICSharpCode.Decompiler.CSharp.Syntax.SimpleType simpleType)
            {
                if (_options != null && _options.SimpleNames)
                {
                    if (simpleType.Identifier.Contains(closureDefaultName))
                    {
                        simpleType.Identifier = simpleType.Identifier.Replace(closureDefaultName, closureReplacementName);
                    }
                }
                base.VisitSimpleType(simpleType);
            }
        }

        //
        // Assembly resolver will resolve references to other libraries, 
        // and load symbols if needed. 
        //
        // @TODO @NOTE We curently don't need to resolve anything, any unresolved
        // references are simply prefixed with a global or left with a comment that the
        // decompiler could not resolve it, but as of now it hasn't been a problem.
        //
        public class CSharpAssemblyResolver : IAssemblyResolver
        {
            public CSharpAssemblyResolver()
            {
            }

            public PEFile Resolve(IAssemblyReference reference)
            {
                return null;
            }

            public Task<PEFile> ResolveAsync(IAssemblyReference reference)
            {
                PEFile peFile = null;
                return Task.FromResult(peFile);
            }

            public PEFile ResolveModule(PEFile mainModule, string moduleName)
            {
                throw new NotImplementedException();
            }

            public Task<PEFile> ResolveModuleAsync(PEFile mainModule, string moduleName)
            {
                throw new NotImplementedException();
            }
        }

    }
}
