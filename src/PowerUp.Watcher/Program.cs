using Microsoft.Extensions.Configuration;
using PowerUp.Core.Console;
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
        static void Main(string[] args)
        {
            XConsole.WriteLine("\r\n=== PowerUP Watcher. ===\r\n");

            if (args.Length != 3)
            {
                XConsole.WriteLine("Missing Arguments:");
                XConsole.WriteLine(" 'arg1' = Path to C#  file");
                XConsole.WriteLine(" 'arg2' = Path to ASM file");
                XConsole.WriteLine(" 'arg3' = Path to IL  file");
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
