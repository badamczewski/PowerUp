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

        public string Decompile(Type type, DebugInfoProvider sourceMapProvider = null)
        {
            _assemblyStream.Position = 0;
            _pdbStream.Position = 0;

            var settings = new ICSharpCode.Decompiler.DecompilerSettings(ICSharpCode.Decompiler.CSharp.LanguageVersion.CSharp1)
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
                return disassembler.DecompileTypeAsString(new FullTypeName(type.FullName));
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
