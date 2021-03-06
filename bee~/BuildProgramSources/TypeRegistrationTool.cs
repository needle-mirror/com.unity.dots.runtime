using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Bee.Core;
using Bee.DotNet;
using Bee.Tools;
using NiceIO;
using Bee.CSharpSupport;

static class TypeRegistrationTool
{
    private static CSharpProgram _entityBuildUtils 
    {
        get {
            var program = new CSharpProgram() {
                FileName = "Unity.Entities.BuildUtils.dll",
                Sources =
                {
                AsmDefConfigFile.AsmDefDescriptionFor("Unity.Entities.BuildUtils").Path.Parent
                    .Files("*.cs", recurse: true)
            },
                Framework = { Framework.Framework471 },
                Unsafe = true,
                References =
                {
                MonoCecil.Paths,
                Il2Cpp.Distribution.Path.Combine("build/deploy/net471/Unity.Cecil.Awesome.dll"),
            },
                LanguageVersion = "7.3",
                ProjectFilePath = "Unity.Entities.BuildUtilities.csproj"
            };
            program.SetupDefault();
            return program;
        }
    }

    public static CSharpProgram EntityBuildUtils => _entityBuildUtils;

    private static DotNetRunnableProgram _typeRegRunnableProgram 
    {
        get {

            var typeRegGen = TypeRegProgram;
            return new DotNetRunnableProgram(typeRegGen.SetupDefault());
        }
    }

    public static CSharpProgram TypeRegProgram => _typeRegProgram;

    private static CSharpProgram _typeRegProgram =>  new CSharpProgram()
    {
        FileName = "TypeRegGen.exe",
        Sources = {BuildProgram.BeeRoot.Parent.Combine("TypeRegGen")},
        Unsafe = true,
        Defines = {"NDESK_OPTIONS"},
        Framework = { Framework.Framework471 },
        References =
        {
            EntityBuildUtils,
            MonoCecil.Paths,
            StevedoreNewtonsoftJson.Paths,
            Il2Cpp.Distribution.Path.Combine("build/deploy/net471/Unity.Cecil.Awesome.dll"),
        },
        LanguageVersion = "7.3",
        ProjectFilePath = "TypeRegGen.csproj"
    };

    public static DotNetAssembly SetupInvocation(
        DotNetAssembly inputAssembly,
        DotsRuntimeCSharpProgramConfiguration dotsConfig)
    {
        return inputAssembly.ApplyDotNetAssembliesPostProcessor($"artifacts/{inputAssembly.Path.FileNameWithoutExtension}/{dotsConfig.Identifier}/post_typereg/",
            (inputAssemblies, targetDirectory) => AddActions(dotsConfig, inputAssemblies, targetDirectory));
    }

    private static void AddActions(DotsRuntimeCSharpProgramConfiguration dotsConfig, DotNetAssembly[] inputAssemblies, NPath targetDirectory)
    {
        var args = new List<string>
        {
            targetDirectory.MakeAbsolute().QuoteForProcessStart(),
            dotsConfig.NativeProgramConfiguration.ToolChain.Architecture.Bits.ToString(),
            dotsConfig.ScriptingBackend == ScriptingBackend.Dotnet ? "DOTSDotNet" : "DOTSNative",
            dotsConfig.UseBurst ? "Bursted" : "Unbursted",
            dotsConfig.Identifier.Contains("release") ? "release" : "debug", // We check for 'release' so we can generate 'debug' info for both debug and develop configs
            dotsConfig.MultiThreadedJobs ? "Multithreaded" : "Singlethreaded",
            inputAssemblies.OrderByDependencies().Select(p => p.Path.MakeAbsolute().QuoteForProcessStart())
        }.ToArray();

        var inputFiles = inputAssemblies.SelectMany(InputPathsFor)
            .Concat(new[] {_typeRegRunnableProgram.Path}).ToArray();
        var targetFiles = inputAssemblies.SelectMany(i => TargetPathsFor(targetDirectory, i)).ToArray();

        Backend.Current.AddAction("TypeRegGen",
            targetFiles,
            inputFiles,
            _typeRegRunnableProgram.InvocationString,
            args,
            allowedOutputSubstrings: new[] {"Static Type Registry Generation Time:"});
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
