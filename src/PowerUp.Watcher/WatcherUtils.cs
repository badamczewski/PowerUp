using PowerUp.Core.Compilation;
using PowerUp.Core.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PowerUp.Watcher
{
    public class WatcherUtils
    {
        public struct UpCommand
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public string[] Args { get; set; } = Array.Empty<string>();
            public static UpCommand Create(string name, string description = null)
            {
                return new UpCommand() { Name = name, Description = description };
            }
            public static UpCommand Create(string name, string description, params string[] args)
            {
                return new UpCommand() { Name = name, Description = description, Args = args };
            }
        }

        public static void StartCompilerProcess(string command)
        {
            System.Diagnostics.Process process = new System.Diagnostics.Process();
            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
            startInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = $"/C {command}";
            process.StartInfo = startInfo;
            process.Start();
            process.WaitForExit();
        }

        //
        // This array contains all of the up:commands
        // that we curently support, the list is different
        // for each compiler.
        //
        public static UpCommand[] upCommands = {
            UpCommand.Create("up:showGuides",   "Used to enable jump guides in the ASM outputs."),
            UpCommand.Create("up:showASMDocs",  "Used to turn on ASM code documentation.",        new[] { "offset" }),
            UpCommand.Create("up:optimization", "Used to set the compilation optimization level", new[] { "level" }),
            UpCommand.Create("up:shortAddr",    "Used to turn on ASM code documentation.",        new[] { "by" }),
            UpCommand.Create("up:showCode",     "Used to show source code maps, that map assembly instructions to source code."),
            UpCommand.Create("up:showHelp",     "Used to show help.")
        };

        //
        // @TODO: BA Move this to it's own class once we expose more complex options
        // and parsing.
        //
        public static CompilationOptions SetCommandOptions(string code, CompilationOptions optionsToSet)
        {
            XConsoleTokenizer xConsoleTokenizer = new XConsoleTokenizer();
            var tokens = xConsoleTokenizer.Tokenize(code);
            //
            // A command option for languages that don't support attributes in a nice
            // way is a comment (inspired by the GO lang compiler options) where we 
            // prefix it with 'up' (as PowerUP) followed by the command name and a list of
            // arguments:
            //
            //     //up:someName arg1 = 10
            // 
            // The main goal of this format was parsing simplicity.
            //
            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                var value = token.GetValue();

                if(value == "up:showHelp")
                {
                    optionsToSet.ShowHelp = true;
                }
                if (value == "up:showGuides")
                {
                    optionsToSet.ShowGuides = true;
                }
                else if (value == "up:showSource")
                {
                    optionsToSet.ShowSourceMaps = true;
                }
                //
                // This is compiler specific.
                //
                else if (value == "up:optimization")
                {
                    // i + 1 = whitespace; i + 2 = word?
                    if (i + 2 < tokens.Count)
                    {
                        var argValue = ParseCommandArgument(tokens, i + 2, "level");
                        if (argValue != null)
                        {
                            optionsToSet.OptimizationLevel = int.Parse(argValue);
                        }
                    }
                }
                else if (value == "up:showASMDocs")
                {
                    optionsToSet.ShowASMDocumentation = true;
                    // i + 1 = whitespace; i + 2 = word?
                    if (i + 2 < tokens.Count)
                    {
                        var argValue = ParseCommandArgument(tokens, i + 2, "offset");
                        if (argValue != null)
                        {
                            optionsToSet.ASMDocumentationOffset = int.Parse(argValue);
                        }
                    }
                }
                else if (value == "up:shortAddr")
                {
                    optionsToSet.ShortAddresses = true;
                    // i + 1 = whitespace; i + 2 = word?
                    if (i + 2 < tokens.Count)
                    {
                        var argValue = ParseCommandArgument(tokens, i + 2, "by");
                        if (argValue != null)
                        {
                            optionsToSet.AddressesCutByLength = int.Parse(argValue);
                        }
                    }
                }
            }

            return optionsToSet;
        }

        public static CompilationOptions ProcessCommandOptions(string code)
        {
            var options = new CompilationOptions();
            return SetCommandOptions(code, options);
        }

        private static string ParseCommandArgument(List<Token> tokens, int index, string name)
        {
            var token = tokens[index];
            if (token.GetValue() == name)
            {
                index++;
                //
                // Find the OP
                //
                if (index < tokens.Count)
                {
                    if (tokens[index] is WhitespaceToken) index++;
                    //
                    // We have the OP, find the RValue
                    //
                    if (index < tokens.Count && tokens[index].GetValue() == "=")
                    {
                        index++;
                        var next = tokens[index];
                        if (index < tokens.Count && next is WhitespaceToken) index++;

                        if (index < tokens.Count)
                            return tokens[index].GetValue();
                    }
                }
            }
            return null;
        }

    }
}
