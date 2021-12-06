using PowerUp.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PowerUp.Tests.Tests
{
    public class BenchmarkTests
    {
        public static void BenchmarkTest_1()
        {
            double sum = 0;
            QuickBenchmark.Measure(() =>
            {
                for (int i = 0; i < 1_000_000; i++)
                    sum *= i;
            });
        }

        public static void BenchmarkTest_Issue5()
        {
            string txt = "12,34,56";

            QuickBenchmark.Measure(() => {
                for (int i = 0; i < 10_000; i++)
                {
                    string[] a = txt.Split(',');
                    int x = int.Parse(a[0]);
                    int y = int.Parse(a[1]);
                    int z = int.Parse(a[2]);
                }
            });
        }


    }
}
