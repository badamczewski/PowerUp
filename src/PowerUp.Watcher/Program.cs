using Microsoft.Extensions.Configuration;
using PowerUp.Core.Console;
using PowerUp.Core.Decompilation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PowerUp.Watcher
{
    class Program
    {
        static void Main(string[] args)
        {
            var commands = ParseArguments(args);

            XConsole.WriteLine("\r\n=== `PowerUP Watcher` ===\r\n");

            if (ValidateCommands(commands) == false) return;

            var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetParent(AppContext.BaseDirectory).FullName)
            .AddJsonFile("appsettings.json", false)
            .Build();

            foreach (var command in commands)
            {
                if (command.Name == "cs")
                {
                    CSharpWatcher w = new CSharpWatcher(configuration, false);
                    _ = w.WatchFile(
                        command.Arguments[0],
                        command.Arguments[1],
                        command.Arguments[2]);
                }
                else if (command.Name == "go")
                {
                    GOWatcher goWatcher = new GOWatcher(configuration);
                    _ = goWatcher.WatchFile(
                        command.Arguments[0],
                        command.Arguments[1]);
                }
            }

            //
            // Block the current thread, and let watchers work.
            //
            Console.ReadLine();
        }

        static bool ValidateCommands(List<Command> commands)
        {
            bool isOK = true;

            if (commands.Count == 0)
            {
                XConsole.WriteLine("'No Watcher Arguments provided.'");
                XConsole.WriteLine("A valid argument has a command like: `-cs`, `-go`; followed by a list of arguments.\r\n");
                XConsole.WriteLine("Example:");
                XConsole.WriteLine("`-cs` 'C:\\code.cs C:\\out.asm C:\\out.il' `-go` 'C:\\code.go C:\\out.asm'\r\n\r");
                isOK = false;
            }

            foreach (var command in commands)
            {
                if (command.Name == "cs" && command.Arguments.Count < 3)
                {
                    //
                    // Figure out what's wrong.
                    //
                    XConsole.WriteLine("`CSharp Watcher` is Missing Arguments:");

                    if (command.Arguments.Count == 2)
                        XConsole.WriteLine(" 'arg3' is missing");
                    else if (command.Arguments.Count == 1)
                        XConsole.WriteLine("'arg2' and 'arg3' are missing");
                    else if (command.Arguments.Count == 0)
                        XConsole.WriteLine("'arg1', 'arg2' and 'arg3' are missing");

                    XConsole.WriteLine("\r\nHelp:");
                    XConsole.WriteLine(" 'arg1' = Path to C#  file");
                    XConsole.WriteLine(" 'arg2' = Path to ASM file");
                    XConsole.WriteLine(" 'arg3' = Path to IL  file");
                    XConsole.WriteLine("");
                    isOK = false;

                }
                else if (command.Name == "go" && command.Arguments.Count < 2)
                {
                    //
                    // Figure out what's wrong.
                    //
                    XConsole.WriteLine("`GO Watcher` is Missing Arguments:");

                    if (command.Arguments.Count == 1)
                        XConsole.WriteLine("'arg1' is missing");
                    else if (command.Arguments.Count == 0)
                        XConsole.WriteLine("arg1' and 'arg2' are missing");

                    XConsole.WriteLine("\r\nHelp:");
                    XConsole.WriteLine(" 'arg1' = Path to GO   file");
                    XConsole.WriteLine(" 'arg2' = Path to ASM  file");
                    XConsole.WriteLine("");

                    isOK = false;
                }
            }
            return isOK;
        }

        static List<Command> ParseArguments(string[] args)
        {
            //
            // Parse Console Arguments:
            //   
            // The argument string will be split into multiple commands with their arg lists
            // The list has unlimited since and we should be able to use the same compiler multiple
            // times but with different arguments.
            //
            // Example: -cs C:\\code.cs C:\\out.asm C:\\out.il -go C:\\code.go C:\\out.asm
            // 
            // The output should be a list of Watchers with provided arguments:
            // 1) CSharpWatcher (code.cs, out.asm, out.il)
            // 2) GoWatcher     (code.go, out.asm)
            // 
            List<Command> commands = new List<Command>();  
            Command command = null;
            for(int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                //
                // Check for command:
                //
                if (arg.StartsWith('-'))
                {
                    //
                    // Read Arguments
                    //
                    command = new Command();
                    command.Name = arg.Substring(1);
                    commands.Add(command);
                }
                else
                {
                    if (command == null)
                        break;

                    command.Arguments.Add(arg);
                }
            }
            return commands;
        }
    }

    public class Command
    {
        public string Name { get; set; }
        public List<string> Arguments { get; set; } = new();
    }
}
