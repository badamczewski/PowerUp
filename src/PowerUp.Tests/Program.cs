using PowerUp.Core;
using PowerUp.Core.Console;
using System;
using PowerUp.Core.Decompilation;
using PowerUp.Tests.Tests;

namespace PowerUp.Tests
{
    class Program
    {
        static void Main(string[] args)
        { 
            if (args.Length == 0 || args[0] == "demo")
                Demo.Start();

            else if(args[0] == "tests")
            {
                //
                // WIP
                //
                BenchmarkTests.BenchmarkTest_1();
                BenchmarkTests.BenchmarkTest_Issue5();
            }
        }


    }
}
