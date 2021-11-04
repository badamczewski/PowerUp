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

            if (ValidateArgs(args) == false) return;

            var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetParent(AppContext.BaseDirectory).FullName)
            .AddJsonFile("appsettings.json", false)
            .Build();

            Watcher w = new Watcher(configuration, false);
            var t = w.WatchFile(
                args[0],
                args[1],
                args[2]);
            t.Wait();
        }

        static bool ValidateArgs(string[] args)
        {
            if (args.Length < 3)
            {
                //
                // Figure out what's wrong.
                //
                XConsole.WriteLine("Missing Arguments:");

                if (args.Length == 2)
                    XConsole.WriteLine(" 'arg3' is missing");
                else if (args.Length == 1)
                    XConsole.WriteLine("'arg2' and 'arg3' are missing");
                else if (args.Length == 0)
                    XConsole.WriteLine("'arg1', 'arg2' and 'arg3' are missing");

                XConsole.WriteLine("\r\nHelp:");
                XConsole.WriteLine(" 'arg1' = Path to C#  file");
                XConsole.WriteLine(" 'arg2' = Path to ASM file");
                XConsole.WriteLine(" 'arg3' = Path to IL  file");
                return false;
            }

            return true;
        }
    }
}
