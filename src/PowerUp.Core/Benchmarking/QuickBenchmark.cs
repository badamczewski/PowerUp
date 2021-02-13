using PowerUp.Core.Console;
using PowerUp.Core.Decompilation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Linq;

namespace PowerUp.Core
{
    public class QuickBenchmark
    {
        public static double Measure(Action a, BenchmarkOptions options = default(BenchmarkOptions))
        {
            if (options == default(BenchmarkOptions))
                options = BenchmarkOptions.Default();

            var decompiler = new JitCodeDecompiler();

            var warmupCount = options.WarmUpCount;
            var runCount = options.RunCount;
            DecompiledMethod before = null;

            if (options.Decompile)
            {
                a();
                before = decompiler.DecompileMethod(a.Method, options.DecompileMethodName);
            }
            //
            // Quick Warm Up.
            //
            XConsole.WriteLine("Quick Benchmark:");
            XConsole.WriteLine($" [1] Warm Up ...({warmupCount})");

            for (int i = 0; i < warmupCount; i++)
            {
                a();
            }

            Span<double> res = stackalloc double[runCount];
            var initialMemory = GC.GetTotalAllocatedBytes();
            XConsole.WriteLine($" [2] Running ...({runCount})");

            for (int i = 0; i < runCount; i++)
            {
                Stopwatch w = new Stopwatch();
                w.Start();
                {
                    a();
                }
                w.Stop();

                res[i] = w.ElapsedMilliseconds;
            }

            var memoryAfterRuns = GC.GetTotalAllocatedBytes();
            double sum = 0;
            for (int i = 0; i < res.Length; i++)
            {
                XConsole.WriteLine($" [3-{i}] Took: `{res[i]}` ms");
                sum += res[i];
            }

            var cursonLeft = XConsole.CursorLeft;

            var mean = sum / runCount;
            var stdDev = StdDev(res, mean);
            var totalMemory = memoryAfterRuns - initialMemory;
            int separatorLen = cursonLeft + 30 + (runCount * 4);

            XConsole.DrawSeparator(separatorLen);
            XConsole.WriteLine($" Mean         -> `{string.Format("{0:N4}", mean)}` ms");
            XConsole.WriteLine($" StdDev       -> `{string.Format("{0:N4}", stdDev)}` ms");
            XConsole.WriteLine($" Total Memory -> `{totalMemory}` b\r\n");

            var cursorTop = XConsole.CursorTop;

            XConsole.DrawPlot(30, cursorTop - 5, res.ToArray());

            if (options.Decompile)
            {
                XConsole.WriteLine($" Decompiling Method: `{options.DecompileMethodName}`");

                cursorTop += 2;

                var after = decompiler.DecompileMethod(before.CodeAddress, before.CodeSize, options.DecompileMethodName);

                if (before.CodeAddress == after.CodeAddress)
                {
                    XConsole.WriteLine("Captured Only Single Tier");
                    var coords1 = PrintCode("Tier0", 1, cursorTop + 1, before);
                    XConsole.SetCursorPosition(0, coords1.y);
                }
                else
                {
                    var coords1 = PrintCode("Tier0", 1, cursorTop, before);
                    var coords2 = PrintCode("Tier1", 50, cursorTop, after);
                    int top = coords1.y > coords2.y ? coords1.y : coords2.y;
                    XConsole.SetCursorPosition(0, top);
                }
            }

            return mean;
        }

        private static double StdDev(Span<double> array, double mean, bool asSample = false)
        {
            double sumOfSquares = 0;
            foreach (var value in array)
            {
                sumOfSquares += (value - mean) * (value - mean);
            }

            if (asSample == false)
            {
                return Math.Sqrt(sumOfSquares / (array.Length - 1));
            }
            else
            {
                return Math.Sqrt(sumOfSquares / (array.Length));
            }
        }

