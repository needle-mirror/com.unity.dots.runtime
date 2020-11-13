using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Bee.Core;
using Bee.CSharpSupport;
using Bee.DotNet;
using Bee.Tools;
using NiceIO;

static class ILPostProcessorTool
{
    private static DotNetRunnableProgram _ILPostProcessorRunnableProgram => new DotNetRunnableProgram(ILPostProcessorRunnerProgram.SetupSpecificConfiguration(DotsConfigs.HostDotnet));

    public static CSharpProgram _ilPostProcessorRunnerProgramInternal;
    public static CSharpProgram ILPostProcessorRunnerProgram { get {
            if (_ilPostProcessorRunnerProgramInternal == null)
                _ilPostProcessorRunnerProgramInternal = _ilPostProcessorRunnerProgram();

            return _ilPostProcessorRunnerProgramInternal;
    } }

    private static CSharpProgram _ilPostProcessorRunnerProgram()
    {
        var ilppRunnerDir = BuildProgram.BeeRoot.Parent.Combine("ILPostProcessing~/ILPostProcessorRunner");

        // Since we will be running ILPostProcessors in a separate executable, we need to pull in the assembly references from the execution
        // environment the ILPostProcessors would have run in normally (i.e. the editor)
        var assemblyReferences = new List<NPath>();
        foreach (var ilppAsm in BuildProgram.ILPostProcessorAssemblies)
        {
            foreach (var asm in ilppAsm.RecursiveRuntimeDependenciesIncludingSelf)
            {
                if ((asm.Path.FileNameWithoutExtension != "mscorlib" &&
                     asm.Path.FileNameWithoutExtension != "netstandard") &&
                    asm.Path.FileNameWithoutExtension != BuildProgram.UnityCompilationPipeline.Path.FileNameWithoutExtension)
                    assemblyReferences.Add(asm.Path.ResolveWithFileSystem());
            }
        }

        var ilPostProcessorRunner = new CSharpProgram()
        {
            FileName = "ILPostProcessorRunner.exe",
            Sources = { ilppRunnerDir },
            Unsafe = true,
            LanguageVersion = "7.3",
            References =
            {
                BuildProgram.UnityCompilationPipeline
            },
            ProjectFilePath = "ILPostProcessorRunner.csproj",
            Framework = { Framework.Framework471 },
            IgnoredWarnings = { 3270 },
            SupportFiles = { assemblyReferences },
        };

        return ilPostProcessorRunner;
    }

    public static DotNetAssembly SetupInvocation(
        DotNetAssembly inputAssembly,
        CSharpProgramConfiguration config,
        string[] defines)
    {
        return inputAssembly.ApplyDotNetAssembliesPostProcessor($"artifacts/{inputAssembly.Path.FileNameWithoutExtension}/{config.Identifier}/post_ilprocessing/",
            (inputAssemblies, targetDirectory) => AddActions(config, inputAssemblies, targetDirectory, defines)
        );
    }

    private static void AddActions(CSharpProgramConfiguration config, DotNetAssembly[] inputAssemblies, NPath targetDirectory, string[] defines)
    {
        var processors = BuildProgram.ILPostProcessorAssemblies.Select(asm => asm.Path.MakeAbsolute().ResolveWithFileSystem());
        var outputDirArg = "-o " + targetDirectory.MakeAbsolute().QuoteForProcessStart();
        var processorPathsArg = processors.Count() > 0 ? "-p " + processors.Select(p => p.QuoteForProcessStart()).Aggregate((s1, s2) => s1 + "," + s2) : "";

        foreach (var inputAsm in inputAssemblies.OrderByDependencies())
        {
            var inputAssemblyPath = inputAsm.Path.ResolveWithFileSystem().MakeAbsolute();
            var assemblyArg = "-a " + inputAssemblyPath.QuoteForProcessStart();
            var referenceAsmPaths = inputAsm.RecursiveRuntimeDependenciesIncludingSelf.Where(a => !a.Path.IsChildOf("post_ilprocessing"))
                .Select(a => a.Path.MakeAbsolute().ResolveWithFileSystem());

            var dotsConfig = (DotsRuntimeCSharpProgramConfiguration)config;

            switch (dotsConfig.TargetFramework)
            {
                case TargetFramework.Tiny:
                    {
                        referenceAsmPaths = referenceAsmPaths.Concat(
                            new[]
                            {
                                Il2Cpp.Distribution.Path.Combine("build/profiles/Tiny/Facades/netstandard.dll").ResolveWithFileSystem(),
                                Il2Cpp.TinyCorlib.Path.ResolveWithFileSystem()
                            });
                        break;
                    }

                case TargetFramework.NetStandard20:
                    {
                        NPath bclDir = Il2Cpp.Il2CppDependencies.Path.Combine("MonoBleedingEdge/builds/monodistribution/lib/mono/unityaot").ResolveWithFileSystem();
                        referenceAsmPaths = referenceAsmPaths.Concat(new[] { bclDir.Combine("mscorlib.dll"), bclDir.Combine("Facades/netstandard.dll") });
                        break;
                    }

                default:
                    throw new NotImplementedException($"Unknown target framework: {dotsConfig.TargetFramework}");
            }

            var referencesArg = referenceAsmPaths.Any() ? "-r " + referenceAsmPaths.Select(r => r.MakeAbsolute().QuoteForProcessStart()).Distinct().Aggregate((s1, s2) => s1 + "," + s2) : string.Empty;
            var assemblyFolders = referenceAsmPaths.Select(p => p.Parent.QuoteForProcessStart()).Concat(new[] { inputAssemblyPath.Parent.QuoteForProcessStart() }).Distinct();
            var assemblyFoldersArg = assemblyFolders.Any() ? "-f " + assemblyFolders.Aggregate((d1, d2) => d1 + "," + d2) : "";
            var allscriptDefines = dotsConfig.Defines.Concat(defines);
            var definesArg = !allscriptDefines.Empty() ? "-d " + allscriptDefines.Distinct().Aggregate((d1, d2) => d1 + "," + d2) : "";
            var targetFiles = TargetPathsFor(targetDirectory, inputAsm).ResolveWithFileSystem().ToArray();
            var inputFiles = InputPathsFor(inputAsm).Concat(processors).Concat(new[] { _ILPostProcessorRunnableProgram.Path }).Concat(referenceAsmPaths).ResolveWithFileSystem().ToArray();

            var args = new List<string>
            {
                assemblyArg,
                outputDirArg,
                processorPathsArg,
                assemblyFoldersArg,
                referencesArg,
                definesArg
            }.ToArray();

            Backend.Current.AddAction(
                
                actionName:$"ILPostProcessorRunner",
                targetFiles: targetFiles,
                inputs: inputFiles,
                executableStringFor: _ILPostProcessorRunnableProgram.InvocationString,
                commandLineArguments: args,
                allowedOutputSubstrings: new[] { "ILPostProcessorRunner", "[WARN]", "[ERROR]" },
                supportResponseFile: true
            );
        }
    }

    private static IEnumerable<NPath> TargetPathsFor(NPath targetDirectory, DotNetAssembly inputAssembly)
    {
        yield return targetDirectory.Combine(inputAssembly.Path.FileName);
        if (inputAssembly.DebugSymbolPath != null)
            yield return targetDirectory.Combine(inputAssembly.DebugSymbolPath.FileName);
    }

    private static IEnumerable<NPath> InputPathsFor(DotNetAssembly inputAssembly)
    {
        yield return inputAssembly.Path;
        if (inputAssembly.DebugSymbolPath != null)
            yield return inputAssembly.DebugSymbolPath;
    }
}
