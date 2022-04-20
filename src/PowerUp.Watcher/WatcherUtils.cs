using Microsoft.Extensions.Configuration;
using PowerUp.Core.Compilation;
using PowerUp.Core.Console;
using PowerUp.Core.Decompilation;
using System;
using System.Collections.Generic;
using System.IO;
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
            public string[] Args { get; set; }
            public static UpCommand Create(string name, string description = null)
            {
                return new UpCommand() { Name = name, Description = description, Args = Array.Empty<string>() };
            }
            public static UpCommand Create(string name, string description, params string[] args)
            {
                return new UpCommand() { Name = name, Description = description, Args = args ?? Array.Empty<string>() };
            }
        }

        public static (string[] messages, string[] errors) StartCompilerProcess(string command, string errorPattern = null)
        {
            List<string> msg = new List<string>();
            List<string> err = new List<string>();

            void Process_OutputDataReceived(object sender, System.Diagnostics.DataReceivedEventArgs e)
            {
                if (e.Data != null)
                    msg.Add(e.Data);
            }

            void Process_ErrorDataReceived(object sender, System.Diagnostics.DataReceivedEventArgs e)
            {
                if (e.Data != null)
                {
                    //
                    // For some of the compilers we need to look for an error message
                    // (I'm looking at you Rust) to be able to tell if the compilation even
                    // happened, so we detect these simple patterns and set the compilation error
                    // flag and return that as an error stream.
                    //
                    // Warnings and even normal messages will sometimes be pushed using the error stream
                    // but most of the time we are interested in handling them differently.
                    //
                    if(errorPattern != null && e.Data.StartsWith(errorPattern))
                    {
                        err.Add(e.Data);
                    }
                    else
                    {
                        msg.Add(e.Data);
                    }
                }
            }

            System.Diagnostics.Process process = new System.Diagnostics.Process();
            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
            startInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = $"/C {command}";
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            process.StartInfo = startInfo;

            process.OutputDataReceived += Process_OutputDataReceived;
            process.ErrorDataReceived  += Process_ErrorDataReceived;

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            process.WaitForExit();

            process.OutputDataReceived -= Process_OutputDataReceived;
            process.ErrorDataReceived  -= Process_ErrorDataReceived;

            return (msg.ToArray(), err.ToArray());
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

        public static (DecompiledMethod source, DecompiledMethod target) FindDiffMethods(DecompilationUnit unit)
        {
            //
            // Find Methods based on the argument names in the diff command.
            //
            DecompiledMethod source = null;
            DecompiledMethod target = null;
            foreach (var method in unit.DecompiledMethods)
            {
                var name = method.Name;

                if (name == unit.Options.DiffSource)
                {
                    source = method;
                }
                else if (name == unit.Options.DiffTarget)
                {
                    target = method;
                }

                if (source != null && target != null)
                    break;
            }

            return (source, target);
        }

        //
        // Take a configuration path such as:
        // "C:\\Program Files\\dotnet\\shared\\Microsoft.NETCore.App\\5.*"
        // or
        // "C:\\Program Files\\dotnet\\shared\\Microsoft.NETCore.App\\5.*\\"
        // And try to find the pattern based on the wildcard on the same level.
        //
        public static string FindDirectoryWithPattern(string value)
        {
            var pathConfig = value;
            var segments = pathConfig.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            string sep = Path.DirectorySeparatorChar.ToString();

            var path = "";
            for(int s = 0; s < segments.Length; s++)
            {
                var segment = segments[s];
                bool handeled = false;
                

                for (int i = 0; i < segment.Length; i++)
                {
                    var c = segment[i];
                    //
                    // Find the wildcard, and if found then record
                    // the index at which it has been found along with the correct segment id.
                    //
                    if (c == '*')
                    {
                        var dirs = Directory.EnumerateDirectories(path, segment, SearchOption.TopDirectoryOnly);
                        //
                        // Peek next segment, it must match.
                        //
                        string next = null;
                        if (s + 1 < segments.Length)
                            next = segments[s + 1];

                        if (next != null)
                        {
                            bool found = false;
                            foreach (var dir in dirs)
                            {
                                //
                                // If the next segment matches with one of the inner directories then simply append it
                                // and break the loop chain.
                                //
                                var innerDirs = Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly);

                                foreach (var innerDir in innerDirs)
                                {
                                    var dirName = Path.GetFileName(innerDir);
                                    if (dirName == next)
                                    {
                                        path += Path.GetFileName(dir) + sep;
                                        found = true;
                                        break;
                                    }
                                }

                                if (found) break;
                            }
                        }
                        else if(dirs.Any())
                        {
                            path += Path.GetFileName(dirs.First()) + sep;
                            
                        }
                        //
                        // This is a flag that informs the outer loop to not append the current segment to 
                        // the end of the path since it has already been done.
                        //
                        handeled = true;
                    }
                }

                if (handeled == false)
                {
                    path += segment + sep;
                }
            }

            return path;
        }
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
            // There also exists a more complicated command where we use multiple arguments:
            //
            //     //up:someName arg1 = "Hello", arg2 = "World"
            //
            // The main goal of this format was parsing simplicity.
            //
            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                var value = token.GetValue();
                //
                // Generic Options that are fit for all compilers.
                //
                if (value == "up:showHelp")
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
                else if (value == "up:simpleNames")
                {
                    optionsToSet.SimpleNames = true;
                }
                else if (value == "up:relativeAddr")
                {
                    optionsToSet.RelativeAddresses = true;
                }
                else if (value == "up:import")
                {
                    if (MatchNext(tokens, ref i, "Word"))
                    {
                        var argValue = ParseCommandArgument(tokens, ref i, "from");
                        if (argValue != null)
                        {
                            optionsToSet.ImportList.Add(argValue);
                        }
                    }
                }
                //
                // Future
                //
                else if (value == "up:diff")
                {
                    optionsToSet.Diff = true;

                    // i + 1 = whitespace; i + 2 = word?
                    if (MatchNext(tokens, ref i, "Word"))
                    {
                        var argValue = ParseCommandArgument(tokens, ref i, "source");
                        if (argValue != null)
                        {
                            optionsToSet.DiffSource = argValue;
                        }

                        if (MatchNext(tokens, ref i, "Separator") && MatchNext(tokens, ref i, "Word"))
                        {
                            argValue = ParseCommandArgument(tokens, ref i, "target");
                            if (argValue != null)
                            {
                                optionsToSet.DiffTarget = argValue;
                            }
                        }
                    }
                }
                //
                // This is compiler specific.
                //
                else if (value == "up:optimization")
                {
                    // i + 1 = whitespace; i + 2 = word?
                    if (MatchNext(tokens, ref i, "Word"))
                    {
                        var argValue = ParseCommandArgument(tokens, ref i, "level");
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
                    if (MatchNext(tokens, ref i, "Word"))
                    {
                        var argValue = ParseCommandArgument(tokens, ref i, "offset");
                        if (argValue != null)
                        {
                            if (argValue.Equals("auto", StringComparison.OrdinalIgnoreCase))
                            {
                                optionsToSet.ASMDocumentationOffset = 0;
                            }
                            else
                            {
                                optionsToSet.ASMDocumentationOffset = int.Parse(argValue);
                            }
                        }
                    }
                }
                else if (value == "up:shortAddr")
                {
                    optionsToSet.ShortAddresses = true;
                    // i + 1 = whitespace; i + 2 = word?
                    if (MatchNext(tokens, ref i, "Word"))
                    {
                        var argValue = ParseCommandArgument(tokens, ref i, "by");
                        if (argValue != null)
                        {
                            optionsToSet.AddressesCutByLength = int.Parse(argValue);
                        }
                    }

                }
            }

            return optionsToSet;
        }

        /// <summary>
        /// Match next token and if it's a match move the reference index 'i' to that token.
        /// This method will ignore whitespace tokens.
        /// </summary>
        /// <param name="tokens"></param>
        /// <param name="i"></param>
        /// <param name="matches"></param>
        /// <returns></returns>
        private static bool MatchNext(List<Token> tokens, ref int i, params string[] matches)
        {
            int move = 1;
            //
            // Ignore Whitespace. We don't need to do it in a loop
            // since our tokenizer captures repeating sequences so all
            // whitespaces are captured in a single token.
            //
            if (tokens[i + move] is WhitespaceToken)
                move++;

            if (i + move < tokens.Count)
            {
                if (matches.Contains(tokens[i + move].TokenName))
                {
                    i += move;
                    return true;
                }
            }
            return false;
        }

        public static CompilationOptions ProcessCommandOptions(string code)
        {
            var options = new CompilationOptions();
            return SetCommandOptions(code, options);
        }

        private static string ParseCommandArgument(List<Token> tokens, ref int index, string name)
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
