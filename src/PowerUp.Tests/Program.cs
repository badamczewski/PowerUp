using PowerUp.Core;
using PowerUp.Core.Console;
using System;

namespace PowerUp.Tests
{
    class Program
    {
        static void Main(string[] args)
        {
            XConsole.WriteLine("This is a high level demo of `PowerUp` tools:");
            BenchmarkTest();
        }

        static void BenchmarkTest()
        {
            double sum = 0;
            QuickBenchmark.Measure(() => {
                for (int i = 0; i < 1_000_000; i++)
                    sum *= i;
            });
        }
    }
}
