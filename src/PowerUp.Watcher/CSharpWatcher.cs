﻿using ICSharpCode.Decompiler;
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
using System.Collections;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using CompilationOptions = PowerUp.Core.Compilation.CompilationOptions;

namespace PowerUp.Watcher
{
    public class CSharpWatcher
    {
        private IConfigurationRoot _configuration;

        private CSharpCodeCompiler _compiler = null;
        private ILCompiler _iLCompiler = new ILCompiler();

        private bool _unsafeUseTieredCompilation = false;

        // $env:DOTNET_TC_QuickJitForLoops = 1
        // $env:DOTNET_TieredPGO = 1
        private bool _isPGO = false;
             
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

        public CSharpWatcher(IConfigurationRoot configuration, bool unsafeUseTieredCompilation = false)
        {
            _configuration = configuration;
            _unsafeUseTieredCompilation = unsafeUseTieredCompilation;
        }

        private void Initialize(string csharpFile, string outAsmFile, string outILFile, string outCsFile)
        {
            XConsole.WriteLine("CSharp Watcher Initialize:");

            InitializeCsharpCompiler();

            XConsole.WriteLine($"`Input File`: {csharpFile}");
            XConsole.WriteLine($"`ASM   File`: {outAsmFile}");
            XConsole.WriteLine($"`IL    File`: {outILFile}");
            XConsole.WriteLine($"`CS    File`: {outCsFile}");

            if (File.Exists(csharpFile) == false)
                XConsole.WriteLine("'[WARNING]': Input File doesn''t exist");

            if (File.Exists(outAsmFile) == false)
                XConsole.WriteLine("'[WARNING]': ASM File doesn''t exist");

            if (File.Exists(outILFile) == false)
                XConsole.WriteLine("'[WARNING]': IL File doesn''t exist");

            if (File.Exists(outCsFile) == false)
                XConsole.WriteLine("'[WARNING]': Lowered CSharp File doesn''t exist");

            XConsole.WriteLine($"`Libs  Path`: {_compiler.DotNetCoreDirPath}");

            if(Directory.Exists(_compiler.DotNetCoreDirPath) == false)
            {
                XConsole.WriteLine($"'Cannot find the libs under Path: {_compiler.DotNetCoreDirPath}");
                XConsole.WriteLine($"Using '{System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()}'");
                _compiler.DotNetCoreDirPath = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
            }

            //
            // Check the DOTNET Enviroment variables and report them as well
            // since they are special configuration flags that influence the ASM outputs
            // like PGO and others.
            //
            XConsole.WriteLine("`.NET Enviromment variables:`");
            var env = Environment.GetEnvironmentVariables();
            foreach(DictionaryEntry entry in env)
            {
                //?? Will this ever happen
                if (entry.Key != null)
                {
                    var key = entry.Key.ToString();
                    if (key.StartsWith("DOTNET"))
                    {
                        XConsole.WriteLine($"  `{entry.Key}` = {entry.Value}");
                        if(key == "DOTNET_TieredPGO")
                        {
                            _isPGO = true;
                        }
                        //
                        // WE don't need it right now, we have collected it in the past
                        // but I'm not sure if there's a good reason for it now.
                        // TBD if this should go away for good.
                        //
                        //else if(key == "DOTNET_TC_QuickJitForLoops")
                        //{
                        //    _isQuickJITLoops = true;
                        //}
                    }
                }
            }

            XConsole.WriteLine($"`Language  Version`: {_compiler.LanguageVersion.ToDisplayString()}");
            XConsole.WriteLine($"`.NET Version`: {Environment.Version.ToString()}");
            XConsole.WriteLine(IsDebug ? "'[DEBUG]'" : "`[RELEASE]`");
        }


        //
        // @TODO @DESIGN: Not sure if I like this.
        // All watchers should be albe to run on any installed runtime and this should
        // be a dynamic process (the user should be albe to use a command flag)
        // 
        // To be able to do this, the C# and F# (dotnet) watchers would need to work by calling
        // a seperate decompilation / dissasembly process.
        //
        public void InitializeCsharpCompiler()
        {
            if(Environment.Version.Major == 3)
            {
                _compiler = new CSharpCodeCompiler(_configuration["DotNetCoreDirPathNet3"]);
            }
            else if (Environment.Version.Major == 5)
            {
                _compiler = new CSharpCodeCompiler(_configuration["DotNetCoreDirPathNet5"]);
            }
            else if (Environment.Version.Major == 6)
            {
                _compiler = new CSharpCodeCompiler(_configuration["DotNetCoreDirPathNet6"], LanguageVersion.CSharp9);
            }
            else if (Environment.Version.Major == 7)
            {
                _compiler = new CSharpCodeCompiler(_configuration["DotNetCoreDirPathNet7"], LanguageVersion.CSharp10);
            }
            else
            {
                _compiler = new CSharpCodeCompiler(_configuration["DotNetCoreDirPathDefault"]);
            }
        } 

