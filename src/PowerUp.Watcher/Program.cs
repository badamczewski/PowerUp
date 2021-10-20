using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PowerUp.Watcher
{
    class Program
    {

        public static bool IsDebug
        {
            get
            {
#if DEBUG
            return true;
#else
                return false;
#endif
            }
        }

        static void Main(string[] args)
        {
            Console.WriteLine(".NET {0}", Environment.Version.ToString());
            Console.WriteLine(IsDebug ? "[DEBUG]" : "[RELEASE]");

            if (args.Length != 3)
            {
                Console.WriteLine("arg1 = Path to C#  file");
                Console.WriteLine("arg2 = Path to ASM file");
                Console.WriteLine("arg3 = Path to IL  file");
                return;
            }

            var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetParent(AppContext.BaseDirectory).FullName)
            .AddJsonFile("appsettings.json", false)
            .Build();

            Watcher w = new Watcher(configuration);
            var t = w.WatchFile(
                args[0],
                args[1],
                args[2]);
            t.Wait();
        }
    }
}
