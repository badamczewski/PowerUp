using PowerUp.Core.Errors;
using System;
using System.Collections.Generic;

namespace PowerUp.Core.Compilation
{
    public class ILCompilationUnit
    {
        public Type CompiledType { get; set; }
        public List<Error> Errors { get; set; } = new();
    }
}
