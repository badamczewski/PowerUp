using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PowerUp.Core.Decompilation.Attributes
{
    [System.AttributeUsage(
        AttributeTargets.Method | 
        AttributeTargets.Class  | 
        AttributeTargets.Struct, Inherited = false, AllowMultiple = true)]
    public class JITAttribute : Attribute
    {
        private Type[] _types;

        public JITAttribute(params Type[] types)
        {
            _types = types;
        }

        public Type[] Types
        {
            get { return _types; }
        }
    }
}
