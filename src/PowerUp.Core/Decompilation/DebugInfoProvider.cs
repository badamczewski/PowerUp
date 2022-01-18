using ICSharpCode.Decompiler.DebugInfo;
using PowerUp.Core.Compilation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

namespace PowerUp.Core.Decompilation
{

    /// <summary>
    /// Used by the ICsharpCode Decompiler to resolve variable
    /// names, and map sequence points.
    /// 
    /// What is a sequence point? 
    /// It's a struct that maps Source Code in a high level language 
    /// to it's intermediate representation in IL / IR
    /// </summary>
    public class DebugInfoProvider : IDisposable, IDebugInfoProvider
    {
        private Stream _pdbStream;
        private MetadataReaderProvider _metadataReaderProvider;
        private MetadataReader _metadataReader;

        public string Description => "";
        public string SourceFileName => CSharpCodeCompiler.BaseClassName;

        public DebugInfoProvider(Stream pdbStream)
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
                sequencePoints.Add(new ICSharpCode.Decompiler.DebugInfo.SequencePoint()
                {
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
