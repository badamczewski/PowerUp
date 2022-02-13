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
using System.Collections;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace PowerUp.Watcher
{
    public class FSharpWatcher
    {
        private IConfigurationRoot _configuration;

        private string _pathToCompiler;
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

        public FSharpWatcher(IConfigurationRoot configuration, bool unsafeUseTieredCompilation = false)
        {
            _configuration = configuration;
            _unsafeUseTieredCompilation = unsafeUseTieredCompilation;
        }

        private void Initialize(string fsharpFile, string outAsmFile, string outILFile, string outFsFile)
        {
            XConsole.WriteLine("FSharp Watcher Initialize:");

            XConsole.WriteLine($"`Input File`: {fsharpFile}");
            XConsole.WriteLine($"`ASM   File`: {outAsmFile}");
            XConsole.WriteLine($"`IL    File`: {outILFile}");
            XConsole.WriteLine($"`FS    File`: {outFsFile}");

            if (File.Exists(fsharpFile) == false)
                XConsole.WriteLine("'[WARNING]': Input File doesn''t exist");

            if (File.Exists(outAsmFile) == false)
                XConsole.WriteLine("'[WARNING]': ASM File doesn''t exist");

            if (File.Exists(outILFile) == false)
                XConsole.WriteLine("'[WARNING]': IL File doesn''t exist");

            if (File.Exists(outFsFile) == false)
                XConsole.WriteLine("'[WARNING]': FSharp File doesn''t exist");

            _pathToCompiler = _configuration["FSharpCompilerPath"];

            XConsole.WriteLine($"`Compiler  Path`: {_pathToCompiler}");

            //
            // Check the DOTNET Enviroment variables and report them as well
            // since they are special configuration flags that influence the ASM outputs
            // like PGO and others.
            //
            XConsole.WriteLine("`.NET Enviromment variables:`");
            var env = Environment.GetEnvironmentVariables();
            foreach (DictionaryEntry entry in env)
            {
                //?? Will this ever happen
                if (entry.Key != null)
                {
                    var key = entry.Key.ToString();
                    if (key.StartsWith("DOTNET"))
                    {
                        XConsole.WriteLine($"  `{entry.Key}` = {entry.Value}");
                        if (key == "DOTNET_TieredPGO")
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

            XConsole.WriteLine($"`.NET Version`: {Environment.Version.ToString()}");
            XConsole.WriteLine(IsDebug ? "'[DEBUG]'" : "`[RELEASE]`");
        }

        public Task WatchFile(string fsharpFile, string outAsmFile = null, string outILFile = null, string outFSFile = null)
        {
            Initialize(fsharpFile, outAsmFile, outILFile, outFSFile);

            var tmpAsmFile = outAsmFile + "_tmp.dll";
            var command = $"dotnet \"{_pathToCompiler}\" -a {fsharpFile} --optimize+ --debug:portable -o:\"{tmpAsmFile}\"";

            string lastCode = null;
            DateTime lastWrite = DateTime.MinValue;
            var iDontCareAboutThisTask = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        FileInfo fileInfo = new FileInfo(fsharpFile);
                        if (fileInfo.LastWriteTime.Ticks > lastWrite.Ticks)
                        {
                            var code = File.ReadAllText(fsharpFile);
                            if (string.IsNullOrEmpty(code) == false && lastCode != code)
                            {
                                DecompilationUnit unit = null;
                                XConsole.WriteLine($"Decompiling: {fsharpFile}");

                                var info = WatcherUtils.StartCompilerProcess(command);

                                if (info.messages.Any(x => x.Contains("error")) || info.errors.Any())
                                {
                                    StringBuilder errorBuilder = new StringBuilder();
                                    errorBuilder.AppendLine(String.Join(Environment.NewLine, info.errors));
                                    errorBuilder.AppendLine(String.Join(Environment.NewLine, info.messages));
                                    File.WriteAllText(outAsmFile, errorBuilder.ToString());
                                }
                                else
                                {
                                    unit = DecompileToASM(code, tmpAsmFile);

                                    lastWrite = fileInfo.LastWriteTime;
                                    lastCode = code;

                                    string asmCode = string.Empty;
                                    string ilCode = string.Empty;
                                    string fsCode = string.Empty;

                                    if (unit.Errors.Length > 0)
                                    {
                                        StringBuilder errorBuilder = new StringBuilder();
                                        foreach (var error in unit.Errors)
                                        {
                                            errorBuilder.AppendLine($"{error.Message} {error.Trace} {error.Position}");
                                            errorBuilder.AppendLine($"{Environment.NewLine}-----------------------------");
                                        }

                                        XConsole.WriteLine($"Writing Errors to: {outAsmFile}, {outILFile}, {outFSFile}");

                                        var errors = errorBuilder.ToString();

                                        WriteIfNotNullOrEmpty(outAsmFile, errors);
                                        WriteIfNotNullOrEmpty(outILFile, errors);
                                        WriteIfNotNullOrEmpty(outFSFile, errors);
                                    }
                                    else
                                    {
                                        if (unit.DecompiledMethods != null)
                                        {
                                            asmCode = ToAsmString(unit);
                                        }
                                        // 
                                        if (unit.ILTokens != null)
                                        {
                                            ilCode = ToILString(unit);
                                        }
                                        //
                                        if (unit.OutputSourceCode != null)
                                        {
                                            fsCode = unit.OutputSourceCode;
                                        }
                                        //
                                        // Pring Global Messages.
                                        //
                                        if (unit.Messages != null)
                                        {
                                            asmCode +=
                                                Environment.NewLine +
                                                string.Join(Environment.NewLine, unit.Messages);
                                        }

                                        XConsole.WriteLine($"Writing Results to: {outAsmFile}, {outILFile}, {outFSFile}");

                                        WriteIfNotNullOrEmpty(outAsmFile, asmCode);
                                        WriteIfNotNullOrEmpty(outILFile, ilCode);
                                        WriteIfNotNullOrEmpty(outFSFile, fsCode);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        XConsole.WriteLine($"Writing Errors to: {outAsmFile}, {outILFile}, {outFSFile}");
                        //
                        // Report this back to the out files.
                        //
                        var exMessage = ex.ToString();
                        WriteIfNotNullOrEmpty(outAsmFile, exMessage);
                        WriteIfNotNullOrEmpty(outILFile, exMessage);
                        WriteIfNotNullOrEmpty(outFSFile, exMessage);
                    }

                    await Task.Delay(500);
                }
            });
            return iDontCareAboutThisTask;
        }

        private void WriteCompilerVersion(StringBuilder content, DecompilationUnit unit)
        {
            content.Append($"# F# {unit.OutputLanguageVersion} .NET: {Environment.Version.ToString()} {(_isPGO ? "PGO" : "")}");
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
            int sizePad = 30;
            int tableSize = 42;
            var headerTopBottom = new string(ConsoleBorderStyle.TopBottom, tableSize);
            var displayPadding = new string(' ', 4);

            StringBuilder layoutBuilder = new StringBuilder();
            foreach (var typeLayout in typeLayouts)
            {
                if (typeLayout.IsValid == false)
                {
                    layoutBuilder.AppendLine($"# [WARN] {typeLayout.Name} {typeLayout.Message}");
                    continue;
                }

                displayPadding = new string(' ', 4);

                if (typeLayout.Message != null)
                    layoutBuilder.AppendLine($"# {typeLayout.Message}");

                layoutBuilder.AppendLine($"# {typeLayout.Name} Memory Layout. {(typeLayout.IsBoxed ? "\r\n# (struct sizes might be wrong since they are boxed to extract layouts)" : "")} ");
                layoutBuilder.AppendLine($"{(typeLayout.IsBoxed ? "struct" : "class")} {typeLayout.Name}");
                layoutBuilder.AppendLine("{");

                bool isHeaderEnd = false;
                bool hasHeader = false;
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

                    var offsetString = $"[{fieldLayout.Offset}-{fieldLayout.Offset + fieldLayout.Size - 1}]";
                    var padBy = offsetPad - offsetString.Length;
                    var line = $"{offsetString} {new string(' ', padBy)} {fieldLayout.Type} {fieldLayout.Name}";
                    var sizeInBytesPad = "";

                    //
                    // Pad the (X bytes) string such that it's all nicley 
                    // aligned when we render multiple lines on screen.
                    //
                    if (sizePad - line.Length > 0)
                    {
                        sizeInBytesPad = new string(' ', sizePad - line.Length);
                    }
                    var sizeInBytes = $"{sizeInBytesPad}({fieldLayout.Size} bytes)";
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

        public string ToAsmString(DecompilationUnit unit)
        {
            var builder = new StringBuilder();
            var lineBuilder = new StringBuilder();
            var writer = new AssemblyWriter();

            if (unit.Options.ShowHelp)
            {
                writer.AppendHelp(builder);
                return builder.ToString();
            }

            WriteCompilerVersion(builder, unit);

            builder.AppendLine();

            if (unit.TypeLayouts != null && unit.TypeLayouts.Any())
            {
                builder.AppendLine(ToLayout(unit.TypeLayouts));
                builder.AppendLine();
            }

            //
            // If the option to cut addresses was selected we should set the cut length
            // before we write out the address using this writer.
            //
            if (unit.Options.ShortAddresses)
                writer.AddressCutBy = unit.Options.AddressesCutByLength;

            //
            // Diff selected methods.
            //
            if (unit.Options.Diff)
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
                else if(method.Instructions.Any() == false)
                {
                    method.Messages.Add($"# [WARN] The Method: {method.Name} Contains Zero Instructions.");
                }


                methodLines.Clear();

                (int jumpSize, int nestingLevel) sizeAndNesting = (-1, -1);
                sizeAndNesting = JumpGuideDetector.PopulateGuides(method);
                var inliningCalls = InlineDetector.DetectInlining(method);

                //
                // Print messages.
                //
                writer.AppendMessages(builder, method);
                //
                // Write out method signature.
                //
                writer.AppendMethodSignature(builder, method);

                foreach (var inliningCall in inliningCalls)
                {
                    builder.AppendLine("  " + XConsole.ConsoleBorderStyle.Bullet + "inlined" + " " + inliningCall);
                }

                if (method.Instructions.Any() == false)
                    continue;

                var firstInst = method.Instructions.First(x => x.IsCode == false);
                var baseAddr = firstInst.Address;


                double avgOffset = 0;
                foreach (var inst in method.Instructions)
                {
                    lineBuilder.Clear();

                    if (inst.IsCode && unit.Options.ShowSourceMaps == false) continue;

                    if (inst.IsCode == false && unit.Options.RelativeAddresses == true)
                    {
                        inst.Address -= baseAddr;
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
                if (unit.Options.ShowASMDocumentation && IsDocumentationAutoLayout(unit.Options.ASMDocumentationOffset))
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
                    if (inst.IsCode && unit.Options.ShowSourceMaps == false) continue;

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

        public DecompilationUnit DecompileToASM(string code, string compiledArtifactName)
        {
            var unit = new DecompilationUnit();
            //
            // This was already compiled so simply get the streams from outputs.
            //
            var compilation = new CompilationUnit();
            compilation.AssemblyStream = new MemoryStream(File.ReadAllBytes(compiledArtifactName));
            //
            // Construct a PDB path.
            //
            compilation.PDBStream = new MemoryStream(File.ReadAllBytes(
                Path.GetDirectoryName(compiledArtifactName) + 
                Path.DirectorySeparatorChar + 
                Path.GetFileNameWithoutExtension(compiledArtifactName) + ".pdb"));
            compilation.SourceCode = code;

            //
            // Get the streams, note that we have to dispose them thus 'using'
            //
            using var assemblyStream = compilation.AssemblyStream;
            using var pdbStream = compilation.PDBStream;

            compilation.Options = WatcherUtils.SetCommandOptions(code, compilation.Options);
            unit.Options = compilation.Options;
            unit.InputSouceCode = compilation.SourceCode;
            unit.OutputLanguageVersion = compilation.LanguageVersion;
            var compilationResult = compilation.CompilationResult;

            XConsole.WriteLine($"Language Version: `{compilation.LanguageVersion}`");
            //
            // Create the collectible load context. This context will only compile to non-tiered
            // Optimized compilation if the collectible option is set to true, so we are leaving it
            // set to true, but if the compilation option is set to 1 then we will treat is as Debug
            // and change the flag, anything else is considered a Release No-Tier build.
            //
            var isOptimizedOnly = _unsafeUseTieredCompilation == false && compilation.Options.OptimizationLevel != 1;
            using (var ctx = new CollectibleAssemblyLoadContext(isOptimizedOnly))
            {
                assemblyStream.Position = 0;
                var loaded = ctx.LoadFromStream(assemblyStream);
                List<DecompiledMethod> globalMethodList = new List<DecompiledMethod>();
                List<TypeLayout> typesMemoryLayouts = new List<TypeLayout>();

                //
                // Get IL <-> Code Map.
                // @TODO, @NOTE: This map is curently used to just go from X86 <-> F#
                // for IL Source Code Map we are using a DebugInfo provider, which does the 
                // same thing but it's a part of the ReflectionDisassembler (IL Decompiler) API
                // which we do not control.
                //
                // This creates a bit of a confusion and complicates things so it would be good to just use
                // a single map from.
                //
                using var codeMap = new ILToCodeMapProvider(unit.InputSouceCode, pdbStream);

                //
                // Debug Info Provider will be used by the CS and IL Decompilers to resolve variable
                // names, and map sequence points.
                //
                using var sourceMapProvider = new DebugInfoProvider(pdbStream);
                //
                // Since F# creates multiple types per module, we don't want to guess which is our code
                // code we will atempt to decompile and dissasembly all of them.
                //
                foreach (var compiledType in loaded.GetTypes())
                {
                    assemblyStream.Position = 0;
                    pdbStream.Position = 0;

                    var ilMethodMap = codeMap.GetMap(compiledType);
                    //
                    // Use the CSharp Code Decompiler to go from IL back to C#
                    // This will allow us to see what kinds of transformations took place in the IL
                    // code from the C# perspective (things like lowering, morphing, magic code, etc).
                    //
                    // This is not converted back to F# yet since we need to use a different decompiler.
                    //
                    var decompiler = new CSharpDecompiler(assemblyStream, pdbStream);
                    var nativeSourceCode = decompiler.Decompile(compiledType, sourceMapProvider, unit.Options);
                    unit.OutputSourceCode += nativeSourceCode;

                    //
                    // Get ASM code from the compiled type, and pass in the IL <-> Code map
                    // We shall use this map to correlate sequence points with IL <-> ASM map
                    // and produce X86 ASM to Source Code Map.
                    //
                    var decompiledMethods = compiledType.ToAsm(sourceCodeMap: ilMethodMap, @private: true);
          
                    //
                    // Get all nested types and decompile them as well.
                    //
                    var types = compiledType.GetNestedTypes();
                    foreach (var type in types)
                    {
                        if (type.IsInterface) continue;
                        else if (type.IsEnum) continue;

                        var innerMethods = type.ToAsm(@private: true);
                        var nestedLayout = type.ToLayout(@private: true);

                        AddMethodsToGlobalList(innerMethods, globalMethodList);
                        typesMemoryLayouts.Add(nestedLayout);
                    }

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
                        foreach (var compiledCollectibleType in loaded.GetTypes())//.FirstOrDefault(x => x.Name == (CSharpCodeCompiler.BaseClassName));
                        {
                            decompiledMethods = compiledCollectibleType.ToAsm(@private: true);
                            //
                            // @NOTE: This is not optimial at all but for now let's go with this version since it's
                            // simple top-bottom code.
                            //
                            // Get all of the T0 decompiled methods and find the ones that have any attributes
                            // in the compilation map. QuickJIT methods are simple since we already have them so
                            // let's add them as a new decompiled method to our existing list.
                            // 
                            var flags = BindingFlags.Public |
                                        BindingFlags.Static |
                                        BindingFlags.Instance |
                                        BindingFlags.NonPublic;
                            var methodInfos = compiledCollectibleType.GetMethods(flags);
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

        private void AddMethodsToGlobalList(DecompiledMethod[] methods, List<DecompiledMethod> totalMethodsToAdd)
        {
            foreach (var method in methods)
            {
                if (method == null) continue;
                totalMethodsToAdd.Add(method);
            }
        }

    }
}