        private static List<string> BuildCodeLines(DecompiledMethod root, out int refs)
        {
            List<string> codeLines = new List<string>();

            refs = 0;
            int idx = 0;
            int refId = 0;
            foreach (var assemblyCode in root.Instructions)
            {
                if (assemblyCode.RefAddress > 0 && 
                    assemblyCode.RefAddress >= root.Instructions.First().Address &&
                    assemblyCode.RefAddress <= root.Instructions.Last().Address)
                {
                    refId = 0;

                    for (refId = 0; refId < assemblyCode.RefIds.Length; refId++)
                    {
                        if(assemblyCode.RefIds[refId] <= 0)
                        {
                            assemblyCode.RefIds[refId] = idx;
                            break;
                        }
                    }

                    int innerIdx = 0;
                    foreach (var innerAssemblyCode in root.Instructions)
                    {
                        if (innerAssemblyCode.Address >= assemblyCode.RefAddress)
                        {
                            int start = idx;
                            int end = innerIdx;

                            if (idx > innerIdx)
                            {
                                start = innerIdx;
                                end = idx;
                            }

                            for (int i = start; i <= end; i++)
                            {
                                root.Instructions[i].RefIds[refId] = idx;
                            }

                            break;
                        }

                        innerIdx++;
                    }

                    refs++;
                }

                StringBuilder argBuilder = new StringBuilder();
                foreach (var arg in assemblyCode.Arguments)
                {
                    argBuilder.Append(arg.Value + ",");
                }
                if (argBuilder.Length > 1)
                    argBuilder.Remove(argBuilder.Length - 1, 1);

                codeLines.Add($"{assemblyCode.Address.ToString("X")} `{assemblyCode.Instruction}` {argBuilder.ToString()}");

                idx++;
            }

            return codeLines;
        }

        private static (int x, int y) PrintCode(string title, int x, int y, DecompiledMethod root)
        {
            XConsole.SetCursorPosition(x, y);
            XConsole.WriteLine(title);
            XConsole.DrawSeparator(10, x, y + 1);

            var codeLines = BuildCodeLines(root, out var refs);
            XConsole.SetCursorPosition(x + refs, y + 2);

            int newY = y + 2;
            int idx = 0;

            AssemblyInstruction prevInstruction = null;

            foreach (var assemblyCode in root.Instructions)
            {
                string prefix = null;
                bool isStartEnd = false;

                for (int r = refs - 1; r >= 0; r--)
                {
                    if (assemblyCode.RefIds[r] > 0)
                    {
                        AssemblyInstruction nextInstruction = null;

                        if(idx + 1 < root.Instructions.Count)
                        {
                            nextInstruction = root.Instructions[idx + 1];
                        }

                        //
                        // We are here since we have a jump overlap or a simple jump target
                        // Check for jump overlaps.
                        //
                        

                        //
                        // Prev instruction has different ref inentifier so it means we have to end
                        // our connection here.
                        //
                        if (prevInstruction != null && prevInstruction.RefIds[r] != assemblyCode.RefIds[r])
                        {
                            prefix += XConsole.ConsoleBorderStyle.TopLeft.ToString();
                            prefix += XConsole.ConsoleBorderStyle.TopBottom.ToString();

                            isStartEnd = true;
                        }
                        //
                        // Next instruction has a different ID we have to break the connection here.
                        //
                        else if (nextInstruction != null && nextInstruction.RefIds[r] != assemblyCode.RefIds[r])
                        {
                            prefix += XConsole.ConsoleBorderStyle.BottomLeft.ToString();
                            prefix += XConsole.ConsoleBorderStyle.TopBottom.ToString();

                            isStartEnd = true;
                        }
                        else if(idx + 1 >= root.Instructions.Count)
                        {
                            prefix += XConsole.ConsoleBorderStyle.BottomLeft.ToString();
                            prefix += XConsole.ConsoleBorderStyle.TopBottom.ToString();
                        }
                        //
                        // Contrinue the line.
                        //
                        else
                        {
                            if (isStartEnd)
                            {
                                prefix += XConsole.ConsoleBorderStyle.SeparatorBoth.ToString();
                                prefix += XConsole.ConsoleBorderStyle.TopBottom.ToString();
                            }
                            else
                            {
                                prefix += XConsole.ConsoleBorderStyle.Left.ToString() + " ";
                            }
                        }
                    }
                    //
                    // We have a start instruction and we have the id so lets draw the line out
                    // and go up or down.
                    // 
                    // -------
                    //
                    else if (isStartEnd)
                    {
                        prefix += XConsole.ConsoleBorderStyle.TopBottom.ToString();
                        prefix += XConsole.ConsoleBorderStyle.TopBottom.ToString();
                    }
                    else
                    {
                        prefix += "  ";
                    }
                }

                codeLines[idx] = "`" + prefix + "`" + codeLines[idx];

                XConsole.SetCursorPosition(x, newY);
                XConsole.WriteLine(codeLines[idx]);
                newY++;

                idx++;

                prevInstruction = assemblyCode;
            }

            return (XConsole.CursorLeft, XConsole.CursorTop);
        }
    }
}
