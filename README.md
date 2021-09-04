# PowerUp

The purpose of this library is to provide productivity utilities and tools such as:

* Live IDE Watcher.
* JIT Dissasembler.
* IL Compiler.
* Quick Benchmark.
* Console with rich formatting.
* Others.

## Live IDE Watcher

A watcher application that monitors .cs and .il  files and compiles them with the JIT compiler to produce IL and X86 ASM outputs.

![anim8](https://user-images.githubusercontent.com/752380/131903377-128dbec4-be8d-4455-acbd-50b9d86cebd5.gif)

**I use this application daily to create infographics:** https://leveluppp.ghost.io/infographics/

Note: IL Compilation is still a work in progress, and it will take some time before it's robust.

![anim10](https://user-images.githubusercontent.com/752380/131910137-70f0ee68-b4fa-4bf3-b727-1a4ddaf86384.gif)

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

There are no guarantees that Tier1 will always be available; Very short and fast methods tend not to be promoted (I'm working on a fix). Code that contains loops is always optimized so you should not bother with this method and switch back to the simpler version.

## IL Compiler 

You can write IL Code as a string and compile it to a type; later, it can be fed to the JIT Decompiler. Native IL compilation is an interesting research topic since you can emit instructions that are not used or used in a completely different context.

```csharp
   var il = @"
    .method public hidebysig instance int32 X (int32 x) 
    {
       .maxstack 8

       IL_0000: ldarg.1
       IL_0001: ldc.i4.s 97
       IL_0003: callvirt instance int32 [System.Private.CoreLib]System.String::IndexOf(char)
       IL_0008: ret
    }
   "; 
   ILCompiler compiler = new ILCompiler();
   var type = compiler.Compile(il);
   var asm = type.CompiledType.ToAsm();
   asm.Print();
```

## Quick Benchmark

To create a valid benchmark that tests various examples and it's accurate, one needs to go to great lengths, and there are multiple libraries for that already. This Quick Benchmark is suitable for running only on a single method and test performance; it's handy for live demos, slides, videos, etc. 

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

