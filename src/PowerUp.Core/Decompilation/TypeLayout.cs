using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PowerUp.Core.Decompilation
{
    public class TypeLayout
    {
        public string Name { get; set; }
        public FieldLayout[] Fields { get; set; } = Array.Empty<FieldLayout>();
        public bool IsBoxed { get; set; }
        public ulong Size { get; set; }
        public ulong PaddingSize { get; set; }
        public bool IsValid { get; set; } = true;
        public string Message { get; set; }
    }

    public class FieldLayout
    {
        public bool IsHeader { get; set; }
        public int Offset { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public int Size { get; set; }
    }
}
