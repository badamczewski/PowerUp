using PowerUp.Core.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PowerUp.Watcher
{
    //
    // @TODO Pershaps we should move it to core?
    //

    /// <summary>
    /// Simple Console parser that is able to take input arguments and generate special commands
    /// that can run functions.
    /// </summary>
    public class ConsoleParser
    {
        private List<ConsoleCommand> consoleCommands = new List<ConsoleCommand>();

        public bool Evaluate(string[] args)
        {
            if (args.Length == 0)
            {
                PrintNoArgumentsError();
                return false;
            }

            var commandArgs = ParseArguments(args);

            foreach (var command in commandArgs)
            {
                var cmd = Find(command.Name);
                if (cmd != null)
                {
                    bool isValid = true;

                    for (int i = 0; i < cmd.InputOptions.Count; i++)
                    {
                        var consoleOption = cmd.InputOptions[i];
                        int index = 0;
                        bool argFound = false;

                        foreach (var a in command.Arguments)
                        {
                            if (index == consoleOption.Position)
                            {
                                //
                                // Set Values in the Command Option from Console.
                                // 
                                var prop = cmd.InputProperties[i];
                                prop.SetValue(cmd.InputValues[1], a);
                                argFound = true;
                            }

                            index++;
                        }

                        if (consoleOption.IsRequired && argFound == false)
                        {
                            //
                            // Error
                            //
                            XConsole.WriteLine($" '{consoleOption.Name} is missing.'");
                            isValid = false;
                        }

                    }

                    if (isValid)
                    {
                        cmd.Call.DynamicInvoke(cmd.InputValues.ToArray());
                    }
                }
            }

            return true;
        }
      
        private void PrintNoArgumentsError()
        {
            XConsole.WriteLine("'[ERROR]' No Arguments Provided");
            PrintHelp();
        }

        private void PrintHelp()
        {
            XConsole.WriteLine("Console Commands:\r\n");
            foreach (var cmd in consoleCommands)
            {
                StringBuilder exampleBuilder = new StringBuilder();

                XConsole.WriteLine($" {XConsole.ConsoleBorderStyle.Left} -`{cmd.Command}`");
                foreach (var option in cmd.InputOptions.OrderBy(x => x.Position))
                {
                    XConsole.WriteLine($" {XConsole.ConsoleBorderStyle.Left}   {option.Name} {(option.IsRequired ? "'(Required)'" : "")}");
                    exampleBuilder.Append($"`{option.Example}` ");
                }
                exampleBuilder.Remove(exampleBuilder.Length - 1, 1);

                XConsole.WriteLine($" {XConsole.ConsoleBorderStyle.Left}Example: -`{cmd.Command}` {exampleBuilder.ToString()}\r\n");
            }
        }

        private ConsoleCommand Find(string command)
        {
            //
            // We are doing linear search because we allow multiple commands in a single call.
            // so we can have -cmd for different argument lists.
            //
            return consoleCommands.FirstOrDefault(x => x.Command == command);
        }

        private static List<Command> ParseArguments(string[] args)
       {
            //
            // Parse Console Arguments:
            //   
            // The argument string will be split into multiple commands with their arg lists
            // The list can be unlimited and we should be able to use the same compiler multiple
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
            for (int i = 0; i < args.Length; i++)
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

        public void RegisterCommand<TCaller,TOptions>(string commandKey, TCaller caller, TOptions options, Action<TCaller, TOptions> call)
        {
            //
            // Register a call that is going to be bound to the console arguments.
            //
            // Each console command has two pieces of data that needs to be bound;
            // 1. The options class that maps properties to console arguments
            // 2. The concreate class that will be used to fire a method that has been accociated with a given console command.
            // 
            // To do this we need a couple of things extracted like:
            // 1. Input Attributes - used for help and validation.
            // 2. PropertyInfos from the Options object.
            //
            var cc = new ConsoleCommand() { Command = commandKey };

            foreach (var property in options.GetType().GetProperties())
            {
                var attributes = property.GetCustomAttributes(true);
                var consoleInput = attributes.FirstOrDefault(x => x.GetType() == typeof(ConsoleInputAttribute));
                if (consoleInput != null)
                {
                    var input = (ConsoleInputAttribute)consoleInput;
                    cc.InputOptions.Add(input); 
                    cc.InputProperties.Add(property);
                }
            }

            cc.InputValues.Add(caller);
            cc.InputValues.Add(options);

            //
            // Take a generic delegate and convert it to a non generic one that we can store globaly for every single
            // type of option and concreate class.
            //
            var delegateCall = Delegate.CreateDelegate(call.GetType(), call.Target, call.Method);
            cc.Call = delegateCall;
          
            consoleCommands.Add(cc);
        }
    }
}
