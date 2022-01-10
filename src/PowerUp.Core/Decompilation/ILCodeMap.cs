using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PowerUp.Core.Decompilation
{
    public class ILMethodMap
    {
        public ulong MethodHandle { get; set; }
        public List<ILCodeMap> CodeMap { get; set; } = new List<ILCodeMap>();
    }

    public class ILCodeMap
    {
        public int Offset { get; set; }
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public int StartCol { get; set; }
        public int EndCol { get; set; }
        public string SourceCodeBlock { get; set; }
    }
}
