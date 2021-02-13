using PowerUp.Core;
using PowerUp.Core.Console;
using System;
using PowerUp.Core.Decompilation;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.Metadata;

namespace PowerUp.Tests
{
    class Program
    {
        static void Main(string[] args)
        {
            XConsole.WriteLine("This is a high level demo of `PowerUp` tools:");
            
            BenchmarkTest();
            DecompilationTest();
        }

        static void BenchmarkTest()
        {
            double sum = 0;
            QuickBenchmark.Measure(() =>
            {
                for (int i = 0; i < 1_000_000; i++)
                    sum *= i;
            });
        }

        static void DecompilationTest()
        {
            Program p = new Program();
            Console.WriteLine();

            Console.WriteLine(" x * 2 * 2;\r\n");
            typeof(Program).GetMethod("DecompilationTestMethod").ToAsm().Print();
            Console.WriteLine("Result: " + p.DecompilationTestMethod(int.MaxValue) + "\r\n");
        }

        public int DecompilationTestMethod(int x)
        {
            return x * 2 * 2;
        }
    }
}
