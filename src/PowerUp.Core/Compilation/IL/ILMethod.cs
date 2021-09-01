using System.Collections.Generic;

namespace PowerUp.Core.Compilation
{
    public class ILMethod
    {
        public int StackSize { get; set; }
        public string Returns { get; set; }
        public string Accessor { get; set; }
        public string Name { get; set; }
        public List<ILMethodArg> Args { get; set; } = new();
        public List<ILInst> Code { get; set; }      = new();
    }
}
