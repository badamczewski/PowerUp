using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;

namespace PowerUp.Core.Decompilation
{
    public class ILToCodeMapProvider : IDisposable
    {
        private MetadataReaderProvider _metadataReaderProvider;
        private MetadataReader _metadataReader;
        private string _sourceCode;
        public Stream PdbStream { get; private set; }
        public ILToCodeMapProvider(string sourceCode, Stream pdbStream)
        {
            _sourceCode = sourceCode;
            PdbStream = pdbStream;

            _metadataReaderProvider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
            _metadataReader = _metadataReaderProvider.GetMetadataReader();
        }

        public ILMethodMap[] GetMap(Type type, bool @private = false)
        {
            var flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
            if (@private)
            {
                flags |= BindingFlags.NonPublic;
            }
            var methodInfos = type.GetMethods(flags);
            List<ILMethodMap> methodMaps = new List<ILMethodMap>(methodInfos.Length);
            var codeLines = _sourceCode.Split(Environment.NewLine);

            foreach (var method in methodInfos)
            {
                //
                // Skip object built-in methods since we cannot get any sequence points
                // from these methods, and that's fine since we can't decompile them either.
                //
                if (method.DeclaringType != typeof(System.Object) &&
                    method.DeclaringType != typeof(System.ValueType))
                {
                    ILMethodMap methodMap = new ILMethodMap();
                    methodMap.MethodHandle = (ulong)method.MethodHandle.Value.ToInt64();

                    var points = GetSequencePoints(method);
                    foreach (var point in points)
                    {
                        //
                        // Hidden Sequence points have wonky values 
                        // that make no sense for us.
                        //
                        if (point.IsHidden) continue;

                        //
                        // Get the correct source code block.
                        //
                        var startLine = codeLines[point.StartLine - 1];
                        var endLine   = codeLines[point.EndLine - 1];
                        var value = "";
                        for (int lineId = point.StartLine - 1; lineId < point.EndLine; lineId++)
                        {
                            value += codeLines[lineId].Trim();
                        }

                        ILCodeMap entry = new ILCodeMap()
                        {
                            Offset = point.Offset,
                            StartLine = point.StartLine,
                            EndLine = point.EndLine,
                            StartCol = point.StartColumn,
                            EndCol = point.EndColumn,
                            SourceCodeBlock = value
                        };

                        methodMap.CodeMap.Add(entry);
                    }
                    methodMaps.Add(methodMap);
                }
            }

            return methodMaps.ToArray();
        }

        //
        // Get Sequence Points from method info.
        // What is a Sequence Point? It's a point that maps source code to IL in .NET
        //
        private SequencePointCollection GetSequencePoints(MethodInfo info)
        {
            var handle = (MethodDefinitionHandle)MetadataTokens.EntityHandle(info.MetadataToken);
            var debugInfo = _metadataReader.GetMethodDebugInformation(handle);
            return debugInfo.GetSequencePoints();
        }

        public void Dispose()
        {
            _metadataReaderProvider.Dispose();
        }
    }

}
