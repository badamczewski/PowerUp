using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PowerUp.Core.Errors
{
    public class Error
    {
        public string Id { get; set; }
        public string Message { get; set; }
        public int Position { get; set; }
        public int Line { get; set; }
        public string Trace { get; set; }
    }
}
