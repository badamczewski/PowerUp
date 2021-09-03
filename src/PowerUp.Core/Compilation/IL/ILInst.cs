namespace PowerUp.Core.Compilation
{
    public class ILInst
    {
        public string Label { get; set; }
        public string OpCode { get; set; }
        public string[] Arguments { get; set; }

        //
        // 90% of opcodes have only a single argument.
        //
        public string GetFirstArg() {
            if (Arguments != null && Arguments.Length > 0)
                return Arguments[0];
            return null;
        }
    }

    public class ILCallInst : ILInst
    {
        public string TypeName { get; set; }
        public string MethodCallName { get; set; }
        public string AssemblyName { get; set; }
    }
}
