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
        private bool _disposeStreams = false;

        public ILDecompiler(MemoryStream assemblyStream, MemoryStream pdbStream, bool disposeStreams = false)
        {
            _assemblyStream = assemblyStream;
            _pdbStream = pdbStream;
            _disposeStreams = disposeStreams;
        }
        public ILToken[] Decompile(DebugInfoProvider sourceMapProvider = null)
        {
            StringBuilder ilBuilder = new StringBuilder();
            TextWriter ilWriter = new StringWriter(ilBuilder);

            _assemblyStream.Position = 0;
            _pdbStream.Position = 0;

            List<ILToken> il = new List<ILToken>();
            var peOptions = _disposeStreams == false ? PEStreamOptions.LeaveOpen : PEStreamOptions.Default;

            using (PEFile pEFile = new PEFile("", _assemblyStream, streamOptions: peOptions))
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

        public ILToken[] Decompile(MethodInfo methodInfo, DebugInfoProvider sourceMapProvider = null)
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

        public ILToken[] Decompile(Type type, DebugInfoProvider sourceMapProvider = null)
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
}
