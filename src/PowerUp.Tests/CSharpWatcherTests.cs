using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using PowerUp.Watcher;
using System;
using System.IO;
using System.Linq;

namespace PowerUp.Tests
{
    public class CSharpWatcherTests
    {
        private IConfigurationRoot CreateConfig()
        {
            var dotnetPathEnv = Environment.GetEnvironmentVariable("DotNetCoreDirPathNet6");

            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetParent(AppContext.BaseDirectory).FullName)
                .AddJsonFile("appsettings.json", false)
                .Build();

            if (dotnetPathEnv != null)
                configuration["DotNetCoreDirPathNet6"] = dotnetPathEnv;

            TryAugmentConfiguration(configuration);

            return configuration;
        }

        static void TryAugmentConfiguration(IConfigurationRoot configuration)
        {
            foreach (var kv in configuration.GetChildren())
            {
                //
                // Augment all * and try to find correct directories.
                //
                if (kv.Value.Contains("*"))
                {
                    //
                    // Get augmented value and reassign it to the configuration section.
                    //
                    var value = WatcherUtils.FindDirectoryWithPattern(kv.Value);
                    //
                    // When we have a file name instread of a directory, we need to remove the
                    // last separator that was added.
                    //
                    if (kv.Value.EndsWith(".dll"))
                        value = value.Substring(0, value.Length - 1);

                    configuration[kv.Key] = value;
                }
            }
        }


        private IConfigurationRoot configuration;

        [SetUp]
        public void Setup()
        {
            configuration = CreateConfig();
        }

        [Test]
        public void DecompileToASM_EmptyFunction_ShouldHaveNoErrors()
        {
            CSharpWatcher watcher = new CSharpWatcher(configuration);
            watcher.InitializeCsharpCompiler();
            var result = watcher.DecompileToASM("void M(){}");
            Assert.IsTrue(result.Errors.Any() == false);
        }

        [Test]
        public void DecompileToASM_EmptyCode_ShouldHaveNoErrors()
        {
            CSharpWatcher watcher = new CSharpWatcher(configuration);
            watcher.InitializeCsharpCompiler();
            var result = watcher.DecompileToASM("");
            Assert.IsTrue(result.Errors.Any() == false);
        }

        [Test]
        public void DecompileToASM_FaultyCode_ShouldHaveErrors()
        {
            CSharpWatcher watcher = new CSharpWatcher(configuration);
            watcher.InitializeCsharpCompiler();
            var result = watcher.DecompileToASM("void M();");
            Assert.IsTrue(result.Errors.Any() == true);
        }

        [Test]
        public void DecompileToASM_EmptyClassLayout_ShouldHaveTypeLayouts()
        {
            CSharpWatcher watcher = new CSharpWatcher(configuration);
            watcher.InitializeCsharpCompiler();
            var result = watcher.DecompileToASM("public class Empty{}");

            Assert.IsTrue(result.TypeLayouts.Length == 1);
        }

        [Test]
        public void DecompileToASM_EmptyStructLayout_ShouldHaveTypeLayouts()
        {
            CSharpWatcher watcher = new CSharpWatcher(configuration);
            watcher.InitializeCsharpCompiler();
            var result = watcher.DecompileToASM("public struct Empty{}");

            Assert.IsTrue(result.TypeLayouts.Length == 1);
        }

        [Test]
        public void DecompileToASM_EmptyRecordLayout_ShouldHaveTypeLayouts()
        {
            CSharpWatcher watcher = new CSharpWatcher(configuration);
            watcher.InitializeCsharpCompiler();
            var result = watcher.DecompileToASM("public record Empty();");

            Assert.IsTrue(result.TypeLayouts.Length == 1);
        }

        [Test]
        public void DecompileToIL_EmptyFunction_ShouldHaveIL()
        {
            CSharpWatcher watcher = new CSharpWatcher(configuration);
            watcher.InitializeCsharpCompiler();
            var result = watcher.DecompileToASM("public void M(){}");

            Assert.IsTrue(result.ILTokens.Length > 0);
        }

        [Test]
        public void DecompileToASM_EmptyFunction_ShouldHaveASM()
        {
            CSharpWatcher watcher = new CSharpWatcher(configuration);
            watcher.InitializeCsharpCompiler();
            var result = watcher.DecompileToASM("public void M(){}");
            var method = result.DecompiledMethods.First(m => m.Name == "M");

            Assert.IsTrue(method.Instructions.Count > 0);
        }
    }
}
