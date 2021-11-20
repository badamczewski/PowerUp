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
        // @TODO: BA Move this to it's own class once we expose more complex options
        // and parsing.
        //
        public static CompilationOptions ProcessCommandOptions(string code)
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
            CompilationOptions options = new CompilationOptions();

            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                var value = token.GetValue();
                if (value == "up:showGuides")
                {
                    options.ShowGuides = true;
                }
                else if (value == "up:showSource")
                {
                    options.ShowSourceMaps = true;
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
                            options.OptimizationLevel = int.Parse(argValue);
                        }
                    }
                }
                else if (value == "up:showASMDocs")
                {
                    options.ShowASMDocumentation = true;
                    // i + 1 = whitespace; i + 2 = word?
                    if (i + 2 < tokens.Count)
                    {
                        var argValue = ParseCommandArgument(tokens, i + 2, "offset");
                        if (argValue != null)
                        {
                            options.ASMDocumentationOffset = int.Parse(argValue);
                        }
                    }
                }

            }

            return options;
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
