using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PowerUp.Watcher
{
    class Program
    {
        static void Main(string[] args)
        {
            Watcher w = new Watcher();
            var t = w.WatchFile(
                args[0],
                args[1],
                args[2]);
            t.Wait();
        }
    }
}
