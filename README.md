# PowerUp

The purpose of this library is to provide productivity utilities and tools such as:
* Quick Benchmark.
* Console with rich formatting.
* JIT Dissasembler.
* Others.

## Quick Benchmark

To create a valid benchmark that tests various examples and it's accurate, one needs to go to great lengths, and there are multiple libraries for that already. This is a Quick Benchmark that's suitable to run only on a single method and test performance; it's handy for live demos, slides, videos, etc. 

Quick Benchmark provides a run count and a warmup count. It does all of the needed operations to provide valid measurements like computing the mean and standard deviation and provide memory usage.

![obraz](https://user-images.githubusercontent.com/752380/97169473-6c9baf00-178a-11eb-88d1-a245a482662b.png)

There's also a JIT Dissasembler that tracks the generated assembly code of the tested method across JIT compilation tiers. 

![obraz](https://user-images.githubusercontent.com/752380/97169562-8dfc9b00-178a-11eb-8ba6-6b919e53a686.png)

To run the Benchmark, you need to call the "Measure" method like so:

```csharp
double sum = 0;
QuickBenchmark.Measure(() => {
  for (int i = 0; i < 10_000_000; i++)
    sum *= i;
  });

```

## JIT Dissasembler

To disassemble JIT code, you need to extract the method info using reflection and call the extension method ToAsm(). This will get native assembly code in tier0. To get a higher Tier, you need to provide the delegate that will call the method, and over time the compiler will re-JIT it to Tier1. If you want to skip this, you can compile your project with:  

```<TieredCompilation>false</TieredCompilation>```

Example:

```csharp

   typeof(Program)
       .GetMethod("DecompilationTestMethod")
       .ToAsm()
       .Print();
```

Example for Tier1:

```csharp

    typeof(Program)
        .GetMethod("DecompilationTestMethod")
        .ToAsm(() => p.DecompilationTestMethod(10))
        .tier1
        .Print();
```

There are no guarantees that Tier1 will always be available; Very short and fast methods tend not to be promoted. Code that contains loops is always optimized so you should not bother with this method and switch back to the simpler version.
