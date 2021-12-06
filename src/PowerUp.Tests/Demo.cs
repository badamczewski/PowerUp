using PowerUp.Core;
using System;
using PowerUp.Core.Console;
using PowerUp.Core.Decompilation;

namespace PowerUp.Tests
{
    public class Demo
    {
        public static void Start()
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
            Demo p = new Demo();
            Console.WriteLine();

            Console.WriteLine(" x * 2 * 2;\r\n");
            typeof(Demo).GetMethod("DecompilationTestMethod").ToAsm().Print();
            Console.WriteLine("Result: " + p.DecompilationTestMethod(int.MaxValue) + "\r\n");
        }

        public int DecompilationTestMethod(int x)
        {
            return x * 2 * 2;
        }
    }
}