        public Task WatchFile(string csharpFile, string outAsmFile = null, string outILFile = null, string outCSFile = null)
        {
            Initialize(csharpFile, outAsmFile, outILFile, outCSFile);

            string lastCode = null;
            DateTime lastWrite = DateTime.MinValue;
            var iDontCareAboutThisTask = Task.Run(async () => {
                while (true)
                {
                    try
                    {
                        FileInfo fileInfo = new FileInfo(csharpFile);
                        if (fileInfo.LastWriteTime.Ticks > lastWrite.Ticks)
                        {
                            var code = File.ReadAllText(csharpFile);
                            if (string.IsNullOrEmpty(code) == false && lastCode != code)
                            {
                                DecompilationUnit unit = null;
                                XConsole.WriteLine($"Decompiling: {csharpFile}");

                                if (fileInfo.Extension == ".il")
                                {
                                    unit = DecompileToIL(code);
                                }
                                else
                                {
                                    unit = DecompileToASM(code);
                                }

                                lastWrite = fileInfo.LastWriteTime;
                                lastCode  = code;

                                string asmCode = string.Empty;
                                string ilCode  = string.Empty;
                                string csCode  = string.Empty;

                                if (unit.Errors.Length > 0)
                                {
                                    StringBuilder errorBuilder = new StringBuilder();
                                    foreach (var error in unit.Errors)
                                    {
                                        errorBuilder.AppendLine($"{error.Message} {error.Trace} {error.Position}");
                                        errorBuilder.AppendLine($"{Environment.NewLine}-----------------------------");
                                    }

                                    XConsole.WriteLine($"Writing Errors to: {outAsmFile}, {outILFile}, {outCSFile}");

                                    var errors = errorBuilder.ToString();

                                    WriteIfNotNullOrEmpty(outAsmFile, errors);
                                    WriteIfNotNullOrEmpty(outILFile,  errors);
                                    WriteIfNotNullOrEmpty(outCSFile,  errors);
                                }
                                else
                                {

                                    //
                                    if (TryWriteASM(unit, outAsmFile, out asmCode) == false)
                                    {
                                        XConsole.WriteLine($"Writing Errors to: {outAsmFile}");
                                    }
                                    //
                                    if (TryWriteIL(unit, out ilCode) == false)
                                    {
                                        XConsole.WriteLine($"Writing Errors to: {outILFile}");
                                    }
                                    // 
                                    //
                                    if (unit.OutputSourceCode != null)
                                    {
                                        csCode = unit.OutputSourceCode;
                                    }
                                    //
                                    // Pring Global Messages.
                                    //
                                    if(unit.Messages != null)
                                    {
                                        asmCode += 
                                            Environment.NewLine + 
                                            string.Join(Environment.NewLine, unit.Messages);
                                    }

                                    XConsole.WriteLine($"Writing Results to: {outAsmFile}, {outILFile}, {outCSFile}");

                                    //WriteIfNotNullOrEmpty(outAsmFile, asmCode);
                                    WriteIfNotNullOrEmpty(outILFile, ilCode);
                                    WriteIfNotNullOrEmpty(outCSFile, csCode);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        XConsole.WriteLine($"Writing Errors to: {outAsmFile}, {outILFile}, {outCSFile}");
                        //
                        // Report this back to the out files.
                        //
                        var exMessage = ex.ToString();
                        WriteIfNotNullOrEmpty(outAsmFile, exMessage);
                        WriteIfNotNullOrEmpty(outILFile,  exMessage);
                        WriteIfNotNullOrEmpty(outCSFile,  exMessage);
                    }

                    await Task.Delay(500);
                }  
            });
            return iDontCareAboutThisTask;
        }

        private bool TryWriteIL(DecompilationUnit unit, out string result)
        {
            bool isOK = false;
            result = string.Empty;

            if (unit.ILTokens != null)
            {
                try
                {
                    result = ToILString(unit);
                    isOK = true;
                }
                catch (Exception ex)
                {
                    result = ex.ToString();
                    isOK = false;
                }
            }

            return isOK;
        }

        private bool TryWriteASM(DecompilationUnit unit, string outAsmFile, out string result)
        {
            bool isOK = false;
            result = string.Empty;
            if (unit.DecompiledMethods != null)
            {
                //
                // Hide internal methods that get produced by the IL Compiler and C# Compiler
                //
                try
                {
                    HideInternalDecompiledMethods(unit.DecompiledMethods);
                    result = ToAsmString(unit, outAsmFile);
                    isOK = true;
                }
                catch (Exception ex)
                {
                    result = ex.ToString();
                    isOK = false;
                }
            }
            return isOK;
        }

        private void WriteCompilerVersion(OutputBuilder content, DecompilationUnit unit)
        {
            content.Append($"# C# {unit.OutputLanguageVersion} .NET: {Environment.Version.ToString()} {(_isPGO ? "PGO":"")}");
        }

        private void WriteIfNotNullOrEmpty(string filePath, string content)
        {
            if (string.IsNullOrEmpty(filePath) == false)
                File.WriteAllText(filePath, content);
        }

        public string ToILString(DecompilationUnit unit)
        {
            return new ILWriter().ToILString(unit);
        }
        private string ToLayout(TypeLayout[] typeLayouts)
        {
            int offsetPad = 8;
            int sizePad   = 30;
            int tableSize = 42;
            var headerTopBottom = new string(ConsoleBorderStyle.TopBottom, tableSize);
            var displayPadding  = new string(' ', 4);

            StringBuilder layoutBuilder = new StringBuilder();
            foreach (var typeLayout in typeLayouts)
            {
                if(typeLayout.IsValid == false)
                {
                    layoutBuilder.AppendLine($"# [WARN] {typeLayout.Name} {typeLayout.Message}");
                    continue;
                }

                displayPadding = new string(' ', 4);

                if(typeLayout.Message != null)
                    layoutBuilder.AppendLine($"# {typeLayout.Message}");

                layoutBuilder.AppendLine($"# {typeLayout.Name} Memory Layout. {(typeLayout.IsBoxed ? "\r\n# (struct sizes might be wrong since they are boxed to extract layouts)" : "")} ");
                layoutBuilder.AppendLine($"{(typeLayout.IsBoxed ? "struct" : "class")} {typeLayout.Name}");
                layoutBuilder.AppendLine("{");

                bool isHeaderEnd = false;
                bool hasHeader   = false;
                int index = 0;
                foreach (var fieldLayout in typeLayout.Fields)
                {
                    if (fieldLayout == null) continue;

                    layoutBuilder.Append(displayPadding);
                    //
                    // For reference types we need to include the Metadata
                    // which means that we will check if a collection of fields
                    // has a header flag set:
                    // 
                    // [0] - IsHeader = true  -> Render 'Metadata' string and top guides and the field  
                    // [1] - IsHeader = true  -> Render left and right guids and the field
                    // [2] - IsHeader = true  -> Render left and right guids and the field
                    // [3] - IsHeader = false -> Render 'Fields' string and bottom guides and then the field
                    // (Which will not be a part of the header table)
                    //
                    if (typeLayout.IsBoxed == false)
                    {
                        if (index == 0 && fieldLayout.IsHeader == true)
                        {
                            hasHeader = true;
                            layoutBuilder.AppendLine(@"Metadata:");
                            layoutBuilder.Append(displayPadding);
                            layoutBuilder.AppendLine(ConsoleBorderStyle.TopLeft + headerTopBottom + ConsoleBorderStyle.TopRight);
                            layoutBuilder.Append(displayPadding);
                            layoutBuilder.Append(ConsoleBorderStyle.Left + " ");
                        }
                        else if (index > 0 && fieldLayout.IsHeader == true)
                        {
                            layoutBuilder.Append(ConsoleBorderStyle.Left + " ");
                        }
                        else if (isHeaderEnd == false)
                        {
                            isHeaderEnd = true;
                            layoutBuilder.AppendLine(ConsoleBorderStyle.BottomLeft + headerTopBottom + ConsoleBorderStyle.BottomRight);
                            layoutBuilder.Append(displayPadding);
                            layoutBuilder.AppendLine(@"Fields:");
                            displayPadding += "  ";
                            layoutBuilder.Append(displayPadding);
                        }
                    }
                    else
                    {
                        if (index == 0)
                        {
                            layoutBuilder.AppendLine(@"Fields:");
                            displayPadding += "  ";
                            layoutBuilder.Append(displayPadding);
                        }
                    }

                    var offsetString  = $"[{fieldLayout.Offset}-{fieldLayout.Offset + fieldLayout.Size - 1}]";
                    var padBy         = offsetPad - offsetString.Length;
                    var line          = $"{offsetString} {new string(' ', padBy)} {fieldLayout.Type} {fieldLayout.Name}";
                    var sizeInBytesPad = "";

                    //
                    // Pad the (X bytes) string such that it's all nicley 
                    // aligned when we render multiple lines on screen.
                    //
                    if (sizePad - line.Length > 0)
                    {
                        sizeInBytesPad = new string(' ', sizePad - line.Length);
                    }
                    var sizeInBytes  = $"{sizeInBytesPad}({fieldLayout.Size} bytes)";
                    layoutBuilder.Append(line + sizeInBytes);

                    //
                    // Create the right hand size of the table ('|') with the correct pad and placement  
                    //
                    if (index >= 0 && fieldLayout.IsHeader == true)
                    {
                        var toPad = tableSize - (line.Length + sizeInBytes.Length) - 1;
                        layoutBuilder.Append(new String(' ', toPad) + ConsoleBorderStyle.Left);
                    }

                    layoutBuilder.AppendLine();

                    index++;
                }

                //
                // If we have only the header then we need to put the closing 
                // table border.
                //
                if (hasHeader && isHeaderEnd == false)
                {
                    isHeaderEnd = true;
                    layoutBuilder.Append(displayPadding);
                    layoutBuilder.AppendLine(ConsoleBorderStyle.BottomLeft + headerTopBottom + ConsoleBorderStyle.BottomRight);
                }

                layoutBuilder.AppendLine($"    Size:    {typeLayout.Size} {(typeLayout.IsBoxed ? "# Estimated" : "")}");
                layoutBuilder.AppendLine($"    Padding: {typeLayout.PaddingSize} {(typeLayout.IsBoxed ? "# Estimated" : "")}");

                layoutBuilder.AppendLine("}");
                layoutBuilder.AppendLine();
            }

            return layoutBuilder.ToString();
        }

        public string ToAsmString(DecompilationUnit unit, string outAsmFile)
        {
            using var builder     = new OutputBuilder(outAsmFile);
            var lineBuilder = new StringBuilder();
            var writer      = new AssemblyWriter();

            if(unit.Options.ShowHelp)
            {
                writer.AppendHelp(builder);
                return builder.ToString();
            }

            if (unit.Options.HelpText != null)
            {
                builder.Append(unit.Options.HelpText);
                
                return builder.ToString();
            }

            WriteCompilerVersion(builder, unit);

            builder.AppendLine();

            if (unit.TypeLayouts != null && unit.TypeLayouts.Any())
            {
                builder.AppendLine(ToLayout(unit.TypeLayouts));
                builder.AppendLine();
            }

            var documentationOffset = unit.Options.ASMDocumentationOffset;

            //
            // If the option to cut addresses was selected we should set the cut length
            // before we write out the address using this writer.
            //
            if (unit.Options.ShortAddresses)
                writer.AddressCutBy = unit.Options.AddressesCutByLength;


            //
            // Diff selected methods.
            //
            if(unit.Options.Diff)
            {
                (var source, var target) = WatcherUtils.FindDiffMethods(unit);

                if (source != null && target != null)
                {
                    writer.DocumentationOffset = unit.Options.ASMDocumentationOffset;
                    writer.AppendDiff(builder, source, target, unit.Options.ShowASMDocumentation);
                }
            }

            //
            // Collect lines for each method and append them at the end.
            // This is a two pass system where first pass collects all of
            // the method lines (instructions), and the second pass is responsible
            // for appending them to the final method string builder.
            //
            // This allows us to implement automatic layouts on certain things like
            // documentation, and other things in the future.
            //
            List<string> methodLines = new List<string>();
            foreach (var method in unit.DecompiledMethods)
            {
                if (method == null || method.IsVisible == false) continue;

                methodLines.Clear();

                (int jumpSize, int nestingLevel) sizeAndNesting = (-1,-1);
                sizeAndNesting    = JumpGuideDetector.PopulateGuides(method);
                var inliningCalls = InlineDetector.DetectInlining(method);

                //
                // Print messages.
                //
                writer.AppendMessages(builder, method);
                //
                // Write out method signature.
                //
                // Don't show the base type method types.
                // (This is specific to the C# outputs)
                //
                if (method.TypeName == CSharpCodeCompiler.BaseClassName) method.TypeName = null;
                writer.AppendMethodSignature(builder, method);

                foreach(var inliningCall in inliningCalls)
                {
                    builder.AppendLine("  " + XConsole.ConsoleBorderStyle.Bullet + "inlined" + " " + inliningCall);
                }

                var firstInst = method.Instructions.First(x => x.Type == InstructionType.ASM);
                var baseAddr = firstInst.Address;

                double avgOffset = 0; 
                foreach (var inst in method.Instructions)
                {
                    lineBuilder.Clear();

                    if (inst.Type == InstructionType.Code && unit.Options.ShowSourceMaps == false) continue;

                    if (inst.Type == InstructionType.ASM && unit.Options.RelativeAddresses == true)
                    {
                        inst.Address    -= baseAddr;
                        inst.RefAddress -= baseAddr;
                    }

                    lineBuilder.Append("  ");
                    //
                    // Append Jump Guides if needed.
                    //
                    if (unit.Options.ShowGuides)
                    {
                        writer.AppendGuides(lineBuilder, inst, sizeAndNesting);
                    }
                    //
                    // Write out the address as a hex padded string.
                    //
                    writer.AppendInstructionAddress(lineBuilder, inst, zeroPad: true);
                    writer.AppendInstructionName(lineBuilder, inst);

                    int idx = 0;
                    foreach (var arg in inst.Arguments)
                    {
                        var isLast = idx == inst.Arguments.Length - 1;
                        writer.AppendArgument(lineBuilder, method, inst, arg, isLast);
                        idx++;
                    }
                    
                    avgOffset += lineBuilder.Length;
                    methodLines.Add(lineBuilder.ToString());
                }
                //
                // Check if ASM Docs are using automatic layouts.
                //
                if (unit.Options.ShowASMDocumentation && IsDocumentationAutoLayout(documentationOffset))
                {
                    //
                    // ASM Docs:
                    // 1. Compute Documentation Offset based on trimmed mean.
                    // 2. Compute mean and remove all lines that are > 2x mean
                    // 3. Find maximum line offset on the remaining lines.
                    //
                    // This is needed for automatic offsets, but we don't really want
                    // to always render at maximum offset, we need to be smart about outliers
                    // example:
                    // 
                    // MOV A,B                   Doc1
                    // MOV C,D                   Doc2
                    // CALL XYZ                  Doc3
                    // CALL VERY.LONG.NAME.HERE  Doc4
                    //
                    // We don't want to move all of the docs to the max here since one instruction is an outlier
                    // and we might move the docs off-screen, we want to have this:
                    //
                    // MOV A,B     Doc1
                    // MOV C,D     Doc2
                    // CALL XYZ    Doc3
                    // CALL VERY.LONG.NAME.HERE  Doc4                    
                    //
                    avgOffset /= methodLines.Count;
                    var maxOffset = ComputeDocumentationOffsetBasedOnTrimmedMean(avgOffset, methodLines);
                    unit.Options.ASMDocumentationOffset = maxOffset;
                }
                //
                // Make a second pass and append all of the lines to the final
                // string builder
                //
                int lineIdx = 0;
                foreach (var inst in method.Instructions)
                {
                    if (inst.Type == InstructionType.Code && unit.Options.ShowSourceMaps == false) continue;

                    //
                    // @TODO this is bad design, we should be using the string builder
                    // all the way without switching to string in between.
                    //
                    var line = methodLines[lineIdx];
                    lineBuilder.Clear();
                    lineBuilder.Append(line);
                    //
                    // Check for documentation.
                    //
                    if (unit.Options.ShowASMDocumentation)
                    {
                        writer.DocumentationOffset = unit.Options.ASMDocumentationOffset;
                        writer.AppendX86Documentation(lineBuilder, method, inst);
                    }

                    builder.Append(lineBuilder.ToString());
                    builder.AppendLine();

                    lineIdx++;
                }
            }

            builder.Write();
            return builder.ToString();
        }

        private int ComputeDocumentationOffsetBasedOnTrimmedMean(double avgOffset, List<string> methodLines)
        {
            var trim = 2 * avgOffset;
            int maxOffset = 0;
            foreach (var line in methodLines)
            {
                if (line.Length < trim && line.Length > maxOffset)
                    maxOffset = line.Length;
            }
            return maxOffset;
        }
        private bool IsDocumentationAutoLayout(int offset) => offset == 0;

        public DecompilationUnit DecompileToIL(string code)
        {
            var decompilationUnit = new DecompilationUnit();
            var compilationUnit   = _iLCompiler.Compile(code);

            if (compilationUnit.Errors.Count > 0)
            {
                //
                // Handle errors
                //
                decompilationUnit.Errors = compilationUnit.Errors.ToArray();
            }
            else
            {
                var result = compilationUnit
                    .CompiledType
                    .ToAsm(@private: true);
                decompilationUnit.DecompiledMethods = result;
            }

            return decompilationUnit;
        }

        public DecompilationUnit DecompileToASM(string code)
        {
            var unit = new DecompilationUnit();
            CompilationOptions options = new CompilationOptions();
            options = WatcherUtils.SetCommandOptions(code, options);

            //
            // Compile the Source Code and set both the Compiler Options
            // and options as comments.
            //
            var compilation = _compiler.Compile(code, options);
            //
            // Get the streams, note that we have to dispose them thus 'using'
            //
            using var assemblyStream = compilation.AssemblyStream;
            using var pdbStream = compilation.PDBStream;

            unit.Options = compilation.Options;
            unit.InputSouceCode        = compilation.SourceCode;
            unit.OutputLanguageVersion = compilation.LanguageVersion;
            var compilationResult = compilation.CompilationResult;

            XConsole.WriteLine($"Language Version: `{unit.OutputLanguageVersion}`");

            //
            // Create the collectible load context. This context will only compile to non-tiered
            // Optimized compilation if the collectible option is set to true, so we are leaving it
            // set to true, but if the compilation option is set to 1 then we will treat is as Debug
            // and change the flag, anything else is considered a Release No-Tier build.
            //
            var isOptimizedOnly = _unsafeUseTieredCompilation == false && compilation.Options.OptimizationLevel != 1;
            using (var ctx = new CollectibleAssemblyLoadContext(isOptimizedOnly))
            {
                if (compilation.CompilationResult.Success == false)
                {
                    List<Error> errors = new List<Error>();

                    foreach (var diag in compilationResult.Diagnostics)
                    {
                        errors.Add(new Error() { Id = diag.Id, Message = diag.GetMessage() });
                    }

                    unit.Errors = errors.ToArray();
                }
                else
                {                    
                    assemblyStream.Position = 0;
                    var loaded = ctx.LoadFromStream(assemblyStream);

                    List<DecompiledMethod> globalMethodList = new List<DecompiledMethod>();
                    List<TypeLayout> typesMemoryLayouts     = new List<TypeLayout>();
                    var compiledType = loaded.GetType(CSharpCodeCompiler.BaseClassName);

                    assemblyStream.Position = 0;
                    pdbStream.Position = 0;


                    //
                    // Add imported methods from the BCL to the global list. 
                    // @NOTE: Although you can add just about any .NET internal method to the list
                    // there is guarantee that it will dissasemble & decompile properly.
                    // Some internal classes/methods are already optimized or are handeled in a
                    // special way by the runtime.
                    //
                    AddImportedMethodsToGlobalList(unit.Options.ImportList, globalMethodList);
                    
                    //
                    // Get IL <-> Code Map.
                    // @TODO, @NOTE: This map is curently used to just go from X86 <-> C#
                    // for IL Source Code Map we are using a DebugInfo provider, which does the 
                    // same thing but it's a part of the ReflectionDisassembler (IL Decompiler) API
                    // which we do not control.
                    //
                    // This creates a bit of a confusion and complicates things so it would be good to just use
                    // a single map from.
                    //
                    using var codeMap = new ILToCodeMapProvider(unit.InputSouceCode, pdbStream);
                    var ilMethodMap   = codeMap.GetMap(compiledType);

                    //
                    // Debug Info Provider will be used by the CS and IL Decompilers to resolve variable
                    // names, and map sequence points.
                    //
                    using var sourceMapProvider = new DebugInfoProvider(pdbStream);

                    //
                    // Use the CSharp Code Decompiler to go from IL back to C#
                    // This will allow us to see what kinds of transformations took place in the IL
                    // code from the C# perspective (things like lowering, morphing, magic code, etc).
                    //
                    var decompiler = new CSharpDecompiler(assemblyStream, pdbStream);
                    var nativeSourceCode  = decompiler.Decompile(compiledType, sourceMapProvider, unit.Options);
                    unit.OutputSourceCode = nativeSourceCode;

                    //
                    // Get ASM code from the compiled type, and pass in the IL <-> Code map
                    // We shall use this map to correlate sequence points with IL <-> ASM map
                    // and produce X86 ASM to Source Code Map.
                    //
                    var decompiledMethods = compiledType.ToAsm(sourceCodeMap: ilMethodMap, @private: true);
                    //
                    // @NOTE: For now we are skipping global type layout since
                    // it is kina confusing, hovewer it would be good to have some
                    // configuration option to be able to display it as well.
                    //

                    //var typeLayout         = compiledType.ToLayout(@private: true);
                    //typesMemoryLayouts.Add(typeLayout);
                    //
                    // Get all nested types and decompile them as well.
                    //
                    var types = compiledType.GetNestedTypes();
                    foreach (var type in types)
                    {
                        if (type.IsInterface) continue;
                        if (type.IsEnum)      continue;

                        var innerMethods = type.ToAsm(@private: true);
                        var nestedLayout = type.ToLayout(@private: true);

                        AddMethodsToGlobalList(innerMethods, globalMethodList);
                        typesMemoryLayouts.Add(nestedLayout);
                    }
                    //
                    // Run operations such as Benchmarking, Running and Interactive printing.
                    // Since we don't want these operations and generated methods to be ouptuted
                    // to the IL and ASM outputs we need to hide them.
                    //
                    RunPostCompilationOperations(loaded, compiledType, decompiledMethods, typesMemoryLayouts);
                    AddMethodsToGlobalList(decompiledMethods, globalMethodList);

                    assemblyStream.Position = 0;
                    pdbStream.Position = 0;

                    //
                    // The code below creates a non collectible context (BAD) in order to be able to support
                    // features like PGO, QuickJIT and other future features that will recompile the method
                    // at lower Tier.
                    //
                    // Compile all of the methods in this type but collect only the ones that are on the list.
                    // - For QuickJIT we simply compile again and that should give us the quick version.
                    // - For PGO we need to run multiple times, but the idea here is that PGO methods need a Run or a Bench
                    //   attribute since a profile needs to be collected, so we need to run post operations on this method,
                    //   and hope that it will be optimized in time for us to show it.
                    //
                    using (var nestedCtx = new CollectibleAssemblyLoadContext(isCollectibleButAlwaysOptimized: false))
                    {
                        assemblyStream.Position = 0;
                        loaded = nestedCtx.LoadFromStream(assemblyStream);
                        //
                        // Recompile the type using collectible context.
                        // For Tiered Compilation this will produce the T0 QUICKJIT versions
                        // of methods.
                        //
                        // For PGO it will also inject the histogram table counter.
                        //
                        compiledType      = loaded.GetType(CSharpCodeCompiler.BaseClassName);
                        decompiledMethods = compiledType.ToAsm(@private: true);
                        //
                        // @NOTE: This is not optimial at all but for now let's go with this version since it's
                        // simple top-bottom code.
                        //
                        // Get all of the T0 decompiled methods and find the ones that have any attributes
                        // in the compilation map. QuickJIT methods are simple since we already have them so
                        // let's add them as a new decompiled method to our existing list.
                        // 
                        // For PGO we need to run such method multiple times and switch stacks if possible
                        // (to speed up the process) and only after we need to do another decompilation of the method
                        // to be able to get the T1 PGO codegen.
                        // 
                        // Let's check if PGO was enabled before doing anything since there's no point in running any code
                        // if the flag is not set.
                        //
                        var flags = BindingFlags.Public |
                                    BindingFlags.Static | 
                                    BindingFlags.Instance | 
                                    BindingFlags.NonPublic;
                        var methodInfos     = compiledType.GetMethods(flags);
                        var methodInfosDict = methodInfos.ToDictionary(m => m.Name);

                        foreach (var decompiledMethod in decompiledMethods)
                        {
                            if (unit.Options.CompilationMap.TryGetValue(decompiledMethod.Name, out var attribute))
                            {
                                if (attribute == "QuickJIT")
                                {
                                    //
                                    // No work is needed for Quick JIT but it's good to add a message
                                    // saying that it's Jitted.
                                    //
                                    decompiledMethod.Messages.Add("# QuickJIT");
                                    globalMethodList.Add(decompiledMethod);
                                }
                                else if (attribute == "PGO")
                                {
                                    List<string> messages = new List<string>();
                                    //
                                    // If PGO is not enabled then don't even bother running any methods.
                                    //
                                    if (_isPGO)
                                    {
                                        messages.Add("# [PGO]");
                                        RunPostCompilationOperations(loaded, compiledType, new[] { decompiledMethod }, null);
                                    }
                                    else
                                    {
                                        messages.Add("# [WARN] PGO is Disabled");
                                    }
                                    //
                                    // Decompile the method again
                                    //
                                    var afterPGO = methodInfosDict[decompiledMethod.Name].ToAsm();
                                    afterPGO.Messages.AddRange(messages);
                                    afterPGO.Messages.AddRange(decompiledMethod.Messages);
                                    //
                                    // Now we should have the optimal version of the code.
                                    // Add it to the list.
                                    // 
                                    // @TODO @NOTE: For some unknown reason the PGO version is always slower than the Optimized
                                    // version of the code, even when having a much better codegen.
                                    // I don't know why this happend but we need to investigate this.
                                    //
                                    globalMethodList.Add(afterPGO);
                                }
                            }
                        }
                    }
                    ILDecompiler iLDecompiler = new ILDecompiler(assemblyStream, pdbStream);
                    unit.ILTokens = iLDecompiler.Decompile(compiledType, sourceMapProvider);
                    unit.DecompiledMethods = globalMethodList.ToArray();
                    unit.TypeLayouts = typesMemoryLayouts.ToArray();
                    //
                    // Parse and detect method calls in IL to be able to do inline tracing
                    //
                    ILAnalyzer analyzer = new ILAnalyzer();
                    analyzer.Analyze(unit);
                }
            }

            return unit;
        }

        private void AddImportedMethodsToGlobalList(List<string> imports, List<DecompiledMethod> totalMethodsToAdd)
        {
            foreach (var import in imports)
            {
                var segments = import.Split('.', StringSplitOptions.RemoveEmptyEntries);
                int remove = 1;
                var lastSegment = segments.Last();

                var importedType = Type.GetType(String.Join(".", segments, 0, segments.Length - remove));
                foreach (var method in importedType.GetMethods())
                {
                    if (method.Name == lastSegment)
                    {
                        //
                        // Not all methods can be dissasembled, so skip those that can't
                        //
                        var asm = method.ToAsm();
                        if (asm.Instructions.Any() == true)
                        {
                            AddMethodsToGlobalList(new[] { asm }, totalMethodsToAdd);
                        }
                    }
                }
            }
        }

        private void AddMethodsToGlobalList(DecompiledMethod[] methods, List<DecompiledMethod> totalMethodsToAdd)
        {
            foreach (var method in methods)
            {
                if (method == null) continue;
                totalMethodsToAdd.Add(method);
            }
        }

        private void HideInternalDecompiledMethods(DecompiledMethod[] methods)
        {
            //
            // Some internal methods should not be displayed in X86 or IR.
            //
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name.StartsWith("Bench_"))       methods[i].IsVisible = false;
                else if (methods[i].Name == "Print")            methods[i].IsVisible = false;
                else if (methods[i].Name.StartsWith("SizeOf_")) methods[i].IsVisible = false;
            }
        }

        private void RunPostCompilationOperations(Assembly loadedAssembly, Type compiledType, DecompiledMethod[] decompiledMethods, List<TypeLayout> typeLayouts)
        {
            var methods = compiledType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            RunPostCompilationOperations(loadedAssembly, decompiledMethods, methods, typeLayouts);
        }
        private void RunPostCompilationOperations(Assembly loadedAssembly, DecompiledMethod[] decompiledMethods, MethodInfo[] methodInfos, List<TypeLayout> typeLayouts)
        {
            List<string> messages = new List<string>();
            int order = 1;

            var compiledLog = loadedAssembly.GetType("_Log");
            var instance    = loadedAssembly.CreateInstance(CSharpCodeCompiler.BaseClassName);
            foreach (var method in methodInfos)
            {
                if (method.Name.StartsWith("Bench_"))
                {
                    var methodUnderBenchmarkName = method.Name.Split("_")[1];
                    var found = decompiledMethods.FirstOrDefault(x => x.Name == methodUnderBenchmarkName);
                    if (found != null)
                    {
                        //
                        // Find the method under benchmark to extract it's attribute values.
                        //
                        (long took, int warmUpCount, int runCount) summary =
                           ((long, int, int))method.Invoke(instance, null);

                        messages.Add($"# [{order++}] ");
                        messages.Add($"# Method: {methodUnderBenchmarkName}");
                        messages.Add($"# Warm-up Count: {summary.warmUpCount} calls");
                        messages.Add($"# Took {summary.took} ms / {summary.runCount} calls");
                        messages.Add("# ");

                        found.Messages.AddRange(messages);
                        order++;

                        messages.Clear();
                    }
                }
                else if(method.Name.StartsWith("SizeOf_"))
                {
                    if (typeLayouts != null)
                    {
                        var methodUnderSizeName = method.Name.Split("_")[1];
                        methodUnderSizeName = $"{CSharpCodeCompiler.BaseClassName}+" + methodUnderSizeName;
                        var size = (int)method.Invoke(instance, null);

                        var layout = typeLayouts.FirstOrDefault(x => x.Name == methodUnderSizeName);
                        if (layout != null)
                        {
                            var diff = Math.Abs((int)(layout.Size - (ulong)size));
                            layout.Size = (ulong)size;

                            if (layout.Fields.Any())
                            {
                                layout.PaddingSize -= (ulong)diff;

                                var padding = layout.Fields.Last();
                                if (padding.Type == "Padding")
                                {
                                    padding.Size -= diff;
                                    if (padding.Size <= 0) layout.Fields[layout.Fields.Length - 1] = null;
                                }
                            }
                        }
                    }
                }
                else
                {
                    var attributes = method.GetCustomAttributes();
                    foreach (var attribute in attributes)
                    {
                        var name = attribute.GetType().Name;

                        if (name == "RunAttribute")
                        {
                            var found = decompiledMethods.FirstOrDefault(x => x.Name == method.Name);
                            if (found != null)
                            {
                                method.Invoke(instance, null);
                                var log = (List<string>)compiledLog.GetField("print", BindingFlags.Static | BindingFlags.Public).GetValue(null);

                                messages.AddRange(log);
                                found.Messages.AddRange(messages.ToArray());
                                order++;

                                messages.Clear();
                            }
                        }
                        else if (name == "HideAttribute")
                        {
                            for (int i = 0; i < decompiledMethods.Length; i++)
                            {
                                if (decompiledMethods[i].Name == method.Name)
                                {
                                    decompiledMethods[i].Instructions.Clear();
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
