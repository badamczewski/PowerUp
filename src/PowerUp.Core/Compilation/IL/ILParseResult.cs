using PowerUp.Core.Errors;
using System.Collections.Generic;

namespace PowerUp.Core.Compilation
{
    public class ILParseResult
    {
        public List<ILClass> Classes { get; set; } = new List<ILClass>();
        public List<Error> Errors { get; set; } = new List<Error>();
    }
}
