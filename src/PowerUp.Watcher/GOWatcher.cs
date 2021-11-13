using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Web;
using PowerUp.Core.Decompilation;
using System.Threading.Tasks;
using PowerUp.Core.Compilation;
using PowerUp.Core.Errors;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using BenchmarkDotNet.Reports;
using Microsoft.Extensions.Configuration;
using PowerUp.Core.Console;
using static PowerUp.Core.Console.XConsole;
using System.Diagnostics;
using TypeLayout = PowerUp.Core.Decompilation.TypeLayout;

namespace PowerUp.Watcher
{
    public class GOWatcher
    {
        private string _pathToGOCompiler;
        private IConfigurationRoot _configuration;

        public GOWatcher(IConfigurationRoot configuration)
        {
            _configuration = configuration;
        }

        private void Initialize(string goFile, string outAsmFile)
        {
            XConsole.WriteLine("GO Lang Watcher Initialize:");

            XConsole.WriteLine($"`Input File`: {goFile}");
            XConsole.WriteLine($"`ASM   File`: {outAsmFile}");

            if (File.Exists(goFile) == false)
                XConsole.WriteLine("'[WARNING]': Input File doesn't exist");

            if (File.Exists(outAsmFile) == false)
                XConsole.WriteLine("'[WARNING]': ASM File doesn't exist");

            _pathToGOCompiler = _configuration["GOCompilerPath"]; 

            if (_pathToGOCompiler.EndsWith(Path.DirectorySeparatorChar) == false)
            {
                _pathToGOCompiler += Path.DirectorySeparatorChar;
            }

            if(Directory.Exists(_pathToGOCompiler) == false)
                XConsole.WriteLine("'[WARNING]': Compiler Directory Not Found");


            XConsole.WriteLine($"`Compiler  Path`: {_pathToGOCompiler}");
        }

        public Task WatchFile(string goFile, string outAsmFile)
        {
            Initialize(goFile, outAsmFile);

            var command = $"{_pathToGOCompiler}go.exe tool compile -S {goFile} > {outAsmFile}";
            string lastCode = null;
            DateTime lastWrite = DateTime.MinValue;
            var iDontCareAboutThisTask = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        FileInfo fileInfo = new FileInfo(goFile);
                        if (fileInfo.LastWriteTime.Ticks > lastWrite.Ticks)
                        {
                            var code = File.ReadAllText(goFile);
                            if (string.IsNullOrEmpty(code) == false && lastCode != code)
                            {
                                XConsole.WriteLine($"Calling: {command}");

                                System.Diagnostics.Process process = new System.Diagnostics.Process();
                                System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
                                startInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                                startInfo.FileName = "cmd.exe";
                                startInfo.Arguments = $"/C {command}";
                                process.StartInfo = startInfo;
                                process.Start();
                                process.WaitForExit();

                                lastCode = code;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        XConsole.WriteLine($"Writing Errors to: {outAsmFile}");
                        //
                        // Report this back to the out files.
                        //
                        File.WriteAllText(outAsmFile, ex.ToString());
                    }

                    await Task.Delay(500);
                }
            });
            return iDontCareAboutThisTask;
        }
    }
}
