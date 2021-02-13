using System;
using System.Diagnostics.CodeAnalysis;

namespace PowerUp.Core
{
    public struct BenchmarkOptions : IEquatable<BenchmarkOptions>
    {
        public int WarmUpCount;
        public int RunCount;
        public bool Decompile;
        public string DecompileMethodName;

        public static BenchmarkOptions Default()
        {
            return new BenchmarkOptions()
            {
                WarmUpCount = 60,
                RunCount = 10,
                Decompile = true,
                DecompileMethodName = null
            };
        }

        public bool Equals([AllowNull] BenchmarkOptions other)
        {
            return
                this.RunCount == other.RunCount &&
                this.WarmUpCount == other.WarmUpCount &&
                this.Decompile == other.Decompile &&
                this.DecompileMethodName == other.DecompileMethodName;
        }

        public static bool operator ==(BenchmarkOptions first, BenchmarkOptions second)
            => first.Equals(second);

        public static bool operator !=(BenchmarkOptions first, BenchmarkOptions second)
            => !(first == second);

        public override int GetHashCode()
            => (WarmUpCount, RunCount, Decompile, DecompileMethodName).GetHashCode();

        public override bool Equals(object obj)
            => (obj is BenchmarkOptions bench) && Equals(bench);
    }
}
