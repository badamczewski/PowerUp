using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.Disassembler;
using System.Threading;
using System.Reflection.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.DebugInfo;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using PowerUp.Core.Compilation;
using System.Reflection.PortableExecutable;

namespace PowerUp.Core.Decompilation
{
    public class ILDecompiler
    {
        private MemoryStream _assemblyStream;
        private MemoryStream _pdbStream;
        private string indent = "    ";

        public ILDecompiler(MemoryStream assemblyStream, MemoryStream pdbStream)
        {
            _assemblyStream = assemblyStream;
            _pdbStream = pdbStream;
        }
        public ILToken[] ToIL(ILSourceMapProvider sourceMapProvider = null)
        {
            StringBuilder ilBuilder = new StringBuilder();
            TextWriter ilWriter = new StringWriter(ilBuilder);

            _assemblyStream.Position = 0;
            _pdbStream.Position = 0;

            List<ILToken> il = new List<ILToken>();
            using (PEFile pEFile = new PEFile("", _assemblyStream))
            { 
                var output = new ILCollector() { IndentationString = indent };
                var disassembler = new ReflectionDisassembler(output, CancellationToken.None)
                {
                    ShowSequencePoints = true,
                    DebugInfo = sourceMapProvider
                };
                disassembler.WriteModuleContents(pEFile);
                il = output.ILCodes;
            }

            return il.ToArray();
        }

        public ILToken[] ToIL(MethodInfo methodInfo, ILSourceMapProvider sourceMapProvider = null)
        {
            StringBuilder ilBuilder = new StringBuilder();
            TextWriter ilWriter = new StringWriter(ilBuilder);

            _assemblyStream.Position = 0;
            _pdbStream.Position = 0;

            List<ILToken> il = new List<ILToken>();
            using (PEFile pEFile = new PEFile("", _assemblyStream))
            {
                var output = new ILCollector() { IndentationString = indent };
                var disassembler = new ReflectionDisassembler(output, CancellationToken.None)
                {
                    ShowSequencePoints = true,
                    DebugInfo = sourceMapProvider
                };
                disassembler.DisassembleMethod(pEFile, (MethodDefinitionHandle)GetHandle(methodInfo));
                il = output.ILCodes;
            }

            return il.ToArray();
        }

        public ILToken[] ToIL(Type type, ILSourceMapProvider sourceMapProvider = null)
        {
            StringBuilder ilBuilder = new StringBuilder();
            TextWriter ilWriter = new StringWriter(ilBuilder);

            _assemblyStream.Position = 0;
            _pdbStream.Position = 0;

            List<ILToken> il = new List<ILToken>();
            using (PEFile pEFile = new PEFile("", _assemblyStream))
            {
                var output = new ILCollector() { IndentationString = indent };
                var disassembler = new ReflectionDisassembler(output, CancellationToken.None)
                {
                    ShowSequencePoints = true,
                    DebugInfo = sourceMapProvider
                };
                disassembler.DisassembleType(pEFile, (TypeDefinitionHandle)GetHandle(type));
                il = output.ILCodes;
            }

            return il.ToArray();
        }

        private EntityHandle GetHandle(MemberInfo info)
        {
            using var metadataReaderProvider = MetadataReaderProvider.FromPortablePdbStream(_pdbStream);
            var reader = metadataReaderProvider.GetMetadataReader();
            return MetadataTokens.EntityHandle(info.MetadataToken);
        }
    }

    public class ILSourceMapProvider : IDisposable, IDebugInfoProvider
    {
        private Stream _pdbStream;
        private MetadataReaderProvider _metadataReaderProvider;
        private MetadataReader _metadataReader;

        public string Description => "";
        public string SourceFileName => CodeCompiler.BaseClassName;

        public ILSourceMapProvider(Stream pdbStream)
        {
            _pdbStream = pdbStream;
            _pdbStream.Position = 0;
            _metadataReaderProvider = MetadataReaderProvider.FromPortablePdbStream(_pdbStream);
            _metadataReader = _metadataReaderProvider.GetMetadataReader();
        }

        public IList<ICSharpCode.Decompiler.DebugInfo.SequencePoint> GetSequencePoints(MethodDefinitionHandle method)
        {
            List<ICSharpCode.Decompiler.DebugInfo.SequencePoint> sequencePoints = new();
            var info = _metadataReader.GetMethodDebugInformation(method);
            var points = info.GetSequencePoints();
           
            foreach (var point in points)
            {
                sequencePoints.Add(new ICSharpCode.Decompiler.DebugInfo.SequencePoint() {
                    Offset = point.Offset,
                    StartColumn = point.StartColumn,
                    StartLine = point.StartLine,
                    EndColumn = point.EndColumn,    
                    EndLine = point.EndLine,
                    DocumentUrl = "",
                });
            }

            return sequencePoints;
        }

        public IList<Variable> GetVariables(MethodDefinitionHandle method)
        {
            List<Variable> variables = new List<Variable>();
            foreach (var scopeHandle in _metadataReader.GetLocalScopes(method))
            {
                var scope = _metadataReader.GetLocalScope(scopeHandle);
                foreach (var variableHandle in scope.GetLocalVariables())
                {
                    var local = _metadataReader.GetLocalVariable(variableHandle);
                    variables.Add(new Variable(local.Index, _metadataReader.GetString(local.Name)));
                }
            }
            return variables;
        }

        public bool TryGetName(MethodDefinitionHandle method, int index, out string name)
        {
            name = null;
            foreach (var scopeHandle in _metadataReader.GetLocalScopes(method))
            {
                var scope = _metadataReader.GetLocalScope(scopeHandle);
                foreach (var variableHandle in scope.GetLocalVariables())
                {
                    var local = _metadataReader.GetLocalVariable(variableHandle);
                    if (index == local.Index)
                    {
                        name = _metadataReader.GetString(local.Name);
                        return true;
                    }
                }
            }
            return false;
        }

        public void Dispose()
        {
            _metadataReaderProvider.Dispose();
        }
    }
}
