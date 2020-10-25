using PowerUp.Core.Console;
using PowerUp.Core.Decompilation;
using PowerUp.Core.Decompiler;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace PowerUp.Core
{
    public class QuickBenchmark
    {
        public static double Measure(Action a, BenchmarkOptions options = default(BenchmarkOptions))
        {
            if (options == default(BenchmarkOptions))
                options = BenchmarkOptions.Default();

            var decompiler = new JITCodeDecompiler();

            var warmupCount = options.WarmUpCount;
            var runCount = options.RunCount;
            DecompiledMethod root = null;
            DecompiledMethod before = null;

            if (options.Decompile)
            {
                a();
                root = decompiler.DecompileMethod(a.Method);
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
                var after = decompiler
                    .DecompileMethod(root.CodeAdresss, root.CodeSize, options.DecompileMethodName);

                if (before.CodeAdresss == after.CodeAdresss)
                {
                    XConsole.WriteLine("Caputed Only Single Tier");
                    var coords1 = PrintCode("Tier0", 1, cursorTop + 1, before);
                    XConsole.SetCursorPosition(0, coords1.y);
                }
                else
                {
                    var coords1 = PrintCode("Tier0", 1, cursorTop, before);
                    var coords2 = PrintCode("Tier1", 35, cursorTop, after);
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

        private static (int x, int y) PrintCode(string title, int x, int y, DecompiledMethod root)
        {
            XConsole.SetCursorPosition(x, y);
            XConsole.WriteLine(title);
            XConsole.DrawSeparator(10, x, y + 1);
            XConsole.SetCursorPosition(x, y + 2);

            int idx = 0;
            foreach (var assemblyCode in root.Instructions)
            {
                XConsole.SetCursorPosition(x, XConsole.CursorTop);
                XConsole.WriteLine($"`{assemblyCode.Instruction}` {string.Join(",", assemblyCode.Arguments)}");
                idx++;
            }
            return (XConsole.CursorLeft, XConsole.CursorTop);
        }
    }
}
