using Microsoft.Extensions.Configuration;
using PowerUp.Core.Console;
using PowerUp.Core.Decompilation;
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace PowerUp.Watcher
{ 
    class Program
    {
        static unsafe void Main(string[] args)
        {
            PrintTitle();

            args = TryReadingRunCommandFile(args);

            XConsole.WriteLine($"`Input >>` {string.Join(" ", args)}\r\n");

            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetParent(AppContext.BaseDirectory).FullName)
                .AddJsonFile("appsettings.json", false)
                .Build();

            TryAugmentConfiguration(configuration);

            ConsoleParser parser = new ConsoleParser();
            parser.RegisterCommand<CSharpWatcher, CSharpWatcherOptions>("cs",
                new CSharpWatcher(configuration, false),
                new CSharpWatcherOptions(),
                (x, y) => x.WatchFile(y.CSharpInput, y.AsmOutput, y.ILOutput, y.CSharpOutput));

            parser.RegisterCommand<FSharpWatcher, FSharpWatcherOptions>("fs",
                new FSharpWatcher(configuration, false),
                new FSharpWatcherOptions(),
                (x, y) => x.WatchFile(y.FSharpInput, y.AsmOutput, y.ILOutput, y.CSharpOutput));

            parser.RegisterCommand<GOWatcher, GoWatcherOptions>("go",
                new GOWatcher(configuration),
                new GoWatcherOptions(),
                (x, y) => x.WatchFile(y.GOInput, y.AsmOutput));

            parser.RegisterCommand<RustWatcher, RustWatcherOptions>("rs",
                new RustWatcher(configuration),
                new RustWatcherOptions(),
                (x, y) => x.WatchFile(y.RustInput, y.AsmOutput));

            parser.RegisterCommand<CppWatcher, CppWatcherOptions>("cpp",
                new CppWatcher(configuration),
                new CppWatcherOptions(),
                (x, y) => x.WatchFile(y.CppInput, y.AsmOutput));

            var isOK = parser.Evaluate(args);

            ValidateConfiguration(configuration);

            if (isOK == false)
            {
                XConsole.WriteLine("\r\nYou can put multiple commands in a single program run");
                //
                // This is neet if you neat to test or run the tool frequently.
                //
                XConsole.WriteLine("\r\nYou can also provide a command file (run.cmd) and leave it in the program directory");
                XConsole.WriteLine("and that will get used as a command for the program");

                return;
            }
            //
            // Block the current thread, and let watchers work.
            //
            Console.ReadLine();
        }

        static void PrintTitle()
        {
            XConsole.WriteLine(
                XConsole.ConsoleBorderStyle.TopLeft +
                new string(XConsole.ConsoleBorderStyle.TopBottom, 21) +
                XConsole.ConsoleBorderStyle.TopRight
            );

            XConsole.WriteLine(XConsole.ConsoleBorderStyle.Left + "   `PowerUP Watcher`   " + XConsole.ConsoleBorderStyle.Right);

            XConsole.WriteLine(
                XConsole.ConsoleBorderStyle.BottomLeft +
                new string(XConsole.ConsoleBorderStyle.TopBottom, 21) +
                XConsole.ConsoleBorderStyle.BottomRight
            );
        }

        static string[] TryReadingRunCommandFile(string[] args)
        {
            var fileName = "run.cmd";
            if (File.Exists(fileName))
            {
                XConsole.WriteLine("`Pulling Args from run.cmd.`");

                args = File.ReadLines(fileName)
                    .Select(x => x.Trim())
                    .ToArray();
            }
            return args;
        }

        static void TryAugmentConfiguration(IConfigurationRoot configuration)
        {
            foreach (var kv in configuration.GetChildren())
            {
                //
                // Augment all * and try to find correct directories.
                //
                if(kv.Value.Contains("*"))
                {
                    //
                    // Get augmented value and reassign it to the configuration section.
                    //
                    var value = WatcherUtils.FindDirectoryWithPattern(kv.Value);
                    //
                    // When we have a file name instread of a directory, we need to remove the
                    // last separator that was added.
                    //
                    if (kv.Value.EndsWith(".dll")) 
                        value = value.Substring(0, value.Length - 1);   
                    
                    configuration[kv.Key] = value;
                }
            }
        }

        static void ValidateConfiguration(IConfigurationRoot configuration)
        {
            XConsole.WriteLine("`App Configuration:`");

            foreach(var kv in configuration.GetChildren())
            {
                XConsole.Write($"  `{kv.Key}`");
                //
                // If the config key has something to do with path, then check if this actually exists.
                //
                if (kv.Key.Contains("Path"))
                {
                    var value = kv.Value;
                    var hasExt = Path.HasExtension(value);
                    if (kv.Value.EndsWith("\\") || kv.Value.EndsWith("/"))
                    {
                        value = value.Substring(0, value.Length - 1);
                    }

                    if (hasExt == true && File.Exists(value) == false)
                    {
                        XConsole.WriteLine($" = '[Invalid or Missing]' Watchers might fail.");
                    }
                    else if (hasExt == false && Directory.Exists(value) == false)
                    {
                        XConsole.WriteLine($" = '[Invalid or Missing]' Watchers might fail.");
                    }
                    else
                    {
                        XConsole.WriteLine($" = {kv.Value}");
                    }
                }
                else
                {
                    XConsole.WriteLine($" = {kv.Value}");
                }
            }
        }
    }
}
