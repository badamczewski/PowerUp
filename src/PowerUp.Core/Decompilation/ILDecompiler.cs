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

namespace PowerUp.Core.Decompilation
{
    public class ILDecompiler
    {
        public ILToken[] ToIL(MemoryStream assemblyStream, MemoryStream pdbStream)
        {
            StringBuilder ilBuilder = new StringBuilder();
            TextWriter ilWriter = new StringWriter(ilBuilder);

            assemblyStream.Position = 0;
            pdbStream.Position = 0;

            List<ILToken> il = new List<ILToken>();
            using (PEFile pEFile = new PEFile("", assemblyStream))
            {
                var output = new ILCollector() { IndentationString = "    " };
                var disassembler = new ReflectionDisassembler(output, CancellationToken.None)
                {
                    ShowSequencePoints = true,
                };

                disassembler.WriteModuleContents(pEFile);
                il = output.ILCodes;
            }

            return il.ToArray();
        }
    }
}
